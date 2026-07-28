namespace MaterialLibrary.Domain

open System
open System.Runtime.CompilerServices

// ─────────────────────────────────────────────────────────────────────────────
// PROPERTY TABLE CORE — type definitions and lookup functionality
// ─────────────────────────────────────────────────────────────────────────────
//
// The primary X axis can be any ascending physical quantity:
//   temperature (degC), strain (%), cycles, Factor A, time (h), etc.
// The secondary column selector (optional) is a size/dimension interval:
//   thickness (mm), diameter (mm), or any other range-keyed selector.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Physical meaning of the optional secondary (column-selector) axis.</summary>
type TableDimension =
    /// Wall or plate thickness (mm).
    | Thickness
    /// Nominal outer diameter (mm).
    | Diameter
    /// General length or height dimension (mm).
    | Length
    /// 1D table — primary X axis only, no secondary column selector.
    | NoDimension

/// <summary>Whether a range endpoint is included in the column interval.</summary>
type BoundInclusion =
    /// The bound value belongs to this column (≤ or ≥).
    | Inclusive
    /// The bound value does not belong to this column (< or >).
    | Exclusive

/// <summary>One endpoint of a column interval.</summary>
type SizeRangeBound =
    {
        /// Boundary value in the dimension unit (e.g. mm).
        Value: float
        /// Whether the boundary value itself belongs to this column.
        Inclusion: BoundInclusion
    }

/// <summary>
/// Defines the interval that identifies one column in a 2D property table.
/// </summary>
/// <remarks>
/// Common ASME-style examples (thickness):
/// - t ≤ 25 mm: Lower = None, Upper = Inclusive 25
/// - 25 &lt; t ≤ 50 mm: Lower = Exclusive 25, Upper = Inclusive 50
/// - t &gt; 50 mm: Lower = Exclusive 50, Upper = None
/// </remarks>
type SizeColumnRange =
    {
        /// Lower bound. <c>None</c> means no lower bound (−∞).
        Lower: SizeRangeBound option
        /// Upper bound. <c>None</c> means no upper bound (+∞).
        Upper: SizeRangeBound option
        /// Optional human-readable label (e.g. "t ≤ 25 mm").
        Label: string option
    }

/// <summary>Helpers for <see cref="SizeColumnRange"/> membership and formatting.</summary>
module SizeColumnRange =

    /// <summary>Returns <c>true</c> when <paramref name="size"/> falls within the column interval.</summary>
    let contains (size: float) (range: SizeColumnRange) : bool =
        let lowerOk =
            match range.Lower with
            | None -> true
            | Some lb ->
                match lb.Inclusion with
                | Inclusive -> size >= lb.Value
                | Exclusive -> size > lb.Value

        let upperOk =
            match range.Upper with
            | None -> true
            | Some ub ->
                match ub.Inclusion with
                | Inclusive -> size <= ub.Value
                | Exclusive -> size < ub.Value

        lowerOk && upperOk

    /// <summary>A representative midpoint value used to sort columns for nearest-column lookup.</summary>
    let representative (range: SizeColumnRange) : float =
        let lo =
            match range.Lower with
            | None -> Double.NegativeInfinity
            | Some lb -> lb.Value

        let hi =
            match range.Upper with
            | None -> Double.PositiveInfinity
            | Some ub -> ub.Value

        if Double.IsNegativeInfinity lo && Double.IsPositiveInfinity hi then
            0.0
        elif Double.IsNegativeInfinity lo then
            hi
        elif Double.IsPositiveInfinity hi then
            lo
        else
            (lo + hi) / 2.0

    /// <summary>Returns <c>Label</c> when available, otherwise auto-formats the interval.</summary>
    let format (dimensionUnit: string) (range: SizeColumnRange) : string =
        match range.Label with
        | Some lbl -> lbl
        | None ->
            let lo =
                match range.Lower with
                | None -> ""
                | Some lb ->
                    let op = if lb.Inclusion = Inclusive then ">=" else ">"
                    sprintf "%s %.3f %s" op lb.Value dimensionUnit

            let hi =
                match range.Upper with
                | None -> ""
                | Some ub ->
                    let op = if ub.Inclusion = Inclusive then "<=" else "<"
                    sprintf "%s %.3f %s" op ub.Value dimensionUnit

            match lo, hi with
            | "", "" -> "all"
            | l, "" -> l
            | "", h -> h
            | l, h -> sprintf "%s and %s" l h

/// <summary>
/// Policy applied when the queried X value falls outside the tabulated range.
/// </summary>
type XBoundaryPolicy =
    /// Return <c>MaterialError.InvalidOperation</c> — strict range enforcement.
    | ReturnError
    /// Clamp to the nearest boundary knot and return its value with a warning.
    | FlatExtrapolate

/// <summary>One (X, value) knot within a <see cref="TableColumn"/>.</summary>
type TableColumnEntry =
    {
        /// Primary-axis value (ascending). Units declared at table level in <c>XAxisUnit</c>.
        X: float
        /// Tabulated property value. Units declared at table level in <c>ValueUnit</c>.
        Value: float
    }

/// <summary>
/// One column in a <see cref="PropertyTable"/>: a range selector plus a list of (X, value) knots.
/// </summary>
/// <remarks>
/// For 1D tables the <see cref="SizeRange"/> carries no bounds and is not evaluated.
/// </remarks>
type TableColumn =
    {
        /// Range interval that selects this column. Both bounds <c>None</c> for 1D tables.
        SizeRange: SizeColumnRange
        /// (X, value) knots for this column, sorted ascending by X after validation.
        Entries: TableColumnEntry list
    }

/// <summary>
/// Result of a <see cref="PropertyTable"/> lookup, with full diagnostic metadata.
/// </summary>
type TableLookupResult =
    {
        /// Interpolated (or flat-extrapolated) property value in <c>ValueUnit</c>.
        Value: float
        /// Primary-axis value used during the lookup (in <c>XAxisUnit</c>).
        XQueried: float
        /// Secondary dimension value used during the lookup, or <c>None</c> for 1D tables.
        SizeQueried: float option
        /// Label of the matched column (<c>None</c> for 1D tables).
        MatchedColumnLabel: string option
        /// <c>true</c> when the queried X fell exactly on a tabulated knot.
        IsXExact: bool
        /// <c>true</c> when the X was out of range and the boundary value was used.
        IsXExtrapolated: bool
        /// <c>true</c> when the queried size matched no column interval; nearest column was used.
        SizeIsOutOfRange: bool
        /// Human-readable warning(s), or <c>None</c> when fully within range.
        Warning: string option
    }

/// <summary>
/// A validated, ascending-X property table with an optional range-keyed secondary column selector.
/// </summary>
/// <remarks>
/// <b>1D table</b> — exactly one column; the primary X axis is any ascending quantity
/// (temperature, strain, cycles, Factor A, time, etc.).
/// <b>2D table</b> — multiple columns, each identified by a <see cref="SizeColumnRange"/>.
/// The query selects the matching column (no interpolation between columns); temperature
/// within the matched column is linearly interpolated.
/// <para>
/// <b>X out of range:</b> controlled by <see cref="XBoundaryPolicy"/> — returns <c>Error</c>
/// or clamps to the boundary value with a warning.
/// </para>
/// <para>
/// <b>Size out of range:</b> always <c>Ok</c>; nearest column used with a warning.
/// </para>
/// </remarks>
type PropertyTable =
    {
        /// Human-readable table name (e.g. "Allowable Stress").
        Name: string
        /// Name of the primary X axis (e.g. "Strain", "Temperature", "Cycles", "Factor A").
        XAxisName: string
        /// Units of the primary X axis (e.g. "%", "degC", "cycles", "").
        XAxisUnit: string
        /// Name of the Y axis (tabulated values; e.g. "Stress", "Pressure", "Strain").
        YAxisName: string
        /// Units of the tabulated values (e.g. "MPa", "%").
        ValueUnit: string
        /// Physical meaning of the secondary column-selector axis.
        DimensionType: TableDimension
        /// Units of the secondary dimension (e.g. "mm"). Empty string for 1D tables.
        DimensionUnit: string
        /// Policy applied when the queried X falls outside the tabulated range.
        XBoundaryPolicy: XBoundaryPolicy
        /// Ordered list of validated columns, sorted by representative size value.
        Columns: TableColumn list
    }

/// <summary>Factory, lookup, and query functions for <see cref="PropertyTable"/>.</summary>
module PropertyTable =

    let private isFinite (v: float) =
        not (Double.IsNaN v || Double.IsInfinity v)

    let private linterp (x0: float) (y0: float) (x1: float) (y1: float) (x: float) : float =
        if x0 = x1 then
            y0
        else
            y0 + (y1 - y0) * (x - x0) / (x1 - x0)

    // ── validation ──────────────────────────────────────────────────────────

    let private validateColumn (index: int) (col: TableColumn) : Result<TableColumn, MaterialError> =
        if List.isEmpty col.Entries then
            Error(MaterialError.InvalidOperation(sprintf "PropertyTable column %d has no entries" index))
        elif col.Entries |> List.exists (fun e -> not (isFinite e.X)) then
            Error(MaterialError.InvalidOperation(sprintf "PropertyTable column %d contains a non-finite X value" index))
        elif col.Entries |> List.exists (fun e -> not (isFinite e.Value)) then
            Error(MaterialError.InvalidOperation(sprintf "PropertyTable column %d contains a non-finite value" index))
        else
            let entries = col.Entries |> List.sortBy (fun entry -> entry.X)

            if entries |> List.pairwise |> List.exists (fun (left, right) -> left.X = right.X) then
                Error(MaterialError.InvalidOperation(sprintf "PropertyTable column %d contains duplicate X values" index))
            else
                Ok { col with Entries = entries }

    /// <summary>Validates and normalises a candidate <see cref="PropertyTable"/>.</summary>
    let validate (table: PropertyTable) : Result<PropertyTable, MaterialError> =
        if String.IsNullOrWhiteSpace table.Name then
            Error(MaterialError.InvalidOperation "PropertyTable name cannot be empty")
        elif String.IsNullOrWhiteSpace table.XAxisName then
            Error(MaterialError.InvalidOperation "PropertyTable XAxisName cannot be empty")
        elif List.isEmpty table.Columns then
            Error(MaterialError.InvalidOperation "PropertyTable requires at least one column")
        else
            let results = table.Columns |> List.mapi validateColumn

            let errors =
                results
                |> List.choose (function
                    | Error e -> Some e
                    | Ok _ -> None)

            match errors with
            | e :: _ -> Error e
            | [] ->
                let validated =
                    results
                    |> List.choose (function
                        | Ok c -> Some c
                        | _ -> None)

                let sorted =
                    validated |> List.sortBy (fun c -> SizeColumnRange.representative c.SizeRange)

                Ok { table with Columns = sorted }

    type private ValidationCacheEntry(result: Result<PropertyTable, MaterialError>) =
        member _.Result = result

    let private validationCache = ConditionalWeakTable<PropertyTable, ValidationCacheEntry>()

    let private validateCached (table: PropertyTable) : Result<PropertyTable, MaterialError> =
        if isNull (box table) then
            Error(MaterialError.InvalidOperation "PropertyTable cannot be null")
        else
            validationCache.GetValue(table, fun candidate -> ValidationCacheEntry(validate candidate)).Result

    /// <summary>
    /// Creates and validates a 1D (single-column) <see cref="PropertyTable"/>.
    /// </summary>
    let create1D
        (name: string)
        (xAxisName: string)
        (xAxisUnit: string)
        (yAxisName: string)
        (valueUnit: string)
        (policy: XBoundaryPolicy)
        (entries: TableColumnEntry list)
        : Result<PropertyTable, MaterialError> =
        validate
            { Name = name
              XAxisName = xAxisName
              XAxisUnit = xAxisUnit
              YAxisName = yAxisName
              ValueUnit = valueUnit
              DimensionType = NoDimension
              DimensionUnit = ""
              XBoundaryPolicy = policy
              Columns =
                [ { SizeRange =
                      { Lower = None
                        Upper = None
                        Label = None }
                    Entries = entries } ] }

    /// <summary>
    /// Creates and validates a 2D (multi-column) <see cref="PropertyTable"/>.
    /// </summary>
    let create2D
        (name: string)
        (xAxisName: string)
        (xAxisUnit: string)
        (yAxisName: string)
        (valueUnit: string)
        (dimensionType: TableDimension)
        (dimensionUnit: string)
        (policy: XBoundaryPolicy)
        (columns: TableColumn list)
        : Result<PropertyTable, MaterialError> =
        if dimensionType = NoDimension then
            Error(MaterialError.InvalidOperation "Use create1D for NoDimension tables")
        else
            validate
                { Name = name
                  XAxisName = xAxisName
                  XAxisUnit = xAxisUnit
                  YAxisName = yAxisName
                  ValueUnit = valueUnit
                  DimensionType = dimensionType
                  DimensionUnit = dimensionUnit
                  XBoundaryPolicy = policy
                  Columns = columns }

    // ── X-axis interpolation within one column ───────────────────────────────

    let private lookupXInColumn
        (x: float)
        (policy: XBoundaryPolicy)
        (tableName: string)
        (xAxisName: string)
        (col: TableColumn)
        : Result<float * bool * bool * string option, MaterialError> =

        let entries = col.Entries

        match entries with
        | [] -> Error(MaterialError.InvalidOperation(sprintf "PropertyTable '%s': empty column" tableName))
        | [ single ] -> Ok(single.Value, single.X = x, false, None)
        | _ ->
            let minX = (List.head entries).X
            let maxX = (List.last entries).X

            if x < minX then
                match policy with
                | ReturnError ->
                    Error(
                        MaterialError.InvalidOperation(
                            sprintf
                                "PropertyTable '%s': %s %.4g is below the tabulated minimum %.4g"
                                tableName
                                xAxisName
                                x
                                minX
                        )
                    )
                | FlatExtrapolate ->
                    Ok(
                        (List.head entries).Value,
                        false,
                        true,
                        Some(
                            sprintf
                                "PropertyTable '%s': %s %.4g is below the tabulated minimum %.4g; boundary value used"
                                tableName
                                xAxisName
                                x
                                minX
                        )
                    )

            elif x > maxX then
                match policy with
                | ReturnError ->
                    Error(
                        MaterialError.InvalidOperation(
                            sprintf
                                "PropertyTable '%s': %s %.4g exceeds the tabulated maximum %.4g"
                                tableName
                                xAxisName
                                x
                                maxX
                        )
                    )
                | FlatExtrapolate ->
                    Ok(
                        (List.last entries).Value,
                        false,
                        true,
                        Some(
                            sprintf
                                "PropertyTable '%s': %s %.4g exceeds the tabulated maximum %.4g; boundary value used"
                                tableName
                                xAxisName
                                x
                                maxX
                        )
                    )

            else
                let below = entries |> List.tryFindBack (fun e -> e.X <= x)
                let above = entries |> List.tryFind (fun e -> e.X >= x)

                match below, above with
                | Some e1, Some e2 ->
                    let isExact = e1.X = x
                    Ok(linterp e1.X e1.Value e2.X e2.Value x, isExact, false, None)
                | _ ->
                    Error(
                        MaterialError.InvalidOperation(
                            sprintf
                                "PropertyTable '%s': interpolation bracket not found at %s = %.4g"
                                tableName
                                xAxisName
                                x
                        )
                    )

    // ── column selection ─────────────────────────────────────────────────────

    let private selectColumn
        (size: float)
        (tableName: string)
        (dimensionUnit: string)
        (columns: TableColumn list)
        : TableColumn * bool * string option =

        match columns |> List.tryFind (fun c -> SizeColumnRange.contains size c.SizeRange) with
        | Some col -> col, false, None
        | None ->
            let nearest =
                columns
                |> List.minBy (fun c -> abs (SizeColumnRange.representative c.SizeRange - size))

            let label = SizeColumnRange.format dimensionUnit nearest.SizeRange

            let warn =
                sprintf
                    "PropertyTable '%s': size %.4g %s matches no column; nearest column (%s) used"
                    tableName
                    size
                    dimensionUnit
                    label

            nearest, true, Some warn

    /// <summary>Looks up a value at the given X in a 1D table.</summary>
    let lookup1D (x: float) (table: PropertyTable) : Result<TableLookupResult, MaterialError> =
        if not (isFinite x) then
            Error(MaterialError.InvalidOperation "PropertyTable lookup X must be finite")
        else
            validateCached table
            |> Result.bind (fun normalized ->
                if normalized.DimensionType <> NoDimension then
                    Error(
                        MaterialError.InvalidOperation(
                            sprintf "PropertyTable '%s' has a column-selector axis; use lookup2D" normalized.Name
                        )
                    )
                else
                    match normalized.Columns with
                    | [] -> Error(MaterialError.InvalidOperation "PropertyTable requires at least one column")
                    | col :: _ ->
                        lookupXInColumn
                            x
                            normalized.XBoundaryPolicy
                            normalized.Name
                            normalized.XAxisName
                            col
                        |> Result.map (fun (value, isExact, isExtrapolated, warning) ->
                            { Value = value
                              XQueried = x
                              SizeQueried = None
                              MatchedColumnLabel = col.SizeRange.Label
                              IsXExact = isExact
                              IsXExtrapolated = isExtrapolated
                              SizeIsOutOfRange = false
                              Warning = warning }))

    /// <summary>Looks up a value at the given X and size in a 2D table.</summary>
    let lookup2D (x: float) (size: float) (table: PropertyTable) : Result<TableLookupResult, MaterialError> =
        if not (isFinite x) || not (isFinite size) then
            Error(MaterialError.InvalidOperation "PropertyTable lookup X and size must be finite")
        else
            validateCached table
            |> Result.bind (fun normalized ->
                if normalized.DimensionType = NoDimension then
                    Error(
                        MaterialError.InvalidOperation(
                            sprintf "PropertyTable '%s' has no column-selector axis; use lookup1D" normalized.Name
                        )
                    )
                else
                    let col, sizeOutOfRange, sizeWarning =
                        selectColumn size normalized.Name normalized.DimensionUnit normalized.Columns

                    lookupXInColumn
                        x
                        normalized.XBoundaryPolicy
                        normalized.Name
                        normalized.XAxisName
                        col
                    |> Result.map (fun (value, isExact, isExtrapolated, xWarning) ->
                        let warning =
                            match sizeWarning, xWarning with
                            | None, None -> None
                            | Some message, None
                            | None, Some message -> Some message
                            | Some sizeMessage, Some xMessage -> Some(sprintf "%s | %s" sizeMessage xMessage)

                        { Value = value
                          XQueried = x
                          SizeQueried = Some size
                          MatchedColumnLabel = col.SizeRange.Label
                          IsXExact = isExact
                          IsXExtrapolated = isExtrapolated
                          SizeIsOutOfRange = sizeOutOfRange
                          Warning = warning }))

    /// <summary>Universal dispatch: calls <c>lookup1D</c> when size is None, <c>lookup2D</c> otherwise.</summary>
    let lookup (x: float) (size: float option) (table: PropertyTable) : Result<TableLookupResult, MaterialError> =
        match size with
        | None -> lookup1D x table
        | Some s -> lookup2D x s table

    /// <summary>All distinct X knots across all columns, sorted ascending.</summary>
    let xKnots (table: PropertyTable) : float list =
        table.Columns
        |> List.collect (fun c -> c.Entries |> List.map (fun e -> e.X))
        |> List.distinct
        |> List.sort

    /// <summary>X range [X_min, X_max] of the whole table, or None if empty.</summary>
    let xRange (table: PropertyTable) : (float * float) option =
        let ks = xKnots table

        if List.isEmpty ks then
            None
        else
            Some(List.head ks, List.last ks)

    /// <summary>Number of columns in the table (1 for 1D tables).</summary>
    let columnCount (table: PropertyTable) : int = List.length table.Columns

    /// <summary>All column labels or auto-formatted descriptions.</summary>
    let columnLabels (table: PropertyTable) : string list =
        table.Columns
        |> List.map (fun c -> SizeColumnRange.format table.DimensionUnit c.SizeRange)

    /// <summary>"size ≤ upperBound"</summary>
    let rangeUpTo (upperBound: float) (label: string option) : SizeColumnRange =
        { Lower = None
          Upper =
            Some
                { Value = upperBound
                  Inclusion = Inclusive }
          Label = label }

    /// <summary>"lowerBound &lt; size ≤ upperBound"</summary>
    let rangeExclusiveLowerInclusiveUpper
        (lowerBound: float)
        (upperBound: float)
        (label: string option)
        : SizeColumnRange =
        { Lower =
            Some
                { Value = lowerBound
                  Inclusion = Exclusive }
          Upper =
            Some
                { Value = upperBound
                  Inclusion = Inclusive }
          Label = label }

    /// <summary>"lowerBound ≤ size ≤ upperBound"</summary>
    let rangeBothInclusive (lowerBound: float) (upperBound: float) (label: string option) : SizeColumnRange =
        { Lower =
            Some
                { Value = lowerBound
                  Inclusion = Inclusive }
          Upper =
            Some
                { Value = upperBound
                  Inclusion = Inclusive }
          Label = label }

    /// <summary>"size &gt; lowerBound" (no upper bound)</summary>
    let rangeAbove (lowerBound: float) (label: string option) : SizeColumnRange =
        { Lower =
            Some
                { Value = lowerBound
                  Inclusion = Exclusive }
          Upper = None
          Label = label }

    /// <summary>Builds a TableColumn from a range and a list of (X, value) tuples.</summary>
    let column (range: SizeColumnRange) (entries: (float * float) list) : TableColumn =
        { SizeRange = range
          Entries = entries |> List.map (fun (x, v) -> { X = x; Value = v }) }

    /// <summary>Convenience constructor for a single TableColumnEntry.</summary>
    let entry (x: float) (value: float) : TableColumnEntry = { X = x; Value = value }
