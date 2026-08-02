using System.Globalization;
using System.Text.Json;
using System.Data;
using Microsoft.Data.Sqlite;

namespace MaterialLibraryCrudApp.Services;

/// <summary>Applies the inverse of one committed journal transaction atomically.</summary>
public static class TransactionReverter
{
    /// <summary>Reapplies a previously reverted transaction (redo).</summary>
    /// <param name="databasePath">Database to write; always the working copy.</param>
    /// <param name="entry">Journal entry whose recorded "after" values are restored.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a statement affects a number of rows other than one, which means the database no
    /// longer matches the journal. The surrounding transaction is then rolled back whole.
    /// </exception>
    public static void Apply(string databasePath, TransactionJournalEntry entry)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}"); connection.Open();
        using var transaction = connection.BeginTransaction(); var keys = PrimaryKeys(connection, transaction, entry.TableName);
        foreach (var change in entry.Changes)
        {
            var before = Values(change.Before); var after = Values(change.After); using var command = connection.CreateCommand(); command.Transaction = transaction;
            if (change.State == nameof(DataRowState.Added))
            {
                var columns = after.Keys.ToList();
                if (change.RowId is not null) { columns.Insert(0, "rowid"); after["rowid"] = change.RowId.Value; }
                command.CommandText = $"INSERT INTO {Quote(entry.TableName)} ({string.Join(", ", columns.Select(Quote))}) VALUES ({string.Join(", ", columns.Select((_, i) => "$p" + i))})"; AddValues(command, columns, after);
            }
            else if (change.State == nameof(DataRowState.Deleted))
            {
                command.CommandText = $"DELETE FROM {Quote(entry.TableName)} WHERE {Target(keys, change, before, command)}";
            }
            else
            {
                var columns = after.Keys.Where(key => !keys.Contains(key, StringComparer.OrdinalIgnoreCase)).ToList();
                command.CommandText = $"UPDATE {Quote(entry.TableName)} SET {string.Join(", ", columns.Select((key, i) => $"{Quote(key)} = $p{i}"))} WHERE {Target(keys, change, before, command)}"; AddValues(command, columns, after);
            }
            if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException($"Redo affected an unexpected number of rows in '{entry.TableName}'.");
        }
        transaction.Commit();
    }

    /// <summary>Undoes a committed transaction by applying its inverse.</summary>
    /// <param name="databasePath">Database to write; always the working copy.</param>
    /// <param name="entry">Journal entry whose recorded "before" values are restored.</param>
    /// <remarks>
    /// Changes are replayed in reverse order inside one transaction, so a partially applied undo
    /// can never be committed.
    /// </remarks>
    public static void Revert(string databasePath, TransactionJournalEntry entry)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var keys = PrimaryKeys(connection, transaction, entry.TableName);
        foreach (var change in entry.Changes.Reverse())
        {
            var before = Values(change.Before);
            var after = Values(change.After);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            switch (change.State)
            {
                case nameof(DataRowState.Added):
                    if (change.RowId is null)
                    {
                        if (keys.Count == 0) throw new InvalidOperationException("Cannot undo an inserted row without a rowid or primary key.");
                        command.CommandText = $"DELETE FROM {Quote(entry.TableName)} WHERE " + string.Join(" AND ", keys.Select((key, i) => $"{Quote(key)} = $k{i}"));
                        foreach (var pair in keys.Select((key, i) => (key, i))) command.Parameters.AddWithValue("$k" + pair.i, after[pair.key] ?? DBNull.Value);
                    }
                    else
                    {
                        command.CommandText = $"DELETE FROM {Quote(entry.TableName)} WHERE rowid = $rowid";
                        command.Parameters.AddWithValue("$rowid", change.RowId.Value);
                    }
                    break;
                case nameof(DataRowState.Deleted):
                    var insert = before.Keys.ToList();
                    command.CommandText = $"INSERT INTO {Quote(entry.TableName)} ({string.Join(", ", insert.Select(Quote))}) VALUES ({string.Join(", ", insert.Select((_, i) => "$p" + i))})";
                    AddValues(command, insert, before);
                    break;
                case nameof(DataRowState.Modified):
                    if (before.Count == 0) throw new InvalidOperationException("Cannot undo a modification without original values.");
                    var where = keys.Count > 0 && change.RowId is null
                        ? string.Join(" AND ", keys.Select((key, i) => $"{Quote(key)} = $k{i}"))
                        : "rowid = $rowid";
                    var setColumns = before.Keys.Where(key => !keys.Contains(key, StringComparer.OrdinalIgnoreCase)).ToList();
                    command.CommandText = $"UPDATE {Quote(entry.TableName)} SET {string.Join(", ", setColumns.Select((key, i) => $"{Quote(key)} = $p{i}"))} WHERE {where}";
                    AddValues(command, setColumns, before);
                    if (keys.Count > 0 && change.RowId is null) foreach (var pair in keys.Select((key, i) => (key, i))) command.Parameters.AddWithValue("$k" + pair.i, before[pair.key] ?? DBNull.Value);
                    else command.Parameters.AddWithValue("$rowid", change.RowId!.Value);
                    break;
                default: throw new InvalidOperationException($"Unsupported transaction state '{change.State}'.");
            }
            if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException($"Undo affected an unexpected number of rows in '{entry.TableName}'.");
        }
        transaction.Commit();
    }

    private static List<string> PrimaryKeys(SqliteConnection connection, SqliteTransaction transaction, string table)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = $"PRAGMA table_info({Quote(table)})";
        using var reader = command.ExecuteReader(); var keys = new SortedDictionary<int, string>();
        while (reader.Read()) { var order = reader.GetInt32(5); if (order > 0) keys[order] = reader.GetString(1); }
        return keys.Values.ToList();
    }

    private static string Target(IReadOnlyList<string> keys, TransactionRowChange change, IReadOnlyDictionary<string, object?> values, SqliteCommand command)
    {
        if (change.RowId is not null) { command.Parameters.AddWithValue("$rowid", change.RowId.Value); return "rowid = $rowid"; }
        if (keys.Count == 0) throw new InvalidOperationException("No rowid or primary key is available for the transaction row.");
        foreach (var pair in keys.Select((key, i) => (key, i))) command.Parameters.AddWithValue("$k" + pair.i, values[pair.key] ?? DBNull.Value);
        return string.Join(" AND ", keys.Select((key, i) => $"{Quote(key)} = $k{i}"));
    }

    private static Dictionary<string, object?> Values(IReadOnlyDictionary<string, JsonElement> values) => values.ToDictionary(pair => pair.Key, pair => ToDbValue(pair.Value));
    private static void AddValues(SqliteCommand command, IReadOnlyList<string> columns, IReadOnlyDictionary<string, object?> values) { foreach (var pair in columns.Select((column, index) => (column, index))) command.Parameters.AddWithValue("$p" + pair.index, values[pair.column] ?? DBNull.Value); }
    private static object? ToDbValue(JsonElement value) => value.ValueKind switch { JsonValueKind.Null => null, JsonValueKind.Number when value.TryGetInt64(out var i) => i, JsonValueKind.Number => value.GetDouble(), JsonValueKind.True => true, JsonValueKind.False => false, JsonValueKind.String => value.GetString(), _ => value.GetRawText() };
    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
