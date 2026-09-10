using FileFlow.App.ViewModels;
using FileFlow.Core.Rules;
using Xunit;

namespace FileFlow.Core.Tests.Presentation;

public sealed class RuleEditorPolishTests
{
    private static RuleEditorViewModel Editor(FileRule? rule = null) => new(rule ?? new FileRule(),
        saved => new(new[] { saved }, null), new WorkflowTests.Dialogs());

    [Fact]
    public void Fresh_editor_hides_validation_but_cannot_save()
    {
        var editor = Editor();
        Assert.False(editor.HasErrors);
        Assert.Empty(editor.GetErrors(null).Cast<string>());
        Assert.Null(editor.ErrorMessage);
        Assert.False(editor.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void Editing_a_field_reveals_only_its_errors_and_clears_them_when_fixed()
    {
        var editor = Editor();
        editor.Name = " ";
        Assert.NotEmpty(editor.GetErrors(nameof(editor.Name)).Cast<string>());
        Assert.Empty(editor.GetErrors(nameof(editor.SourceDirectory)).Cast<string>());
        Assert.Empty(editor.GetErrors(nameof(editor.ExtensionsText)).Cast<string>());
        editor.Name = "Photos";
        Assert.Empty(editor.GetErrors(nameof(editor.Name)).Cast<string>());
        Assert.False(editor.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void Leaving_an_unchanged_field_reveals_its_validation()
    {
        var editor = Editor();
        editor.MarkFieldInteracted(nameof(editor.SourceDirectory));
        Assert.NotEmpty(editor.GetErrors(nameof(editor.SourceDirectory)).Cast<string>());
        Assert.Empty(editor.GetErrors(nameof(editor.DestinationDirectory)).Cast<string>());
    }

    [Fact]
    public void Direct_invalid_save_reveals_all_errors_without_persisting()
    {
        var saves = 0;
        var editor = new RuleEditorViewModel(new(), rule => { saves++; return new(new[] { rule }, null); }, new WorkflowTests.Dialogs());
        editor.Save();
        Assert.NotEmpty(editor.GetErrors(nameof(editor.Name)).Cast<string>());
        Assert.NotEmpty(editor.GetErrors(nameof(editor.SourceDirectory)).Cast<string>());
        Assert.NotEmpty(editor.GetErrors(nameof(editor.DestinationDirectory)).Cast<string>());
        Assert.NotEmpty(editor.GetErrors(nameof(editor.ExtensionsText)).Cast<string>());
        Assert.False(editor.SaveCommand.CanExecute(null));
        Assert.Equal(0, saves);
    }

    [Fact]
    public void Editing_a_valid_rule_exposes_a_related_field_error_from_core()
    {
        var editor = Editor(WorkflowTests.Rule());
        editor.SourceDirectory = editor.DestinationDirectory;
        Assert.False(editor.SaveCommand.CanExecute(null));
        Assert.NotEmpty(editor.GetErrors(nameof(editor.DestinationDirectory)).Cast<string>());
    }

    [Fact]
    public void Save_availability_tracks_core_validity_including_cross_field_errors()
    {
        var editor = Editor(WorkflowTests.Rule());
        var changes = 0;
        editor.SaveCommand.CanExecuteChanged += (_, _) => changes++;
        Assert.True(editor.SaveCommand.CanExecute(null));
        editor.DestinationDirectory = editor.SourceDirectory;
        Assert.False(editor.SaveCommand.CanExecute(null));
        editor.DestinationDirectory = @"C:\another-destination";
        Assert.True(editor.SaveCommand.CanExecute(null));
        editor.ExtensionsText = "png,,jpg";
        Assert.False(editor.SaveCommand.CanExecute(null));
        editor.ExtensionsText = "png, JPG";
        Assert.True(editor.SaveCommand.CanExecute(null));
        Assert.True(changes >= 4);
        editor.SaveCommand.Execute(null);
        Assert.True(editor.IsSaved);
        Assert.False(editor.SaveCommand.CanExecute(null));
    }
}
