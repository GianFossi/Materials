using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using MaterialLibrary.Crud;
using MaterialLibrary.Domain.Database.Lookup;
using MaterialLibraryCrudApp.Interop;
using Microsoft.Win32;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>One selectable target association, with visible evidence and an immutable preview version.</summary>
public sealed class XmlTargetChoice : ObservableObject
{
    /// <summary>Target MAIN identity.</summary>
    public Asme2025Material Material { get; init; } = null!;
    /// <summary>Select this explicit record/material association for import.</summary>
    private bool _selected;
    public bool Selected { get => _selected; set => SetProperty(ref _selected, value); }
    /// <summary>Strength of matching evidence; zero indicates a manual association.</summary>
    public int Score { get; init; }
    /// <summary>Explanation of the match or of unresolved alternatives.</summary>
    public string Reason { get; init; } = "Associazione manuale";
    /// <summary>Target state fingerprint captured before the import.</summary>
    public string Version { get; init; } = "";
}

/// <summary>Review and associate source XML records before importing physical data or external-pressure charts.</summary>
public sealed class XmlImportViewModel : ObservableObject
{
    private readonly string _database;
    private XmlPropertyRecord? _record;
    private XmlTargetChoice? _target;
    private string _folder = "", _search = "", _status = "Scegli una cartella XML.", _numeric = "", _property = "E", _composition = "", _uns = "", _chart = "";
    private bool _reviewed, _constant, _replace;
    private XmlPropertyProposal[] _proposals = [];
    private readonly HashSet<long> _selectedIds = [];
    private readonly List<Asme2025Material> _materials;
    /// <summary>Create a read-only preview session over a working database.</summary>
    public XmlImportViewModel(string database)
    {
        _database = database;
        using var db = Asme2025Data.openRead(database);
        _materials = Asme2025Data.materials(db).ToList();
        PickFolderCommand = new RelayCommand(PickFolder);
        ScanCommand = new RelayCommand(Scan);
        MatchCommand = new RelayCommand(Match);
        SelectAutomaticCommand = new RelayCommand(SelectAutomatic);
        ImportCommand = new RelayCommand(Import);
        PreviewCommand = new RelayCommand(() => { try { PreviewRecord = BuildRecord(); RaisePropertyChanged(nameof(PreviewRecord)); Status = $"Anteprima: {PreviewRecord.Points.Length} punti; nessuna scrittura."; } catch (Exception e) { Status = e.Message; } });
    }
    /// <summary>Every parsed source row or reviewable raw XML record.</summary>
    public ObservableCollection<XmlPropertyRecord> Records { get; } = [];
    /// <summary>Search-filtered targets, including explicit manual choices.</summary>
    public ObservableCollection<XmlTargetChoice> Targets { get; } = [];
    /// <summary>Candidate numerical record plotted as the incoming curve.</summary>
    public XmlPropertyRecord? PreviewRecord { get; private set; }
    /// <summary>Current database family plotted beside the incoming record.</summary>
    public IReadOnlyList<Asme2025Curve> ExistingCurves { get; private set; } = [];
    /// <summary>Existing pressure-chart curves, grouped by temperature, with dimensionless A and MPa B.</summary>
    public IReadOnlyList<(string Name, (double A, double B)[] Points)> ExistingPressureCurves { get; private set; } = [];
    /// <summary>Selected source record; its points are editable before confirmation.</summary>
    public XmlPropertyRecord? SelectedRecord
    {
        get => _record;
        set
        {
            if (!SetProperty(ref _record, value) || value is null) return;
            _selectedIds.Clear(); Targets.Clear();
            PropertyCode = value.PropertyCode; SourceComposition = value.Composition; SourceUns = value.UNS; ChartName = value.ChartName;
            IsConstant = value.IsConstant; Reviewed = !value.RequiresReview;
            NumericText = string.Join(Environment.NewLine, value.Points.Select(p => value.PropertyCode == "EP" ? FormattableString.Invariant($"{p.Temperature:G17}\t{p.X:G17}\t{p.Y:G17}") : FormattableString.Invariant($"{p.Temperature:G17}\t{p.Y:G17}")));
            PreviewRecord = value; RaisePropertyChanged(nameof(PreviewRecord)); RaisePropertyChanged(nameof(SourceText)); RaisePropertyChanged(nameof(Diagnostic));
            Match();
        }
    }
    /// <summary>Selected target for overlay of existing database values.</summary>
    public XmlTargetChoice? SelectedTarget { get => _target; set { if (SetProperty(ref _target, value)) RefreshOverlay(); } }
    /// <summary>Folder scanned recursively for XML files.</summary>
    public string Folder { get => _folder; set => SetProperty(ref _folder, value); }
    /// <summary>Filter material identity, composition and matching evidence.</summary>
    public string Search { get => _search; set { if (SetProperty(ref _search, value)) RefreshTargets(); } }
    /// <summary>Supported physical families plus external-pressure chart records.</summary>
    public IReadOnlyList<string> Families { get; } = Asme2025Data.families.Where(f => !new[] { "Sy", "Su", "S1", "S2", "Sb1", "Sb2" }.Contains(f.Item1)).Select(f => f.Item1).Append("EP").ToArray();
    /// <summary>Destination family; Alpha is mean and AlphaInstantaneous is independent.</summary>
    public string PropertyCode { get => _property; set { if (SetProperty(ref _property, value)) RaisePropertyChanged(nameof(Unit)); } }
    /// <summary>Fixed destination unit; EP uses dimensionless A and MPa B.</summary>
    public string Unit => PropertyCode == "EP" ? "A adimensionale, B MPa" : Asme2025Data.families.FirstOrDefault(f => f.Item1 == PropertyCode)?.Item2 ?? "";
    /// <summary>Explicit nominal composition from the source record, editable for a verified raw group.</summary>
    public string SourceComposition { get => _composition; set => SetProperty(ref _composition, value); }
    /// <summary>Explicit UNS identifiers from the source record.</summary>
    public string SourceUns { get => _uns; set => SetProperty(ref _uns, value); }
    /// <summary>External-pressure reference identifier, such as CS-1.</summary>
    public string ChartName { get => _chart; set => SetProperty(ref _chart, value); }
    /// <summary>Review confirmation for raw PDF extraction and manually entered numerical rows.</summary>
    public bool Reviewed { get => _reviewed; set => SetProperty(ref _reviewed, value); }
    /// <summary>Import a single density or Poisson value into Extension.</summary>
    public bool IsConstant { get => _constant; set => SetProperty(ref _constant, value); }
    /// <summary>Explicitly permit replacing an existing family or chart association.</summary>
    public bool ReplaceExisting { get => _replace; set => SetProperty(ref _replace, value); }
    /// <summary>Tab/space/semicolon separated rows: T value, or T A B for pressure charts; SI units only.</summary>
    public string NumericText { get => _numeric; set => SetProperty(ref _numeric, value); }
    /// <summary>Original raw text, with no instructions executed.</summary>
    public string SourceText => _record is null ? "" : $"NOTE DEL RECORD:\n{_record.Notes}\n\n" + (string.IsNullOrWhiteSpace(_record.RawText) ? _record.Xml : _record.RawText);
    /// <summary>Extraction status and limitations.</summary>
    public string Diagnostic => _record?.Diagnostic ?? "";
    /// <summary>Import/validation status.</summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>Choose an XML source folder.</summary>
    public RelayCommand PickFolderCommand { get; }
    /// <summary>Scan all XML files and report malformed files.</summary>
    public RelayCommand ScanCommand { get; }
    /// <summary>Compute exact identity/UNS/composition/chart-reference evidence.</summary>
    public RelayCommand MatchCommand { get; }
    /// <summary>Select only unambiguous proposals for the current source record.</summary>
    public RelayCommand SelectAutomaticCommand { get; }
    /// <summary>Apply selected assignments after a concrete confirmation.</summary>
    public RelayCommand ImportCommand { get; }
    /// <summary>Parse editable numerical rows and redraw the preview.</summary>
    public RelayCommand PreviewCommand { get; }
    /// <summary>Choose a source directory without changing database data.</summary>
    private void PickFolder() { var picker = new OpenFolderDialog { Title = "Cartella con XML delle proprietà fisiche / External Pressure" }; if (picker.ShowDialog() == true) { Folder = picker.FolderName; Scan(); } }
    /// <summary>Read XML files recursively and expose each file diagnostic.</summary>
    private void Scan()
    {
        try
        {
            var result = XmlPropertyReader.readFolder(Folder);
            Records.Clear(); foreach (var record in result.Item1) Records.Add(record);
            SelectedRecord = Records.FirstOrDefault();
            Status = $"{Records.Count} record XML; {result.Item2.Length} file non leggibili." + string.Join(Environment.NewLine, result.Item2);
        }
        catch (Exception e) { Status = e.Message; }
    }
    /// <summary>Parse approved editable SI rows while retaining the original source document.</summary>
    private XmlPropertyRecord BuildRecord()
    {
        if (_record is null) throw new InvalidOperationException("Seleziona un record XML.");
        var points = new List<XmlPropertyPoint>();
        foreach (var line in NumericText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var cells = line.Trim().Split(new[] { '\t', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int count = PropertyCode == "EP" ? 3 : 2;
            if (cells.Length != count) throw new FormatException($"Ogni riga deve contenere {count} numeri: {line}");
            var values = cells.Select(s => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
            points.Add(new(values[0], PropertyCode == "EP" ? values[1] : values[0], values[^1]));
        }
        return new XmlPropertyRecord(_record.Key, _record.File, _record.SHA256, _record.Xml, _record.Description, PropertyCode, PropertyCode == "EP" ? "MPa" : Unit, ChartName, _record.Specification, _record.Grade, _record.Condition, SourceUns, SourceComposition, _record.Notes, _record.RawText, true, _record.Diagnostic, IsConstant, points.ToArray());
    }
    /// <summary>Recalculate explainable associations across all source records to expose ambiguity.</summary>
    private void Match()
    {
        try
        {
            if (_record is null) return;
            var current = new XmlPropertyRecord(_record.Key, _record.File, _record.SHA256, _record.Xml, _record.Description, PropertyCode, PropertyCode == "EP" ? "MPa" : Unit, ChartName, _record.Specification, _record.Grade, _record.Condition, SourceUns, SourceComposition, _record.Notes, _record.RawText, _record.RequiresReview, _record.Diagnostic, IsConstant, _record.Points);
            using var db = Asme2025Data.openRead(_database);
            var all = Records.Where(r => r.Key != current.Key).Append(current).ToArray();
            _proposals = XmlPropertyMatching.propose(db, all);
            RefreshTargets(); Status = "Proposte aggiornate; confronta curva, riga sorgente e composizione prima di importare.";
        }
        catch (Exception e) { Status = e.Message; }
    }
    /// <summary>Filter targets and capture current database versions; retain checkboxes while navigating filters.</summary>
    private void RefreshTargets()
    {
        foreach (var choice in Targets) { if (choice.Selected) _selectedIds.Add(choice.Material.Id); else _selectedIds.Remove(choice.Material.Id); }
        Targets.Clear();
        using var db = Asme2025Data.openRead(_database);
        foreach (var m in _materials)
        {
            var proposal = _proposals.FirstOrDefault(p => p.MaterialId == m.Id && p.RecordKey == _record?.Key);
            if (Search.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(term => !$"{m.Specification} {m.Grade} {m.Condition} {m.UNS} {m.NominalComposition} {proposal?.Reason}".Contains(term, StringComparison.OrdinalIgnoreCase))) continue;
            Targets.Add(new() { Material = m, Selected = _selectedIds.Contains(m.Id), Score = proposal?.Score ?? 0, Reason = proposal?.Reason ?? "Associazione manuale", Version = XmlPropertyImport.version(db, m.Id, PropertyCode) });
        }
        SelectedTarget = Targets.OrderByDescending(t => t.Score).FirstOrDefault();
    }
    /// <summary>Select visible unambiguous proposals without writing any values.</summary>
    private void SelectAutomatic()
    {
        Match();
        var candidates = _proposals.Where(p => p.RecordKey == _record?.Key && p.Score >= 85 && !p.Reason.Contains("più record" )).Select(p => p.MaterialId).ToHashSet();
        _selectedIds.Clear();
        foreach (var t in Targets) { t.Selected = candidates.Contains(t.Material.Id); if (t.Selected) _selectedIds.Add(t.Material.Id); }
        RaisePropertyChanged(nameof(Targets)); Status = $"{Targets.Count(t => t.Selected)} associazioni univoche selezionate. Confermare la verifica del record e importare.";
    }
    /// <summary>Read current SI curves or A/B pressure points for the selected material.</summary>
    private void RefreshOverlay()
    {
        ExistingCurves = []; ExistingPressureCurves = [];
        if (_target is not null)
        {
            using var db = Asme2025Data.openRead(_database);
            if (PropertyCode == "EP") ExistingPressureCurves = Asme2025Data.pressureCurves(db, _target.Material.Id).Select(c => ($"DB {c.Item2} {c.Item3:G6} °C", c.Item4.Select(p => (p.Item1, p.Item2)).ToArray())).ToArray();
            else ExistingCurves = Asme2025Data.curves(db, _target.Material.Id, PropertyCode).ToArray();
        }
        RaisePropertyChanged(nameof(ExistingCurves));
    }
    /// <summary>Confirm the concrete visible batch and apply it atomically with provenance.</summary>
    private void Import()
    {
        try
        {
            var record = BuildRecord();
            if (!XmlPropertyImport.validate(record, Reviewed).TryUnwrap(out _, out var problem)) { Status = problem; return; }
            var selected = Targets.Where(t => t.Selected).ToArray();
            if (selected.Length == 0) { Status = "Seleziona almeno un materiale nella colonna Importa."; return; }
            if (MessageBox.Show($"Importare {record.PropertyCode}, {record.Points.Length} punti, per {selected.Length} materiali?\nSostituzione esplicita: {ReplaceExisting}", "Conferma importazione XML", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var assignments = selected.Select(t => new XmlPropertyAssignment(t.Material.Id, record, Reviewed, ReplaceExisting, t.Version)).ToArray();
            if (!XmlPropertyImport.apply(_database, assignments).TryUnwrap(out var count, out var error)) { Status = error; return; }
            RefreshTargets(); Status = $"Importate {count} associazioni. XML, hash e cronologia conservati.";
        }
        catch (Exception e) { Status = e.Message; }
    }
}
