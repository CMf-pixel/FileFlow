using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using FileFlow.App.Infrastructure;
using FileFlow.App.Presentation;
using FileFlow.App.Services;
using FileFlow.Core.Persistence;
using FileFlow.Core.Rules;

namespace FileFlow.App.ViewModels;

public sealed class RuleEditorViewModel : ObservableObject, INotifyDataErrorInfo
{
    private readonly Guid id;
    private readonly Func<FileRule, RuleStoreResult> save;
    private readonly IAppDialogService dialogs;
    private string name, sourceDirectory, destinationDirectory, extensionsText;
    private FileAction action;
    private string? errorMessage;
    private bool isSaved;
    private bool showAllErrors;
    private readonly HashSet<string> interactedFields = new();
    private const string InvalidRuleMessage = "Check the highlighted fields before saving.";
    private readonly Dictionary<string, string[]> errors = new();
    private RuleValidationResult validation = null!;

    public RuleEditorViewModel(FileRule draft, Func<FileRule, RuleStoreResult> save, IAppDialogService dialogs)
    {
        id = draft.Id;
        this.save = save;
        this.dialogs = dialogs;
        name = draft.Name; sourceDirectory = draft.SourceDirectory;
        destinationDirectory = draft.DestinationDirectory;
        extensionsText = string.Join(", ", draft.Extensions); action = draft.Action;
        SaveCommand = new(_ => Save(), _ => !IsSaved && validation.IsValid);
        BrowseSourceCommand = new(_ => Browse(true));
        BrowseDestinationCommand = new(_ => Browse(false));
        Validate();
    }

    public string Name { get => name; set { if (SetProperty(ref name, value)) MarkFieldInteracted(nameof(Name)); } }
    public string SourceDirectory { get => sourceDirectory; set { if (SetProperty(ref sourceDirectory, value)) MarkFieldInteracted(nameof(SourceDirectory)); } }
    public string DestinationDirectory { get => destinationDirectory; set { if (SetProperty(ref destinationDirectory, value)) MarkFieldInteracted(nameof(DestinationDirectory)); } }
    public string ExtensionsText { get => extensionsText; set { if (SetProperty(ref extensionsText, value)) MarkFieldInteracted(nameof(ExtensionsText)); } }
    public FileAction Action { get => action; set { if (SetProperty(ref action, value)) MarkFieldInteracted(nameof(Action)); } }
    public IReadOnlyList<FileAction> Actions { get; } = new[] { FileAction.Copy, FileAction.Move };
    public string? ErrorMessage { get => errorMessage; private set => SetProperty(ref errorMessage, value); }
    public bool IsSaved { get => isSaved; private set => SetProperty(ref isSaved, value); }
    // INotifyDataErrorInfo exposes only errors ready to show. Save always uses the full Core result.
    public bool HasErrors => errors.Count > 0;
    public string NormalizedExtensions => validation.IsValid ? string.Join(", ", validation.Rule!.Extensions) : string.Empty;
    public RelayCommand SaveCommand { get; }
    public RelayCommand BrowseSourceCommand { get; }
    public RelayCommand BrowseDestinationCommand { get; }
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;
    public IEnumerable GetErrors(string? propertyName) => propertyName is null
        ? errors.Values.SelectMany(value => value).ToArray()
        : errors.GetValueOrDefault(propertyName, Array.Empty<string>());

    public void MarkFieldInteracted(string propertyName)
    {
        interactedFields.Add(propertyName);
        Validate();
    }

    private void Validate()
    {
        validation = RuleValidator.Validate(new FileRule { Id = id, Name = Name, SourceDirectory = SourceDirectory,
            DestinationDirectory = DestinationDirectory, Extensions = ExtensionInputParser.Parse(ExtensionsText), Action = Action });
        // Once valid, show any newly introduced errors, including ones Core assigns to a related field.
        showAllErrors |= validation.IsValid;
        var previous = errors.Keys.ToArray();
        errors.Clear();
        foreach (var group in validation.Errors.GroupBy(e => e.PropertyName == nameof(FileRule.Extensions) ? nameof(ExtensionsText) : e.PropertyName))
            if (showAllErrors || interactedFields.Contains(group.Key))
                errors[group.Key] = group.Select(e => e.Message).ToArray();
        foreach (var property in previous.Concat(errors.Keys).Distinct())
            ErrorsChanged?.Invoke(this, new(property));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(NormalizedExtensions));
        if (validation.IsValid && ErrorMessage == InvalidRuleMessage) ErrorMessage = null;
        SaveCommand.NotifyCanExecuteChanged();
    }

    public void Save()
    {
        if (IsSaved) return;
        showAllErrors = true;
        Validate();
        if (!validation.IsValid) { ErrorMessage = InvalidRuleMessage; return; }
        try
        {
            var result = save(validation.Rule!);
            if (!result.IsSuccess) { ErrorMessage = CoreMessageFormatter.Persistence(result.Error!); return; }
            ErrorMessage = null;
            IsSaved = true;
            SaveCommand.NotifyCanExecuteChanged();
        }
        catch (Exception exception)
        {
            Trace.TraceError("Unexpected rule save failure: {0}", exception);
            ErrorMessage = "An unexpected error prevented saving. Your draft has been kept. Check the saved rules before trying again.";
        }
    }

    private void Browse(bool source)
    {
        try
        {
            var selected = dialogs.PickFolder(source ? SourceDirectory : DestinationDirectory);
            if (selected is null) return;
            if (source) SourceDirectory = selected; else DestinationDirectory = selected;
        }
        catch (Exception exception)
        {
            Trace.TraceError("Folder picker failure: {0}", exception);
            ErrorMessage = "The folder picker could not open. You can still enter the path manually.";
        }
    }
}
