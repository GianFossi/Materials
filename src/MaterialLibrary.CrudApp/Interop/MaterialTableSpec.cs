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
/// values, plus the two size-grouped strength tables, which are flattened to one row per point with
/// their group tags repeated and regrouped on commit.
/// </para>
/// <para>
/// The remaining tables wrap a nested <c>Points</c> list plus per-table metadata - stress-strain,
/// creep, stress-rupture, external pressure, cyclic strain, fatigue, Larson-Miller. They have their
/// own master/detail editors and are not registered here; they are preserved untouched through
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

    /// <summary>
    /// The four columns that carry a Size/Diameter/Thickness band, shared by every size-grouped table.
    /// </summary>
    /// <remarks>
    /// Both bounds are optional, meaning the band is open on that side. The inclusive flags are what
    /// keep adjacent ASME bands disjoint - "up to 5 incl." and "over 5" share a boundary - so they
    /// are editable rather than assumed. A blank flag reads as inclusive, which is how ASME prints an
    /// unqualified limit.
    /// </remarks>
    private static readonly IReadOnlyList<MaterialTableColumn> SizeBandColumns =
    [
        new MaterialTableColumn("Size/thk min", "mm", IsOptional: true),
        new MaterialTableColumn("Min incl.", "", IsOptional: true, IsText: true),
        new MaterialTableColumn("Size/thk max", "mm", IsOptional: true),
        new MaterialTableColumn("Max incl.", "", IsOptional: true, IsText: true),
    ];

    /// <summary>Every table exposed by the generic editor.</summary>
    public static IReadOnlyList<MaterialTableSpec> All { get; } =
    [
        ThermalExpansion(),
        ElasticModulus(),
        Density(),
        SpecificHeat(),
        ThermalConductivity(),
        ThermalDiffusivity(),
        TensileProperties(),
        TensileStrengthDatasets(),
        AllowableStressDatasets(),
        AllowableStresses(),
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

    // ── Size-grouped dataset helpers ──────────────────────────────────────────
    //
    // The generic editor shows one flat grid, but a size-grouped table is a list of curves, each
    // tagged with the band it applies to. The two are bridged by flattening every curve to one row
    // per point with its tag repeated, then regrouping on those tags when the grid is committed.
    // Editing a tag on every row of a group therefore moves that whole group, and editing it on one
    // row splits that point off into a group of its own - which is the behaviour a flat grid can
    // express without a nested editor.

    /// <summary>One temperature/value point of a flattened curve.</summary>
    /// <param name="Temperature">Temperature (degC).</param>
    /// <param name="Value">Value at that temperature, in the curve's own unit.</param>
    private readonly record struct CurvePoint(double Temperature, double Value);

    /// <summary>Rows sharing one set of grouping cells, with the points they carry.</summary>
    /// <param name="Key">The grouping cells, taken from the first row of the group.</param>
    /// <param name="Points">Points belonging to the group, in grid order.</param>
    private sealed record DatasetGroup(string?[] Key, IReadOnlyList<CurvePoint> Points);

    /// <summary>Flattens a curve to its points, ordered by temperature.</summary>
    /// <param name="table">Curve to flatten.</param>
    /// <returns>The points of every column, merged and sorted.</returns>
    private static IEnumerable<CurvePoint> FlattenCurve(PropertyTable table) =>
        table.Columns
            .ToReadOnlyList()
            .SelectMany(column => column.Entries.ToReadOnlyList())
            .Select(entry => new CurvePoint(entry.X, entry.Value))
            .OrderBy(point => point.Temperature);

    /// <summary>Renders a size band as the four grid cells that describe it.</summary>
    /// <param name="range">Band to render.</param>
    /// <returns>Minimum, its inclusive flag, maximum, and its inclusive flag.</returns>
    private static string?[] SizeBandCells(SizeThicknessRange range) =>
    [
        Num(range.Minimum),
        range.MinimumIncluded ? "yes" : "no",
        Num(range.Maximum),
        range.MaximumIncluded ? "yes" : "no",
    ];

    /// <summary>Reads a size band back from four consecutive grid cells.</summary>
    /// <param name="row">Row cells.</param>
    /// <param name="index">Index of the minimum-bound cell.</param>
    /// <returns>The band those cells describe.</returns>
    private static SizeThicknessRange ParseSizeBand(string?[] row, int index) =>
        new(
            DOpt(row, index),
            ParseInclusive(row[index + 1]),
            DOpt(row, index + 2),
            ParseInclusive(row[index + 3]));

    /// <summary>Interprets an inclusive-flag cell.</summary>
    /// <param name="cell">Cell text.</param>
    /// <returns><c>false</c> only for an explicit negative; blank means inclusive.</returns>
    /// <remarks>
    /// Blank defaults to inclusive because that is how ASME prints an unqualified size limit, so a
    /// user who leaves the cell empty gets the common case rather than an exclusive bound.
    /// </remarks>
    private static bool ParseInclusive(string? cell) =>
        string.IsNullOrWhiteSpace(cell)
        || !(cell.Trim().StartsWith('n') || cell.Trim().StartsWith('N') || cell.Trim() == "0");

    /// <summary>Maps a strength-kind cell back to the domain case.</summary>
    /// <param name="cell">Cell text, normally <c>"Sy"</c> or <c>"Su"</c>.</param>
    /// <returns>The matching kind; anything unrecognised falls back to yield strength.</returns>
    private static TensileStrengthKind ParseStrengthKind(string cell) =>
        cell.Trim().Equals("Su", StringComparison.OrdinalIgnoreCase)
            ? TensileStrengthKind.UltimateTensileStrengthSu
            : TensileStrengthKind.YieldStrengthSy;

    /// <summary>Maps the division and case cells back to the domain source and case.</summary>
    /// <param name="division">Division cell, normally <c>"VIII-1"</c>, <c>"VIII-2"</c> or <c>"Bolting"</c>.</param>
    /// <param name="stressCase">Case cell, normally <c>"Normal"</c> or <c>"High"</c>.</param>
    /// <returns>The source table and the allowable-stress case they name.</returns>
    /// <remarks>
    /// The two cells are not independent: the higher alternative allowable stress only exists in
    /// Division 1, so a "High" case is what selects the Division 1 high source. Marking a Division 2
    /// or bolting row as high is meaningless and is normalised back to the standard case rather than
    /// silently producing a source that has no such table.
    /// </remarks>
    private static (AllowableStressSource Source, AllowableStressCase Case) ParseDivisionAndCase(
        string division,
        string stressCase)
    {
        var text = division.Trim();
        var isHigh = stressCase.Trim().StartsWith("H", StringComparison.OrdinalIgnoreCase);

        if (text.Contains("2", StringComparison.Ordinal))
        {
            return (AllowableStressSource.Division2AllowableStress,
                AllowableStressCase.StandardStrengthAllowableStress);
        }

        if (text.StartsWith("B", StringComparison.OrdinalIgnoreCase))
        {
            return (AllowableStressSource.BoltingAllowableStress,
                AllowableStressCase.StandardStrengthAllowableStress);
        }

        return isHigh
            ? (AllowableStressSource.Division1HighAllowableStress,
                AllowableStressCase.HighStrengthAllowableStress)
            : (AllowableStressSource.Division1AllowableStress,
                AllowableStressCase.StandardStrengthAllowableStress);
    }

    /// <summary>Rebuilds size-grouped datasets from the flattened grid rows.</summary>
    /// <typeparam name="T">Dataset type being rebuilt.</typeparam>
    /// <param name="rows">Grid rows, already validated cell by cell.</param>
    /// <param name="keyWidth">Number of leading cells that identify a group.</param>
    /// <param name="temperatureIndex">Column index of the temperature cell.</param>
    /// <param name="valueIndex">Column index of the value cell.</param>
    /// <param name="build">Creates one dataset from its group, assigned row identity, and curve.</param>
    /// <param name="curveName">Names the curve, from the group's key cells.</param>
    /// <param name="valueLabel">Names the value axis, from the group's key cells.</param>
    /// <param name="unit">Unit of the value axis.</param>
    /// <returns>One dataset per group, in first-appearance order.</returns>
    /// <remarks>
    /// Duplicate temperatures inside a group are collapsed to the last one entered, because the
    /// underlying curve rejects a repeated X and the write path has no way to report a validation
    /// error back to the grid. Taking the last value matches what a user editing a row expects.
    /// Groups are keyed on the exact text of their cells, so trailing whitespace makes a new group.
    /// </remarks>
    private static List<T> RegroupDatasets<T>(
        IReadOnlyList<string?[]> rows,
        int keyWidth,
        int temperatureIndex,
        int valueIndex,
        Func<DatasetGroup, long, PropertyTable, T> build,
        Func<string?[], string> curveName,
        Func<string?[], string> valueLabel,
        string unit)
    {
        var keys = new List<string?[]>();
        var points = new List<List<CurvePoint>>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            // The unit separator cannot occur in a grid cell, so joining on it keeps the composite
            // key unambiguous even when a cell itself contains punctuation.
            var key = string.Join('', Enumerable.Range(0, keyWidth).Select(i => row[i] ?? string.Empty));

            if (!index.TryGetValue(key, out var position))
            {
                position = keys.Count;
                index[key] = position;
                keys.Add(row[..keyWidth]);
                points.Add([]);
            }

            points[position].Add(new CurvePoint(D(row, temperatureIndex), D(row, valueIndex)));
        }

        var datasets = new List<T>(keys.Count);

        for (var i = 0; i < keys.Count; i++)
        {
            var group = new DatasetGroup(keys[i], points[i]);

            var entries = group.Points
                .GroupBy(point => point.Temperature)
                .Select(byTemperature => PropertyTableModule.entry(
                    byTemperature.Key,
                    byTemperature.Last().Value))
                .OrderBy(entry => entry.X)
                .ToFSharpList();

            var table = PropertyTableModule.create1D(
                curveName(group.Key),
                "Temperature",
                "degC",
                valueLabel(group.Key),
                unit,
                XBoundaryPolicy.ReturnError,
                entries);

            // Row identity is renumbered from one on every commit: an edited curve no longer
            // corresponds to the reference-database row it came from, so keeping the old key would
            // claim a traceability that no longer holds. The value only has to be positive and
            // unique within the material.
            if (table.IsOk)
            {
                datasets.Add(build(group, i + 1, table.ResultValue));
            }
        }

        return datasets;
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

    /// <summary>Thermal diffusivity vs temperature, stored as plain pairs. Optional in the domain.</summary>
    /// <remarks>
    /// Sits beside specific heat and thermal conductivity: the three describe the same heat-transfer
    /// behaviour and ASME publishes them together per material group.
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

    /// <summary>Governing minimum strengths Sy and Su vs temperature, with no size dependence.</summary>
    /// <remarks>
    /// Elongation and reduction of area are absent by design: they come from the room-temperature
    /// tensile coupon test and are edited as scalars in the material editor, not per temperature.
    /// </remarks>
    private static MaterialTableSpec TensileProperties() => new(
        "Minimum strengths Sy / Su (governing)",
        [
            Temperature,
            new MaterialTableColumn("Yield strength Sy", "MPa"),
            new MaterialTableColumn("Tensile strength Su", "MPa"),
        ],
        material => material.StrengthProperties.TensileProperties
            .ToReadOnlyList()
            .Select(row => new string?[]
            {
                Num(row.Temperature), Num(row.YieldStrength), Num(row.TensileStrength),
            })
            .ToList(),
        (material, rows) => StrengthPropertyCrud.setTensileProperties(
            rows.Select(r => new TensileProperties(D(r, 0), D(r, 1), D(r, 2))).ToFSharpList(),
            material));

    /// <summary>Sy and Su vs temperature, one curve per Size/Diameter/Thickness band.</summary>
    /// <remarks>
    /// Rows are flattened to one point per line, with the band repeated on each, because a single
    /// grid cannot nest a curve inside a group. Committing regroups the lines back into datasets by
    /// (kind, band), so editing the band on every line of a group moves that whole group.
    /// </remarks>
    private static MaterialTableSpec TensileStrengthDatasets() => new(
        "Minimum strengths Sy / Su by size group",
        [
            new MaterialTableColumn("Sy / Su", "", IsText: true),
            .. SizeBandColumns,
            Temperature,
            new MaterialTableColumn("Strength", "MPa"),
        ],
        material => material.StrengthProperties.TensileStrengthDatasets
            .ToReadOnlyList()
            .SelectMany(dataset => FlattenCurve(dataset.Table).Select<CurvePoint, string?[]>(point =>
            [
                TensileStrengthDatasetModule.kindSymbol(dataset.Kind),
                .. SizeBandCells(dataset.SizeRange),
                Num(point.Temperature), Num(point.Value),
            ]))
            .ToList(),
        (material, rows) => StrengthPropertyCrud.setTensileStrengthDatasets(
            RegroupDatasets(
                rows,
                keyWidth: 1 + SizeBandColumns.Count,
                temperatureIndex: 1 + SizeBandColumns.Count,
                valueIndex: 2 + SizeBandColumns.Count,
                build: (group, rowId, table) => new TensileStrengthDataset(
                    rowId,
                    ParseStrengthKind(S(group.Key, 0)),
                    table,
                    ParseSizeBand(group.Key, 1),
                    FSharpList<AsmeNoteReference>.Empty,
                    FSharpOption<string>.None),
                curveName: group => $"{S(group, 0)} vs temperature",
                valueLabel: group => S(group, 0),
                unit: "MPa")
                .ToFSharpList(),
            material));

    /// <summary>
    /// ASME allowable stresses, one curve per division, case, and Size/Diameter/Thickness band.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the table that answers what the material may actually carry. Division 1 publishes two
    /// cases: the normal allowable stress, and the higher alternative one that exceeds two thirds of
    /// the yield strength - permitted only where slightly greater permanent deformation is
    /// acceptable, and never for flanges of gasketed joints. Division 2 and bolting publish one case
    /// each.
    /// </para>
    /// <para>
    /// Flattened and regrouped the same way as the strength datasets; see
    /// <see cref="TensileStrengthDatasets"/>.
    /// </para>
    /// </remarks>
    private static MaterialTableSpec AllowableStressDatasets() => new(
        "Allowable stresses by size group (Div 1 / Div 2)",
        [
            new MaterialTableColumn("Division", "", IsText: true),
            new MaterialTableColumn("Case", "", IsText: true),
            .. SizeBandColumns,
            new MaterialTableColumn("Max. temperature", "degC", IsOptional: true),
            new MaterialTableColumn("Creep onset", "degC", IsOptional: true),
            Temperature,
            new MaterialTableColumn("Allowable stress S", "MPa"),
        ],
        material => material.StrengthProperties.AllowableStressDatasets
            .ToReadOnlyList()
            .SelectMany(dataset => FlattenCurve(dataset.Table).Select<CurvePoint, string?[]>(point =>
            [
                AllowableStressDatasetModule.divisionLabel(dataset.Source),
                AllowableStressDatasetModule.caseLabel(dataset.Case),
                .. SizeBandCells(dataset.SizeRange),
                Num(dataset.MaximumTemperature), Num(dataset.CreepTemperature),
                Num(point.Temperature), Num(point.Value),
            ]))
            .ToList(),
        (material, rows) => StrengthPropertyCrud.setAllowableStressDatasets(
            RegroupDatasets(
                rows,
                keyWidth: 4 + SizeBandColumns.Count,
                temperatureIndex: 4 + SizeBandColumns.Count,
                valueIndex: 5 + SizeBandColumns.Count,
                build: (group, rowId, table) =>
                {
                    var (source, stressCase) = ParseDivisionAndCase(S(group.Key, 0), S(group.Key, 1));
                    return new AllowableStressDataset(
                        rowId,
                        source,
                        stressCase,
                        table,
                        ParseSizeBand(group.Key, 2),
                        DOpt(group.Key, 2 + SizeBandColumns.Count),
                        DOpt(group.Key, 3 + SizeBandColumns.Count),
                        FSharpList<AsmeNoteReference>.Empty,
                        FSharpOption<string>.None);
                },
                curveName: group => $"{S(group, 0)} {S(group, 1)} allowable stress",
                valueLabel: _ => "Allowable Stress",
                unit: "MPa")
                .ToFSharpList(),
            material));

    /// <summary>Allowable stress by ASME service level. Every stress column is optional.</summary>
    /// <remarks>
    /// A hand-entered table kept for materials whose allowable stresses are supplied per Section III
    /// service level rather than imported from the ASME database. Reference materials populate
    /// <see cref="AllowableStressDatasets"/> instead, so this grid is normally empty for them.
    /// </remarks>
    private static MaterialTableSpec AllowableStresses() => new(
        "Allowable stresses by service level",
        [
            Temperature,
            new MaterialTableColumn("Sec. I level A", "MPa", IsOptional: true),
            new MaterialTableColumn("Sec. I level B", "MPa", IsOptional: true),
            new MaterialTableColumn("Sec. I level C", "MPa", IsOptional: true),
            new MaterialTableColumn("Sec. I level D", "MPa", IsOptional: true),
            new MaterialTableColumn("Sec. II weld", "MPa", IsOptional: true),
        ],
        material => material.StrengthProperties.AllowableStresses
            .ToReadOnlyList()
            .Select(row => new string?[]
            {
                Num(row.Temperature),
                Num(row.Section_I_ServiceLevel_A), Num(row.Section_I_ServiceLevel_B),
                Num(row.Section_I_ServiceLevel_C), Num(row.Section_I_ServiceLevel_D),
                Num(row.Section_II_Weld),
            })
            .ToList(),
        (material, rows) => StrengthPropertyCrud.setAllowableStresses(
            rows.Select(r => new AllowableStress(
                D(r, 0), DOpt(r, 1), DOpt(r, 2), DOpt(r, 3), DOpt(r, 4), DOpt(r, 5))).ToFSharpList(),
            material));

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
