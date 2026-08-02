using System.Windows.Input;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>An <see cref="ICommand"/> backed by plain delegates, used to bind buttons to view-model methods.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    /// <summary>Creates a command from an action and an optional guard.</summary>
    /// <param name="execute">Work performed when the command is invoked.</param>
    /// <param name="canExecute">Guard deciding whether the command is currently available; always available when <c>null</c>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="execute"/> is <c>null</c>.</exception>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => _canExecute is null || _canExecute();

    /// <inheritdoc />
    public void Execute(object? parameter) => _execute();

    /// <summary>Asks bound controls to re-evaluate <see cref="CanExecute"/>.</summary>
    /// <remarks>
    /// Called explicitly by the view model after state changes (selection, repository contents),
    /// rather than relying on <see cref="CommandManager.RequerySuggested"/>, which fires on every
    /// input event and would re-query far more often than necessary.
    /// </remarks>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
