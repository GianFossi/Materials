using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MaterialLibraryCrudApp.Services;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>
/// An <see cref="ICommand"/> whose handler is asynchronous, used to bind buttons to work that must
/// not block the UI thread.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Execute"/> is necessarily <c>async void</c>, because that is the signature
/// <see cref="ICommand"/> defines. Nothing can await it, so this class must observe its own
/// failures: an unhandled exception escaping an <c>async void</c> method is re-raised on the
/// synchronization context and terminates the process.
/// </para>
/// <para>
/// The command also guards against re-entrancy through <see cref="IsRunning"/>, so a double click
/// cannot start the same long database operation twice.
/// </para>
/// </remarks>
public sealed class AsyncRelayCommand : ICommand, INotifyPropertyChanged
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private readonly string _name;
    private bool _isRunning;

    /// <summary>Creates a command from an asynchronous handler and an optional guard.</summary>
    /// <param name="execute">Work performed when the command is invoked.</param>
    /// <param name="canExecute">Guard deciding whether the command is currently available; always available when <c>null</c>.</param>
    /// <param name="name">
    /// Name used in log entries. Filled in automatically from the call-site expression, so log
    /// output identifies the failing command without callers passing anything extra.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="execute"/> is <c>null</c>.</exception>
    public AsyncRelayCommand(
        Func<Task> execute,
        Func<bool>? canExecute = null,
        [CallerArgumentExpression(nameof(execute))] string name = "")
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _name = string.IsNullOrWhiteSpace(name) ? "async command" : name;
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Whether the handler is currently running.</summary>
    /// <remarks>
    /// Bound by the UI to show progress, and used by <see cref="CanExecute"/> to block re-entry.
    /// </remarks>
    public bool IsRunning => _isRunning;

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute is null || _canExecute());

    /// <summary>Runs the handler, reporting any failure to the log.</summary>
    /// <param name="parameter">Command parameter; unused.</param>
    /// <remarks>
    /// Failures are logged rather than rethrown. Rethrowing from an <c>async void</c> method would
    /// tear down the application, and command handlers are responsible for their own user-facing
    /// error messages; the log is the record for anything they missed.
    /// </remarks>
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        SetRunning(true);
        AppLog.Current.Diagnostic($"Command '{_name}' started.");

        try
        {
            await _execute();
            AppLog.Current.Diagnostic($"Command '{_name}' completed.");
        }
        catch (OperationCanceledException)
        {
            // Cancellation is the expected outcome of the Cancel button, not a failure.
            AppLog.Current.Information($"Command '{_name}' was cancelled.");
        }
        catch (Exception ex)
        {
            // This previously went to Debug.WriteLine, which is compiled out of Release: the
            // failure left no trace at all in the configuration users actually run.
            AppLog.Current.Error($"Command '{_name}' failed.", ex);
        }
        finally
        {
            SetRunning(false);
        }
    }

    /// <summary>Asks bound controls to re-evaluate <see cref="CanExecute"/>.</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Updates the running flag and notifies both the UI and command bindings.</summary>
    /// <param name="running">New running state.</param>
    private void SetRunning(bool running)
    {
        _isRunning = running;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRunning)));
        RaiseCanExecuteChanged();
    }
}
