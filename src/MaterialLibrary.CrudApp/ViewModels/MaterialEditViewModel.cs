using System.Globalization;
using MaterialLibraryCrudApp.Interop;
using MaterialLibrary.Domain;
using Microsoft.FSharp.Core;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>
/// Mutable editing buffer for one material: the two-way-bindable counterpart of the immutable
/// F# <see cref="Material"/> record.
/// </summary>
/// <remarks>
/// <para>
/// This class exists because of the central F#/C# impedance mismatch in this application. WPF
/// two-way binding requires public settable properties plus change notification; F# records offer
/// neither. The view model therefore holds a flat, mutable copy of the editable fields, and
/// converts back to a domain record only once, on confirmation.
/// </para>
/// <para>
/// Numeric fields are held as <see cref="string"/> rather than <see cref="double"/> on purpose:
/// a partially typed value such as <c>"-"</c> or <c>""</c> is not a valid double, and binding
/// directly to a numeric property would either reject the keystroke or throw. Parsing happens once
/// in <see cref="TryBuildMaterial"/>, using the invariant culture so files stay portable across
/// machines with different decimal separators.
/// </para>
/// </remarks>
public sealed class MaterialEditViewModel : ObservableObject
{
    private readonly Material? _existing;

    /// <summary>
    /// Mutable mirror of the material being edited. Fields the dialog does not expose (property
    /// tables, ASME code lists, timestamps) live here and are carried through to the saved record.
    /// </summary>
    private readonly MaterialDraft _draft;

    private MaterialFamilyChoice _selectedFamily = MaterialFamilyChoice.For(null);
    private string _id = string.Empty;
    private string _specification = string.Empty;
    private string _grade = string.Empty;
    private string _productForm = string.Empty;
    private string _nominalComposition = string.Empty;
    private string _classConditionTempering = string.Empty;
    private string _alloyIdentificationUns = string.Empty;
    private string _elongationLongitudinalPercent = string.Empty;
    private string _elongationTransversePercent = string.Empty;
    private string _reductionOfAreaPercent = "0";
    private string _specifiedMinimumYieldStrength = "0";
    private string _specifiedMinimumUltimateStrength = "0";
    private string _notes = string.Empty;

    /// <summary>Creates an editing buffer.</summary>
    /// <param name="existing">Material to edit, or <c>null</c> to start a new one.</param>
    public MaterialEditViewModel(Material? existing)
    {
        _existing = existing;
        _draft = existing is null ? new MaterialDraft() : new MaterialDraft(existing);

        if (existing is null)
        {
            return;
        }

        // Seed the buffer from the immutable record. Options are flattened to plain strings here;
        // the reverse conversion happens in TryBuildMaterial.
        _id = existing.Id;
        _specification = existing.Specification;
        _grade = existing.Grade;
        _productForm = existing.ProductForm;
        _nominalComposition = existing.NominalComposition;
        _classConditionTempering = existing.Class_Condition_Tempering;
        _alloyIdentificationUns = existing.AlloyIdentification_UNS;
        _elongationLongitudinalPercent = FormatOptional(existing.BasicProperties.ElongationLongitudinalPercent);
        _elongationTransversePercent = FormatOptional(existing.BasicProperties.ElongationTransversePercent);
        _reductionOfAreaPercent = Format(existing.BasicProperties.ReductionOfAreaPercent);
        _specifiedMinimumYieldStrength = Format(existing.BasicProperties.SpecifiedMinimumYieldStrength);
        _specifiedMinimumUltimateStrength = Format(existing.BasicProperties.SpecifiedMinimumUltimateStrength);
        _notes = existing.Notes.AsNullable() ?? string.Empty;
        _selectedFamily = MaterialFamilyChoice.For(_draft.Family);
    }

    /// <summary>Selectable ASME family classifications, including a "not assigned" entry.</summary>
    public IReadOnlyList<MaterialFamilyChoice> FamilyChoices => MaterialFamilyChoice.All;

    /// <summary>ASME family classification chosen by the user.</summary>
    public MaterialFamilyChoice SelectedFamily
    {
        get => _selectedFamily;
        set => SetProperty(ref _selectedFamily, value ?? MaterialFamilyChoice.For(null));
    }

    /// <summary>Window title reflecting create versus edit mode.</summary>
    public string Title => IsNew ? "New Material" : "Edit Material";

    /// <summary>Whether this buffer creates a new material rather than editing an existing one.</summary>
    public bool IsNew => _existing is null;

    /// <summary>
    /// Whether the ID field is editable. The ID is the repository key and is fixed after creation.
    /// </summary>
    public bool IsIdEditable => IsNew;

    /// <summary>Unique repository key; required.</summary>
    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    /// <summary>ASME specification number (e.g. <c>SA-516</c>).</summary>
    public string Specification
    {
        get => _specification;
        set => SetProperty(ref _specification, value);
    }

    /// <summary>Material grade or class (e.g. <c>70</c>).</summary>
    public string Grade
    {
        get => _grade;
        set => SetProperty(ref _grade, value);
    }

    /// <summary>Product form (free text).</summary>
    public string ProductForm
    {
        get => _productForm;
        set => SetProperty(ref _productForm, value);
    }

    /// <summary>Nominal composition (free text).</summary>
    public string NominalComposition
    {
        get => _nominalComposition;
        set => SetProperty(ref _nominalComposition, value);
    }

    /// <summary>Class, condition, or tempering designation (free text).</summary>
    public string ClassConditionTempering
    {
        get => _classConditionTempering;
        set => SetProperty(ref _classConditionTempering, value);
    }

    /// <summary>UNS alloy identifier (free text).</summary>
    public string AlloyIdentificationUns
    {
        get => _alloyIdentificationUns;
        set => SetProperty(ref _alloyIdentificationUns, value);
    }

    /// <summary>
    /// Room-temperature elongation at fracture (%) along the rolling direction, as entered text.
    /// Blank means not reported.
    /// </summary>
    public string ElongationLongitudinalPercent
    {
        get => _elongationLongitudinalPercent;
        set => SetProperty(ref _elongationLongitudinalPercent, value);
    }

    /// <summary>
    /// Room-temperature elongation at fracture (%) across the rolling direction, as entered text.
    /// Blank means not reported.
    /// </summary>
    public string ElongationTransversePercent
    {
        get => _elongationTransversePercent;
        set => SetProperty(ref _elongationTransversePercent, value);
    }

    /// <summary>Room-temperature minimum reduction of area at fracture (%), as entered text.</summary>
    public string ReductionOfAreaPercent
    {
        get => _reductionOfAreaPercent;
        set => SetProperty(ref _reductionOfAreaPercent, value);
    }

    /// <summary>Specified Minimum Yield Strength, SMYS (MPa), as entered text.</summary>
    public string SpecifiedMinimumYieldStrength
    {
        get => _specifiedMinimumYieldStrength;
        set => SetProperty(ref _specifiedMinimumYieldStrength, value);
    }

    /// <summary>Specified Minimum Ultimate Tensile Strength, SMUTS (MPa), as entered text.</summary>
    public string SpecifiedMinimumUltimateStrength
    {
        get => _specifiedMinimumUltimateStrength;
        set => SetProperty(ref _specifiedMinimumUltimateStrength, value);
    }

    /// <summary>Free-text notes; blank is stored as <c>None</c>.</summary>
    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    /// <summary>
    /// Validates the buffer and converts it into an immutable domain record.
    /// </summary>
    /// <param name="material">Receives the built material on success; <c>null</c> on failure.</param>
    /// <param name="validationMessage">Receives a user-facing validation message on failure; <c>null</c> on success.</param>
    /// <returns><c>true</c> when the buffer was valid and a material was produced.</returns>
    /// <remarks>
    /// Pure with respect to the view model: it reads the buffer and allocates a new record without
    /// mutating either the buffer or the material it was seeded from.
    /// </remarks>
    public bool TryBuildMaterial(out Material? material, out string? validationMessage)
    {
        material = null;
        validationMessage = null;

        var id = Id.Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            validationMessage = "Id is required.";
            return false;
        }

        // Both elongations are optional: the ASME reference tables leave them blank, so a blank cell
        // must round-trip as "not reported" rather than being rejected or silently read as zero.
        if (!TryParseOptional(ElongationLongitudinalPercent, out var elongationLongitudinal) ||
            !TryParseOptional(ElongationTransversePercent, out var elongationTransverse))
        {
            validationMessage = "Elongation must be numeric or left blank.";
            return false;
        }

        if (!TryParse(ReductionOfAreaPercent, out var reductionOfArea) ||
            !TryParse(SpecifiedMinimumYieldStrength, out var smys) ||
            !TryParse(SpecifiedMinimumUltimateStrength, out var smuts))
        {
            validationMessage = "Reduction of area, SMYS, and SMUTS must be numeric.";
            return false;
        }

        // Write the edited values into the mirror. Fields the dialog does not expose (property
        // tables, ASME code lists, CreatedDate) are already in the draft and pass through untouched.
        _draft.Id = id;
        _draft.Specification = Specification.Trim();
        _draft.AsmeSpecification = Specification.Trim();
        _draft.Grade = Grade.Trim();
        _draft.ProductForm = ProductForm.Trim();
        _draft.NominalComposition = NominalComposition.Trim();
        _draft.ClassConditionTempering = ClassConditionTempering.Trim();
        _draft.AlloyIdentificationUns = AlloyIdentificationUns.Trim();
        _draft.Family = SelectedFamily.Value;
        _draft.ElongationLongitudinalPercent = elongationLongitudinal;
        _draft.ElongationTransversePercent = elongationTransverse;
        _draft.ReductionOfAreaPercent = reductionOfArea;
        _draft.SpecifiedMinimumYieldStrength = smys;
        _draft.SpecifiedMinimumUltimateStrength = smuts;
        _draft.Notes = Notes;

        // ToMaterial recomposes Name from the identity parts and stamps LastModified.
        material = _draft.ToMaterial();
        return true;
    }

    /// <summary>Formats a numeric field for display using the invariant culture.</summary>
    /// <param name="value">Value to format.</param>
    /// <returns>Round-trippable text matching what <see cref="TryParse"/> accepts.</returns>
    private static string Format(double value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Parses a numeric field using the invariant culture.</summary>
    /// <param name="text">Text entered by the user.</param>
    /// <param name="value">Receives the parsed value, or <c>0</c> on failure.</param>
    /// <returns><c>true</c> when the text was a valid number.</returns>
    private static bool TryParse(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value);

    /// <summary>Formats an optional numeric field, rendering "not reported" as an empty box.</summary>
    /// <param name="value">Optional value read from the domain.</param>
    /// <returns>Round-trippable text, or the empty string when the value is absent.</returns>
    /// <remarks>
    /// Showing a blank rather than <c>0</c> matters: zero elongation would be a real measurement,
    /// while the ASME reference tables simply do not report the value for most materials.
    /// </remarks>
    private static string FormatOptional(FSharpOption<double>? value) =>
        value is null ? string.Empty : Format(value.Value);

    /// <summary>Parses an optional numeric field, accepting blank as "not reported".</summary>
    /// <param name="text">Text entered by the user.</param>
    /// <param name="value">Receives the parsed value, or <c>null</c> when the box is blank.</param>
    /// <returns><c>true</c> when the text was blank or a valid number.</returns>
    private static bool TryParseOptional(string? text, out double? value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = null;
            return true;
        }

        if (TryParse(text, out var parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }
}
