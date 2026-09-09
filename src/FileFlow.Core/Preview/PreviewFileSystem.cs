namespace FileFlow.Core.Preview;

internal sealed class PreviewFileSystem : IPreviewFileSystem
{
    public DriveType GetDriveType(string path) => new DriveInfo(Path.GetPathRoot(path)!).DriveType;

    public FileAttributes GetAttributes(string path) => File.GetAttributes(path);

    public IEnumerable<string> EnumerateEntries(string directory) => Directory.EnumerateFileSystemEntries(
        directory, "*", new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false
        });

    public SourceFileMetadata ReadSourceMetadata(string path)
    {
        var file = new FileInfo(path);
        file.Refresh();
        // Length throws if the entry has disappeared, instead of returning a fabricated empty file.
        return new(file.Length, file.LastWriteTimeUtc);
    }
}
