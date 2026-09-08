namespace FileFlow.Core.Rules;

public static class ExtensionMatcher
{
    /// <summary>Matches a filename against the extensions of a validated rule. Does not access files.</summary>
    public static bool Matches(FileRule rule, string filePath)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(filePath);

        var extension = Path.GetExtension(filePath);
        return extension.Length > 0 && rule.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }
}
