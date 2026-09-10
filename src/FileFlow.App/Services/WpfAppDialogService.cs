using System.IO;
using System.Windows;
using System.Windows.Controls;
using FileFlow.App.ViewModels;
using FileFlow.App.Views;
using Microsoft.Win32;

namespace FileFlow.App.Services;

public sealed class WpfAppDialogService(Func<Window?> mainWindow) : IAppDialogService
{
    private Window? activeDialog;

    public void ShowRuleEditor(RuleEditorViewModel editor)
    {
        var window = new RuleEditorWindow(editor) { Owner = ResolveOwner() };
        ShowOwned(window);
    }

    public void ShowRun(RunViewModel run)
    {
        var window = new RunWindow(run) { Owner = ResolveOwner() };
        ShowOwned(window);
    }

    public bool ConfirmDelete(string ruleName)
    {
        var owner = ResolveOwner();
        var window = CreateDeleteConfirmation(ruleName);
        if (owner is not null) window.Owner = owner;
        return ShowOwned(window) == true;
    }

    public string? PickFolder(string currentPath)
    {
        var dialog = new OpenFolderDialog
        {
            Multiselect = false,
            Title = "Select folder"
        };
        if (!string.IsNullOrWhiteSpace(currentPath) && Directory.Exists(currentPath))
            dialog.InitialDirectory = currentPath;

        var owner = ResolveOwner();
        var accepted = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        return accepted == true ? dialog.FolderName : null;
    }

    private Window? ResolveOwner() => activeDialog?.IsVisible == true ? activeDialog : mainWindow();

    private bool? ShowOwned(Window window)
    {
        window.Owner ??= ResolveOwner();
        window.WindowStartupLocation = window.Owner is null
            ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        var previous = activeDialog;
        activeDialog = window;
        try { return window.ShowDialog(); }
        finally { activeDialog = previous; }
    }

    private static Window CreateDeleteConfirmation(string ruleName)
    {
        var window = new Window
        {
            Title = "Delete rule",
            Width = 430,
            Height = 190,
            MinWidth = 380,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Content = new Grid { Margin = new Thickness(16) }
        };

        var grid = (Grid)window.Content;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(new TextBlock
        {
            Text = $"Delete “{ruleName}”? This removes only the FileFlow rule. Your files will not be changed.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0)
        };
        Grid.SetRow(buttons, 1);
        var cancel = new Button { Content = "_Cancel", IsDefault = true, IsCancel = true, MinWidth = 84 };
        System.Windows.Automation.AutomationProperties.SetName(cancel, "Cancel deleting rule");
        var delete = new Button { Content = "_Delete", MinWidth = 84, Margin = new Thickness(8, 0, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(delete, $"Delete rule {ruleName}");
        delete.SetResourceReference(FrameworkElement.StyleProperty, "DangerButtonStyle");
        delete.Click += (_, _) => window.DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(delete);
        grid.Children.Add(buttons);
        return window;
    }
}
