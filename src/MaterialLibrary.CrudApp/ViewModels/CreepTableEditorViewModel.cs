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

    public ObservableCollection<CreepTableItemViewModel> Tables { get; } = [];
    public CreepTableItemViewModel? SelectedTable { get => _selectedTable; set { if (SetProperty(ref _selectedTable, value)) { SelectedPoint = null; DeleteTableCommand.RaiseCanExecuteChanged(); AddPointCommand.RaiseCanExecuteChanged(); } } }
    public CreepPointItemViewModel? SelectedPoint { get => _selectedPoint; set { if (SetProperty(ref _selectedPoint, value)) DeletePointCommand.RaiseCanExecuteChanged(); } }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public RelayCommand AddTableCommand { get; }
    public RelayCommand DeleteTableCommand { get; }
    public RelayCommand AddPointCommand { get; }
    public RelayCommand DeletePointCommand { get; }

    public bool TryApply(Material material, out Material updated, out string? error)
    {
        var built = new List<CreepTable>();
        for (var i = 0; i < Tables.Count; i++)
        {
            if (!Tables[i].TryBuild(out var table, out error)) { updated = material; error = $"creep table {i + 1}: {error}"; return false; }
            built.Add(table!);
        }
        updated = new Material(material.Id, material.Name, material.ProductForm, material.NominalComposition, material.Specification, material.ASMESpecification, material.Grade, material.Class_Condition_Tempering, material.AlloyIdentification_UNS, material.Family, material.AllowableStressLevel, material.ApplicableAsmeCodes, material.AsmeNoteReferences, material.BasicProperties, material.PhysicalProperties,
            new StrengthProperties(material.StrengthProperties.AllowableStresses, material.StrengthProperties.AllowableStressDatasets, material.StrengthProperties.TensileProperties, material.StrengthProperties.CompressionProperties, material.StrengthProperties.StressStrainTables, material.StrengthProperties.CyclicStrainTables, material.StrengthProperties.ExternalPressureTables, material.StrengthProperties.NortonModels, material.StrengthProperties.GarofaloModels, material.StrengthProperties.KachanovOmegaModels, built.ToFSharpList(), material.StrengthProperties.AverageCreepStrainRateStress, material.StrengthProperties.MinimumCreepStrainRateStress, material.StrengthProperties.StressRuptureCurves, material.StrengthProperties.AverageCreepRuptureStress, material.StrengthProperties.MinimumCreepRuptureStress, material.StrengthProperties.LarsonMillerCurves, material.StrengthProperties.FatigueCurves), material.SpecialProperties, material.MaximumAllowableTemperature, material.TimeDepenedingStartTemperature, material.WeldingInfo, material.CreatedDate, DateTime.UtcNow, material.Notes);
        error = null; StatusMessage = $"Saved {built.Count} creep table(s)."; return true;
    }

    private void AddTable() { var item = new CreepTableItemViewModel(); Tables.Add(item); SelectedTable = item; }
    private void DeleteTable() { if (SelectedTable is null) return; var i = Tables.IndexOf(SelectedTable); Tables.Remove(SelectedTable); SelectedTable = Tables.Count == 0 ? null : Tables[Math.Clamp(i, 0, Tables.Count - 1)]; }
    private void AddPoint() { if (SelectedTable is null) return; var point = new CreepPointItemViewModel(); SelectedTable.Points.Add(point); SelectedPoint = point; }
    private void DeletePoint() { if (SelectedTable is null || SelectedPoint is null) return; SelectedTable.Points.Remove(SelectedPoint); SelectedPoint = null; }
}

public sealed class CreepTableItemViewModel : ObservableObject
{
    private double _temperature;
    private double _stress;
    private string _description = "Creep curve";
    public CreepTableItemViewModel() { Points.Add(new CreepPointItemViewModel()); }
    public CreepTableItemViewModel(CreepTable table)
    {
        _temperature = table.ReferenceTemperature; _stress = table.AppliedStress.AsNullable() ?? 0; _description = table.Table.Name;
        var column = table.Table.Columns.ToReadOnlyList().FirstOrDefault();
        if (column is not null) foreach (var entry in column.Entries.ToReadOnlyList()) Points.Add(new CreepPointItemViewModel(entry.X, entry.Value));
    }
    public double Temperature { get => _temperature; set => SetProperty(ref _temperature, value); }
    public double AppliedStress { get => _stress; set => SetProperty(ref _stress, value); }
    public string Description { get => _description; set => SetProperty(ref _description, value); }
    public ObservableCollection<CreepPointItemViewModel> Points { get; } = [];
    public string DisplayName => $"{Temperature.ToString("G5", CultureInfo.InvariantCulture)} C / {AppliedStress.ToString("G5", CultureInfo.InvariantCulture)} MPa";
    public bool TryBuild(out CreepTable? table, out string? error)
    {
        var points = Points.Select(p => new CreepPoint(p.Time, p.Strain)).ToFSharpList();
        var result = CreepTableBuilder.create(Temperature, AppliedStress, Description, points);
        if (result.TryUnwrap(out table, out var domainError)) { error = null; return true; }
        error = MaterialErrorFormat.Format(domainError); table = null; return false;
    }
}

public sealed class CreepPointItemViewModel : ObservableObject
{
    private double _time;
    private double _strain;
    public CreepPointItemViewModel() { }
    public CreepPointItemViewModel(double time, double strain) { _time = time; _strain = strain; }
    public double Time { get => _time; set => SetProperty(ref _time, value); }
    public double Strain { get => _strain; set => SetProperty(ref _strain, value); }
}
