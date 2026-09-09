using FileFlow.Core.Preview;
using FileFlow.Core.Rules;

namespace FileFlow.Core.Execution;

/// <summary>
/// Executes an explicitly approved preview once, synchronously and in its existing order.
/// Callers must obtain approval before calling. Checks narrow filesystem races but do not lock paths
/// or make execution transactional. Failed operations may leave filesystem changes; no cleanup is attempted.
/// </summary>
public sealed class FileOperationExecutor
{
    private readonly IExecutionFileSystem fileSystem;
    private readonly ExecutionPreflight preflight;

    public FileOperationExecutor() : this(new ExecutionFileSystem()) { }

    internal FileOperationExecutor(IExecutionFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        this.fileSystem = fileSystem;
        preflight = new(fileSystem);
    }

    /// <summary>Consumes this preview even on rejection. Every subsequent attempt requires a fresh approved preview.</summary>
    public ExecutionResult Execute(OperationPreview approvedPreview)
    {
        ArgumentNullException.ThrowIfNull(approvedPreview);
        if (!PreviewExecutionGuard.TryClaim(approvedPreview))
            return Reject(approvedPreview, new[] { new ExecutionError(ExecutionErrorCode.PreviewAlreadyAttempted,
                "This preview has already been submitted. Generate and approve a fresh preview.") });
        if (approvedPreview.Status != PreviewStatus.Ready || approvedPreview.Operations.Count == 0 || approvedPreview.Issues.Count != 0)
            return Reject(approvedPreview, new[] { new ExecutionError(ExecutionErrorCode.InvalidPreview,
                $"Preview status {approvedPreview.Status} cannot execute. A Ready preview with operations and no issues is required.") });

        var errors = preflight.ValidateBatch(approvedPreview);
        if (errors.Count > 0) return Reject(approvedPreview, errors);

        var results = approvedPreview.Operations.Select(operation => new OperationResult(operation, OperationStatus.NotAttempted)).ToArray();
        for (var index = 0; index < approvedPreview.Operations.Count; index++)
        {
            var operation = approvedPreview.Operations[index];
            var error = preflight.CheckOperation(operation, approvedPreview.Rule, index);
            if (error is null)
            {
                try
                {
                    if (operation.Action == FileAction.Copy)
                        fileSystem.CopyNoOverwrite(operation.SourcePath, operation.DestinationPath);
                    else
                        fileSystem.MoveNoOverwrite(operation.SourcePath, operation.DestinationPath);
                }
                catch (Exception exception) when (ExecutionPreflight.IsExpectedFileSystemFailure(exception))
                {
                    error = new(ExecutionErrorCode.MutationFailed,
                        $"{operation.Action} failed: {exception.Message} Filesystem changes may remain; no cleanup was attempted.",
                        operation.DestinationPath, index);
                }
                if (error is null) error = preflight.VerifyCompletion(operation, index);
            }

            if (error is not null)
            {
                results[index] = new(operation, OperationStatus.Failed, error);
                return new(results, new[] { error }, false);
            }
            results[index] = new(operation, OperationStatus.Succeeded);
        }
        return new(results, Array.Empty<ExecutionError>(), false);
    }

    private static ExecutionResult Reject(OperationPreview preview, IEnumerable<ExecutionError> errors) => new(
        preview.Operations.Select(operation => new OperationResult(operation, OperationStatus.NotAttempted)), errors, true);
}
