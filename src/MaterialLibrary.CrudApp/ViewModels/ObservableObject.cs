using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>Base class supplying <see cref="INotifyPropertyChanged"/> plumbing for view models.</summary>
/// <remarks>
/// This is the bridge that makes WPF data binding possible at all. The F# domain records are
/// immutable and raise no change notifications, so every value the UI must observe lives on a
/// mutable view model deriving from this class rather than on the domain record itself.
/// </remarks>
public abstract class ObservableObject : INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raises <see cref="PropertyChanged"/> for the given property.</summary>
    /// <param name="propertyName">Property name; supplied automatically by the compiler.</param>
    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Assigns a backing field and notifies the UI only when the value actually changes.</summary>
    /// <typeparam name="T">Property type.</typeparam>
    /// <param name="field">Backing field, passed by reference.</param>
    /// <param name="value">Incoming value.</param>
    /// <param name="propertyName">Property name; supplied automatically by the compiler.</param>
    /// <returns><c>true</c> when the field was updated.</returns>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        // Short-circuiting on equality prevents binding feedback loops when the UI writes back
        // the value it was just given.
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        RaisePropertyChanged(propertyName);
        return true;
    }
}
