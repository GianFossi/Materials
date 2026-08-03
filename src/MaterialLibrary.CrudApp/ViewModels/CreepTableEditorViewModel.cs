using System.Collections.ObjectModel;
using System.Globalization;
using MaterialLibrary.Domain;
using MaterialLibraryCrudApp.Interop;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>Master/detail editor for validated time-strain creep curves.</summary>
public sealed class CreepTableEditorViewModel : ObservableObject
{
    private CreepTableItemViewModel? _selectedTable;
    private CreepPointItemViewModel? _selectedPoint;
    private string _statusMessage = string.Empty;

    /// <summary>Creates the editor over a material's creep tables.</summary>
    /// <param name="material">Material to read the tables from; never mutated.</param>
    public CreepTableEditorViewModel(Material material)
    {
        foreach (var table in material.StrengthProperties.CreepTables.ToReadOnlyList()) Tables.Add(new CreepTableItemViewModel(table));
        AddTableCommand = new RelayCommand(AddTable);
        DeleteTableCommand = new RelayCommand(DeleteTable, () => SelectedTable is not null);
        AddPointCommand = new RelayCommand(AddPoint, () => SelectedTable is not null);
        DeletePointCommand = new RelayCommand(DeletePoint, () => SelectedPoint is not null);
        SelectedTable = Tables.FirstOrDefault();
        StatusMessage = $"{Tables.Count} creep table(s).";
    }

    /// <summary>Tables being edited, one entry per stored table.</summary>
    public ObservableCollection<CreepTableItemViewModel> Tables { get; } = [];
    /// <summary>Table whose points are shown in the detail pane.</summary>
    public CreepTableItemViewModel? SelectedTable { get => _selectedTable; set { if (SetProperty(ref _selectedTable, value)) { SelectedPoint = null; DeleteTableCommand.RaiseCanExecuteChanged(); AddPointCommand.RaiseCanExecuteChanged(); } } }
    /// <summary>Point selected within the current table.</summary>
    public CreepPointItemViewModel? SelectedPoint { get => _selectedPoint; set { if (SetProperty(ref _selectedPoint, value)) DeletePointCommand.RaiseCanExecuteChanged(); } }
    /// <summary>Validation or progress message shown under the editor.</summary>
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    /// <summary>Appends an empty table and selects it.</summary>
    public RelayCommand AddTableCommand { get; }
    /// <summary>Removes the selected table.</summary>
    public RelayCommand DeleteTableCommand { get; }
    /// <summary>Appends an empty point to the selected table.</summary>
    public RelayCommand AddPointCommand { get; }
    /// <summary>Removes the selected point.</summary>
    public RelayCommand DeletePointCommand { get; }

    /// <summary>Writes the edited tables back into a material.</summary>
    /// <param name="material">Source material; left unchanged.</param>
    /// <param name="updated">Receives a new material carrying the edited tables.</param>
    /// <param name="error">Receives a validation message on failure; <c>null</c> on success.</param>
    /// <returns><c>true</c> when every table was valid and the edits were applied.</returns>
    /// <remarks>
    /// Rebuilds <c>StrengthProperties</c> positionally because F# records offer no copy-and-update
    /// expression from C#; every sibling collection is carried through so nothing outside this
    /// editor is lost.
    /// </remarks>
    public bool TryApply(Material material, out Material updated, out string? error)
    {
        var built = new List<CreepTable>();
        for (var i = 0; i < Tables.Count; i++)
        {
            if (!Tables[i].TryBuild(out var table, out error)) { updated = material; error = $"creep table {i + 1}: {error}"; return false; }
            built.Add(table!);
        }
        updated = new Material(material.Id, material.Name, material.ProductForm, material.NominalComposition, material.Specification, material.ASMESpecification, material.Grade, material.Class_Condition_Tempering, material.AlloyIdentification_UNS, material.Family, material.AllowableStressLevel, material.ApplicableAsmeCodes, material.AsmeNoteReferences, material.BasicProperties, material.PhysicalProperties,
            new StrengthProperties(material.StrengthProperties.SyTable, material.StrengthProperties.SuTable, material.StrengthProperties.AllowableStressDatasets, material.StrengthProperties.CompressionProperties, material.StrengthProperties.StressStrainTables, material.StrengthProperties.CyclicStrainTables, material.StrengthProperties.ExternalPressureTables, material.StrengthProperties.NortonModels, material.StrengthProperties.GarofaloModels, material.StrengthProperties.KachanovOmegaModels, built.ToFSharpList(), material.StrengthProperties.AverageCreepStrainRateStress, material.StrengthProperties.MinimumCreepStrainRateStress, material.StrengthProperties.StressRuptureCurves, material.StrengthProperties.AverageCreepRuptureStress, material.StrengthProperties.MinimumCreepRuptureStress, material.StrengthProperties.LarsonMillerCurves, material.StrengthProperties.FatigueCurves), material.SpecialProperties, material.MaximumAllowableTemperature, material.TimeDepenedingStartTemperature, material.WeldingInfo, material.CreatedDate, DateTime.UtcNow, material.Notes);
        error = null; StatusMessage = $"Saved {built.Count} creep table(s)."; return true;
    }

    private void AddTable() { var item = new CreepTableItemViewModel(); Tables.Add(item); SelectedTable = item; }
    private void DeleteTable() { if (SelectedTable is null) return; var i = Tables.IndexOf(SelectedTable); Tables.Remove(SelectedTable); SelectedTable = Tables.Count == 0 ? null : Tables[Math.Clamp(i, 0, Tables.Count - 1)]; }
    private void AddPoint() { if (SelectedTable is null) return; var point = new CreepPointItemViewModel(); SelectedTable.Points.Add(point); SelectedPoint = point; }
    private void DeletePoint() { if (SelectedTable is null || SelectedPoint is null) return; SelectedTable.Points.Remove(SelectedPoint); SelectedPoint = null; }
}

/// <summary>One editable creep table: its conditions plus its time/strain points.</summary>
public sealed class CreepTableItemViewModel : ObservableObject
{
    private double _temperature;
    private double _stress;
    private string _description = "Creep curve";
    /// <summary>Creates a new table with default conditions.</summary>
    public CreepTableItemViewModel() { Points.Add(new CreepPointItemViewModel()); }
    /// <summary>Creates an editable copy of an existing table.</summary>
    /// <param name="table">Table to mirror.</param>
    public CreepTableItemViewModel(CreepTable table)
    {
        _temperature = table.ReferenceTemperature; _stress = table.AppliedStress.AsNullable() ?? 0; _description = table.Table.Name;
        var column = table.Table.Columns.ToReadOnlyList().FirstOrDefault();
        if (column is not null) foreach (var entry in column.Entries.ToReadOnlyList()) Points.Add(new CreepPointItemViewModel(entry.X, entry.Value));
    }
    /// <summary>Temperature the table applies at (degC).</summary>
    public double Temperature { get => _temperature; set => SetProperty(ref _temperature, value); }
    /// <summary>Constant stress applied during the creep test (MPa).</summary>
    public double AppliedStress { get => _stress; set => SetProperty(ref _stress, value); }
    /// <summary>Free-text description of the table.</summary>
    public string Description { get => _description; set => SetProperty(ref _description, value); }
    /// <summary>Creep-strain points of this table.</summary>
    public ObservableCollection<CreepPointItemViewModel> Points { get; } = [];
    /// <summary>Label shown in the table list.</summary>
    public string DisplayName => $"{Temperature.ToString("G5", CultureInfo.InvariantCulture)} C / {AppliedStress.ToString("G5", CultureInfo.InvariantCulture)} MPa";
    /// <summary>Validates the buffer and converts it into the immutable domain record.</summary>
    /// <param name="table">Receives the built table on success; <c>null</c> on failure.</param>
    /// <param name="error">Receives a user-facing validation message on failure.</param>
    /// <returns><c>true</c> when the table was valid.</returns>
    public bool TryBuild(out CreepTable? table, out string? error)
    {
        var points = Points.Select(p => new CreepPoint(p.Time, p.Strain)).ToFSharpList();
        var result = CreepTableBuilder.create(Temperature, AppliedStress, Description, points);
        if (result.TryUnwrap(out table, out var domainError)) { error = null; return true; }
        error = MaterialErrorFormat.Format(domainError); table = null; return false;
    }
}

/// <summary>One editable point of a creep table.</summary>
public sealed class CreepPointItemViewModel : ObservableObject
{
    private double _time;
    private double _strain;
    /// <summary>Creates an empty point.</summary>
    public CreepPointItemViewModel() { }
    /// <summary>Creates a point from stored values.</summary>
    /// <param name="time">Elapsed time (hours).</param>
    /// <param name="strain">Creep strain at that time (dimensionless).</param>
    public CreepPointItemViewModel(double time, double strain) { _time = time; _strain = strain; }
    /// <summary>Elapsed time (hours).</summary>
    public double Time { get => _time; set => SetProperty(ref _time, value); }
    /// <summary>Creep strain at this time (dimensionless).</summary>
    public double Strain { get => _strain; set => SetProperty(ref _strain, value); }
}
