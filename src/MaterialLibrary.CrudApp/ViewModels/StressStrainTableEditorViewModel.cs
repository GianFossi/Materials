using System.Collections.ObjectModel;
using System.Globalization;
using MaterialLibrary.Domain;
using MaterialLibraryCrudApp.Interop;
using Microsoft.FSharp.Core;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>Master/detail editor for a material's stress-strain tables.</summary>
public sealed class StressStrainTableEditorViewModel : ObservableObject
{
    private StressStrainTableViewModel? _selectedTable;
    private StressStrainPointViewModel? _selectedPoint;
    private string _statusMessage = string.Empty;

    public StressStrainTableEditorViewModel(Material material)
    {
        ArgumentNullException.ThrowIfNull(material);

        foreach (var table in material.StrengthProperties.StressStrainTables.ToReadOnlyList())
        {
            Tables.Add(new StressStrainTableViewModel(table));
        }

        AddTableCommand = new RelayCommand(AddTable);
        DeleteTableCommand = new RelayCommand(DeleteTable, () => SelectedTable is not null);
        AddPointCommand = new RelayCommand(AddPoint, () => SelectedTable is not null);
        DeletePointCommand = new RelayCommand(DeletePoint, () => SelectedPoint is not null);

        SelectedTable = Tables.FirstOrDefault();
        StatusMessage = $"{Tables.Count} stress-strain table(s).";
    }

    public ObservableCollection<StressStrainTableViewModel> Tables { get; } = [];

    public StressStrainTableViewModel? SelectedTable
    {
        get => _selectedTable;
        set
        {
            if (SetProperty(ref _selectedTable, value))
            {
                SelectedPoint = null;
                DeleteTableCommand.RaiseCanExecuteChanged();
                AddPointCommand.RaiseCanExecuteChanged();
                RaisePropertyChanged(nameof(HasSelectedTable));
            }
        }
    }

    public StressStrainPointViewModel? SelectedPoint
    {
        get => _selectedPoint;
        set
        {
            if (SetProperty(ref _selectedPoint, value))
            {
                DeletePointCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasSelectedTable => SelectedTable is not null;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public RelayCommand AddTableCommand { get; }
    public RelayCommand DeleteTableCommand { get; }
    public RelayCommand AddPointCommand { get; }
    public RelayCommand DeletePointCommand { get; }

    public bool TryApply(Material material, out Material updated, out string? error)
    {
        var built = new List<StressStrainTable>();

        for (var index = 0; index < Tables.Count; index++)
        {
            if (!Tables[index].TryBuild(out var table, out error))
            {
                updated = material;
                error = $"stress-strain table {index + 1}: {error}";
                StatusMessage = error;
                return false;
            }

            built.Add(table!);
        }

        updated = material;
        foreach (var table in built)
        {
            if (!StressStrainTableBuilder.addOrReplaceTable(table, updated).TryUnwrap(out updated, out var materialError))
            {
                error = MaterialErrorFormat.Format(materialError);
                StatusMessage = error;
                return false;
            }
        }

        updated =
            new Material(
                updated.Id,
                updated.Name,
                updated.ProductForm,
                updated.NominalComposition,
                updated.Specification,
                updated.ASMESpecification,
                updated.Grade,
                updated.Class_Condition_Tempering,
                updated.AlloyIdentification_UNS,
                updated.Family,
                updated.AllowableStressLevel,
                updated.ApplicableAsmeCodes,
                updated.AsmeNoteReferences,
                updated.BasicProperties,
                updated.PhysicalProperties,
                new StrengthProperties(
                    updated.StrengthProperties.AllowableStresses,
                    updated.StrengthProperties.AllowableStressDatasets,
                    updated.StrengthProperties.TensileProperties,
                    updated.StrengthProperties.CompressionProperties,
                     built.ToFSharpList(),
                    updated.StrengthProperties.CyclicStrainTables,
                    updated.StrengthProperties.ExternalPressureTables,
                    updated.StrengthProperties.NortonModels,
                    updated.StrengthProperties.GarofaloModels,
                    updated.StrengthProperties.KachanovOmegaModels,
                    updated.StrengthProperties.CreepTables,
                    updated.StrengthProperties.AverageCreepStrainRateStress,
                    updated.StrengthProperties.MinimumCreepStrainRateStress,
                    updated.StrengthProperties.StressRuptureCurves,
                    updated.StrengthProperties.AverageCreepRuptureStress,
                    updated.StrengthProperties.MinimumCreepRuptureStress,
                    updated.StrengthProperties.LarsonMillerCurves,
                    updated.StrengthProperties.FatigueCurves),
                updated.SpecialProperties,
                updated.MaximumAllowableTemperature,
                updated.TimeDepenedingStartTemperature,
                updated.WeldingInfo,
                updated.CreatedDate,
                DateTime.UtcNow,
                updated.Notes);

        error = null;
        StatusMessage = $"Saved {built.Count} stress-strain table(s).";
        return true;
    }

    private void AddTable()
    {
        var table = new StressStrainTableViewModel();
        Tables.Add(table);
        SelectedTable = table;
        StatusMessage = $"{Tables.Count} stress-strain table(s).";
    }

    private void DeleteTable()
    {
        if (SelectedTable is null)
        {
            return;
        }

        var index = Tables.IndexOf(SelectedTable);
        Tables.Remove(SelectedTable);
        SelectedTable = Tables.Count == 0 ? null : Tables[Math.Clamp(index, 0, Tables.Count - 1)];
        StatusMessage = $"{Tables.Count} stress-strain table(s).";
    }

    private void AddPoint()
    {
        if (SelectedTable is null)
        {
            return;
        }

        var point = new StressStrainPointViewModel();
        SelectedTable.Points.Add(point);
        SelectedPoint = point;
        StatusMessage = $"{SelectedTable.DisplayName}: {SelectedTable.Points.Count} point(s).";
    }

    private void DeletePoint()
    {
        if (SelectedTable is null || SelectedPoint is null)
        {
            return;
        }

        SelectedTable.Points.Remove(SelectedPoint);
        SelectedPoint = null;
        StatusMessage = $"{SelectedTable.DisplayName}: {SelectedTable.Points.Count} point(s).";
    }

    private static string Num(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static string? Num(FSharpOption<double>? value) => value is null ? null : Num(value.Value);
}

public sealed class StressStrainTableViewModel : ObservableObject
{
    private string _temperature = "20";
    private string? _durationHours;
    private string _strainBasis = "Engineering";
    private string _stressBasis = "Engineering";
    private string? _yieldStress;
    private string? _ultimateStress;
    private string _description = "User stress-strain";

    public StressStrainTableViewModel()
    {
        Points.Add(new StressStrainPointViewModel("0", "0"));
        Points.Add(new StressStrainPointViewModel("0.2", "200"));
    }

    public StressStrainTableViewModel(StressStrainTable table)
    {
        Temperature = Num(table.ReferenceTemperature);
        DurationHours = Num(table.ReferenceDurationHours);
        StrainBasis = BasisName(table.StrainBasis);
        StressBasis = BasisName(table.StressBasis);
        YieldStress = Num(table.YieldStress);
        UltimateStress = Num(table.UltimateStress);
        Description = table.Table.Name.Replace("Stress-Strain - ", string.Empty, StringComparison.Ordinal);

        foreach (var entry in table.Table.Columns.Head.Entries.ToReadOnlyList())
        {
            Points.Add(new StressStrainPointViewModel(Num(entry.X), Num(entry.Value)));
        }
    }

    public ObservableCollection<StressStrainPointViewModel> Points { get; } = [];
    public IReadOnlyList<string> BasisOptions { get; } = ["Engineering", "True"];

    public string Temperature
    {
        get => _temperature;
        set
        {
            if (SetProperty(ref _temperature, value)) RaisePropertyChanged(nameof(DisplayName));
        }
    }

    public string? DurationHours
    {
        get => _durationHours;
        set
        {
            if (SetProperty(ref _durationHours, value)) RaisePropertyChanged(nameof(DisplayName));
        }
    }

    public string StrainBasis
    {
        get => _strainBasis;
        set => SetProperty(ref _strainBasis, value);
    }

    public string StressBasis
    {
        get => _stressBasis;
        set => SetProperty(ref _stressBasis, value);
    }

    public string? YieldStress
    {
        get => _yieldStress;
        set => SetProperty(ref _yieldStress, value);
    }

    public string? UltimateStress
    {
        get => _ultimateStress;
        set => SetProperty(ref _ultimateStress, value);
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public string DisplayName =>
        string.IsNullOrWhiteSpace(DurationHours)
            ? $"{Temperature} degC"
            : $"{Temperature} degC / {DurationHours} h";

    public bool TryBuild(out StressStrainTable? table, out string? error)
    {
        table = null;

        if (!TryParseRequired(Temperature, "temperature", out var temperature, out error)) return false;
        if (!TryParseOptional(DurationHours, "duration", out var duration, out error)) return false;
        if (!TryParseOptional(YieldStress, "yield stress", out var yieldStress, out error)) return false;
        if (!TryParseOptional(UltimateStress, "ultimate stress", out var ultimateStress, out error)) return false;

        var points = new List<StressStrainPoint>();
        for (var i = 0; i < Points.Count; i++)
        {
            if (!TryParseRequired(Points[i].Strain, $"point {i + 1} strain", out var strain, out error)) return false;
            if (!TryParseRequired(Points[i].Stress, $"point {i + 1} stress", out var stress, out error)) return false;
            points.Add(new StressStrainPoint(strain, stress));
        }

        var strainBasis = ParseBasis(StrainBasis);
        var stressBasis = ParseBasis(StressBasis);
        var result = duration.HasValue
            ? StressStrainTableBuilder.createIsochronous(
                temperature,
                duration.Value,
                strainBasis,
                stressBasis,
                Description,
                points.ToFSharpList(),
                FSharpInterop.ToOption(yieldStress),
                FSharpInterop.ToOption(ultimateStress))
            : StressStrainTableBuilder.createTimeIndependent(
                temperature,
                strainBasis,
                stressBasis,
                Description,
                points.ToFSharpList(),
                FSharpInterop.ToOption(yieldStress),
                FSharpInterop.ToOption(ultimateStress));

        if (!result.TryUnwrap(out table, out var materialError))
        {
            error = MaterialErrorFormat.Format(materialError);
            return false;
        }

        error = null;
        return true;
    }

    private static string Num(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static string? Num(FSharpOption<double>? value) => value is null ? null : Num(value.Value);
    private static string BasisName(int basis) => basis == 2 ? "True" : "Engineering";
    private static StressStrainBasis ParseBasis(string basis) => basis == "True" ? StressStrainBasis.True : StressStrainBasis.Engineering;

    private static bool TryParseRequired(string? text, string label, out double value, out string? error)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value))
        {
            error = null;
            return true;
        }

        error = $"{label} must be numeric.";
        return false;
    }

    private static bool TryParseOptional(string? text, string label, out double? value, out string? error)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = null;
            error = null;
            return true;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            && double.IsFinite(parsed))
        {
            value = parsed;
            error = null;
            return true;
        }

        value = null;
        error = $"{label} must be numeric or blank.";
        return false;
    }
}

public sealed class StressStrainPointViewModel(string strain = "", string stress = "") : ObservableObject
{
    private string _strain = strain;
    private string _stress = stress;

    public string Strain
    {
        get => _strain;
        set => SetProperty(ref _strain, value);
    }

    public string Stress
    {
        get => _stress;
        set => SetProperty(ref _stress, value);
    }
}
