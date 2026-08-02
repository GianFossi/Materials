using System.Collections.ObjectModel;
using MaterialLibrary.Domain;
using MaterialLibraryCrudApp.Interop;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>Master/detail editor for fatigue S-N curves.</summary>
public sealed class FatigueTableEditorViewModel : ObservableObject
{
    private FatigueItemViewModel? _selectedTable;
    private FatiguePointViewModel? _selectedPoint;
    /// <summary>Creates the editor over a material's fatigue curves.</summary>
    /// <param name="material">Material to read the curves from; never mutated.</param>
    public FatigueTableEditorViewModel(Material material)
    {
        foreach (var table in material.StrengthProperties.FatigueCurves.ToReadOnlyList()) Tables.Add(new FatigueItemViewModel(table));
        AddTableCommand = new RelayCommand(() => { var item = new FatigueItemViewModel(); Tables.Add(item); SelectedTable = item; });
        DeleteTableCommand = new RelayCommand(() => { if (SelectedTable is null) return; Tables.Remove(SelectedTable); SelectedTable = Tables.FirstOrDefault(); }, () => SelectedTable is not null);
        AddPointCommand = new RelayCommand(() => { if (SelectedTable is null) return; var point = new FatiguePointViewModel(); SelectedTable.Points.Add(point); SelectedPoint = point; }, () => SelectedTable is not null);
        DeletePointCommand = new RelayCommand(() => { if (SelectedTable is null || SelectedPoint is null) return; SelectedTable.Points.Remove(SelectedPoint); SelectedPoint = null; }, () => SelectedPoint is not null);
        SelectedTable = Tables.FirstOrDefault();
    }
    /// <summary>Curves being edited, one entry per stored curve.</summary>
    public ObservableCollection<FatigueItemViewModel> Tables { get; } = [];
    /// <summary>Table whose points are shown in the detail pane.</summary>
    public FatigueItemViewModel? SelectedTable { get => _selectedTable; set { if (SetProperty(ref _selectedTable, value)) { SelectedPoint = null; DeleteTableCommand.RaiseCanExecuteChanged(); AddPointCommand.RaiseCanExecuteChanged(); } } }
    /// <summary>Point selected within the current table.</summary>
    public FatiguePointViewModel? SelectedPoint { get => _selectedPoint; set { if (SetProperty(ref _selectedPoint, value)) DeletePointCommand.RaiseCanExecuteChanged(); } }
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
        var built = new List<FatigueTable>();
        for (var i = 0; i < Tables.Count; i++) { if (!Tables[i].TryBuild(out var table, out error)) { updated = material; error = $"fatigue table {i + 1}: {error}"; return false; } built.Add(table!); }
        var s = material.StrengthProperties;
        updated = new Material(material.Id, material.Name, material.ProductForm, material.NominalComposition, material.Specification, material.ASMESpecification, material.Grade, material.Class_Condition_Tempering, material.AlloyIdentification_UNS, material.Family, material.AllowableStressLevel, material.ApplicableAsmeCodes, material.AsmeNoteReferences, material.BasicProperties, material.PhysicalProperties,
            new StrengthProperties(s.AllowableStresses, s.AllowableStressDatasets, s.TensileProperties, s.CompressionProperties, s.StressStrainTables, s.CyclicStrainTables, s.ExternalPressureTables, s.NortonModels, s.GarofaloModels, s.KachanovOmegaModels, s.CreepTables, s.AverageCreepStrainRateStress, s.MinimumCreepStrainRateStress, s.StressRuptureCurves, s.AverageCreepRuptureStress, s.MinimumCreepRuptureStress, s.LarsonMillerCurves, built.ToFSharpList()), material.SpecialProperties, material.MaximumAllowableTemperature, material.TimeDepenedingStartTemperature, material.WeldingInfo, material.CreatedDate, DateTime.UtcNow, material.Notes);
        error = null; return true;
    }
}

/// <summary>One editable fatigue curve: its conditions plus its cycles/stress points.</summary>
public sealed class FatigueItemViewModel : ObservableObject
{
    private double _temperature;
    private double? _duration;
    /// <summary>Creates a new curve with default metadata.</summary>
    public FatigueItemViewModel() { Points.Add(new FatiguePointViewModel()); }
    /// <summary>Creates an editable copy of an existing curve.</summary>
    /// <param name="table">Curve to mirror.</param>
    public FatigueItemViewModel(FatigueTable table) { _temperature = table.ReferenceTemperature; _duration = table.ReferenceDurationHours.AsNullable(); var column = table.Table.Columns.ToReadOnlyList().FirstOrDefault(); if (column is not null) foreach (var entry in column.Entries.ToReadOnlyList()) Points.Add(new FatiguePointViewModel(entry.X, entry.Value)); }
    /// <summary>Temperature the curve applies at (degC).</summary>
    public double Temperature { get => _temperature; set { if (SetProperty(ref _temperature, value)) RaisePropertyChanged(nameof(DisplayName)); } }
    /// <summary>Reference hold duration (hours) for a time-dependent curve, or <c>null</c> when time-independent.</summary>
    public double? ReferenceDurationHours { get => _duration; set => SetProperty(ref _duration, value); }
    /// <summary>Label shown in the curve list.</summary>
    public string DisplayName => $"{Temperature:G5} C";
    /// <summary>Points of this curve.</summary>
    public ObservableCollection<FatiguePointViewModel> Points { get; } = [];
    /// <summary>Validates the buffer and converts it into the immutable domain record.</summary>
    /// <param name="table">Receives the built curve on success; <c>null</c> on failure.</param>
    /// <param name="error">Receives a user-facing validation message on failure.</param>
    /// <returns><c>true</c> when the curve was valid.</returns>
    public bool TryBuild(out FatigueTable? table, out string? error)
    {
        var result = PropertyTableModule.create1D("Fatigue", "Cycles", "", "Stress amplitude", "MPa", XBoundaryPolicy.FlatExtrapolate, Points.Select(p => new TableColumnEntry(p.Cycles, p.StressMpa)).ToFSharpList());
        if (!result.TryUnwrap(out var propertyTable, out var domainError)) { table = null; error = MaterialErrorFormat.Format(domainError); return false; }
        table = FatigueTableModule.create(propertyTable, Temperature, FSharpInterop.ToOption(ReferenceDurationHours)); error = null; return true;
    }
}

/// <summary>One editable point of a fatigue curve.</summary>
public sealed class FatiguePointViewModel : ObservableObject
{
    private double _cycles;
    private double _stressMpa;
    /// <summary>Creates an empty point.</summary>
    public FatiguePointViewModel() { }
    /// <summary>Creates a point from stored values.</summary>
    /// <param name="cycles">Number of cycles to failure (dimensionless).</param>
    /// <param name="stress">Alternating stress amplitude (MPa).</param>
    public FatiguePointViewModel(double cycles, double stress) { _cycles = cycles; _stressMpa = stress; }
    /// <summary>Number of cycles to failure (dimensionless).</summary>
    public double Cycles { get => _cycles; set => SetProperty(ref _cycles, value); }
    /// <summary>Alternating stress amplitude at this cycle count (MPa).</summary>
    public double StressMpa { get => _stressMpa; set => SetProperty(ref _stressMpa, value); }
}
