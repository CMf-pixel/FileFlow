using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using FileFlow.App.Infrastructure;
using FileFlow.App.Presentation;
using FileFlow.App.Services;
using FileFlow.Core.Persistence;
using FileFlow.Core.Rules;

namespace FileFlow.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IRuleStore store;
    private readonly IFileWorkflowService workflow;
    private readonly IAppDialogService dialogs;
    private bool loaded, busy, dialogOpen;
    private string? errorMessage;
    private RunViewModel? activeRun;

    public MainWindowViewModel(IRuleStore store, IFileWorkflowService workflow, IAppDialogService dialogs)
    {
        this.store = store; this.workflow = workflow; this.dialogs = dialogs;
        RetryCommand = new(_ => Initialize(), _ => !busy && !dialogOpen);
        CreateCommand = new(_ => CreateRule(), _ => CanManage);
        EditCommand = new(row => { if (row is RuleRowViewModel r) EditRule(r); }, row => CanManage && row is RuleRowViewModel);
        DeleteCommand = new(row => { if (row is RuleRowViewModel r) DeleteRule(r); }, row => CanManage && row is RuleRowViewModel);
        RunCommand = new(row => row is RuleRowViewModel r ? RunAsync(r) : Task.CompletedTask, row => CanManage && row is RuleRowViewModel);
    }

    public ObservableCollection<RuleRowViewModel> Rules { get; } = new();
    public bool IsBusy => busy;
    public bool IsLoaded => loaded;
    public bool HasRules => Rules.Count > 0;
    public bool IsEmpty => loaded && Rules.Count == 0;
    public bool HasLoadError => !loaded && errorMessage is not null;
    public bool CanManage => loaded && !busy && !dialogOpen;
    public bool CanClose => !busy && (activeRun?.CanClose ?? true);
    public string? ErrorMessage { get => errorMessage; private set { SetProperty(ref errorMessage, value); OnPropertyChanged(nameof(HasLoadError)); } }
    public RelayCommand RetryCommand { get; }
    public RelayCommand CreateCommand { get; }
    public RelayCommand EditCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public AsyncRelayCommand RunCommand { get; }

    public void Initialize()
    {
        if (busy || dialogOpen) return;
        try
        {
            var result = store.Load();
            loaded = result.IsSuccess;
            if (result.IsSuccess) { Publish(result.Rules!); ErrorMessage = null; }
            else ErrorMessage = CoreMessageFormatter.Persistence(result.Error!);
        }
        catch (Exception exception)
        {
            loaded = false;
            Trace.TraceError("Unexpected rule load failure: {0}", exception);
            ErrorMessage = "An unexpected error prevented loading your rules. No empty replacement was saved. Try loading again.";
        }
        NotifyState();
    }

    public void CreateRule() => OpenEditor(null);
    public void EditRule(RuleRowViewModel row) => OpenEditor(row);

    private void OpenEditor(RuleRowViewModel? row)
    {
        if (!CanManage || (row is not null && !Rules.Contains(row))) return;
        dialogOpen = true;
        NotifyState();
        try
        {
            var index = row is null ? -1 : Rules.IndexOf(row);
            var editor = new RuleEditorViewModel(row?.Rule ?? new FileRule(), normalized =>
            {
                var proposed = Rules.Select(r => r.Rule).ToList();
                if (index < 0) proposed.Add(normalized); else proposed[index] = normalized;
                var result = store.Save(proposed);
                if (result.IsSuccess) { Publish(result.Rules!); ErrorMessage = null; }
                return result;
            }, dialogs);
            dialogs.ShowRuleEditor(editor);
        }
        catch (Exception exception) { Unexpected("opening the rule editor", exception); }
        finally { dialogOpen = false; NotifyState(); }
    }

    public void DeleteRule(RuleRowViewModel row)
    {
        if (!CanManage || !Rules.Contains(row)) return;
        dialogOpen = true;
        NotifyState();
        try
        {
            if (!dialogs.ConfirmDelete(row.Rule.Name)) return;
            var proposed = Rules.Where(r => !ReferenceEquals(r, row)).Select(r => r.Rule).ToArray();
            var result = store.Save(proposed);
            if (result.IsSuccess) { Publish(result.Rules!); ErrorMessage = null; }
            else ErrorMessage = CoreMessageFormatter.Persistence(result.Error!);
        }
        catch (Exception exception) { Unexpected("deleting the rule", exception); }
        finally { dialogOpen = false; NotifyState(); }
    }

    public async Task RunAsync(RuleRowViewModel row)
    {
        if (!CanManage || !Rules.Contains(row)) return;
        busy = true;
        dialogOpen = true;
        ErrorMessage = null;
        NotifyState();
        try
        {
            var preview = await workflow.CreatePreviewAsync(row.Rule);
            activeRun = new(preview, workflow);
            activeRun.PropertyChanged += RunStateChanged;
            busy = false;
            NotifyState();
            dialogs.ShowRun(activeRun);
        }
        catch (Exception exception) { Unexpected("preparing or displaying the preview", exception); }
        finally
        {
            if (activeRun is not null) activeRun.PropertyChanged -= RunStateChanged;
            activeRun = null;
            busy = false;
            dialogOpen = false;
            NotifyState();
        }
    }

    private void RunStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RunViewModel.CanClose)) OnPropertyChanged(nameof(CanClose));
    }
    private void Publish(IReadOnlyList<FileRule> rules)
    {
        Rules.Clear();
        foreach (var rule in rules) Rules.Add(new(rule));
        NotifyState();
    }
    private void Unexpected(string operation, Exception exception)
    {
        Trace.TraceError("Unexpected error {0}: {1}", operation, exception);
        ErrorMessage = $"An unexpected error occurred while {operation}. Check the current state before trying again.";
    }
    private void NotifyState()
    {
        foreach (var property in new[] { nameof(IsBusy), nameof(IsLoaded), nameof(HasRules), nameof(IsEmpty), nameof(HasLoadError), nameof(CanManage), nameof(CanClose) })
            OnPropertyChanged(property);
        RetryCommand.NotifyCanExecuteChanged(); CreateCommand.NotifyCanExecuteChanged(); EditCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged(); RunCommand.NotifyCanExecuteChanged();
    }
}
