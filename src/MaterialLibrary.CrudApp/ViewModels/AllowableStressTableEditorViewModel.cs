using System.Globalization;
using MaterialLibrary.Domain;
using MaterialLibraryCrudApp.Interop;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>
/// Specialised size-ranged table editor that loads from and saves to a list of
/// <see cref="AllowableStressDataset"/> entries filtered by a specific
/// <see cref="AllowableStressSource"/>. The base class already provides the
/// temperature × column grid; this class only overrides the domain
/// load/build methods.
/// </summary>
public sealed class AllowableStressTableEditorViewModel : SizeRangedTableEditorViewModel
{
    // ── Identity ─────────────────────────────────────────────────────────────

    private readonly AllowableStressSource _source;

    // ── Original DatabaseRowIds (preserved on round-trip) ───────────────────

    /// <summary>
    /// Maps column index → original DatabaseRowId so that IDs are stable when editing
    /// existing ASME-sourced datasets. New columns start at 0 (assigned later).
    /// </summary>
    private readonly List<long> _columnRowIds = [];

    // ── Construction ─────────────────────────────────────────────────────────

    /// <summary>Creates an editor for the given allowable-stress source type.</summary>
    /// <param name="source">The source this editor manages (Div1, Div1 High, or Div2).</param>
    public AllowableStressTableEditorViewModel(AllowableStressSource source)
    {
        _source = source;
    }

    // ── Overrides ────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public override string Title
    {
        get
        {
            if (_source.IsDivision1AllowableStress)     return "Allowable — Div. 1 Normal";
            if (_source.IsDivision1HighAllowableStress) return "Allowable — Div. 1 High";
            if (_source.IsDivision2AllowableStress)     return "Allowable — Div. 2";
            if (_source.IsBoltingAllowableStress)       return "Allowable — Bolting";
            return "Allowable Stress";
        }
    }

    /// <inheritdoc/>
    public override string ValueUnit => "MPa";

    // ── Domain load ──────────────────────────────────────────────────────────

    /// <summary>
    /// Populates the editor from the material's <see cref="AllowableStressDataset"/> list,
    /// keeping only entries whose <see cref="AllowableStressSource"/> matches this editor.
    /// </summary>
    /// <param name="material">Source material.</param>
    public void LoadFromMaterial(Material material)
    {
        _columnRowIds.Clear();

        var datasets = material.StrengthProperties.AllowableStressDatasets
            .ToReadOnlyList()
            .Where(d => d.Source.Equals(_source))
            .ToList();

        Temperatures.Clear();
        Columns.Clear();

        if (datasets.Count == 0)
        {
            Columns.Add(new SizeRangedColumnViewModel(0));
            return;
        }

        // Collect the union of all temperatures across all datasets, sorted.
        var allTemps = datasets
            .SelectMany(d => d.Table.Columns
                .ToReadOnlyList()
                .SelectMany(c => c.Entries.ToReadOnlyList().Select(e => e.X)))
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        foreach (var t in allTemps)
        {
            Temperatures.Add(t);
        }

        foreach (var dataset in datasets)
        {
            _columnRowIds.Add(dataset.DatabaseRowId);

            // Each dataset is a 1-D PropertyTable; use its first (only) column.
            var firstCol = dataset.Table.Columns.ToReadOnlyList().FirstOrDefault();
            if (firstCol == null)
            {
                Columns.Add(new SizeRangedColumnViewModel(allTemps.Count));
            }
            else
            {
                var colVm = new SizeRangedColumnViewModel(firstCol, allTemps);

                // Override size bounds from the dataset-level metadata fields (authoritative).
                // FSharpOption<double> is null when None, non-null when Some.
                if (dataset.SizeMinimum != null)
                {
                    colVm.SizeMin = dataset.SizeMinimum.Value.ToString("R", CultureInfo.InvariantCulture);
                }

                if (dataset.SizeMaximum != null)
                {
                    colVm.SizeMax = dataset.SizeMaximum.Value.ToString("R", CultureInfo.InvariantCulture);
                }

                Columns.Add(colVm);
            }
        }
    }

    // ── Domain build ─────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the list of <see cref="AllowableStressDataset"/> entries for this source,
    /// replacing the previous entries in the supplied list.
    /// </summary>
    /// <param name="existingDatasets">Current full list (all sources); other sources are kept unchanged.</param>
    /// <param name="updatedDatasets">Receives the updated list on success.</param>
    /// <param name="error">Receives a user-facing message on failure.</param>
    /// <returns><c>true</c> when the datasets could be built.</returns>
    public bool TryBuildDatasets(
        IEnumerable<AllowableStressDataset> existingDatasets,
        out IReadOnlyList<AllowableStressDataset>? updatedDatasets,
        out string? error)
    {
        var temps = Temperatures.ToList();

        // Datasets from other sources are kept as-is.
        var otherDatasets = existingDatasets
            .Where(d => !d.Source.Equals(_source))
            .ToList();

        if (temps.Count == 0)
        {
            // Empty editor: remove all datasets for this source.
            updatedDatasets = otherDatasets;
            error = null;
            return true;
        }

        var newDatasets = new List<AllowableStressDataset>();
        var nextFreeId = otherDatasets.Count > 0
            ? otherDatasets.Max(d => d.DatabaseRowId) + 1
            : 1L;

        for (var i = 0; i < Columns.Count; i++)
        {
            var col = Columns[i];

            if (!col.TryBuild(temps, out var domainCol, out var colError))
            {
                updatedDatasets = null;
                error = colError;
                return false;
            }

            // Skip blank columns.
            if (domainCol!.Entries.ToReadOnlyList().Count == 0)
            {
                continue;
            }

            // Build a 1-D PropertyTable from the single column (NoDimension, no size axis).
            var tableResult = PropertyTableModule.create1D(
                Title, "Temperature", "°C", Title, ValueUnit,
                XBoundaryPolicy.FlatExtrapolate,
                domainCol.Entries.ToReadOnlyList().ToFSharpList());

            if (tableResult.IsError)
            {
                updatedDatasets = null;
                error = tableResult.ErrorValue?.ToString();
                return false;
            }

            // Assign a stable row ID where available; otherwise generate a new one.
            long rowId;
            if (i < _columnRowIds.Count && _columnRowIds[i] > 0)
            {
                rowId = _columnRowIds[i];
            }
            else
            {
                rowId = nextFreeId++;
            }

            // Parse size bounds from the column header.
            FSharpOption<double> sizeMin = FSharpOption<double>.None;
            FSharpOption<double> sizeMax = FSharpOption<double>.None;

            if (!string.IsNullOrWhiteSpace(col.SizeMin) &&
                double.TryParse(col.SizeMin, NumberStyles.Float, CultureInfo.InvariantCulture, out var lo))
            {
                sizeMin = FSharpOption<double>.Some(lo);
            }

            if (!string.IsNullOrWhiteSpace(col.SizeMax) &&
                double.TryParse(col.SizeMax, NumberStyles.Float, CultureInfo.InvariantCulture, out var hi))
            {
                sizeMax = FSharpOption<double>.Some(hi);
            }

            var dataset = new AllowableStressDataset(
                rowId,
                _source,
                AllowableStressCase.StandardStrengthAllowableStress,
                tableResult.ResultValue,
                sizeMin,
                sizeMax,
                FSharpOption<double>.None,
                FSharpOption<double>.None,
                FSharpList<AsmeNoteReference>.Empty,
                FSharpOption<string>.None);

            newDatasets.Add(dataset);
        }

        updatedDatasets = [.. otherDatasets, .. newDatasets];
        error = null;
        return true;
    }
}
