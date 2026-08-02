using System.Collections.ObjectModel;
using MaterialLibrary.Domain;
using MaterialLibraryCrudApp.Interop;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>
/// Drives the material-tables editor: a list of editable tables on the left, the rows of the
/// selected table on the right.
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

        AddRowCommand = new RelayCommand(AddRow);
        DeleteRowCommand = new RelayCommand(DeleteRow, () => SelectedRow is not null);
        StressStrainEditor = new StressStrainTableEditorViewModel(material);
        CreepEditor = new CreepTableEditorViewModel(material);
        StressRuptureEditor = new StressRuptureTableEditorViewModel(material);
        FatigueEditor = new FatigueTableEditorViewModel(material);
        CyclicEditor = new CyclicStrainTableEditorViewModel(material);
        ExternalPressureEditor = new ExternalPressureTableEditorViewModel(material);
        LarsonMillerEditor = new LarsonMillerEditorViewModel(material);

        LoadRows();
    }

    /// <summary>Tables available for editing.</summary>
    public IReadOnlyList<MaterialTableSpec> Tables => MaterialTableSpecs.All;

    /// <summary>Rows of the currently selected table.</summary>
    public ObservableCollection<TableRowViewModel> Rows { get; } = [];

    /// <summary>Master/detail editor for nested stress-strain tables.</summary>
    public StressStrainTableEditorViewModel StressStrainEditor { get; }
    public CreepTableEditorViewModel CreepEditor { get; }
    public StressRuptureTableEditorViewModel StressRuptureEditor { get; }
    public FatigueTableEditorViewModel FatigueEditor { get; }
    public CyclicStrainTableEditorViewModel CyclicEditor { get; }
    public ExternalPressureTableEditorViewModel ExternalPressureEditor { get; }
    public LarsonMillerEditorViewModel LarsonMillerEditor { get; }

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
    /// Commits the visible grid and returns the material carrying every table edit.
    /// </summary>
    /// <param name="material">Receives the updated material on success; <c>null</c> on failure.</param>
    /// <param name="error">Receives a user-facing validation message on failure; <c>null</c> on success.</param>
    /// <returns><c>true</c> when the current table was valid and the result was produced.</returns>
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

        material = withLarsonMiller;
        return true;
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
