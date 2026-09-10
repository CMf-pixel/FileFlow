using FileFlow.Core.Persistence;

namespace FileFlow.App.Presentation;

public static class CoreMessageFormatter
{
    public static string Persistence(RulePersistenceError error) => (error.Code switch
    {
        RulePersistenceErrorCode.InvalidData => "The saved rules contain invalid data. The file has been preserved.",
        RulePersistenceErrorCode.UnsupportedSchemaVersion => "This version of FileFlow cannot read the saved rules. The file has been preserved.",
        _ => "FileFlow could not access the rules file. Check its location and permissions, then try again."
    }) + "\n" + error.Message;
}
