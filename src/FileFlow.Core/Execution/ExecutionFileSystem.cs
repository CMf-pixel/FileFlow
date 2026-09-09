using FileFlow.Core.Preview;

namespace FileFlow.Core.Execution;

internal sealed class ExecutionFileSystem : IExecutionFileSystem
{
    private readonly PreviewFileSystem inspection = new();

    public DriveType GetDriveType(string path) => inspection.GetDriveType(path);
    public FileAttributes GetAttributes(string path) => inspection.GetAttributes(path);
    public SourceFileMetadata ReadSourceMetadata(string path) => inspection.ReadSourceMetadata(path);
    public void CopyNoOverwrite(string source, string destination) => File.Copy(source, destination, overwrite: false);
    public void MoveNoOverwrite(string source, string destination) => File.Move(source, destination, overwrite: false);
}
