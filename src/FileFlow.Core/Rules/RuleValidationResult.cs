namespace FileFlow.Core.Rules;

public sealed record RuleValidationResult(FileRule? Rule, IReadOnlyList<RuleValidationError> Errors)
{
    public bool IsValid => Rule is not null && Errors.Count == 0;
}
