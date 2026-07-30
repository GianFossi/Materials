namespace MaterialLibrary.Excel

open System
open MaterialLibrary.Domain
open MaterialLibrary.Interpolation

/// <summary>
/// Pure helpers converting <c>MaterialLibrary</c> <see cref="Result{T,TError}"/> values and tables
/// into the plain <c>obj</c> / <c>obj[,]</c> shapes Excel-DNA marshals into worksheet cells.
/// </summary>
/// <remarks>
/// Failures are reported as short, prefixed text (e.g. <c>"#N/A ..."</c>, <c>"#VALUE! ..."</c>) rather
/// than native Excel error codes, so the cell always shows *why* a lookup failed instead of a bare
/// error glyph.
/// </remarks>
module ExcelHelpers =

    let private interpolationErrorMessage (err: InterpolationError) : string =
        match err with
        | OutOfRange(lo, hi) -> sprintf "value is outside the tabulated range [%.6g, %.6g]" lo hi
        | InsufficientData -> "the table has too few points to interpolate"
        | InvalidInput msg -> msg

    /// <summary>Formats a <see cref="MaterialError"/> as a short, Excel-friendly message prefixed with an error tag.</summary>
    let materialErrorToText (err: MaterialError) : string =
        match err with
        | NotFound msg -> sprintf "#N/A material not found: %s" msg
        | MaterialError.InterpolationError inner -> sprintf "#VALUE! %s" (interpolationErrorMessage inner)
        | InvalidOperation msg -> sprintf "#VALUE! %s" msg
        | CreepModelError msg -> sprintf "#VALUE! %s" msg

    /// <summary>Unwraps a <c>Result&lt;float, MaterialError&gt;</c> to a boxed Excel value.</summary>
    let ofFloatResult (result: Result<float, MaterialError>) : obj =
        match result with
        | Ok value -> box value
        | Error err -> box (materialErrorToText err)

    /// <summary>Unwraps a <c>Result&lt;string, MaterialError&gt;</c> to a boxed Excel value.</summary>
    let ofStringResult (result: Result<string, MaterialError>) : obj =
        match result with
        | Ok value -> box value
        | Error err -> box (materialErrorToText err)

    /// <summary>Looks up <paramref name="materialId"/> in the current cache and applies <paramref name="f"/>, or reports <c>NotFound</c>.</summary>
    let withMaterial (materialId: string) (f: Material -> Result<'a, MaterialError>) : Result<'a, MaterialError> =
        match LibraryCache.current().GetMaterialById materialId with
        | None -> Error(MaterialError.NotFound materialId)
        | Some material -> f material

    /// <summary>Builds a 1x1 error grid, used when a table-returning function fails before any rows are known.</summary>
    let errorGrid (message: string) : obj[,] = array2D [ [ box message ] ]

    /// <summary>Converts a <c>Result&lt;obj[,], MaterialError&gt;</c> to a grid, substituting a 1x1 error grid on failure.</summary>
    let ofGridResult (result: Result<obj[,], MaterialError>) : obj[,] =
        match result with
        | Ok grid -> grid
        | Error err -> errorGrid (materialErrorToText err)

    /// <summary>Builds a rectangular Excel grid from a header row and a list of same-length data rows.</summary>
    let gridOfRows (headers: string list) (rows: obj list list) : obj[,] =
        let columnCount = List.length headers
        let grid = Array2D.create (List.length rows + 1) columnCount (box "")

        headers |> List.iteri (fun j header -> grid.[0, j] <- box header)

        rows
        |> List.iteri (fun i row -> row |> List.iteri (fun j value -> grid.[i + 1, j] <- value))

        grid

    /// <summary>
    /// Converts a 1D <see cref="PropertyTable"/> (single column, no size dimension) to a two-column
    /// (X, value) Excel grid, sorted ascending by X.
    /// </summary>
    let table1DToGrid (table: PropertyTable) : obj[,] =
        let rows =
            table.Columns
            |> List.collect (fun column -> column.Entries)
            |> List.sortBy (fun entry -> entry.X)
            |> List.map (fun entry -> [ box entry.X; box entry.Value ])

        gridOfRows [ table.XAxisName; table.YAxisName ] rows

    /// <summary>
    /// Converts a 2D <see cref="PropertyTable"/> (multiple size-range columns) to an Excel grid: the
    /// first column lists every distinct X knot across all columns, and each subsequent column holds
    /// that column's raw (non-interpolated) value at each X knot, blank where the column has no entry.
    /// </summary>
    let table2DToGrid (table: PropertyTable) : obj[,] =
        let xs = PropertyTable.xKnots table
        let labels = PropertyTable.columnLabels table

        let rows =
            xs
            |> List.map (fun x ->
                let cells =
                    table.Columns
                    |> List.map (fun column ->
                        column.Entries
                        |> List.tryFind (fun entry -> entry.X = x)
                        |> Option.map (fun entry -> box entry.Value)
                        |> Option.defaultValue (box ""))

                box x :: cells)

        gridOfRows (table.XAxisName :: labels) rows

    /// <summary>Converts any <see cref="PropertyTable"/> to a grid, dispatching on whether it has a size dimension.</summary>
    let anyTableToGrid (table: PropertyTable) : obj[,] =
        if table.DimensionType = NoDimension then
            table1DToGrid table
        else
            table2DToGrid table

/// <summary>
/// Linear interpolation over property axes that the domain model stores as plain <c>(x, value)</c>
/// lists rather than as a <see cref="PropertyTable"/> (elastic modulus, Poisson's ratio, thermal
/// expansion/conductivity, tensile/compression strength vs temperature, Larson-Miller curves, ...).
/// </summary>
/// <remarks>
/// Deliberately linear-only: it builds an ad-hoc single-column <see cref="PropertyTable"/> and reuses
/// <see cref="PropertyTable.lookup1D"/>, matching ASME Section II Part D's own convention of linear
/// interpolation between tabulated values, and avoiding a third reimplementation of the cubic-spline
/// / Lagrange machinery that already exists (duplicated) inside <c>Interpolations.fs</c>. Properties
/// that need those richer modes (density, specific heat) already expose them through
/// <c>MaterialLibrary.GetDensity</c> / <c>GetSpecificHeatFromTable</c>.
/// </remarks>
module AdHocTable =

    /// <summary>Interpolates <paramref name="x"/> from ad-hoc <c>(x, value)</c> pairs; flat-extrapolates outside the tabulated range.</summary>
    let interpolate
        (name: string)
        (xAxisName: string)
        (xAxisUnit: string)
        (yAxisName: string)
        (valueUnit: string)
        (points: (float * float) list)
        (x: float)
        : Result<float, MaterialError> =
        if List.isEmpty points then
            Error(MaterialError.InvalidOperation(sprintf "%s has no data points" name))
        else
            PropertyTable.create1D
                name
                xAxisName
                xAxisUnit
                yAxisName
                valueUnit
                XBoundaryPolicy.FlatExtrapolate
                (points |> List.map (fun (px, pv) -> PropertyTable.entry px pv))
            |> Result.bind (PropertyTable.lookup1D x)
            |> Result.map (fun result -> result.Value)

/// <summary>
/// Helpers for reading optional Excel worksheet-function arguments supplied as <c>obj</c>.
/// </summary>
/// <remarks>
/// Excel-DNA represents an omitted argument as <see cref="ExcelDna.Integration.ExcelMissing"/> and an
/// empty cell as <see cref="ExcelDna.Integration.ExcelEmpty"/>; plain F# optional-parameter syntax is
/// only available on members, not on the static functions Excel-DNA registers, so every optional
/// worksheet argument in this project is typed <c>obj</c> and read through these helpers.
/// </remarks>
module Args =

    let private isBlank (value: obj) : bool =
        isNull value
        || (value :? ExcelDna.Integration.ExcelMissing)
        || (value :? ExcelDna.Integration.ExcelEmpty)
        || (match value with
            | :? string as s -> String.IsNullOrWhiteSpace s
            | _ -> false)

    /// <summary>Reads an optional numeric argument, returning <paramref name="fallback"/> when omitted.</summary>
    let optionalNumber (fallback: float) (value: obj) : float =
        if isBlank value then
            fallback
        else
            match value with
            | :? float as d -> d
            | :? int as i -> float i
            | :? string as s ->
                match Double.TryParse(s, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
                | true, d -> d
                | false, _ -> fallback
            | _ -> fallback

    /// <summary>Reads an optional numeric argument as <c>Some value</c>, or <c>None</c> when omitted/blank.</summary>
    let optionalNumberOption (value: obj) : float option =
        if isBlank value then
            None
        else
            match value with
            | :? float as d -> Some d
            | :? int as i -> Some(float i)
            | :? string as s ->
                match Double.TryParse(s, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
                | true, d -> Some d
                | false, _ -> None
            | _ -> None

    /// <summary>Reads an optional text argument, returning <paramref name="fallback"/> when omitted.</summary>
    let optionalText (fallback: string) (value: obj) : string =
        if isBlank value then
            fallback
        else
            match value with
            | :? string as s -> s
            | other -> string other

    /// <summary>Reads an optional text argument as <c>Some value</c>, or <c>None</c> when omitted/blank.</summary>
    let optionalTextOption (value: obj) : string option =
        if isBlank value then None else Some(optionalText "" value)

    /// <summary>Reads an optional boolean argument, returning <paramref name="fallback"/> when omitted. Accepts TRUE/FALSE, 1/0, and "true"/"false" text.</summary>
    let optionalBool (fallback: bool) (value: obj) : bool =
        if isBlank value then
            fallback
        else
            match value with
            | :? bool as b -> b
            | :? float as d -> d <> 0.0
            | :? int as i -> i <> 0
            | :? string as s ->
                match Boolean.TryParse s with
                | true, b -> b
                | false, _ -> fallback
            | _ -> fallback

    /// <summary>
    /// Parses the interpolation-mode/Lagrange-degree pair used by every interpolated-value function
    /// in this project. Recognised mode names (case-insensitive): "Linear" (default), "CubicSpline",
    /// "Constant", "Lagrange"/"LagrangePolynomial" (degree from <paramref name="lagrangeDegree"/>,
    /// default 3).
    /// </summary>
    let interpolationMode (modeText: obj) (lagrangeDegree: obj) : InterpolationMode =
        let degree = optionalNumber 3.0 lagrangeDegree |> int

        match (optionalText "Linear" modeText).Trim().ToLowerInvariant() with
        | "linear" -> InterpolationMode.Linear
        | "cubicspline"
        | "cubic" -> InterpolationMode.CubicSpline
        | "constant"
        | "step" -> InterpolationMode.Constant
        | "lagrange"
        | "lagrangepolynomial" -> InterpolationMode.LagrangePolynomial degree
        | _ -> InterpolationMode.Linear
