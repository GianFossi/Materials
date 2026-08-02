using System.Windows.Input;
using System.ComponentModel;

namespace MaterialLibraryCrudApp.ViewModels;

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isRunning;
    public bool IsRunning => _isRunning;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute is null || _canExecute());

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _isRunning = true;
        RaisePropertyChanged(nameof(IsRunning));
        RaiseCanExecuteChanged();
        try
        {
            await _execute();
        }
        catch (Exception ex)
        {
            // Command handlers own user-facing error reporting; this guard prevents an
            // unobserved exception from terminating the WPF dispatcher.
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            _isRunning = false;
            RaisePropertyChanged(nameof(IsRunning));
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    public event PropertyChangedEventHandler? PropertyChanged;
    private void RaisePropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
