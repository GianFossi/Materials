using System;
using System.Collections.Generic;
using System.IO;
using MaterialLibrary.Domain;
using MaterialLibraryCrudApp.Services;
using MaterialLibraryCrudApp.ViewModels;
using Xunit;

namespace MaterialLibrary.CrudApp.Tests;

/// <summary>
/// Verifies that saving the working copy cannot silently overwrite the original database.
/// </summary>
/// <remarks>
/// The Save As dialog used to be pre-filled with the source path, so confirming it without editing
/// the filename destroyed the reference data the working-copy design exists to protect. These tests
/// pin both halves of the fix: the suggested name is distinct, and choosing the source anyway is
/// gated behind an explicit confirmation.
/// </remarks>
public sealed class SaveWorkingCopyAsTests : IDisposable
{
    private readonly string _directory;
    private readonly string _sourceDatabase;

    /// <summary>Creates an isolated directory holding a stand-in source database.</summary>
    public SaveWorkingCopyAsTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "saveas-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_directory);

        _sourceDatabase = Path.Combine(_directory, "asme_materials.db");
        CreateDatabase(_sourceDatabase);
    }

    /// <summary>Removes the temporary directory.</summary>
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A locked temp directory must not fail the test run.
        }
    }

    /// <summary>Creates a minimal database carrying the Materials table the manager requires.</summary>
    /// <param name="path">File to create.</param>
    private static void CreateDatabase(string path)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE Materials (ID INTEGER PRIMARY KEY, Specification TEXT, TypeGrade TEXT)";
        command.ExecuteNonQuery();
    }

    /// <summary>Reads a database file's bytes, releasing any pooled handle on it first.</summary>
    /// <param name="path">File to read.</param>
    /// <returns>The file contents.</returns>
    /// <remarks>
    /// SQLite pools connections, so a file opened earlier in the test stays locked until the pool is
    /// cleared. Without this the assertion fails on the file handle rather than on the behaviour.
    /// </remarks>
    private static byte[] Snapshot(string path)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        return File.ReadAllBytes(path);
    }

    /// <summary>Opens the manager on the source database and waits for it to settle.</summary>
    /// <param name="dialogs">Dialog stub driving the flow.</param>
    /// <returns>An opened view model.</returns>
    private static DatabaseViewModel OpenManager(RecordingDialogs dialogs)
    {
        var viewModel = new DatabaseViewModel(dialogs, Array.Empty<Material>());
        viewModel.OpenDatabaseCommand.Execute(null);

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!viewModel.IsOpen && DateTime.UtcNow < deadline)
        {
            System.Threading.Thread.Sleep(20);
        }

        Assert.True(viewModel.IsOpen, $"database did not open: {viewModel.StatusMessage}");
        return viewModel;
    }

    [Fact]
    public void SuggestedNameIsNeverTheOriginalDatabase()
    {
        var dialogs = new RecordingDialogs { OpenPath = _sourceDatabase, SavePath = null };
        var viewModel = OpenManager(dialogs);

        viewModel.SaveWorkingCopyAsCommand.Execute(null);

        Assert.NotNull(dialogs.LastSuggestedSavePath);
        Assert.NotEqual(
            Path.GetFullPath(_sourceDatabase),
            Path.GetFullPath(dialogs.LastSuggestedSavePath!),
            StringComparer.OrdinalIgnoreCase);
        Assert.Contains(".edited", dialogs.LastSuggestedSavePath!, StringComparison.Ordinal);
    }

    [Fact]
    public void ChoosingTheOriginalRequiresConfirmationAndIsRefusedByDefault()
    {
        var dialogs = new RecordingDialogs
        {
            OpenPath = _sourceDatabase,
            SavePath = _sourceDatabase,
            AllowOverwriteReference = false,
        };

        var viewModel = OpenManager(dialogs);
        var originalBytes = Snapshot(_sourceDatabase);

        viewModel.SaveWorkingCopyAsCommand.Execute(null);

        Assert.True(dialogs.OverwriteReferenceAsked, "the overwrite guard was never shown");
        Assert.Contains("cancelled", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(originalBytes, Snapshot(_sourceDatabase));
    }

    [Fact]
    public void ChoosingTheOriginalProceedsOnlyWhenConfirmed()
    {
        var dialogs = new RecordingDialogs
        {
            OpenPath = _sourceDatabase,
            SavePath = _sourceDatabase,
            AllowOverwriteReference = true,
        };

        var viewModel = OpenManager(dialogs);

        viewModel.SaveWorkingCopyAsCommand.Execute(null);

        Assert.True(dialogs.OverwriteReferenceAsked);
        Assert.Contains("Saved database to", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void SavingElsewhereNeedsNoConfirmationAndLeavesTheOriginalIntact()
    {
        var target = Path.Combine(_directory, "exported.db");
        var dialogs = new RecordingDialogs { OpenPath = _sourceDatabase, SavePath = target };

        var viewModel = OpenManager(dialogs);
        var originalBytes = Snapshot(_sourceDatabase);

        viewModel.SaveWorkingCopyAsCommand.Execute(null);

        Assert.False(dialogs.OverwriteReferenceAsked, "an unrelated path must not trigger the guard");
        Assert.True(File.Exists(target));
        Assert.Equal(originalBytes, Snapshot(_sourceDatabase));
    }

    /// <summary>Dialog stub that records what it was asked and answers from fixed settings.</summary>
    private sealed class RecordingDialogs : IDialogService
    {
        /// <summary>Path returned by the open-file dialog.</summary>
        public string? OpenPath { get; init; }

        /// <summary>Path returned by the save-file dialog; <c>null</c> simulates cancelling.</summary>
        public string? SavePath { get; init; }

        /// <summary>Answer given to the overwrite-reference confirmation.</summary>
        public bool AllowOverwriteReference { get; init; }

        /// <summary>Suggested path the save dialog was offered.</summary>
        public string? LastSuggestedSavePath { get; private set; }

        /// <summary>Whether the overwrite-reference confirmation was shown.</summary>
        public bool OverwriteReferenceAsked { get; private set; }

        /// <inheritdoc />
        public string? AskOpenPath(string title, string filter) => OpenPath;

        /// <inheritdoc />
        public string? AskSavePath(string title, string filter, string? suggestedPath)
        {
            LastSuggestedSavePath = suggestedPath;
            return SavePath;
        }

        /// <inheritdoc />
        public void ShowError(string message) { }

        /// <inheritdoc />
        public void ShowInformation(string message) { }

        /// <inheritdoc />
        public bool ConfirmDelete(string materialId) => true;

        /// <inheritdoc />
        public bool ConfirmDestructiveSql(string sql) => true;

        /// <inheritdoc />
        public bool ConfirmOverwriteReference(string path)
        {
            OverwriteReferenceAsked = true;
            return AllowOverwriteReference;
        }

        /// <inheritdoc />
        public bool ConfirmDiscardChanges(string context) => true;

        /// <inheritdoc />
        public Material? EditMaterial(Material? existing) => null;

        /// <inheritdoc />
        public Material? EditMaterialTables(Material material) => material;

        /// <inheritdoc />
        public IReadOnlyList<Material>? ManageDatabase(IReadOnlyList<Material> currentMaterials) => null;
    }
}
