using FileFlow.Core.Preview;
using FileFlow.Core.Rules;
using Xunit;

namespace FileFlow.Core.Tests;

public sealed class PreviewTests
{
    [Theory]
    [InlineData(FileAction.Move)]
    [InlineData(FileAction.Copy)]
    public void Plans_exact_ordered_operations_and_metadata_without_changing_any_files(FileAction action)
    {
        using var fixture = new PreviewDirectory();
        fixture.Write("zebra.jpg", "second");
        fixture.Write("Alpha.PNG", "first");
        fixture.Write("ignore.txt", "untouched");
        fixture.Write("README", "extensionless");
        Directory.CreateDirectory(Path.Combine(fixture.Source, "nested.png"));
        fixture.Write("nested.png/hidden.png", "nested");
        File.WriteAllText(Path.Combine(fixture.Destination, "existing.txt"), "keep");
        var timestamp = new DateTime(2026, 1, 2, 3, 4, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path.Combine(fixture.Source, "Alpha.PNG"), timestamp);
        var before = fixture.Snapshot();

        var result = new FileRulePreviewer().CreatePreview(fixture.Rule with { Action = action });

        Assert.Equal(PreviewStatus.Ready, result.Status);
        Assert.True(result.CanExecute);
        Assert.Empty(result.Issues);
        Assert.Collection(result.Operations,
            operation => Assert.Equal(new PlannedOperation(Path.Combine(fixture.Source, "Alpha.PNG"),
                Path.Combine(fixture.Destination, "Alpha.PNG"), action, 5, timestamp), operation),
            operation =>
            {
                Assert.Equal(Path.Combine(fixture.Source, "zebra.jpg"), operation.SourcePath);
                Assert.Equal(Path.Combine(fixture.Destination, "zebra.jpg"), operation.DestinationPath);
                Assert.Equal(action, operation.Action);
                Assert.Equal(6, operation.SourceLength);
                Assert.Equal(File.GetLastWriteTimeUtc(operation.SourcePath), operation.SourceLastWriteUtc);
                Assert.Equal(DateTimeKind.Utc, operation.SourceLastWriteUtc.Kind);
            });
        Assert.Equal(before, fixture.Snapshot());
        Assert.True(File.Exists(Path.Combine(fixture.Source, "Alpha.PNG")));
        Assert.True(File.Exists(Path.Combine(fixture.Source, "zebra.jpg")));
    }

    [Fact]
    public void Single_extension_matches_case_insensitively()
    {
        using var fixture = new PreviewDirectory();
        fixture.Write("photo.PNG", "a");
        fixture.Write("other.jpg", "b");
        var result = new FileRulePreviewer().CreatePreview(fixture.Rule with { Extensions = new[] { "png" } });
        Assert.Equal("photo.PNG", Path.GetFileName(Assert.Single(result.Operations).SourcePath));
        Assert.Equal(PreviewStatus.Ready, result.Status);
    }

    [Fact]
    public void No_matches_is_readable_but_not_executable_and_does_not_change_files()
    {
        using var fixture = new PreviewDirectory();
        fixture.Write("README", "keep");
        fixture.Write("other.txt", "keep");
        var before = fixture.Snapshot();
        var result = new FileRulePreviewer().CreatePreview(fixture.Rule);
        Assert.Equal(PreviewStatus.NoMatches, result.Status);
        Assert.False(result.CanExecute);
        Assert.Empty(result.Operations);
        Assert.Empty(result.Issues);
        Assert.Equal(before, fixture.Snapshot());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Missing_directory_blocks_without_creating_it(bool sourceMissing)
    {
        using var fixture = new PreviewDirectory();
        var missing = Path.Combine(fixture.Root, "missing");
        var rule = sourceMissing ? fixture.Rule with { SourceDirectory = missing }
            : fixture.Rule with { DestinationDirectory = missing };
        var before = fixture.Snapshot();
        var result = new FileRulePreviewer().CreatePreview(rule);
        Assert.Equal(PreviewStatus.Blocked, result.Status);
        Assert.False(result.CanExecute);
        Assert.Contains(result.Issues, issue => issue.Code == PreviewIssueCode.DirectoryMissing && issue.Path == missing);
        Assert.Equal(before, fixture.Snapshot());
        Assert.False(Directory.Exists(missing));
    }

    [Theory]
    [InlineData(false, FileAction.Move)]
    [InlineData(true, FileAction.Move)]
    [InlineData(false, FileAction.Copy)]
    [InlineData(true, FileAction.Copy)]
    public void Existing_destination_blocks_entire_batch_and_leaves_everything_unchanged(bool directory, FileAction action)
    {
        using var fixture = new PreviewDirectory();
        fixture.Write("cat.png", "source");
        fixture.Write("free.jpg", "also source");
        var occupied = Path.Combine(fixture.Destination, "cat.png");
        if (directory) Directory.CreateDirectory(occupied);
        else File.WriteAllText(occupied, "destination");
        var before = fixture.Snapshot();
        var result = new FileRulePreviewer().CreatePreview(fixture.Rule with { Action = action });
        Assert.Equal(PreviewStatus.Blocked, result.Status);
        Assert.False(result.CanExecute);
        Assert.Equal(2, result.Operations.Count);
        Assert.Contains(result.Issues, issue => issue.Code == PreviewIssueCode.DestinationExists && issue.Path == occupied);
        Assert.Equal(before, fixture.Snapshot());
    }

    [Fact]
    public void Snapshot_is_normalized_and_cannot_be_changed_through_the_input_or_returned_collections()
    {
        using var fixture = new PreviewDirectory();
        fixture.Write("cat.png", "cat");
        var extensions = new List<string> { " PNG " };
        var rule = fixture.Rule with { Name = "  Pictures  ", Extensions = extensions };
        var result = new FileRulePreviewer().CreatePreview(rule);
        extensions[0] = "exe";
        Assert.Equal("Pictures", result.Rule!.Name);
        Assert.Equal(new[] { ".png" }, result.Rule.Extensions);
        Assert.Equal("  Pictures  ", rule.Name);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)result.Rule.Extensions)[0] = ".exe");
        Assert.Throws<NotSupportedException>(() => ((IList<PlannedOperation>)result.Operations).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<PreviewIssue>)result.Issues).Add(
            new PreviewIssue(PreviewIssueCode.InvalidRule, "injected")));
    }

    [Fact]
    public void Equivalent_paths_are_rejected_after_normalization()
    {
        using var fixture = new PreviewDirectory();
        var result = new FileRulePreviewer().CreatePreview(fixture.Rule with
        { DestinationDirectory = fixture.Source.ToUpperInvariant() + @"\child\..\" });
        Assert.Equal(PreviewStatus.Blocked, result.Status);
        Assert.False(result.CanExecute);
        Assert.Null(result.Rule);
        Assert.Contains(result.Issues, issue => issue.PropertyName == nameof(FileRule.DestinationDirectory));
    }

    [Theory]
    [InlineData(@"\\server\share\files")]
    [InlineData(@"\\?\C:\files")]
    [InlineData(@"\\.\C:\files")]
    [InlineData("relative")]
    [InlineData("C:\\bad\0path")]
    public void Unsupported_or_invalid_paths_return_validation_issues(string path)
    {
        using var fixture = new PreviewDirectory();
        foreach (var rule in new[] { fixture.Rule with { SourceDirectory = path }, fixture.Rule with { DestinationDirectory = path } })
        {
            var result = new FileRulePreviewer().CreatePreview(rule);
            Assert.Equal(PreviewStatus.Blocked, result.Status);
            Assert.False(result.CanExecute);
            Assert.NotEmpty(result.Issues);
            Assert.Empty(result.Operations);
        }
    }
}

internal sealed class PreviewDirectory : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "FileFlow-preview-tests-" + Guid.NewGuid().ToString("N"));
    public string Source => Path.Combine(Root, "source");
    public string Destination => Path.Combine(Root, "destination");
    public FileRule Rule => new()
    {
        Name = "Pictures", SourceDirectory = Source, DestinationDirectory = Destination,
        Extensions = new[] { ".png", ".jpg" }, Action = FileAction.Move
    };

    public PreviewDirectory()
    {
        Directory.CreateDirectory(Source);
        Directory.CreateDirectory(Destination);
    }

    public void Write(string name, string contents) => File.WriteAllText(Path.Combine(Source, name), contents);

    public string[] Snapshot() => Directory.EnumerateFileSystemEntries(Root, "*", SearchOption.AllDirectories)
        .Select(path => Directory.Exists(path) ? "D:" + Path.GetRelativePath(Root, path)
            : "F:" + Path.GetRelativePath(Root, path) + ":" + Convert.ToBase64String(File.ReadAllBytes(path)))
        .OrderBy(entry => entry, StringComparer.Ordinal).ToArray();

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
