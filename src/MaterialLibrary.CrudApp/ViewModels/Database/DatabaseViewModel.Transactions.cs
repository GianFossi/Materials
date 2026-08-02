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

/// <summary>Undo/redo of committed transactions, plus the audit and session stores.</summary>
public sealed partial class DatabaseViewModel
{

    private void RecordAudit(string operation, string target)
    {
        _auditLog.Insert(0, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {operation} | {target}");
        while (_auditLog.Count > 100) _auditLog.RemoveAt(_auditLog.Count - 1);
        SaveAuditStore();
    }

    private void LoadAuditStore()
    {
        try
        {
            if (!File.Exists(_auditStorePath)) return;
            var entries = System.Text.Json.JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_auditStorePath));
            if (entries is null) return;
            foreach (var entry in entries.Take(100)) _auditLog.Add(entry);
        }
        catch { }
    }

    private void SaveAuditStore()
    {
        try
        {
            var directory = Path.GetDirectoryName(_auditStorePath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(_auditStorePath, System.Text.Json.JsonSerializer.Serialize(_auditLog.ToList()));
        }
        catch { }
    }

    private void LoadSessionStore()
    {
        try
        {
            if (!File.Exists(_sessionStorePath)) return;
            _lastSourcePath = System.Text.Json.JsonSerializer.Deserialize<SessionStore>(File.ReadAllText(_sessionStorePath))?.SourcePath;
            ReopenLastDatabaseCommand.RaiseCanExecuteChanged();
        }
        catch { }
    }

    private void SaveSessionStore()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_sessionStorePath)!);
            File.WriteAllText(_sessionStorePath, System.Text.Json.JsonSerializer.Serialize(new SessionStore(_lastSourcePath)));
            ReopenLastDatabaseCommand.RaiseCanExecuteChanged();
        }
        catch { }
    }

    private async Task RedoLastTransactionAsync()
    {
        if (_workingPath is null || HasUnsavedTableChanges) return;
        var entry = _transactionJournal.LoadRedo().LastOrDefault(item => string.Equals(item.DatabasePath, _workingPath, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return;
        IsBusy = true;
        try
        {
            var path = _workingPath;
            await Task.Run(() => TransactionReverter.Apply(path, entry));
            _transactionJournal.RemoveRedo(entry);
            _transactionJournal.Append(entry);
            CaptureWorkingFingerprint();
            _transactionHistory = _transactionJournal.Load();
            RaisePropertyChanged(nameof(TransactionHistory));
            await RefreshSelectedTableAsync();
            await RefreshMaterialsAsync();
            StatusMessage = $"Redid the transaction on {entry.TableName}.";
            RecordAudit("Redo transaction", entry.TableName);
        }
        catch (Exception ex) { ShowRawError(ex); }
        finally { IsBusy = false; RefreshEditCommands(); }
    }

    private async Task UndoLastTransactionAsync()
    {
        if (_workingPath is null || HasUnsavedTableChanges) return;
        var entry = TransactionHistory.LastOrDefault(item => string.Equals(item.DatabasePath, _workingPath, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return;
        IsBusy = true;
        try
        {
            var path = _workingPath;
            await Task.Run(() => TransactionReverter.Revert(path, entry));
            CaptureWorkingFingerprint();
            _transactionJournal.PushRedo(entry);
            _transactionJournal.Remove(entry);
            _transactionHistory = _transactionJournal.Load();
            RaisePropertyChanged(nameof(TransactionHistory));
            await RefreshSelectedTableAsync();
            await RefreshMaterialsAsync();
            StatusMessage = $"Undid the transaction on {entry.TableName}.";
            RecordAudit("Undo transaction", entry.TableName);
        }
        catch (Exception ex) { ShowRawError(ex); }
        finally { IsBusy = false; RefreshEditCommands(); }
    }
}
