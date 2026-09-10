using FileFlow.Core.Persistence;
using FileFlow.Core.Rules;

namespace FileFlow.App.Services;

public sealed class CoreRuleStore(FileRuleStore store) : IRuleStore
{
    public RuleStoreResult Load() => store.Load();
    public RuleStoreResult Save(IReadOnlyList<FileRule> rules) => store.Save(rules);
}
