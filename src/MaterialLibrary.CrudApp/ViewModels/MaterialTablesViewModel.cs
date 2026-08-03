using System.Collections.ObjectModel;
using MaterialLibrary.Crud;
using MaterialLibrary.Domain;
using MaterialLibraryCrudApp.Interop;
using Microsoft.FSharp.Core;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>
/// Drives the material-tables editor: a list of editable tables on the left, the rows of the
/// selected table on the right, plus dedicated size-ranged editors for Sy, Su, and allowable
/// stress tables.
/// </summary>
/// <remarks>
/// <para>
/// The view model owns a working <see cref="Material"/>. Switching tables commits the current grid
/// into that working record first, so edits are never lost by navigating away, and each commit goes
/// through the F# CRUD helpers named by the table's <see cref="MaterialTableSpec"/>.
/// </para>
/// <para>
/// The working record is only handed back on confirmation, so cancelling discards everything and
/// the caller's original material is never mutated.
/// </para>
/// </remarks>
public sealed class MaterialTablesViewModel : ObservableObject
{
    private readonly Material _original;
    private Material _working;
    private MaterialTableSpec _selectedTable;
    private TableRowViewModel? _selectedRow;
    private string _statusMessage = string.Empty;

    /// <summary>Creates the tables editor over a material.</summary>
    /// <param name="material">Material whose tables are edited; never mutated.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="material"/> is <c>null</c>.</exception>
    public MaterialTablesViewModel(Material material)
    {
        _original = material ?? throw new ArgumentNullException(nameof(material));
        _working = material;
        _selectedTable = Tables[0];

        AddRowCommand    = new RelayCommand(AddRow);
        DeleteRowCommand = new RelayCommand(DeleteRow, () => SelectedRow is not null);
        StressStrainEditor = new StressStrainTableEditorViewModel(material);
        CreepEditor = new CreepTableEditorViewModel(material);
        StressRuptureEditor = new StressRuptureTableEditorViewModel(material);
        FatigueEditor = new FatigueTableEditorViewModel(material);
        CyclicEditor = new CyclicStrainTableEditorViewModel(material);
        ExternalPressureEditor = new ExternalPressureTableEditorViewModel(material);
        LarsonMillerEditor = new LarsonMillerEditorViewModel(material);

        // Strength size-ranged editors.
        SyEditor = new SyTableEditorViewModel();
        SuEditor = new SuTableEditorViewModel();
        AllowableDiv1Editor = new AllowableStressTableEditorViewModel(
            AllowableStressSource.Division1AllowableStress);
        AllowableDiv1HighEditor = new AllowableStressTableEditorViewModel(
            AllowableStressSource.Division1HighAllowableStress);
        AllowableDiv2Editor = new AllowableStressTableEditorViewModel(
            AllowableStressSource.Division2AllowableStress);

        LoadStrengthEditors(material);
        LoadRows();
    }

    /// <summary>Tables available for editing.</summary>
    public IReadOnlyList<MaterialTableSpec> Tables => MaterialTableSpecs.All;

    /// <summary>Rows of the currently selected table.</summary>
    public ObservableCollection<TableRowViewModel> Rows { get; } = [];

    /// <summary>Master/detail editor for nested stress-strain tables.</summary>
    public StressStrainTableEditorViewModel StressStrainEditor { get; }
    /// <summary>Editor for creep tables, which carry per-table metadata plus a nested point list.</summary>
    public CreepTableEditorViewModel CreepEditor { get; }
    /// <summary>Editor for stress-rupture tables.</summary>
    public StressRuptureTableEditorViewModel StressRuptureEditor { get; }
    /// <summary>Editor for fatigue curves.</summary>
    public FatigueTableEditorViewModel FatigueEditor { get; }
    /// <summary>Editor for cyclic-strain tables.</summary>
    public CyclicStrainTableEditorViewModel CyclicEditor { get; }
    /// <summary>Editor for external-pressure tables.</summary>
    public ExternalPressureTableEditorViewModel ExternalPressureEditor { get; }
    /// <summary>Editor for Larson-Miller curves.</summary>
    public LarsonMillerEditorViewModel LarsonMillerEditor { get; }

    /// <summary>Size-ranged editor for the Sy (yield strength, MPa) table.</summary>
    public SyTableEditorViewModel SyEditor { get; }
    /// <summary>Size-ranged editor for the Su (ultimate tensile strength, MPa) table.</summary>
    public SuTableEditorViewModel SuEditor { get; }
    /// <summary>Size-ranged editor for the Allowable Stress Division 1 Normal (MPa) table.</summary>
    public AllowableStressTableEditorViewModel AllowableDiv1Editor { get; }
    /// <summary>Size-ranged editor for the Allowable Stress Division 1 High (MPa) table.</summary>
    public AllowableStressTableEditorViewModel AllowableDiv1HighEditor { get; }
    /// <summary>Size-ranged editor for the Allowable Stress Division 2 (MPa) table.</summary>
    public AllowableStressTableEditorViewModel AllowableDiv2Editor { get; }

    /// <summary>Identifier of the material being edited, shown in the window title.</summary>
    public string Title => $"Tables - {_original.Id}";

    /// <summary>Table currently being edited.</summary>
    public MaterialTableSpec SelectedTable
    {
        get => _selectedTable;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedTable))
            {
                return;
            }

            // Commit the outgoing table before switching, so edits survive navigation.
            if (!TryCommitCurrentTable(out var error))
            {
                StatusMessage = $"{_selectedTable.Title}: {error}";
                return;
            }

            SetProperty(ref _selectedTable, value);
            LoadRows();
        }
    }

    /// <summary>Row selected in the grid.</summary>
    public TableRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (SetProperty(ref _selectedRow, value))
            {
                DeleteRowCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Validation or progress message shown under the grid.</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>Appends an empty row to the current table.</summary>
    public RelayCommand AddRowCommand { get; }

    /// <summary>Removes the selected row from the current table.</summary>
    public RelayCommand DeleteRowCommand { get; }

    /// <summary>
    /// Commits the visible grid and all size-ranged strength editors, then returns the updated
    /// material.
    /// </summary>
    /// <param name="material">Receives the updated material on success; <c>null</c> on failure.</param>
    /// <param name="error">Receives a user-facing validation message on failure; <c>null</c> on success.</param>
    /// <returns><c>true</c> when every editor was valid and the result was produced.</returns>
    public bool TryBuildMaterial(out Material? material, out string? error)
    {
        if (!TryCommitCurrentTable(out error))
        {
            material = null;
            error = $"{_selectedTable.Title}: {error}";
            return false;
        }

        if (!StressStrainEditor.TryApply(_working, out var withStressStrain, out error))
        {
            material = null;
            return false;
        }

        if (!CreepEditor.TryApply(withStressStrain, out var withCreep, out error))
        {
            material = null;
            return false;
        }

        if (!StressRuptureEditor.TryApply(withCreep, out var withRupture, out error))
        {
            material = null;
            return false;
        }

        if (!FatigueEditor.TryApply(withRupture, out var withFatigue, out error))
        {
            material = null;
            return false;
        }

        if (!CyclicEditor.TryApply(withFatigue, out var withCyclic, out error))
        {
            material = null;
            return false;
        }

        if (!ExternalPressureEditor.TryApply(withCyclic, out var withExternalPressure, out error))
        {
            material = null;
            return false;
        }

        if (!LarsonMillerEditor.TryApply(withExternalPressure, out var withLarsonMiller, out error))
        {
            material = null;
            return false;
        }

        // Apply size-ranged Sy / Su tables.
        if (!SyEditor.TryBuildTable(out var syTable, out error))
        {
            material = null;
            error = $"Sy table: {error}";
            return false;
        }

        var withSy = StrengthPropertyCrud.setSyTable(
            syTable != null ? FSharpOption<PropertyTable>.Some(syTable) : FSharpOption<PropertyTable>.None,
            withLarsonMiller);

        if (!SuEditor.TryBuildTable(out var suTable, out error))
        {
            material = null;
            error = $"Su table: {error}";
            return false;
        }

        var withSu = StrengthPropertyCrud.setSuTable(
            suTable != null ? FSharpOption<PropertyTable>.Some(suTable) : FSharpOption<PropertyTable>.None,
            withSy);

        // Apply allowable stress tables (each editor only replaces its own source).
        var existing = withSu.StrengthProperties.AllowableStressDatasets.ToReadOnlyList();

        if (!AllowableDiv1Editor.TryBuildDatasets(existing, out var afterDiv1, out error))
        {
            material = null;
            error = $"Allowable Div.1: {error}";
            return false;
        }

        if (!AllowableDiv1HighEditor.TryBuildDatasets(afterDiv1!, out var afterDiv1H, out error))
        {
            material = null;
            error = $"Allowable Div.1 High: {error}";
            return false;
        }

        if (!AllowableDiv2Editor.TryBuildDatasets(afterDiv1H!, out var afterDiv2, out error))
        {
            material = null;
            error = $"Allowable Div.2: {error}";
            return false;
        }

        material = StrengthPropertyCrud.setAllowableStressDatasets(
            afterDiv2!.ToFSharpList(), withSu);
        return true;
    }

    // ── Internal helpers ─────────────────────────────────────────────────────

    /// <summary>Loads all five size-ranged strength editors from a material snapshot.</summary>
    private void LoadStrengthEditors(Material m)
    {
        var sp = m.StrengthProperties;

        SyEditor.LoadFromTable(sp.SyTable != null ? sp.SyTable.Value : null);
        SuEditor.LoadFromTable(sp.SuTable != null ? sp.SuTable.Value : null);
        AllowableDiv1Editor.LoadFromMaterial(m);
        AllowableDiv1HighEditor.LoadFromMaterial(m);
        AllowableDiv2Editor.LoadFromMaterial(m);
    }

    /// <summary>Loads the selected table's rows from the working material into the grid.</summary>
    private void LoadRows()
    {
        Rows.Clear();

        foreach (var row in _selectedTable.Read(_working))
        {
            Rows.Add(new TableRowViewModel(row));
        }

        SelectedRow = null;
        StatusMessage = $"{_selectedTable.Title}: {Rows.Count} row(s).";
        RaisePropertyChanged(nameof(SelectedTable));
    }

    /// <summary>
    /// Parses the visible grid and writes it back into the working material.
    /// </summary>
    /// <param name="error">Receives a message naming the offending row and column, or <c>null</c>.</param>
    /// <returns><c>true</c> when every non-blank row parsed and the write succeeded.</returns>
    private bool TryCommitCurrentTable(out string? error)
    {
        error = null;
        var parsed = new List<string?[]>(Rows.Count);

        for (var i = 0; i < Rows.Count; i++)
        {
            // Fully blank rows are treated as "not filled in yet" and dropped rather than rejected,
            // so an accidental Add Row cannot block the whole commit.
            if (Rows[i].IsBlank())
            {
                continue;
            }

            if (!Rows[i].TryParse(_selectedTable.Columns, out var values, out var rowError))
            {
                error = $"row {i + 1}: {rowError}";
                return false;
            }

            parsed.Add(values);
        }

        _working = _selectedTable.Write(_working, parsed);
        return true;
    }

    /// <summary>Appends an empty row and selects it.</summary>
    private void AddRow()
    {
        var row = new TableRowViewModel(_selectedTable.Columns.Count);
        Rows.Add(row);
        SelectedRow = row;
        StatusMessage = $"{_selectedTable.Title}: {Rows.Count} row(s).";
    }

    /// <summary>Removes the selected row.</summary>
    private void DeleteRow()
    {
        if (SelectedRow is null)
        {
            return;
        }

        Rows.Remove(SelectedRow);
        SelectedRow = null;
        StatusMessage = $"{_selectedTable.Title}: {Rows.Count} row(s).";
    }
}
