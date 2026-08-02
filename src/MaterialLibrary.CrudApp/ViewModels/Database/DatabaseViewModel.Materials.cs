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

/// <summary>Listing materials and moving them between the database and the library.</summary>
public sealed partial class DatabaseViewModel
{

    private async Task RefreshMaterialsAsync()
    {
        Materials.Clear();
        if (_workingPath is null) return;

        try
        {
            var path = _workingPath;
            var result = await Task.Run(() => MaterialDatabaseCrud.listMaterials(path));
            if (!result.TryUnwrap(out var summaries, out var error))
            {
                ShowError(error);
                return;
            }

            foreach (var summary in summaries.ToReadOnlyList())
            {
                Materials.Add(new DatabaseRowViewModel(summary));
            }

            SelectedMaterial = null;
        }
        catch (Exception ex)
        {
            ShowRawError(ex);
        }
    }

    /// <summary>Starts a material-list refresh from a synchronous caller.</summary>
    private void RefreshMaterials() => RunDetached(RefreshMaterialsAsync, "Refreshing materials");

    private void ImportSelected()
    {
        if (_workingPath is null || SelectedMaterial is null) return;

        if (!MaterialDatabaseCrud.readMaterial(_workingPath, SelectedMaterial.MaterialKey)
                .TryUnwrap(out var material, out var error))
        {
            ShowError(error);
            return;
        }

        _imported.RemoveAll(m => string.Equals(m.Id, material.Id, StringComparison.Ordinal));
        _imported.Add(material);
        StatusMessage = $"Imported {material.Id}; {_imported.Count} material(s) queued for the library.";
    }

    private void ExportLibrary()
    {
        if (_workingPath is null) return;

        if (!ForeignKeysAreValid(_workingPath, out var foreignKeyError))
        {
            ShowRawError(new InvalidOperationException(foreignKeyError));
            return;
        }

        if (!MaterialDatabaseCrud.upsertMaterials(_workingPath, _libraryMaterials.ToFSharpList())
                .TryUnwrap(out var changes, out var error))
        {
            ShowError(error);
            return;
        }

        var exportMessage = $"Wrote {changes.ToReadOnlyList().Count} material(s) to the working copy.";
        RunDetached(
            async () =>
            {
                await RefreshAllAsync();
                StatusMessage = exportMessage;
            },
            "Refreshing after export");
    }

    private static bool ForeignKeysAreValid(string path, out string error)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check";
        using var reader = command.ExecuteReader();
        if (!reader.Read()) { error = string.Empty; return true; }
        error = $"Foreign-key validation failed in table '{reader.GetString(0)}', row {reader.GetValue(1)}.";
        return false;
    }

    private void DeleteSelected()
    {
        if (_workingPath is null || SelectedMaterial is null) return;
        var key = SelectedMaterial.MaterialKey;
        if (!_dialogService.ConfirmDelete(key)) return;

        if (!MaterialDatabaseCrud.deleteMaterial(_workingPath, key).TryUnwrap(out var change, out var error))
        {
            ShowError(error);
            return;
        }

        var deleteMessage = change.Message;
        RunDetached(
            async () =>
            {
                await RefreshAllAsync();
                StatusMessage = deleteMessage;
            },
            "Refreshing after delete");
    }
}
