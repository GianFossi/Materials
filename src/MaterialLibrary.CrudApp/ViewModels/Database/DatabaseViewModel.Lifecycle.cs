using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using MaterialLibrary.Crud;
using MaterialLibrary.Domain;
using MaterialLibraryCrudApp.Interop;
using MaterialLibraryCrudApp.Services;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>Opening, copying, backing up, and closing the working database.</summary>
public sealed partial class DatabaseViewModel
{

    /// <summary>Decides whether the window may close.</summary>
    /// <returns><c>true</c> to allow closing; <c>false</c> to keep it open.</returns>
    /// <remarks>
    /// Prompts when the raw grid holds uncommitted edits, so closing the manager cannot silently
    /// discard them.
    /// </remarks>
    public bool CanClose() => !HasUnsavedTableChanges || _dialogService.ConfirmDiscardChanges("closing the database window");

    private async Task OpenDatabaseAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        using var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        try
        {
            var path = _dialogService.AskOpenPath("Open material database", FileFilters.Database);
            if (path is null) return;
            await OpenDatabasePathAsync(path, cancellation.Token);
        }
        catch (Exception ex)
        {
            ShowRawError(ex);
        }
        finally
        {
            _operationCancellation = null;
            IsBusy = false;
        }
    }

    private async Task OpenDatabasePathAsync(string path, CancellationToken cancellationToken)
    {
        var source = ResolveSourcePath(path);
        var working = BuildWorkingCopyPath(source);

        var workingPath = working;
        if (!string.Equals(source, working, StringComparison.OrdinalIgnoreCase))
        {
            var copyResult = await Task.Run(() => MaterialDatabaseCrud.createWorkingCopy(source, working), cancellationToken);
            if (!copyResult.TryUnwrap(out workingPath, out var copyError))
            {
                ShowError(copyError);
                return;
            }
        }

        var schemaResult = await Task.Run(() => MaterialDatabaseCrud.ensureSchema(workingPath), cancellationToken);
        if (!schemaResult.TryUnwrap(out var createdTables, out var schemaError))
        {
            ShowError(schemaError);
            return;
        }

        _sourcePath = source;
        _lastSourcePath = source;
        _workingPath = workingPath;
        SaveSessionStore();
        CaptureWorkingFingerprint();
        RaisePropertyChanged(nameof(IsOpen));
        RaisePropertyChanged(nameof(IsReferenceReadOnly));
        RaisePropertyChanged(nameof(ReferenceModeDisplay));
        RaisePropertyChanged(nameof(WorkingPathDisplay));
        RefreshCommandStates();
        await RefreshAllAsync();

        var created = createdTables.ToReadOnlyList();
        StatusMessage = created.Count == 0
            ? $"Opened working copy of {Path.GetFileName(source)}; schema already complete."
            : $"Opened working copy of {Path.GetFileName(source)}; created {created.Count} missing table(s): {string.Join(", ", created)}.";
    }

    /// <summary>
    /// Resolves the source database path selected by the user.
    /// </summary>
    /// <param name="requestedPath">Path picked in the open-file dialog.</param>
    /// <returns>
    /// The best source path: when a <c>.working.db</c> file is picked and its sibling source exists,
    /// the sibling source is returned to avoid chaining into <c>.working.working.db</c> files.
    /// </returns>
    private static string ResolveSourcePath(string requestedPath)
    {
        var fullPath = Path.GetFullPath(requestedPath);
        var directory = Path.GetDirectoryName(fullPath) ?? Path.GetTempPath();
        var extension = Path.GetExtension(fullPath);
        var stem = Path.GetFileNameWithoutExtension(fullPath);

        // If the user picks an existing working copy, prefer its base source when present.
        while (stem.EndsWith(".working", StringComparison.OrdinalIgnoreCase))
        {
            stem = stem[..^".working".Length];
        }

        var candidate = Path.Combine(directory, stem + extension);
        return File.Exists(candidate) ? candidate : fullPath;
    }

    /// <summary>
    /// Builds the deterministic working-copy path beside a source database.
    /// </summary>
    /// <param name="sourcePath">Resolved source database path.</param>
    /// <returns>A sibling path ending in <c>.working</c> before the extension.</returns>
    private static string BuildWorkingCopyPath(string sourcePath)
    {
        var fullPath = Path.GetFullPath(sourcePath);
        var directory = Path.GetDirectoryName(fullPath) ?? Path.GetTempPath();
        var extension = Path.GetExtension(fullPath);
        var stem = Path.GetFileNameWithoutExtension(fullPath);
        return Path.Combine(directory, stem + ".working" + extension);
    }

    private async Task ReopenLastDatabaseAsync()
    {
        if (string.IsNullOrWhiteSpace(_lastSourcePath) || !File.Exists(_lastSourcePath))
        {
            StatusMessage = "The last database is unavailable.";
            return;
        }
        if (IsBusy) return;
        IsBusy = true;
        using var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        try { await OpenDatabasePathAsync(_lastSourcePath, cancellation.Token); }
        catch (Exception ex) { ShowRawError(ex); }
        finally { _operationCancellation = null; IsBusy = false; }
    }

    private async Task BackupWorkingCopyAsync()
    {
        if (_workingPath is null || IsBusy) return;
        if (WorkingCopyChanged()) { ShowRawError(new IOException("The working database changed externally; reload it before writing.")); return; }
        var target = _dialogService.AskSavePath("Back up working database", FileFilters.Database, _workingPath + ".backup");
        if (target is null) return;

        IsBusy = true;
        using var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        try
        {
            var source = _workingPath;
            var result = await Task.Run(() => MaterialDatabaseCrud.createWorkingCopy(source, target), cancellation.Token);
            if (result.TryUnwrap(out var saved, out var error))
                StatusMessage = $"Backed up working database to {saved}.";
            else
                ShowError(error);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Database operation cancelled.";
        }
        catch (Exception ex)
        {
            ShowRawError(ex);
        }
        finally
        {
            _operationCancellation = null;
            IsBusy = false;
        }
    }

    private void CancelOperation()
    {
        _operationCancellation?.Cancel();
        StatusMessage = "Cancelling database operation...";
    }

    /// <summary>
    /// Starts background work from a synchronous context, observing any failure.
    /// </summary>
    /// <param name="work">Asynchronous work to start.</param>
    /// <param name="context">Short description used in the log entry and the status message.</param>
    /// <remarks>
    /// Property setters and synchronous command handlers cannot await, so the task they start would
    /// otherwise be discarded. A discarded task swallows its exception until finalization, meaning a
    /// failed refresh left the grid stale with no message anywhere. Routing every such call through
    /// this helper guarantees the failure reaches both the log and the user.
    /// </remarks>
    private void RunDetached(Func<Task> work, string context)
    {
        _ = ObserveAsync(work, context);
    }

    /// <summary>Awaits detached work and reports its outcome.</summary>
    /// <param name="work">Asynchronous work to await.</param>
    /// <param name="context">Short description used in the log entry and the status message.</param>
    /// <returns>A task that always completes successfully; failures are reported, not propagated.</returns>
    private async Task ObserveAsync(Func<Task> work, string context)
    {
        try
        {
            await work();
        }
        catch (OperationCanceledException)
        {
            // Cancellation is a normal outcome of the Cancel button.
            AppLog.Current.Information($"{context} was cancelled.");
        }
        catch (Exception ex)
        {
            AppLog.Current.Error($"{context} failed.", ex);
            StatusMessage = $"{context} failed: {ex.Message}";
        }
    }

    private async Task RefreshAllAsync()
    {
        await RefreshMaterialsAsync();
        RefreshTables();
    }

    private bool CreateAutomaticBackup(out string error)
    {
        error = string.Empty;
        if (_workingPath is null) return true;
        var directory = Path.GetDirectoryName(_workingPath) ?? Path.GetTempPath();
        var name = Path.GetFileNameWithoutExtension(_workingPath);
        var backup = Path.Combine(directory, $"{name}.pre-sql-{DateTime.Now:yyyyMMdd-HHmmss-fff}.db");
        var result = MaterialDatabaseCrud.createWorkingCopy(_workingPath, backup);
        if (result.TryUnwrap(out _, out var materialError))
        {
            try
            {
                var backups = Directory.GetFiles(directory, $"{name}.pre-sql-*.db")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .Skip(10);
                foreach (var oldBackup in backups) File.Delete(oldBackup);
            }
            catch { /* Cleanup is best effort; the new backup remains authoritative. */ }
            return true;
        }
        error = MaterialErrorFormat.Format(materialError);
        return false;
    }

    private void CaptureWorkingFingerprint()
    {
        if (_workingPath is null || !File.Exists(_workingPath)) return;
        var info = new FileInfo(_workingPath);
        _workingLastWriteUtc = info.LastWriteTimeUtc;
        _workingLength = info.Length;
    }

    private bool WorkingCopyChanged()
    {
        if (_workingPath is null || !File.Exists(_workingPath)) return false;
        var info = new FileInfo(_workingPath);
        return info.LastWriteTimeUtc != _workingLastWriteUtc || info.Length != _workingLength;
    }

    /// <summary>Copies the working file to a permanent location chosen by the user.</summary>
    /// <remarks>
    /// The dialog is pre-filled with a distinct "<c>.edited</c>" name beside the original rather than
    /// with the original path itself. Suggesting the source meant that confirming the dialog without
    /// editing the filename overwrote the pristine reference database - precisely the file the
    /// working-copy design exists to protect. Choosing the source is still possible, but it is now a
    /// deliberate act and is confirmed explicitly.
    /// </remarks>
    private void SaveWorkingCopyAs()
    {
        if (_workingPath is null)
        {
            return;
        }

        var target = _dialogService.AskSavePath("Save database as", FileFilters.Database, SuggestedSaveAsPath());
        if (target is null)
        {
            return;
        }

        if (IsSameFile(target, _sourcePath)
            && !_dialogService.ConfirmOverwriteReference(target))
        {
            StatusMessage = "Save cancelled; the original database was left untouched.";
            return;
        }

        if (!MaterialDatabaseCrud.createWorkingCopy(_workingPath, target).TryUnwrap(out var saved, out var error))
        {
            ShowError(error);
            return;
        }

        AppLog.Current.Information($"Working copy saved to '{saved}'.");
        StatusMessage = $"Saved database to {saved}.";
    }

    /// <summary>Builds the filename offered by the Save As dialog.</summary>
    /// <returns>
    /// A path beside the original with an "<c>.edited</c>" suffix, or <c>null</c> when no source is
    /// known and the dialog should open with no suggestion.
    /// </returns>
    /// <remarks>
    /// If that name is already taken, a numeric suffix is appended, so repeated saves never propose
    /// silently overwriting a file the user saved earlier either.
    /// </remarks>
    private string? SuggestedSaveAsPath()
    {
        if (string.IsNullOrWhiteSpace(_sourcePath))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(_sourcePath));
        var stem = Path.GetFileNameWithoutExtension(_sourcePath);
        var extension = Path.GetExtension(_sourcePath);

        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var candidate = Path.Combine(directory, $"{stem}.edited{extension}");

        for (var attempt = 2; File.Exists(candidate) && attempt < 100; attempt++)
        {
            candidate = Path.Combine(directory, $"{stem}.edited-{attempt}{extension}");
        }

        return candidate;
    }

    /// <summary>Reports whether two paths designate the same file on disk.</summary>
    /// <param name="left">First path.</param>
    /// <param name="right">Second path, which may be <c>null</c>.</param>
    /// <returns><c>true</c> when both resolve to the same full path.</returns>
    /// <remarks>
    /// Compared after full-path normalisation so that a relative path, a trailing separator, or a
    /// different letter case cannot slip past the guard on Windows.
    /// </remarks>
    private static bool IsSameFile(string left, string? right)
    {
        if (string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            // An unparsable path cannot be the source file.
            return false;
        }
    }

    private SqliteConnection OpenRawConnection()
    {
        var connection = new SqliteConnection($"Data Source={_workingPath}");
        connection.Open();
        return connection;
    }

    private async Task IntegrityCheckAsync()
    {
        if (_workingPath is null || IsBusy) return;
        IsBusy = true;
        try
        {
            var path = _workingPath;
            var result = await Task.Run(() =>
            {
                using var connection = new SqliteConnection($"Data Source={path}");
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA integrity_check";
                return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture) ?? "unknown";
            });
            StatusMessage = result.Equals("ok", StringComparison.OrdinalIgnoreCase)
                ? "SQLite integrity check passed."
                : $"SQLite integrity check reported: {result}";
        }
        catch (Exception ex)
        {
            ShowRawError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
