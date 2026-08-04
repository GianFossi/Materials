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

/// <summary>Running ad-hoc SQL and managing saved queries.</summary>
public sealed partial class DatabaseViewModel
{

    private void ExecuteSql()
    {
        if (_workingPath is null) return;

        try
        {
            if (IsDestructiveSql(SqlCommandText))
            {
                if (!_dialogService.ConfirmDestructiveSql(SqlCommandText)) return;
                if (!CreateAutomaticBackup(out var backupError))
                {
                    ShowRawError(new IOException(backupError));
                    return;
                }
            }
            SqlHistory.Insert(0, SqlCommandText.Trim());
            while (SqlHistory.Count > 50) SqlHistory.RemoveAt(SqlHistory.Count - 1);
            using var connection = OpenRawConnection();
            using var command = connection.CreateCommand();
            command.CommandText = SqlCommandText;

            if (ReturnsRows(SqlCommandText))
            {
                var table = new DataTable("SqlResults");
                using var reader = command.ExecuteReader();
                table.Load(reader);
                SqlResults = table.DefaultView;
                StatusMessage = $"SQL returned {table.Rows.Count:N0} row(s).";
            }
            else
            {
                var changes = command.ExecuteNonQuery();
                SqlResults = null;
                RefreshTables();
                RefreshMaterials();
                StatusMessage = $"SQL completed; SQLite reported {changes} changed row(s).";
            }
        }
        catch (Exception ex)
        {
            ShowRawError(ex);
        }
    }

    private async Task ExecuteSqlAsync()
    {
        if (_workingPath is null || IsBusy) return;
        var sql = SqlCommandText.Trim();
        if (WorkingCopyChanged())
        {
            ShowRawError(new IOException("The working database changed externally; reload it before executing SQL."));
            return;
        }
        if (IsDestructiveSql(sql))
        {
            if (!_dialogService.ConfirmDestructiveSql(sql)) return;
            if (!CreateAutomaticBackup(out var backupError)) { ShowRawError(new IOException(backupError)); return; }
        }

        IsBusy = true;
        using var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        try
        {
            var path = _workingPath;
            var result = await Task.Run(() => ExecuteSqlCore(path, sql, cancellation.Token), cancellation.Token);
            if (result.Rows is not null)
            {
                SqlResults = result.Rows.DefaultView;
                StatusMessage = $"SQL returned {result.Rows.Rows.Count:N0} row(s).";
            }
            else
            {
                SqlResults = null;
                await RefreshAllAsync();
                CaptureWorkingFingerprint();
                StatusMessage = $"SQL completed; SQLite reported {result.Changes} changed row(s).";
                RecordAudit("SQL", sql);
            }
            SqlHistory.Insert(0, sql);
            while (SqlHistory.Count > 50) SqlHistory.RemoveAt(SqlHistory.Count - 1);
            SaveQueryStore();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "SQL operation cancelled.";
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

    private static SqlExecutionResult ExecuteSqlCore(string path, string sql, CancellationToken cancellationToken)
    {
        using var connection = new SqliteConnection(BuildConnectionString(path));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var cancellationRegistration = cancellationToken.Register(() => command.Cancel());
        if (ReturnsRows(sql))
        {
            var table = new DataTable("SqlResults");
            using var reader = command.ExecuteReader();
            table.Load(reader);
            return new SqlExecutionResult(table, 0);
        }
        return new SqlExecutionResult(null, command.ExecuteNonQuery());
    }

    private void SaveQuery()
    {
        if (string.IsNullOrWhiteSpace(SavedQueryName)) return;
        if (!SavedQueries.Contains(SavedQueryName)) SavedQueries.Add(SavedQueryName);
        _savedQueryTexts[SavedQueryName] = SqlCommandText;
        SaveQueryStore();
        StatusMessage = $"Saved query '{SavedQueryName}'.";
    }

    private void LoadQuery()
    {
        if (_savedQueryTexts.TryGetValue(SavedQueryName, out var query)) SqlCommandText = query;
    }

    private static bool IsDestructiveSql(string sql)
    {
        var text = sql.TrimStart();
        return new[] { "DELETE", "DROP", "ALTER", "UPDATE", "INSERT", "REPLACE", "VACUUM" }
            .Any(keyword => text.StartsWith(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private void LoadQueryStore()
    {
        try
        {
            if (!File.Exists(_queryStorePath)) return;
            var store = System.Text.Json.JsonSerializer.Deserialize<QueryStore>(File.ReadAllText(_queryStorePath));
            if (store is null) return;
            foreach (var item in store.History.Take(50)) SqlHistory.Add(item);
            foreach (var item in store.Saved) { SavedQueries.Add(item.Name); _savedQueryTexts[item.Name] = item.Sql; }
        }
        catch { /* Corrupt preferences must not prevent the database window from opening. */ }
    }

    private void SaveQueryStore()
    {
        try
        {
            var directory = Path.GetDirectoryName(_queryStorePath)!;
            Directory.CreateDirectory(directory);
            var store = new QueryStore(SqlHistory.ToList(), _savedQueryTexts.Select(pair => new SavedQuery(pair.Key, pair.Value)).ToList());
            File.WriteAllText(_queryStorePath, System.Text.Json.JsonSerializer.Serialize(store, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* Preferences are optional and must never break database work. */ }
    }

    private static bool ReturnsRows(string sql)
    {
        var trimmed = sql.TrimStart();
        return trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("PRAGMA", StringComparison.OrdinalIgnoreCase);
    }
}
