namespace FileFlow.Core.Rules;

public sealed record RuleValidationError(string PropertyName, string Message);
