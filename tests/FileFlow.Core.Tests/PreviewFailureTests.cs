using System.Security;
using FileFlow.Core.Preview;
using FileFlow.Core.Rules;
using Xunit;

namespace FileFlow.Core.Tests;

public sealed class PreviewFailureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Enumeration_failure_is_incomplete_and_retains_discovered_operations(bool conflict)
    {
        var fs = new PreviewFileSystemFake { FailEnumeration = true };
        fs.AddFile("cat.png");
        if (conflict) fs.Attributes[@"C:\preview-destination\cat.png"] = FileAttributes.Normal;
        var result = new FileRulePreviewer(fs).CreatePreview(fs.Rule);
        Assert.Equal(PreviewStatus.Incomplete, result.Status);
        Assert.False(result.CanExecute);
        Assert.Single(result.Operations);
        Assert.Contains(result.Issues, issue => issue.Code == PreviewIssueCode.EnumerationFailed);
        Assert.Equal(conflict, result.Issues.Any(issue => issue.Code == PreviewIssueCode.DestinationExists));
    }

    [Fact]
    public void Empty_incomplete_scan_is_not_no_matches()
    {
        var fs = new PreviewFileSystemFake { FailEnumeration = true };
        var result = new FileRulePreviewer(fs).CreatePreview(fs.Rule);
        Assert.Equal(PreviewStatus.Incomplete, result.Status);
        Assert.Empty(result.Operations);
        Assert.False(result.CanExecute);
    }

    public static IEnumerable<object[]> InspectionErrors()
    {
        yield return new object[] { new UnauthorizedAccessException("denied") };
        yield return new object[] { new IOException("unreadable") };
        yield return new object[] { new DirectoryNotFoundException("gone") };
        yield return new object[] { new FileNotFoundException("gone") };
        yield return new object[] { new SecurityException("denied") };
        yield return new object[] { new ArgumentException("invalid") };
        yield return new object[] { new NotSupportedException("unsupported") };
    }

    [Theory]
    [MemberData(nameof(InspectionErrors))]
    public void Source_metadata_failure_is_incomplete_and_other_files_are_retained(Exception error)
    {
        var fs = new PreviewFileSystemFake();
        fs.AddFile("bad.png");
        fs.AddFile("good.jpg");
        fs.MetadataErrors[@"C:\preview-source\bad.png"] = error;
        var result = new FileRulePreviewer(fs).CreatePreview(fs.Rule);
        Assert.Equal(PreviewStatus.Incomplete, result.Status);
        Assert.Equal(@"C:\preview-source\good.jpg", Assert.Single(result.Operations).SourcePath);
        Assert.Contains(result.Issues, issue => issue.Code == PreviewIssueCode.InspectionFailed && issue.Path == @"C:\preview-source\bad.png");
        Assert.False(result.CanExecute);
    }

    [Theory]
    [InlineData(@"C:\preview-source")]
    [InlineData(@"C:\preview-destination")]
    [InlineData(@"C:\")]
    public void Inaccessible_directory_or_ancestor_is_incomplete_instead_of_missing(string path)
    {
        var fs = new PreviewFileSystemFake();
        fs.AttributeErrors[path] = new UnauthorizedAccessException("denied");
        var result = new FileRulePreviewer(fs).CreatePreview(fs.Rule);
        Assert.Equal(PreviewStatus.Incomplete, result.Status);
        Assert.Contains(result.Issues, issue => issue.Code == PreviewIssueCode.InspectionFailed && issue.Path == path);
        Assert.False(result.CanExecute);
    }

    [Fact]
    public void Incomplete_inspection_takes_precedence_over_confirmed_preflight_block()
    {
        var fs = new PreviewFileSystemFake();
        fs.Attributes.Remove(fs.Rule.SourceDirectory);
        fs.AttributeErrors[fs.Rule.DestinationDirectory] = new IOException("unreadable");
        var result = new FileRulePreviewer(fs).CreatePreview(fs.Rule);
        Assert.Equal(PreviewStatus.Incomplete, result.Status);
        Assert.Contains(result.Issues, issue => issue.Code == PreviewIssueCode.DirectoryMissing);
        Assert.Contains(result.Issues, issue => issue.Code == PreviewIssueCode.InspectionFailed);
    }

    [Theory]
    [InlineData(@"C:\preview-source")]
    [InlineData(@"C:\preview-destination")]
    [InlineData(@"C:\")]
    public void Reparse_directory_or_ancestor_blocks_without_traversal(string path)
    {
        var fs = new PreviewFileSystemFake();
        fs.Attributes[path] = FileAttributes.Directory | FileAttributes.ReparsePoint;
        var result = new FileRulePreviewer(fs).CreatePreview(fs.Rule);
        Assert.Equal(PreviewStatus.Blocked, result.Status);
        Assert.Contains(result.Issues, issue => issue.Code == PreviewIssueCode.UnsupportedReparsePoint && issue.Path == path);
        Assert.Empty(result.Operations);
        Assert.Empty(fs.EnumeratedDirectories);
        Assert.DoesNotContain(fs.AttributeReads, p => p != path && p.StartsWith(path.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("link.png", FileAttributes.ReparsePoint)]
    [InlineData("link.txt", FileAttributes.ReparsePoint)]
    [InlineData("subdir", FileAttributes.ReparsePoint | FileAttributes.Directory)]
    public void Direct_reparse_entries_block_even_when_nonmatching(string name, FileAttributes attributes)
    {
        var fs = new PreviewFileSystemFake();
        fs.AddFile(name, attributes);
        var result = new FileRulePreviewer(fs).CreatePreview(fs.Rule);
        Assert.Equal(PreviewStatus.Blocked, result.Status);
        Assert.Empty(result.Operations);
        Assert.Contains(result.Issues, issue => issue.Code == PreviewIssueCode.UnsupportedReparsePoint);
        Assert.Equal(new[] { fs.Rule.SourceDirectory }, fs.EnumeratedDirectories);
    }

    [Theory]
    [InlineData(FileAttributes.Normal)]
    [InlineData(FileAttributes.Directory)]
    [InlineData(FileAttributes.ReparsePoint)]
    public void Destination_occupants_block(FileAttributes attributes)
    {
        var fs = new PreviewFileSystemFake();
        fs.AddFile("cat.png");
        fs.Attributes[@"C:\preview-destination\cat.png"] = attributes;
        var result = new FileRulePreviewer(fs).CreatePreview(fs.Rule);
        Assert.Equal(PreviewStatus.Blocked, result.Status);
        Assert.Single(result.Operations);
        Assert.False(result.CanExecute);
    }

    [Fact]
    public void Failed_destination_inspection_retains_operation_but_blocks_as_incomplete()
    {
        var fs = new PreviewFileSystemFake();
        fs.AddFile("cat.png");
        fs.AttributeErrors[@"C:\preview-destination\cat.png"] = new UnauthorizedAccessException("denied");
        var result = new FileRulePreviewer(fs).CreatePreview(fs.Rule);
        Assert.Equal(PreviewStatus.Incomplete, result.Status);
        Assert.Single(result.Operations);
        Assert.Contains(result.Issues, issue => issue.Code == PreviewIssueCode.InspectionFailed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Missing_destination_parent_is_incomplete_but_missing_final_name_is_free(bool parentMissing)
    {
        var fs = new PreviewFileSystemFake();
        fs.AddFile("cat.png");
        fs.AttributeErrors[@"C:\preview-destination\cat.png"] = parentMissing
            ? new DirectoryNotFoundException("parent vanished") : new FileNotFoundException("name is free");
        var result = new FileRulePreviewer(fs).CreatePreview(fs.Rule);
        Assert.Equal(parentMissing ? PreviewStatus.Incomplete : PreviewStatus.Ready, result.Status);
        Assert.Equal(!parentMissing, result.CanExecute);
        Assert.Single(result.Operations);
        Assert.Equal(parentMissing, result.Issues.Any(issue => issue.Code == PreviewIssueCode.InspectionFailed));
    }

    [Fact]
    public void Unreadable_nonmatching_entry_makes_preview_incomplete_and_retains_other_operations()
    {
        var fs = new PreviewFileSystemFake();
        fs.AddFile("unreadable.txt");
        fs.AddFile("cat.png");
        fs.AttributeErrors[@"C:\preview-source\unreadable.txt"] = new UnauthorizedAccessException("denied");
        var result = new FileRulePreviewer(fs).CreatePreview(fs.Rule);
        Assert.Equal(PreviewStatus.Incomplete, result.Status);
        Assert.False(result.CanExecute);
        Assert.Equal(@"C:\preview-source\cat.png", Assert.Single(result.Operations).SourcePath);
        Assert.Contains(result.Issues, issue => issue.Code == PreviewIssueCode.InspectionFailed && issue.Path == @"C:\preview-source\unreadable.txt");
    }

    [Fact]
    public void Vanished_source_entry_is_not_silently_ignored()
    {
        var fs = new PreviewFileSystemFake();
        fs.AddFile("cat.png");
        fs.Attributes.Remove(@"C:\preview-source\cat.png");
        var result = new FileRulePreviewer(fs).CreatePreview(fs.Rule);
        Assert.Equal(PreviewStatus.Incomplete, result.Status);
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public void Mapped_network_drive_is_rejected_before_enumeration()
    {
        var fs = new PreviewFileSystemFake { DriveType = DriveType.Network };
        var result = new FileRulePreviewer(fs).CreatePreview(fs.Rule);
        Assert.Equal(PreviewStatus.Blocked, result.Status);
        Assert.Contains(result.Issues, issue => issue.Code == PreviewIssueCode.UnsupportedNetworkPath);
        Assert.Empty(fs.EnumeratedDirectories);
    }

    [Fact]
    public void Case_equivalent_destination_names_block_and_have_deterministic_tie_order()
    {
        var fs = new PreviewFileSystemFake();
        fs.AddFile("cat.png");
        fs.AddFile("CAT.png");
        var result = new FileRulePreviewer(fs).CreatePreview(fs.Rule);
        Assert.Equal(PreviewStatus.Blocked, result.Status);
        Assert.Equal(new[] { @"C:\preview-source\CAT.png", @"C:\preview-source\cat.png" }, result.Operations.Select(o => o.SourcePath));
        Assert.Contains(result.Issues, issue => issue.Code == PreviewIssueCode.DuplicateDestination);
    }

    [Fact]
    public void Invalid_rule_does_not_access_filesystem()
    {
        var fs = new PreviewFileSystemFake();
        var result = new FileRulePreviewer(fs).CreatePreview(fs.Rule with { Name = "", Extensions = null! });
        Assert.Equal(PreviewStatus.Blocked, result.Status);
        Assert.Null(result.Rule);
        Assert.Empty(fs.AttributeReads);
        Assert.Empty(fs.EnumeratedDirectories);
        Assert.All(result.Issues, issue => Assert.Equal(PreviewIssueCode.InvalidRule, issue.Code));
    }

    [Fact]
    public void Adapter_contract_exposes_only_inspection_without_stream_or_mutation_access()
    {
        var methods = typeof(IPreviewFileSystem).GetMethods();
        Assert.Equal(new[] { "EnumerateEntries", "GetAttributes", "GetDriveType", "ReadSourceMetadata" },
            methods.Select(method => method.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.All(methods, method => Assert.DoesNotContain(method.GetParameters(), p => typeof(Stream).IsAssignableFrom(p.ParameterType)));
        Assert.All(methods, method => Assert.False(typeof(Stream).IsAssignableFrom(method.ReturnType)));
    }
}

internal sealed class PreviewFileSystemFake : IPreviewFileSystem
{
    public FileRule Rule { get; } = new()
    {
        Name = "Pictures", SourceDirectory = @"C:\preview-source", DestinationDirectory = @"C:\preview-destination",
        Extensions = new[] { ".png", ".jpg" }, Action = FileAction.Move
    };
    public Dictionary<string, FileAttributes> Attributes { get; } = new(StringComparer.Ordinal)
    {
        [@"C:\"] = FileAttributes.Directory,
        [@"C:\preview-source"] = FileAttributes.Directory,
        [@"C:\preview-destination"] = FileAttributes.Directory
    };
    public Dictionary<string, Exception> AttributeErrors { get; } = new();
    public Dictionary<string, Exception> MetadataErrors { get; } = new();
    public List<string> Entries { get; } = new();
    public List<string> AttributeReads { get; } = new();
    public List<string> EnumeratedDirectories { get; } = new();
    public bool FailEnumeration { get; init; }
    public DriveType DriveType { get; init; } = DriveType.Fixed;

    public void AddFile(string name, FileAttributes attributes = FileAttributes.Normal)
    {
        var path = Path.Combine(Rule.SourceDirectory, name);
        Entries.Add(path);
        Attributes[path] = attributes;
    }

    public FileAttributes GetAttributes(string path)
    {
        AttributeReads.Add(path);
        if (AttributeErrors.TryGetValue(path, out var error)) throw error;
        return Attributes.TryGetValue(path, out var attributes) ? attributes : throw new FileNotFoundException("missing");
    }

    public IEnumerable<string> EnumerateEntries(string directory)
    {
        EnumeratedDirectories.Add(directory);
        foreach (var path in Entries) yield return path;
        if (FailEnumeration) throw new IOException("interrupted enumeration");
    }

    public SourceFileMetadata ReadSourceMetadata(string path)
    {
        if (MetadataErrors.TryGetValue(path, out var error)) throw error;
        return new(17, new DateTime(2026, 1, 2, 3, 4, 6, DateTimeKind.Utc));
    }

    public DriveType GetDriveType(string path) => DriveType;
}
