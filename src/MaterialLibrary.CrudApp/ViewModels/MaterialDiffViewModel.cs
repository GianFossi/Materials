using System.Collections.ObjectModel;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>
/// Drives the side-by-side comparison of two materials' identification fields.
/// </summary>
/// <remarks>
/// Compares only the flat identity fields projected by <see cref="MaterialRowViewModel"/>, which is
/// what the grid already shows. The nested property tables are deliberately out of scope: a useful
/// diff of those needs row-level alignment by temperature, not a field-by-field string comparison.
/// </remarks>
public sealed class MaterialDiffViewModel : ObservableObject
{
    private MaterialRowViewModel? _left;
    private MaterialRowViewModel? _right;

    /// <summary>Creates the comparison over the loaded materials.</summary>
    /// <param name="materials">Materials available to compare.</param>
    /// <param name="initial">Material to preselect on the left, or <c>null</c> to take the first.</param>
    public MaterialDiffViewModel(IReadOnlyList<MaterialRowViewModel> materials, MaterialRowViewModel? initial)
    {
        Materials = new ObservableCollection<MaterialRowViewModel>(materials);
        _left = initial ?? Materials.FirstOrDefault();
        _right = Materials.FirstOrDefault(item => !ReferenceEquals(item, _left)) ?? _left;
        Rebuild();
    }

    /// <summary>Materials offered in both selection lists.</summary>
    public ObservableCollection<MaterialRowViewModel> Materials { get; }
    /// <summary>One row per compared field, rebuilt whenever either side changes.</summary>
    public ObservableCollection<MaterialDiffRow> Differences { get; } = [];

    /// <summary>Material shown in the left column.</summary>
    public MaterialRowViewModel? Left
    {
        get => _left;
        set { if (SetProperty(ref _left, value)) Rebuild(); }
    }

    /// <summary>Material shown in the right column.</summary>
    public MaterialRowViewModel? Right
    {
        get => _right;
        set { if (SetProperty(ref _right, value)) Rebuild(); }
    }

    /// <summary>Recomputes the comparison rows for the current selection.</summary>
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

    /// <summary>Appends one comparison row, flagging it when the two values differ.</summary>
    /// <param name="field">Display name of the field.</param>
    /// <param name="left">Value from the left material.</param>
    /// <param name="right">Value from the right material.</param>
    private void Add(string field, string left, string right) => Differences.Add(new MaterialDiffRow(field, left, right, !string.Equals(left, right, StringComparison.Ordinal)));
}

/// <summary>One compared field and the two values, with a flag the view uses to highlight it.</summary>
/// <param name="Field">Display name of the compared field.</param>
/// <param name="LeftValue">Value from the left material.</param>
/// <param name="RightValue">Value from the right material.</param>
/// <param name="IsChanged">Whether the two values differ.</param>
public sealed record MaterialDiffRow(string Field, string LeftValue, string RightValue, bool IsChanged);
