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

    /// <summary>Creates the editor over a material's stress-strain tables.</summary>
    /// <param name="material">Material to read the tables from; never mutated.</param>
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

    /// <summary>Tables being edited, one entry per stored table.</summary>
    public ObservableCollection<StressStrainTableViewModel> Tables { get; } = [];

    /// <summary>Table whose points are shown in the detail pane.</summary>
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

    /// <summary>Point selected within the current table.</summary>
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

    /// <summary>Whether a table is selected, used to enable the detail pane.</summary>
    public bool HasSelectedTable => SelectedTable is not null;

    /// <summary>Validation or progress message shown under the editor.</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

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
                   updated.StrengthProperties.SyTable,
                   updated.StrengthProperties.SuTable,
                   updated.StrengthProperties.AllowableStressDatasets,
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

/// <summary>
/// One editable stress-strain table: its conditions and basis metadata plus its curve points.
/// </summary>
/// <remarks>
/// Numeric fields are held as text so a partially typed value does not fail binding; they are
/// parsed once in <see cref="TryBuild"/> using the invariant culture.
/// </remarks>
public sealed class StressStrainTableViewModel : ObservableObject
{
    private string _temperature = "20";
    private string? _durationHours;
    private string _strainBasis = "Engineering";
    private string _stressBasis = "Engineering";
    private string? _yieldStress;
    private string? _ultimateStress;
    private string _description = "User stress-strain";

    /// <summary>Creates a new table with default metadata.</summary>
    public StressStrainTableViewModel()
    {
        Points.Add(new StressStrainPointViewModel("0", "0"));
        Points.Add(new StressStrainPointViewModel("0.2", "200"));
    }

    /// <summary>Creates an editable copy of an existing table.</summary>
    /// <param name="table">Table to mirror.</param>
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

    /// <summary>Curve points of this table.</summary>
    public ObservableCollection<StressStrainPointViewModel> Points { get; } = [];
    /// <summary>Selectable values for the strain and stress basis dropdowns (engineering or true).</summary>
    public IReadOnlyList<string> BasisOptions { get; } = ["Engineering", "True"];

    /// <summary>Temperature the table applies at (degC), as entered text.</summary>
    public string Temperature
    {
        get => _temperature;
        set
        {
            if (SetProperty(ref _temperature, value)) RaisePropertyChanged(nameof(DisplayName));
        }
    }

    /// <summary>Reference duration (hours) for an isochronous table, blank when time-independent.</summary>
    public string? DurationHours
    {
        get => _durationHours;
        set
        {
            if (SetProperty(ref _durationHours, value)) RaisePropertyChanged(nameof(DisplayName));
        }
    }

    /// <summary>Whether strain values are engineering or true strain.</summary>
    public string StrainBasis
    {
        get => _strainBasis;
        set => SetProperty(ref _strainBasis, value);
    }

    /// <summary>Whether stress values are engineering or true stress.</summary>
    public string StressBasis
    {
        get => _stressBasis;
        set => SetProperty(ref _stressBasis, value);
    }

    /// <summary>Optional yield stress recorded with the table (MPa), as entered text.</summary>
    public string? YieldStress
    {
        get => _yieldStress;
        set => SetProperty(ref _yieldStress, value);
    }

    /// <summary>Optional ultimate stress recorded with the table (MPa), as entered text.</summary>
    public string? UltimateStress
    {
        get => _ultimateStress;
        set => SetProperty(ref _ultimateStress, value);
    }

    /// <summary>Free-text description of the table.</summary>
    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    /// <summary>Label shown in the table list.</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(DurationHours)
            ? $"{Temperature} degC"
            : $"{Temperature} degC / {DurationHours} h";

    /// <summary>Validates the buffer and converts it into the immutable domain record.</summary>
    /// <param name="table">Receives the built table on success; <c>null</c> on failure.</param>
    /// <param name="error">Receives a user-facing validation message on failure.</param>
    /// <returns><c>true</c> when the table was valid.</returns>
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

/// <summary>One editable point of a stress-strain curve, held as text.</summary>
/// <param name="strain">Strain value, as entered text.</param>
/// <param name="stress">Stress value (MPa), as entered text.</param>
public sealed class StressStrainPointViewModel(string strain = "", string stress = "") : ObservableObject
{
    private string _strain = strain;
    private string _stress = stress;

    /// <summary>Strain at this point, as entered text (dimensionless).</summary>
    public string Strain
    {
        get => _strain;
        set => SetProperty(ref _strain, value);
    }

    /// <summary>Stress at this point, as entered text (MPa).</summary>
    public string Stress
    {
        get => _stress;
        set => SetProperty(ref _stress, value);
    }
}
