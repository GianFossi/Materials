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
    /// <summary>Alias for <see cref="RaiseCanExecuteChanged"/> used by size-ranged editor sub-commands.</summary>
    public void NotifyCanExecuteChanged() => RaiseCanExecuteChanged();
}

/// <summary>
/// A typed <see cref="ICommand"/> backed by delegates that accept a single parameter of type
/// <typeparamref name="T"/>.  Used for row/column delete commands that need to know the index.
/// </summary>
/// <typeparam name="T">Type of the command parameter.</typeparam>
public sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T> _execute;
    private readonly Func<T, bool>? _canExecute;

    /// <summary>Creates a command from an action and an optional guard.</summary>
    /// <param name="execute">Work performed when the command is invoked.</param>
    /// <param name="canExecute">Guard deciding whether the command is currently available.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="execute"/> is <c>null</c>.</exception>
    public RelayCommand(Action<T> execute, Func<T, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc />
    public bool CanExecute(object? parameter)
    {
        if (_canExecute is null)
        {
            return true;
        }

        if (parameter is T typedParam)
        {
            return _canExecute(typedParam);
        }

        return true;
    }

    /// <inheritdoc />
    public void Execute(object? parameter)
    {
        if (parameter is T typedParam)
        {
            _execute(typedParam);
        }
    }

    /// <summary>Asks bound controls to re-evaluate <see cref="CanExecute"/>.</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    /// <summary>Alias for <see cref="RaiseCanExecuteChanged"/>.</summary>
    public void NotifyCanExecuteChanged() => RaiseCanExecuteChanged();
}

