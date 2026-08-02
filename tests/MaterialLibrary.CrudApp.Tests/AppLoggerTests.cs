using System;
using System.IO;
using System.Threading.Tasks;
using MaterialLibraryCrudApp.Services;
using MaterialLibraryCrudApp.ViewModels;
using Xunit;

namespace MaterialLibrary.CrudApp.Tests;

/// <summary>
/// Verifies that failures are recorded in the configuration users actually run.
/// </summary>
/// <remarks>
/// These exist because the previous implementation logged through
/// <c>System.Diagnostics.Debug.WriteLine</c>, which the compiler removes from Release builds: an
/// exception inside an async command left no trace anywhere. Every assertion here therefore runs
/// against the real file sink rather than a stub.
/// </remarks>
public sealed class AppLoggerTests : IDisposable
{
    private readonly string _directory;

    /// <summary>Creates an isolated log directory for one test.</summary>
    public AppLoggerTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "applog-" + Guid.NewGuid().ToString("N")[..8]);
    }

    /// <summary>Removes the temporary log directory.</summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A locked temp directory must not fail the test run.
        }
    }

    /// <summary>Reads everything the logger has written so far.</summary>
    /// <param name="logger">Logger whose file is read.</param>
    /// <returns>File contents, or an empty string when nothing was written.</returns>
    private static string ReadLog(IAppLogger logger) =>
        File.Exists(logger.LogFilePath) ? File.ReadAllText(logger.LogFilePath) : string.Empty;

    [Fact]
    public void ErrorEntryRecordsMessageTypeAndStackTrace()
    {
        var logger = new AppLogger(_directory);

        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (Exception ex)
        {
            logger.Error("Saving failed.", ex);
        }

        var text = ReadLog(logger);

        Assert.Contains("[ERROR]", text);
        Assert.Contains("Saving failed.", text);
        Assert.Contains(nameof(InvalidOperationException), text);
        Assert.Contains("boom", text);
        // The stack trace is what makes a user-supplied log actionable.
        Assert.Contains(nameof(ErrorEntryRecordsMessageTypeAndStackTrace), text);
    }

    [Fact]
    public void InnerExceptionChainIsExpanded()
    {
        var logger = new AppLogger(_directory);
        var inner = new FormatException("inner detail");

        logger.Error("Outer failed.", new InvalidOperationException("outer", inner));

        var text = ReadLog(logger);

        Assert.Contains("outer", text);
        // The useful detail is usually in the inner exception, so it must not be dropped.
        Assert.Contains("inner detail", text);
        Assert.Contains(nameof(FormatException), text);
    }

    [Fact]
    public void DiagnosticEntriesAreSuppressedUnlessEnabled()
    {
        var logger = new AppLogger(_directory) { IsDiagnosticEnabled = false };

        logger.Diagnostic("verbose-while-off");
        Assert.DoesNotContain("verbose-while-off", ReadLog(logger));

        logger.IsDiagnosticEnabled = true;
        logger.Diagnostic("verbose-while-on");
        Assert.Contains("verbose-while-on", ReadLog(logger));
    }

    [Fact]
    public void NonDiagnosticEntriesAreWrittenRegardlessOfBuild()
    {
        var logger = new AppLogger(_directory) { IsDiagnosticEnabled = false };

        logger.Information("progress");
        logger.Warning("careful");
        logger.Error("failed");

        var text = ReadLog(logger);

        // Whatever configuration the tests are compiled in, these three levels always persist.
        Assert.Contains("progress", text);
        Assert.Contains("careful", text);
        Assert.Contains("failed", text);
    }

    [Fact]
    public void SessionHeaderNamesTheBuildConfiguration()
    {
        var logger = new AppLogger(_directory);

        logger.LogSessionStart();

        var text = ReadLog(logger);

        Assert.Contains("Session start", text);
        Assert.Contains(AppLogger.BuildConfiguration, text);
        Assert.True(
            AppLogger.BuildConfiguration is "DEBUG" or "RELEASE",
            $"unexpected build configuration '{AppLogger.BuildConfiguration}'");
    }

    [Fact]
    public void LoggerSurvivesAnUnwritableDirectory()
    {
        // A logger must never take the application down; a bad path is reported, not thrown.
        var logger = new AppLogger(Path.Combine(_directory, new string('x', 300)));

        var exception = Record.Exception(() => logger.Error("still fine"));

        Assert.Null(exception);
    }

    [Fact]
    public async Task FailingAsyncCommandIsLoggedAndDoesNotThrow()
    {
        var logger = new AppLogger(_directory);
        AppLog.Initialize(logger);

        var command = new AsyncRelayCommand(() => throw new InvalidOperationException("handler exploded"));

        // Execute is async void, so nothing can await it: poll the guard flag instead.
        var exception = Record.Exception(() => command.Execute(null));
        Assert.Null(exception);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (command.IsRunning && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        var text = ReadLog(logger);

        Assert.Contains("[ERROR]", text);
        Assert.Contains("handler exploded", text);
        Assert.Contains(nameof(InvalidOperationException), text);
    }

    [Fact]
    public async Task CancelledAsyncCommandIsNotReportedAsAFailure()
    {
        var logger = new AppLogger(_directory);
        AppLog.Initialize(logger);

        var command = new AsyncRelayCommand(() => throw new OperationCanceledException());

        command.Execute(null);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (command.IsRunning && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        var text = ReadLog(logger);

        // Pressing Cancel is a normal outcome and must not look like a crash in the log.
        Assert.DoesNotContain("[ERROR]", text);
        Assert.Contains("cancelled", text, StringComparison.OrdinalIgnoreCase);
    }
}
