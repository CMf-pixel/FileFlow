using FileFlow.App.Services;
using FileFlow.App.ViewModels;
using FileFlow.Core.Execution;
using FileFlow.Core.Persistence;
using FileFlow.Core.Preview;
using FileFlow.Core.Rules;
using Xunit;

namespace FileFlow.Core.Tests.Presentation;

public sealed class WorkflowTests
{
    internal static FileRule Rule(string name = "Images") => new()
    {
        Name = name, SourceDirectory = @"C:\source", DestinationDirectory = @"C:\destination",
        Extensions = new[] { ".png" }
    };

    internal static OperationPreview Preview(PreviewStatus status = PreviewStatus.Ready) => new(Rule(),
        status == PreviewStatus.NoMatches ? Array.Empty<PlannedOperation>() : new[] {
            new PlannedOperation(@"C:\source\z.png", @"C:\destination\z.png", FileAction.Copy, 1, DateTime.UnixEpoch),
            new PlannedOperation(@"C:\source\a.png", @"C:\destination\a.png", FileAction.Copy, 1, DateTime.UnixEpoch) },
        status is PreviewStatus.Blocked or PreviewStatus.Incomplete
            ? new[] { new PreviewIssue(PreviewIssueCode.InspectionFailed, "Folder could not be read.", @"C:\source") }
            : Array.Empty<PreviewIssue>(), status == PreviewStatus.Incomplete);

    [Fact]
    public void Startup_loads_saved_order_and_missing_store_is_an_empty_success()
    {
        var store = new Store { Loaded = new(new[] { Rule("Second"), Rule("First") }, null) };
        var vm = new MainWindowViewModel(store, new Workflow(), new Dialogs());
        vm.Initialize();
        Assert.Equal(new[] { "Second", "First" }, vm.Rules.Select(r => r.Rule.Name));
        Assert.True(vm.CanManage);
        Assert.False(vm.IsEmpty);
        store.Loaded = new(Array.Empty<FileRule>(), null);
        vm.Initialize();
        Assert.True(vm.IsEmpty);
        Assert.Null(vm.ErrorMessage);
    }

    [Theory]
    [InlineData(RulePersistenceErrorCode.InvalidData)]
    [InlineData(RulePersistenceErrorCode.UnsupportedSchemaVersion)]
    [InlineData(RulePersistenceErrorCode.IoError)]
    public void Failed_load_is_not_empty_success_and_blocks_writes_until_retry(RulePersistenceErrorCode code)
    {
        var store = new Store { Loaded = new(null, new(code, "Preserved file")) };
        var dialogs = new Dialogs();
        var vm = new MainWindowViewModel(store, new Workflow(), dialogs);
        vm.Initialize();
        Assert.False(vm.IsEmpty);
        Assert.False(vm.CanManage);
        Assert.False(vm.CreateCommand.CanExecute(null));
        Assert.False(string.IsNullOrWhiteSpace(vm.ErrorMessage));
        vm.CreateRule();
        Assert.Equal(0, dialogs.EditCalls);
        Assert.Equal(0, store.Saves);
        store.Loaded = new(Array.Empty<FileRule>(), null);
        vm.Initialize();
        Assert.True(vm.CanManage);
    }

    [Fact]
    public void Create_publishes_only_a_successful_normalized_snapshot()
    {
        var store = new Store();
        var dialogs = new Dialogs();
        var vm = new MainWindowViewModel(store, new Workflow(), dialogs);
        vm.Initialize();
        dialogs.Edit = editor =>
        {
            Assert.Equal(FileAction.Copy, editor.Action);
            Fill(editor);
            editor.Name = "  Images  ";
            editor.ExtensionsText = "png, .PNG, jpg";
            Assert.Empty(vm.Rules);
            editor.Save();
            Assert.True(editor.IsSaved);
        };
        vm.CreateRule();
        var saved = Assert.Single(vm.Rules).Rule;
        Assert.Equal("Images", saved.Name);
        Assert.Equal(new[] { ".png", ".jpg" }, saved.Extensions);
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public void Save_failure_keeps_editor_and_displayed_collection_intact_and_can_retry()
    {
        var original = Rule();
        var store = new Store { Loaded = new(new[] { original }, null), FailSave = true };
        var dialogs = new Dialogs();
        var vm = new MainWindowViewModel(store, new Workflow(), dialogs);
        vm.Initialize();
        dialogs.Edit = editor =>
        {
            editor.Name = "Changed";
            editor.Save();
            Assert.False(editor.IsSaved);
            Assert.Equal("Changed", editor.Name);
            Assert.NotNull(editor.ErrorMessage);
            Assert.Same(original, Assert.Single(vm.Rules).Rule);
            store.FailSave = false;
            editor.Save();
            Assert.True(editor.IsSaved);
        };
        vm.EditRule(vm.Rules[0]);
        Assert.Equal("Changed", vm.Rules[0].Rule.Name);
        Assert.Equal(original.Id, vm.Rules[0].Rule.Id);
    }

    [Fact]
    public void Edit_targets_selected_entry_even_when_stored_identifiers_repeat()
    {
        var first = Rule("One");
        var store = new Store { Loaded = new(new[] { first, first with { Name = "Two" } }, null) };
        var dialogs = new Dialogs { Edit = editor => { editor.Name = "Edited"; editor.Save(); } };
        var vm = new MainWindowViewModel(store, new Workflow(), dialogs);
        vm.Initialize();
        vm.EditRule(vm.Rules[1]);
        Assert.Equal(new[] { "One", "Edited" }, vm.Rules.Select(r => r.Rule.Name));
    }

    [Fact]
    public void Invalid_editor_rejects_save_and_does_not_repair_empty_extension_entries()
    {
        var saves = 0;
        var editor = new RuleEditorViewModel(Rule(), rule => { saves++; return new(new[] { rule }, null); }, new Dialogs());
        editor.Name = "";
        editor.Save();
        Assert.True(editor.HasErrors);
        Assert.NotEmpty(editor.GetErrors(nameof(editor.Name)).Cast<string>());
        editor.Name = "Images";
        editor.ExtensionsText = "png,,jpg";
        editor.Save();
        Assert.NotEmpty(editor.GetErrors(nameof(editor.ExtensionsText)).Cast<string>());
        Assert.Equal(0, saves);
        editor.ExtensionsText = "png, JPG";
        editor.Save();
        Assert.Equal(1, saves); // Structurally valid folders need not exist until Run.
    }

    [Fact]
    public void Folder_picker_cancellation_preserves_manual_input()
    {
        var dialogs = new Dialogs();
        var editor = new RuleEditorViewModel(Rule(), rule => new(new[] { rule }, null), dialogs);
        editor.BrowseSourceCommand.Execute(null);
        Assert.Equal(@"C:\source", editor.SourceDirectory);
        dialogs.Folder = @"D:\photos";
        editor.BrowseDestinationCommand.Execute(null);
        Assert.Equal(@"D:\photos", editor.DestinationDirectory);
    }

    [Fact]
    public void Delete_requires_confirmation_and_failed_persistence_keeps_the_row()
    {
        var store = new Store { Loaded = new(new[] { Rule() }, null) };
        var dialogs = new Dialogs();
        var vm = new MainWindowViewModel(store, new Workflow(), dialogs);
        vm.Initialize();
        vm.DeleteRule(vm.Rules[0]);
        Assert.Equal(0, store.Saves);
        Assert.Single(vm.Rules);
        Assert.Equal("Images", dialogs.ConfirmedName);
        dialogs.Confirm = true;
        store.FailSave = true;
        vm.DeleteRule(vm.Rules[0]);
        Assert.Single(vm.Rules);
        Assert.NotNull(vm.ErrorMessage);
        store.FailSave = false;
        vm.DeleteRule(vm.Rules[0]);
        Assert.Empty(vm.Rules);
        Assert.True(vm.IsEmpty);
    }

    [Fact]
    public async Task Run_is_guarded_until_preview_and_owned_dialog_finish()
    {
        var pending = new TaskCompletionSource<OperationPreview>();
        var workflow = new Workflow { PreviewTask = pending.Task };
        var dialogs = new Dialogs();
        var vm = new MainWindowViewModel(new Store { Loaded = new(new[] { Rule() }, null) }, workflow, dialogs);
        vm.Initialize();
        dialogs.Run = run => { Assert.False(vm.CanManage); Assert.True(run.CanExecute); };
        var row = vm.Rules[0];
        var task = vm.RunAsync(row);
        Assert.True(vm.IsBusy);
        Assert.False(vm.CanClose);
        await vm.RunAsync(row);
        Assert.Equal(1, workflow.PreviewCalls);
        var preview = Preview();
        pending.SetResult(preview);
        await task;
        Assert.Same(preview, dialogs.LastRun!.Preview);
        Assert.True(vm.CanManage);
        Assert.True(vm.CanClose);
    }

    [Theory]
    [InlineData(PreviewStatus.Ready, true)]
    [InlineData(PreviewStatus.Blocked, false)]
    [InlineData(PreviewStatus.Incomplete, false)]
    [InlineData(PreviewStatus.NoMatches, false)]
    public void Preview_uses_core_eligibility_and_preserves_operations(PreviewStatus status, bool canExecute)
    {
        var preview = Preview(status);
        var vm = new RunViewModel(preview, new Workflow());
        Assert.Equal(canExecute, vm.CanExecute);
        Assert.Same(preview.Operations, vm.Preview.Operations);
        Assert.Equal(status, vm.Preview.Status);
        if (status != PreviewStatus.NoMatches)
            Assert.Equal(@"C:\source\a.png", vm.Preview.Operations[0].SourcePath);
    }

    [Fact]
    public async Task Execution_consumes_exact_preview_once_and_blocks_close_until_results()
    {
        var preview = Preview();
        var pending = new TaskCompletionSource<ExecutionResult>();
        var workflow = new Workflow { ExecutionTask = pending.Task };
        var vm = new RunViewModel(preview, workflow);
        var task = vm.ExecuteAsync();
        Assert.True(vm.IsExecuting);
        Assert.False(vm.CanClose);
        Assert.False(vm.CanExecute);
        await vm.ExecuteAsync();
        Assert.Equal(1, workflow.ExecutionCalls);
        Assert.Same(preview, workflow.ExecutedPreview);
        var result = new ExecutionResult(preview.Operations.Select(o => new OperationResult(o, OperationStatus.Succeeded)), Array.Empty<ExecutionError>(), false);
        pending.SetResult(result);
        await task;
        Assert.Same(result, vm.Results!.Result);
        Assert.True(vm.CanClose);
        Assert.False(vm.CanExecute);
        await vm.ExecuteAsync();
        Assert.Equal(1, workflow.ExecutionCalls);
    }

    [Fact]
    public async Task Nonready_preview_never_reaches_executor()
    {
        var workflow = new Workflow();
        foreach (var status in new[] { PreviewStatus.Blocked, PreviewStatus.Incomplete, PreviewStatus.NoMatches })
            await new RunViewModel(Preview(status), workflow).ExecuteAsync();
        Assert.Equal(0, workflow.ExecutionCalls);
    }

    [Fact]
    public async Task Unexpected_execution_failure_recovers_busy_state_without_reenabling_preview()
    {
        var workflow = new Workflow { ExecutionTask = Task.FromException<ExecutionResult>(new InvalidOperationException("bug")) };
        var vm = new RunViewModel(Preview(), workflow);
        await vm.ExecuteAsync();
        Assert.False(vm.IsExecuting);
        Assert.True(vm.CanClose);
        Assert.False(vm.CanExecute);
        Assert.Null(vm.Results);
        Assert.True(vm.HasUnexpectedError);
        Assert.DoesNotContain("InvalidOperationException", vm.ErrorMessage);
    }

    [Fact]
    public async Task Preview_failure_returns_to_rules_and_fresh_run_creates_new_preview()
    {
        var workflow = new Workflow { PreviewTask = Task.FromException<OperationPreview>(new InvalidOperationException("bug")) };
        var dialogs = new Dialogs();
        var vm = new MainWindowViewModel(new Store { Loaded = new(new[] { Rule() }, null) }, workflow, dialogs);
        vm.Initialize();
        await vm.RunAsync(vm.Rules[0]);
        Assert.True(vm.CanManage);
        Assert.False(vm.IsBusy);
        Assert.NotNull(vm.ErrorMessage);
        workflow.PreviewTask = Task.FromResult(Preview());
        await vm.RunAsync(vm.Rules[0]);
        var previous = dialogs.LastRun!.Preview;
        workflow.PreviewTask = Task.FromResult(Preview());
        await vm.RunAsync(vm.Rules[0]);
        Assert.NotSame(previous, dialogs.LastRun!.Preview);
        Assert.Equal(3, workflow.PreviewCalls);
    }

    [Theory]
    [InlineData(ExecutionOutcome.Succeeded)]
    [InlineData(ExecutionOutcome.Rejected)]
    [InlineData(ExecutionOutcome.Failed)]
    [InlineData(ExecutionOutcome.PartiallySucceeded)]
    public async Task Results_preserve_core_status_counts_and_attempt_remains_consumed(ExecutionOutcome outcome)
    {
        var preview = Preview();
        var error = new ExecutionError(ExecutionErrorCode.MutationFailed, "Access denied; filesystem changes may remain.");
        var statuses = outcome switch {
            ExecutionOutcome.Succeeded => new[] { OperationStatus.Succeeded, OperationStatus.Succeeded },
            ExecutionOutcome.Rejected => new[] { OperationStatus.NotAttempted, OperationStatus.NotAttempted },
            ExecutionOutcome.Failed => new[] { OperationStatus.Failed, OperationStatus.NotAttempted },
            _ => new[] { OperationStatus.Succeeded, OperationStatus.Failed }
        };
        var result = new ExecutionResult(preview.Operations.Select((o, i) => new OperationResult(o, statuses[i], statuses[i] == OperationStatus.Failed ? error : null)),
            outcome == ExecutionOutcome.Succeeded ? Array.Empty<ExecutionError>() : new[] { error }, outcome == ExecutionOutcome.Rejected);
        var vm = new RunViewModel(preview, new Workflow { ExecutionTask = Task.FromResult(result) });
        await vm.ExecuteAsync();
        Assert.Equal(outcome, vm.Results!.Result.Outcome);
        Assert.Same(result.Operations, vm.Results.Result.Operations);
        Assert.Equal(outcome == ExecutionOutcome.PartiallySucceeded, vm.Results.IsPartialFailure);
        Assert.Equal(statuses.Count(s => s == OperationStatus.Succeeded), vm.Results.Result.SucceededCount);
        Assert.False(vm.CanExecute);
        Assert.False(string.IsNullOrWhiteSpace(vm.Results.Summary));
    }

    private static void Fill(RuleEditorViewModel editor)
    {
        editor.Name = "Images"; editor.SourceDirectory = @"C:\source";
        editor.DestinationDirectory = @"C:\destination"; editor.ExtensionsText = "png";
    }

    internal sealed class Store : IRuleStore
    {
        public RuleStoreResult Loaded = new(Array.Empty<FileRule>(), null);
        public bool FailSave;
        public int Saves;
        public RuleStoreResult Load() => Loaded;
        public RuleStoreResult Save(IReadOnlyList<FileRule> rules)
        {
            Saves++;
            return FailSave ? new(null, new(RulePersistenceErrorCode.IoError, "Disk unavailable")) : new(rules.ToArray(), null);
        }
    }

    internal sealed class Workflow : IFileWorkflowService
    {
        public Task<OperationPreview> PreviewTask = Task.FromResult(Preview());
        public Task<ExecutionResult>? ExecutionTask;
        public int PreviewCalls, ExecutionCalls;
        public OperationPreview? ExecutedPreview;
        public Task<OperationPreview> CreatePreviewAsync(FileRule rule) { PreviewCalls++; return PreviewTask; }
        public Task<ExecutionResult> ExecuteAsync(OperationPreview preview)
        {
            ExecutionCalls++; ExecutedPreview = preview;
            return ExecutionTask ?? Task.FromResult(new ExecutionResult(preview.Operations.Select(o => new OperationResult(o, OperationStatus.NotAttempted)), Array.Empty<ExecutionError>(), true));
        }
    }

    internal sealed class Dialogs : IAppDialogService
    {
        public Action<RuleEditorViewModel>? Edit;
        public Action<RunViewModel>? Run;
        public RunViewModel? LastRun;
        public bool Confirm;
        public string? Folder, ConfirmedName;
        public int EditCalls;
        public void ShowRuleEditor(RuleEditorViewModel editor) { EditCalls++; Edit?.Invoke(editor); }
        public void ShowRun(RunViewModel run) { LastRun = run; Run?.Invoke(run); }
        public bool ConfirmDelete(string ruleName) { ConfirmedName = ruleName; return Confirm; }
        public string? PickFolder(string currentPath) => Folder;
    }
}
