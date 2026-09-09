using System.Security;
using FileFlow.Core.Rules;

namespace FileFlow.Core.Preview;

/// <summary>Synchronously inspects a rule without modifying the filesystem or following reparse entries.</summary>
public sealed class FileRulePreviewer
{
    private readonly IPreviewFileSystem fileSystem;

    public FileRulePreviewer() : this(new PreviewFileSystem()) { }

    internal FileRulePreviewer(IPreviewFileSystem fileSystem) => this.fileSystem = fileSystem;

    public OperationPreview CreatePreview(FileRule rule)
    {
        var validation = RuleValidator.Validate(rule);
        if (!validation.IsValid)
            return new(null, Array.Empty<PlannedOperation>(), validation.Errors.Select(error =>
                new PreviewIssue(PreviewIssueCode.InvalidRule, error.Message, PropertyName: error.PropertyName)), false);

        var evaluatedRule = validation.Rule!;
        var operations = new List<PlannedOperation>();
        var issues = new List<PreviewIssue>();
        var incomplete = false;

        // Evaluate both sides: an access failure must outrank a confirmed problem on the other side.
        var sourceValid = InspectDirectory(evaluatedRule.SourceDirectory, issues, ref incomplete);
        var destinationValid = InspectDirectory(evaluatedRule.DestinationDirectory, issues, ref incomplete);
        if (!sourceValid || !destinationValid)
            return new(evaluatedRule, operations, issues, incomplete);

        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // Catch around iteration as well as creation: directory enumeration is lazy and can fail partway.
            foreach (var sourcePath in fileSystem.EnumerateEntries(evaluatedRule.SourceDirectory))
                InspectEntry(evaluatedRule, sourcePath, operations, destinations, issues, ref incomplete);
        }
        catch (Exception exception) when (IsInspectionFailure(exception))
        {
            incomplete = true;
            issues.Add(new(PreviewIssueCode.EnumerationFailed,
                $"Source enumeration could not be completed: {exception.Message}", evaluatedRule.SourceDirectory));
        }

        return new(evaluatedRule, operations, issues, incomplete);
    }

    private bool InspectDirectory(string directory, List<PreviewIssue> issues, ref bool incomplete)
    {
        var inspectedPath = directory;
        try
        {
            var driveType = fileSystem.GetDriveType(directory);
            if (driveType == DriveType.Network)
            {
                issues.Add(new(PreviewIssueCode.UnsupportedNetworkPath, "Network drives are unsupported in v0.1.", directory));
                return false;
            }
            if (driveType == DriveType.Unknown)
                throw new IOException("The drive type could not be established.");

            // Inspect from the root down; never inspect through an already identified junction or link.
            var components = new Stack<string>();
            for (string? path = directory; path is not null; path = Path.GetDirectoryName(path))
                components.Push(path);

            foreach (var path in components)
            {
                inspectedPath = path;
                var attributes = fileSystem.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    issues.Add(new(PreviewIssueCode.UnsupportedReparsePoint, "Reparse points are unsupported in preview paths.", path));
                    return false;
                }
                if ((attributes & FileAttributes.Directory) == 0)
                {
                    issues.Add(new(PreviewIssueCode.NotDirectory, "A directory path component is a file.", path));
                    return false;
                }
            }
            return true;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            issues.Add(new(PreviewIssueCode.DirectoryMissing, "The required directory does not exist.", directory));
        }
        catch (Exception exception) when (IsInspectionFailure(exception))
        {
            AddInspectionFailure(inspectedPath, exception, issues, ref incomplete);
        }
        return false;
    }

    private void InspectEntry(FileRule rule, string sourcePath, List<PlannedOperation> operations,
        HashSet<string> destinations, List<PreviewIssue> issues, ref bool incomplete)
    {
        var inspectedPath = sourcePath;
        try
        {
            var attributes = fileSystem.GetAttributes(sourcePath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                issues.Add(new(PreviewIssueCode.UnsupportedReparsePoint, "A direct source entry is a reparse point.", sourcePath));
                return;
            }
            if ((attributes & FileAttributes.Directory) != 0 || !ExtensionMatcher.Matches(rule, sourcePath))
                return;

            var destinationPath = Path.Combine(rule.DestinationDirectory, Path.GetFileName(sourcePath));
            var metadata = fileSystem.ReadSourceMetadata(sourcePath);
            operations.Add(new(sourcePath, destinationPath, rule.Action, metadata.Length, metadata.LastWriteUtc));

            if (!destinations.Add(destinationPath))
                issues.Add(new(PreviewIssueCode.DuplicateDestination, "Multiple source files have equivalent destination names.", destinationPath));

            inspectedPath = destinationPath;
            try
            {
                fileSystem.GetAttributes(destinationPath);
                issues.Add(new(PreviewIssueCode.DestinationExists, "A file, directory, or reparse entry already occupies the destination name.", destinationPath));
            }
            catch (FileNotFoundException)
            {
                // Only a missing final entry is free. Missing parents and access failures are incomplete.
            }
        }
        catch (Exception exception) when (IsInspectionFailure(exception))
        {
            AddInspectionFailure(inspectedPath, exception, issues, ref incomplete);
        }
    }

    private static void AddInspectionFailure(string path, Exception exception, List<PreviewIssue> issues, ref bool incomplete)
    {
        incomplete = true;
        issues.Add(new(PreviewIssueCode.InspectionFailed, $"Inspection could not be completed: {exception.Message}", path));
    }

    private static bool IsInspectionFailure(Exception exception) => exception is
        IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException;
}
