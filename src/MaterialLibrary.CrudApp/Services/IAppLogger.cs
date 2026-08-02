namespace MaterialLibraryCrudApp.Services;

/// <summary>Severity of a log entry.</summary>
public enum LogLevel
{
    /// <summary>Fine-grained tracing, written only when diagnostic logging is enabled.</summary>
    Debug = 0,

    /// <summary>Normal progress of an operation.</summary>
    Information = 1,

    /// <summary>Something recoverable that the user may need to know about.</summary>
    Warning = 2,

    /// <summary>An operation failed.</summary>
    Error = 3,
}

/// <summary>Writes diagnostic and failure information to a durable log.</summary>
/// <remarks>
/// Logging must work in Release builds, which is the whole reason this abstraction exists:
/// <c>System.Diagnostics.Debug.WriteLine</c> is compiled out of Release, so anything that relied on
/// it produced no record at all in the configuration users actually run.
/// </remarks>
public interface IAppLogger
{
    /// <summary>Whether verbose diagnostic entries are recorded.</summary>
    /// <remarks>
    /// Defaults to <c>true</c> in Debug builds and <c>false</c> in Release, and can be turned on in
    /// Release to capture a detailed trace from a user's machine without a special build.
    /// </remarks>
    bool IsDiagnosticEnabled { get; set; }

    /// <summary>Absolute path of the file entries are appended to.</summary>
    string LogFilePath { get; }

    /// <summary>Writes an entry.</summary>
    /// <param name="level">Severity of the entry.</param>
    /// <param name="message">Human-readable description of what happened.</param>
    /// <param name="exception">Exception to record with full type, message, and stack trace; may be <c>null</c>.</param>
    void Log(LogLevel level, string message, Exception? exception = null);
}

/// <summary>Convenience wrappers over <see cref="IAppLogger.Log"/>.</summary>
public static class AppLoggerExtensions
{
    /// <summary>Records a verbose diagnostic entry.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="message">Description of what happened.</param>
    public static void Diagnostic(this IAppLogger logger, string message) =>
        logger.Log(LogLevel.Debug, message);

    /// <summary>Records normal progress.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="message">Description of what happened.</param>
    public static void Information(this IAppLogger logger, string message) =>
        logger.Log(LogLevel.Information, message);

    /// <summary>Records a recoverable problem.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="message">Description of what happened.</param>
    /// <param name="exception">Optional exception context.</param>
    public static void Warning(this IAppLogger logger, string message, Exception? exception = null) =>
        logger.Log(LogLevel.Warning, message, exception);

    /// <summary>Records a failure.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="message">Description of what failed.</param>
    /// <param name="exception">Exception that caused the failure.</param>
    public static void Error(this IAppLogger logger, string message, Exception? exception = null) =>
        logger.Log(LogLevel.Error, message, exception);
}
