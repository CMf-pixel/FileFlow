using FileFlow.Core.Rules;
using Xunit;

namespace FileFlow.Core.Tests;

public sealed class RuleValidatorTests
{
    private static FileRule ValidRule() => new()
    {
        Id = Guid.Parse("271df344-1e31-4ea0-b1b8-716cfa9f39e2"),
        Name = "Sort images",
        SourceDirectory = @"C:\FileFlow-test-source",
        Extensions = new[] { ".png", ".jpg" },
        DestinationDirectory = @"D:\FileFlow-test-destination"
    };

    [Theory]
    [InlineData(FileAction.Copy)]
    [InlineData(FileAction.Move)]
    public void Normalizes_a_definition_without_requiring_existing_directories(FileAction action)
    {
        var input = ValidRule() with
        {
            Name = "  Sort images  ",
            SourceDirectory = @"C:/FileFlow-test-source/child/../",
            DestinationDirectory = @"D:\FileFlow-test-destination\",
            Extensions = new[] { " png ", ".PNG", "JpG", ".jpg" },
            Action = action
        };

        var result = RuleValidator.Validate(input);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        var rule = Assert.IsType<FileRule>(result.Rule);
        Assert.Equal(input.Id, rule.Id);
        Assert.Equal(action, rule.Action);
        Assert.Equal("Sort images", rule.Name);
        Assert.Equal(@"C:\FileFlow-test-source", rule.SourceDirectory);
        Assert.Equal(@"D:\FileFlow-test-destination", rule.DestinationDirectory);
        Assert.Equal(new[] { ".png", ".jpg" }, rule.Extensions);
        Assert.Equal("  Sort images  ", input.Name);
        Assert.Equal(new[] { " png ", ".PNG", "JpG", ".jpg" }, input.Extensions);
        Assert.True(ExtensionMatcher.Matches(rule, "PHOTO.PNG"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Rejects_missing_names(string? name) =>
        AssertInvalid(ValidRule() with { Name = name! }, nameof(FileRule.Name));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..png")]
    [InlineData("*.png")]
    [InlineData("p?g")]
    [InlineData(".tar.gz")]
    [InlineData(".png,.jpg")]
    [InlineData(".png;.jpg")]
    [InlineData(".p ng")]
    [InlineData(".png/jpg")]
    [InlineData(@".png\jpg")]
    [InlineData(".png:stream")]
    [InlineData(".p<g")]
    [InlineData(".p>g")]
    [InlineData(".p|g")]
    [InlineData(".p\"g")]
    [InlineData(".p\0g")]
    [InlineData(null)]
    public void Rejects_invalid_extension_entries_instead_of_dropping_them(string? extension) =>
        AssertInvalid(ValidRule() with { Extensions = new[] { ".jpg", extension! } }, nameof(FileRule.Extensions));

    [Fact]
    public void Rejects_an_empty_extension_list() =>
        AssertInvalid(ValidRule() with { Extensions = Array.Empty<string>() }, nameof(FileRule.Extensions));

    [Fact]
    public void Rejects_a_null_extension_list() =>
        AssertInvalid(ValidRule() with { Extensions = null! }, nameof(FileRule.Extensions));

    [Fact]
    public void Rejects_unknown_actions() =>
        AssertInvalid(ValidRule() with { Action = (FileAction)99 }, nameof(FileRule.Action));

    [Fact]
    public void Rejects_an_empty_rule_id() =>
        AssertInvalid(ValidRule() with { Id = Guid.Empty }, nameof(FileRule.Id));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    [InlineData("Downloads")]
    [InlineData(@"C:Downloads")]
    [InlineData(@"\Downloads")]
    [InlineData(@"\\server\share\Downloads")]
    [InlineData(@"\\?\C:\Downloads")]
    [InlineData(@"\\.\C:\Downloads")]
    [InlineData(@"%USERPROFILE%\Downloads")]
    [InlineData(@"C:\Downloads\*")]
    [InlineData(@"C:\Downloads\?")]
    [InlineData(@"C:\Downloads:stream")]
    [InlineData(@"C:\Down<loads")]
    [InlineData(@"C:\Down>loads")]
    [InlineData(@"C:\Down|loads")]
    [InlineData("C:\\Down\"loads")]
    [InlineData("C:\\Down\0loads")]
    [InlineData(@"C:\Downloads.\Images")]
    [InlineData(@"C:\Downloads \Images")]
    [InlineData(@"C:\CON\Images")]
    [InlineData(@"C:\nul.txt")]
    [InlineData(@"C:\COM1\Images")]
    [InlineData(@"C:\lpt9\Images")]
    [InlineData(@"C:\COM¹\Images")]
    [InlineData(@"C:\COM².txt")]
    [InlineData(@"C:\COM³")]
    [InlineData(@"C:\LPT¹")]
    [InlineData(@"C:\LPT².txt")]
    [InlineData(@"C:\LPT³\Images")]
    public void Rejects_invalid_paths_for_either_directory(string? path)
    {
        AssertInvalid(ValidRule() with { SourceDirectory = path! }, nameof(FileRule.SourceDirectory));
        AssertInvalid(ValidRule() with { DestinationDirectory = path! }, nameof(FileRule.DestinationDirectory));
    }

    [Theory]
    [InlineData(@"c:\fileflow-test-source\")]
    [InlineData("C:/FileFlow-test-source/.")]
    [InlineData(@"C:\FileFlow-test-source\child\..")]
    public void Rejects_equivalent_source_and_destination_paths(string destination) =>
        AssertInvalid(ValidRule() with { DestinationDirectory = destination }, nameof(FileRule.DestinationDirectory));

    [Theory]
    [InlineData(@"C:\", @"D:\")]
    [InlineData(@"C:\Photos", @"C:\Photos\Sorted")]
    [InlineData(@"C:\Photos", @"C:\Photos-old")]
    [InlineData(@"C:\Пользователи\Мои фото", @"D:\Pictures")]
    public void Accepts_distinct_local_paths(string source, string destination)
    {
        var result = RuleValidator.Validate(ValidRule() with
        {
            SourceDirectory = source,
            DestinationDirectory = destination
        });

        Assert.True(result.IsValid);
        Assert.Equal(source, result.Rule!.SourceDirectory);
        Assert.Equal(destination, result.Rule.DestinationDirectory);
    }

    [Fact]
    public void Returns_all_field_errors_without_a_usable_rule()
    {
        var result = RuleValidator.Validate(ValidRule() with
        {
            Name = "", SourceDirectory = "", DestinationDirectory = "", Extensions = Array.Empty<string>()
        });

        Assert.False(result.IsValid);
        Assert.Null(result.Rule);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(FileRule.Name));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(FileRule.SourceDirectory));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(FileRule.DestinationDirectory));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(FileRule.Extensions));
        Assert.All(result.Errors, error => Assert.False(string.IsNullOrWhiteSpace(error.Message)));
    }

    [Fact]
    public void Normalized_extensions_do_not_share_the_editors_mutable_list()
    {
        var extensions = new List<string> { "png" };
        var result = RuleValidator.Validate(ValidRule() with { Extensions = extensions });

        extensions[0] = "exe";

        Assert.Equal(new[] { ".png" }, result.Rule!.Extensions);
    }

    private static void AssertInvalid(FileRule input, string propertyName)
    {
        var result = RuleValidator.Validate(input);

        Assert.False(result.IsValid);
        Assert.Null(result.Rule);
        Assert.Contains(result.Errors, error => error.PropertyName == propertyName && !string.IsNullOrWhiteSpace(error.Message));
    }
}
