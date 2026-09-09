using FileFlow.Core.Rules;

namespace FileFlow.Core.Preview;

/// <summary>A proposed operation and source metadata observed during preview; no execution is performed.</summary>
public sealed record PlannedOperation(
    string SourcePath,
    string DestinationPath,
    FileAction Action,
    long SourceLength,
    DateTime SourceLastWriteUtc);
