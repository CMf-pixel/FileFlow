namespace FileFlow.Core.Preview;

// This boundary deliberately cannot open streams, persist rules, or mutate the filesystem.
internal interface IPreviewFileSystem
{
    DriveType GetDriveType(string path);
    FileAttributes GetAttributes(string path);
    IEnumerable<string> EnumerateEntries(string directory);
    SourceFileMetadata ReadSourceMetadata(string path);
}

internal readonly record struct SourceFileMetadata(long Length, DateTime LastWriteUtc);
