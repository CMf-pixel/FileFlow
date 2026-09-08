using FileFlow.Core.Rules;
using Xunit;

namespace FileFlow.Core.Tests;

public sealed class ExtensionMatcherTests
{
    [Theory]
    [InlineData("photo.png", true)]
    [InlineData("PHOTO.PNG", true)]
    [InlineData("wallpaper.jpg", true)]
    [InlineData("photo.jpeg", false)]
    [InlineData("photo.png.backup", false)]
    [InlineData("photo", false)]
    [InlineData("photo.", false)]
    [InlineData("", false)]
    [InlineData(@"C:\missing\folder.png\notes.txt", false)]
    [InlineData(@"C:\missing\фото с пробелами.JPG", true)]
    public void Matches_any_listed_extension_without_accessing_the_file(string path, bool expected)
    {
        var rule = new FileRule { Extensions = new[] { ".png", ".jpg" } };

        Assert.Equal(expected, ExtensionMatcher.Matches(rule, path));
    }

    [Theory]
    [InlineData("archive.tar.gz", ".gz", true)]
    [InlineData("archive.tar.gz", ".tar", false)]
    public void Matches_only_the_final_extension(string path, string extension, bool expected)
    {
        var rule = new FileRule { Extensions = new[] { extension } };

        Assert.Equal(expected, ExtensionMatcher.Matches(rule, path));
    }
}
