using System.Collections.ObjectModel;
using MaterialLibrary.Domain;
using MaterialLibraryCrudApp.Interop;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>Master/detail editor for Larson-Miller parameter curves.</summary>
public sealed class LarsonMillerEditorViewModel : ObservableObject
{
    private LarsonItemViewModel? _selected;
    public LarsonMillerEditorViewModel(Material material)
    {
        foreach (var curve in material.StrengthProperties.LarsonMillerCurves.ToReadOnlyList()) Curves.Add(new LarsonItemViewModel(curve));
        AddCurveCommand = new RelayCommand(() => { var item = new LarsonItemViewModel(); Curves.Add(item); Selected = item; });
        DeleteCurveCommand = new RelayCommand(() => { if (Selected is null) return; Curves.Remove(Selected); Selected = Curves.FirstOrDefault(); }, () => Selected is not null);
        Selected = Curves.FirstOrDefault();
    }
    public ObservableCollection<LarsonItemViewModel> Curves { get; } = [];
    public LarsonItemViewModel? Selected { get => _selected; set { if (SetProperty(ref _selected, value)) DeleteCurveCommand.RaiseCanExecuteChanged(); } }
    public RelayCommand AddCurveCommand { get; }
    public RelayCommand DeleteCurveCommand { get; }
    public bool TryApply(Material material, out Material updated, out string? error)
    {
        error = null;
        var curves = Curves.Select(c => c.Build()).ToFSharpList();
        var s = material.StrengthProperties;
        updated = new Material(material.Id, material.Name, material.ProductForm, material.NominalComposition, material.Specification, material.ASMESpecification, material.Grade, material.Class_Condition_Tempering, material.AlloyIdentification_UNS, material.Family, material.AllowableStressLevel, material.ApplicableAsmeCodes, material.AsmeNoteReferences, material.BasicProperties, material.PhysicalProperties, new StrengthProperties(s.AllowableStresses, s.AllowableStressDatasets, s.TensileProperties, s.CompressionProperties, s.StressStrainTables, s.CyclicStrainTables, s.ExternalPressureTables, s.NortonModels, s.GarofaloModels, s.KachanovOmegaModels, s.CreepTables, s.AverageCreepStrainRateStress, s.MinimumCreepStrainRateStress, s.StressRuptureCurves, s.AverageCreepRuptureStress, s.MinimumCreepRuptureStress, curves, s.FatigueCurves), material.SpecialProperties, material.MaximumAllowableTemperature, material.TimeDepenedingStartTemperature, material.WeldingInfo, material.CreatedDate, DateTime.UtcNow, material.Notes); return true;
    }
}

public sealed class LarsonItemViewModel : ObservableObject
{
    private string _material = "Material";
    private string _description = "Larson-Miller curve";
    public LarsonItemViewModel() { Points.Add(new LarsonPointViewModel()); }
    public LarsonItemViewModel(LarsonMillerCurve curve) { _material = curve.Material; _description = curve.Description; foreach (var point in curve.Points.ToReadOnlyList()) Points.Add(new LarsonPointViewModel(point.LarsonMillerParameter, point.Stress)); }
    public string Material { get => _material; set => SetProperty(ref _material, value); }
    public string Description { get => _description; set => SetProperty(ref _description, value); }
    public ObservableCollection<LarsonPointViewModel> Points { get; } = [];
    public LarsonMillerCurve Build() => new(Material, Description, Points.Select(p => new LarsonMillerPoint(p.Parameter, p.Stress)).ToFSharpList());
}

public sealed class LarsonPointViewModel : ObservableObject
{
    private double _parameter;
    private double _stress;
    public LarsonPointViewModel() { }
    public LarsonPointViewModel(double parameter, double stress) { _parameter = parameter; _stress = stress; }
    public double Parameter { get => _parameter; set => SetProperty(ref _parameter, value); }
    public double Stress { get => _stress; set => SetProperty(ref _stress, value); }
}
