using System.Diagnostics;
using System.Windows;
using FileFlow.App.Infrastructure;
using FileFlow.App.Services;
using FileFlow.App.ViewModels;
using FileFlow.Core.Execution;
using FileFlow.Core.Persistence;
using FileFlow.Core.Preview;

namespace FileFlow.App;

public partial class App : Application
{
    private SingleInstanceGuard? instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            instance = SingleInstanceGuard.TryAcquire();
            if (instance is null)
            {
                MessageBox.Show("FileFlow is already running for this Windows user. Switch to the existing FileFlow window.",
                    "FileFlow", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }
            var dialogs = new WpfAppDialogService(() => MainWindow);
            var vm = new MainWindowViewModel(new CoreRuleStore(new FileRuleStore()),
                new CoreFileWorkflowService(new FileRulePreviewer(), new FileOperationExecutor()), dialogs);
            MainWindow = new MainWindow(vm);
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            MainWindow.Show();
        }
        catch (Exception exception)
        {
            Trace.TraceError("FileFlow startup failed: {0}", exception);
            MessageBox.Show("FileFlow could not start safely. Close any existing FileFlow window and try again. Your rules were not replaced.",
                "FileFlow could not start", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        instance?.Dispose();
        base.OnExit(e);
    }
}
