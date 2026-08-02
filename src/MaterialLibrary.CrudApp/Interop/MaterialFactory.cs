using MaterialLibrary.Domain;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace MaterialLibraryCrudApp.Interop;

/// <summary>
/// Construction and non-destructive update helpers for the immutable F# <see cref="Material"/> record.
/// </summary>
/// <remarks>
/// <para>
/// F# records are immutable and their properties are get-only, so C# cannot mutate a
/// <see cref="Material"/> in place and has no equivalent of the F# copy-and-update expression
/// <c>{ material with Notes = ... }</c>. The compiled record does expose a positional constructor
/// taking every field, which is what the <c>With*</c> helpers below use to emulate copy-and-update.
/// </para>
/// <para>
/// Keeping that constructor call in exactly one place matters: it is positional and 23 fields wide,
/// so any field added to the F# record breaks this file at compile time — which is the desired
/// behaviour — rather than silently mis-assigning values across call sites.
/// </para>
/// <para>
/// Where the F# <c>Material</c> module already provides a setter (for example
/// <c>Material.setIdentity</c>), prefer it: those helpers also recompute derived state such as the
/// composed <c>Name</c>. Note that F# module functions compile to a class named
/// <c>MaterialModule</c>, not <c>Material</c>, because a type of that name already occupies the
/// namespace; and the record argument comes <b>last</b>, mirroring F# pipeline order.
/// </para>
/// </remarks>
internal static class MaterialFactory
{
    /// <summary>Creates an empty physical-properties table for a brand-new material.</summary>
    /// <returns>
    /// A <see cref="PhysicalPropertiesTable"/> with no rows and the default thermal-expansion
    /// reference temperature of 20 degC applied by the domain.
    /// </returns>
    internal static PhysicalPropertiesTable CreateEmptyPhysicalProperties() =>
        PhysicalPropertiesTableModule.create(
            FSharpOption<double>.None,
            FSharpList<ThermalExpansionTablePoint>.Empty,
            FSharpList<ElasticModulusTablePoint>.Empty,
            FSharpOption<FSharpList<SpecificHeatTablePoint>>.None,
            FSharpList<DensityTablePoint>.Empty,
            FSharpOption<FSharpList<Tuple<double, double>>>.None);

    /// <summary>Creates minimum mechanical properties from the values entered in the editor.</summary>
    /// <param name="elongationPercent">Minimum elongation at fracture (%).</param>
    /// <param name="reductionOfAreaPercent">Minimum reduction of area at fracture (%).</param>
    /// <param name="specifiedMinimumYieldStrength">Specified Minimum Yield Strength, SMYS (MPa).</param>
    /// <param name="specifiedMinimumUltimateStrength">Specified Minimum Ultimate Tensile Strength, SMUTS (MPa).</param>
    /// <returns>A populated <see cref="BasicProperties"/> record.</returns>
    internal static BasicProperties CreateBasicProperties(
        double elongationPercent,
        double reductionOfAreaPercent,
        double specifiedMinimumYieldStrength,
        double specifiedMinimumUltimateStrength) =>
        BasicPropertiesModule.create(
            elongationPercent,
            reductionOfAreaPercent,
            specifiedMinimumYieldStrength,
            specifiedMinimumUltimateStrength);

    /// <summary>Creates a new material with empty property tables.</summary>
    /// <param name="id">Unique repository key; must be non-empty.</param>
    /// <param name="specification">ASME specification number (e.g. <c>"SA-516"</c>).</param>
    /// <param name="grade">Material grade or class (e.g. <c>"70"</c>).</param>
    /// <param name="basicProperties">Minimum mechanical properties (see <see cref="CreateBasicProperties"/> for units).</param>
    /// <returns>A material whose <c>Name</c> is composed by the domain from specification and grade.</returns>
    internal static Material CreateNew(
        string id,
        string specification,
        string grade,
        BasicProperties basicProperties) =>
        MaterialModule.create(
            id,
            id,
            specification,
            grade,
            basicProperties,
            CreateEmptyPhysicalProperties());

    /// <summary>
    /// Applies identity metadata, delegating to the domain so the composed <c>Name</c> and
    /// <c>LastModified</c> stamp stay consistent.
    /// </summary>
    /// <param name="material">Source material; left unchanged.</param>
    /// <param name="productForm">Product form (free text).</param>
    /// <param name="nominalComposition">Nominal composition (free text).</param>
    /// <param name="specification">Specification string used in the composed name.</param>
    /// <param name="grade">Grade string used in the composed name.</param>
    /// <param name="classConditionTempering">Class/condition/tempering string used in the composed name.</param>
    /// <param name="alloyIdentificationUns">UNS identifier used in the composed name.</param>
    /// <returns>A new material instance; the input is not modified.</returns>
    internal static Material WithIdentity(
        Material material,
        string productForm,
        string nominalComposition,
        string specification,
        string grade,
        string classConditionTempering,
        string alloyIdentificationUns) =>
        MaterialModule.setIdentity(
            productForm,
            nominalComposition,
            specification,
            grade,
            classConditionTempering,
            alloyIdentificationUns,
            material);

    /// <summary>Returns a copy of the material with replaced basic properties.</summary>
    /// <param name="material">Source material; left unchanged.</param>
    /// <param name="basicProperties">Replacement minimum mechanical properties.</param>
    /// <returns>A new material instance with a refreshed <c>LastModified</c> stamp (UTC).</returns>
    internal static Material WithBasicProperties(Material material, BasicProperties basicProperties) =>
        Copy(material, basicProperties, material.Notes);

    /// <summary>Returns a copy of the material with replaced free-text notes.</summary>
    /// <param name="material">Source material; left unchanged.</param>
    /// <param name="notes">Notes text; <c>null</c> or blank clears the field to <c>None</c>.</param>
    /// <returns>A new material instance with a refreshed <c>LastModified</c> stamp (UTC).</returns>
    internal static Material WithNotes(Material material, string? notes) =>
        Copy(material, material.BasicProperties, FSharpInterop.ToOption(notes));

    /// <summary>
    /// Returns a copy of the material with replaced basic properties and notes in one step.
    /// </summary>
    /// <param name="material">Source material; left unchanged.</param>
    /// <param name="basicProperties">Replacement minimum mechanical properties.</param>
    /// <param name="notes">Notes text; <c>null</c> or blank clears the field to <c>None</c>.</param>
    /// <returns>A new material instance with a refreshed <c>LastModified</c> stamp (UTC).</returns>
    internal static Material WithBasicPropertiesAndNotes(
        Material material,
        BasicProperties basicProperties,
        string? notes) =>
        Copy(material, basicProperties, FSharpInterop.ToOption(notes));

    /// <summary>
    /// Emulates the F# copy-and-update expression by re-invoking the record's positional constructor.
    /// </summary>
    /// <param name="material">Source material to copy from.</param>
    /// <param name="basicProperties">Basic properties for the copy; pass the source's own value to keep it.</param>
    /// <param name="notes">Notes option for the copy (<c>null</c> means <c>None</c>); pass the source's own value to keep it.</param>
    /// <returns>A new material with <c>LastModified</c> set to the current UTC time.</returns>
    /// <remarks>
    /// Every replaceable field is a required parameter on purpose. The tempting alternative -
    /// optional parameters defaulting to <c>null</c> to mean "unchanged" - is silently wrong
    /// for option-typed fields, because F# represents <c>None</c> as a null reference: a caller
    /// clearing <c>Notes</c> would pass <c>None</c>, the null-coalescing fallback would fire, and
    /// the old notes would be retained instead of cleared.
    /// </remarks>
    private static Material Copy(
        Material material,
        BasicProperties basicProperties,
        FSharpOption<string>? notes) =>
        new(
            material.Id,
            material.Name,
            material.ProductForm,
            material.NominalComposition,
            material.Specification,
            material.ASMESpecification,
            material.Grade,
            material.Class_Condition_Tempering,
            material.AlloyIdentification_UNS,
            material.Family,
            material.AllowableStressLevel,
            material.ApplicableAsmeCodes,
            material.AsmeNoteReferences,
            basicProperties,
            material.PhysicalProperties,
            material.StrengthProperties,
            material.SpecialProperties,
            material.MaximumAllowableTemperature,
            material.TimeDepenedingStartTemperature,
            material.WeldingInfo,
            material.CreatedDate,
            DateTime.UtcNow,
            notes);
}
