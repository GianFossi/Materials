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

/// <summary>Schema mutations: create table, create index, add column, rename table.</summary>
public sealed partial class DatabaseViewModel
{

    private void CreateTable()
    {
        if (_workingPath is null) return;

        try
        {
            using var connection = OpenRawConnection();
            using var command = connection.CreateCommand();
            command.CommandText = CreateTableSql;
            var changes = command.ExecuteNonQuery();
            RefreshTables();
            StatusMessage = $"Create table command completed; SQLite reported {changes} changed row(s).";
        }
        catch (Exception ex)
        {
            ShowRawError(ex);
        }
    }

    private async Task CreateTableAsync()
    {
        if (_workingPath is null || IsBusy) return;
        if (WorkingCopyChanged()) { ShowRawError(new IOException("The working database changed externally; reload it before writing.")); return; }
        IsBusy = true;
        try
        {
            var path = _workingPath;
            var sql = CreateTableSql;
            var changes = await Task.Run(() =>
            {
                using var connection = new SqliteConnection(BuildConnectionString(path));
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                return command.ExecuteNonQuery();
            });
            RefreshTables();
            StatusMessage = $"Create table command completed; SQLite reported {changes} changed row(s).";
        }
        catch (Exception ex) { ShowRawError(ex); }
        finally { IsBusy = false; }
    }

    private void RenameTable()
    {
        if (_workingPath is null || SelectedTable is null || !_dialogService.ConfirmDestructiveSql($"ALTER TABLE {QuoteIdentifier(SelectedTable.Name)} RENAME TO {QuoteIdentifier(RenameTableName)}")) return;
        if (!CreateAutomaticBackup(out var backupError)) { ShowRawError(new IOException(backupError)); return; }
        ExecuteSchemaCommand($"ALTER TABLE {QuoteIdentifier(SelectedTable.Name)} RENAME TO {QuoteIdentifier(RenameTableName)}");
    }

    private async Task RenameTableAsync()
    {
        if (_workingPath is null || SelectedTable is null || !_dialogService.ConfirmDestructiveSql($"ALTER TABLE {QuoteIdentifier(SelectedTable.Name)} RENAME TO {QuoteIdentifier(RenameTableName)}")) return;
        if (!CreateAutomaticBackup(out var backupError)) { ShowRawError(new IOException(backupError)); return; }
        await RunSchemaMutationAsync($"ALTER TABLE {QuoteIdentifier(SelectedTable.Name)} RENAME TO {QuoteIdentifier(RenameTableName)}");
    }

    private void AddColumn()
    {
        if (_workingPath is null || SelectedTable is null || !_dialogService.ConfirmDestructiveSql($"ALTER TABLE {QuoteIdentifier(SelectedTable.Name)} ADD COLUMN {AddColumnSql}")) return;
        if (!CreateAutomaticBackup(out var backupError)) { ShowRawError(new IOException(backupError)); return; }
        ExecuteSchemaCommand($"ALTER TABLE {QuoteIdentifier(SelectedTable.Name)} ADD COLUMN {AddColumnSql}");
    }

    private async Task AddColumnAsync()
    {
        if (_workingPath is null || SelectedTable is null || !_dialogService.ConfirmDestructiveSql($"ALTER TABLE {QuoteIdentifier(SelectedTable.Name)} ADD COLUMN {AddColumnSql}")) return;
        if (!CreateAutomaticBackup(out var backupError)) { ShowRawError(new IOException(backupError)); return; }
        await RunSchemaMutationAsync($"ALTER TABLE {QuoteIdentifier(SelectedTable.Name)} ADD COLUMN {AddColumnSql}");
    }

    private void CreateIndex()
    {
        if (_workingPath is null || !_dialogService.ConfirmDestructiveSql(CreateIndexSql)) return;
        if (!CreateAutomaticBackup(out var backupError)) { ShowRawError(new IOException(backupError)); return; }
        ExecuteSchemaCommand(CreateIndexSql);
    }

    private async Task CreateIndexAsync()
    {
        if (_workingPath is null || !_dialogService.ConfirmDestructiveSql(CreateIndexSql)) return;
        if (!CreateAutomaticBackup(out var backupError)) { ShowRawError(new IOException(backupError)); return; }
        await RunSchemaMutationAsync(CreateIndexSql);
    }

    private void ExecuteSchemaCommand(string sql)
    {
        try
        {
            using var connection = OpenRawConnection();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
            RefreshTables();
            CaptureWorkingFingerprint();
            StatusMessage = "Schema change completed on the working copy.";
            RecordAudit("Schema", sql);
        }
        catch (Exception ex) { ShowRawError(ex); }
    }

    private async Task RunSchemaMutationAsync(string sql)
    {
        if (_workingPath is null || IsBusy) return;
        IsBusy = true;
        try
        {
            var path = _workingPath;
            await Task.Run(() =>
            {
                using var connection = new SqliteConnection(BuildConnectionString(path));
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            });
            RefreshTables();
            StatusMessage = "Schema change completed on the working copy.";
            RecordAudit("Schema", SchemaUndo.TryGetInverse(sql, out var inverse) ? $"{sql} | inverse: {inverse}" : $"{sql} | inverse: unsupported");
        }
        catch (Exception ex) { ShowRawError(ex); }
        finally { IsBusy = false; }
    }
}
