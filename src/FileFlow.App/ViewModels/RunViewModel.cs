using System.Diagnostics;
using FileFlow.App.Infrastructure;
using FileFlow.App.Services;
using FileFlow.Core.Preview;

namespace FileFlow.App.ViewModels;

public sealed class RunViewModel : ObservableObject
{
    private readonly IFileWorkflowService workflow;
    private bool submitted, isExecuting;
    private ExecutionResultsViewModel? results;
    private string? errorMessage;

    public RunViewModel(OperationPreview preview, IFileWorkflowService workflow)
    {
        Preview = preview;
        this.workflow = workflow;
        ExecuteCommand = new(_ => ExecuteAsync(), _ => CanExecute);
    }
    public OperationPreview Preview { get; }
    public string Title => "Run — " + (Preview.Rule?.Name ?? "Rule");
    public bool IsExecuting => isExecuting;
    public bool CanExecute => Preview.CanExecute && !submitted && !isExecuting;
    public bool CanClose => !isExecuting;
    public bool ShowPreview => !submitted;
    public bool HasResults => results is not null;
    public bool HasUnexpectedError => errorMessage is not null;
    public string? ErrorMessage => errorMessage;
    public ExecutionResultsViewModel? Results => results;
    public AsyncRelayCommand ExecuteCommand { get; }
    public string PreviewTitle => Preview.Status switch
    {
        PreviewStatus.Ready => "Preview",
        PreviewStatus.NoMatches => "No matching files",
        PreviewStatus.Blocked => "Preview blocked",
        _ => "Preview incomplete"
    };
    public string PreviewSummary => Preview.Status switch
    {
        PreviewStatus.Ready => "Nothing has changed yet. Review every operation before choosing Execute.",
        PreviewStatus.NoMatches => "This rule currently matches no files. Nothing has changed.",
        PreviewStatus.Blocked => "Nothing has changed. Resolve the issues below and run the rule again.",
        _ => "FileFlow could not finish scanning this folder. The operations shown below are diagnostic only; the list may be incomplete. Nothing has changed."
    };

    public async Task ExecuteAsync()
    {
        if (!CanExecute) return;
        // Never release this application guard, including rejection or unexpected exceptions.
        submitted = true;
        isExecuting = true;
        NotifyState();
        try { results = new(await workflow.ExecuteAsync(Preview)); }
        catch (Exception exception)
        {
            Trace.TraceError("Unexpected execution failure: {0}", exception);
            errorMessage = "Execution ended unexpectedly and its outcome is unknown. Some files may have changed. Check the source and destination folders before running a fresh preview. FileFlow did not automatically roll back changes.";
        }
        finally { isExecuting = false; NotifyState(); }
    }

    private void NotifyState()
    {
        foreach (var property in new[] { nameof(IsExecuting), nameof(CanExecute), nameof(CanClose), nameof(ShowPreview),
            nameof(HasResults), nameof(Results), nameof(HasUnexpectedError), nameof(ErrorMessage) }) OnPropertyChanged(property);
        ExecuteCommand.NotifyCanExecuteChanged();
    }
}
