using FileFlow.Core.Execution;
using FileFlow.Core.Preview;
using FileFlow.Core.Rules;
using Xunit;

namespace FileFlow.Core.Tests;

public sealed class ExecutionTests
{
    [Theory]
    [InlineData(FileAction.Copy, false)]
    [InlineData(FileAction.Move, false)]
    [InlineData(FileAction.Copy, true)]
    [InlineData(FileAction.Move, true)]
    public void Real_destination_appearing_at_mutation_time_is_preserved_and_batch_stops(FileAction action, bool directoryConflict)
    {
        using var directory = new ExecutionDirectory();
        directory.Write("a.png", "approved source bytes");
        directory.Write("b.jpg", "never attempted");
        var preview = new FileRulePreviewer().CreatePreview(directory.Rule with { Action = action });
        var fs = new ObservedExecutionFileSystem();
        string[]? atRace = null;
        fs.BeforeMutation = (_, destination) =>
        {
            if (directoryConflict) Directory.CreateDirectory(destination);
            else File.WriteAllText(destination, "external content must survive");
            atRace = directory.Snapshot();
        };
        var result = new FileOperationExecutor(fs).Execute(preview);
        Assert.Equal(ExecutionOutcome.Failed, result.Outcome);
        Assert.Equal(ExecutionErrorCode.MutationFailed, Assert.Single(result.Errors).Code);
        Assert.Equal(1, fs.MutationCount);
        Assert.Equal(OperationStatus.NotAttempted, result.Operations[1].Status);
        Assert.NotNull(atRace);
        Assert.Equal(atRace, directory.Snapshot());
    }

    [Theory]
    [InlineData(FileAction.Copy)]
    [InlineData(FileAction.Move)]
    public void Real_success_before_injected_failure_is_preserved_without_cleanup(FileAction action)
    {
        using var directory = new ExecutionDirectory();
        directory.Write("a.png", "first success");
        directory.Write("b.jpg", "failing source");
        directory.Write("c.png", "not attempted");
        var preview = new FileRulePreviewer().CreatePreview(directory.Rule with { Action = action });
        var fs = new ObservedExecutionFileSystem();
        string[]? atFailure = null;
        fs.BeforeMutation = (source, destination) =>
        {
            if (Path.GetFileName(source) != "b.jpg") return;
            File.WriteAllText(destination, "partial bytes from failed copy");
            atFailure = directory.Snapshot();
            throw new IOException("disk full");
        };
        var result = new FileOperationExecutor(fs).Execute(preview);
        Assert.Equal(ExecutionOutcome.PartiallySucceeded, result.Outcome);
        Assert.Equal((1, 1, 1), (result.SucceededCount, result.FailedCount, result.NotAttemptedCount));
        Assert.Equal(2, fs.MutationCount);
        Assert.Equal("first success", File.ReadAllText(Path.Combine(directory.Destination, "a.png")));
        Assert.Equal(action == FileAction.Copy, File.Exists(Path.Combine(directory.Source, "a.png")));
        Assert.NotNull(atFailure);
        Assert.Equal(atFailure, directory.Snapshot());
    }

    [Theory]
    [InlineData(FileAction.Copy)]
    [InlineData(FileAction.Move)]
    public void Executor_runs_the_approved_preview_and_verifies_real_file_state(FileAction action)
    {
        using var directory = new ExecutionDirectory();
        var source = directory.Write("a.png", "actual file bytes\0\u03bb");
        var bytes = File.ReadAllBytes(source);
        var preview = new FileRulePreviewer().CreatePreview(directory.Rule with { Action = action });
        directory.Write("unapproved.png", "leave alone");
        var result = new FileOperationExecutor().Execute(preview);
        Assert.Equal(ExecutionOutcome.Succeeded, result.Outcome);
        Assert.False(result.PreflightRejected);
        Assert.Equal(1, result.SucceededCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(0, result.NotAttemptedCount);
        Assert.Empty(result.Errors);
        Assert.Same(preview.Operations[0], Assert.Single(result.Operations).Operation);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(directory.Destination, "a.png")));
        Assert.Equal(action == FileAction.Copy, File.Exists(source));
        Assert.Equal("leave alone", File.ReadAllText(Path.Combine(directory.Source, "unapproved.png")));
        Assert.False(File.Exists(Path.Combine(directory.Destination, "unapproved.png")));
    }

    [Fact]
    public void All_preflight_reads_precede_first_mutation_and_each_mutation_gets_an_immediate_recheck()
    {
        var fs = new ExecutionFileSystemFake();
        var preview = fs.Preview("z.jpg", "B.png", "a.png");
        var previousMutationEnd = 0;
        fs.BeforeMutation = (_, source, destination) =>
        {
            if (fs.Mutations.Count == 1)
                foreach (var operation in preview.Operations)
                    Assert.Contains(("metadata", operation.SourcePath), fs.Calls);
            var recent = fs.Calls.Skip(previousMutationEnd).ToArray();
            Assert.Contains(("metadata", source), recent);
            Assert.Contains(("attributes", destination), recent);
            Assert.Contains(("drive", fs.Rule.SourceDirectory), recent);
            Assert.Contains(("drive", fs.Rule.DestinationDirectory), recent);
            Assert.Equal(("attributes", destination), fs.Calls[^2]);
        };
        fs.AfterMutation = (_, _, _) => previousMutationEnd = fs.Calls.Count;
        var result = new FileOperationExecutor(fs).Execute(preview);
        Assert.Equal(ExecutionOutcome.Succeeded, result.Outcome);
        Assert.Equal(new[] { "a.png", "B.png", "z.jpg" }, fs.Mutations.Select(call => Path.GetFileName(call.Source)));
        Assert.Equal(preview.Operations, result.Operations.Select(item => item.Operation));
        Assert.DoesNotContain(fs.Calls, call => call.Method == "enumerate");
    }

    [Fact]
    public void Concurrent_executor_instances_only_execute_one_attempt()
    {
        var fs = new ExecutionFileSystemFake();
        var preview = fs.Preview("a.png");
        var results = new ExecutionResult[16];
        Parallel.For(0, results.Length, i => results[i] = new FileOperationExecutor(fs).Execute(preview));
        Assert.Equal(1, results.Count(result => result.Outcome == ExecutionOutcome.Succeeded));
        Assert.Equal(15, results.Count(result => result.PreflightRejected &&
            result.Errors.Single().Code == ExecutionErrorCode.PreviewAlreadyAttempted));
        Assert.Single(fs.Mutations);
    }

    [Fact]
    public void Null_preview_is_a_programming_error()
    {
        Assert.Throws<ArgumentNullException>(() => new FileOperationExecutor().Execute(null!));
    }

    [Theory]
    [InlineData(FileAction.Copy)]
    [InlineData(FileAction.Move)]
    public void Adapter_copies_bytes_and_preserves_or_removes_source(FileAction action)
    {
        using var directory = new ExecutionDirectory();
        var source = directory.Write("a.png", "copied content\0\u03bb");
        var bytes = File.ReadAllBytes(source);
        var destination = Path.Combine(directory.Destination, "a.png");
        var fs = new ExecutionFileSystem();
        if (action == FileAction.Copy) fs.CopyNoOverwrite(source, destination);
        else fs.MoveNoOverwrite(source, destination);
        Assert.Equal(bytes, File.ReadAllBytes(destination));
        Assert.Equal(action == FileAction.Copy, File.Exists(source));
    }

    [Theory]
    [InlineData(FileAction.Copy, false)]
    [InlineData(FileAction.Move, false)]
    [InlineData(FileAction.Copy, true)]
    [InlineData(FileAction.Move, true)]
    public void Adapter_never_overwrites_files_or_directories(FileAction action, bool isDirectory)
    {
        using var directory = new ExecutionDirectory();
        var source = directory.Write("a.png", "source");
        var destination = Path.Combine(directory.Destination, "a.png");
        if (isDirectory) Directory.CreateDirectory(destination);
        else File.WriteAllText(destination, "keep this destination");
        var before = directory.Snapshot();
        var fs = new ExecutionFileSystem();
        var exception = Record.Exception(() =>
        {
            if (action == FileAction.Copy) fs.CopyNoOverwrite(source, destination);
            else fs.MoveNoOverwrite(source, destination);
        });
        Assert.True(exception is IOException or UnauthorizedAccessException, exception?.ToString());
        Assert.Equal(before, directory.Snapshot());
    }

    [Fact]
    public void Execution_boundary_has_no_enumeration_delete_streams_or_overwrite_switch()
    {
        var methods = typeof(IExecutionFileSystem).GetMethods();
        Assert.Equal(new[] { "CopyNoOverwrite", "GetAttributes", "GetDriveType", "MoveNoOverwrite", "ReadSourceMetadata" },
            methods.Select(method => method.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.All(methods, method => Assert.DoesNotContain(method.GetParameters(), p =>
            p.ParameterType == typeof(bool) || typeof(Stream).IsAssignableFrom(p.ParameterType)));
        Assert.All(methods, method => Assert.False(typeof(Stream).IsAssignableFrom(method.ReturnType)));
    }
}
