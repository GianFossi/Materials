using System.Collections.ObjectModel;
using MaterialLibrary.Domain;
using MaterialLibraryCrudApp.Interop;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>Validated master/detail editor model for stress-versus-rupture-time curves.</summary>
public sealed class StressRuptureTableEditorViewModel : ObservableObject
{
    private StressRuptureItemViewModel? _selectedTable;
    private StressRupturePointViewModel? _selectedPoint;
    /// <summary>Creates the editor over a material's stress-rupture tables.</summary>
    /// <param name="material">Material to read the tables from; never mutated.</param>
    public StressRuptureTableEditorViewModel(Material material)
    {
        foreach (var table in material.StrengthProperties.StressRuptureCurves.ToReadOnlyList()) Tables.Add(new StressRuptureItemViewModel(table));
        AddTableCommand = new RelayCommand(() => { var item = new StressRuptureItemViewModel(); Tables.Add(item); SelectedTable = item; });
        DeleteTableCommand = new RelayCommand(() => { if (SelectedTable is null) return; Tables.Remove(SelectedTable); SelectedTable = Tables.FirstOrDefault(); }, () => SelectedTable is not null);
        AddPointCommand = new RelayCommand(() => { if (SelectedTable is null) return; var point = new StressRupturePointViewModel(); SelectedTable.Points.Add(point); SelectedPoint = point; }, () => SelectedTable is not null);
        DeletePointCommand = new RelayCommand(() => { if (SelectedTable is null || SelectedPoint is null) return; SelectedTable.Points.Remove(SelectedPoint); SelectedPoint = null; }, () => SelectedPoint is not null);
        SelectedTable = Tables.FirstOrDefault();
    }
    /// <summary>Tables being edited, one entry per stored table.</summary>
    public ObservableCollection<StressRuptureItemViewModel> Tables { get; } = [];
    /// <summary>Table whose points are shown in the detail pane.</summary>
    public StressRuptureItemViewModel? SelectedTable { get => _selectedTable; set { if (SetProperty(ref _selectedTable, value)) { SelectedPoint = null; DeleteTableCommand.RaiseCanExecuteChanged(); AddPointCommand.RaiseCanExecuteChanged(); } } }
    /// <summary>Point selected within the current table.</summary>
    public StressRupturePointViewModel? SelectedPoint { get => _selectedPoint; set { if (SetProperty(ref _selectedPoint, value)) DeletePointCommand.RaiseCanExecuteChanged(); } }
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
        var built = new List<StressRuptureTable>();
        for (var i = 0; i < Tables.Count; i++) { if (!Tables[i].TryBuild(out var table, out error)) { updated = material; error = $"stress-rupture table {i + 1}: {error}"; return false; } built.Add(table!); }
        var strength = material.StrengthProperties;
        updated = new Material(material.Id, material.Name, material.ProductForm, material.NominalComposition, material.Specification, material.ASMESpecification, material.Grade, material.Class_Condition_Tempering, material.AlloyIdentification_UNS, material.Family, material.AllowableStressLevel, material.ApplicableAsmeCodes, material.AsmeNoteReferences, material.BasicProperties, material.PhysicalProperties,
            new StrengthProperties(strength.AllowableStresses, strength.AllowableStressDatasets, strength.TensileProperties, strength.CompressionProperties, strength.StressStrainTables, strength.CyclicStrainTables, strength.ExternalPressureTables, strength.NortonModels, strength.GarofaloModels, strength.KachanovOmegaModels, strength.CreepTables, strength.AverageCreepStrainRateStress, strength.MinimumCreepStrainRateStress, built.ToFSharpList(), strength.AverageCreepRuptureStress, strength.MinimumCreepRuptureStress, strength.LarsonMillerCurves, strength.FatigueCurves), material.SpecialProperties, material.MaximumAllowableTemperature, material.TimeDepenedingStartTemperature, material.WeldingInfo, material.CreatedDate, DateTime.UtcNow, material.Notes);
        error = null; return true;
    }
}

/// <summary>One editable stress-rupture table: its temperature plus its time/stress points.</summary>
public sealed class StressRuptureItemViewModel : ObservableObject
{
    private double _temperature;
    /// <summary>Creates a new table with default metadata.</summary>
    public StressRuptureItemViewModel() { Points.Add(new StressRupturePointViewModel()); }
    /// <summary>Creates an editable copy of an existing table.</summary>
    /// <param name="table">Table to mirror.</param>
    public StressRuptureItemViewModel(StressRuptureTable table) { _temperature = table.ReferenceTemperature; var column = table.Table.Columns.ToReadOnlyList().FirstOrDefault(); if (column is not null) foreach (var entry in column.Entries.ToReadOnlyList()) Points.Add(new StressRupturePointViewModel(entry.X, entry.Value)); }
    /// <summary>Temperature the table applies at (degC).</summary>
    public double Temperature { get => _temperature; set { if (SetProperty(ref _temperature, value)) RaisePropertyChanged(nameof(DisplayName)); } }
    /// <summary>Label shown in the table list.</summary>
    public string DisplayName => $"{Temperature:G5} C";
    /// <summary>Rupture points of this table.</summary>
    public ObservableCollection<StressRupturePointViewModel> Points { get; } = [];
    /// <summary>Validates the buffer and converts it into the immutable domain record.</summary>
    /// <param name="table">Receives the built table on success; <c>null</c> on failure.</param>
    /// <param name="error">Receives a user-facing validation message on failure.</param>
    /// <returns><c>true</c> when the table was valid.</returns>
    public bool TryBuild(out StressRuptureTable? table, out string? error)
    {
        var entries = Points.Select(p => new TableColumnEntry(p.TimeHours, p.StressMpa)).ToFSharpList();
        var result = PropertyTableModule.create1D("Stress rupture", "Time", "h", "Stress", "MPa", XBoundaryPolicy.FlatExtrapolate, entries);
        if (!result.TryUnwrap(out var propertyTable, out var domainError)) { table = null; error = MaterialErrorFormat.Format(domainError); return false; }
        table = StressRuptureTableModule.create(propertyTable, Temperature); error = null; return true;
    }
}

/// <summary>One editable point of a stress-rupture table.</summary>
public sealed class StressRupturePointViewModel : ObservableObject
{
    private double _timeHours;
    private double _stressMpa;
    /// <summary>Creates an empty point.</summary>
    public StressRupturePointViewModel() { }
    /// <summary>Creates a point from stored values.</summary>
    /// <param name="time">Time to rupture (hours).</param>
    /// <param name="stress">Stress causing rupture at that time (MPa).</param>
    public StressRupturePointViewModel(double time, double stress) { _timeHours = time; _stressMpa = stress; }
    /// <summary>Time to rupture (hours).</summary>
    public double TimeHours { get => _timeHours; set => SetProperty(ref _timeHours, value); }
    /// <summary>Stress causing rupture at this time (MPa).</summary>
    public double StressMpa { get => _stressMpa; set => SetProperty(ref _stressMpa, value); }
}
