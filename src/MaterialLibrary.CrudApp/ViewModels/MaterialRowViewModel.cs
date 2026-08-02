using MaterialLibraryCrudApp.Interop;
using MaterialLibrary.Domain;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>Read-only projection of a <see cref="Material"/> for one row of the materials grid.</summary>
/// <remarks>
/// <para>
/// The grid does not bind to the F# <see cref="Material"/> record directly, for two reasons.
/// First, option-typed fields would render through <c>ToString</c> as <c>Some(...)</c> instead of
/// the underlying text. Second, binding to the domain record would tie column paths to the domain
/// shape, so any field rename would break silently at runtime instead of at compile time.
/// </para>
/// <para>
/// The projection keeps a reference to the originating record so the selected row can be turned
/// back into a domain value without a repository round-trip.
/// </para>
/// </remarks>
public sealed class MaterialRowViewModel
{
    /// <summary>Creates a row projection.</summary>
    /// <param name="material">Domain record to project; retained unmodified.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="material"/> is <c>null</c>.</exception>
    public MaterialRowViewModel(Material material)
    {
        Material = material ?? throw new ArgumentNullException(nameof(material));
    }

    /// <summary>The underlying immutable domain record.</summary>
    internal Material Material { get; }

    // ---------- Material identification (the columns shown in the grid) ----------

    /// <summary>Unique repository key (e.g. <c>SA-106-B</c>).</summary>
    public string Id => Material.Id;

    /// <summary>Material specification used for identification (e.g. <c>ASME SA-106</c>).</summary>
    public string Specification => Material.Specification;

    /// <summary>Material grade designation (e.g. <c>B</c>).</summary>
    public string Grade => Material.Grade;

    /// <summary>Class, condition, or tempering designation.</summary>
    public string ClassConditionTempering => Material.Class_Condition_Tempering;

    /// <summary>UNS alloy identifier.</summary>
    public string AlloyIdentificationUns => Material.AlloyIdentification_UNS;

    /// <summary>Product form (e.g. plate, pipe, forging).</summary>
    public string ProductForm => Material.ProductForm;

    /// <summary>
    /// Product analysis: the nominal chemical composition text (e.g. <c>2 1/4Cr-1Mo</c>).
    /// </summary>
    /// <remarks>
    /// Backed by the domain's <c>NominalComposition</c>, which is the only composition field on
    /// <see cref="Material"/>. Strictly, an ASME product analysis is the chemistry measured on the
    /// finished product and is not the same thing as a nominal composition; the domain would need a
    /// separate field to distinguish them.
    /// </remarks>
    public string ProductAnalysis => Material.NominalComposition;

    /// <summary>
    /// ASME database family classification code (e.g. <c>CS</c>, <c>LAS1.00</c>, <c>SSD+</c>),
    /// or an empty cell when the material has no family assigned.
    /// </summary>
    /// <remarks>
    /// Two F# constructs stack here: an <c>option</c> wrapping a discriminated union. The option is
    /// unwrapped first (<c>None</c> is null), then the domain's own <c>AsmeMaterialFamily.code</c>
    /// converts the union case to its display code. Calling <c>ToString()</c> on the case instead
    /// would print the F# identifier (<c>LAS1_00</c>, <c>SSDPlus</c>) rather than the ASME code.
    /// </remarks>
    public string Family
    {
        get
        {
            var family = Material.Family.AsNullableRef();
            return family is null ? string.Empty : AsmeMaterialFamilyModule.code(family);
        }
    }

    /// <summary>
    /// Full name, composed by the domain as Specification + Grade + Class/Condition/Tempering + UNS.
    /// </summary>
    public string Name => Material.Name;

    // ---------- Not shown as columns, but used by the view ----------

    /// <summary>Timestamp of the last modification (UTC).</summary>
    public DateTime LastModified => Material.LastModified;

    /// <summary>Free-text notes, flattened from the F# option to a nullable string.</summary>
    public string? Notes => Material.Notes.AsNullable();
}
