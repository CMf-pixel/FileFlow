using FileFlow.Core.Execution;
using FileFlow.Core.Preview;
using FileFlow.Core.Rules;
using Xunit;

namespace FileFlow.Core.Tests;

public sealed class ExecutionResultTests
{
    private static PlannedOperation Operation => new(@"C:\source\a.png", @"C:\target\a.png",
        FileAction.Copy, 3, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    [Theory]
    [InlineData(0, 0, 2, true, ExecutionOutcome.Rejected)]
    [InlineData(2, 0, 0, false, ExecutionOutcome.Succeeded)]
    [InlineData(0, 1, 1, false, ExecutionOutcome.Failed)]
    [InlineData(1, 1, 1, false, ExecutionOutcome.PartiallySucceeded)]
    public void Counts_and_outcome_describe_the_ordered_results(int succeeded, int failed, int notAttempted,
        bool rejected, ExecutionOutcome outcome)
    {
        var operation = Operation;
        var error = new ExecutionError(ExecutionErrorCode.MutationFailed, "disk full", operation.DestinationPath, succeeded);
        var items = Enumerable.Repeat(new OperationResult(operation, OperationStatus.Succeeded), succeeded)
            .Concat(Enumerable.Repeat(new OperationResult(operation, OperationStatus.Failed, error), failed))
            .Concat(Enumerable.Repeat(new OperationResult(operation, OperationStatus.NotAttempted), notAttempted)).ToList();
        var errors = new List<ExecutionError> { error };
        var result = new ExecutionResult(items, errors, rejected);
        items.Clear();
        errors.Clear();

        Assert.Equal(succeeded, result.SucceededCount);
        Assert.Equal(failed, result.FailedCount);
        Assert.Equal(notAttempted, result.NotAttemptedCount);
        Assert.Equal(rejected, result.PreflightRejected);
        Assert.Equal(outcome, result.Outcome);
        Assert.Same(operation, result.Operations[0].Operation);
        Assert.Same(error, Assert.Single(result.Errors));
        Assert.Throws<NotSupportedException>(() => ((IList<OperationResult>)result.Operations).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<ExecutionError>)result.Errors).Clear());
    }

    [Fact]
    public void Preview_identity_can_only_be_claimed_once_even_concurrently()
    {
        var preview = new OperationPreview(null, new[] { Operation }, Array.Empty<PreviewIssue>(), false);
        var claims = new bool[32];
        Parallel.For(0, claims.Length, i => claims[i] = PreviewExecutionGuard.TryClaim(preview));
        Assert.Equal(1, claims.Count(claimed => claimed));
        Assert.False(PreviewExecutionGuard.TryClaim(preview));
        Assert.True(PreviewExecutionGuard.TryClaim(new OperationPreview(null, new[] { Operation },
            Array.Empty<PreviewIssue>(), false)));
    }
}
