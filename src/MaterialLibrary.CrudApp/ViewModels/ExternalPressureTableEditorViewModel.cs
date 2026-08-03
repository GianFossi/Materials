using System.Collections.ObjectModel;
using MaterialLibrary.Domain;
using MaterialLibraryCrudApp.Interop;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>Validated editor model for external-pressure Factor A/Factor B curves.</summary>
public sealed class ExternalPressureTableEditorViewModel : ObservableObject
{
    private ExternalPressureItemViewModel? _selected;
    /// <summary>Creates the editor over a material's external-pressure tables.</summary>
    /// <param name="material">Material to read the tables from; never mutated.</param>
    public ExternalPressureTableEditorViewModel(Material material)
    {
        foreach (var table in material.StrengthProperties.ExternalPressureTables.ToReadOnlyList()) Tables.Add(new ExternalPressureItemViewModel(table));
        AddTableCommand = new RelayCommand(() => { var item = new ExternalPressureItemViewModel(); Tables.Add(item); Selected = item; });
        DeleteTableCommand = new RelayCommand(() => { if (Selected is null) return; Tables.Remove(Selected); Selected = Tables.FirstOrDefault(); }, () => Selected is not null);
        Selected = Tables.FirstOrDefault();
    }
    /// <summary>Tables being edited, one entry per stored table.</summary>
    public ObservableCollection<ExternalPressureItemViewModel> Tables { get; } = [];
    /// <summary>Table whose points are shown in the detail pane.</summary>
    public ExternalPressureItemViewModel? Selected { get => _selected; set { if (SetProperty(ref _selected, value)) DeleteTableCommand.RaiseCanExecuteChanged(); } }
    /// <summary>Appends an empty table and selects it.</summary>
    public RelayCommand AddTableCommand { get; }
    /// <summary>Removes the selected table.</summary>
    public RelayCommand DeleteTableCommand { get; }
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
        error = null;
        var built = new List<ExternalPressureTable>();
        foreach (var item in Tables) { if (!item.TryBuild(out var table, out error)) { updated = material; return false; } built.Add(table!); }
        var s = material.StrengthProperties;
        updated = new Material(material.Id, material.Name, material.ProductForm, material.NominalComposition, material.Specification, material.ASMESpecification, material.Grade, material.Class_Condition_Tempering, material.AlloyIdentification_UNS, material.Family, material.AllowableStressLevel, material.ApplicableAsmeCodes, material.AsmeNoteReferences, material.BasicProperties, material.PhysicalProperties, new StrengthProperties(s.SyTable, s.SuTable, s.AllowableStressDatasets, s.CompressionProperties, s.StressStrainTables, s.CyclicStrainTables, built.ToFSharpList(), s.NortonModels, s.GarofaloModels, s.KachanovOmegaModels, s.CreepTables, s.AverageCreepStrainRateStress, s.MinimumCreepStrainRateStress, s.StressRuptureCurves, s.AverageCreepRuptureStress, s.MinimumCreepRuptureStress, s.LarsonMillerCurves, s.FatigueCurves), material.SpecialProperties, material.MaximumAllowableTemperature, material.TimeDepenedingStartTemperature, material.WeldingInfo, material.CreatedDate, DateTime.UtcNow, material.Notes); return true;
    }
}

/// <summary>One editable external-pressure chart: its metadata plus its Factor A points.</summary>
public sealed class ExternalPressureItemViewModel : ObservableObject
{
    private double _temperature;
    private double? _duration;
    private double? _reduction;
    /// <summary>Creates a new table with default metadata.</summary>
    public ExternalPressureItemViewModel() { Points.Add(new ExternalPressurePointViewModel()); }
    /// <summary>Creates an editable copy of an existing table.</summary>
    /// <param name="table">Table to mirror.</param>
    public ExternalPressureItemViewModel(ExternalPressureTable table) { _temperature = table.ReferenceTemperature; _duration = table.ReferenceDurationHours.AsNullable(); _reduction = table.ReductionFactor.AsNullable(); var column = table.Table.Columns.ToReadOnlyList().FirstOrDefault(); if (column is not null) foreach (var e in column.Entries.ToReadOnlyList()) Points.Add(new ExternalPressurePointViewModel(e.X, e.Value)); }
    /// <summary>Temperature the chart applies at (degC).</summary>
    public double Temperature { get => _temperature; set => SetProperty(ref _temperature, value); }
    /// <summary>Reference duration (hours) for a time-dependent chart, or <c>null</c> when time-independent.</summary>
    public double? DurationHours { get => _duration; set => SetProperty(ref _duration, value); }
    /// <summary>Optional reduction factor applied to the chart (dimensionless).</summary>
    public double? ReductionFactor { get => _reduction; set => SetProperty(ref _reduction, value); }
    /// <summary>Chart points mapping Factor A to allowable compressive stress.</summary>
    public ObservableCollection<ExternalPressurePointViewModel> Points { get; } = [];
    /// <summary>Validates the buffer and converts it into the immutable domain record.</summary>
    /// <param name="table">Receives the built table on success; <c>null</c> on failure.</param>
    /// <param name="error">Receives a user-facing validation message on failure.</param>
    /// <returns><c>true</c> when the table was valid.</returns>
    public bool TryBuild(out ExternalPressureTable? table, out string? error)
    {
        var result = PropertyTableModule.create1D("External pressure", "Factor A", "", "Factor B", "MPa", XBoundaryPolicy.FlatExtrapolate, Points.Select(p => new TableColumnEntry(p.FactorA, p.CompressiveStress)).ToFSharpList());
        if (!result.TryUnwrap(out var propertyTable, out var domainError)) { table = null; error = MaterialErrorFormat.Format(domainError); return false; }
        var candidate = ExternalPressureTableModule.create(propertyTable, Temperature, FSharpInterop.ToOption(DurationHours), ExternalPressureTableSource.MaterialDatabase, FSharpInterop.ToOption(ReductionFactor));
        var validated = ExternalPressureTableModule.validate(candidate);
        if (validated.TryUnwrap(out table, out domainError)) { error = null; return true; }
        error = MaterialErrorFormat.Format(domainError); table = null; return false;
    }
}

/// <summary>One editable point of an external-pressure chart.</summary>
public sealed class ExternalPressurePointViewModel : ObservableObject
{
    private double _factorA;
    private double _compressiveStress;
    /// <summary>Creates an empty point.</summary>
    public ExternalPressurePointViewModel() { }
    /// <summary>Creates a point from stored values.</summary>
    /// <param name="a">Factor A (dimensionless).</param>
    /// <param name="b">Allowable compressive stress (MPa).</param>
    public ExternalPressurePointViewModel(double a, double b) { _factorA = a; _compressiveStress = b; }
    /// <summary>Factor A, the strain axis of the ASME external-pressure chart (dimensionless).</summary>
    public double FactorA { get => _factorA; set => SetProperty(ref _factorA, value); }
    /// <summary>Allowable compressive stress at this Factor A (MPa).</summary>
    public double CompressiveStress { get => _compressiveStress; set => SetProperty(ref _compressiveStress, value); }
}
