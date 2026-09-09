using System.Security;
using FileFlow.Core.Execution;
using FileFlow.Core.Rules;
using Xunit;

namespace FileFlow.Core.Tests;

public sealed class ExecutionFailureTests
{
    [Theory]
    [InlineData(FileAction.Copy, false)]
    [InlineData(FileAction.Move, false)]
    [InlineData(FileAction.Copy, true)]
    [InlineData(FileAction.Move, true)]
    public void Destination_appearing_after_preflight_fails_at_recheck_or_nonoverwriting_mutation(FileAction action, bool afterRecheck)
    {
        var fs = new ExecutionFileSystemFake();
        fs.Rule = fs.Rule with { Action = action };
        var preview = fs.Preview("a.png", "b.jpg");
        if (afterRecheck)
            fs.BeforeMutation = (_, _, destination) => fs.AddFile(destination);
        else
            fs.BeforeRead = (method, path) =>
            {
                if (method == "attributes" && path == fs.Destination("a.png") &&
                    fs.Calls.Count(call => call == (method, path)) == 2)
                    fs.AddFile(path);
            };
        var result = new FileOperationExecutor(fs).Execute(preview);
        Assert.Equal(ExecutionOutcome.Failed, result.Outcome);
        Assert.False(result.PreflightRejected);
        Assert.Equal(afterRecheck ? ExecutionErrorCode.MutationFailed : ExecutionErrorCode.DestinationExists,
            Assert.Single(result.Errors).Code);
        Assert.Equal(afterRecheck ? 1 : 0, fs.Mutations.Count);
        Assert.True(fs.Attributes.ContainsKey(fs.Source("a.png")));
        Assert.Equal(OperationStatus.NotAttempted, result.Operations[1].Status);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("length")]
    [InlineData("timestamp")]
    [InlineData("source-reparse")]
    [InlineData("source-directory")]
    [InlineData("source-ancestor")]
    [InlineData("destination-ancestor")]
    [InlineData("network")]
    [InlineData("destination-file")]
    [InlineData("destination-directory")]
    public void Immediate_recheck_catches_changes_after_previous_success(string change)
    {
        var fs = new ExecutionFileSystemFake();
        var preview = fs.Preview("a.png", "b.jpg", "c.png");
        var firstVerified = false;
        fs.BeforeRead = (method, path) =>
        {
            if (method == "attributes" && path == fs.Source("a.png") && fs.Mutations.Count == 1)
                firstVerified = true;
            if (!firstVerified || method != "drive") return;
            fs.BeforeRead = null;
            var source = fs.Source("b.jpg");
            switch (change)
            {
                case "missing": fs.Attributes.Remove(source); break;
                case "length": fs.Metadata[source] = fs.Metadata[source] with { Length = 0 }; break;
                case "timestamp": fs.Metadata[source] = fs.Metadata[source] with { LastWriteUtc = fs.Metadata[source].LastWriteUtc.AddSeconds(1) }; break;
                case "source-reparse": fs.Attributes[source] = FileAttributes.ReparsePoint; break;
                case "source-directory": fs.Attributes[source] = FileAttributes.Directory; break;
                case "source-ancestor": fs.Attributes[fs.Rule.SourceDirectory] |= FileAttributes.ReparsePoint; break;
                case "destination-ancestor": fs.Attributes[fs.Rule.DestinationDirectory] |= FileAttributes.ReparsePoint; break;
                case "network": fs.Drives[@"C:\"] = DriveType.Network; break;
                case "destination-file": fs.AddFile(fs.Destination("b.jpg")); break;
                case "destination-directory": fs.Attributes[fs.Destination("b.jpg")] = FileAttributes.Directory; break;
            }
        };
        var result = new FileOperationExecutor(fs).Execute(preview);
        Assert.Equal(ExecutionOutcome.PartiallySucceeded, result.Outcome);
        Assert.Equal(new[] { OperationStatus.Succeeded, OperationStatus.Failed, OperationStatus.NotAttempted },
            result.Operations.Select(item => item.Status));
        Assert.Single(fs.Mutations);
        Assert.Equal(1, Assert.Single(result.Errors).OperationIndex);
        Assert.True(fs.Attributes.ContainsKey(fs.Destination("a.png")));
    }

    [Theory]
    [MemberData(nameof(FileSystemErrors))]
    public void Expected_inspection_failures_reject_the_whole_batch_with_zero_mutation_calls(Exception failure)
    {
        var fs = new ExecutionFileSystemFake();
        var preview = fs.Preview("a.png", "b.jpg");
        fs.BeforeRead = (method, path) => { if (method == "metadata" && path == fs.Source("b.jpg")) throw failure; };
        var result = new FileOperationExecutor(fs).Execute(preview);
        Assert.True(result.PreflightRejected);
        Assert.Equal(2, result.NotAttemptedCount);
        Assert.Equal(0, result.SucceededCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(fs.Source("b.jpg"), Assert.Single(result.Errors).Path);
        Assert.Empty(fs.Mutations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unexpected_programming_errors_propagate_and_preview_remains_consumed(bool duringMutation)
    {
        var fs = new ExecutionFileSystemFake();
        var preview = fs.Preview("a.png");
        if (duringMutation) fs.BeforeMutation = (_, _, _) => throw new InvalidOperationException("bug");
        else fs.BeforeRead = (_, _) => throw new InvalidOperationException("bug");
        Assert.Throws<InvalidOperationException>(() => new FileOperationExecutor(fs).Execute(preview));
        fs.Calls.Clear();
        Assert.Equal(ExecutionErrorCode.PreviewAlreadyAttempted,
            Assert.Single(new FileOperationExecutor(fs).Execute(preview).Errors).Code);
        Assert.Empty(fs.Calls);
    }

    public static IEnumerable<object[]> FileSystemErrors()
    {
        yield return new object[] { new IOException("disk full or locked file") };
        yield return new object[] { new UnauthorizedAccessException("access denied") };
        yield return new object[] { new FileNotFoundException("source disappeared") };
        yield return new object[] { new DirectoryNotFoundException("drive disconnected") };
        yield return new object[] { new SecurityException("security denied") };
        yield return new object[] { new ArgumentException("invalid path") };
        yield return new object[] { new NotSupportedException("unsupported path") };
    }

    [Theory]
    [MemberData(nameof(FileSystemErrors))]
    public void Expected_mutation_failures_return_diagnostics_and_do_not_attempt_later_entries(Exception failure)
    {
        var fs = new ExecutionFileSystemFake();
        var preview = fs.Preview("a.png", "b.jpg");
        fs.BeforeMutation = (_, _, _) => throw failure;
        var result = new FileOperationExecutor(fs).Execute(preview);
        Assert.False(result.PreflightRejected);
        Assert.Equal(ExecutionOutcome.Failed, result.Outcome);
        Assert.Equal(new[] { OperationStatus.Failed, OperationStatus.NotAttempted }, result.Operations.Select(item => item.Status));
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(1, result.NotAttemptedCount);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ExecutionErrorCode.MutationFailed, error.Code);
        Assert.Equal(0, error.OperationIndex);
        Assert.Contains(failure.Message, error.Message);
        Assert.Contains("may", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Same(error, result.Operations[0].Error);
        Assert.Single(fs.Mutations);
        Assert.Equal(("mutate", fs.Source("a.png")), fs.Calls[^1]);
    }

    [Theory]
    [InlineData(FileAction.Copy)]
    [InlineData(FileAction.Move)]
    public void Failure_after_success_preserves_success_and_stops_without_retry(FileAction action)
    {
        var fs = new ExecutionFileSystemFake();
        fs.Rule = fs.Rule with { Action = action };
        var preview = fs.Preview("a.png", "b.jpg", "c.png");
        fs.BeforeMutation = (_, source, _) => { if (source == fs.Source("b.jpg")) throw new IOException("disk full"); };
        var executor = new FileOperationExecutor(fs);
        var result = executor.Execute(preview);
        Assert.Equal(ExecutionOutcome.PartiallySucceeded, result.Outcome);
        Assert.Equal(new[] { OperationStatus.Succeeded, OperationStatus.Failed, OperationStatus.NotAttempted },
            result.Operations.Select(item => item.Status));
        Assert.Equal((1, 1, 1), (result.SucceededCount, result.FailedCount, result.NotAttemptedCount));
        Assert.Equal(preview.Operations, result.Operations.Select(item => item.Operation));
        Assert.True(fs.Attributes.ContainsKey(fs.Destination("a.png")));
        Assert.Equal(action == FileAction.Copy, fs.Attributes.ContainsKey(fs.Source("a.png")));
        Assert.Equal(2, fs.Mutations.Count);
        Assert.Equal(("mutate", fs.Source("b.jpg")), fs.Calls[^1]);
        fs.Calls.Clear();
        Assert.Equal(ExecutionErrorCode.PreviewAlreadyAttempted, Assert.Single(executor.Execute(preview).Errors).Code);
        Assert.Empty(fs.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cross_volume_move_reports_success_only_when_source_is_absent(bool leaveSource)
    {
        var fs = new ExecutionFileSystemFake { LeaveMoveSource = leaveSource };
        fs.Rule = fs.Rule with { Action = FileAction.Move, DestinationDirectory = @"D:\execution-destination" };
        var result = new FileOperationExecutor(fs).Execute(fs.Preview("a.png", "b.jpg"));
        Assert.Equal(leaveSource ? ExecutionOutcome.Failed : ExecutionOutcome.Succeeded, result.Outcome);
        Assert.True(fs.Attributes.ContainsKey(fs.Destination("a.png")));
        Assert.Equal(leaveSource, fs.Attributes.ContainsKey(fs.Source("a.png")));
        Assert.Contains(("attributes", fs.Source("a.png")), fs.Calls.SkipWhile(call => call.Method != "mutate"));
        if (leaveSource)
        {
            Assert.Equal(ExecutionErrorCode.MoveIncomplete, Assert.Single(result.Errors).Code);
            Assert.Equal(OperationStatus.NotAttempted, result.Operations[1].Status);
            Assert.Single(fs.Mutations);
        }
    }

    [Theory]
    [InlineData(FileAction.Copy, "destination-missing")]
    [InlineData(FileAction.Move, "destination-missing")]
    [InlineData(FileAction.Copy, "destination-length")]
    [InlineData(FileAction.Move, "destination-length")]
    [InlineData(FileAction.Copy, "destination-reparse")]
    [InlineData(FileAction.Move, "destination-reparse")]
    [InlineData(FileAction.Copy, "source-missing")]
    [InlineData(FileAction.Copy, "source-changed")]
    [InlineData(FileAction.Move, "source-access")]
    [InlineData(FileAction.Move, "source-parent-missing")]
    public void Unverified_final_state_is_failure_and_later_operations_are_not_attempted(FileAction action, string change)
    {
        var fs = new ExecutionFileSystemFake();
        fs.Rule = fs.Rule with { Action = action };
        var preview = fs.Preview("a.png", "b.jpg");
        fs.AfterMutation = (_, source, destination) =>
        {
            switch (change)
            {
                case "destination-missing": fs.Attributes.Remove(destination); break;
                case "destination-length": fs.Metadata[destination] = fs.Metadata[destination] with { Length = 0 }; break;
                case "destination-reparse": fs.Attributes[destination] = FileAttributes.ReparsePoint; break;
                case "source-missing": fs.Attributes.Remove(source); break;
                case "source-changed": fs.Metadata[source] = fs.Metadata[source] with { Length = 0 }; break;
                case "source-access": fs.BeforeRead = (method, path) =>
                    { if (method == "attributes" && path == source) throw new UnauthorizedAccessException("cannot confirm absence"); }; break;
                case "source-parent-missing": fs.Attributes.Remove(fs.Rule.SourceDirectory); break;
            }
        };
        var result = new FileOperationExecutor(fs).Execute(preview);
        Assert.Equal(ExecutionOutcome.Failed, result.Outcome);
        Assert.Equal(ExecutionErrorCode.VerificationFailed, Assert.Single(result.Errors).Code);
        Assert.Equal(OperationStatus.NotAttempted, result.Operations[1].Status);
        Assert.Single(fs.Mutations);
    }
}
