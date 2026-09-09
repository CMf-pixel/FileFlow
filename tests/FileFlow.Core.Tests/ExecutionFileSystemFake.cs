using FileFlow.Core.Execution;
using FileFlow.Core.Preview;
using FileFlow.Core.Rules;

namespace FileFlow.Core.Tests;

// Virtual paths only. Hooks model races and failures without touching user files or real drives.
internal sealed class ExecutionFileSystemFake : IExecutionFileSystem, IPreviewFileSystem
{
    public FileRule Rule { get; set; } = new()
    {
        Name = "Execution", SourceDirectory = @"C:\execution-source", DestinationDirectory = @"C:\execution-destination",
        Extensions = new[] { ".png", ".jpg" }, Action = FileAction.Copy
    };
    public Dictionary<string, FileAttributes> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, SourceFileMetadata> Metadata { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, DriveType> Drives { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(string Method, string Path)> Calls { get; } = new();
    public List<(FileAction Action, string Source, string Destination)> Mutations { get; } = new();
    public Action<string, string>? BeforeRead { get; set; }
    public Action<FileAction, string, string>? BeforeMutation { get; set; }
    public Action<FileAction, string, string>? AfterMutation { get; set; }
    public bool LeaveMoveSource { get; set; }

    public string Source(string name) => Path.Combine(Rule.SourceDirectory, name);
    public string Destination(string name) => Path.Combine(Rule.DestinationDirectory, name);

    public OperationPreview Preview(params string[] names)
    {
        foreach (var directory in new[] { Rule.SourceDirectory, Rule.DestinationDirectory })
            for (string? path = directory; path is not null; path = Path.GetDirectoryName(path))
                Attributes[path] = FileAttributes.Directory;
        foreach (var name in names) AddFile(Source(name));
        var preview = new FileRulePreviewer(this).CreatePreview(Rule);
        Calls.Clear();
        return preview;
    }

    public void AddFile(string path)
    {
        Attributes[path] = FileAttributes.Normal;
        Metadata[path] = new(17, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    private void Read(string method, string path)
    {
        Calls.Add((method, path));
        BeforeRead?.Invoke(method, path);
    }

    public FileAttributes GetAttributes(string path)
    {
        Read("attributes", path);
        if (Attributes.TryGetValue(path, out var attributes)) return attributes;
        if (Path.GetDirectoryName(path) is { } parent && !Attributes.ContainsKey(parent))
            throw new DirectoryNotFoundException("parent missing");
        throw new FileNotFoundException("entry missing", path);
    }

    public DriveType GetDriveType(string path)
    {
        Read("drive", path);
        return Drives.GetValueOrDefault(Path.GetPathRoot(path)!, DriveType.Fixed);
    }

    public SourceFileMetadata ReadSourceMetadata(string path)
    {
        Read("metadata", path);
        return Metadata.TryGetValue(path, out var metadata) ? metadata : throw new FileNotFoundException("metadata missing", path);
    }

    public IEnumerable<string> EnumerateEntries(string directory)
    {
        Read("enumerate", directory);
        return Attributes.Keys.Where(path => string.Equals(Path.GetDirectoryName(path), directory,
            StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    public void CopyNoOverwrite(string source, string destination) => Mutate(FileAction.Copy, source, destination);
    public void MoveNoOverwrite(string source, string destination) => Mutate(FileAction.Move, source, destination);

    private void Mutate(FileAction action, string source, string destination)
    {
        Calls.Add(("mutate", source));
        Mutations.Add((action, source, destination));
        BeforeMutation?.Invoke(action, source, destination);
        if (Attributes.ContainsKey(destination)) throw new IOException("destination exists");
        if (!Attributes.ContainsKey(source)) throw new FileNotFoundException("source missing", source);
        Attributes[destination] = Attributes[source];
        Metadata[destination] = Metadata[source];
        if (action == FileAction.Move && !LeaveMoveSource)
        {
            Attributes.Remove(source);
            Metadata.Remove(source);
        }
        AfterMutation?.Invoke(action, source, destination);
    }
}

// A real-filesystem decorator for proving mutation counts and injecting races inside owned temp folders.
internal sealed class ObservedExecutionFileSystem : IExecutionFileSystem
{
    private readonly ExecutionFileSystem inner = new();
    public int MutationCount { get; private set; }
    public Action<string, string>? BeforeMutation { get; set; }
    public DriveType GetDriveType(string path) => inner.GetDriveType(path);
    public FileAttributes GetAttributes(string path) => inner.GetAttributes(path);
    public SourceFileMetadata ReadSourceMetadata(string path) => inner.ReadSourceMetadata(path);

    public void CopyNoOverwrite(string source, string destination)
    {
        MutationCount++;
        BeforeMutation?.Invoke(source, destination);
        inner.CopyNoOverwrite(source, destination);
    }

    public void MoveNoOverwrite(string source, string destination)
    {
        MutationCount++;
        BeforeMutation?.Invoke(source, destination);
        inner.MoveNoOverwrite(source, destination);
    }
}
