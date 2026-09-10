using FileFlow.Core.Execution;

namespace FileFlow.App.ViewModels;

public sealed class ExecutionResultsViewModel(ExecutionResult result)
{
    public ExecutionResult Result { get; } = result;
    public bool IsPartialFailure => Result.Outcome == ExecutionOutcome.PartiallySucceeded;
    public string Title => Result.Outcome switch
    {
        ExecutionOutcome.Succeeded => "Completed",
        ExecutionOutcome.Rejected => "Execution did not start",
        ExecutionOutcome.PartiallySucceeded => "Partially completed",
        _ => "Execution stopped"
    };
    public string Summary => Result.Outcome switch
    {
        ExecutionOutcome.Succeeded => "All operations completed successfully.",
        ExecutionOutcome.Rejected => "FileFlow did not change any files in this attempt. Resolve the issues, then run the rule for a fresh preview.",
        ExecutionOutcome.PartiallySucceeded => "Some operations completed before the failure. FileFlow did not automatically roll them back. Failed operations may also have left filesystem changes.",
        _ => "FileFlow stopped at the first failure. Filesystem changes may remain; nothing was automatically rolled back."
    };
}
