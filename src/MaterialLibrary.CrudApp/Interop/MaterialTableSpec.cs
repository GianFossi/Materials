using System.Globalization;
using MaterialLibrary.Crud;
using MaterialLibrary.Domain;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace MaterialLibraryCrudApp.Interop;

/// <summary>One column of an editable material table.</summary>
/// <param name="Header">Column name shown in the grid.</param>
/// <param name="Unit">Unit of measure appended to the header; empty when dimensionless.</param>
/// <param name="IsOptional">
/// When <c>true</c>, a blank cell is valid and maps to the F# <c>None</c>; when <c>false</c>, a
/// blank cell fails validation.
/// </param>
/// <param name="IsText">
/// When <c>true</c>, the cell holds free text and is not parsed as a number.
/// </param>
public sealed record MaterialTableColumn(string Header, string Unit, bool IsOptional = false, bool IsText = false)
{
    /// <summary>Header text including the unit, e.g. <c>Temperature (degC)</c>.</summary>
    public string DisplayHeader =>
        string.IsNullOrEmpty(Unit) ? Header : $"{Header} ({Unit})";
}

/// <summary>
/// Describes one editable table inside a <see cref="Material"/>: its columns, how to read its rows
/// out of a material, and how to write edited rows back.
/// </summary>
/// <remarks>
/// <para>
/// A single generic editor is driven by these descriptors rather than hand-writing a window per
/// table. Rows are carried as <c>string?[]</c> so one editor covers numeric and text tables alike;
/// a <c>null</c> cell means the F# <c>None</c> for optional columns.
/// </para>
/// <para>
/// The row view model validates every numeric cell before <see cref="Write"/> runs, which is why the
/// parse helpers used inside a spec can assume success.
/// </para>
/// <para>
/// <see cref="Write"/> always delegates to the F# helpers in <c>MaterialLibrary.Crud</c>
/// (<c>PhysicalPropertyCrud</c>, <c>StrengthPropertyCrud</c>, <c>SpecialPropertyCrud</c>), so domain
/// rules such as sorting by temperature and refreshing <c>LastModified</c> stay in the library and
/// are not reimplemented here.
/// </para>
/// </remarks>
public sealed class MaterialTableSpec
{
    /// <summary>Creates a table descriptor.</summary>
    /// <param name="title">Table name shown to the user.</param>
    /// <param name="columns">Column definitions, in grid order.</param>
    /// <param name="read">Extracts the current rows from a material.</param>
    /// <param name="write">Returns a new material with the given rows replacing the table.</param>
    public MaterialTableSpec(
        string title,
        IReadOnlyList<MaterialTableColumn> columns,
        Func<Material, IReadOnlyList<string?[]>> read,
        Func<Material, IReadOnlyList<string?[]>, Material> write)
    {
        Title = title;
        Columns = columns;
        Read = read;
        Write = write;
    }

    /// <summary>Table name shown to the user.</summary>
    public string Title { get; }

    /// <summary>Column definitions, in grid order.</summary>
    public IReadOnlyList<MaterialTableColumn> Columns { get; }

    /// <summary>Extracts the current rows from a material.</summary>
    public Func<Material, IReadOnlyList<string?[]>> Read { get; }

    /// <summary>Returns a new material with the given rows replacing the table.</summary>
    public Func<Material, IReadOnlyList<string?[]>, Material> Write { get; }

    /// <inheritdoc />
    public override string ToString() => Title;
}

/// <summary>Registry of the material tables the application can edit.</summary>
/// <remarks>
/// <para>
/// Covers every table in <see cref="Material"/> whose rows are flat, meaning one row of scalar
/// values. The tables that wrap a nested <c>Points</c> list plus per-table metadata - stress-strain,
/// creep, stress-rupture, external pressure, cyclic strain, fatigue, Larson-Miller - do not fit this
/// one-grid shape and are not registered here; they are preserved untouched through
/// <see cref="MaterialDraft"/> and the serializers.
/// </para>
/// <para>
/// Units follow the project-wide fixed conventions: temperature in degC, stress and strength in MPa,
/// density in kg/m^3, specific heat in J/(kg*K), thermal conductivity in W/(m*K), thermal expansion
/// coefficient in 1/degC, elongation and reduction of area in percent, time in hours.
/// </para>
/// </remarks>
public static class MaterialTableSpecs
{
    /// <summary>Standard temperature column shared by every table.</summary>
    private static readonly MaterialTableColumn Temperature = new("Temperature", "degC");

    /// <summary>Every table exposed by the generic editor.</summary>
    public static IReadOnlyList<MaterialTableSpec> All { get; } =
    [
        ThermalExpansion(),
        ElasticModulus(),
        Density(),
        SpecificHeat(),
        ThermalConductivity(),
        ThermalDiffusivity(),
        CompressionProperties(),
        NortonModels(),
        GarofaloModels(),
        KachanovOmegaModels(),
        CodeCase2964Constants(),
    ];

    // ── Cell conversion helpers ───────────────────────────────────────────────

    /// <summary>Formats a number for display, round-trippably and culture-independently.</summary>
    /// <param name="value">Value to format.</param>
    /// <returns>Invariant-culture text.</returns>
    private static string Num(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>Formats an optional number, producing a blank cell for <c>None</c>.</summary>
    /// <param name="value">Optional value read from the domain.</param>
    /// <returns>Invariant-culture text, or <c>null</c> for a blank cell.</returns>
    private static string? Num(FSharpOption<double>? value) =>
        value is null ? null : Num(value.Value);

    /// <summary>Parses a validated numeric cell.</summary>
    /// <param name="row">Row cells.</param>
    /// <param name="index">Column index.</param>
    /// <returns>The parsed value.</returns>
    /// <remarks>
    /// Safe because <c>TableRowViewModel.TryParse</c> has already rejected any row whose required
    /// numeric cells are blank or unparsable.
    /// </remarks>
    private static double D(string?[] row, int index) =>
        double.Parse(row[index]!, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>Parses an optional numeric cell into an F# option.</summary>
    /// <param name="row">Row cells.</param>
    /// <param name="index">Column index.</param>
    /// <returns><c>Some value</c>, or <c>None</c> for a blank cell.</returns>
    private static FSharpOption<double> DOpt(string?[] row, int index) =>
        string.IsNullOrWhiteSpace(row[index])
            ? FSharpOption<double>.None
            : FSharpOption<double>.Some(D(row, index));

    /// <summary>Reads a text cell, substituting the empty string for a blank.</summary>
    /// <param name="row">Row cells.</param>
    /// <param name="index">Column index.</param>
    /// <returns>The cell text, never <c>null</c>.</returns>
    private static string S(string?[] row, int index) => row[index] ?? string.Empty;

    /// <summary>Reads an optional text cell into an F# option.</summary>
    /// <param name="row">Row cells.</param>
    /// <param name="index">Column index.</param>
    /// <returns><c>Some text</c>, or <c>None</c> for a blank cell.</returns>
    private static FSharpOption<string> SOpt(string?[] row, int index) =>
        FSharpInterop.ToOption(row[index]) ?? FSharpOption<string>.None;

    /// <summary>
    /// Wraps a sequence as an F# <c>list option</c>, mapping an empty sequence to <c>None</c>.
    /// </summary>
    /// <typeparam name="T">Row type.</typeparam>
    /// <param name="rows">Rows to wrap.</param>
    /// <returns><c>Some rows</c>, or <c>None</c> when there are no rows.</returns>
    /// <remarks>
    /// Several domain tables are <c>'T list option</c>, where <c>None</c> means "no data recorded"
    /// and <c>Some []</c> would mean "recorded as empty". Clearing every row in the editor is the
    /// former, so an empty grid must collapse to <c>None</c>.
    /// </remarks>
    private static FSharpOption<FSharpList<T>> OptionalList<T>(IEnumerable<T> rows)
    {
        var list = rows.ToList();
        return list.Count == 0
            ? FSharpOption<FSharpList<T>>.None
            : FSharpOption<FSharpList<T>>.Some(list.ToFSharpList());
    }

    // ── Physical property tables ──────────────────────────────────────────────

    /// <summary>Mean coefficient of linear thermal expansion vs temperature.</summary>
    private static MaterialTableSpec ThermalExpansion() => new(
        "Thermal expansion",
        [Temperature, new MaterialTableColumn("Expansion coefficient", "1/degC")],
        material => material.PhysicalProperties.ThermalExpansionTable
            .ToReadOnlyList()
            .Select(row => new string?[] { Num(row.Temperature), Num(row.ExpansionCoefficient) })
            .ToList(),
        (material, rows) => PhysicalPropertyCrud.setThermalExpansion(
            rows.Select(r => new ThermalExpansionTablePoint(D(r, 0), D(r, 1))).ToFSharpList(),
            material));

    /// <summary>Young's modulus and optional Poisson's ratio vs temperature.</summary>
    private static MaterialTableSpec ElasticModulus() => new(
        "Elastic modulus",
        [
            Temperature,
            new MaterialTableColumn("Elastic modulus E", "MPa"),
            new MaterialTableColumn("Poisson ratio", string.Empty, IsOptional: true),
        ],
        material => material.PhysicalProperties.ElasticModulusTable
            .ToReadOnlyList()
            .Select(row => new string?[] { Num(row.Temperature), Num(row.ElasticModulus), Num(row.PoissonRatio) })
            .ToList(),
        (material, rows) => PhysicalPropertyCrud.setElasticModulus(
            rows.Select(r => new ElasticModulusTablePoint(D(r, 0), D(r, 1), DOpt(r, 2))).ToFSharpList(),
            material));

    /// <summary>Mass density vs temperature.</summary>
    private static MaterialTableSpec Density() => new(
        "Density",
        [Temperature, new MaterialTableColumn("Density", "kg/m^3")],
        material => material.PhysicalProperties.DensityTable
            .ToReadOnlyList()
            .Select(row => new string?[] { Num(row.Temperature), Num(row.Density) })
            .ToList(),
        (material, rows) => PhysicalPropertyCrud.setDensity(
            rows.Select(r => new DensityTablePoint(D(r, 0), D(r, 1))).ToFSharpList(),
            material));

    /// <summary>Specific heat vs temperature. The whole table is optional in the domain.</summary>
    private static MaterialTableSpec SpecificHeat() => new(
        "Specific heat",
        [Temperature, new MaterialTableColumn("Specific heat Cp", "J/(kg*K)")],
        material => material.PhysicalProperties.SpecificHeatTable
            .AsNullableRef()
            .ToReadOnlyList()
            .Select(row => new string?[] { Num(row.Temperature), Num(row.SpecificHeat) })
            .ToList(),
        (material, rows) => PhysicalPropertyCrud.setSpecificHeat(
            OptionalList(rows.Select(r => new SpecificHeatTablePoint(D(r, 0), D(r, 1)))),
            material));

    /// <summary>Thermal conductivity vs temperature, stored as plain pairs. Optional in the domain.</summary>
    private static MaterialTableSpec ThermalConductivity() => new(
        "Thermal conductivity",
        [Temperature, new MaterialTableColumn("Conductivity k", "W/(m*K)")],
        material => material.PhysicalProperties.ThermalConductivityTable
            .AsNullableRef()
            .ToReadOnlyList()
            .Select(pair => new string?[] { Num(pair.Item1), Num(pair.Item2) })
            .ToList(),
        (material, rows) => PhysicalPropertyCrud.setThermalConductivity(
            OptionalList(rows.Select(r => Tuple.Create(D(r, 0), D(r, 1)))),
            material));

    /// <summary>Thermal diffusivity vs temperature.</summary>
    /// <remarks>
    /// Grouped with specific heat and thermal conductivity: the three describe the same heat-transfer
    /// behaviour and ASME publishes them together per material group. Stored in coherent SI (m^2/s),
    /// while the reference database publishes mm^2/s.
    /// </remarks>
    private static MaterialTableSpec ThermalDiffusivity() => new(
        "Thermal diffusivity",
        [Temperature, new MaterialTableColumn("Diffusivity a", "m^2/s")],
        material => material.PhysicalProperties.ThermalDiffusivityTable
            .AsNullableRef()
            .ToReadOnlyList()
            .Select(pair => new string?[] { Num(pair.Item1), Num(pair.Item2) })
            .ToList(),
        (material, rows) => PhysicalPropertyCrud.setThermalDiffusivity(
            OptionalList(rows.Select(r => Tuple.Create(D(r, 0), D(r, 1)))),
            material));

    // ── Strength property tables ──────────────────────────────────────────────

    /// <summary>Compressive strength and yield vs temperature. Optional in the domain.</summary>
    private static MaterialTableSpec CompressionProperties() => new(
        "Compression properties",
        [
            Temperature,
            new MaterialTableColumn("Compressive strength", "MPa"),
            new MaterialTableColumn("Compressive yield", "MPa"),
        ],
        material => material.StrengthProperties.CompressionProperties
            .AsNullableRef()
            .ToReadOnlyList()
            .Select(row => new string?[]
            {
                Num(row.Temperature), Num(row.CompressiveStrength), Num(row.CompressiveYield),
            })
            .ToList(),
        (material, rows) => StrengthPropertyCrud.setCompressionProperties(
            OptionalList(rows.Select(r => new MaterialLibrary.Domain.CompressionProperties(
                D(r, 0), D(r, 1), D(r, 2)))),
            material));

    // ── Creep model coefficients ──────────────────────────────────────────────

    /// <summary>Norton power-law creep coefficients, one row per temperature.</summary>
    private static MaterialTableSpec NortonModels() => new(
        "Creep: Norton power law",
        [
            Temperature,
            new MaterialTableColumn("A (coefficient)", string.Empty),
            new MaterialTableColumn("n (stress exponent)", string.Empty),
            new MaterialTableColumn("m (time exponent)", string.Empty),
        ],
        material => material.StrengthProperties.NortonModels
            .ToReadOnlyList()
            .Select(row => new string?[] { Num(row.Temperature), Num(row.A), Num(row.N), Num(row.M) })
            .ToList(),
        (material, rows) => StrengthPropertyCrud.setNortonModels(
            rows.Select(r => new NortonPowerLawCoefficients(D(r, 0), D(r, 1), D(r, 2), D(r, 3))).ToFSharpList(),
            material));

    /// <summary>Garofalo (hyperbolic sine) creep coefficients, one row per temperature.</summary>
    private static MaterialTableSpec GarofaloModels() => new(
        "Creep: Garofalo",
        [
            Temperature,
            new MaterialTableColumn("A (coefficient)", string.Empty),
            new MaterialTableColumn("n (stress exponent)", string.Empty),
            new MaterialTableColumn("m (time exponent)", string.Empty),
            new MaterialTableColumn("alpha", "1/MPa"),
            new MaterialTableColumn("Q (activation energy)", "J/mol"),
        ],
        material => material.StrengthProperties.GarofaloModels
            .ToReadOnlyList()
            .Select(row => new string?[]
            {
                Num(row.Temperature), Num(row.A), Num(row.N), Num(row.M), Num(row.Alpha), Num(row.Q),
            })
            .ToList(),
        (material, rows) => StrengthPropertyCrud.setGarofaloModels(
            rows.Select(r => new GarofaloCoefficients(
                D(r, 0), D(r, 1), D(r, 2), D(r, 3), D(r, 4), D(r, 5))).ToFSharpList(),
            material));

    /// <summary>Kachanov-Rabotnov omega creep-damage coefficients, one row per temperature.</summary>
    private static MaterialTableSpec KachanovOmegaModels() => new(
        "Creep: Kachanov omega",
        [
            Temperature,
            new MaterialTableColumn("A1 (creep coefficient)", string.Empty),
            new MaterialTableColumn("n1 (stress exponent)", string.Empty),
            new MaterialTableColumn("m1 (omega exponent)", string.Empty),
            new MaterialTableColumn("A2 (damage coefficient)", string.Empty),
            new MaterialTableColumn("n2 (stress exponent)", string.Empty),
            new MaterialTableColumn("m2 (omega exponent)", string.Empty),
            new MaterialTableColumn("Description", string.Empty, IsOptional: true, IsText: true),
        ],
        material => material.StrengthProperties.KachanovOmegaModels
            .ToReadOnlyList()
            .Select(row => new string?[]
            {
                Num(row.Temperature),
                Num(row.A1), Num(row.N1), Num(row.M1),
                Num(row.A2), Num(row.N2), Num(row.M2),
                row.Description,
            })
            .ToList(),
        (material, rows) => StrengthPropertyCrud.setKachanovOmegaModels(
            rows.Select(r => new KachanovOmegaModel(
                D(r, 0), D(r, 1), D(r, 2), D(r, 3), D(r, 4), D(r, 5), D(r, 6), S(r, 7))).ToFSharpList(),
            material));

    // ── Special properties ────────────────────────────────────────────────────

    /// <summary>ASME Code Case 2964 Appendix III A and B constants, one row per temperature.</summary>
    private static MaterialTableSpec CodeCase2964Constants() => new(
        "Code Case 2964 Appendix III",
        [
            Temperature,
            new MaterialTableColumn("A0", string.Empty),
            new MaterialTableColumn("A1", string.Empty),
            new MaterialTableColumn("A2", string.Empty),
            new MaterialTableColumn("A3", string.Empty),
            new MaterialTableColumn("A4", string.Empty),
            new MaterialTableColumn("B0", string.Empty),
            new MaterialTableColumn("B1", string.Empty),
            new MaterialTableColumn("B2", string.Empty),
            new MaterialTableColumn("B3", string.Empty),
            new MaterialTableColumn("B4", string.Empty),
            new MaterialTableColumn("Notes", string.Empty, IsOptional: true, IsText: true),
        ],
        material => material.SpecialProperties.AppendixIIIConstants
            .ToReadOnlyList()
            .Select(row => new string?[]
            {
                Num(row.Temperature),
                Num(row.A0), Num(row.A1), Num(row.A2), Num(row.A3), Num(row.A4),
                Num(row.B0), Num(row.B1), Num(row.B2), Num(row.B3), Num(row.B4),
                row.Notes.AsNullable(),
            })
            .ToList(),
        (material, rows) => SpecialPropertyCrud.setCodeCase2964AppendixIIIConstants(
            rows.Select(r => new CodeCase2964AppendixIIIConstants(
                D(r, 0),
                D(r, 1), D(r, 2), D(r, 3), D(r, 4), D(r, 5),
                D(r, 6), D(r, 7), D(r, 8), D(r, 9), D(r, 10),
                SOpt(r, 11))).ToFSharpList(),
            material));
}
