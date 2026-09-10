using System.Windows.Input;

namespace FileFlow.App.Infrastructure;

// Workflow methods own error presentation. This command owns only ICommand's async bridge and reentry guard.
public sealed class AsyncRelayCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null) : ICommand
{
    private bool running;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !running && (canExecute?.Invoke(parameter) ?? true);
    public async void Execute(object? parameter) => await ExecuteAsync(parameter);
    public async Task ExecuteAsync(object? parameter = null)
    {
        if (!CanExecute(parameter)) return;
        running = true;
        NotifyCanExecuteChanged();
        try { await execute(parameter); }
        finally { running = false; NotifyCanExecuteChanged(); }
    }
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
