using System.Collections.ObjectModel;
using System.Globalization;
using MaterialLibrary.Domain;
using MaterialLibraryCrudApp.Interop;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>
/// Base ViewModel for an independent temperature × size-range 2-D property table editor.
/// Rows are temperatures (user-editable); columns are size-range bands whose bounds are shown
/// in the column header.  Each of Sy, Su, Allowable Div1, Div1-High, and Div2 owns one instance.
/// </summary>
public abstract class SizeRangedTableEditorViewModel : ObservableObject
{
    // ── State ────────────────────────────────────────────────────────────────

    private string _newTemperature = string.Empty;

    // ── Construction ────────────────────────────────────────────────────────

    /// <summary>Creates an empty editor with no rows and one default column.</summary>
    protected SizeRangedTableEditorViewModel()
    {
        Temperatures = [];
        Columns = [];
        Columns.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(HasColumns));

        // Default empty column.
        Columns.Add(new SizeRangedColumnViewModel(0));

        AddRowCommand       = new RelayCommand(ExecuteAddRow, () => CanAddRow);
        DeleteRowCommand    = new RelayCommand<int>(ExecuteDeleteRow);
        AddColumnCommand    = new RelayCommand(ExecuteAddColumn);
        DeleteColumnCommand = new RelayCommand<int>(ExecuteDeleteColumn, _ => Columns.Count > 1);
    }

    // ── Properties ──────────────────────────────────────────────────────────

    /// <summary>User-facing table title shown on the tab, e.g. "Sy — Yield Strength".</summary>
    public abstract string Title { get; }

    /// <summary>Unit label shown in the table body, e.g. "MPa".</summary>
    public abstract string ValueUnit { get; }

    /// <summary>Editable temperature rows (°C).</summary>
    public ObservableCollection<double> Temperatures { get; }

    /// <summary>Size-range columns.  Each column has its own editable size bounds and per-row values.</summary>
    public ObservableCollection<SizeRangedColumnViewModel> Columns { get; }

    /// <summary><c>true</c> when there is at least one column (always, given the default column).</summary>
    public bool HasColumns => Columns.Count > 0;

    /// <summary>Candidate temperature text for <see cref="AddRowCommand"/>.</summary>
    public string NewTemperature
    {
        get => _newTemperature;
        set
        {
            if (SetProperty(ref _newTemperature, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(CanAddRow));
                AddRowCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary><c>true</c> when <see cref="NewTemperature"/> parses to a number.</summary>
    public bool CanAddRow =>
        double.TryParse(NewTemperature, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    // ── Commands ────────────────────────────────────────────────────────────

    /// <summary>Appends the temperature in <see cref="NewTemperature"/> as a new row.</summary>
    public RelayCommand AddRowCommand { get; }

    /// <summary>Removes the row at the given index.</summary>
    public RelayCommand<int> DeleteRowCommand { get; }

    /// <summary>Adds a new blank size-range column.</summary>
    public RelayCommand AddColumnCommand { get; }

    /// <summary>Removes the column at the given index. Disabled when only one column remains.</summary>
    public RelayCommand<int> DeleteColumnCommand { get; }

    // ── Command logic ────────────────────────────────────────────────────────

    private void ExecuteAddRow()
    {
        if (!double.TryParse(NewTemperature, NumberStyles.Float, CultureInfo.InvariantCulture, out var temp))
        {
            return;
        }

        // Insert in sorted order.
        var tempList = Temperatures.ToList();
        var idx = tempList.BinarySearch(temp);
        if (idx < 0)
        {
            idx = ~idx;
        }

        Temperatures.Insert(idx, temp);

        foreach (var col in Columns)
        {
            col.Values.Insert(idx, string.Empty);
        }

        NewTemperature = string.Empty;
    }

    private void ExecuteDeleteRow(int index)
    {
        if (index < 0 || index >= Temperatures.Count)
        {
            return;
        }

        Temperatures.RemoveAt(index);

        foreach (var col in Columns)
        {
            col.RemoveRow(index);
        }
    }

    private void ExecuteAddColumn()
    {
        Columns.Add(new SizeRangedColumnViewModel(Temperatures.Count));
        DeleteColumnCommand.NotifyCanExecuteChanged();
    }

    private void ExecuteDeleteColumn(int index)
    {
        if (index < 0 || index >= Columns.Count || Columns.Count <= 1)
        {
            return;
        }

        Columns.RemoveAt(index);
        DeleteColumnCommand.NotifyCanExecuteChanged();
    }

    // ── Load from domain ─────────────────────────────────────────────────────

    /// <summary>
    /// Populates the editor from a domain <see cref="PropertyTable"/>.
    /// Clears all current rows and columns first.
    /// </summary>
    /// <param name="table">Domain table to load. Pass <c>null</c> to clear the editor.</param>
    public void LoadFromTable(PropertyTable? table)
    {
        Temperatures.Clear();
        Columns.Clear();

        if (table == null)
        {
            Columns.Add(new SizeRangedColumnViewModel(0));
            DeleteColumnCommand.NotifyCanExecuteChanged();
            return;
        }

        // Collect the union of all temperatures across columns, sorted.
        var allTemps = table.Columns
            .ToReadOnlyList()
            .SelectMany(c => c.Entries.ToReadOnlyList().Select(e => e.X))
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        foreach (var t in allTemps)
        {
            Temperatures.Add(t);
        }

        foreach (var col in table.Columns.ToReadOnlyList())
        {
            Columns.Add(new SizeRangedColumnViewModel(col, allTemps));
        }

        if (Columns.Count == 0)
        {
            Columns.Add(new SizeRangedColumnViewModel(allTemps.Count));
        }

        DeleteColumnCommand.NotifyCanExecuteChanged();
    }

    // ── Build domain table ───────────────────────────────────────────────────

    /// <summary>
    /// Tries to build a domain <see cref="PropertyTable"/> from the current editor state.
    /// Returns <c>null</c> when the editor is empty (no temperatures or all cells blank).
    /// </summary>
    /// <param name="table">Receives the domain table on success.</param>
    /// <param name="error">Receives a user-facing error message on failure.</param>
    /// <returns><c>true</c> when the table could be built.</returns>
    public bool TryBuildTable(out PropertyTable? table, out string? error)
    {
        var temps = Temperatures.ToList();

        if (temps.Count == 0)
        {
            table = null;
            error = null;
            return true; // Empty editor → no table stored (cleared).
        }

        var domainColumns = new List<TableColumn>();

        foreach (var col in Columns)
        {
            if (!col.TryBuild(temps, out var domainCol, out var colError))
            {
                table = null;
                error = colError;
                return false;
            }

            // Drop entirely-blank columns.
            if (domainCol!.Entries.ToReadOnlyList().Count > 0)
            {
                domainColumns.Add(domainCol);
            }
        }

        if (domainColumns.Count == 0)
        {
            table = null;
            error = null;
            return true; // Nothing to store.
        }

        // Determine whether any column has size bounds.
        var hasSizeBounds = Columns.Any(c =>
            !string.IsNullOrWhiteSpace(c.SizeMin) || !string.IsNullOrWhiteSpace(c.SizeMax));

        Microsoft.FSharp.Core.FSharpResult<PropertyTable, MaterialError> result;

        if (!hasSizeBounds || domainColumns.Count == 1)
        {
            // 1-D table: single-column with no size dimension (or all bounds blank).
            result = PropertyTableModule.create1D(
                Title, "Temperature", "°C", Title, ValueUnit,
                XBoundaryPolicy.FlatExtrapolate,
                domainColumns[0].Entries.ToReadOnlyList().ToFSharpList());
        }
        else
        {
            // 2-D table: multiple columns keyed by thickness/size range.
            result = PropertyTableModule.create2D(
                Title, "Temperature", "°C", Title, ValueUnit,
                TableDimension.Thickness, "mm",
                XBoundaryPolicy.FlatExtrapolate,
                domainColumns.ToFSharpList());
        }

        if (result.IsError)
        {
            table = null;
            error = result.ErrorValue?.ToString();
            return false;
        }

        table = result.ResultValue;
        error = null;
        return true;
    }
}
