using FileFlow.App.Services;
using FileFlow.App.ViewModels;
using FileFlow.Core.Execution;
using FileFlow.Core.Persistence;
using FileFlow.Core.Preview;
using FileFlow.Core.Rules;
using Xunit;

namespace FileFlow.Core.Tests.Presentation;

public sealed class WorkflowIntegrationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FileFlow-UI-tests-" + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(root, "source");
    private string Destination => Path.Combine(root, "destination");
    private string Storage => Path.Combine(root, "storage");

    public WorkflowIntegrationTests()
    {
        Directory.CreateDirectory(Source);
        Directory.CreateDirectory(Destination);
    }

    [Theory]
    [InlineData(FileAction.Copy)]
    [InlineData(FileAction.Move)]
    public async Task Create_restart_preview_and_execute_uses_real_core_without_mutating_before_confirmation(FileAction action)
    {
        var sourceFile = Path.Combine(Source, "photo.PNG");
        File.WriteAllBytes(sourceFile, new byte[] { 0, 1, 2, 42, 255 });
        var dialogs = new WorkflowTests.Dialogs();
        var app = NewMain(dialogs);
        app.Initialize();
        Assert.True(app.IsEmpty);
        dialogs.Edit = editor =>
        {
            editor.Name = "Photos"; editor.SourceDirectory = Source; editor.DestinationDirectory = Destination;
            editor.ExtensionsText = "png, .PNG"; editor.Action = action; editor.Save();
            Assert.True(editor.IsSaved);
        };
        app.CreateRule();
        var restart = NewMain(dialogs);
        restart.Initialize();
        Assert.Equal(action, Assert.Single(restart.Rules).Rule.Action);
        await restart.RunAsync(restart.Rules[0]);
        var run = dialogs.LastRun!;
        Assert.True(run.CanExecute);
        Assert.True(File.Exists(sourceFile));
        Assert.Empty(Directory.GetFiles(Destination));
        await run.ExecuteAsync();
        Assert.Equal(ExecutionOutcome.Succeeded, run.Results!.Result.Outcome);
        Assert.Equal(new byte[] { 0, 1, 2, 42, 255 }, File.ReadAllBytes(Path.Combine(Destination, "photo.PNG")));
        Assert.Equal(action == FileAction.Copy, File.Exists(sourceFile));
        Assert.False(run.CanExecute);
        Assert.True(run.CanClose);
    }

    [Fact]
    public async Task Cancelled_preview_leaves_files_unchanged_and_later_run_obtains_a_fresh_preview()
    {
        File.WriteAllText(Path.Combine(Source, "a.png"), "untouched");
        var dialogs = new WorkflowTests.Dialogs();
        var store = new FileRuleStore(Storage);
        Assert.True(store.Save(new[] { WorkflowTests.Rule() with { SourceDirectory = Source, DestinationDirectory = Destination } }).IsSuccess);
        var app = NewMain(dialogs);
        app.Initialize();
        await app.RunAsync(app.Rules[0]);
        var cancelled = dialogs.LastRun!.Preview;
        await app.RunAsync(app.Rules[0]);
        Assert.NotSame(cancelled, dialogs.LastRun!.Preview);
        Assert.Equal("untouched", File.ReadAllText(Path.Combine(Source, "a.png")));
        Assert.Empty(Directory.GetFiles(Destination));
    }

    [Fact]
    public async Task Destination_appearing_after_preview_is_rejected_without_overwrite_and_cannot_retry_old_preview()
    {
        File.WriteAllText(Path.Combine(Source, "a.png"), "source");
        var rule = WorkflowTests.Rule() with { SourceDirectory = Source, DestinationDirectory = Destination };
        var service = new CoreFileWorkflowService(new FileRulePreviewer(), new FileOperationExecutor());
        var run = new RunViewModel(await service.CreatePreviewAsync(rule), service);
        File.WriteAllText(Path.Combine(Destination, "a.png"), "existing");
        await run.ExecuteAsync();
        Assert.Equal(ExecutionOutcome.Rejected, run.Results!.Result.Outcome);
        Assert.Equal(1, run.Results.Result.NotAttemptedCount);
        Assert.False(run.CanExecute);
        Assert.Equal("existing", File.ReadAllText(Path.Combine(Destination, "a.png")));
        Assert.Equal("source", File.ReadAllText(Path.Combine(Source, "a.png")));
    }

    [Fact]
    public void Corrupt_store_is_preserved_by_startup_and_attempted_create()
    {
        Directory.CreateDirectory(Storage);
        var file = Path.Combine(Storage, "rules.json");
        File.WriteAllText(file, "{broken");
        var dialogs = new WorkflowTests.Dialogs();
        var app = NewMain(dialogs);
        app.Initialize();
        app.CreateRule();
        Assert.False(app.IsEmpty);
        Assert.False(app.CanManage);
        Assert.Equal(0, dialogs.EditCalls);
        Assert.Equal("{broken", File.ReadAllText(file));
        Assert.Single(Directory.GetFiles(Storage));
    }

    private MainWindowViewModel NewMain(WorkflowTests.Dialogs dialogs) => new(
        new CoreRuleStore(new FileRuleStore(Storage)),
        new CoreFileWorkflowService(new FileRulePreviewer(), new FileOperationExecutor()), dialogs);

    public void Dispose() => Directory.Delete(root, recursive: true);
}
