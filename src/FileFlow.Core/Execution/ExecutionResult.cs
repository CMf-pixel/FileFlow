namespace FileFlow.Core.Execution;

public enum ExecutionOutcome
{
    Rejected,
    Succeeded,
    Failed,
    PartiallySucceeded
}

/// <summary>An immutable result in approved preview order. Preflight rejection never mutates files.</summary>
public sealed class ExecutionResult
{
    public IReadOnlyList<OperationResult> Operations { get; }
    public IReadOnlyList<ExecutionError> Errors { get; }
    public int SucceededCount { get; }
    public int FailedCount { get; }
    public int NotAttemptedCount { get; }
    public bool PreflightRejected { get; }
    public ExecutionOutcome Outcome { get; }

    internal ExecutionResult(IEnumerable<OperationResult> operations, IEnumerable<ExecutionError> errors, bool preflightRejected)
    {
        Operations = Array.AsReadOnly(operations.ToArray());
        Errors = Array.AsReadOnly(errors.ToArray());
        SucceededCount = Operations.Count(result => result.Status == OperationStatus.Succeeded);
        FailedCount = Operations.Count(result => result.Status == OperationStatus.Failed);
        NotAttemptedCount = Operations.Count(result => result.Status == OperationStatus.NotAttempted);
        PreflightRejected = preflightRejected;
        Outcome = preflightRejected ? ExecutionOutcome.Rejected
            : FailedCount == 0 ? ExecutionOutcome.Succeeded
            : SucceededCount == 0 ? ExecutionOutcome.Failed : ExecutionOutcome.PartiallySucceeded;
    }
}
