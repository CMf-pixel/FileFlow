namespace FileFlow.Core.Execution;

public enum ExecutionErrorCode
{
    InvalidPreview,
    PreviewAlreadyAttempted,
    InvalidOperation,
    SourceMissing,
    SourceChanged,
    DestinationExists,
    UnsupportedPath,
    InspectionFailed,
    MutationFailed,
    MoveIncomplete,
    VerificationFailed
}

/// <summary>A diagnostic with an optional zero-based index into the approved preview.</summary>
public sealed record ExecutionError(ExecutionErrorCode Code, string Message, string? Path = null, int? OperationIndex = null);
