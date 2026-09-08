using FileFlow.Core.Rules;

namespace FileFlow.Core.Persistence;

/// <summary>On success, contains the loaded or saved rules. On failure, Rules is null, never an empty fallback.</summary>
public sealed record RuleStoreResult(IReadOnlyList<FileRule>? Rules, RulePersistenceError? Error)
{
    public bool IsSuccess => Rules is not null && Error is null;
}
