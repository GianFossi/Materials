using System.Collections.ObjectModel;
using MaterialLibrary.Domain;
using MaterialLibraryCrudApp.Interop;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>Master/detail editor for Larson-Miller parameter curves.</summary>
public sealed class LarsonMillerEditorViewModel : ObservableObject
{
    private LarsonItemViewModel? _selected;
    /// <summary>Creates the editor over a material's Larson-Miller curves.</summary>
    /// <param name="material">Material to read the curves from; never mutated.</param>
    public LarsonMillerEditorViewModel(Material material)
    {
        foreach (var curve in material.StrengthProperties.LarsonMillerCurves.ToReadOnlyList()) Curves.Add(new LarsonItemViewModel(curve));
        AddCurveCommand = new RelayCommand(() => { var item = new LarsonItemViewModel(); Curves.Add(item); Selected = item; });
        DeleteCurveCommand = new RelayCommand(() => { if (Selected is null) return; Curves.Remove(Selected); Selected = Curves.FirstOrDefault(); }, () => Selected is not null);
        Selected = Curves.FirstOrDefault();
    }
    /// <summary>Curves being edited, one entry per stored curve.</summary>
    public ObservableCollection<LarsonItemViewModel> Curves { get; } = [];
    /// <summary>Curve whose points are shown in the detail pane.</summary>
    public LarsonItemViewModel? Selected { get => _selected; set { if (SetProperty(ref _selected, value)) DeleteCurveCommand.RaiseCanExecuteChanged(); } }
    /// <summary>Appends an empty curve and selects it.</summary>
    public RelayCommand AddCurveCommand { get; }
    /// <summary>Removes the selected curve.</summary>
    public RelayCommand DeleteCurveCommand { get; }
    /// <summary>Writes the edited curves back into a material.</summary>
    /// <param name="material">Source material; left unchanged.</param>
    /// <param name="updated">Receives a new material carrying the edited curves.</param>
    /// <param name="error">Receives a validation message on failure; <c>null</c> on success.</param>
    /// <returns><c>true</c> when the edits were applied.</returns>
    /// <remarks>
    /// Rebuilds <c>StrengthProperties</c> positionally because F# records offer no copy-and-update
    /// expression from C#; every sibling collection is carried through so nothing outside this
    /// editor is lost.
    /// </remarks>
    public bool TryApply(Material material, out Material updated, out string? error)
    {
        error = null;
        var curves = Curves.Select(c => c.Build()).ToFSharpList();
        var s = material.StrengthProperties;
        updated = new Material(material.Id, material.Name, material.ProductForm, material.NominalComposition, material.Specification, material.ASMESpecification, material.Grade, material.Class_Condition_Tempering, material.AlloyIdentification_UNS, material.Family, material.AllowableStressLevel, material.ApplicableAsmeCodes, material.AsmeNoteReferences, material.BasicProperties, material.PhysicalProperties, new StrengthProperties(s.AllowableStresses, s.AllowableStressDatasets, s.TensileProperties, s.TensileStrengthDatasets, s.CompressionProperties, s.StressStrainTables, s.CyclicStrainTables, s.ExternalPressureTables, s.NortonModels, s.GarofaloModels, s.KachanovOmegaModels, s.CreepTables, s.AverageCreepStrainRateStress, s.MinimumCreepStrainRateStress, s.StressRuptureCurves, s.AverageCreepRuptureStress, s.MinimumCreepRuptureStress, curves, s.FatigueCurves), material.SpecialProperties, material.MaximumAllowableTemperature, material.TimeDepenedingStartTemperature, material.WeldingInfo, material.CreatedDate, DateTime.UtcNow, material.Notes); return true;
    }
}

/// <summary>One editable Larson-Miller curve: its metadata plus its point list.</summary>
public sealed class LarsonItemViewModel : ObservableObject
{
    private string _material = "Material";
    private string _description = "Larson-Miller curve";
    /// <summary>Creates a new curve seeded with a single empty point.</summary>
    public LarsonItemViewModel() { Points.Add(new LarsonPointViewModel()); }
    /// <summary>Creates an editable copy of an existing curve.</summary>
    /// <param name="curve">Curve to mirror.</param>
    public LarsonItemViewModel(LarsonMillerCurve curve) { _material = curve.Material; _description = curve.Description; foreach (var point in curve.Points.ToReadOnlyList()) Points.Add(new LarsonPointViewModel(point.LarsonMillerParameter, point.Stress)); }
    /// <summary>Material name recorded on the curve.</summary>
    public string Material { get => _material; set => SetProperty(ref _material, value); }
    /// <summary>Free-text description of the curve.</summary>
    public string Description { get => _description; set => SetProperty(ref _description, value); }
    /// <summary>Points of this curve.</summary>
    public ObservableCollection<LarsonPointViewModel> Points { get; } = [];
    /// <summary>Converts the editable curve back into the immutable domain record.</summary>
    /// <returns>A curve carrying the current metadata and points.</returns>
    public LarsonMillerCurve Build() => new(Material, Description, Points.Select(p => new LarsonMillerPoint(p.Parameter, p.Stress)).ToFSharpList());
}

/// <summary>One editable point of a Larson-Miller curve.</summary>
public sealed class LarsonPointViewModel : ObservableObject
{
    private double _parameter;
    private double _stress;
    /// <summary>Creates an empty point.</summary>
    public LarsonPointViewModel() { }
    /// <summary>Creates a point from stored values.</summary>
    /// <param name="parameter">Larson-Miller parameter (dimensionless).</param>
    /// <param name="stress">Stress at that parameter (MPa).</param>
    public LarsonPointViewModel(double parameter, double stress) { _parameter = parameter; _stress = stress; }
    /// <summary>Larson-Miller parameter (dimensionless).</summary>
    public double Parameter { get => _parameter; set => SetProperty(ref _parameter, value); }
    /// <summary>Stress at this parameter (MPa).</summary>
    public double Stress { get => _stress; set => SetProperty(ref _stress, value); }
}
