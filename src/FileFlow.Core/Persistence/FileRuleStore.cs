using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using FileFlow.Core.Rules;

namespace FileFlow.Core.Persistence;

/// <summary>Local schema-v1 JSON storage. Does not check rule directory existence or execute rules.</summary>
public sealed class FileRuleStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter<FileAction>(allowIntegerValues: false) }
    };

    private readonly string directory;
    private readonly string filePath;

    /// <param name="storageDirectory">Override for isolated storage, including tests. Null selects the local application data directory.</param>
    public FileRuleStore(string? storageDirectory = null)
    {
        directory = Path.GetFullPath(storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileFlow"));
        filePath = Path.Combine(directory, "rules.json");
    }

    /// <summary>Missing files return empty rules. Invalid, unsupported, or inaccessible files return an error and are left alone.</summary>
    public RuleStoreResult Load()
    {
        try
        {
            return ReadCurrent(out _);
        }
        catch (JsonException exception)
        {
            return Failure(RulePersistenceErrorCode.InvalidData, $"Invalid JSON or rule data: {exception.Message}");
        }
        catch (Exception exception) when (IsIoFailure(exception))
        {
            return Failure(RulePersistenceErrorCode.IoError, $"Could not load rules: {exception.Message}");
        }
    }

    /// <summary>
    /// Saves a snapshot, retaining the previous valid document as rules.json.bak.
    /// Errors are returned to the caller; there is no delete-first or automatic recovery fallback.
    /// </summary>
    public RuleStoreResult Save(IReadOnlyList<FileRule> rules)
    {
        string? temporaryPath = null;
        string? backupTemporaryPath = null;
        try
        {
            var snapshot = SnapshotRules(rules);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new RuleDocument(1, snapshot), JsonOptions);
            Directory.CreateDirectory(directory);

            // Keep the lock file in place: deleting it could let writers lock different file identities.
            // A competing writer fails clearly and can retry; there is no background work.
            using var writerLock = new FileStream(filePath + ".lock", FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);

            var current = ReadCurrent(out var previousBytes);
            if (!current.IsSuccess)
                return current;

            WriteTemporary(bytes, ref temporaryPath);
            if (previousBytes is not null)
            {
                // Publish a flushed copy without moving the active file out of the way.
                // File.Replace can leave the original under its backup name on some Windows failures.
                WriteTemporary(previousBytes, ref backupTemporaryPath);
                File.Move(backupTemporaryPath!, filePath + ".bak", overwrite: true);
            }

            // Same-directory rename publishes the completed file. First launch refuses an unexpected target.
            File.Move(temporaryPath!, filePath, overwrite: previousBytes is not null);
            return new(snapshot, null);
        }
        catch (JsonException exception)
        {
            return Failure(RulePersistenceErrorCode.InvalidData, $"Rules were not saved. Invalid JSON or rule data: {exception.Message}");
        }
        catch (Exception exception) when (IsIoFailure(exception))
        {
            return Failure(RulePersistenceErrorCode.IoError, $"Rules were not saved: {exception.Message}");
        }
        finally
        {
            DeleteTemporary(temporaryPath);
            DeleteTemporary(backupTemporaryPath);
        }
    }

    private RuleStoreResult ReadCurrent(out byte[]? bytes)
    {
        bytes = null;
        try
        {
            // File.Exists would also return false for some access failures, hiding them as first launch.
            bytes = File.ReadAllBytes(filePath);
        }
        catch (FileNotFoundException)
        {
            return new(Array.Empty<FileRule>(), null);
        }
        catch (DirectoryNotFoundException)
        {
            CheckMissingDirectory();
            return new(Array.Empty<FileRule>(), null);
        }

        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("schemaVersion", out var version) ||
            version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var schemaVersion))
            throw new JsonException("A numeric schemaVersion is required.");

        if (schemaVersion != 1)
            return Failure(RulePersistenceErrorCode.UnsupportedSchemaVersion,
                $"Schema version {schemaVersion} is unsupported; expected version 1. The file was preserved.");

        RequireProperties(root, "schemaVersion", "rules");
        var entries = root.GetProperty("rules");
        if (entries.ValueKind != JsonValueKind.Array)
            throw new JsonException("The rules property must be an array.");

        var rules = new List<FileRule>();
        foreach (var entry in entries.EnumerateArray())
        {
            // All fields are mandatory: FileRule's defaults must not fabricate a missing ID or action.
            RequireProperties(entry, "id", "name", "sourceDirectory", "extensions", "action", "destinationDirectory");
            var action = entry.GetProperty("action");
            if (action.ValueKind != JsonValueKind.String || action.GetString() is not ("Copy" or "Move"))
                throw new JsonException("The action must be exactly 'Copy' or 'Move'.");
            rules.Add(entry.Deserialize<FileRule>(JsonOptions)!);
        }

        return new(SnapshotRules(rules), null);
    }

    private void CheckMissingDirectory()
    {
        // Windows also reports path-not-found when a parent component is a regular file.
        // Find the nearest existing ancestor without creating anything or hiding access failures.
        for (string? path = directory; path is not null; path = Path.GetDirectoryName(path))
        {
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.Directory) == 0)
                    throw new IOException($"Storage directory component '{path}' is a file.");
                return;
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static IReadOnlyList<FileRule> SnapshotRules(IReadOnlyList<FileRule>? rules)
    {
        if (rules is null)
            throw new JsonException("A rule collection is required.");

        var snapshot = new List<FileRule>(rules.Count);
        foreach (var rule in rules)
        {
            if (rule is null)
                throw new JsonException("A rule cannot be null.");

            var validation = RuleValidator.Validate(rule);
            if (!validation.IsValid)
                throw new JsonException($"Rule '{rule.Id}' is invalid: " +
                    string.Join(" ", validation.Errors.Select(error => $"{error.PropertyName}: {error.Message}")));

            // Validate without renormalizing persisted values or sharing callers' mutable extension lists.
            snapshot.Add(rule with { Extensions = Array.AsReadOnly(rule.Extensions.ToArray()) });
        }
        return snapshot.AsReadOnly();
    }

    private static void RequireProperties(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new JsonException("Expected a JSON object.");

        var remaining = new HashSet<string>(names, StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!remaining.Remove(property.Name))
                throw new JsonException($"Unknown or duplicate property '{property.Name}'.");

        if (remaining.Count != 0)
            throw new JsonException($"Missing required properties: {string.Join(", ", remaining)}.");
    }

    private void WriteTemporary(byte[] bytes, ref string? ownedPath)
    {
        var path = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        ownedPath = path; // Cleanup is permitted only after this call has created the file.
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static void DeleteTemporary(string? path)
    {
        if (path is null)
            return;
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (IsIoFailure(exception))
        {
            // Cleanup must not hide the save outcome. Leftovers are never loaded or promoted.
        }
    }

    private RuleStoreResult Failure(RulePersistenceErrorCode code, string message) =>
        new(null, new(code, $"FileFlow persistence error for '{filePath}': {message}"));

    private static bool IsIoFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException;

    private sealed record RuleDocument(int SchemaVersion, IReadOnlyList<FileRule> Rules);
}
