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

/// <summary>Browsing, paging, and editing raw table rows.</summary>
public sealed partial class DatabaseViewModel
{

    private void RefreshTables()
    {
        Tables.Clear();
        TableRows = null;
        _currentTable = null;
        TableColumns.Clear();
        TableSchema.Clear();
        TableForeignKeys.Clear();
        _currentPrimaryKeyColumns.Clear();
        TableRowCount = 0;
        if (_workingPath is null)
        {
            SelectedTable = null;
            return;
        }

        try
        {
            using var connection = OpenRawConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT name, type
                FROM sqlite_master
                WHERE type IN ('table', 'view')
                ORDER BY type, name
                """;

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var table = new DatabaseTableViewModel(reader.GetString(0), reader.GetString(1));
                if (string.IsNullOrWhiteSpace(TableSearch)
                    || table.Name.Contains(TableSearch, StringComparison.OrdinalIgnoreCase))
                    Tables.Add(table);
            }

            SelectedTable = Tables.FirstOrDefault(t => string.Equals(t.Name, "Materials", StringComparison.OrdinalIgnoreCase))
                ?? Tables.FirstOrDefault();
        }
        catch (Exception ex)
        {
            ShowRawError(ex);
        }
    }

    private async Task RefreshSelectedTableAsync()
    {
        if (_workingPath is null || SelectedTable is null) return;
        var path = _workingPath;
        var tableName = SelectedTable.Name;
        var kind = SelectedTable.Kind;
        var pageSize = TablePageSize;
        var offset = TableOffset;
        try
        {
            var result = await Task.Run(() =>
            {
                using var connection = new SqliteConnection($"Data Source={path}");
                connection.Open();
                var sourceColumns = GetColumnNames(connection, tableName);
                var count = CountRows(connection, tableName, sourceColumns, _rowFilter, _materialIdFilter);
                var table = new DataTable(tableName);
                using var command = connection.CreateCommand();
                var where = BuildSqlSearch(sourceColumns, _rowFilter, _materialIdFilter);
                var order = string.IsNullOrWhiteSpace(_sortColumn) || !sourceColumns.Contains(_sortColumn)
                    ? string.Empty
                    : $" ORDER BY {QuoteIdentifier(_sortColumn)}{(_sortDescending ? " DESC" : " ASC")}";
                command.CommandText = kind == "table"
                    ? $"SELECT rowid AS __rowid, * FROM {QuoteIdentifier(tableName)}" + where + order + " LIMIT $limit OFFSET $offset"
                    : $"SELECT * FROM {QuoteIdentifier(tableName)}" + where + order + " LIMIT $limit OFFSET $offset";
                BindSearchParameters(command, sourceColumns, _rowFilter, _materialIdFilter);
                command.Parameters.AddWithValue("$limit", pageSize);
                command.Parameters.AddWithValue("$offset", offset);
                try
                {
                    using var reader = command.ExecuteReader();
                    table.Load(reader);
                }
                catch (SqliteException) when (kind == "table")
                {
                    table.Clear();
                    using var fallback = connection.CreateCommand();
                    fallback.CommandText = $"SELECT * FROM {QuoteIdentifier(tableName)}" + where + order + " LIMIT $limit OFFSET $offset";
                    BindSearchParameters(fallback, sourceColumns, _rowFilter, _materialIdFilter);
                    if (!string.IsNullOrWhiteSpace(_rowFilter)) fallback.Parameters.AddWithValue("$search", "%" + _rowFilter + "%");
                    fallback.Parameters.AddWithValue("$limit", pageSize);
                    fallback.Parameters.AddWithValue("$offset", offset);
                    using var reader = fallback.ExecuteReader();
                    table.Load(reader);
                }
                return (table, count);
            });
            if (_workingPath != path || SelectedTable?.Name != tableName) return;
            TableRowCount = result.count;
            result.table.AcceptChanges();
            _currentTable = result.table;
            TableRows = result.table.DefaultView;
            using (var schemaConnection = OpenRawConnection())
            {
                LoadSchema(schemaConnection, tableName);
            }
            RefreshColumnList(result.table);
            RefreshTableCommands();
            StatusMessage = $"Browsing {tableName}: {TablePageDisplay}";
        }
        catch (Exception ex) { ShowRawError(ex); }
    }

    private void RefreshSelectedTable()
    {
        if (_workingPath is null || SelectedTable is null)
        {
            TableRows = null;
            _currentTable = null;
            TableRowCount = 0;
            RaisePropertyChanged(nameof(CanEditSelectedTable));
            return;
        }

        try
        {
            using var connection = OpenRawConnection();
            var sourceColumns = GetColumnNames(connection, SelectedTable.Name);
            TableRowCount = CountRows(connection, SelectedTable.Name, sourceColumns, _rowFilter, _materialIdFilter);

            var table = new DataTable(SelectedTable.Name);
            using var command = connection.CreateCommand();
            var where = BuildSqlSearch(sourceColumns, _rowFilter, _materialIdFilter);
            var order = string.IsNullOrWhiteSpace(_sortColumn) || !sourceColumns.Contains(_sortColumn)
                ? string.Empty
                : $" ORDER BY {QuoteIdentifier(_sortColumn)}{(_sortDescending ? " DESC" : " ASC")}";
            command.CommandText = (SelectedTable.Kind == "table"
                ? $"SELECT rowid AS __rowid, * FROM {QuoteIdentifier(SelectedTable.Name)}"
                : $"SELECT * FROM {QuoteIdentifier(SelectedTable.Name)}")
                + where + order + " LIMIT $limit OFFSET $offset";
            BindSearchParameters(command, sourceColumns, _rowFilter, _materialIdFilter);
            command.Parameters.AddWithValue("$limit", TablePageSize);
            command.Parameters.AddWithValue("$offset", TableOffset);

            try
            {
                using var reader = command.ExecuteReader();
                table.Load(reader);
            }
            catch (SqliteException) when (SelectedTable.Kind == "table")
            {
                // WITHOUT ROWID tables are browseable, but their rows cannot be updated through
                // the generic editor because there is no stable rowid address.
                table.Clear();
                using var fallback = connection.CreateCommand();
                fallback.CommandText = $"SELECT * FROM {QuoteIdentifier(SelectedTable.Name)}" + where + order + " LIMIT $limit OFFSET $offset";
                BindSearchParameters(fallback, sourceColumns, _rowFilter, _materialIdFilter);
                if (!string.IsNullOrWhiteSpace(_rowFilter)) fallback.Parameters.AddWithValue("$search", "%" + _rowFilter + "%");
                fallback.Parameters.AddWithValue("$limit", TablePageSize);
                fallback.Parameters.AddWithValue("$offset", TableOffset);
                using var reader = fallback.ExecuteReader();
                table.Load(reader);
            }
            table.AcceptChanges();
            _tableUndoSnapshot = table.Copy();
            _tableRedoSnapshot = null;
            table.RowChanged += (_, args) =>
            {
                if (args.Action is DataRowAction.Add or DataRowAction.Change or DataRowAction.Delete)
                    HasUnsavedTableChanges = true;
            };
            table.RowDeleted += (_, _) => HasUnsavedTableChanges = true;
            _currentTable = table;
            HasUnsavedTableChanges = false;
            RefreshEditCommands();
            RaisePropertyChanged(nameof(CanEditSelectedTable));
            RefreshColumnList(table);
            TableRows = table.DefaultView;
            LoadSchema(connection, SelectedTable.Name);
            RaisePropertyChanged(nameof(CanEditSelectedTable));
            ApplyTableView();
            StatusMessage = $"Browsing {SelectedTable.Name}: {TablePageDisplay}";
            RefreshTableCommands();
        }
        catch (Exception ex)
        {
            ShowRawError(ex);
        }
    }

    private void PreviousTablePage()
    {
        TableOffset = Math.Max(0, TableOffset - TablePageSize);
        RunDetached(RefreshSelectedTableAsync, "Loading previous page");
    }

    private void NextTablePage()
    {
        TableOffset = Math.Min(Math.Max(0, TableRowCount - 1), TableOffset + TablePageSize);
        RunDetached(RefreshSelectedTableAsync, "Loading next page");
    }

    private void SaveTableChanges()
    {
        if (_workingPath is null || SelectedTable is null || _currentTable is null || !CanEditSelectedTable) return;

        try
        {
            using var connection = OpenRawConnection();
            using var transaction = connection.BeginTransaction();
            var changed = 0;

            foreach (DataRow row in _currentTable.Rows)
            {
                changed += row.RowState switch
                {
                    DataRowState.Added => InsertRawRow(connection, transaction, SelectedTable.Name, row),
                    DataRowState.Modified => UpdateRawRow(connection, transaction, SelectedTable.Name, row),
                    DataRowState.Deleted => DeleteRawRow(connection, transaction, SelectedTable.Name, row),
                    _ => 0
                };
            }

            transaction.Commit();
            HasUnsavedTableChanges = false;
            _tableUndoSnapshot = _currentTable.Copy();
            _tableRedoSnapshot = null;
            RefreshEditCommands();

            // The refresh also writes StatusMessage, so the completion message is set after it
            // rather than before; otherwise whichever finished last won the race.
            var savedMessage = $"Saved {changed} raw table change(s) to the working copy.";
            RunDetached(
                async () =>
                {
                    await RefreshSelectedTableAsync();
                    await RefreshMaterialsAsync();
                    StatusMessage = savedMessage;
                },
                "Refreshing after save");
        }
        catch (Exception ex)
        {
            ShowRawError(ex);
        }
    }

    private async Task SaveTableChangesAsync()
    {
        if (_workingPath is null || SelectedTable is null || _currentTable is null || !CanEditSelectedTable) return;
        var changes = SnapshotChanges(_currentTable);
        if (changes.Count == 0) return;
        if (_currentPrimaryKeyColumns.Count > 0 && HasDuplicatePrimaryKeys(_currentTable, _currentPrimaryKeyColumns))
        {
            StatusMessage = "Cannot save: duplicate primary-key values exist in the edited page.";
            return;
        }
        var path = _workingPath;
        var tableName = SelectedTable.Name;
        var primaryKeys = _currentPrimaryKeyColumns.ToList();
        IsBusy = true;
        try
        {
            var commit = await Task.Run(() => ApplyRawChanges(path, tableName, primaryKeys, changes));
            var changed = commit.Changed;
            _transactionJournal.Append(new TransactionJournalEntry(
                DateTimeOffset.UtcNow,
                path,
                tableName,
                commit.Changes.Select(change => new TransactionRowChange(
                    change.State.ToString(),
                    ToJsonValues(change.Original),
                    ToJsonValues(change.Current),
                    change.RowId)).ToList()));
            _transactionJournal.ClearRedo();
            _transactionHistory = _transactionJournal.Load();
            RaisePropertyChanged(nameof(TransactionHistory));
            CaptureWorkingFingerprint();
            HasUnsavedTableChanges = false;
            await RefreshSelectedTableAsync();
            await RefreshMaterialsAsync();
            StatusMessage = $"Saved {changed} raw table change(s) to the working copy.";
            RecordAudit("Raw table save", $"{tableName} ({changed} change(s))");
        }
        catch (Exception ex) { ShowRawError(ex); }
        finally { IsBusy = false; }
    }

    private static List<RawRowChange> SnapshotChanges(DataTable table)
    {
        var changes = new List<RawRowChange>();
        foreach (DataRow row in table.Rows)
        {
            if (row.RowState is not (DataRowState.Added or DataRowState.Modified or DataRowState.Deleted)) continue;
            var columns = table.Columns.Cast<DataColumn>().Where(c => c.ColumnName != "__rowid").ToList();
            var current = row.RowState == DataRowState.Deleted
                ? new Dictionary<string, object?>()
                : columns.ToDictionary(c => c.ColumnName, c => (object?)NormalizeDbValue(row[c, DataRowVersion.Current]));
            var original = columns.ToDictionary(c => c.ColumnName, c => (object?)NormalizeDbValue(row[c, DataRowVersion.Original]));
            var rowId = row.Table.Columns.Contains("__rowid") && row.RowState != DataRowState.Added ? Convert.ToInt64(row["__rowid", DataRowVersion.Original], CultureInfo.InvariantCulture) : (long?)null;
            changes.Add(new RawRowChange(row.RowState, current, original, rowId));
        }
        return changes;
    }

    private static IReadOnlyDictionary<string, JsonElement> ToJsonValues(IReadOnlyDictionary<string, object?> values)
    {
        return values.ToDictionary(
            pair => pair.Key,
            pair => JsonSerializer.SerializeToElement(pair.Value, pair.Value?.GetType() ?? typeof(object)));
    }

    private static bool HasDuplicatePrimaryKeys(DataTable table, IReadOnlyList<string> keys)
    {
        return table.Rows.Cast<DataRow>()
            .Where(row => row.RowState != DataRowState.Deleted)
            .Select(row => string.Join("\u001F", keys.Select(key => Convert.ToString(row[key], CultureInfo.InvariantCulture) ?? string.Empty)))
            .GroupBy(value => value, StringComparer.Ordinal)
            .Any(group => group.Count() > 1);
    }

    private static RawCommitResult ApplyRawChanges(string path, string tableName, IReadOnlyList<string> primaryKeys, IReadOnlyList<RawRowChange> changes)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var changed = 0;
        var committed = new List<RawRowChange>();
        foreach (var change in changes)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            if (change.State == DataRowState.Added)
            {
                var columns = change.Current.Keys.ToList();
                command.CommandText = $"INSERT INTO {QuoteIdentifier(tableName)} ({string.Join(", ", columns.Select(QuoteIdentifier))}) VALUES ({string.Join(", ", columns.Select((_, i) => "$p" + i))})";
                for (var i = 0; i < columns.Count; i++) command.Parameters.AddWithValue("$p" + i, change.Current[columns[i]] ?? DBNull.Value);
            }
            else
            {
                var usePk = primaryKeys.Count > 0 && change.RowId is null;
                var keys = usePk ? primaryKeys : new List<string>();
                command.CommandText = change.State == DataRowState.Deleted
                    ? $"DELETE FROM {QuoteIdentifier(tableName)} WHERE " + (usePk ? string.Join(" AND ", keys.Select((k, i) => $"{QuoteIdentifier(k)} = $k{i}")) : "rowid = $rowid")
                    : $"UPDATE {QuoteIdentifier(tableName)} SET " + string.Join(", ", change.Current.Keys.Select((k, i) => $"{QuoteIdentifier(k)} = $p{i}")) + " WHERE " + (usePk ? string.Join(" AND ", keys.Select((k, i) => $"{QuoteIdentifier(k)} = $k{i}")) : "rowid = $rowid");
                if (change.State == DataRowState.Modified) foreach (var pair in change.Current.Select((p, i) => (p, i))) command.Parameters.AddWithValue("$p" + pair.i, pair.p.Value ?? DBNull.Value);
                if (usePk) foreach (var pair in keys.Select((k, i) => (k, i))) command.Parameters.AddWithValue("$k" + pair.i, change.Original[pair.k] ?? DBNull.Value);
                else command.Parameters.AddWithValue("$rowid", change.RowId!.Value);
            }
            changed += command.ExecuteNonQuery();
            if (change.State == DataRowState.Added && change.RowId is null)
            {
                using var identity = connection.CreateCommand();
                identity.Transaction = transaction;
                identity.CommandText = "SELECT last_insert_rowid()";
                var rowId = Convert.ToInt64(identity.ExecuteScalar(), CultureInfo.InvariantCulture);
                committed.Add(change with { RowId = rowId });
            }
            else
            {
                committed.Add(change);
            }
        }
        using (var foreignKeys = connection.CreateCommand())
        {
            foreignKeys.Transaction = transaction;
            foreignKeys.CommandText = "PRAGMA foreign_key_check";
            using var violations = foreignKeys.ExecuteReader();
            if (violations.Read())
                throw new InvalidOperationException("Cannot commit raw-table changes: the transaction would violate a foreign-key constraint.");
        }
        transaction.Commit();
        return new RawCommitResult(changed, committed);
    }

    private static int CountRows(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {QuoteIdentifier(tableName)}";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>Counts the rows a table would return under the current restrictions.</summary>
    /// <param name="connection">Open connection.</param>
    /// <param name="tableName">Table to count.</param>
    /// <param name="columns">Columns of that table.</param>
    /// <param name="filter">Free-text filter.</param>
    /// <param name="materialId">Material restriction, or <c>null</c>.</param>
    /// <returns>Row count, used to drive paging.</returns>
    private static int CountRows(
        SqliteConnection connection,
        string tableName,
        IReadOnlyList<string> columns,
        string filter,
        long? materialId = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {QuoteIdentifier(tableName)}" + BuildSqlSearch(columns, filter, materialId);
        BindSearchParameters(command, columns, filter, materialId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static List<string> GetColumnNames(SqliteConnection connection, string tableName)
    {
        var columns = new List<string>();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({QuoteIdentifier(tableName)})";
        using var reader = command.ExecuteReader();
        while (reader.Read()) columns.Add(reader.GetString(1));
        return columns;
    }

    /// <summary>Name of the column that links a raw table back to a material.</summary>
    private const string MaterialLinkColumn = "MaterialID";

    /// <summary>Builds the WHERE clause for the free-text search and the material link.</summary>
    /// <param name="columns">Columns of the table being queried.</param>
    /// <param name="filter">Free-text filter; matched against every column as text.</param>
    /// <param name="materialId">Material to restrict to, or <c>null</c> for no restriction.</param>
    /// <returns>A WHERE clause, or an empty string when neither restriction applies.</returns>
    /// <remarks>
    /// The two restrictions are combined with AND, so following a material and then typing a search
    /// narrows within that material rather than escaping it. The material link is an exact
    /// comparison on <c>MaterialID</c>, not a text match, so filtering on material 77 cannot also
    /// match a stress value that happens to contain "77".
    /// </remarks>
    private static string BuildSqlSearch(IReadOnlyList<string> columns, string filter, long? materialId = null)
    {
        var clauses = new List<string>();

        if (!string.IsNullOrWhiteSpace(filter) && columns.Count > 0)
        {
            clauses.Add("(" + string.Join(" OR ", columns.Select(c => $"CAST({QuoteIdentifier(c)} AS TEXT) LIKE $search")) + ")");
        }

        if (materialId is not null && columns.Contains(MaterialLinkColumn, StringComparer.OrdinalIgnoreCase))
        {
            clauses.Add($"{QuoteIdentifier(MaterialLinkColumn)} = $materialId");
        }

        return clauses.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", clauses);
    }

    /// <summary>Binds the parameters used by <see cref="BuildSqlSearch"/>.</summary>
    /// <param name="command">Command being built.</param>
    /// <param name="columns">Columns of the table being queried.</param>
    /// <param name="filter">Free-text filter.</param>
    /// <param name="materialId">Material restriction, or <c>null</c>.</param>
    private static void BindSearchParameters(
        SqliteCommand command,
        IReadOnlyList<string> columns,
        string filter,
        long? materialId)
    {
        if (!string.IsNullOrWhiteSpace(filter) && columns.Count > 0)
        {
            command.Parameters.AddWithValue("$search", "%" + filter + "%");
        }

        if (materialId is not null && columns.Contains(MaterialLinkColumn, StringComparer.OrdinalIgnoreCase))
        {
            command.Parameters.AddWithValue("$materialId", materialId.Value);
        }
    }

    private static int InsertRawRow(SqliteConnection connection, SqliteTransaction transaction, string tableName, DataRow row)
    {
        var columns = row.Table.Columns.Cast<DataColumn>().Where(c => c.ColumnName != "__rowid").ToList();
        if (columns.Count == 0) return 0;

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"INSERT INTO {QuoteIdentifier(tableName)} ({string.Join(", ", columns.Select(c => QuoteIdentifier(c.ColumnName)))}) " +
            $"VALUES ({string.Join(", ", columns.Select((_, i) => "$p" + i.ToString(CultureInfo.InvariantCulture)))})";

        for (var i = 0; i < columns.Count; i++)
        {
            command.Parameters.AddWithValue("$p" + i.ToString(CultureInfo.InvariantCulture), NormalizeDbValue(row[columns[i]]));
        }

        return command.ExecuteNonQuery();
    }

    private int UpdateRawRow(SqliteConnection connection, SqliteTransaction transaction, string tableName, DataRow row)
    {
        var columns = row.Table.Columns.Cast<DataColumn>().Where(c => c.ColumnName != "__rowid").ToList();
        if (columns.Count == 0) return 0;

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"UPDATE {QuoteIdentifier(tableName)} SET {string.Join(", ", columns.Select((c, i) => $"{QuoteIdentifier(c.ColumnName)} = $p{i.ToString(CultureInfo.InvariantCulture)}"))} " +
            ((_currentPrimaryKeyColumns.Count > 0 && !row.Table.Columns.Contains("__rowid"))
                ? "WHERE " + string.Join(" AND ", _currentPrimaryKeyColumns.Select((c, i) => $"{QuoteIdentifier(c)} = $key{i}"))
                : "WHERE rowid = $rowid");

        for (var i = 0; i < columns.Count; i++)
        {
            command.Parameters.AddWithValue("$p" + i.ToString(CultureInfo.InvariantCulture), NormalizeDbValue(row[columns[i], DataRowVersion.Current]));
        }

        if (_currentPrimaryKeyColumns.Count > 0 && !row.Table.Columns.Contains("__rowid"))
        {
            for (var i = 0; i < _currentPrimaryKeyColumns.Count; i++)
                command.Parameters.AddWithValue("$key" + i, NormalizeDbValue(row[_currentPrimaryKeyColumns[i], DataRowVersion.Original]));
        }
        else if (TryGetRowId(row, DataRowVersion.Original, out var rowId))
            command.Parameters.AddWithValue("$rowid", rowId);
        else return 0;
        return command.ExecuteNonQuery();
    }

    private int DeleteRawRow(SqliteConnection connection, SqliteTransaction transaction, string tableName, DataRow row)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (_currentPrimaryKeyColumns.Count > 0 && !row.Table.Columns.Contains("__rowid"))
        {
            command.CommandText = $"DELETE FROM {QuoteIdentifier(tableName)} WHERE " + string.Join(" AND ", _currentPrimaryKeyColumns.Select((c, i) => $"{QuoteIdentifier(c)} = $key{i}"));
            for (var i = 0; i < _currentPrimaryKeyColumns.Count; i++)
                command.Parameters.AddWithValue("$key" + i, NormalizeDbValue(row[_currentPrimaryKeyColumns[i], DataRowVersion.Original]));
        }
        else
        {
            if (!TryGetRowId(row, DataRowVersion.Original, out var rowId)) return 0;
            command.CommandText = $"DELETE FROM {QuoteIdentifier(tableName)} WHERE rowid = $rowid";
            command.Parameters.AddWithValue("$rowid", rowId);
        }
        return command.ExecuteNonQuery();
    }

    private void RefreshColumnList(DataTable table)
    {
        TableColumns.Clear();
        foreach (DataColumn column in table.Columns)
        {
            if (column.ColumnName != "__rowid") TableColumns.Add(column.ColumnName);
        }

        PlotXColumn = TableColumns.Contains(PlotXColumn) ? PlotXColumn : TableColumns.FirstOrDefault() ?? string.Empty;
        PlotYColumn = TableColumns.Contains(PlotYColumn) ? PlotYColumn : TableColumns.Skip(1).FirstOrDefault() ?? PlotXColumn;
        PlotTableCommand.RaiseCanExecuteChanged();
    }

    private void ApplyTableView()
    {
        if (_currentTable is null) return;
        var view = _currentTable.DefaultView;
        try
        {
            view.RowFilter = BuildRowFilter(_rowFilter, _currentTable.Columns);
            view.Sort = string.IsNullOrWhiteSpace(_sortColumn)
                ? string.Empty
                : QuoteViewIdentifier(_sortColumn) + (_sortDescending ? " DESC" : " ASC");
            TableRows = view;
        }
        catch (EvaluateException)
        {
            StatusMessage = "Filter expression is invalid.";
        }
    }

    private void LoadSchema(SqliteConnection connection, string tableName)
    {
        TableSchema.Clear();
        TableForeignKeys.Clear();
        _currentPrimaryKeyColumns.Clear();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({QuoteIdentifier(tableName)})";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetInt32(5) > 0) _currentPrimaryKeyColumns.Add(reader.GetString(1));
            TableSchema.Add(new DatabaseColumnViewModel(
                reader.GetString(1),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                reader.GetInt32(3) == 0,
                reader.GetInt32(5) > 0,
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4)));
        }

        using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = $"PRAGMA foreign_key_list({QuoteIdentifier(tableName)})";
        using var fkReader = foreignKeys.ExecuteReader();
        while (fkReader.Read())
        {
            TableForeignKeys.Add(new DatabaseForeignKeyViewModel(
                fkReader.GetString(2), fkReader.GetString(3), fkReader.GetString(4)));
        }
    }

    private void ClearTableTools()
    {
        TableSearch = string.Empty;
        RowFilter = string.Empty;
        SortColumn = string.Empty;
        SortDescending = false;
        RunDetached(RefreshSelectedTableAsync, "Reloading table");
    }

    private void UndoTable()
    {
        if (_currentTable is null || _tableUndoSnapshot is null) return;
        _tableRedoSnapshot = _currentTable.Copy();
        ReplaceCurrentTable(_tableUndoSnapshot.Copy());
        StatusMessage = "Undid raw-table changes for the current page.";
    }

    private void RedoTable()
    {
        if (_currentTable is null || _tableRedoSnapshot is null) return;
        _tableUndoSnapshot = _currentTable.Copy();
        ReplaceCurrentTable(_tableRedoSnapshot.Copy());
        _tableRedoSnapshot = null;
        StatusMessage = "Redid raw-table changes for the current page.";
    }

    private void DiscardTableChanges()
    {
        if (_tableUndoSnapshot is null) return;
        ReplaceCurrentTable(_tableUndoSnapshot.Copy());
        _tableRedoSnapshot = null;
        StatusMessage = "Discarded unsaved raw-table changes.";
    }

    private void ReplaceCurrentTable(DataTable table)
    {
        table.AcceptChanges();
        _currentTable = table;
        TableRows = table.DefaultView;
        HasUnsavedTableChanges = false;
        RefreshEditCommands();
    }

    private void RefreshEditCommands()
    {
        UndoTableCommand.RaiseCanExecuteChanged();
        RedoTableCommand.RaiseCanExecuteChanged();
        DiscardTableChangesCommand.RaiseCanExecuteChanged();
        UndoLastTransactionCommand.RaiseCanExecuteChanged();
        RedoLastTransactionCommand.RaiseCanExecuteChanged();
    }

    private void RefreshTableCommands()
    {
        RefreshTableCommand.RaiseCanExecuteChanged();
        PreviousTablePageCommand.RaiseCanExecuteChanged();
        NextTablePageCommand.RaiseCanExecuteChanged();
        SaveTableChangesCommand.RaiseCanExecuteChanged();
        PlotTableCommand.RaiseCanExecuteChanged();
        RaisePropertyChanged(nameof(TablePageDisplay));
    }

    private static string QuoteIdentifier(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static bool TryGetRowId(DataRow row, DataRowVersion version, out long rowId)
    {
        rowId = 0;
        return row.Table.Columns.Contains("__rowid")
            && row["__rowid", version] is not DBNull
            && long.TryParse(Convert.ToString(row["__rowid", version], CultureInfo.InvariantCulture), out rowId);
    }
}
