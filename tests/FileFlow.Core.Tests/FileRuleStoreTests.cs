using System.Text.Json;
using System.Text.Json.Nodes;
using FileFlow.Core.Persistence;
using FileFlow.Core.Rules;
using Xunit;

namespace FileFlow.Core.Tests;

public sealed class FileRuleStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "FileFlow.Core.Tests", Guid.NewGuid().ToString("N"));
    private string RulesPath => Path.Combine(directory, "rules.json");
    private string BackupPath => RulesPath + ".bak";
    private FileRuleStore Store() => new(directory);

    private FileRule Rule(string name = "Sort images", FileAction action = FileAction.Copy) => new()
    {
        Id = Guid.Parse("271df344-1e31-4ea0-b1b8-716cfa9f39e2"),
        Name = name,
        SourceDirectory = Path.Combine(directory, "source"),
        Extensions = new[] { ".png", ".jpg" },
        Action = action,
        DestinationDirectory = Path.Combine(directory, "destination")
    };

    [Theory]
    [InlineData(FileAction.Copy)]
    [InlineData(FileAction.Move)]
    public void Round_trip_preserves_every_field_across_store_instances(FileAction action)
    {
        var rule = Rule("Фото and documents", action);
        AssertSuccess(Store().Save(new[] { rule }));

        var actual = Assert.Single(AssertSuccess(Store().Load()));

        Assert.Equal(rule.Id, actual.Id);
        Assert.Equal(rule.Name, actual.Name);
        Assert.Equal(rule.SourceDirectory, actual.SourceDirectory);
        Assert.Equal(new[] { ".png", ".jpg" }, actual.Extensions);
        Assert.Equal(action, actual.Action);
        Assert.Equal(rule.DestinationDirectory, actual.DestinationDirectory);
    }

    [Fact]
    public void Multiple_rules_preserve_order()
    {
        var rules = new[] { Rule("Third"), Rule("First") with { Id = Guid.NewGuid() }, Rule("Second") with { Id = Guid.NewGuid() } };
        AssertSuccess(Store().Save(rules));
        Assert.Equal(new[] { "Third", "First", "Second" }, AssertSuccess(Store().Load()).Select(rule => rule.Name));
    }

    [Fact]
    public void Missing_file_returns_empty_without_creating_the_storage_directory()
    {
        Assert.Empty(AssertSuccess(Store().Load()));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void Empty_collection_can_replace_rules()
    {
        AssertSuccess(Store().Save(new[] { Rule() }));
        AssertSuccess(Store().Save(Array.Empty<FileRule>()));
        Assert.Empty(AssertSuccess(Store().Load()));
    }

    [Fact]
    public void Schema_version_one_fixture_loads()
    {
        WriteActive(VersionOneJson);
        var rule = Assert.Single(AssertSuccess(Store().Load()));
        Assert.Equal(Guid.Parse("271df344-1e31-4ea0-b1b8-716cfa9f39e2"), rule.Id);
        Assert.Equal("Fixture", rule.Name);
        Assert.Equal(@"C:\FileFlow-missing-source", rule.SourceDirectory);
        Assert.Equal(new[] { ".txt" }, rule.Extensions);
        Assert.Equal(FileAction.Move, rule.Action);
        Assert.Equal(@"D:\FileFlow-missing-destination", rule.DestinationDirectory);
    }

    [Fact]
    public void Saved_document_declares_schema_one_and_action_names()
    {
        AssertSuccess(Store().Save(new[] { Rule(action: FileAction.Move) }));
        using var document = JsonDocument.Parse(File.ReadAllText(RulesPath));
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("Move", document.RootElement.GetProperty("rules")[0].GetProperty("action").GetString());
        Assert.False(File.Exists(BackupPath));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-1)]
    public void Unsupported_version_is_rejected_and_preserved_even_by_a_fresh_writer(int version)
    {
        var json = $$"""{"schemaVersion":{{version}},"rules":[]} """;
        WriteActive(json);
        AssertError(Store().Load(), RulePersistenceErrorCode.UnsupportedSchemaVersion);
        AssertError(Store().Save(new[] { Rule() }), RulePersistenceErrorCode.UnsupportedSchemaVersion);
        Assert.Equal(json, File.ReadAllText(RulesPath));
        Assert.False(File.Exists(BackupPath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":\"1\",\"rules\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"rules\":null}")]
    [InlineData("{\"schemaVersion\":1,\"rules\":[null]}")]
    [InlineData("{\"schemaVersion\":1,\"rules\":[{}]}")]
    [InlineData("{\"schemaVersion\":1,\"rules\":[],\"extra\":true}")]
    [InlineData("{\"schemaVersion\":1,\"rules\":[],\"rules\":[]}")]
    public void Corrupt_data_returns_a_clear_error_and_is_never_overwritten(string json)
    {
        WriteActive(json);
        File.WriteAllText(BackupPath, VersionOneJson);
        AssertError(Store().Load(), RulePersistenceErrorCode.InvalidData);
        AssertError(Store().Save(new[] { Rule() }), RulePersistenceErrorCode.InvalidData);
        Assert.Equal(json, File.ReadAllText(RulesPath));
        Assert.Equal(VersionOneJson, File.ReadAllText(BackupPath));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("name")]
    [InlineData("sourceDirectory")]
    [InlineData("extensions")]
    [InlineData("action")]
    [InlineData("destinationDirectory")]
    public void Missing_rule_fields_are_rejected_instead_of_defaulted(string property)
    {
        var document = JsonNode.Parse(VersionOneJson)!;
        document["rules"]![0]!.AsObject().Remove(property);
        var json = document.ToJsonString();
        WriteActive(json);
        AssertError(Store().Load(), RulePersistenceErrorCode.InvalidData);
        AssertError(Store().Save(new[] { Rule() }), RulePersistenceErrorCode.InvalidData);
        Assert.Equal(json, File.ReadAllText(RulesPath));
    }

    [Theory]
    [InlineData("id", "\"not-a-guid\"")]
    [InlineData("name", "null")]
    [InlineData("sourceDirectory", "\"relative\"")]
    [InlineData("extensions", "[]")]
    [InlineData("extensions", "[null]")]
    [InlineData("action", "\"Delete\"")]
    [InlineData("action", "\"Copy, Move\"")]
    [InlineData("action", "\"move\"")]
    [InlineData("action", "\"0\"")]
    [InlineData("action", "\"1\"")]
    [InlineData("action", "99")]
    [InlineData("destinationDirectory", "null")]
    public void Invalid_rule_fields_are_rejected_and_preserved(string property, string value)
    {
        var document = JsonNode.Parse(VersionOneJson)!;
        document["rules"]![0]![property] = JsonNode.Parse(value);
        var json = document.ToJsonString();
        WriteActive(json);
        AssertError(Store().Load(), RulePersistenceErrorCode.InvalidData);
        AssertError(Store().Save(new[] { Rule() }), RulePersistenceErrorCode.InvalidData);
        Assert.Equal(json, File.ReadAllText(RulesPath));
    }

    [Fact]
    public void Load_does_not_require_source_or_destination_to_still_exist()
    {
        var rule = Rule();
        Directory.CreateDirectory(rule.SourceDirectory);
        Directory.CreateDirectory(rule.DestinationDirectory);
        AssertSuccess(Store().Save(new[] { rule }));
        Directory.Delete(rule.SourceDirectory);
        Directory.Delete(rule.DestinationDirectory);
        Assert.Equal(rule.SourceDirectory, Assert.Single(AssertSuccess(Store().Load())).SourceDirectory);
        Assert.False(Directory.Exists(rule.SourceDirectory));
        Assert.False(Directory.Exists(rule.DestinationDirectory));
    }

    [Fact]
    public void Replacement_keeps_exactly_one_previous_valid_backup()
    {
        AssertSuccess(Store().Save(new[] { Rule("One") }));
        var first = File.ReadAllBytes(RulesPath);
        AssertSuccess(Store().Save(new[] { Rule("Two") }));
        Assert.Equal(first, File.ReadAllBytes(BackupPath));
        var second = File.ReadAllBytes(RulesPath);
        AssertSuccess(Store().Save(new[] { Rule("Three") }));
        Assert.Equal(second, File.ReadAllBytes(BackupPath));
        Assert.Equal("Three", Assert.Single(AssertSuccess(Store().Load())).Name);
        Assert.Single(Directory.GetFiles(directory, "*.bak"));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public void Failed_publication_preserves_active_rules_and_retry_does_not_resurrect_failed_data()
    {
        AssertSuccess(Store().Save(new[] { Rule("Older backup") }));
        var older = File.ReadAllBytes(RulesPath);
        AssertSuccess(Store().Save(new[] { Rule("Original") }));
        var original = File.ReadAllBytes(RulesPath);
        Assert.Equal(older, File.ReadAllBytes(BackupPath));
        // Reading remains possible, but the OS refuses renaming over this open file.
        using (var locked = new FileStream(RulesPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            AssertError(Store().Save(new[] { Rule("Failed") }), RulePersistenceErrorCode.IoError);

        Assert.Equal(original, File.ReadAllBytes(RulesPath));
        Assert.Equal(original, File.ReadAllBytes(BackupPath));
        Assert.Equal("Original", Assert.Single(AssertSuccess(Store().Load())).Name);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        AssertSuccess(Store().Save(new[] { Rule("Retry") }));
        Assert.Equal("Retry", Assert.Single(AssertSuccess(Store().Load())).Name);
        Assert.Equal(original, File.ReadAllBytes(BackupPath));
    }

    [Fact]
    public void Failed_backup_publication_preserves_active_and_previous_backup()
    {
        AssertSuccess(Store().Save(new[] { Rule("One") }));
        AssertSuccess(Store().Save(new[] { Rule("Two") }));
        var active = File.ReadAllBytes(RulesPath);
        var backup = File.ReadAllBytes(BackupPath);
        using (var locked = new FileStream(BackupPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            AssertError(Store().Save(new[] { Rule("Failed") }), RulePersistenceErrorCode.IoError);

        Assert.Equal(active, File.ReadAllBytes(RulesPath));
        Assert.Equal(backup, File.ReadAllBytes(BackupPath));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public void Invalid_input_cannot_replace_a_valid_file()
    {
        AssertSuccess(Store().Save(new[] { Rule() }));
        var original = File.ReadAllBytes(RulesPath);
        AssertError(Store().Save(new[] { Rule() with { Name = "" } }), RulePersistenceErrorCode.InvalidData);
        AssertError(Store().Save(new FileRule[] { null! }), RulePersistenceErrorCode.InvalidData);
        AssertError(Store().Save(null!), RulePersistenceErrorCode.InvalidData);
        Assert.Equal(original, File.ReadAllBytes(RulesPath));
    }

    [Fact]
    public void Leftover_temporary_files_and_backup_are_never_promoted()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(BackupPath, VersionOneJson);
        var temporary = Path.Combine(directory, "rules.json.abandoned.tmp");
        File.WriteAllText(temporary, VersionOneJson);
        Assert.Empty(AssertSuccess(Store().Load()));
        AssertSuccess(Store().Save(new[] { Rule("Active") }));
        Assert.Equal("Active", Assert.Single(AssertSuccess(Store().Load())).Name);
        Assert.Equal(VersionOneJson, File.ReadAllText(temporary));
        Assert.Equal(VersionOneJson, File.ReadAllText(BackupPath));
    }

    [Fact]
    public void Another_writer_lock_returns_an_error_without_modifying_rules()
    {
        AssertSuccess(Store().Save(new[] { Rule() }));
        var original = File.ReadAllBytes(RulesPath);
        using (var locked = new FileStream(RulesPath + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            AssertError(Store().Save(new[] { Rule("Competing") }), RulePersistenceErrorCode.IoError);
        Assert.Equal(original, File.ReadAllBytes(RulesPath));
        AssertSuccess(Store().Save(new[] { Rule("After lock release") }));
    }

    [Fact]
    public void Unreadable_active_file_returns_an_error_instead_of_first_launch()
    {
        WriteActive(VersionOneJson);
        using var locked = new FileStream(RulesPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        AssertError(Store().Load(), RulePersistenceErrorCode.IoError);
        AssertError(Store().Save(new[] { Rule() }), RulePersistenceErrorCode.IoError);
    }

    [Fact]
    public void Directory_at_active_file_path_is_an_error()
    {
        Directory.CreateDirectory(RulesPath);
        AssertError(Store().Load(), RulePersistenceErrorCode.IoError);
        AssertError(Store().Save(new[] { Rule() }), RulePersistenceErrorCode.IoError);
        Assert.True(Directory.Exists(RulesPath));
    }

    [Fact]
    public void Unusable_storage_directory_returns_a_save_error()
    {
        Directory.CreateDirectory(directory);
        var blockedDirectory = Path.Combine(directory, "blocked");
        File.WriteAllText(blockedDirectory, "Keep me");
        AssertError(new FileRuleStore(blockedDirectory).Save(new[] { Rule() }), RulePersistenceErrorCode.IoError);
        Assert.Equal("Keep me", File.ReadAllText(blockedDirectory));
    }

    [Theory]
    [InlineData("")]
    [InlineData("child")]
    public void File_in_storage_directory_path_is_not_treated_as_first_launch(string child)
    {
        Directory.CreateDirectory(directory);
        var blockedDirectory = Path.Combine(directory, "blocked");
        File.WriteAllText(blockedDirectory, "Keep me");
        AssertError(new FileRuleStore(Path.Combine(blockedDirectory, child)).Load(), RulePersistenceErrorCode.IoError);
        Assert.Equal("Keep me", File.ReadAllText(blockedDirectory));
    }

    private void WriteActive(string json)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(RulesPath, json);
    }

    private static IReadOnlyList<FileRule> AssertSuccess(RuleStoreResult result)
    {
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Null(result.Error);
        return Assert.IsAssignableFrom<IReadOnlyList<FileRule>>(result.Rules);
    }

    private static void AssertError(RuleStoreResult result, RulePersistenceErrorCode code)
    {
        Assert.False(result.IsSuccess);
        Assert.Null(result.Rules);
        Assert.NotNull(result.Error);
        Assert.Equal(code, result.Error.Code);
        Assert.Contains("FileFlow", result.Error.Message);
        Assert.False(string.IsNullOrWhiteSpace(result.Error.Message));
    }

    public void Dispose()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FileFlow.Core.Tests")) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(directory);
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Test cleanup must remain inside the isolated temporary root.");
        if (Directory.Exists(target))
            Directory.Delete(target, recursive: true);
    }

    private const string VersionOneJson = """
        {
          "schemaVersion": 1,
          "rules": [{
            "id": "271df344-1e31-4ea0-b1b8-716cfa9f39e2",
            "name": "Fixture",
            "sourceDirectory": "C:\\FileFlow-missing-source",
            "extensions": [".txt"],
            "action": "Move",
            "destinationDirectory": "D:\\FileFlow-missing-destination"
          }]
        }
        """;
}
