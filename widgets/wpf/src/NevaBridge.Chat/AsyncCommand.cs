using System.Windows.Input;

namespace NevaBridge.Chat;

/// <summary>An <see cref="ICommand"/> that runs an async action and re-checks its guard on demand.</summary>
public sealed class AsyncCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool> _canExecute;

    public AsyncCommand(Func<Task> execute, Func<bool> canExecute)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute();

    public async void Execute(object? parameter)
    {
        // WPF invokes commands as fire-and-forget; the view model reports failures itself.
        await _execute();
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
