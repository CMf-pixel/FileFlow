using FileFlow.Core.Persistence;
using FileFlow.Core.Rules;

namespace FileFlow.App.Services;

public interface IRuleStore
{
    RuleStoreResult Load();
    RuleStoreResult Save(IReadOnlyList<FileRule> rules);
}
