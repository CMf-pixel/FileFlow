using FileFlow.Core.Rules;

namespace FileFlow.Core.Preview;

public enum PreviewStatus
{
    Ready,
    NoMatches,
    Blocked,
    Incomplete
}

/// <summary>An immutable observation, not an atomic filesystem snapshot or an execution-time guarantee.</summary>
public sealed class OperationPreview
{
    public FileRule? Rule { get; }
    public IReadOnlyList<PlannedOperation> Operations { get; }
    public IReadOnlyList<PreviewIssue> Issues { get; }
    public PreviewStatus Status { get; }

    /// <summary>Preview eligibility only. Future execution must independently revalidate the filesystem.</summary>
    public bool CanExecute => Status == PreviewStatus.Ready && Operations.Count > 0 && Issues.Count == 0;

    internal OperationPreview(FileRule? rule, IEnumerable<PlannedOperation> operations,
        IEnumerable<PreviewIssue> issues, bool incomplete)
    {
        Rule = rule is null ? null : rule with { Extensions = Array.AsReadOnly(rule.Extensions.ToArray()) };
        Operations = Array.AsReadOnly(operations
            .OrderBy(operation => Path.GetFileName(operation.SourcePath), StringComparer.OrdinalIgnoreCase)
            .ThenBy(operation => Path.GetFileName(operation.SourcePath), StringComparer.Ordinal).ToArray());
        Issues = Array.AsReadOnly(issues.ToArray());
        Status = incomplete ? PreviewStatus.Incomplete
            : Issues.Count > 0 ? PreviewStatus.Blocked
            : Operations.Count == 0 ? PreviewStatus.NoMatches
            : PreviewStatus.Ready;
    }
}
