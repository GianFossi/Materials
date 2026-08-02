using System.Collections.ObjectModel;
using MaterialLibrary.Domain;
using MaterialLibraryCrudApp.Interop;
using Microsoft.FSharp.Core;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>Validated editor model for cyclic stress-strain and hysteresis-range curves.</summary>
public sealed class CyclicStrainTableEditorViewModel : ObservableObject
{
    private CyclicItemViewModel? _selected;
    /// <summary>Creates the editor over a material's cyclic-strain tables.</summary>
    /// <param name="material">Material to read the tables from; never mutated.</param>
    public CyclicStrainTableEditorViewModel(Material material)
    {
        foreach (var table in material.StrengthProperties.CyclicStrainTables.ToReadOnlyList()) Tables.Add(new CyclicItemViewModel(table));
        AddTableCommand = new RelayCommand(() => { var item = new CyclicItemViewModel(); Tables.Add(item); Selected = item; });
        DeleteTableCommand = new RelayCommand(() => { if (Selected is null) return; Tables.Remove(Selected); Selected = Tables.FirstOrDefault(); }, () => Selected is not null);
        Selected = Tables.FirstOrDefault();
    }
    /// <summary>Tables being edited, one entry per stored table.</summary>
    public ObservableCollection<CyclicItemViewModel> Tables { get; } = [];
    /// <summary>Table whose points are shown in the detail pane.</summary>
    public CyclicItemViewModel? Selected { get => _selected; set { if (SetProperty(ref _selected, value)) DeleteTableCommand.RaiseCanExecuteChanged(); } }
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
        var built = new List<CyclicStrainTable>();
        error = null;
        foreach (var item in Tables) { if (!item.TryBuild(out var table, out error)) { updated = material; return false; } built.Add(table!); }
        var s = material.StrengthProperties;
        updated = new Material(material.Id, material.Name, material.ProductForm, material.NominalComposition, material.Specification, material.ASMESpecification, material.Grade, material.Class_Condition_Tempering, material.AlloyIdentification_UNS, material.Family, material.AllowableStressLevel, material.ApplicableAsmeCodes, material.AsmeNoteReferences, material.BasicProperties, material.PhysicalProperties, new StrengthProperties(s.AllowableStresses, s.AllowableStressDatasets, s.TensileProperties, s.CompressionProperties, s.StressStrainTables, built.ToFSharpList(), s.ExternalPressureTables, s.NortonModels, s.GarofaloModels, s.KachanovOmegaModels, s.CreepTables, s.AverageCreepStrainRateStress, s.MinimumCreepStrainRateStress, s.StressRuptureCurves, s.AverageCreepRuptureStress, s.MinimumCreepRuptureStress, s.LarsonMillerCurves, s.FatigueCurves), material.SpecialProperties, material.MaximumAllowableTemperature, material.TimeDepenedingStartTemperature, material.WeldingInfo, material.CreatedDate, DateTime.UtcNow, material.Notes); return true;
    }
}

/// <summary>One editable cyclic-strain table: its coefficients plus its curve points.</summary>
public sealed class CyclicItemViewModel : ObservableObject
{
    private double _temperature;
    private double _kcss = 1;
    private double _ncss = 1;
    private string _materialDescription = "Material";
    private string _description = "Cyclic strain";
    /// <summary>Creates a new table with default coefficients.</summary>
    public CyclicItemViewModel() { }
    /// <summary>Creates an editable copy of an existing table.</summary>
    /// <param name="table">Table to mirror.</param>
    public CyclicItemViewModel(CyclicStrainTable table) { _temperature = table.ReferenceTemperature; _kcss = table.Kcss; _ncss = table.Ncss; _materialDescription = table.MaterialDescription; _description = table.Description; Load(table.Table, Points); Load(table.HysteresisRangeTable, HysteresisPoints); }
    /// <summary>Temperature the table applies at (degC).</summary>
    public double Temperature { get => _temperature; set => SetProperty(ref _temperature, value); }
    /// <summary>Cyclic strength coefficient of the Ramberg-Osgood cyclic curve (MPa).</summary>
    public double Kcss { get => _kcss; set => SetProperty(ref _kcss, value); }
    /// <summary>Cyclic strain-hardening exponent (dimensionless).</summary>
    public double Ncss { get => _ncss; set => SetProperty(ref _ncss, value); }
    /// <summary>Material name recorded on the table.</summary>
    public string MaterialDescription { get => _materialDescription; set => SetProperty(ref _materialDescription, value); }
    /// <summary>Free-text description of the table.</summary>
    public string Description { get => _description; set => SetProperty(ref _description, value); }
    /// <summary>Cyclic stress-strain curve points.</summary>
    public ObservableCollection<CyclicPoint> Points { get; } = [];
    /// <summary>Hysteresis-loop points.</summary>
    public ObservableCollection<CyclicPoint> HysteresisPoints { get; } = [];
    private static void Load(PropertyTable table, ObservableCollection<CyclicPoint> target) { var column = table.Columns.ToReadOnlyList().FirstOrDefault(); if (column is not null) foreach (var e in column.Entries.ToReadOnlyList()) target.Add(new CyclicPoint(e.X, e.Value)); }
    /// <summary>Validates the buffer and converts it into the immutable domain record.</summary>
    /// <param name="table">Receives the built table on success; <c>null</c> on failure.</param>
    /// <param name="error">Receives a user-facing validation message on failure.</param>
    /// <returns><c>true</c> when the table was valid.</returns>
    public bool TryBuild(out CyclicStrainTable? table, out string? error)
    {
        var a = Make("Stress amplitude", "MPa", "Strain amplitude", Points); var h = Make("Stress range", "MPa", "Strain range", HysteresisPoints);
        if (!a.TryUnwrap(out var at, out var ae)) { table = null; error = MaterialErrorFormat.Format(ae); return false; }
        if (!h.TryUnwrap(out var ht, out var he)) { table = null; error = MaterialErrorFormat.Format(he); return false; }
        var result = CyclicStrainTableModule.validate(CyclicStrainTableModule.create(at, ht, Temperature, Kcss, Ncss, MaterialDescription, Description));
        if (result.TryUnwrap(out table, out var de)) { error = null; return true; } error = MaterialErrorFormat.Format(de); table = null; return false;
    }
    private static FSharpResult<PropertyTable, MaterialError> Make(string y, string unit, string value, IEnumerable<CyclicPoint> points) => PropertyTableModule.create1D("Cyclic strain", y, unit, value, "", XBoundaryPolicy.FlatExtrapolate, points.Select(p => new TableColumnEntry(p.X, p.Y)).ToFSharpList());
}

/// <summary>One point of a cyclic-strain or hysteresis curve.</summary>
/// <param name="X">Strain (dimensionless).</param>
/// <param name="Y">Stress (MPa).</param>
public sealed record CyclicPoint(double X, double Y);
