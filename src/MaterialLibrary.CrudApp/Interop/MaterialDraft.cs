using MaterialLibrary.Domain;
using Microsoft.FSharp.Core;

namespace MaterialLibraryCrudApp.Interop;

/// <summary>
/// Complete mutable mirror of the immutable F# <see cref="Material"/> record.
/// </summary>
/// <remarks>
/// <para>
/// This is the single sanctioned way to edit a material from C#. Loading a record into a draft,
/// mutating fields freely, and calling <see cref="ToMaterial"/> once replaces the pattern of
/// chaining many <c>With*</c> copy helpers, each of which allocated an intermediate record and
/// stamped <c>LastModified</c> again.
/// </para>
/// <para>
/// Mirror-integrity rules, which exist to stop the two representations drifting apart:
/// </para>
/// <list type="number">
/// <item>
/// Every field of the F# record is represented here exactly once. <see cref="ToMaterial"/> is the
/// only call site of the record's 23-argument positional constructor, so adding a field to the F#
/// record is a compile error here rather than a silent data loss elsewhere.
/// </item>
/// <item>
/// Option-typed fields are mirrored as nullable C# values, never as <c>FSharpOption</c>. F#
/// represents <c>None</c> as a null reference, so round-tripping through nullable C# is lossless
/// and removes any chance of a <c>??</c> fallback silently resurrecting a cleared value.
/// </item>
/// <item>
/// Fields the editor does not yet expose (the large nested property tables) are carried through
/// verbatim as their F# values. They are still mirrored - dropping them would destroy data on save.
/// </item>
/// <item>
/// <see cref="ToMaterial"/> is pure apart from the <c>LastModified</c> stamp: it never mutates the
/// draft, so a failed validation upstream leaves the draft reusable.
/// </item>
/// </list>
/// </remarks>
public sealed class MaterialDraft
{
    /// <summary>Creates an empty draft with domain-consistent defaults.</summary>
    public MaterialDraft()
    {
        var seed = MaterialFactory.CreateNew(
            string.Empty,
            string.Empty,
            string.Empty,
            MaterialFactory.CreateBasicProperties(0, 0, 0, 0));

        // Reuse the domain's own empty-material shape so defaults stay in one place.
        PhysicalProperties = seed.PhysicalProperties;
        StrengthProperties = seed.StrengthProperties;
        SpecialProperties = seed.SpecialProperties;
        AllowableStressLevel = seed.AllowableStressLevel;
        ApplicableAsmeCodes = seed.ApplicableAsmeCodes.ToReadOnlyList().ToList();
        AsmeNoteReferences = seed.AsmeNoteReferences.ToReadOnlyList().ToList();
        CreatedDate = seed.CreatedDate;
        LastModified = seed.LastModified;
    }

    /// <summary>Creates a draft mirroring an existing material.</summary>
    /// <param name="material">Record to mirror; not modified.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="material"/> is <c>null</c>.</exception>
    public MaterialDraft(Material material)
    {
        ArgumentNullException.ThrowIfNull(material);

        Id = material.Id;
        Name = material.Name;
        ProductForm = material.ProductForm;
        NominalComposition = material.NominalComposition;
        Specification = material.Specification;
        AsmeSpecification = material.ASMESpecification;
        Grade = material.Grade;
        ClassConditionTempering = material.Class_Condition_Tempering;
        AlloyIdentificationUns = material.AlloyIdentification_UNS;

        // Options -> nullable C#.
        Family = material.Family.AsNullableRef();
        TimeDependingStartTemperature = material.TimeDepenedingStartTemperature.AsNullable();
        Notes = material.Notes.AsNullable();

        AllowableStressLevel = material.AllowableStressLevel;
        ApplicableAsmeCodes = material.ApplicableAsmeCodes.ToReadOnlyList().ToList();
        AsmeNoteReferences = material.AsmeNoteReferences.ToReadOnlyList().ToList();

        // Flattened scalar sub-records.
        ElongationPercent = material.BasicProperties.ElongationPercent;
        ReductionOfAreaPercent = material.BasicProperties.ReductionOfAreaPercent;
        SpecifiedMinimumYieldStrength = material.BasicProperties.SpecifiedMinimumYieldStrength;
        SpecifiedMinimumUltimateStrength = material.BasicProperties.SpecifiedMinimumUltimateStrength;

        MaxTemperatureAsmeViiiI = material.MaximumAllowableTemperature.AsmeViiiI.AsNullable();
        MaxTemperatureAsmeViii1 = material.MaximumAllowableTemperature.AsmeViii1.AsNullable();
        MaxTemperatureAsmeViii2 = material.MaximumAllowableTemperature.AsmeViii2.AsNullable();

        var welding = material.WeldingInfo.AsNullableRef();
        WeldingPNumber = welding?.PNumber;
        WeldingGNumber = welding?.GNumber;

        // Carried through verbatim: large nested tables the editor does not yet expose.
        PhysicalProperties = material.PhysicalProperties;
        StrengthProperties = material.StrengthProperties;
        SpecialProperties = material.SpecialProperties;

        CreatedDate = material.CreatedDate;
        LastModified = material.LastModified;
    }

    // ---------- Identity ----------

    /// <summary>Unique repository key.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Composed full name; recomputed by <see cref="ToMaterial"/> from the identity parts.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Product form (e.g. plate, pipe, forging).</summary>
    public string ProductForm { get; set; } = string.Empty;

    /// <summary>Nominal composition text (e.g. <c>2 1/4Cr-1Mo</c>).</summary>
    public string NominalComposition { get; set; } = string.Empty;

    /// <summary>Material specification used for identification.</summary>
    public string Specification { get; set; } = string.Empty;

    /// <summary>ASME specification, kept for backward compatibility with existing JSON naming.</summary>
    public string AsmeSpecification { get; set; } = string.Empty;

    /// <summary>Material grade designation.</summary>
    public string Grade { get; set; } = string.Empty;

    /// <summary>Class, condition, or tempering designation.</summary>
    public string ClassConditionTempering { get; set; } = string.Empty;

    /// <summary>UNS alloy identifier.</summary>
    public string AlloyIdentificationUns { get; set; } = string.Empty;

    /// <summary>ASME database family classification, or <c>null</c> when unassigned.</summary>
    public AsmeMaterialFamily? Family { get; set; }

    // ---------- Basic properties (units fixed a priori) ----------

    /// <summary>Minimum elongation at fracture (%).</summary>
    public double ElongationPercent { get; set; }

    /// <summary>Minimum reduction of area at fracture (%).</summary>
    public double ReductionOfAreaPercent { get; set; }

    /// <summary>Specified Minimum Yield Strength, SMYS (MPa).</summary>
    public double SpecifiedMinimumYieldStrength { get; set; }

    /// <summary>Specified Minimum Ultimate Tensile Strength, SMUTS (MPa).</summary>
    public double SpecifiedMinimumUltimateStrength { get; set; }

    // ---------- Code design metadata ----------

    /// <summary>Standard or high allowable-stress curve selection.</summary>
    public MaterialAllowableStressLevel AllowableStressLevel { get; set; } = MaterialAllowableStressLevel.StandardAllowableStress;

    /// <summary>ASME construction codes with allowable-stress data available.</summary>
    public List<AsmeCode> ApplicableAsmeCodes { get; set; } = [];

    /// <summary>ASME Section II-D note references attached to material-level data.</summary>
    public List<AsmeNoteReference> AsmeNoteReferences { get; set; } = [];

    /// <summary>Maximum allowable temperature for ASME VIII-I (degC), or <c>null</c>.</summary>
    public double? MaxTemperatureAsmeViiiI { get; set; }

    /// <summary>Maximum allowable temperature for ASME VIII-1 (degC), or <c>null</c>.</summary>
    public double? MaxTemperatureAsmeViii1 { get; set; }

    /// <summary>Maximum allowable temperature for ASME VIII-2 (degC), or <c>null</c>.</summary>
    public double? MaxTemperatureAsmeViii2 { get; set; }

    /// <summary>Temperature where time-dependent behaviour starts (degC), or <c>null</c>.</summary>
    public double? TimeDependingStartTemperature { get; set; }

    /// <summary>ASME P-Number, or <c>null</c> when no welding info is recorded.</summary>
    public string? WeldingPNumber { get; set; }

    /// <summary>ASME Group Number, or <c>null</c> when no welding info is recorded.</summary>
    public string? WeldingGNumber { get; set; }

    // ---------- Nested property tables (carried through verbatim for now) ----------

    /// <summary>Temperature-dependent physical properties.</summary>
    public PhysicalPropertiesTable PhysicalProperties { get; set; }

    /// <summary>Tensile, compressive, allowable-stress, creep, and fatigue data.</summary>
    public StrengthProperties StrengthProperties { get; set; }

    /// <summary>ASME Code Case 2964 data.</summary>
    public SpecialProperties SpecialProperties { get; set; }

    // ---------- Timestamps ----------

    /// <summary>UTC timestamp when the record was first created; preserved across edits.</summary>
    public DateTime CreatedDate { get; set; }

    /// <summary>UTC timestamp of the most recent modification; refreshed by <see cref="ToMaterial"/>.</summary>
    public DateTime LastModified { get; set; }

    /// <summary>Free-text notes; <c>null</c> or blank becomes <c>None</c>.</summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Materialises the draft back into an immutable <see cref="Material"/> record.
    /// </summary>
    /// <param name="recomposeName">
    /// When <c>true</c> (the default), <c>Name</c> is recomputed by the domain from Specification,
    /// Grade, Class/Condition/Tempering, and UNS. Pass <c>false</c> to keep <see cref="Name"/> as set.
    /// </param>
    /// <returns>A new record carrying every mirrored field, stamped with the current UTC time.</returns>
    /// <remarks>
    /// The only call site of the 23-argument positional constructor. Argument order is verified
    /// against the F# record definition; a field added there breaks this call rather than shifting
    /// values silently into neighbouring parameters.
    /// </remarks>
    public Material ToMaterial(bool recomposeName = true)
    {
        var composed = recomposeName
            ? MaterialModule.composeMaterialName(Specification, Grade, ClassConditionTempering, AlloyIdentificationUns)
            : Name;

        // An all-blank identity composes to an empty string; fall back so Name is never blank.
        var resolvedName = string.IsNullOrWhiteSpace(composed)
            ? (string.IsNullOrWhiteSpace(Name) ? Id : Name)
            : composed;

        var weldingInfo = BuildWeldingInfo();

        return new Material(
            Id,
            resolvedName,
            ProductForm,
            NominalComposition,
            Specification,
            // The domain's own create/setIdentity keep ASMESpecification in step with Specification;
            // mirror that when the draft carries no distinct value.
            string.IsNullOrWhiteSpace(AsmeSpecification) ? Specification : AsmeSpecification,
            Grade,
            ClassConditionTempering,
            AlloyIdentificationUns,
            Family is null ? FSharpOption<AsmeMaterialFamily>.None : FSharpOption<AsmeMaterialFamily>.Some(Family),
            AllowableStressLevel,
            ApplicableAsmeCodes.ToFSharpList(),
            AsmeNoteReferences.ToFSharpList(),
            MaterialFactory.CreateBasicProperties(
                ElongationPercent,
                ReductionOfAreaPercent,
                SpecifiedMinimumYieldStrength,
                SpecifiedMinimumUltimateStrength),
            PhysicalProperties,
            StrengthProperties,
            SpecialProperties,
            new MaximumAllowableTemperature(
                FSharpInterop.ToOption(MaxTemperatureAsmeViiiI),
                FSharpInterop.ToOption(MaxTemperatureAsmeViii1),
                FSharpInterop.ToOption(MaxTemperatureAsmeViii2)),
            FSharpInterop.ToOption(TimeDependingStartTemperature),
            weldingInfo,
            CreatedDate,
            DateTime.UtcNow,
            FSharpInterop.ToOption(Notes));
    }

    /// <summary>Builds the optional welding-info sub-record from the mirrored P and G numbers.</summary>
    /// <returns><c>Some</c> welding info when either number is present, otherwise <c>None</c>.</returns>
    /// <remarks>
    /// The F# record has non-optional string fields inside an optional record, so a half-filled pair
    /// is represented by substituting empty strings rather than by dropping the record entirely.
    /// </remarks>
    private FSharpOption<WeldingInfo> BuildWeldingInfo()
    {
        var hasP = !string.IsNullOrWhiteSpace(WeldingPNumber);
        var hasG = !string.IsNullOrWhiteSpace(WeldingGNumber);

        if (!hasP && !hasG)
        {
            return FSharpOption<WeldingInfo>.None;
        }

        return FSharpOption<WeldingInfo>.Some(
            new WeldingInfo(WeldingPNumber?.Trim() ?? string.Empty, WeldingGNumber?.Trim() ?? string.Empty));
    }
}
