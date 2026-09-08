namespace FileFlow.Core.Persistence;

public enum RulePersistenceErrorCode
{
    InvalidData,
    UnsupportedSchemaVersion,
    IoError
}

public sealed record RulePersistenceError(RulePersistenceErrorCode Code, string Message);
