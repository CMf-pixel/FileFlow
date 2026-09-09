namespace FileFlow.Core.Preview;

public enum PreviewIssueCode
{
    InvalidRule,
    DirectoryMissing,
    NotDirectory,
    UnsupportedNetworkPath,
    UnsupportedReparsePoint,
    DestinationExists,
    DuplicateDestination,
    EnumerationFailed,
    InspectionFailed
}

/// <summary>Every preview issue blocks the entire batch.</summary>
public sealed record PreviewIssue(PreviewIssueCode Code, string Message, string? Path = null, string? PropertyName = null);
