using FileFlow.App.Infrastructure;
using FileFlow.App.Services;
using FileFlow.Core.Execution;
using FileFlow.Core.Preview;
using FileFlow.Core.Rules;
using Xunit;

namespace FileFlow.Core.Tests.Presentation;

public sealed class InfrastructureTests
{
    [Fact]
    public void Mutex_prevents_another_thread_owning_the_same_instance_and_releases_on_dispose()
    {
        var name = "Local\\FileFlow.Tests." + Guid.NewGuid().ToString("N");
        var first = SingleInstanceGuard.TryAcquire(name);
        Assert.NotNull(first);
        bool? acquired = null;
        var contender = new Thread(() => { using var second = SingleInstanceGuard.TryAcquire(name); acquired = second is not null; });
        contender.Start();
        Assert.True(contender.Join(TimeSpan.FromSeconds(5)));
        Assert.False(acquired);
        first.Dispose();
        using var next = SingleInstanceGuard.TryAcquire(name);
        Assert.NotNull(next);
    }

    [Fact]
    public void Independent_mutex_names_do_not_block_each_other()
    {
        using var userOne = SingleInstanceGuard.TryAcquire("Local\\FileFlow.Tests." + Guid.NewGuid().ToString("N"));
        using var userTwo = SingleInstanceGuard.TryAcquire("Local\\FileFlow.Tests." + Guid.NewGuid().ToString("N"));
        Assert.NotNull(userOne);
        Assert.NotNull(userTwo);
    }

    [Fact]
    public void Abandoned_mutex_is_recovered()
    {
        var name = "Local\\FileFlow.Tests." + Guid.NewGuid().ToString("N");
        Mutex? abandoned = null;
        var thread = new Thread(() => { abandoned = new Mutex(false, name); abandoned.WaitOne(); });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        try { using var recovered = SingleInstanceGuard.TryAcquire(name); Assert.NotNull(recovered); }
        finally { abandoned?.Dispose(); }
    }

    [Fact]
    public async Task Async_command_ignores_reentry_and_restores_availability_after_failure()
    {
        var pending = new TaskCompletionSource();
        var calls = 0;
        var command = new AsyncRelayCommand(_ => { calls++; return pending.Task; });
        var first = command.ExecuteAsync();
        Assert.False(command.CanExecute(null));
        await command.ExecuteAsync();
        Assert.Equal(1, calls);
        pending.SetException(new InvalidOperationException("failure"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => first);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task Production_adapter_runs_preview_and_execution_off_the_calling_thread()
    {
        var completed = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var caller = new Thread(() =>
        {
            try
            {
                var callingThread = Environment.CurrentManagedThreadId;
                var fs = new RecordingFileSystem();
                var adapter = new CoreFileWorkflowService(new FileRulePreviewer(fs), new FileOperationExecutor(fs));
                // This dedicated thread intentionally stays occupied, making same-thread execution detectable.
#pragma warning disable xUnit1031
                var preview = adapter.CreatePreviewAsync(WorkflowTests.Rule()).GetAwaiter().GetResult();
                Assert.True(preview.CanExecute);
                Assert.NotEmpty(fs.Threads);
                Assert.DoesNotContain(callingThread, fs.Threads);
                fs.Threads.Clear();
                var result = adapter.ExecuteAsync(preview).GetAwaiter().GetResult();
#pragma warning restore xUnit1031
                Assert.Equal(ExecutionOutcome.Succeeded, result.Outcome);
                Assert.NotEmpty(fs.Threads);
                Assert.DoesNotContain(callingThread, fs.Threads);
                completed.SetResult(null);
            }
            catch (Exception error) { completed.SetResult(error); }
        });
        caller.Start();
        var failure = await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(caller.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(failure);
    }

    private sealed class RecordingFileSystem : IPreviewFileSystem, IExecutionFileSystem
    {
        public readonly List<int> Threads = new();
        private bool copied;
        private void Record() => Threads.Add(Environment.CurrentManagedThreadId);
        public DriveType GetDriveType(string path) { Record(); return DriveType.Fixed; }
        public FileAttributes GetAttributes(string path)
        {
            Record();
            if (path == @"C:\destination\a.png" && !copied) throw new FileNotFoundException();
            return path.EndsWith(".png", StringComparison.Ordinal) ? FileAttributes.Normal : FileAttributes.Directory;
        }
        public IEnumerable<string> EnumerateEntries(string path) { Record(); return new[] { @"C:\source\a.png" }; }
        public SourceFileMetadata ReadSourceMetadata(string path) { Record(); return new(1, DateTime.UnixEpoch); }
        public void CopyNoOverwrite(string source, string destination) { Record(); copied = true; }
        public void MoveNoOverwrite(string source, string destination) => throw new NotSupportedException();
    }
}
