using System.Collections.ObjectModel;
using MaterialLibrary.Domain;
using MaterialLibraryCrudApp.Interop;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>Validated master/detail editor model for stress-versus-rupture-time curves.</summary>
public sealed class StressRuptureTableEditorViewModel : ObservableObject
{
    private StressRuptureItemViewModel? _selectedTable;
    private StressRupturePointViewModel? _selectedPoint;
    public StressRuptureTableEditorViewModel(Material material)
    {
        foreach (var table in material.StrengthProperties.StressRuptureCurves.ToReadOnlyList()) Tables.Add(new StressRuptureItemViewModel(table));
        AddTableCommand = new RelayCommand(() => { var item = new StressRuptureItemViewModel(); Tables.Add(item); SelectedTable = item; });
        DeleteTableCommand = new RelayCommand(() => { if (SelectedTable is null) return; Tables.Remove(SelectedTable); SelectedTable = Tables.FirstOrDefault(); }, () => SelectedTable is not null);
        AddPointCommand = new RelayCommand(() => { if (SelectedTable is null) return; var point = new StressRupturePointViewModel(); SelectedTable.Points.Add(point); SelectedPoint = point; }, () => SelectedTable is not null);
        DeletePointCommand = new RelayCommand(() => { if (SelectedTable is null || SelectedPoint is null) return; SelectedTable.Points.Remove(SelectedPoint); SelectedPoint = null; }, () => SelectedPoint is not null);
        SelectedTable = Tables.FirstOrDefault();
    }
    public ObservableCollection<StressRuptureItemViewModel> Tables { get; } = [];
    public StressRuptureItemViewModel? SelectedTable { get => _selectedTable; set { if (SetProperty(ref _selectedTable, value)) { SelectedPoint = null; DeleteTableCommand.RaiseCanExecuteChanged(); AddPointCommand.RaiseCanExecuteChanged(); } } }
    public StressRupturePointViewModel? SelectedPoint { get => _selectedPoint; set { if (SetProperty(ref _selectedPoint, value)) DeletePointCommand.RaiseCanExecuteChanged(); } }
    public RelayCommand AddTableCommand { get; }
    public RelayCommand DeleteTableCommand { get; }
    public RelayCommand AddPointCommand { get; }
    public RelayCommand DeletePointCommand { get; }
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

public sealed class StressRuptureItemViewModel : ObservableObject
{
    private double _temperature;
    public StressRuptureItemViewModel() { Points.Add(new StressRupturePointViewModel()); }
    public StressRuptureItemViewModel(StressRuptureTable table) { _temperature = table.ReferenceTemperature; var column = table.Table.Columns.ToReadOnlyList().FirstOrDefault(); if (column is not null) foreach (var entry in column.Entries.ToReadOnlyList()) Points.Add(new StressRupturePointViewModel(entry.X, entry.Value)); }
    public double Temperature { get => _temperature; set { if (SetProperty(ref _temperature, value)) RaisePropertyChanged(nameof(DisplayName)); } }
    public string DisplayName => $"{Temperature:G5} C";
    public ObservableCollection<StressRupturePointViewModel> Points { get; } = [];
    public bool TryBuild(out StressRuptureTable? table, out string? error)
    {
        var entries = Points.Select(p => new TableColumnEntry(p.TimeHours, p.StressMpa)).ToFSharpList();
        var result = PropertyTableModule.create1D("Stress rupture", "Time", "h", "Stress", "MPa", XBoundaryPolicy.FlatExtrapolate, entries);
        if (!result.TryUnwrap(out var propertyTable, out var domainError)) { table = null; error = MaterialErrorFormat.Format(domainError); return false; }
        table = StressRuptureTableModule.create(propertyTable, Temperature); error = null; return true;
    }
}

public sealed class StressRupturePointViewModel : ObservableObject
{
    private double _timeHours;
    private double _stressMpa;
    public StressRupturePointViewModel() { }
    public StressRupturePointViewModel(double time, double stress) { _timeHours = time; _stressMpa = stress; }
    public double TimeHours { get => _timeHours; set => SetProperty(ref _timeHours, value); }
    public double StressMpa { get => _stressMpa; set => SetProperty(ref _stressMpa, value); }
}
