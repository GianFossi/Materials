using System.Collections.ObjectModel;
using System.Globalization;
using MaterialLibrary.Domain;
using MaterialLibraryCrudApp.Interop;
using Microsoft.FSharp.Core;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>
/// One size-range column inside a <see cref="SizeRangedTableEditorViewModel"/>.
/// The column header carries two editable size-range bound fields; its cells are the
/// property values at each temperature (one cell per row in the shared temperature axis).
/// </summary>
public sealed class SizeRangedColumnViewModel : ObservableObject
{
    private string _sizeMin = string.Empty;
    private string _sizeMax = string.Empty;

    /// <summary>Initialises an empty column with the given number of temperature rows.</summary>
    /// <param name="rowCount">Number of temperature rows to allocate.</param>
    public SizeRangedColumnViewModel(int rowCount)
    {
        Values = new ObservableCollection<string>(Enumerable.Repeat(string.Empty, rowCount));
    }

    /// <summary>
    /// Reconstructs a column from an existing <see cref="TableColumn"/>, aligning values to the
    /// supplied shared temperature list.
    /// </summary>
    /// <param name="column">Domain column carrying size range and (temperature, value) entries.</param>
    /// <param name="temperatures">Shared ordered temperature list for the parent editor.</param>
    public SizeRangedColumnViewModel(TableColumn column, IReadOnlyList<double> temperatures)
    {
        // FSharpOption<T>.None is represented as null in C#; Some(x) has .Value == x.
        var lower = column.SizeRange.Lower;
        var upper = column.SizeRange.Upper;

        _sizeMin = lower != null
            ? lower.Value.Value.ToString("R", CultureInfo.InvariantCulture)
            : string.Empty;

        _sizeMax = upper != null
            ? upper.Value.Value.ToString("R", CultureInfo.InvariantCulture)
            : string.Empty;

        var entryMap = column.Entries
            .ToReadOnlyList()
            .ToDictionary(e => e.X, e => e.Value.ToString("R", CultureInfo.InvariantCulture));

        Values = new ObservableCollection<string>(
            temperatures.Select(t => entryMap.TryGetValue(t, out var v) ? v : string.Empty));
    }

    /// <summary>Lower size bound (mm), blank for unbounded (−∞).</summary>
    public string SizeMin
    {
        get => _sizeMin;
        set => SetProperty(ref _sizeMin, value ?? string.Empty);
    }

    /// <summary>Upper size bound (mm), blank for unbounded (+∞).</summary>
    public string SizeMax
    {
        get => _sizeMax;
        set => SetProperty(ref _sizeMax, value ?? string.Empty);
    }

    /// <summary>Cell values; one entry per temperature row, parallel to the parent's temperature list.</summary>
    public ObservableCollection<string> Values { get; }

    /// <summary>Label shown in the column header for display purposes.</summary>
    public string DisplayLabel
    {
        get
        {
            var hasMin = !string.IsNullOrWhiteSpace(SizeMin);
            var hasMax = !string.IsNullOrWhiteSpace(SizeMax);
            return (hasMin, hasMax) switch
            {
                (false, false) => "all",
                (false, true)  => $"≤ {SizeMax} mm",
                (true, false)  => $"> {SizeMin} mm",
                (true, true)   => $"{SizeMin}–{SizeMax} mm",
            };
        }
    }

    /// <summary>
    /// Adds a blank cell at the end (appended when a new temperature row is added to the parent).
    /// </summary>
    public void AddRow() => Values.Add(string.Empty);

    /// <summary>Removes the cell at the given index.</summary>
    /// <param name="index">Zero-based row index to remove.</param>
    public void RemoveRow(int index)
    {
        if (index >= 0 && index < Values.Count)
        {
            Values.RemoveAt(index);
        }
    }

    /// <summary>
    /// Tries to build a domain <see cref="TableColumn"/> from the current cell values.
    /// Blank cells are skipped (sparse columns are allowed).
    /// </summary>
    /// <param name="temperatures">Shared temperature list; must be parallel to <see cref="Values"/>.</param>
    /// <param name="column">Receives the domain column on success.</param>
    /// <param name="error">Receives a user-facing message on failure.</param>
    /// <returns><c>true</c> when the column could be built.</returns>
    public bool TryBuild(
        IReadOnlyList<double> temperatures,
        out TableColumn? column,
        out string? error)
    {
        // Parse size-range bounds.
        SizeRangeBound? lower = null;
        SizeRangeBound? upper = null;

        if (!string.IsNullOrWhiteSpace(SizeMin))
        {
            if (!double.TryParse(SizeMin, NumberStyles.Float, CultureInfo.InvariantCulture, out var lo))
            {
                column = null;
                error = $"Size min '{SizeMin}' is not a valid number.";
                return false;
            }

            lower = new SizeRangeBound(lo, BoundInclusion.Exclusive);
        }

        if (!string.IsNullOrWhiteSpace(SizeMax))
        {
            if (!double.TryParse(SizeMax, NumberStyles.Float, CultureInfo.InvariantCulture, out var hi))
            {
                column = null;
                error = $"Size max '{SizeMax}' is not a valid number.";
                return false;
            }

            upper = new SizeRangeBound(hi, BoundInclusion.Inclusive);
        }

        var sizeRange = new SizeColumnRange(
            lower != null ? FSharpOption<SizeRangeBound>.Some(lower) : FSharpOption<SizeRangeBound>.None,
            upper != null ? FSharpOption<SizeRangeBound>.Some(upper) : FSharpOption<SizeRangeBound>.None,
            FSharpOption<string>.None);

        // Parse cell values.
        var entries = new List<TableColumnEntry>();

        for (var i = 0; i < Math.Min(temperatures.Count, Values.Count); i++)
        {
            var cell = Values[i];
            if (string.IsNullOrWhiteSpace(cell))
            {
                continue; // Sparse — blank cell means no data at this temperature for this column.
            }

            if (!double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
            {
                column = null;
                error = $"Cell at row {i + 1} ({temperatures[i]} °C): '{cell}' is not a valid number.";
                return false;
            }

            entries.Add(new TableColumnEntry(temperatures[i], val));
        }

        column = new TableColumn(sizeRange, entries.ToFSharpList());
        error = null;
        return true;
    }
}
