using System.Collections.ObjectModel;

namespace MaterialLibraryCrudApp.ViewModels;

public sealed class MaterialDiffViewModel : ObservableObject
{
    private MaterialRowViewModel? _left;
    private MaterialRowViewModel? _right;

    public MaterialDiffViewModel(IReadOnlyList<MaterialRowViewModel> materials, MaterialRowViewModel? initial)
    {
        Materials = new ObservableCollection<MaterialRowViewModel>(materials);
        _left = initial ?? Materials.FirstOrDefault();
        _right = Materials.FirstOrDefault(item => !ReferenceEquals(item, _left)) ?? _left;
        Rebuild();
    }

    public ObservableCollection<MaterialRowViewModel> Materials { get; }
    public ObservableCollection<MaterialDiffRow> Differences { get; } = [];

    public MaterialRowViewModel? Left
    {
        get => _left;
        set { if (SetProperty(ref _left, value)) Rebuild(); }
    }

    public MaterialRowViewModel? Right
    {
        get => _right;
        set { if (SetProperty(ref _right, value)) Rebuild(); }
    }

    private void Rebuild()
    {
        Differences.Clear();
        if (Left is null || Right is null) return;
        Add("Id", Left.Id, Right.Id);
        Add("Name", Left.Name, Right.Name);
        Add("Specification", Left.Specification, Right.Specification);
        Add("Grade", Left.Grade, Right.Grade);
        Add("Class / condition / tempering", Left.ClassConditionTempering, Right.ClassConditionTempering);
        Add("UNS", Left.AlloyIdentificationUns, Right.AlloyIdentificationUns);
        Add("Product form", Left.ProductForm, Right.ProductForm);
        Add("Product analysis", Left.ProductAnalysis, Right.ProductAnalysis);
        Add("Family", Left.Family, Right.Family);
        Add("Notes", Left.Notes ?? string.Empty, Right.Notes ?? string.Empty);
        Add("Last modified", Left.LastModified.ToString("u"), Right.LastModified.ToString("u"));
    }

    private void Add(string field, string left, string right) => Differences.Add(new MaterialDiffRow(field, left, right, !string.Equals(left, right, StringComparison.Ordinal)));
}

public sealed record MaterialDiffRow(string Field, string LeftValue, string RightValue, bool IsChanged);
