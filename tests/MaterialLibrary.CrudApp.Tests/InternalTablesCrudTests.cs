using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using MaterialLibrary.Domain;
using MaterialLibraryCrudApp.Services;
using MaterialLibraryCrudApp.ViewModels;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MaterialLibraryCrudApp.Tests;

public sealed class InternalTablesCrudTests : IDisposable
{
    private readonly string _directory;
    private readonly string _sourceDatabase;

    public InternalTablesCrudTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "internal-tables-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_directory);
        _sourceDatabase = Path.Combine(_directory, "asme_materials.db");
        CreateDatabase(_sourceDatabase);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void InternalSqliteTablesAreVisibleAndEditableFromRawTablesWorkflow()
    {
        var dialogs = new RecordingDialogs { OpenPath = _sourceDatabase };
        var viewModel = OpenManager(dialogs);

        var sqliteSequence = viewModel.Tables.FirstOrDefault(table => string.Equals(table.Name, "sqlite_sequence", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(sqliteSequence);

        viewModel.SelectedTable = sqliteSequence;
        WaitUntil(
            () => string.Equals(viewModel.TableRows?.Table?.TableName, "sqlite_sequence", StringComparison.OrdinalIgnoreCase) && viewModel.TableRows.Count > 0,
            "sqlite_sequence table did not load");

        Assert.True(viewModel.CanEditSelectedTable, "sqlite_sequence should be editable through the raw-table workflow.");

        var row = viewModel.TableRows!
            .Cast<DataRowView>()
            .Select(item => item.Row)
            .First(dataRow => string.Equals(Convert.ToString(dataRow["name"]), "AppAudit", StringComparison.OrdinalIgnoreCase));
        row["seq"] = 7L;

        viewModel.SaveTableChangesCommand.Execute(null);
        WaitUntil(() => !viewModel.IsBusy && !viewModel.HasUnsavedTableChanges, "raw-table save did not complete");

        using var connection = new SqliteConnection($"Data Source={WorkingCopyPath(_sourceDatabase)}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT seq FROM sqlite_sequence WHERE name = 'AppAudit'";
        var seq = Convert.ToInt64(command.ExecuteScalar());
        Assert.Equal(7L, seq);
    }

    private static void CreateDatabase(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE Materials (ID INTEGER PRIMARY KEY, Specification TEXT, TypeGrade TEXT);
            CREATE TABLE AppAudit (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT);
            INSERT INTO AppAudit (Name) VALUES ('seed');
            """;
        command.ExecuteNonQuery();
    }

    private static DatabaseViewModel OpenManager(RecordingDialogs dialogs)
    {
        var viewModel = new DatabaseViewModel(dialogs, Array.Empty<Material>());
        viewModel.OpenDatabaseCommand.Execute(null);
        WaitUntil(() => viewModel.IsOpen && viewModel.Tables.Count > 0, $"database did not open: {viewModel.StatusMessage}");
        return viewModel;
    }

    private static string WorkingCopyPath(string sourcePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? Path.GetTempPath();
        return Path.Combine(directory, Path.GetFileNameWithoutExtension(sourcePath) + ".working" + Path.GetExtension(sourcePath));
    }

    private static void WaitUntil(Func<bool> condition, string failureMessage)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition() && DateTime.UtcNow < deadline) Thread.Sleep(20);
        Assert.True(condition(), failureMessage);
    }

    private sealed class RecordingDialogs : IDialogService
    {
        public string? OpenPath { get; init; }
        public string? AskOpenPath(string title, string filter) => OpenPath;
        public string? AskSavePath(string title, string filter, string? suggestedPath) => null;
        public void ShowError(string message) { }
        public void ShowInformation(string message) { }
        public bool ConfirmDelete(string materialId) => true;
        public bool ConfirmDestructiveSql(string sql) => true;
        public bool ConfirmOverwriteReference(string path) => true;
        public bool ConfirmDiscardChanges(string context) => true;
        public Material? EditMaterial(Material? existing) => null;
        public Material? EditMaterialTables(Material material) => material;
        public IReadOnlyList<Material>? ManageDatabase(IReadOnlyList<Material> currentMaterials) => null;
    }
}
