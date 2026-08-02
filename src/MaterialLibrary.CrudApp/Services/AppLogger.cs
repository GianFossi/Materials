using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace MaterialLibraryCrudApp.Services;

/// <summary>
/// File-based logger that also mirrors entries to the attached debugger.
/// </summary>
/// <remarks>
/// <para>
/// Entries go to a rolling file under
/// <c>%LOCALAPPDATA%\MaterialLibrary.CrudApp\logs\</c>, one file per day, so a user can send a
/// single file after a failure. Writing is append-only and guarded by a lock because unhandled
/// exceptions can arrive on the dispatcher thread, a thread-pool thread, and the finalizer thread.
/// </para>
/// <para>
/// The same entries are echoed through <see cref="Trace"/> rather than <c>Debug</c>. Both are
/// compiled out by default in Release, but <c>TRACE</c> is defined by the .NET SDK in Release too,
/// so the debugger echo survives where a <c>Debug.WriteLine</c> would silently disappear.
/// </para>
/// </remarks>
public sealed class AppLogger : IAppLogger
{
    /// <summary>Serialises writes from dispatcher, thread-pool, and finalizer threads.</summary>
    private readonly object _gate = new();

    private readonly string _logDirectory;

    /// <summary>Creates a logger writing under the default per-user log directory.</summary>
    public AppLogger()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MaterialLibrary.CrudApp",
            "logs"))
    {
    }

    /// <summary>Creates a logger writing to a specific directory.</summary>
    /// <param name="logDirectory">Directory to hold the log files; created if absent.</param>
    /// <exception cref="ArgumentException">Thrown when the directory path is blank.</exception>
    public AppLogger(string logDirectory)
    {
        if (string.IsNullOrWhiteSpace(logDirectory))
        {
            throw new ArgumentException("Log directory must not be blank.", nameof(logDirectory));
        }

        _logDirectory = logDirectory;

        // Verbose tracing is on by default only in Debug; Release can still opt in at runtime.
#if DEBUG
        IsDiagnosticEnabled = true;
#else
        IsDiagnosticEnabled = false;
#endif
    }

    /// <inheritdoc />
    public bool IsDiagnosticEnabled { get; set; }

    /// <inheritdoc />
    public string LogFilePath =>
        Path.Combine(_logDirectory, $"crudapp-{DateTime.Now:yyyy-MM-dd}.log");

    /// <summary>Build configuration this assembly was compiled in.</summary>
    /// <remarks>Recorded in the session header so a log file is unambiguous about its origin.</remarks>
    public static string BuildConfiguration =>
#if DEBUG
        "DEBUG";
#else
        "RELEASE";
#endif

    /// <summary>Writes a header marking the start of an application session.</summary>
    /// <remarks>
    /// Makes it obvious where one run ends and the next begins inside a day's file, and records the
    /// build configuration and version so a report from a user is self-describing.
    /// </remarks>
    public void LogSessionStart()
    {
        var version = typeof(AppLogger).Assembly.GetName().Version?.ToString() ?? "unknown";

        Log(
            LogLevel.Information,
            $"=== Session start | build={BuildConfiguration} | version={version} | " +
            $"os={Environment.OSVersion} | diagnostic={IsDiagnosticEnabled} ===");
    }

    /// <inheritdoc />
    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        // Verbose entries are dropped unless diagnostic logging is on, so the Release log stays
        // small enough to be worth reading.
        if (level == LogLevel.Debug && !IsDiagnosticEnabled)
        {
            return;
        }

        var entry = Format(level, message, exception);

        // Always mirror to the debugger. TRACE is defined in Release by the SDK, unlike DEBUG.
        Trace.WriteLine(entry);

        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(_logDirectory);
                File.AppendAllText(LogFilePath, entry + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception writeFailure)
        {
            // A logger must never take the application down. If the file cannot be written - disk
            // full, permissions, path too long - the debugger echo above is the remaining record.
            Trace.WriteLine($"[logger] could not write to '{LogFilePath}': {writeFailure.Message}");
        }
    }

    /// <summary>Renders one entry as a single self-contained block of text.</summary>
    /// <param name="level">Severity of the entry.</param>
    /// <param name="message">Description of what happened.</param>
    /// <param name="exception">Optional exception to expand.</param>
    /// <returns>Formatted text, including the full exception chain when one is supplied.</returns>
    private static string Format(LogLevel level, string message, Exception? exception)
    {
        var builder = new StringBuilder();

        builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append(" [").Append(level.ToString().ToUpperInvariant()).Append(']')
            .Append(" [T").Append(Environment.CurrentManagedThreadId).Append(']')
            .Append(' ').Append(message);

        // Walk the whole chain: the useful detail is often in an inner exception.
        for (var current = exception; current is not null; current = current.InnerException)
        {
            builder.AppendLine()
                .Append("    ").Append(current.GetType().FullName)
                .Append(": ").Append(current.Message);

            if (!string.IsNullOrWhiteSpace(current.StackTrace))
            {
                builder.AppendLine().Append(current.StackTrace);
            }
        }

        return builder.ToString();
    }
}
