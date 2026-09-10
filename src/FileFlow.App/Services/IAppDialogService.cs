using FileFlow.App.ViewModels;

namespace FileFlow.App.Services;

public interface IAppDialogService
{
    void ShowRuleEditor(RuleEditorViewModel editor);
    void ShowRun(RunViewModel run);
    bool ConfirmDelete(string ruleName);
    string? PickFolder(string currentPath);
}
