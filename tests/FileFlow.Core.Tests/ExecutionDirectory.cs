using FileFlow.Core.Rules;

namespace FileFlow.Core.Tests;

internal sealed class ExecutionDirectory : IDisposable
{
    private const string Prefix = "FileFlow-execution-tests-";
    public string Root { get; } = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString("N"));
    public string Source => Path.Combine(Root, "source");
    public string Destination => Path.Combine(Root, "destination");
    public FileRule Rule => new()
    {
        Name = "Execution", SourceDirectory = Source, DestinationDirectory = Destination,
        Extensions = new[] { ".png", ".jpg" }, Action = FileAction.Copy
    };

    public ExecutionDirectory()
    {
        Directory.CreateDirectory(Source);
        Directory.CreateDirectory(Destination);
    }

    public string Write(string name, string contents = "file contents")
    {
        var path = Path.Combine(Source, name);
        File.WriteAllText(path, contents);
        return path;
    }

    public string[] Snapshot() => Directory.EnumerateFileSystemEntries(Root, "*", SearchOption.AllDirectories)
        .Select(path => Directory.Exists(path) ? "D:" + Path.GetRelativePath(Root, path)
            : "F:" + Path.GetRelativePath(Root, path) + ":" + Convert.ToBase64String(File.ReadAllBytes(path)))
        .OrderBy(entry => entry, StringComparer.Ordinal).ToArray();

    public void Dispose()
    {
        var root = Path.GetFullPath(Root);
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        if (!string.Equals(Path.GetDirectoryName(root), temp, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(root).StartsWith(Prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing cleanup outside the owned temporary directory.");
        Directory.Delete(root, recursive: true);
    }
}
