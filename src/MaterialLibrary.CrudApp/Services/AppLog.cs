namespace MaterialLibraryCrudApp.Services;

/// <summary>Ambient access to the application logger.</summary>
/// <remarks>
/// <para>
/// The application has no dependency-injection container, and the places that most need to log -
/// the <c>ICommand</c> wrappers and the global unhandled-exception hooks - are constructed long
/// before, or entirely outside, any view model. A single ambient instance set once at startup is the
/// pragmatic way to reach them.
/// </para>
/// <para>
/// It is deliberately never <c>null</c>: it starts as a no-op sink so that unit tests and design-time
/// construction do not have to configure logging, and <see cref="Initialize"/> swaps in the real
/// writer during application startup.
/// </para>
/// </remarks>
public static class AppLog
{
    /// <summary>The active logger; a no-op sink until <see cref="Initialize"/> is called.</summary>
    public static IAppLogger Current { get; private set; } = new NullAppLogger();

    /// <summary>Installs the logger used for the rest of the process lifetime.</summary>
    /// <param name="logger">Logger to install.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <c>null</c>.</exception>
    public static void Initialize(IAppLogger logger)
    {
        Current = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Logger that discards everything, used before initialization and in tests.</summary>
    private sealed class NullAppLogger : IAppLogger
    {
        /// <inheritdoc />
        public bool IsDiagnosticEnabled { get; set; }

        /// <inheritdoc />
        public string LogFilePath => string.Empty;

        /// <inheritdoc />
        public void Log(LogLevel level, string message, Exception? exception = null)
        {
            // Intentionally empty: no logger configured.
        }
    }
}
