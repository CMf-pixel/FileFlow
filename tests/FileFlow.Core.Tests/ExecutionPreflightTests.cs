using FileFlow.Core.Execution;
using FileFlow.Core.Preview;
using FileFlow.Core.Rules;
using Xunit;

namespace FileFlow.Core.Tests;

public sealed class ExecutionPreflightTests
{
    [Theory]
    [InlineData(PreviewStatus.Blocked)]
    [InlineData(PreviewStatus.Incomplete)]
    [InlineData(PreviewStatus.NoMatches)]
    public void Ineligible_preview_is_rejected_without_any_filesystem_access(PreviewStatus status)
    {
        var fs = new ExecutionFileSystemFake();
        var ready = fs.Preview("a.png");
        var preview = new OperationPreview(ready.Rule,
            status == PreviewStatus.NoMatches ? Array.Empty<PlannedOperation>() : ready.Operations,
            status == PreviewStatus.Blocked ? new[] { new PreviewIssue(PreviewIssueCode.DestinationExists, "conflict") }
                : Array.Empty<PreviewIssue>(), status == PreviewStatus.Incomplete);
        Assert.Equal(status, preview.Status);
        var result = new FileOperationExecutor(fs).Execute(preview);
        Assert.True(result.PreflightRejected);
        Assert.Equal(ExecutionOutcome.Rejected, result.Outcome);
        Assert.Equal(preview.Operations.Count, result.NotAttemptedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(ExecutionErrorCode.InvalidPreview, Assert.Single(result.Errors).Code);
        Assert.Empty(fs.Calls);
    }

    [Fact]
    public void Rejected_preview_stays_consumed_after_repair_and_across_instances_but_fresh_preview_can_execute()
    {
        var fs = new ExecutionFileSystemFake();
        var preview = fs.Preview("a.png");
        fs.AddFile(fs.Destination("a.png"));
        var executor = new FileOperationExecutor(fs);
        Assert.True(executor.Execute(preview).PreflightRejected);
        fs.Attributes.Remove(fs.Destination("a.png"));
        fs.Metadata.Remove(fs.Destination("a.png"));
        fs.Calls.Clear();
        Assert.Equal(ExecutionErrorCode.PreviewAlreadyAttempted, Assert.Single(executor.Execute(preview).Errors).Code);
        Assert.Equal(ExecutionErrorCode.PreviewAlreadyAttempted,
            Assert.Single(new FileOperationExecutor(fs).Execute(preview).Errors).Code);
        Assert.Empty(fs.Calls);
        Assert.Equal(ExecutionOutcome.Succeeded, executor.Execute(fs.Preview()).Outcome);
    }

    [Theory]
    [InlineData(FileAction.Copy, "length")]
    [InlineData(FileAction.Move, "length")]
    [InlineData(FileAction.Copy, "timestamp")]
    [InlineData(FileAction.Move, "timestamp")]
    [InlineData(FileAction.Copy, "missing")]
    [InlineData(FileAction.Move, "missing")]
    [InlineData(FileAction.Copy, "destination-file")]
    [InlineData(FileAction.Move, "destination-file")]
    [InlineData(FileAction.Copy, "destination-directory")]
    [InlineData(FileAction.Move, "destination-directory")]
    public void Real_stale_later_operation_leaves_entire_directory_inventory_and_bytes_unchanged(FileAction action, string change)
    {
        using var directory = new ExecutionDirectory();
        directory.Write("a.png", "valid first file");
        var later = directory.Write("b.jpg", "later file");
        var preview = new FileRulePreviewer().CreatePreview(directory.Rule with { Action = action });
        switch (change)
        {
            case "length": File.AppendAllText(later, "changed"); break;
            case "timestamp": File.SetLastWriteTimeUtc(later, File.GetLastWriteTimeUtc(later).AddMinutes(1)); break;
            case "missing": File.Delete(later); break;
            case "destination-file": File.WriteAllText(Path.Combine(directory.Destination, "b.jpg"), "preserve"); break;
            case "destination-directory": Directory.CreateDirectory(Path.Combine(directory.Destination, "b.jpg")); break;
        }
        var before = directory.Snapshot();
        var fs = new ObservedExecutionFileSystem();
        var result = new FileOperationExecutor(fs).Execute(preview);
        Assert.Equal(ExecutionOutcome.Rejected, result.Outcome);
        Assert.Equal(0, result.SucceededCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(2, result.NotAttemptedCount);
        Assert.Equal(1, Assert.Single(result.Errors).OperationIndex);
        Assert.Equal(0, fs.MutationCount);
        Assert.Equal(before, directory.Snapshot());
    }

    [Fact]
    public void Ready_batch_checks_all_approved_files_without_enumerating_or_mutating()
    {
        var fs = new ExecutionFileSystemFake();
        var preview = fs.Preview("b.jpg", "a.png");
        fs.AddFile(fs.Source("unapproved.png"));
        Assert.Empty(new ExecutionPreflight(fs).ValidateBatch(preview));
        Assert.Contains(("metadata", fs.Source("a.png")), fs.Calls);
        Assert.Contains(("metadata", fs.Source("b.jpg")), fs.Calls);
        Assert.DoesNotContain(fs.Calls, call => call.Method is "enumerate" or "mutate" || call.Path.EndsWith("unapproved.png"));
    }

    [Theory]
    [InlineData("length", ExecutionErrorCode.SourceChanged)]
    [InlineData("timestamp", ExecutionErrorCode.SourceChanged)]
    [InlineData("missing", ExecutionErrorCode.SourceMissing)]
    [InlineData("directory", ExecutionErrorCode.UnsupportedPath)]
    [InlineData("device", ExecutionErrorCode.UnsupportedPath)]
    [InlineData("reparse", ExecutionErrorCode.UnsupportedPath)]
    [InlineData("destination-file", ExecutionErrorCode.DestinationExists)]
    [InlineData("destination-directory", ExecutionErrorCode.DestinationExists)]
    [InlineData("destination-reparse", ExecutionErrorCode.DestinationExists)]
    [InlineData("missing-parent", ExecutionErrorCode.InspectionFailed)]
    public void Stale_operation_is_reported_with_index_and_path(string change, ExecutionErrorCode code)
    {
        var fs = new ExecutionFileSystemFake();
        var preview = fs.Preview("a.png", "b.jpg");
        var source = fs.Source("b.jpg");
        switch (change)
        {
            case "length": fs.Metadata[source] = fs.Metadata[source] with { Length = 99 }; break;
            case "timestamp": fs.Metadata[source] = fs.Metadata[source] with { LastWriteUtc = fs.Metadata[source].LastWriteUtc.AddSeconds(1) }; break;
            case "missing": fs.Attributes.Remove(source); break;
            case "directory": fs.Attributes[source] = FileAttributes.Directory; break;
            case "device": fs.Attributes[source] = FileAttributes.Device; break;
            case "reparse": fs.Attributes[source] = FileAttributes.ReparsePoint; break;
            case "destination-file": fs.AddFile(fs.Destination("b.jpg")); break;
            case "destination-directory": fs.Attributes[fs.Destination("b.jpg")] = FileAttributes.Directory; break;
            case "destination-reparse": fs.Attributes[fs.Destination("b.jpg")] = FileAttributes.ReparsePoint; break;
            case "missing-parent": fs.Attributes.Remove(fs.Rule.DestinationDirectory); break;
        }
        var errors = new ExecutionPreflight(fs).ValidateBatch(preview);
        Assert.Contains(errors, error => error.Code == code && error.OperationIndex == 1 && !string.IsNullOrWhiteSpace(error.Path));
        Assert.Empty(fs.Mutations);
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:\execution-source")]
    [InlineData(@"C:\execution-destination")]
    public void Reparse_ancestors_are_rejected_before_reading_descendants(string path)
    {
        var fs = new ExecutionFileSystemFake();
        var preview = fs.Preview("a.png");
        fs.Attributes[path] |= FileAttributes.ReparsePoint;
        Assert.Contains(new ExecutionPreflight(fs).ValidateBatch(preview), error => error.Code == ExecutionErrorCode.UnsupportedPath);
        Assert.DoesNotContain(fs.Calls, call => call.Method != "drive" && call.Path != path &&
            call.Path.StartsWith(path.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(fs.Mutations);
    }

    [Theory]
    [InlineData(DriveType.Network)]
    [InlineData(DriveType.Unknown)]
    [InlineData(DriveType.NoRootDirectory)]
    public void Unsupported_or_disconnected_drive_is_rejected(DriveType type)
    {
        var fs = new ExecutionFileSystemFake();
        var preview = fs.Preview("a.png");
        fs.Drives[@"C:\"] = type;
        Assert.Contains(new ExecutionPreflight(fs).ValidateBatch(preview), error => error.Code == ExecutionErrorCode.UnsupportedPath);
        Assert.DoesNotContain(fs.Calls, call => call.Method is "attributes" or "metadata");
    }

    [Theory]
    [InlineData("source")]
    [InlineData("destination")]
    [InlineData("action")]
    [InlineData("relative")]
    [InlineData("unc")]
    [InlineData("device")]
    [InlineData("traversal")]
    [InlineData("stream")]
    [InlineData("reserved")]
    [InlineData("same-path")]
    [InlineData("duplicate")]
    public void Malformed_operations_cannot_redirect_the_approved_batch(string change)
    {
        var fs = new ExecutionFileSystemFake();
        var original = fs.Preview("a.png");
        var operation = original.Operations[0];
        operation = change switch
        {
            "source" => operation with { SourcePath = @"C:\elsewhere\a.png" },
            "destination" => operation with { DestinationPath = fs.Destination("renamed.png") },
            "action" => operation with { Action = (FileAction)42 },
            "relative" => operation with { SourcePath = "a.png" },
            "unc" => operation with { SourcePath = @"\\server\share\a.png" },
            "device" => operation with { SourcePath = @"\\?\C:\execution-source\a.png" },
            "traversal" => operation with { SourcePath = @"C:\execution-source\nested\..\a.png" },
            "stream" => operation with { SourcePath = fs.Source("a.png:stream"), DestinationPath = fs.Destination("a.png:stream") },
            "reserved" => operation with { SourcePath = fs.Source("CON.png"), DestinationPath = fs.Destination("CON.png") },
            "same-path" => operation with { DestinationPath = operation.SourcePath },
            _ => operation
        };
        var preview = new OperationPreview(original.Rule, change == "duplicate" ? new[] { operation, operation } : new[] { operation },
            Array.Empty<PreviewIssue>(), false);
        Assert.Contains(new ExecutionPreflight(fs).ValidateBatch(preview), error => error.Code == ExecutionErrorCode.InvalidOperation);
        Assert.Empty(fs.Mutations);
    }

    [Fact]
    public void Batch_collects_multiple_failures_in_preview_order()
    {
        var fs = new ExecutionFileSystemFake();
        var preview = fs.Preview("b.jpg", "a.png");
        fs.Attributes.Remove(fs.Source("a.png"));
        fs.AddFile(fs.Destination("b.jpg"));
        var errors = new ExecutionPreflight(fs).ValidateBatch(preview);
        Assert.Equal(new int?[] { 0, 1 }, errors.Select(error => error.OperationIndex));
        Assert.Equal(new[] { ExecutionErrorCode.SourceMissing, ExecutionErrorCode.DestinationExists }, errors.Select(error => error.Code));
    }
}
