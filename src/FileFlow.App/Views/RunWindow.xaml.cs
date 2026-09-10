using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using FileFlow.App.ViewModels;

namespace FileFlow.App.Views;

public partial class RunWindow : Window
{
    private readonly RunViewModel viewModel;

    public RunWindow(RunViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) =>
        Dispatcher.BeginInvoke(() => CloseButton.Focus(), DispatcherPriority.Input);

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!viewModel.CanClose) e.Cancel = true;
    }
}
