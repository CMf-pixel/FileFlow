using System.Security;
using FileFlow.Core.Preview;
using FileFlow.Core.Rules;

namespace FileFlow.Core.Execution;

internal sealed class ExecutionPreflight(IExecutionFileSystem fileSystem)
{
    internal IReadOnlyList<ExecutionError> ValidateBatch(OperationPreview preview)
    {
        var errors = new List<ExecutionError>();
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < preview.Operations.Count; index++)
        {
            var operation = preview.Operations[index];
            if (!destinations.Add(operation.DestinationPath))
                errors.Add(new(ExecutionErrorCode.InvalidOperation, "Duplicate approved destination.", operation.DestinationPath, index));
            if (CheckOperation(operation, preview.Rule, index) is { } error)
                errors.Add(error);
        }
        return errors;
    }

    internal ExecutionError? CheckOperation(PlannedOperation operation, FileRule? rule, int index)
    {
        if (ValidateStructure(operation, rule, index) is { } invalid) return invalid;
        var path = operation.SourcePath;
        try
        {
            if (InspectDirectory(Path.GetDirectoryName(operation.SourcePath)!, index, ref path) is { } sourceDirectoryError)
                return sourceDirectoryError;
            if (InspectDirectory(Path.GetDirectoryName(operation.DestinationPath)!, index, ref path) is { } destinationDirectoryError)
                return destinationDirectoryError;
            path = operation.SourcePath;
            if (CheckSource(operation, index) is { } sourceError) return sourceError;
            path = operation.DestinationPath;
            try
            {
                fileSystem.GetAttributes(path);
                return new(ExecutionErrorCode.DestinationExists, "A file, directory, or reparse entry occupies the destination.", path, index);
            }
            catch (FileNotFoundException)
            {
                // Only a missing final name is free. Parent/access failures must abort the batch.
                return null;
            }
        }
        catch (Exception exception) when (IsExpectedFileSystemFailure(exception))
        {
            return new(ExecutionErrorCode.InspectionFailed, $"Pre-execution inspection failed: {exception.Message}", path, index);
        }
    }

    internal ExecutionError? VerifyCompletion(PlannedOperation operation, int index)
    {
        var path = operation.DestinationPath;
        ExecutionError Unverified(string message) => new(ExecutionErrorCode.VerificationFailed,
            $"Completion could not be verified: {message} Filesystem changes may remain; no cleanup was attempted.", path, index);
        try
        {
            if (InspectDirectory(Path.GetDirectoryName(operation.DestinationPath)!, index, ref path) is { } destinationError)
                return Unverified(destinationError.Message);
            path = operation.DestinationPath;
            if (!IsOrdinaryFile(fileSystem.GetAttributes(path)) ||
                fileSystem.ReadSourceMetadata(path).Length != operation.SourceLength)
                return Unverified("The destination is not an ordinary file of the expected length.");

            if (InspectDirectory(Path.GetDirectoryName(operation.SourcePath)!, index, ref path) is { } sourceDirectoryError)
                return Unverified(sourceDirectoryError.Message);
            path = operation.SourcePath;
            if (operation.Action == FileAction.Copy)
            {
                return CheckSource(operation, index) is { } sourceError ? Unverified(sourceError.Message) : null;
            }

            try
            {
                fileSystem.GetAttributes(path);
                return new(ExecutionErrorCode.MoveIncomplete,
                    "Move is incomplete: destination exists but the source path is still occupied. Both entries were left untouched; no cleanup was attempted.",
                    path, index);
            }
            catch (FileNotFoundException)
            {
                // An unreadable source or missing parent is not confirmed removal.
                return null;
            }
        }
        catch (Exception exception) when (IsExpectedFileSystemFailure(exception))
        {
            return Unverified(exception.Message);
        }
    }

    private static ExecutionError? ValidateStructure(PlannedOperation operation, FileRule? rule, int index)
    {
        ExecutionError Invalid() => new(ExecutionErrorCode.InvalidOperation,
            "The operation must retain canonical local paths, filename, action, and source metadata from the approved preview.",
            operation.SourcePath, index);

        if (rule is null || !Enum.IsDefined(operation.Action) || operation.Action != rule.Action ||
            operation.SourceLength < 0 || operation.SourceLastWriteUtc.Kind != DateTimeKind.Utc)
            return Invalid();

        // Reuse structural Windows path validation only; this does not enumerate or regenerate a preview.
        var ruleValidation = RuleValidator.Validate(rule);
        var pathValidation = RuleValidator.Validate(rule with
        {
            SourceDirectory = operation.SourcePath,
            DestinationDirectory = operation.DestinationPath
        });
        if (!ruleValidation.IsValid || !pathValidation.IsValid) return Invalid();
        if (!SamePath(rule.SourceDirectory, ruleValidation.Rule!.SourceDirectory) ||
            !SamePath(rule.DestinationDirectory, ruleValidation.Rule.DestinationDirectory) ||
            !SamePath(operation.SourcePath, pathValidation.Rule!.SourceDirectory) ||
            !SamePath(operation.DestinationPath, pathValidation.Rule.DestinationDirectory) ||
            !SamePath(Path.GetDirectoryName(operation.SourcePath), rule.SourceDirectory) ||
            !SamePath(Path.GetDirectoryName(operation.DestinationPath), rule.DestinationDirectory) ||
            !string.Equals(Path.GetFileName(operation.SourcePath), Path.GetFileName(operation.DestinationPath), StringComparison.Ordinal) ||
            !ExtensionMatcher.Matches(rule, operation.SourcePath))
            return Invalid();
        return null;
    }

    private ExecutionError? InspectDirectory(string directory, int index, ref string inspectedPath)
    {
        inspectedPath = directory;
        var drive = fileSystem.GetDriveType(directory);
        if (drive is DriveType.Network or DriveType.Unknown or DriveType.NoRootDirectory)
            return new(ExecutionErrorCode.UnsupportedPath, "Network, unknown, or unavailable drives cannot be executed.", directory, index);

        var components = new Stack<string>();
        for (string? path = directory; path is not null; path = Path.GetDirectoryName(path))
            components.Push(path);
        foreach (var path in components)
        {
            inspectedPath = path;
            var attributes = fileSystem.GetAttributes(path);
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0 ||
                (attributes & FileAttributes.Directory) == 0)
                return new(ExecutionErrorCode.UnsupportedPath, "Every path component must be an ordinary directory without reparse points.", path, index);
        }
        return null;
    }

    private ExecutionError? CheckSource(PlannedOperation operation, int index)
    {
        try
        {
            if (!IsOrdinaryFile(fileSystem.GetAttributes(operation.SourcePath)))
                return new(ExecutionErrorCode.UnsupportedPath, "The approved source is no longer an ordinary supported file.", operation.SourcePath, index);
            var metadata = fileSystem.ReadSourceMetadata(operation.SourcePath);
            if (metadata.Length != operation.SourceLength || metadata.LastWriteUtc != operation.SourceLastWriteUtc)
                return new(ExecutionErrorCode.SourceChanged, "Source length or last-write UTC differs from the approved preview.", operation.SourcePath, index);
            return null;
        }
        catch (FileNotFoundException)
        {
            return new(ExecutionErrorCode.SourceMissing, "The approved source file no longer exists.", operation.SourcePath, index);
        }
    }

    private static bool SamePath(string? first, string? second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
    private static bool IsOrdinaryFile(FileAttributes attributes) =>
        (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) == 0;

    internal static bool IsExpectedFileSystemFailure(Exception exception) => exception is
        IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException;
}
