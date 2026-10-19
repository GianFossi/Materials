using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using MaterialLibrary.Crud;
using MaterialLibrary.Domain.Database.Lookup;
using MaterialLibraryCrudApp.Interop;
using MaterialLibraryCrudApp.Services;
using Microsoft.Data.Sqlite;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>One editable point, in Celsius and the selected property's displayed unit.</summary>
public sealed class PropertyPointRow
{
    /// <summary>Temperature in Celsius; blank values must be completed before saving.</summary>
    public double? Temperature { get; set; }
    /// <summary>Property value in the selected family's fixed unit.</summary>
    public double? Value { get; set; }
}

/// <summary>One original ASME note row shown for a curve; edits are stored as a curve-level override.</summary>
public sealed class PropertyNoteRow
{
    /// <summary>ASME source table (for example 1A), or Manual for a row added by the user.</summary>
    public string SourceTable { get; set; } = "Manual";
    /// <summary>ASME note code (for example G5); blank for manual rows.</summary>
    public string Code { get; set; } = "";
    /// <summary>Note body.</summary>
    public string Text { get; set; } = "";
}

/// <summary>A reusable ASME source note offered for copying into a curve's user notes.</summary>
public sealed record CommonAsmeNote(string SourceTable, string Code, string Kind, string Text)
{
    /// <summary>Compact source label for the note-copy dropdown.</summary>
    public string Label
    {
        get
        {
            var preview = Regex.Replace(Text, @"\s+", " ").Trim();
            if (preview.Length > 90) preview = preview[..87] + "...";
            return $"{SourceTable} · {Code} ({Kind}) — {preview}";
        }
    }
}

/// <summary>A source variant identified independently of its temperature points.</summary>
public sealed record PropertyCurveChoice(Asme2025Curve Curve)
{
    /// <summary>Product form and curve ID shown as the primary variant label.</summary>
    public string Title => $"Curve #{Curve.Id} · {ProductFormLabel}{StressLevelLabel}";
    /// <summary>Dimensional bounds in millimetres and a readable axis label.</summary>
    public string SizeRangeLabel
    {
        get
        {
            var lower = Curve.SizeMinimum.HasValue
                ? $"{(Curve.MinimumIncluded ? "≥" : ">")} {Curve.SizeMinimum.Value.ToString("G", CultureInfo.CurrentCulture)} mm"
                : null;
            var upper = Curve.SizeMaximum.HasValue
                ? $"{(Curve.MaximumIncluded ? "≤" : "<")} {Curve.SizeMaximum.Value.ToString("G", CultureInfo.CurrentCulture)} mm"
                : null;
            var range = lower is null && upper is null
                ? "no bounds specified"
                : string.Join(" · ", new[] { lower, upper }.Where(bound => bound is not null));
            return $"Size: {range} · Axis: {AxisLabel}";
        }
    }
    /// <summary>Source table, number of points and, when available, the welding P-No./Group and the design-Section temperature limit (°C).</summary>
    public string SourceLabel
    {
        get
        {
            var label = $"Table {Curve.SourceTable} · {Curve.Points.Length} points";
            var limits = Curve.CodeLimits;
            if (limits is null) return label;
            if (!string.IsNullOrWhiteSpace(limits.PNumber))
                label += $" · P-No. {limits.PNumber}" + (string.IsNullOrWhiteSpace(limits.GroupNumber) ? "" : $" Gr. {limits.GroupNumber}");
            // S1/Sb1 belong to Section VIII-1; S2/Sb2 to Section VIII-2.
            var (section, limit) = Curve.Code is "S1" or "Sb1" ? ("VIII-1", limits.MaxTempSectionVIII1) : ("VIII-2", limits.MaxTempSectionVIII2);
            if (!string.IsNullOrWhiteSpace(limit))
                label += string.Equals(limit, "NP", StringComparison.OrdinalIgnoreCase) ? $" · {section}: not permitted" : $" · {section} max {limit} °C";
            return label;
        }
    }

    private string ProductFormLabel
    {
        get
        {
            var form = Curve.ProductForm?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(form)) return "form not specified";
            if (form.Contains("SMLS", StringComparison.OrdinalIgnoreCase) && (form.Contains("WLD", StringComparison.OrdinalIgnoreCase) || form.Contains("WELD", StringComparison.OrdinalIgnoreCase)))
                return $"Seamless / welded ({form})";
            if (form.StartsWith("SMLS", StringComparison.OrdinalIgnoreCase)
                || form.Contains("SEAMLESS", StringComparison.OrdinalIgnoreCase))
                return $"Seamless ({form})";
            if (form.Contains("WLD", StringComparison.OrdinalIgnoreCase)
                || form.Contains("WELDED", StringComparison.OrdinalIgnoreCase))
                return $"Welded ({form})";
            return form;
        }
    }

    private string StressLevelLabel => Curve.Code == "S1"
        ? Curve.S1StressLevel == "High" ? " · High Stress (G5)" : " · Normal Stress"
        : "";

    private string AxisLabel => Curve.SizeAxis switch
    {
        "thickness-or-unspecified" => "thickness / unspecified",
        "diameter" => "diameter",
        "OD" => "outside diameter (OD)",
        "DN" => "nominal diameter (DN)",
        _ => string.IsNullOrWhiteSpace(Curve.SizeAxis) ? "unspecified" : Curve.SizeAxis
    };
}

/// <summary>Edits native 2025 curves on a working copy, with optimistic concurrency and preserved source notes.</summary>
public sealed class Asme2025EditorViewModel : ObservableObject
{
    private readonly string _path;
    private readonly ApplicationOptions _options;
    private Asme2025Material? _material;
    private PropertyCurveChoice? _curve;
    private string _family = "Sy", _search = "", _status = "Select a material and property family.", _productForm = "", _s1StressLevel = "Normal";
    private CommonAsmeNote? _selectedCommonNote;
    private PropertyNoteRow? _selectedSourceNoteRow;
    private bool _allowSourceNoteEdit;
    private string _userNotesText = "";
    private bool _loading, _curveDirty, _constantsDirty, _identityDirty;
    private bool _hasBoltingCurves, _hasNonBoltingCurves;
    private readonly List<CommonAsmeNote> _noteCatalog = [];
    /// <summary>Creates an editor over an existing explicit working copy.</summary>
    public Asme2025EditorViewModel(string path, long? materialId = null, ApplicationOptions? options = null)
    {
        _path = path;
        _options = options ?? new ApplicationOptions();
        using var db = Asme2025Data.openRead(path);
        if (!Asme2025Data.isNormalized(db)) throw new InvalidOperationException("Open the ASME 2025 database.");
        foreach (var material in Asme2025Data.materials(db)) Materials.Add(material);
        MaterialView = CollectionViewSource.GetDefaultView(Materials);
        MaterialView.Filter = item => item is Asme2025Material m && Search.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(term => $"{m.Id} {m.Specification} {m.Grade} {m.Condition} {m.UNS}".Contains(term, StringComparison.OrdinalIgnoreCase));
        LoadCommonNoteCatalog();
        ImportXmlCommand = new RelayCommand(OpenXmlImport);
        SaveCurveCommand = new RelayCommand(SaveCurve, () => SelectedMaterial is not null);
        NewCurveCommand = new RelayCommand(NewCurve, () => SelectedMaterial is not null);
        DeleteCurveCommand = new RelayCommand(DeleteCurve, () => _curve is not null);
        AddNoteCommand = new RelayCommand(() => { var row = new PropertyNoteRow(); SourceNoteRows.Add(row); SelectedSourceNoteRow = row; }, () => AllowSourceNoteEdit);
        CopyCommonNoteCommand = new RelayCommand(CopyCommonNote, () => AllowSourceNoteEdit && SelectedCommonNote is not null);
        DeleteNoteCommand = new RelayCommand(DeleteSourceNote, () => AllowSourceNoteEdit && SelectedSourceNoteRow is not null);
        ReloadCommand = new RelayCommand(() => { if (AllowCurveChange()) ReloadCurves(); });
        SaveConstantsCommand = new RelayCommand(SaveConstants, () => SelectedMaterial is not null);
        SaveIdentityCommand = new RelayCommand(SaveIdentity);
        NewIdentityCommand = new RelayCommand(() => { if (AllowDiscard()) { SelectedMaterial = null; Specification = Grade = Condition = Uns = Composition = ""; RaiseIdentity(); } });
        SelectedMaterial = Materials.FirstOrDefault(m => m.Id == materialId) ?? Materials.FirstOrDefault();
    }
    /// <summary>All native MAIN identities, unique on specification/grade/condition/UNS.</summary>
    public ObservableCollection<Asme2025Material> Materials { get; } = [];
    /// <summary>Filtered material list.</summary>
    public ICollectionView MaterialView { get; }
    /// <summary>Substring terms matched across the four identity fields.</summary>
    public string Search { get => _search; set { if (SetProperty(ref _search, value)) MaterialView.Refresh(); } }
    /// <summary>Current selected identity.</summary>
    public Asme2025Material? SelectedMaterial
    {
        get => _material;
        set
        {
            if (value == _material) return;
            if (!AllowDiscard()) { RaisePropertyChanged(); return; }
            _loading = true; _material = value; RaisePropertyChanged();
            Specification = value?.Specification ?? ""; Grade = value?.Grade ?? ""; Condition = value?.Condition ?? ""; Uns = value?.UNS ?? ""; Composition = value?.NominalComposition ?? "";
            RefreshFamilyAvailability();
            RaiseIdentity(); ReloadCurves(); LoadConstants(); RefreshCommands(); _loading = false; _identityDirty = false;
        }
    }
    /// <summary>Families exposed without confusing ordinary stresses and the two bolting families.</summary>
    public IReadOnlyList<string> Families { get; } = Asme2025Data.families.Select(f => f.Item1).ToArray();
    /// <summary>Families applicable to the selected identity; bolting and non-bolting sources stay distinct.</summary>
    public IReadOnlyList<string> AvailableFamilies => Families.Where(family =>
        !(_hasBoltingCurves && !_hasNonBoltingCurves && (family is "S1" or "S2"))
        && !(_hasNonBoltingCurves && !_hasBoltingCurves && (family is "Sb1" or "Sb2"))).ToArray();
    /// <summary>Selected property family.</summary>
    public string Family { get => _family; set { if (value == _family) return; if (!AvailableFamilies.Contains(value) || !AllowCurveChange()) { RaisePropertyChanged(); return; } _family = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(Unit)); RaisePropertyChanged(nameof(CanEditForm)); RaisePropertyChanged(nameof(IsMechanical)); RaisePropertyChanged(nameof(ShowS1StressLevel)); RaisePropertyChanged(nameof(ShowJointEfficiency)); RaisePropertyChanged(nameof(ShowCodeLimits)); ReloadCurves(); } }
    /// <summary>Fixed property unit; temperatures are always Celsius.</summary>
    public string Unit => Asme2025Data.families.First(f => f.Item1 == Family).Item2;
    /// <summary>Product form applies only to S1 and S2.</summary>
    public bool CanEditForm => Family is "S1" or "S2";
    /// <summary>Mechanical properties may have size boundaries in mm.</summary>
    public bool IsMechanical => Family is "Sy" or "Su" or "S1" or "S2" or "Sb1" or "Sb2";
    /// <summary>Whether to show the explicit S1 standard/high-stress classification.</summary>
    public bool ShowS1StressLevel => Family == "S1";
    /// <summary>Whether the selected S1/S2 product form includes welded products.</summary>
    public bool ShowJointEfficiency => (Family is "S1" or "S2") && IsWelded(ProductForm);
    /// <summary>Joint-efficiency guidance from the selected curve's ASME notes, with a clearly marked typical fallback.</summary>
    public string JointEfficiencySummary
    {
        get
        {
            var notes = _curve?.Curve.SourceNotes ?? "";
            var factors = Regex.Matches(notes, @"joint\s+efficiency\s+factor(?:\s+of)?\s+([0-9]+(?:\.[0-9]+)?)", RegexOptions.IgnoreCase)
                .Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray();
            return factors.Length > 0
                ? $"The ASME source notes specify joint-efficiency factor(s): {string.Join(", ", factors)}. Check the original notes for temperature or application limits. The configured default for the longitudinal or pressure-critical joint ({_options.DefaultJointEfficiency:0.##}) is reference only and must not override applicable source requirements."
                : $"Configured default efficiency for the longitudinal or pressure-critical joint: {_options.DefaultJointEfficiency:0.##}. No explicit factor was found in this curve's notes. This is a reference only; verify the applicable ASME notes and design conditions.";
        }
    }
    /// <summary>Every separately selectable curve in the current family.</summary>
    public ObservableCollection<PropertyCurveChoice> Curves { get; } = [];
    /// <summary>Selected variant; source-note text remains read-only.</summary>
    public PropertyCurveChoice? SelectedCurve { get => _curve; set { if (value == _curve) return; if (!AllowCurveChange()) { RaisePropertyChanged(); return; } _curve = value; RaisePropertyChanged(); LoadCurve(); } }
    /// <summary>Editable temperature/value rows.</summary>
    public ObservableCollection<PropertyPointRow> Points { get; } = [];
    /// <summary>Original ASME note rows for the selected curve: the official rows, or the saved curve-level override.</summary>
    public ObservableCollection<PropertyNoteRow> SourceNoteRows { get; } = [];
    /// <summary>Selected original-note row, the target of the Delete command.</summary>
    public PropertyNoteRow? SelectedSourceNoteRow
    {
        get => _selectedSourceNoteRow;
        set { if (SetProperty(ref _selectedSourceNoteRow, value)) DeleteNoteCommand?.RaiseCanExecuteChanged(); }
    }
    /// <summary>Unlocks the original-note grid and its Add/Copy/Delete commands; this UI toggle is not saved.</summary>
    public bool AllowSourceNoteEdit
    {
        get => _allowSourceNoteEdit;
        set
        {
            if (!SetProperty(ref _allowSourceNoteEdit, value)) return;
            RaisePropertyChanged(nameof(SourceNotesReadOnly));
            AddNoteCommand?.RaiseCanExecuteChanged(); CopyCommonNoteCommand?.RaiseCanExecuteChanged(); DeleteNoteCommand?.RaiseCanExecuteChanged();
        }
    }
    /// <summary>Inverse of <see cref="AllowSourceNoteEdit"/> for the grid's read-only binding.</summary>
    public bool SourceNotesReadOnly => !AllowSourceNoteEdit;
    /// <summary>Whether the curve currently shows a saved curve-level override instead of the official notes.</summary>
    public bool HasSourceNoteOverride => _curve?.Curve.UserNoteItems is { Length: > 0 };
    /// <summary>Explains whether the grid shows the official notes or an edited curve-level copy.</summary>
    public string SourceNoteOrigin => HasSourceNoteOverride
        ? "Showing this curve's edited copy of the original ASME notes. The official ASME note tables are unchanged."
        : "Showing the official ASME notes linked to this curve. Check \"Allow edit\" to change them for this curve only.";
    /// <summary>Free-text user notes for the selected curve.</summary>
    public string UserNotesText { get => _userNotesText; set => SetProperty(ref _userNotesText, value ?? ""); }
    /// <summary>ASME notes relevant to the current table, available for copying.</summary>
    public ObservableCollection<CommonAsmeNote> CommonNotes { get; } = [];
    /// <summary>Selected ASME note in the dropdown, copied into the original-note rows by the Copy command.</summary>
    public CommonAsmeNote? SelectedCommonNote
    {
        get => _selectedCommonNote;
        set { if (SetProperty(ref _selectedCommonNote, value)) CopyCommonNoteCommand?.RaiseCanExecuteChanged(); }
    }
    /// <summary>Product form for S1/S2.</summary>
    public string ProductForm
    {
        get => _productForm;
        set
        {
            if (_productForm == value) return;
            _productForm = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(ProductFormChoices));
            RaisePropertyChanged(nameof(ShowJointEfficiency));
        }
    }
    /// <summary>Configured product-form choices plus the selected value if it is not yet in options.</summary>
    public IReadOnlyList<string> ProductFormChoices =>
        _options.ProductForms
            .Concat(string.IsNullOrWhiteSpace(ProductForm) ? [] : [ProductForm])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    /// <summary>Explicit S1 designation: Normal Stress or the higher G5 alternative.</summary>
    public string S1StressLevel
    {
        get => _s1StressLevel;
        set { if (SetProperty(ref _s1StressLevel, value)) RaisePropertyChanged(nameof(SelectedCurve)); }
    }
    /// <summary>Lower size boundary in mm; blank means unbounded.</summary>
    public double? SizeMinimum { get; set; }
    /// <summary>Upper size boundary in mm; blank means unbounded.</summary>
    public double? SizeMaximum { get; set; }
    /// <summary>Whether the lower boundary is included.</summary>
    public bool MinimumIncluded { get; set; }
    /// <summary>Whether the upper boundary is included.</summary>
    public bool MaximumIncluded { get; set; }
    /// <summary>Dimension axis, retained independently of source text.</summary>
    public string SizeAxis { get; set; } = "thickness-or-unspecified";
    /// <summary>Full general and specific ASME note texts for the source variant.</summary>
    public string SourceNotes => _curve?.Curve.SourceNotes ?? "";
    /// <summary>P-No., Group No. and Section temperature limits apply to the allowable-stress families only.</summary>
    public bool ShowCodeLimits => Family is "S1" or "S2" or "Sb1" or "Sb2";
    /// <summary>Welding P-Number (ASME Section IX) of this curve's source row.</summary>
    public string PNumber { get; set; } = "";
    /// <summary>Welding Group Number within the P-Number.</summary>
    public string GroupNumber { get; set; } = "";
    /// <summary>External-pressure chart designation, for example CS–2.</summary>
    public string ExternalPressureChart { get; set; } = "";
    /// <summary>Section I maximum temperature limit, °C as ASME text (number, NP, or blank).</summary>
    public string MaxTempSectionI { get; set; } = "";
    /// <summary>Section III maximum temperature limit, °C as ASME text; may include a qualifier such as "(Cl. 3 only)".</summary>
    public string MaxTempSectionIII { get; set; } = "";
    /// <summary>Section VIII Division 1 maximum temperature limit, °C as ASME text.</summary>
    public string MaxTempSectionVIII1 { get; set; } = "";
    /// <summary>Section VIII Division 2 maximum temperature limit, °C as ASME text.</summary>
    public string MaxTempSectionVIII2 { get; set; } = "";
    /// <summary>Section XII maximum temperature limit, °C as ASME text.</summary>
    public string MaxTempSectionXII { get; set; } = "";
    /// <summary>Constant density in kg/m³; blank means absent.</summary>
    public double? ConstantDensity { get; set; }
    /// <summary>Constant dimensionless Poisson ratio; blank means absent.</summary>
    public double? ConstantNu { get; set; }
    /// <summary>Material specification.</summary>
    public string Specification { get; set; } = "";
    /// <summary>Material grade.</summary>
    public string Grade { get; set; } = "";
    /// <summary>Class, condition or temper.</summary>
    public string Condition { get; set; } = "";
    /// <summary>UNS identity.</summary>
    public string Uns { get; set; } = "";
    /// <summary>Nominal composition, outside the four-field uniqueness key.</summary>
    public string Composition { get; set; } = "";
    /// <summary>Whether an edit requires saving or discarding before changing context.</summary>
    public bool IsDirty => _curveDirty || _constantsDirty || _identityDirty;
    /// <summary>Current operation status and validation errors.</summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>Open XML association and preview tools for physical properties and pressure charts.</summary>
    public RelayCommand ImportXmlCommand { get; }
    private void OpenXmlImport()
    {
        if (!AllowDiscard()) return;
        var window = new MaterialLibraryCrudApp.Views.XmlImportWindow(new XmlImportViewModel(_path, _options.FlexXmlFolder));
        window.Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.DataContext == this);
        window.ShowDialog(); ReloadCurves(); LoadConstants();
    }
    /// <summary>Save this variant atomically.</summary>
    public RelayCommand SaveCurveCommand { get; }
    /// <summary>Prepare a new variant in the current family.</summary>
    public RelayCommand NewCurveCommand { get; }
    /// <summary>Delete the selected variant, retaining an audit snapshot.</summary>
    public RelayCommand DeleteCurveCommand { get; }
    /// <summary>Add a blank original-note row (requires Allow edit).</summary>
    public RelayCommand AddNoteCommand { get; }
    /// <summary>Copy the selected ASME note from the dropdown into the original-note rows (requires Allow edit).</summary>
    public RelayCommand CopyCommonNoteCommand { get; }
    /// <summary>Remove the selected original-note row from this curve (requires Allow edit).</summary>
    public RelayCommand DeleteNoteCommand { get; }
    /// <summary>Reload the current family from the working copy.</summary>
    public RelayCommand ReloadCommand { get; }
    /// <summary>Save or clear the two scalar physical constants.</summary>
    public RelayCommand SaveConstantsCommand { get; }
    /// <summary>Save the four-field identity and composition.</summary>
    public RelayCommand SaveIdentityCommand { get; }
    /// <summary>Prepare a new MAIN identity.</summary>
    public RelayCommand NewIdentityCommand { get; }
    /// <summary>Mark UI edits after data binding has completed initial population.</summary>
    public void MarkDirty(string field = "curve")
    {
        if (_loading || field == nameof(Search)) return;
        if (field is nameof(ConstantDensity) or nameof(ConstantNu)) _constantsDirty = true;
        else if (field is nameof(Specification) or nameof(Grade) or nameof(Condition) or nameof(Uns) or nameof(Composition)) _identityDirty = true;
        else _curveDirty = true;
    }
    /// <summary>Protect unsaved edits when changing context or closing.</summary>
    public bool AllowDiscard()
    {
        if (!IsDirty) return true;
        if (MessageBox.Show("Discard unsaved changes?", "ASME 2025", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return false;
        _curveDirty = _constantsDirty = _identityDirty = false;
        return true;
    }
    private bool AllowCurveChange()
    {
        if (!_curveDirty) return true;
        if (MessageBox.Show("Discard unsaved curve changes?", "ASME 2025", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return false;
        _curveDirty = false; return true;
    }
    private void RefreshCommands() { SaveCurveCommand?.RaiseCanExecuteChanged(); NewCurveCommand?.RaiseCanExecuteChanged(); DeleteCurveCommand?.RaiseCanExecuteChanged(); SaveConstantsCommand?.RaiseCanExecuteChanged(); }
    private void RaiseIdentity() { foreach (var name in new[] { nameof(Specification), nameof(Grade), nameof(Condition), nameof(Uns), nameof(Composition) }) RaisePropertyChanged(name); }
    private void RefreshFamilyAvailability()
    {
        _hasBoltingCurves = _hasNonBoltingCurves = false;
        if (_material is not null)
        {
            using var db = Asme2025Data.openRead(_path);
            _hasBoltingCurves = Asme2025Data.curves(db, _material.Id, "Sb1").Any()
                || Asme2025Data.curves(db, _material.Id, "Sb2").Any();
            _hasNonBoltingCurves = new[] { "Sy", "Su", "S1", "S2" }
                .Any(code => Asme2025Data.curves(db, _material.Id, code).Any());
        }
        RaisePropertyChanged(nameof(AvailableFamilies));
        if (!AvailableFamilies.Contains(Family))
        {
            _family = AvailableFamilies.FirstOrDefault() ?? "Sy";
            RaisePropertyChanged(nameof(Family));
            RaisePropertyChanged(nameof(Unit));
            RaisePropertyChanged(nameof(CanEditForm));
            RaisePropertyChanged(nameof(IsMechanical));
            RaisePropertyChanged(nameof(ShowS1StressLevel));
            RaisePropertyChanged(nameof(ShowJointEfficiency));
        }
    }

    private void LoadCommonNoteCatalog()
    {
        _noteCatalog.Clear();
        using var db = Asme2025Data.openRead(_path);
        using var command = db.CreateCommand();
        command.CommandText = "SELECT SourceTable,Code,Kind,NoteText FROM Source_Notes ORDER BY SourceTable,Kind,Code";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            _noteCatalog.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
    }

    private void RefreshCommonNotes()
    {
        var tables = Family switch
        {
            "S1" => new[] { "1A", "1B" },
            "S2" => new[] { "5A", "5B" },
            "Sb1" => new[] { "3" },
            "Sb2" => new[] { "4" },
            "Sy" or "Su" => new[] { "1A", "1B", "2A", "2B" },
            _ => Array.Empty<string>()
        };
        CommonNotes.Clear();
        foreach (var note in _noteCatalog.Where(note => tables.Contains(note.SourceTable, StringComparer.OrdinalIgnoreCase)))
            CommonNotes.Add(note);
        SelectedCommonNote = null;
    }

    private void CopyCommonNote()
    {
        if (SelectedCommonNote is not { } note) return;
        if (SourceNoteRows.Any(existing => existing.SourceTable == note.SourceTable && existing.Code == note.Code))
        {
            Status = $"Note {note.SourceTable}/{note.Code} is already listed for this curve.";
            return;
        }
        var row = new PropertyNoteRow { SourceTable = note.SourceTable, Code = note.Code, Text = note.Text };
        SourceNoteRows.Add(row); SelectedSourceNoteRow = row;
        Status = $"Copied note {note.SourceTable}/{note.Code}. Save the curve to keep this change; the official ASME table is unchanged.";
    }

    private void DeleteSourceNote()
    {
        if (SelectedSourceNoteRow is not { } row) return;
        SourceNoteRows.Remove(row); SelectedSourceNoteRow = null;
        Status = "Note removed from this curve. Save the curve to keep this change; the official ASME table is unchanged.";
    }

    /// <summary>
    /// Splits the formatted source-note text ("table / code (kind): text", blank-line separated) into rows.
    /// Pure function: the regex anchors on each note header so multi-paragraph note bodies stay intact.
    /// </summary>
    public static IReadOnlyList<PropertyNoteRow> ParseSourceNotes(string? sourceNotes)
    {
        if (string.IsNullOrWhiteSpace(sourceNotes)) return [];
        var headers = Regex.Matches(sourceNotes, @"^(?<table>[^\s/]+) / (?<code>\S+) \((?<kind>[^)\r\n]*)\): ", RegexOptions.Multiline);
        if (headers.Count == 0) return [new PropertyNoteRow { SourceTable = "Source", Text = sourceNotes.Trim() }];
        var rows = new List<PropertyNoteRow>(headers.Count);
        // Each body runs from the end of its header to the start of the next header.
        for (var i = 0; i < headers.Count; i++)
        {
            var start = headers[i].Index + headers[i].Length;
            var end = i + 1 < headers.Count ? headers[i + 1].Index : sourceNotes.Length;
            rows.Add(new PropertyNoteRow { SourceTable = headers[i].Groups["table"].Value, Code = headers[i].Groups["code"].Value, Text = sourceNotes[start..end].Trim() });
        }
        return rows;
    }

    private static bool SameNotes(IReadOnlyList<PropertyNoteRow> left, IReadOnlyList<Asme2025UserNote> right) =>
        left.Count == right.Count && left.Zip(right).All(pair => pair.First.SourceTable == pair.Second.SourceTable && pair.First.Code == pair.Second.Code && pair.First.Text.Trim() == pair.Second.Text.Trim());

    private static bool IsWelded(string productForm) =>
        productForm.Contains("WLD", StringComparison.OrdinalIgnoreCase)
        || productForm.Contains("WELD", StringComparison.OrdinalIgnoreCase);

    private void ReloadCurves(long? selectedId = null)
    {
        _loading = true; _curveDirty = false; Curves.Clear(); _curve = null;
        if (_material is not null) { using var db = Asme2025Data.openRead(_path); foreach (var c in Asme2025Data.curves(db, _material.Id, Family)) Curves.Add(new(c)); }
        _curve = Curves.FirstOrDefault(c => c.Curve.Id == selectedId) ?? Curves.FirstOrDefault();
        RaisePropertyChanged(nameof(SelectedCurve)); LoadCurve(); _loading = false; _curveDirty = false;
    }
    private void LoadCurve()
    {
        _loading = true; _curveDirty = false; Points.Clear(); SourceNoteRows.Clear(); SelectedSourceNoteRow = null;
        var c = _curve?.Curve;
        ProductForm = c?.ProductForm ?? ""; SizeMinimum = c?.SizeMinimum; SizeMaximum = c?.SizeMaximum; MinimumIncluded = c?.MinimumIncluded ?? false; MaximumIncluded = c?.MaximumIncluded ?? true; SizeAxis = c?.SizeAxis ?? "thickness-or-unspecified"; S1StressLevel = c?.S1StressLevel ?? "Normal";
        if (c is not null) foreach (var p in c.Points) Points.Add(new() { Temperature = p.Temperature, Value = p.Value });
        if (c is not null && c.UserNoteItems is { Length: > 0 })
            foreach (var note in c.UserNoteItems) SourceNoteRows.Add(new() { SourceTable = note.SourceTable, Code = note.Code, Text = note.Text });
        else
            foreach (var row in ParseSourceNotes(c?.SourceNotes)) SourceNoteRows.Add(row);
        UserNotesText = c?.UserNotes ?? "";
        var limits = c?.CodeLimits;
        PNumber = limits?.PNumber ?? ""; GroupNumber = limits?.GroupNumber ?? ""; ExternalPressureChart = limits?.ExternalPressureChart ?? "";
        MaxTempSectionI = limits?.MaxTempSectionI ?? ""; MaxTempSectionIII = limits?.MaxTempSectionIII ?? ""; MaxTempSectionVIII1 = limits?.MaxTempSectionVIII1 ?? ""; MaxTempSectionVIII2 = limits?.MaxTempSectionVIII2 ?? ""; MaxTempSectionXII = limits?.MaxTempSectionXII ?? "";
        foreach (var name in new[] { nameof(PNumber), nameof(GroupNumber), nameof(ExternalPressureChart), nameof(MaxTempSectionI), nameof(MaxTempSectionIII), nameof(MaxTempSectionVIII1), nameof(MaxTempSectionVIII2), nameof(MaxTempSectionXII), nameof(ShowCodeLimits) }) RaisePropertyChanged(name);
        foreach (var name in new[] { nameof(ProductForm), nameof(SizeMinimum), nameof(SizeMaximum), nameof(MinimumIncluded), nameof(MaximumIncluded), nameof(SizeAxis), nameof(S1StressLevel), nameof(SourceNotes), nameof(HasSourceNoteOverride), nameof(SourceNoteOrigin), nameof(ShowJointEfficiency), nameof(JointEfficiencySummary) }) RaisePropertyChanged(name);
        RefreshCommonNotes();
        RefreshCommands(); _loading = false;
    }
    private void NewCurve() { if (!AllowCurveChange()) return; _curve = null; RaisePropertyChanged(nameof(SelectedCurve)); LoadCurve(); Status = "New curve: add temperature/value rows, then Save curve."; }
    private void SaveCurve()
    {
        try
        {
            if (_material is null) return;
            if (Points.Any(p => p.Temperature is null || p.Value is null)) { Status = "Complete all temperature/value cells or remove empty rows."; return; }
            // Rows identical to the official notes are not stored, so future official updates still apply.
            var editedRows = SourceNoteRows.Where(note => !string.IsNullOrWhiteSpace(note.Text))
                .Select(note => new Asme2025UserNote { SourceTable = string.Equals(note.SourceTable, "User", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(note.SourceTable) ? "Manual" : note.SourceTable.Trim(), Code = note.Code?.Trim() ?? "", Text = note.Text.Trim() }).ToArray();
            var noteItems = SameNotes(ParseSourceNotes(SourceNotes), editedRows) ? [] : editedRows;
            var userNotes = UserNotesText.Trim();
            var codeLimits = ShowCodeLimits
                ? new Asme2025CodeLimits { PNumber = PNumber, GroupNumber = GroupNumber, ExternalPressureChart = ExternalPressureChart, MaxTempSectionI = MaxTempSectionI, MaxTempSectionIII = MaxTempSectionIII, MaxTempSectionVIII1 = MaxTempSectionVIII1, MaxTempSectionVIII2 = MaxTempSectionVIII2, MaxTempSectionXII = MaxTempSectionXII }
                : Asme2025Data.emptyCodeLimits;
            var c = new Asme2025Curve { Id = _curve?.Curve.Id ?? 0, MaterialId = _material.Id, Code = Family, ProductForm = CanEditForm ? ProductForm : "", SizeMinimum = IsMechanical ? SizeMinimum : null, SizeMaximum = IsMechanical ? SizeMaximum : null, MinimumIncluded = MinimumIncluded, MaximumIncluded = MaximumIncluded, SizeAxis = SizeAxis, SourceTable = _curve?.Curve.SourceTable ?? "User", SourceNotes = SourceNotes, UserNotes = userNotes, UserNoteItems = noteItems, S1StressLevel = Family == "S1" ? S1StressLevel : "Normal", CodeLimits = codeLimits, Version = _curve?.Curve.Version ?? "", Points = Points.Select(p => new Asme2025Point(p.Temperature!.Value, p.Value!.Value)).ToArray() };
            if (!Asme2025Crud.saveCurve(_path, c).TryUnwrap(out var id, out var error)) { Status = error; return; }
            ReloadCurves(id); Status = $"Saved {Family} curve #{id}. Source notes preserved.";
        }
        catch (Exception ex) { Status = ex.Message; }
    }
    private void DeleteCurve()
    {
        if (_curve is null || MessageBox.Show("Delete this curve and its values?", "ASME 2025", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        if (!Asme2025Crud.deleteCurve(_path, _curve.Curve).TryUnwrap(out _, out var error)) { Status = error; return; }
        ReloadCurves(); Status = "Curve deleted. Other variants and source documents preserved.";
    }
    private void LoadConstants()
    {
        ConstantDensity = ConstantNu = null;
        if (_material is not null) { using var db = Asme2025Data.openRead(_path); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT ConstantDensity,ConstantPoissonRatio FROM Extension WHERE id_main=$id"; cmd.Parameters.AddWithValue("$id", _material.Id); using var r = cmd.ExecuteReader(); if (r.Read()) { ConstantDensity = r.IsDBNull(0) ? null : r.GetDouble(0); ConstantNu = r.IsDBNull(1) ? null : r.GetDouble(1); } }
        RaisePropertyChanged(nameof(ConstantDensity)); RaisePropertyChanged(nameof(ConstantNu)); _constantsDirty = false;
    }
    private void SaveConstants()
    {
        if (_material is null) return;
        if (!Asme2025Crud.saveConstants(_path, _material.Id, ConstantDensity, ConstantNu).TryUnwrap(out _, out var error)) { Status = error; return; }
        _constantsDirty = false; Status = "Saved constant density and Poisson ratio; blank values are NULL.";
    }
    private void SaveIdentity()
    {
        if (_curveDirty || _constantsDirty) { Status = "Save or discard curve/constant edits before saving the identity."; return; }
        var item = new Asme2025Material(_material?.Id ?? 0, Specification, Grade, Condition, Uns, Composition);
        if (!Asme2025Crud.saveIdentity(_path, item).TryUnwrap(out var id, out var error)) { Status = error; return; }
        _identityDirty = false; using var db = Asme2025Data.openRead(_path); Materials.Clear(); foreach (var m in Asme2025Data.materials(db)) Materials.Add(m); SelectedMaterial = Materials.First(m => m.Id == id); Status = "Saved unique MAIN identity.";
    }
}
