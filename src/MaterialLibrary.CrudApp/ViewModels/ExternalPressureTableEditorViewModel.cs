using System.Collections.ObjectModel;
using MaterialLibrary.Domain;
using MaterialLibraryCrudApp.Interop;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>Validated editor model for external-pressure Factor A/Factor B curves.</summary>
public sealed class ExternalPressureTableEditorViewModel : ObservableObject
{
    private ExternalPressureItemViewModel? _selected;
    public ExternalPressureTableEditorViewModel(Material material)
    {
        foreach (var table in material.StrengthProperties.ExternalPressureTables.ToReadOnlyList()) Tables.Add(new ExternalPressureItemViewModel(table));
        AddTableCommand = new RelayCommand(() => { var item = new ExternalPressureItemViewModel(); Tables.Add(item); Selected = item; });
        DeleteTableCommand = new RelayCommand(() => { if (Selected is null) return; Tables.Remove(Selected); Selected = Tables.FirstOrDefault(); }, () => Selected is not null);
        Selected = Tables.FirstOrDefault();
    }
    public ObservableCollection<ExternalPressureItemViewModel> Tables { get; } = [];
    public ExternalPressureItemViewModel? Selected { get => _selected; set { if (SetProperty(ref _selected, value)) DeleteTableCommand.RaiseCanExecuteChanged(); } }
    public RelayCommand AddTableCommand { get; }
    public RelayCommand DeleteTableCommand { get; }
    public bool TryApply(Material material, out Material updated, out string? error)
    {
        error = null;
        var built = new List<ExternalPressureTable>();
        foreach (var item in Tables) { if (!item.TryBuild(out var table, out error)) { updated = material; return false; } built.Add(table!); }
        var s = material.StrengthProperties;
        updated = new Material(material.Id, material.Name, material.ProductForm, material.NominalComposition, material.Specification, material.ASMESpecification, material.Grade, material.Class_Condition_Tempering, material.AlloyIdentification_UNS, material.Family, material.AllowableStressLevel, material.ApplicableAsmeCodes, material.AsmeNoteReferences, material.BasicProperties, material.PhysicalProperties, new StrengthProperties(s.AllowableStresses, s.AllowableStressDatasets, s.TensileProperties, s.CompressionProperties, s.StressStrainTables, s.CyclicStrainTables, built.ToFSharpList(), s.NortonModels, s.GarofaloModels, s.KachanovOmegaModels, s.CreepTables, s.AverageCreepStrainRateStress, s.MinimumCreepStrainRateStress, s.StressRuptureCurves, s.AverageCreepRuptureStress, s.MinimumCreepRuptureStress, s.LarsonMillerCurves, s.FatigueCurves), material.SpecialProperties, material.MaximumAllowableTemperature, material.TimeDepenedingStartTemperature, material.WeldingInfo, material.CreatedDate, DateTime.UtcNow, material.Notes); return true;
    }
}

public sealed class ExternalPressureItemViewModel : ObservableObject
{
    private double _temperature;
    private double? _duration;
    private double? _reduction;
    public ExternalPressureItemViewModel() { Points.Add(new ExternalPressurePointViewModel()); }
    public ExternalPressureItemViewModel(ExternalPressureTable table) { _temperature = table.ReferenceTemperature; _duration = table.ReferenceDurationHours.AsNullable(); _reduction = table.ReductionFactor.AsNullable(); var column = table.Table.Columns.ToReadOnlyList().FirstOrDefault(); if (column is not null) foreach (var e in column.Entries.ToReadOnlyList()) Points.Add(new ExternalPressurePointViewModel(e.X, e.Value)); }
    public double Temperature { get => _temperature; set => SetProperty(ref _temperature, value); }
    public double? DurationHours { get => _duration; set => SetProperty(ref _duration, value); }
    public double? ReductionFactor { get => _reduction; set => SetProperty(ref _reduction, value); }
    public ObservableCollection<ExternalPressurePointViewModel> Points { get; } = [];
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

public sealed class ExternalPressurePointViewModel : ObservableObject
{
    private double _factorA;
    private double _compressiveStress;
    public ExternalPressurePointViewModel() { }
    public ExternalPressurePointViewModel(double a, double b) { _factorA = a; _compressiveStress = b; }
    public double FactorA { get => _factorA; set => SetProperty(ref _factorA, value); }
    public double CompressiveStress { get => _compressiveStress; set => SetProperty(ref _compressiveStress, value); }
}
