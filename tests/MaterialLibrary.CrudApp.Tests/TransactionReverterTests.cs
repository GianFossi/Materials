using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;
using MaterialLibraryCrudApp.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MaterialLibraryCrudApp.Tests;

public sealed class TransactionReverterTests
{
    [Fact]
    public void SuppliedWorkingDatabaseFixtureIsReadableAndIntact()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "asme_materials.working.db");
        Assert.True(File.Exists(path), $"Missing test fixture: {path}");
        using var connection = new SqliteConnection($"Data Source={path};Foreign Keys=True");
        connection.Open();
        using var integrity = connection.CreateCommand();
        integrity.CommandText = "PRAGMA integrity_check";
        Assert.Equal("ok", integrity.ExecuteScalar()?.ToString());
        using var schema = connection.CreateCommand();
        schema.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Materials'";
        Assert.True(Convert.ToInt64(schema.ExecuteScalar()) > 0);
    }

    [Fact]
    public void RealFixtureIsNeverMutatedByTestDatabaseWork()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "asme_materials.working.db");
        var originalLength = new FileInfo(fixture).Length;
        var copy = Path.Combine(Path.GetTempPath(), $"asme-working-{Guid.NewGuid():N}.db");
        File.Copy(fixture, copy);
        using (var connection = new SqliteConnection($"Data Source={copy};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE TestIsolation (Id INTEGER PRIMARY KEY, Value TEXT); INSERT INTO TestIsolation VALUES (1, 'temporary');";
            command.ExecuteNonQuery();
        }
        Assert.Equal(originalLength, new FileInfo(fixture).Length);
        Assert.Equal("ok", Integrity(fixture));
        File.Delete(copy);
    }

    [Theory]
    [InlineData("CREATE TABLE Items (Id INTEGER)", "DROP TABLE Items")]
    [InlineData("CREATE UNIQUE INDEX IX_Items ON Items(Id)", "DROP INDEX IX_Items")]
    [InlineData("ALTER TABLE OldName RENAME TO NewName", "ALTER TABLE NewName RENAME TO OldName")]
    public void SchemaInverseRecognizesSafeOperations(string sql, string expected)
    {
        Assert.True(SchemaUndo.TryGetInverse(sql, out var inverse));
        Assert.Equal(expected, inverse);
    }

    [Fact]
    public void SchemaInverseRejectsArbitrarySql()
    {
        Assert.False(SchemaUndo.TryGetInverse("DELETE FROM Items", out _));
    }

    [Fact]
    public void JournalPersistsUndoAndRedoState()
    {
        var journalPath = Path.Combine(Path.GetTempPath(), $"journal-{Guid.NewGuid():N}.json");
        var journal = new TransactionJournal(journalPath);
        var entry = Entry("test.db", "Items", new TransactionRowChange("Added", new Dictionary<string, JsonElement>(), Values(("Id", 1L)), 1));
        journal.Append(entry);
        Assert.Single(journal.Load());
        journal.PushRedo(entry);
        Assert.Single(journal.LoadRedo());
        journal.RemoveRedo(entry);
        Assert.Empty(journal.LoadRedo());
        journal.Remove(entry);
        Assert.Empty(journal.Load());
    }

    [Fact]
    public void RevertsAddedRowByRowId()
    {
        var path = NewDatabase("CREATE TABLE Items (Id INTEGER PRIMARY KEY, Name TEXT); INSERT INTO Items VALUES (1, 'before'); INSERT INTO Items VALUES (2, 'added');");
        var entry = Entry(path, "Items", new TransactionRowChange("Added", new Dictionary<string, JsonElement>(), Values(("Name", "added")), 2));
        TransactionReverter.Revert(path, entry);
        Assert.Equal(1, Count(path, "Items"));
    }

    [Fact]
    public void RevertsCompositeWithoutRowidInsertByKey()
    {
        var path = NewDatabase("CREATE TABLE Items (A TEXT NOT NULL, B INTEGER NOT NULL, Value TEXT, PRIMARY KEY (A, B)) WITHOUT ROWID; INSERT INTO Items VALUES ('x', 1, 'before'); INSERT INTO Items VALUES ('x', 2, 'added');");
        var entry = Entry(path, "Items", new TransactionRowChange("Added", new Dictionary<string, JsonElement>(), Values(("A", "x"), ("B", 2L), ("Value", "added")), null));
        TransactionReverter.Revert(path, entry);
        Assert.Equal(1, Count(path, "Items"));
    }

    [Fact]
    public void RevertsModifiedRow()
    {
        var path = NewDatabase("CREATE TABLE Items (Id INTEGER PRIMARY KEY, Name TEXT); INSERT INTO Items VALUES (1, 'after');");
        var entry = Entry(path, "Items", new TransactionRowChange("Modified", Values(("Id", 1L), ("Name", "before")), Values(("Id", 1L), ("Name", "after")), 1));
        TransactionReverter.Revert(path, entry);
        Assert.Equal("before", Scalar(path, "SELECT Name FROM Items WHERE Id = 1"));
    }

    [Fact]
    public void RevertsDeletedRow()
    {
        var path = NewDatabase("CREATE TABLE Items (Id INTEGER PRIMARY KEY, Name TEXT); INSERT INTO Items VALUES (1, 'deleted'); DELETE FROM Items WHERE Id = 1;");
        var entry = Entry(path, "Items", new TransactionRowChange("Deleted", Values(("Id", 1L), ("Name", "deleted")), new Dictionary<string, JsonElement>(), 1));
        TransactionReverter.Revert(path, entry);
        Assert.Equal("deleted", Scalar(path, "SELECT Name FROM Items WHERE Id = 1"));
    }

    [Fact]
    public void ForeignKeyFailureRollsBackWholeUndo()
    {
        var path = NewDatabase("PRAGMA foreign_keys=ON; CREATE TABLE Parent (Id INTEGER PRIMARY KEY); CREATE TABLE Child (Id INTEGER PRIMARY KEY, ParentId INTEGER REFERENCES Parent(Id)); INSERT INTO Parent VALUES (1); INSERT INTO Child VALUES (1, 1);");
        var entry = Entry(path, "Parent", new TransactionRowChange("Deleted", Values(("Id", 1L)), new Dictionary<string, JsonElement>(), 1));
        Assert.Throws<SqliteException>(() => TransactionReverter.Revert(path, entry));
        Assert.Equal(1, Count(path, "Parent"));
    }

    private static TransactionJournalEntry Entry(string path, string table, params TransactionRowChange[] changes) => new(DateTimeOffset.UtcNow, path, table, changes);
    private static Dictionary<string, JsonElement> Values(params (string Key, object Value)[] values) => values.ToDictionary(item => item.Key, item => JsonSerializer.SerializeToElement(item.Value));
    private static string NewDatabase(string sql) { var path = Path.Combine(Path.GetTempPath(), $"crud-{Guid.NewGuid():N}.db"); using var connection = new SqliteConnection($"Data Source={path}"); connection.Open(); using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery(); return path; }
    private static long Count(string path, string table) { using var connection = new SqliteConnection($"Data Source={path}"); connection.Open(); using var command = connection.CreateCommand(); command.CommandText = $"SELECT COUNT(*) FROM \"{table}\""; return (long)command.ExecuteScalar()!; }
    private static object? Scalar(string path, string sql) { using var connection = new SqliteConnection($"Data Source={path}"); connection.Open(); using var command = connection.CreateCommand(); command.CommandText = sql; return command.ExecuteScalar(); }
    private static string Integrity(string path) { using var connection = new SqliteConnection($"Data Source={path}"); connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "PRAGMA integrity_check"; return command.ExecuteScalar()?.ToString() ?? string.Empty; }
}
