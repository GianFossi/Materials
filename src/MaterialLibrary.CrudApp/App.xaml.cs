using System.Windows;
using System.Windows.Threading;
using MaterialLibraryCrudApp.Services;
using MaterialLibraryCrudApp.ViewModels;
using MaterialLibraryCrudApp.Views;

namespace MaterialLibraryCrudApp;

/// <summary>Application entry point: installs logging, composes the object graph, and shows the main window.</summary>
/// <remarks>
/// Composition happens here, in one place, rather than inside the view models. That keeps the
/// view models free of construction side effects and makes the dependency between
/// <see cref="MainViewModel"/> and <see cref="IDialogService"/> explicit.
/// </remarks>
public partial class App : Application
{
    /// <summary>Command-line switch that turns on verbose logging in any build.</summary>
    /// <remarks>
    /// Lets a user reproduce a problem with a full trace on a stock Release build, instead of
    /// needing a special Debug binary.
    /// </remarks>
    private const string DiagnosticSwitch = "--diagnostic";

    /// <summary>Logger installed for the process lifetime.</summary>
    private AppLogger? _logger;

    /// <summary>Starts the application: logging first, then the UI.</summary>
    /// <param name="e">Startup arguments, including any command-line switches.</param>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Logging is installed before anything else so a failure during composition is still recorded.
        _logger = new AppLogger();

        if (e.Args.Any(arg => string.Equals(arg, DiagnosticSwitch, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.IsDiagnosticEnabled = true;
        }

        AppLog.Initialize(_logger);
        _logger.LogSessionStart();

        InstallGlobalExceptionHandlers();

        var window = new MainWindow();

        // The dialog service needs the window as its modal owner, and the view model needs the
        // dialog service, so the window is created first and its DataContext assigned afterwards.
        var dialogService = new DialogService(window);
        window.DataContext = new MainViewModel(dialogService);

        MainWindow = window;
        window.Show();

        AppLog.Current.Information("Main window shown.");
    }

    /// <summary>Records the end of the session.</summary>
    /// <param name="e">Exit arguments.</param>
    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Current.Information($"=== Session end | exit code {e.ApplicationExitCode} ===");
        base.OnExit(e);
    }

    /// <summary>
    /// Subscribes to the three channels through which an exception can escape the application.
    /// </summary>
    /// <remarks>
    /// Each channel is reached by a different class of failure, and none of them covers the others:
    /// UI-thread exceptions surface on the dispatcher, background-thread exceptions on the app
    /// domain, and exceptions inside tasks nobody awaited only when the task is finalized.
    /// </remarks>
    private void InstallGlobalExceptionHandlers()
    {
        // 1. Anything thrown on the UI thread. Handled, so the app stays alive and the user is told.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // 2. Anything thrown on a background thread. The runtime terminates after this returns, so
        //    the only thing possible is to make sure the failure is on record first.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Current.Error(
                $"Unhandled exception on a background thread (terminating: {args.IsTerminating}).",
                args.ExceptionObject as Exception);

        // 3. Faulted tasks that nothing awaited - the fire-and-forget refreshes are the main source.
        //    Observing them here stops the exception being lost silently at finalization.
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Current.Error("Unobserved exception in a background task.", args.Exception);
            args.SetObserved();
        };
    }

    /// <summary>Reports a UI-thread exception and keeps the application running.</summary>
    /// <param name="sender">Event source; unused.</param>
    /// <param name="e">Exception details; marked handled so the dispatcher continues.</param>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Current.Error("Unhandled exception on the UI thread.", e.Exception);

        MessageBox.Show(
            MainWindow,
            $"An unexpected error occurred:\n\n{e.Exception.Message}\n\n" +
            $"Details were written to:\n{AppLog.Current.LogFilePath}",
            "Material Library",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        // Marking it handled keeps the session alive; the user can save their work rather than
        // losing an unsaved library to a non-fatal UI fault.
        e.Handled = true;
    }
}
