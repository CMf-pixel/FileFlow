using FileFlow.Core.Preview;

namespace FileFlow.Core.Execution;

public enum OperationStatus
{
    Succeeded,
    Failed,
    NotAttempted
}

/// <summary>A result for the exact approved operation. Failure does not imply no filesystem changes.</summary>
public sealed record OperationResult(PlannedOperation Operation, OperationStatus Status, ExecutionError? Error = null);
