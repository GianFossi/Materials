using System.Collections.ObjectModel;
using MaterialLibrary.Domain;
using MaterialLibraryCrudApp.Interop;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>Master/detail editor for fatigue S-N curves.</summary>
public sealed class FatigueTableEditorViewModel : ObservableObject
{
    private FatigueItemViewModel? _selectedTable;
    private FatiguePointViewModel? _selectedPoint;
    public FatigueTableEditorViewModel(Material material)
    {
        foreach (var table in material.StrengthProperties.FatigueCurves.ToReadOnlyList()) Tables.Add(new FatigueItemViewModel(table));
        AddTableCommand = new RelayCommand(() => { var item = new FatigueItemViewModel(); Tables.Add(item); SelectedTable = item; });
        DeleteTableCommand = new RelayCommand(() => { if (SelectedTable is null) return; Tables.Remove(SelectedTable); SelectedTable = Tables.FirstOrDefault(); }, () => SelectedTable is not null);
        AddPointCommand = new RelayCommand(() => { if (SelectedTable is null) return; var point = new FatiguePointViewModel(); SelectedTable.Points.Add(point); SelectedPoint = point; }, () => SelectedTable is not null);
        DeletePointCommand = new RelayCommand(() => { if (SelectedTable is null || SelectedPoint is null) return; SelectedTable.Points.Remove(SelectedPoint); SelectedPoint = null; }, () => SelectedPoint is not null);
        SelectedTable = Tables.FirstOrDefault();
    }
    public ObservableCollection<FatigueItemViewModel> Tables { get; } = [];
    public FatigueItemViewModel? SelectedTable { get => _selectedTable; set { if (SetProperty(ref _selectedTable, value)) { SelectedPoint = null; DeleteTableCommand.RaiseCanExecuteChanged(); AddPointCommand.RaiseCanExecuteChanged(); } } }
    public FatiguePointViewModel? SelectedPoint { get => _selectedPoint; set { if (SetProperty(ref _selectedPoint, value)) DeletePointCommand.RaiseCanExecuteChanged(); } }
    public RelayCommand AddTableCommand { get; }
    public RelayCommand DeleteTableCommand { get; }
    public RelayCommand AddPointCommand { get; }
    public RelayCommand DeletePointCommand { get; }
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

public sealed class FatigueItemViewModel : ObservableObject
{
    private double _temperature;
    private double? _duration;
    public FatigueItemViewModel() { Points.Add(new FatiguePointViewModel()); }
    public FatigueItemViewModel(FatigueTable table) { _temperature = table.ReferenceTemperature; _duration = table.ReferenceDurationHours.AsNullable(); var column = table.Table.Columns.ToReadOnlyList().FirstOrDefault(); if (column is not null) foreach (var entry in column.Entries.ToReadOnlyList()) Points.Add(new FatiguePointViewModel(entry.X, entry.Value)); }
    public double Temperature { get => _temperature; set { if (SetProperty(ref _temperature, value)) RaisePropertyChanged(nameof(DisplayName)); } }
    public double? ReferenceDurationHours { get => _duration; set => SetProperty(ref _duration, value); }
    public string DisplayName => $"{Temperature:G5} C";
    public ObservableCollection<FatiguePointViewModel> Points { get; } = [];
    public bool TryBuild(out FatigueTable? table, out string? error)
    {
        var result = PropertyTableModule.create1D("Fatigue", "Cycles", "", "Stress amplitude", "MPa", XBoundaryPolicy.FlatExtrapolate, Points.Select(p => new TableColumnEntry(p.Cycles, p.StressMpa)).ToFSharpList());
        if (!result.TryUnwrap(out var propertyTable, out var domainError)) { table = null; error = MaterialErrorFormat.Format(domainError); return false; }
        table = FatigueTableModule.create(propertyTable, Temperature, FSharpInterop.ToOption(ReferenceDurationHours)); error = null; return true;
    }
}

public sealed class FatiguePointViewModel : ObservableObject
{
    private double _cycles;
    private double _stressMpa;
    public FatiguePointViewModel() { }
    public FatiguePointViewModel(double cycles, double stress) { _cycles = cycles; _stressMpa = stress; }
    public double Cycles { get => _cycles; set => SetProperty(ref _cycles, value); }
    public double StressMpa { get => _stressMpa; set => SetProperty(ref _stressMpa, value); }
}
