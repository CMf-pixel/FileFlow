using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using FileFlow.App.ViewModels;

namespace FileFlow.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel viewModel;
    private bool initialized;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (initialized) return;
        initialized = true;
        viewModel.Initialize();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!viewModel.CanClose) e.Cancel = true;
    }

    private void MoreActionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }
}
