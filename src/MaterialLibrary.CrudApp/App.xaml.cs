using System.Windows;
using MaterialLibraryCrudApp.Services;
using MaterialLibraryCrudApp.ViewModels;
using MaterialLibraryCrudApp.Views;

namespace MaterialLibraryCrudApp;

/// <summary>Application entry point: composes the object graph and shows the main window.</summary>
/// <remarks>
/// Composition happens here, in one place, rather than inside the view models. That keeps the
/// view models free of construction side effects and makes the dependency between
/// <see cref="MainViewModel"/> and <see cref="IDialogService"/> explicit.
/// </remarks>
public partial class App : Application
{
    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var window = new MainWindow();

        // The dialog service needs the window as its modal owner, and the view model needs the
        // dialog service, so the window is created first and its DataContext assigned afterwards.
        var dialogService = new DialogService(window);
        window.DataContext = new MainViewModel(dialogService);

        MainWindow = window;
        window.Show();
    }
}
