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

    /// <summary>Every material read from the database, before the search filter is applied.</summary>
    /// <remarks>
    /// Kept separately so typing in the search box re-filters in memory instead of re-querying a
    /// 2129-row table on every keystroke.
    /// </remarks>
    private readonly List<DatabaseRowViewModel> _allMaterials = [];

    private string _materialSearch = string.Empty;

    /// <summary>
    /// Text matched against a material's ID, key, specification, grade, class/condition/tempering,
    /// UNS, and composed name.
    /// </summary>
    /// <remarks>
    /// Every term must appear somewhere in the row, so "sa-516 70" narrows to that grade rather than
    /// returning everything matching either word. Matching is case-insensitive and substring-based.
    /// </remarks>
    public string MaterialSearch
    {
        get => _materialSearch;
        set
        {
            if (SetProperty(ref _materialSearch, value ?? string.Empty))
            {
                ApplyMaterialSearch();
            }
        }
    }

    /// <summary>Number of materials shown out of the number loaded.</summary>
    public string MaterialCountDisplay =>
        _allMaterials.Count == Materials.Count
            ? $"{Materials.Count} material(s)"
            : $"{Materials.Count} of {_allMaterials.Count} material(s)";

    private async Task RefreshMaterialsAsync()
    {
        Materials.Clear();
        _allMaterials.Clear();

        if (_workingPath is null)
        {
            RaisePropertyChanged(nameof(MaterialCountDisplay));
            return;
        }

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
                _allMaterials.Add(new DatabaseRowViewModel(summary));
            }

            ApplyMaterialSearch();
            SelectedMaterial = null;
        }
        catch (Exception ex)
        {
            ShowRawError(ex);
        }
    }

    private long? _materialIdFilter;
    private int _selectedTabIndex;

    /// <summary>
    /// Material the raw-table tab is restricted to, or <c>null</c> when it shows every row.
    /// </summary>
    /// <remarks>
    /// An exact <c>MaterialID</c> comparison rather than a text match, so following material 77
    /// cannot also pull in rows whose stress value happens to contain "77".
    /// </remarks>
    public long? MaterialIdFilter
    {
        get => _materialIdFilter;
        private set
        {
            if (SetProperty(ref _materialIdFilter, value))
            {
                RaisePropertyChanged(nameof(MaterialFilterDisplay));
                RaisePropertyChanged(nameof(HasMaterialFilter));
                ClearMaterialFilterCommand.RaiseCanExecuteChanged();
                TableOffset = 0;
                RunDetached(RefreshSelectedTableAsync, "Reloading table");
            }
        }
    }

    /// <summary>Whether the raw-table tab is currently following a material.</summary>
    public bool HasMaterialFilter => _materialIdFilter is not null;

    /// <summary>Banner text naming the material the raw tables are restricted to.</summary>
    public string MaterialFilterDisplay =>
        _materialIdFilter is null
            ? "Showing all rows."
            : $"Showing only rows linked to material {_materialIdFilter}"
              + (_allMaterials.FirstOrDefault(m => m.DatabaseId == _materialIdFilter) is { } row
                  ? $" - {row.Name}"
                  : string.Empty)
              + ". Tables without a MaterialID column are unaffected.";

    /// <summary>Tab shown in the manager; index 1 is the raw-table workspace.</summary>
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    /// <summary>Restricts the raw tables to the selected material and shows that tab.</summary>
    private void ShowRawRowsForSelected()
    {
        if (SelectedMaterial is null)
        {
            return;
        }

        MaterialIdFilter = SelectedMaterial.DatabaseId;

        // Index 1 is the Raw Tables tab; switching keeps the two views in step so the user does
        // not have to carry the identifier across by hand.
        SelectedTabIndex = 1;
        StatusMessage = $"Raw tables now follow material {SelectedMaterial.DatabaseId}.";
    }

    /// <summary>Stops restricting the raw tables to one material.</summary>
    private void ClearMaterialFilter()
    {
        MaterialIdFilter = null;
        StatusMessage = "Raw tables show all rows again.";
    }

    /// <summary>Rebuilds the visible material list from the current search text.</summary>
    private void ApplyMaterialSearch()
    {
        var terms = MaterialSearch.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Materials.Clear();

        foreach (var row in _allMaterials)
        {
            if (terms.Length == 0 || terms.All(row.Matches))
            {
                Materials.Add(row);
            }
        }

        RaisePropertyChanged(nameof(MaterialCountDisplay));
    }

    /// <summary>Starts a material-list refresh from a synchronous caller.</summary>
    private void RefreshMaterials() => RunDetached(RefreshMaterialsAsync, "Refreshing materials");

    /// <summary>Reads the selected material out of the database and queues it for the library.</summary>
    /// <remarks>
    /// Two sources are possible. A material this application saved has a stored document and comes
    /// back losslessly. One of the shipped ASME rows has no document and is assembled from the
    /// reference tables instead, which yields only what that schema holds - identity, tensile rows,
    /// and allowable-stress datasets. The status message names which happened, because the
    /// difference matters before editing.
    /// </remarks>
    private void ImportSelected()
    {
        if (_workingPath is null || SelectedMaterial is null)
        {
            return;
        }

        var selected = SelectedMaterial;
        var fromDocument = selected.HasDocument;

        if (!MaterialDatabaseCrud.readMaterial(_workingPath, selected.MaterialKey)
                .TryUnwrap(out var material, out var error))
        {
            ShowError(error);
            return;
        }

        // Re-importing the same material replaces the queued copy rather than duplicating it.
        _imported.RemoveAll(m => string.Equals(m.Id, material.Id, StringComparison.Ordinal));
        _imported.Add(material);

        // Sy and Su are 2D tables keyed by size range, so the useful count is how many size
        // columns were published, not a flat row count.
        var syColumns = material.StrengthProperties.SyTable.AsNullableRef()?.Columns.Length ?? 0;

        var source = fromDocument
            ? "stored document"
            : $"ASME reference tables ({syColumns} Sy size group(s), "
              + $"{material.StrengthProperties.AllowableStressDatasets.Length} allowable-stress set(s))";

        AppLog.Current.Information($"Imported '{material.Id}' from the {source}.");
        StatusMessage = $"Imported {material.Id} from the {source}; {_imported.Count} queued for the library.";
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
        using var connection = new SqliteConnection(BuildConnectionString(path));
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
