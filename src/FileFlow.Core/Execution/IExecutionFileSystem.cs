using FileFlow.Core.Preview;

namespace FileFlow.Core.Execution;

// Separate from preview: no enumeration, streams, delete, directory creation, or overwrite option.
internal interface IExecutionFileSystem
{
    DriveType GetDriveType(string path);
    FileAttributes GetAttributes(string path);
    SourceFileMetadata ReadSourceMetadata(string path);
    void CopyNoOverwrite(string source, string destination);
    void MoveNoOverwrite(string source, string destination);
}
