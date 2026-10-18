using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using MaterialLibrary.Crud;
using MaterialLibrary.Domain.Database.Lookup;
using MaterialLibraryCrudApp.Interop;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>One editable point, in Celsius and the selected property's displayed unit.</summary>
public sealed class PropertyPointRow
{
    /// <summary>Temperature in Celsius; blank values must be completed before saving.</summary>
    public double? Temperature { get; set; }
    /// <summary>Property value in the selected family's fixed unit.</summary>
    public double? Value { get; set; }
}

/// <summary>A source variant identified independently of its temperature points.</summary>
public sealed record PropertyCurveChoice(Asme2025Curve Curve)
{
    /// <summary>Readable variant label retaining curve ID, form and dimensional boundaries in mm.</summary>
    public string Label => $"#{Curve.Id} · {Curve.ProductForm} · {(Curve.MinimumIncluded ? "[" : "(")}{Curve.SizeMinimum}, {Curve.SizeMaximum}{(Curve.MaximumIncluded ? "]" : ")")} mm · {Curve.SourceTable} · {Curve.Points.Length} points";
}

/// <summary>Edits native 2025 curves on a working copy, with optimistic concurrency and preserved source notes.</summary>
public sealed class Asme2025EditorViewModel : ObservableObject
{
    private readonly string _path;
    private Asme2025Material? _material;
    private PropertyCurveChoice? _curve;
    private string _family = "Sy", _search = "", _status = "Select a material and property family.";
    private bool _loading, _curveDirty, _constantsDirty, _identityDirty;
    /// <summary>Creates an editor over an existing explicit working copy.</summary>
    public Asme2025EditorViewModel(string path, long? materialId = null)
    {
        _path = path;
        using var db = Asme2025Data.openRead(path);
        if (!Asme2025Data.isNormalized(db)) throw new InvalidOperationException("Open the ASME 2025 database.");
        foreach (var material in Asme2025Data.materials(db)) Materials.Add(material);
        MaterialView = CollectionViewSource.GetDefaultView(Materials);
        MaterialView.Filter = item => item is Asme2025Material m && Search.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(term => $"{m.Id} {m.Specification} {m.Grade} {m.Condition} {m.UNS}".Contains(term, StringComparison.OrdinalIgnoreCase));
        ImportXmlCommand = new RelayCommand(OpenXmlImport);
        SaveCurveCommand = new RelayCommand(SaveCurve, () => SelectedMaterial is not null);
        NewCurveCommand = new RelayCommand(NewCurve, () => SelectedMaterial is not null);
        DeleteCurveCommand = new RelayCommand(DeleteCurve, () => _curve is not null);
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
            RaiseIdentity(); ReloadCurves(); LoadConstants(); RefreshCommands(); _loading = false; _identityDirty = false;
        }
    }
    /// <summary>Families exposed without confusing ordinary stresses and the two bolting families.</summary>
    public IReadOnlyList<string> Families { get; } = Asme2025Data.families.Select(f => f.Item1).ToArray();
    /// <summary>Selected property family.</summary>
    public string Family { get => _family; set { if (value == _family) return; if (!AllowCurveChange()) { RaisePropertyChanged(); return; } _family = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(Unit)); RaisePropertyChanged(nameof(CanEditForm)); RaisePropertyChanged(nameof(IsMechanical)); ReloadCurves(); } }
    /// <summary>Fixed property unit; temperatures are always Celsius.</summary>
    public string Unit => Asme2025Data.families.First(f => f.Item1 == Family).Item2;
    /// <summary>Product form applies only to S1 and S2.</summary>
    public bool CanEditForm => Family is "S1" or "S2";
    /// <summary>Mechanical properties may have size boundaries in mm.</summary>
    public bool IsMechanical => Family is "Sy" or "Su" or "S1" or "S2" or "Sb1" or "Sb2";
    /// <summary>Every separately selectable curve in the current family.</summary>
    public ObservableCollection<PropertyCurveChoice> Curves { get; } = [];
    /// <summary>Selected variant; source-note text remains read-only.</summary>
    public PropertyCurveChoice? SelectedCurve { get => _curve; set { if (value == _curve) return; if (!AllowCurveChange()) { RaisePropertyChanged(); return; } _curve = value; RaisePropertyChanged(); LoadCurve(); } }
    /// <summary>Editable temperature/value rows.</summary>
    public ObservableCollection<PropertyPointRow> Points { get; } = [];
    /// <summary>Product form for S1/S2.</summary>
    public string ProductForm { get; set; } = "";
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
    /// <summary>Separate editable commentary; source ASME text is never overwritten.</summary>
    public string UserNotes { get; set; } = "";
    /// <summary>Full general and specific ASME note texts for the source variant.</summary>
    public string SourceNotes => _curve?.Curve.SourceNotes ?? "";
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
        var window = new MaterialLibraryCrudApp.Views.XmlImportWindow(new XmlImportViewModel(_path));
        window.Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.DataContext == this);
        window.ShowDialog(); ReloadCurves(); LoadConstants();
    }
    /// <summary>Save this variant atomically.</summary>
    public RelayCommand SaveCurveCommand { get; }
    /// <summary>Prepare a new variant in the current family.</summary>
    public RelayCommand NewCurveCommand { get; }
    /// <summary>Delete the selected variant, retaining an audit snapshot.</summary>
    public RelayCommand DeleteCurveCommand { get; }
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
    private void ReloadCurves(long? selectedId = null)
    {
        _loading = true; _curveDirty = false; Curves.Clear(); _curve = null;
        if (_material is not null) { using var db = Asme2025Data.openRead(_path); foreach (var c in Asme2025Data.curves(db, _material.Id, Family)) Curves.Add(new(c)); }
        _curve = Curves.FirstOrDefault(c => c.Curve.Id == selectedId) ?? Curves.FirstOrDefault();
        RaisePropertyChanged(nameof(SelectedCurve)); LoadCurve(); _loading = false; _curveDirty = false;
    }
    private void LoadCurve()
    {
        _loading = true; _curveDirty = false; Points.Clear();
        var c = _curve?.Curve;
        ProductForm = c?.ProductForm ?? ""; SizeMinimum = c?.SizeMinimum; SizeMaximum = c?.SizeMaximum; MinimumIncluded = c?.MinimumIncluded ?? false; MaximumIncluded = c?.MaximumIncluded ?? true; SizeAxis = c?.SizeAxis ?? "thickness-or-unspecified"; UserNotes = c?.UserNotes ?? "";
        if (c is not null) foreach (var p in c.Points) Points.Add(new() { Temperature = p.Temperature, Value = p.Value });
        foreach (var name in new[] { nameof(ProductForm), nameof(SizeMinimum), nameof(SizeMaximum), nameof(MinimumIncluded), nameof(MaximumIncluded), nameof(SizeAxis), nameof(UserNotes), nameof(SourceNotes) }) RaisePropertyChanged(name);
        RefreshCommands(); _loading = false;
    }
    private void NewCurve() { if (!AllowCurveChange()) return; _curve = null; RaisePropertyChanged(nameof(SelectedCurve)); LoadCurve(); Status = "New curve: add temperature/value rows, then Save curve."; }
    private void SaveCurve()
    {
        try
        {
            if (_material is null) return;
            if (Points.Any(p => p.Temperature is null || p.Value is null)) { Status = "Complete all temperature/value cells or remove empty rows."; return; }
            var c = new Asme2025Curve { Id = _curve?.Curve.Id ?? 0, MaterialId = _material.Id, Code = Family, ProductForm = CanEditForm ? ProductForm : "", SizeMinimum = IsMechanical ? SizeMinimum : null, SizeMaximum = IsMechanical ? SizeMaximum : null, MinimumIncluded = MinimumIncluded, MaximumIncluded = MaximumIncluded, SizeAxis = SizeAxis, SourceTable = _curve?.Curve.SourceTable ?? "User", SourceNotes = SourceNotes, UserNotes = UserNotes, Version = _curve?.Curve.Version ?? "", Points = Points.Select(p => new Asme2025Point(p.Temperature!.Value, p.Value!.Value)).ToArray() };
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
