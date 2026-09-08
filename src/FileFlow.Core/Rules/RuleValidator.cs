namespace FileFlow.Core.Rules;

public static class RuleValidator
{
    /// <summary>
    /// Validates and normalizes a definition using Windows path semantics, without accessing the filesystem.
    /// Directory existence, permissions, mapped drives and links must be checked separately before use.
    /// </summary>
    public static RuleValidationResult Validate(FileRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var errors = new List<RuleValidationError>();

        if (rule.Id == Guid.Empty)
            errors.Add(new(nameof(FileRule.Id), "The rule must have a non-empty identifier."));

        if (string.IsNullOrWhiteSpace(rule.Name))
            errors.Add(new(nameof(FileRule.Name), "Enter a rule name."));

        if (!Enum.IsDefined(rule.Action))
            errors.Add(new(nameof(FileRule.Action), "Choose Copy or Move."));

        var extensions = NormalizeExtensions(rule.Extensions, errors);
        var source = NormalizeDirectory(rule.SourceDirectory, nameof(FileRule.SourceDirectory), errors);
        var destination = NormalizeDirectory(rule.DestinationDirectory, nameof(FileRule.DestinationDirectory), errors);

        if (source is not null && destination is not null &&
            string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new(nameof(FileRule.DestinationDirectory), "Choose a destination different from the source directory."));
        }

        if (errors.Count > 0)
            return new(null, errors.AsReadOnly());

        return new(rule with
        {
            Name = rule.Name.Trim(),
            SourceDirectory = source!,
            DestinationDirectory = destination!,
            Extensions = extensions.AsReadOnly()
        }, Array.Empty<RuleValidationError>());
    }

    private static List<string> NormalizeExtensions(
        IReadOnlyList<string>? entries, List<RuleValidationError> errors)
    {
        var extensions = new List<string>();
        if (entries is null || entries.Count == 0)
        {
            errors.Add(new(nameof(FileRule.Extensions), "Add at least one extension, such as .png."));
            return extensions;
        }

        foreach (var entry in entries)
        {
            var value = entry?.Trim() ?? string.Empty;
            if (value.StartsWith('.'))
                value = value[1..];

            if (value.Length == 0 || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                value.Any(character => char.IsWhiteSpace(character) || character is '.' or ',' or ';'))
            {
                errors.Add(new(nameof(FileRule.Extensions),
                    "Enter one extension per entry, such as .png; wildcards, separators and compound extensions are not supported."));
                continue;
            }

            var extension = "." + value.ToLowerInvariant();
            if (!extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                extensions.Add(extension);
        }

        return extensions;
    }

    private static string? NormalizeDirectory(
        string? path, string propertyName, List<RuleValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            errors.Add(new(propertyName, "Select a directory."));
            return null;
        }

        // Accept ordinary drive-qualified Windows paths only, not UNC or device paths.
        var value = path.Replace('/', '\\');
        if (value.Length < 3 || !char.IsAsciiLetter(value[0]) || value[1] != ':' || value[2] != '\\')
        {
            errors.Add(new(propertyName, @"Enter a full local directory path, such as C:\Users\User\Downloads."));
            return null;
        }

        foreach (var component in value[3..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (component is "." or "..")
                continue;

            if (component.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                component.EndsWith('.') || component.EndsWith(' ') || IsReservedName(component))
            {
                errors.Add(new(propertyName, "The directory path contains an invalid or reserved name, or a name ending in a dot or space."));
                return null;
            }
        }

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            errors.Add(new(propertyName, "The directory path is invalid or too long."));
            return null;
        }
    }

    private static bool IsReservedName(string component)
    {
        var name = component.Split('.')[0].ToUpperInvariant();
        return name is "CON" or "PRN" or "AUX" or "NUL" ||
            (name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal) ||
                                name.StartsWith("LPT", StringComparison.Ordinal)) &&
             name[3] is (>= '1' and <= '9') or '¹' or '²' or '³');
    }
}
