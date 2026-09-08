namespace FileFlow.Core.Rules;

/// <summary>A rule definition. Validate before saving or evaluating it.</summary>
public sealed record FileRule
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public string SourceDirectory { get; init; } = string.Empty;
    public IReadOnlyList<string> Extensions { get; init; } = Array.Empty<string>();
    public FileAction Action { get; init; } = FileAction.Copy;
    public string DestinationDirectory { get; init; } = string.Empty;
}
