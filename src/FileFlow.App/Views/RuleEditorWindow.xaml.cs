using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FileFlow.App.ViewModels;

namespace FileFlow.App.Views;

public partial class RuleEditorWindow : Window
{
    private readonly RuleEditorViewModel viewModel;

    public RuleEditorWindow(RuleEditorViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
        Title = string.IsNullOrWhiteSpace(viewModel.Name) ? "Create rule" : "Edit rule";
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        Closed += Window_Closed;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RuleEditorViewModel.IsSaved) && viewModel.IsSaved)
            DialogResult = true;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(FocusFirstInvalidField, DispatcherPriority.ContextIdle);
    }

    private void Field_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var binding = sender switch
        {
            TextBox textBox => textBox.GetBindingExpression(TextBox.TextProperty),
            ComboBox comboBox => comboBox.GetBindingExpression(ComboBox.SelectedItemProperty),
            _ => null
        };
        if (binding?.ParentBinding.Path?.Path is { } propertyName)
            viewModel.MarkFieldInteracted(propertyName);
    }

    private void FocusFirstInvalidField()
    {
        if (viewModel.IsSaved || !viewModel.HasErrors) return;
        foreach (var field in new Control[] { NameBox, SourceBox, ExtensionsBox, DestinationBox })
        {
            if (!Validation.GetHasError(field)) continue;
            field.Focus();
            Keyboard.Focus(field);
            if (field is TextBox textBox) textBox.SelectAll();
            return;
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        Closed -= Window_Closed;
    }
}
