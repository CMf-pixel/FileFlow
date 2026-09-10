using FileFlow.Core.Rules;

namespace FileFlow.App.ViewModels;

public sealed class RuleRowViewModel(FileRule rule)
{
    public FileRule Rule { get; } = rule;
    public string Extensions => string.Join("  ", Rule.Extensions);
}
