namespace MaterialLibrary.Interpolation

open System
open MaterialLibrary.Domain

/// CULTURE RULE: Numeric parsing and formatting for XML/JSON persistence must always use CultureInfo.InvariantCulture.
/// <summary>
/// Specifies the algorithm used when evaluating a property at a temperature or time not directly tabulated.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><description><c>Linear</c> — piecewise linear (trapezoidal); exact at knot points, first order.</description></item>
///   <item><description><c>CubicSpline</c> — natural cubic spline with zero second derivatives at the ends.</description></item>
///   <item><description><c>LagrangePolynomial n</c> — local Lagrange polynomial of degree n (1..10); use with caution near dataset boundaries.</description></item>
///   <item><description><c>Constant</c> — nearest-neighbour (step) interpolation; returns the value at the closest knot.</description></item>
/// </list>
/// </remarks>
type InterpolationMode =
    | Linear
    | CubicSpline
    | LagrangePolynomial of int
    | Constant

// ========== LINEAR INTERPOLATION HELPER ==========

[<AutoOpen>]
module private Helpers =
    /// <summary>Performs a one-dimensional linear (lerp) interpolation between two known points.</summary>
    /// <param name="x0">Abscissa of the left knot.</param>
    /// <param name="y0">Ordinate of the left knot.</param>
    /// <param name="x1">Abscissa of the right knot.</param>
    /// <param name="y1">Ordinate of the right knot.</param>
    /// <param name="x">Query abscissa. Must satisfy x0 ≤ x ≤ x1.</param>
    /// <returns>
    /// Interpolated ordinate y = y0 + (y1 - y0) · (x - x0) / (x1 - x0).
    /// Returns <paramref name="y0"/> when x0 = x1 to avoid division by zero.
    /// </returns>
    let linearInterpolate (x0: float) (y0: float) (x1: float) (y1: float) (x: float) : float =
        if x0 = x1 then
            y0
        else
            y0 + (y1 - y0) * (x - x0) / (x1 - x0)

    /// Evaluates a property at temperature t by linear interpolation on sorted (T, V) knots.
    /// A single-knot list returns that constant for any t (no interpolation, no range check).
    /// extrapolateFlat=true: extend first/last value outside range; false: return Double.NaN.
    let evalAtTemp (extrapolateFlat: bool) (points: (float * float) list) (t: float) : float =
        let sorted: (float * float) list = points |> List.sortBy fst

        match sorted with
        | [] -> Double.NaN
        | [ (_, v: float) ] -> v
        | _ ->
            let (tMin: float), (vMin: float) = List.head sorted
            let (tMax: float), (vMax: float) = List.last sorted

            if t < tMin then
                if extrapolateFlat then vMin else Double.NaN
            elif t > tMax then
                if extrapolateFlat then vMax else Double.NaN
            else
                match
                    sorted |> List.tryFindBack (fun (ti, _) -> ti <= t), sorted |> List.tryFind (fun (ti, _) -> ti >= t)
                with
                | Some(t0: float, v0: float), Some(t1: float, v1: float) -> linearInterpolate t0 v0 t1 v1 t
                | _ -> Double.NaN

    let private cubicSplineCoefficients (points: (float * float) list) : (float * float * float * float * float) list =
        let sorted = points |> List.sortBy fst
        let n = List.length sorted - 1

        if n < 1 then
            []
        else
            let xs = sorted |> List.map fst |> Array.ofList
            let ys = sorted |> List.map snd |> Array.ofList
            let h = Array.init n (fun i -> xs.[i + 1] - xs.[i])

            let lo = Array.zeroCreate (n + 1)
            let diag = Array.create (n + 1) 1.0
            let up = Array.zeroCreate (n + 1)
            let rhs = Array.zeroCreate (n + 1)

            for i in 1 .. n - 1 do
                lo.[i] <- h.[i - 1]
                diag.[i] <- 2.0 * (h.[i - 1] + h.[i])
                up.[i] <- h.[i]
                rhs.[i] <- 3.0 * ((ys.[i + 1] - ys.[i]) / h.[i] - (ys.[i] - ys.[i - 1]) / h.[i - 1])

            let diagW = Array.copy diag
            let rhsW = Array.copy rhs

            for i in 1..n do
                let m = lo.[i] / diagW.[i - 1]
                diagW.[i] <- diagW.[i] - m * up.[i - 1]
                rhsW.[i] <- rhsW.[i] - m * rhsW.[i - 1]

            let c = Array.zeroCreate (n + 1)
            c.[n] <- rhsW.[n] / diagW.[n]

            for i in n - 1 .. -1 .. 0 do
                c.[i] <- (rhsW.[i] - up.[i] * c.[i + 1]) / diagW.[i]

            [ for i in 0 .. n - 1 ->
                  let a = ys.[i]
                  let b = (ys.[i + 1] - ys.[i]) / h.[i] - h.[i] * (2.0 * c.[i] + c.[i + 1]) / 3.0
                  let dco = (c.[i + 1] - c.[i]) / (3.0 * h.[i])
                  (xs.[i], a, b, c.[i], dco) ]

    let private cubicSplineEvaluate (segments: (float * float * float * float * float) list) (x: float) : float =
        if List.isEmpty segments then
            Double.NaN
        else
            let sorted = segments |> List.sortBy (fun (x0, _, _, _, _) -> x0)

            let seg =
                sorted
                |> List.tryFindBack (fun (x0, _, _, _, _) -> x0 <= x)
                |> Option.defaultValue (List.head sorted)

            let (x0, a, b, c, d) = seg
            let dx = x - x0
            a + b * dx + c * dx * dx + d * dx * dx * dx

    let private lagrangeEvaluate (degree: int) (points: (float * float) list) (x: float) : float =
        if degree < 1 || degree > 10 then
            Double.NaN
        else
            let sorted = points |> List.sortBy fst |> Array.ofList
            let m = degree + 1

            if sorted.Length < m then
                Double.NaN
            else
                let pivot =
                    sorted
                    |> Array.tryFindIndexBack (fun (xi, _) -> xi <= x)
                    |> Option.defaultValue 0

                let start = max 0 (min (sorted.Length - m) (pivot - m / 2))
                let knots = sorted.[start .. start + m - 1]

                knots
                |> Array.sumBy (fun (xk, yk) ->
                    let basis =
                        knots
                        |> Array.fold (fun acc (xj, _) -> if xj = xk then acc else acc * (x - xj) / (xk - xj)) 1.0

                    yk * basis)

    /// Evaluates a sorted 1D dataset using the requested interpolation mode.
    let interpolate1D
        (mode: InterpolationMode)
        (points: (float * float) list)
        (x: float)
        : Result<float, InterpolationError> =
        let sorted = points |> List.sortBy fst

        if List.isEmpty sorted then
            Error InterpolationError.InsufficientData
        else
            let minX = fst (List.head sorted)
            let maxX = fst (List.last sorted)

            if sorted.Length = 1 then
                Ok(snd (List.head sorted))
            elif x < minX || x > maxX then
                Error(InterpolationError.OutOfRange(minX, maxX))
            else
                match mode with
                | Constant ->
                    let closest = sorted |> List.minBy (fun (xi, _) -> abs (xi - x))
                    Ok(snd closest)

                | Linear ->
                    match
                        sorted |> List.tryFindBack (fun (xi, _) -> xi <= x),
                        sorted |> List.tryFind (fun (xi, _) -> xi >= x)
                    with
                    | Some(x0, y0), Some(x1, y1) -> Ok(linearInterpolate x0 y0 x1 y1 x)
                    | _ -> Error InterpolationError.InsufficientData

                | CubicSpline ->
                    let value =
                        sorted
                        |> cubicSplineCoefficients
                        |> fun segments -> cubicSplineEvaluate segments x

                    if Double.IsNaN value then
                        Error InterpolationError.InsufficientData
                    else
                        Ok value

                | LagrangePolynomial degree ->
                    let value = lagrangeEvaluate degree sorted x

                    if Double.IsNaN value then
                        Error InterpolationError.InsufficientData
                    else
                        Ok value

// ========== SPECIFIC HEAT INTERPOLATION ==========

/// <summary>Interpolation functions for temperature-dependent specific heat Cp(T) tables.</summary>
module SpecificHeatInterpolation =
    /// <summary>Evaluates Cp at a target temperature by interpolating a tabulated Cp(T) dataset.</summary>
    /// <param name="mode">Interpolation algorithm to use (see <see cref="InterpolationMode"/>).
    /// <c>CubicSpline</c> and <c>LagrangePolynomial n</c> are evaluated directly.</param>
    /// <param name="targetTemp">Query temperature (°C). Must lie within the table range.</param>
    /// <param name="table">List of <see cref="SpecificHeatTablePoint"/> entries that define Cp(T).</param>
    /// <returns>
    /// <c>Ok cp</c> — interpolated specific heat (J⋅kg⁻¹⋅K⁻¹). <br/>
    /// <c>Error InsufficientData</c> — the table is empty or the required bracket is missing. <br/>
    /// <c>Error (OutOfRange (T_min, T_max))</c> — <paramref name="targetTemp"/> is outside the table range.
    /// </returns>
    let interpolate
        (mode: InterpolationMode)
        (targetTemp: float)
        (table: SpecificHeatTablePoint list)
        : Result<float, InterpolationError> =

        if List.isEmpty table then
            Error InterpolationError.InsufficientData
        else
            interpolate1D mode (table |> List.map (fun p -> float p.Temperature, float p.SpecificHeat)) targetTemp

// ========== DENSITY INTERPOLATION ==========

/// <summary>Interpolation functions for temperature-dependent density ρ(T) tables.</summary>
module DensityInterpolation =
    /// <summary>Evaluates density at a target temperature by interpolating a tabulated ρ(T) dataset.</summary>
    /// <param name="mode">Interpolation algorithm to use (see <see cref="InterpolationMode"/>).</param>
    /// <param name="targetTemp">Query temperature (°C). Must lie within the table range.</param>
    /// <param name="table">List of <see cref="DensityTablePoint"/> entries that define ρ(T).</param>
    /// <returns>
    /// <c>Ok rho</c> — interpolated density (kg⋅m⁻³). <br/>
    /// <c>Error InsufficientData</c> — the table is empty or the required bracket is missing. <br/>
    /// <c>Error (OutOfRange (T_min, T_max))</c> — <paramref name="targetTemp"/> is outside the table range.
    /// </returns>
    let interpolate
        (mode: InterpolationMode)
        (targetTemp: float)
        (table: DensityTablePoint list)
        : Result<float, InterpolationError> =

        if List.isEmpty table then
            Error InterpolationError.InsufficientData
        else
            interpolate1D mode (table |> List.map (fun p -> float p.Temperature, float p.Density)) targetTemp

// ========== STRESS-STRAIN TABLE INTERPOLATION ==========

/// <summary>Interpolation functions for stress-strain tables sigma(epsilon) at a given temperature.</summary>
module StressStrainInterpolation =
    /// <summary>Evaluates stress at a target strain by interpolating a stress-strain table.</summary>
    /// <param name="mode">Interpolation algorithm to use (see <see cref="InterpolationMode"/>).</param>
    /// <param name="targetStrain">Query strain (dimensionless, e.g. 0.002 for 0.2%). Must lie within the curve range.</param>
    /// <param name="table">A <see cref="StressStrainTable"/> containing tabulated strain/stress data.</param>
    /// <returns>
    /// <c>Ok σ</c> — interpolated stress (MPa). <br/>
    /// <c>Error InsufficientData</c> — the curve contains no points or the bracket is missing. <br/>
    /// <c>Error (OutOfRange (ε_min, ε_max))</c> — <paramref name="targetStrain"/> is outside the curve range.
    /// </returns>
    let stressFromStrain
        (mode: InterpolationMode)
        (targetStrain: float)
        (table: StressStrainTable)
        : Result<float, InterpolationError> =
        match table.Table.Columns with
        | [ column ] when not (List.isEmpty column.Entries) ->
            column.Entries
            |> List.map (fun entry -> entry.X, entry.Value)
            |> fun points -> interpolate1D mode points targetStrain
        | _ -> Error InterpolationError.InsufficientData

// ========== CREEP CURVE INTERPOLATION ==========

/// <summary>Interpolation functions for experimental creep tables at a given temperature and stress.</summary>
module CreepInterpolation =
    /// <summary>Evaluates creep strain at a target time by interpolating a creep table.</summary>
    /// <param name="mode">Interpolation algorithm to use (see <see cref="InterpolationMode"/>).</param>
    /// <param name="targetTime">Query time (hours). Must lie within the curve's time range.</param>
    /// <param name="table">A <see cref="CreepTable"/> containing tabulated time/strain data.</param>
    /// <returns>
    /// <c>Ok ε</c> — interpolated creep strain (%). <br/>
    /// <c>Error InsufficientData</c> — the curve contains no points or the bracket is missing. <br/>
    /// <c>Error (OutOfRange (t_min, t_max))</c> — <paramref name="targetTime"/> is outside the curve range.
    /// </returns>
    let strainFromTime
        (mode: InterpolationMode)
        (targetTime: float)
        (table: CreepTable)
        : Result<float, InterpolationError> =

        match table.Table.Columns with
        | [ column ] when not (List.isEmpty column.Entries) ->
            interpolate1D mode (column.Entries |> List.map (fun entry -> entry.X, entry.Value)) targetTime
        | _ -> Error InterpolationError.InsufficientData

// ========== STRESS-RUPTURE INTERPOLATION ==========

/// <summary>Interpolation functions for stress-rupture (creep-rupture) curves at a given temperature.</summary>
/// <remarks>
/// Stress-rupture curves plot the stress required to cause fracture in a given time at elevated temperature.
/// They are a key design input for ASME Section II Part D allowable stress determination.
/// </remarks>
module StressRuptureInterpolation =
    /// <summary>Evaluates the rupture stress at a target time to rupture by interpolating a stress-rupture table.</summary>
    /// <param name="mode">Interpolation algorithm to use (see <see cref="InterpolationMode"/>).</param>
    /// <param name="targetTime">Query time to rupture (hours). Must lie within the table's time range.</param>
    /// <param name="table">A <see cref="StressRuptureTable"/> containing the tabulated (t_r, σ_r) data points.</param>
    /// <returns>
    /// <c>Ok σ_r</c> — interpolated rupture stress (MPa). <br/>
    /// <c>Error InsufficientData</c> — the table contains no points or the bracket is missing. <br/>
    /// <c>Error (OutOfRange (t_min, t_max))</c> — <paramref name="targetTime"/> is outside the table range.
    /// </returns>
    let stressFromTimeToRupture
        (mode: InterpolationMode)
        (targetTime: float)
        (table: StressRuptureTable)
        : Result<float, InterpolationError> =

        match table.Table.Columns with
        | [ column ] when not (List.isEmpty column.Entries) ->
            interpolate1D mode (column.Entries |> List.map (fun entry -> entry.X, entry.Value)) targetTime
        | _ -> Error InterpolationError.InsufficientData

// ========== FATIGUE INTERPOLATION ==========

/// <summary>
/// Interpolation rules used for fatigue S-N data.
/// </summary>
/// <remarks>
/// <c>Linear</c> uses the raw cycle/stress axes. <c>LogCycle</c> uses log10(cycles) against stress.
/// <c>LogLog</c> uses log10(cycles) against log10(stress), which matches the usual S-N presentation.
/// </remarks>
type FatigueInterpolationMode =
    | FatigueLinear
    | FatigueLogCycle
    | FatigueLogLog

/// <summary>Interpolation functions for fatigue S-N tables.</summary>
module FatigueInterpolation =

    let private isFinite (value: float) : bool =
        not (Double.IsNaN value || Double.IsInfinity value)

    let private isPositiveFinite (value: float) : bool = isFinite value && value > 0.0

    let private validateFinite (label: string) (value: float) : Result<float, InterpolationError> =
        if isFinite value then
            Ok value
        else
            Error(InterpolationError.InvalidInput(sprintf "%s must be finite" label))

    let private validatePositive (label: string) (value: float) : Result<float, InterpolationError> =
        if isPositiveFinite value then
            Ok value
        else
            Error(InterpolationError.InvalidInput(sprintf "%s must be > 0 for fatigue log interpolation" label))

    let private log10Safe (label: string) (value: float) : Result<float, InterpolationError> =
        validatePositive label value |> Result.map log10

    let private buildTablePairs (table: FatigueTable) : Result<(float * float) list, InterpolationError> =
        if table.Table.DimensionType <> NoDimension then
            Error(InterpolationError.InvalidInput "FatigueTable must wrap a 1D PropertyTable")
        else
            match table.Table.Columns with
            | [ col ] -> Ok(col.Entries |> List.map (fun e -> e.X, e.Value))
            | _ -> Error(InterpolationError.InvalidInput "FatigueTable must contain exactly one column")

    let private interpolateSorted
        (policy: XBoundaryPolicy option)
        (points: (float * float) list)
        (x: float)
        : Result<float, InterpolationError> =

        if not (isFinite x) then
            Error(InterpolationError.InvalidInput "Fatigue query value must be finite")
        elif List.isEmpty points then
            Error InterpolationError.InsufficientData
        else
            let sorted = points |> List.sortBy fst
            let minX = fst (List.head sorted)
            let maxX = fst (List.last sorted)

            if sorted.Length = 1 then
                Ok(snd (List.head sorted))
            elif x < minX then
                match policy with
                | Some FlatExtrapolate -> Ok(snd (List.head sorted))
                | _ -> Error(InterpolationError.OutOfRange(minX, maxX))
            elif x > maxX then
                match policy with
                | Some FlatExtrapolate -> Ok(snd (List.last sorted))
                | _ -> Error(InterpolationError.OutOfRange(minX, maxX))
            else
                match
                    sorted |> List.tryFindBack (fun (xi, _) -> xi <= x), sorted |> List.tryFind (fun (xi, _) -> xi >= x)
                with
                | Some(x0, y0), Some(x1, y1) -> Ok(linearInterpolate x0 y0 x1 y1 x)
                | _ -> Error InterpolationError.InsufficientData

    let private mapPoints
        (xMap: float -> Result<float, InterpolationError>)
        (yMap: float -> Result<float, InterpolationError>)
        (points: (float * float) list)
        : Result<(float * float) list, InterpolationError> =

        let rec loop acc =
            function
            | [] -> Ok(List.rev acc)
            | (x, y) :: rest ->
                match xMap x, yMap y with
                | Ok mx, Ok my -> loop ((mx, my) :: acc) rest
                | Error err, _ -> Error err
                | _, Error err -> Error err

        loop [] points

    let private mapLogCycleTable (table: FatigueTable) : Result<(float * float) list, InterpolationError> =
        buildTablePairs table
        |> Result.bind (fun pairs ->
            mapPoints (log10Safe "FatigueTable cycles") (validateFinite "FatigueTable stress amplitude") pairs)

    let private mapLogLogTable (table: FatigueTable) : Result<(float * float) list, InterpolationError> =
        buildTablePairs table
        |> Result.bind (fun pairs ->
            mapPoints (log10Safe "FatigueTable cycles") (log10Safe "FatigueTable stress amplitude") pairs)

    /// <summary>
    /// Evaluates fatigue stress amplitude Sa at a given cycle count from a fatigue table.
    /// </summary>
    let stressAmplitudeFromCycles
        (mode: FatigueInterpolationMode)
        (targetCycles: float)
        (table: FatigueTable)
        : Result<float, InterpolationError> =

        let policy = Some table.Table.XBoundaryPolicy

        match mode with
        | FatigueLinear ->
            buildTablePairs table
            |> Result.bind (fun mapped ->
                validateFinite "Fatigue query cycles" targetCycles
                |> Result.bind (fun tc -> interpolateSorted policy mapped tc))
        | FatigueLogCycle ->
            match log10Safe "Fatigue query cycles" targetCycles, mapLogCycleTable table with
            | Ok tc, Ok mapped -> interpolateSorted policy mapped tc
            | Error err, _ -> Error err
            | _, Error err -> Error err
        | FatigueLogLog ->
            match log10Safe "Fatigue query cycles" targetCycles, mapLogLogTable table with
            | Ok tc, Ok mapped ->
                interpolateSorted policy mapped tc
                |> Result.map (fun stressLog -> Math.Pow(10.0, stressLog))
            | Error err, _ -> Error err
            | _, Error err -> Error err

    /// <summary>
    /// Evaluates the cycle count associated with a target fatigue stress amplitude Sa from a fatigue table.
    /// </summary>
    let cyclesFromStressAmplitude
        (mode: FatigueInterpolationMode)
        (targetStressAmplitude: float)
        (table: FatigueTable)
        : Result<float, InterpolationError> =

        let policy = Some table.Table.XBoundaryPolicy

        match mode with
        | FatigueLinear ->
            buildTablePairs table
            |> Result.bind (fun pairs ->
                validateFinite "Fatigue query stress amplitude" targetStressAmplitude
                |> Result.bind (fun ts ->
                    interpolateSorted policy (pairs |> List.map (fun (cycles, stress) -> stress, cycles)) ts))
        | FatigueLogCycle ->
            match validateFinite "Fatigue query stress amplitude" targetStressAmplitude, mapLogCycleTable table with
            | Ok ts, Ok mapped ->
                interpolateSorted policy (mapped |> List.map (fun (lc, stress) -> stress, lc)) ts
                |> Result.map (fun v -> Math.Pow(10.0, v))
            | Error err, _ -> Error err
            | _, Error err -> Error err
        | FatigueLogLog ->
            match log10Safe "Fatigue query stress amplitude" targetStressAmplitude, mapLogLogTable table with
            | Ok tsLog, Ok mapped ->
                interpolateSorted policy (mapped |> List.map (fun (lc, ls) -> ls, lc)) tsLog
                |> Result.map (fun cyclesLog -> Math.Pow(10.0, cyclesLog))
            | Error err, _ -> Error err
            | _, Error err -> Error err

// ========== EXTERNAL PRESSURE TABLE INTERPOLATION ==========

/// <summary>Interpolation functions for external-pressure material tables (A vs allowable compressive stress).</summary>
module ExternalPressureTableInterpolation =
    let private linearInterpolateLogX (x0: float) (y0: float) (x1: float) (y1: float) (x: float) : float =
        let lx0 = log10 x0
        let lx1 = log10 x1
        let lx = log10 x

        if lx0 = lx1 then
            y0
        else
            y0 + (y1 - y0) * (lx - lx0) / (lx1 - lx0)

    let private points (table: ExternalPressureTable) =
        table.Table.Columns
        |> List.tryHead
        |> Option.map (fun column ->
            column.Entries
            |> List.map (fun entry ->
                { FactorA = entry.X
                  CompressiveStress = entry.Value
                  TangentModulus = entry.Value / entry.X }))
        |> Option.defaultValue []

    /// <summary>Evaluates allowable compressive stress at a target factor A.</summary>
    /// <param name="mode">Interpolation algorithm to use (see <see cref="InterpolationMode"/>).</param>
    /// <param name="targetFactorA">Query factor A (dimensionless). Must lie within the chart range.</param>
    /// <param name="table">An <see cref="ExternalPressureTable"/> containing tabulated (A, stress) points.</param>
    /// <returns>
    /// <c>Ok Sc</c> — interpolated compressive stress (MPa). <br/>
    /// <c>Error InsufficientData</c> — the chart contains no points or the bracket is missing. <br/>
    /// <c>Error (OutOfRange (A_min, A_max))</c> — <paramref name="targetFactorA"/> is outside the chart range.
    /// </returns>
    let compressiveStressFromFactorA
        (mode: InterpolationMode)
        (targetFactorA: float)
        (table: ExternalPressureTable)
        : Result<float, InterpolationError> =

        let tablePoints = points table

        if List.isEmpty tablePoints then
            Error InterpolationError.InsufficientData
        else
            let sorted = tablePoints |> List.sortBy (fun p -> p.FactorA)
            let minA = (List.head sorted).FactorA
            let maxA = (List.last sorted).FactorA

            if sorted.Length = 1 then
                Ok (List.head sorted).CompressiveStress
            elif targetFactorA < minA || targetFactorA > maxA then
                Error(InterpolationError.OutOfRange(minA, maxA))
            else
                match mode with
                | Constant ->
                    let closest = sorted |> List.minBy (fun p -> abs (p.FactorA - targetFactorA))
                    Ok closest.CompressiveStress

                | Linear
                | CubicSpline
                | LagrangePolynomial _ ->
                    let below = sorted |> List.tryFindBack (fun p -> p.FactorA <= targetFactorA)
                    let above = sorted |> List.tryFind (fun p -> p.FactorA >= targetFactorA)

                    match below, above with
                    | Some p1, Some p2 ->
                        let interp =
                            linearInterpolate
                                p1.FactorA
                                p1.CompressiveStress
                                p2.FactorA
                                p2.CompressiveStress
                                targetFactorA

                        Ok interp
                    | _ -> Error InterpolationError.InsufficientData

    /// <summary>
    /// Evaluates compressive stress Sc at a target factor A using linear interpolation on log10(A).
    /// </summary>
    /// <param name="targetFactorA">Query factor A (dimensionless). Must lie within the chart range and be strictly positive.</param>
    /// <param name="table">An <see cref="ExternalPressureTable"/> containing tabulated (A, stress) points.</param>
    /// <returns>
    /// <c>Ok Sc</c> — interpolated compressive stress (MPa). <br/>
    /// <c>Error InsufficientData</c> — the chart contains no points or the bracket is missing. <br/>
    /// <c>Error (InvalidInput msg)</c> — <paramref name="targetFactorA"/> is not strictly positive. <br/>
    /// <c>Error (OutOfRange (A_min, A_max))</c> — <paramref name="targetFactorA"/> is outside the chart range.
    /// </returns>
    let compressiveStressFromFactorALogScale
        (targetFactorA: float)
        (table: ExternalPressureTable)
        : Result<float, InterpolationError> =

        if
            targetFactorA <= 0.0
            || Double.IsNaN targetFactorA
            || Double.IsInfinity targetFactorA
        then
            Error(
                InterpolationError.InvalidInput
                    "Code Case 2964 factor A must be strictly positive for log-scale interpolation"
            )
        elif List.isEmpty (points table) then
            Error InterpolationError.InsufficientData
        else
            let sorted = points table |> List.sortBy (fun p -> p.FactorA)
            let minA = (List.head sorted).FactorA
            let maxA = (List.last sorted).FactorA

            if sorted.Length = 1 then
                Ok (List.head sorted).CompressiveStress
            elif targetFactorA < minA || targetFactorA > maxA then
                Error(InterpolationError.OutOfRange(minA, maxA))
            else
                let below = sorted |> List.tryFindBack (fun p -> p.FactorA <= targetFactorA)
                let above = sorted |> List.tryFind (fun p -> p.FactorA >= targetFactorA)

                match below, above with
                | Some p1, Some p2 ->
                    let interp =
                        linearInterpolateLogX
                            p1.FactorA
                            p1.CompressiveStress
                            p2.FactorA
                            p2.CompressiveStress
                            targetFactorA

                    Ok interp
                | _ -> Error InterpolationError.InsufficientData

// ========== TEMPERATURE GRID PRESETS ==========

/// Default temperature column presets matching ASME Section II Part D tables.
type TemperatureGrid =
    /// Allowable-stress temperature columns from Table 1A (carbon/low-alloy) and 1B (high-alloy) — °F values.
    | ASME_Table1A_1B
    /// Physical-property temperature columns from Table 5A / 5B — °C (SI) values.
    | ASME_Table5A_5B
    /// Yield-strength (Table Y-1) and tensile-strength (Table U) temperature columns — °C (SI) values.
    | SyAndSu
    /// Uniform grid from T0 to T1 (inclusive) with step deltaT; must satisfy deltaT > 0 and T1 >= T0.
    | CustomRange of T0: float * T1: float * deltaT: float
    /// Caller-supplied explicit temperature list; sorted ascending and de-duplicated on use.
    | ExplicitTemperatures of float list

module TemperatureGrid =
    /// Returns the temperature list for the given preset or custom grid, sorted ascending.
    let toList (grid: TemperatureGrid) : float list =
        match grid with
        | ASME_Table1A_1B ->
            [ 40.0
              65.0
              100.0
              125.0
              150.0
              200.0
              250.0
              300.0
              325.0
              350.0
              375.0
              400.0
              425.0
              450.0
              475.0
              500.0
              525.0
              550.0
              575.0
              600.0
              625.0
              650.0
              675.0
              700.0
              725.0
              750.0
              775.0
              800.0
              825.0
              850.0
              875.0
              900.0 ]
        | ASME_Table5A_5B ->
            [ 20.0
              50.0
              100.0
              150.0
              200.0
              250.0
              300.0
              350.0
              400.0
              450.0
              500.0
              550.0
              600.0
              650.0
              700.0 ]
        | SyAndSu ->
            [ 20.0
              50.0
              100.0
              150.0
              200.0
              250.0
              300.0
              350.0
              400.0
              450.0
              500.0
              550.0
              600.0 ]
        | CustomRange(t0: float, t1: float, dt: float) ->
            let values = [ t0; t1; dt ]

            if
                values |> List.exists (fun value -> Double.IsNaN value || Double.IsInfinity value)
                || dt <= 0.0
                || t1 < t0
            then
                []
            else
                let count = Math.Ceiling((t1 - t0) / dt)

                if count > 1_000_000.0 then
                    []
                else
                    let n = int count
                    [ for i in 0..n -> min t1 (t0 + float i * dt) ] |> List.distinctBy id
        | ExplicitTemperatures temps -> temps |> List.sort |> List.distinct


// ========== PROPERTY TABLE TYPES ==========

/// A (temperature, property-value) knot used as input to all property table types.
type TvPoint = float * float


/// Type 1 — Property as a function of temperature only.
/// Linear interpolation on T; a single input point is extended as a constant for all temperatures.
type PropertyTableType1 =
    {
        /// Input (T, V) knots, sorted ascending by T.
        Points: TvPoint list
        /// true = extend first/last value outside the tabulated range; false = return Double.NaN.
        ExtrapolateFlat: bool
    }

/// Construction and evaluation functions for <see cref="PropertyTableType1"/>.
module PropertyTableType1 =

    /// Creates a Type-1 table, sorting the input knots by temperature.
    let create (points: TvPoint list) (extrapolateFlat: bool) : PropertyTableType1 =
        { Points = points |> List.sortBy fst
          ExtrapolateFlat = extrapolateFlat }

    /// Evaluates the property at temperature t using linear interpolation.
    let evaluate (t: float) (table: PropertyTableType1) : float =
        evalAtTemp table.ExtrapolateFlat table.Points t


/// Type 2 — Property as a function of temperature and a size range (no interpolation on size).
/// Each curve is keyed by the maximum (inclusive) size it applies to.
/// The selected curve is the one with the smallest max-size that is >= the query size.
/// When the query size exceeds all defined max-sizes the last (largest) curve is used.
type PropertyTableType2 =
    {
        /// (maxSize, (T,V) knots) entries, sorted ascending by maxSize.
        Curves: (float * TvPoint list) list
        /// true = extend first/last value outside the tabulated T range; false = return Double.NaN.
        ExtrapolateFlat: bool
    }

/// Construction and evaluation functions for <see cref="PropertyTableType2"/>.
module PropertyTableType2 =

    /// Creates a Type-2 table, sorting the curves by their max-size key.
    let create (curves: (float * TvPoint list) list) (extrapolateFlat: bool) : PropertyTableType2 =
        { Curves = curves |> List.sortBy fst
          ExtrapolateFlat = extrapolateFlat }

    /// Selects the correct size bucket and evaluates the property at temperature t.
    /// No interpolation is performed across size boundaries.
    let evaluate (size: float) (t: float) (table: PropertyTableType2) : float =
        let sorted = table.Curves |> List.sortBy fst

        let pts =
            match sorted |> List.tryFind (fun (maxS, _) -> size <= maxS) with
            | Some(_, pts) -> pts
            | None -> if List.isEmpty sorted then [] else snd (List.last sorted)

        if List.isEmpty pts then
            Double.NaN
        else
            evalAtTemp table.ExtrapolateFlat pts t


/// Type 3 — Property as a function of temperature and a second continuous parameter (double interpolation).
/// Step 1: linear interpolation along T on the two nearest parameter curves.
/// Step 2: linear interpolation between those two T-evaluated values across the second parameter.
type PropertyTableType3 =
    {
        /// (paramValue, (T,V) knots) entries, sorted ascending by parameter value.
        Curves: (float * TvPoint list) list
        /// true = extend first/last value outside the tabulated T range; false = return Double.NaN.
        ExtrapolateFlat: bool
    }

/// Construction and evaluation functions for <see cref="PropertyTableType3"/>.
module PropertyTableType3 =

    /// Creates a Type-3 table, sorting the curves by parameter value.
    let create (curves: (float * TvPoint list) list) (extrapolateFlat: bool) : PropertyTableType3 =
        { Curves = curves |> List.sortBy fst
          ExtrapolateFlat = extrapolateFlat }

    /// Evaluates the property at (param, t) using bilinear (T then param) linear interpolation.
    /// For param outside the defined range the nearest boundary curve is used (flat extension on param).
    let evaluate (param: float) (t: float) (table: PropertyTableType3) : float =
        let sorted = table.Curves |> List.sortBy fst

        match sorted with
        | [] -> Double.NaN
        | [ (_, pts) ] -> evalAtTemp table.ExtrapolateFlat pts t
        | _ ->
            let pMin, ptsMin = List.head sorted
            let pMax, ptsMax = List.last sorted

            if param <= pMin then
                evalAtTemp table.ExtrapolateFlat ptsMin t
            elif param >= pMax then
                evalAtTemp table.ExtrapolateFlat ptsMax t
            else
                let lower = sorted |> List.tryFindBack (fun (p, _) -> p <= param)
                let upper = sorted |> List.tryFind (fun (p, _) -> p >= param)

                match lower, upper with
                | Some(p0, pts0), Some(p1, pts1) ->
                    let v0 = evalAtTemp table.ExtrapolateFlat pts0 t
                    let v1 = evalAtTemp table.ExtrapolateFlat pts1 t

                    if Double.IsNaN v0 || Double.IsNaN v1 then
                        Double.NaN
                    else
                        linearInterpolate p0 v0 p1 v1 param
                | _ -> Double.NaN


// ========== SPLINE AND POLYNOMIAL HELPERS ==========

/// Coefficients for one segment [X0, X_next) of a natural cubic spline.
/// The interpolant on this segment is: S(x) = A + B·(x−X0) + C·(x−X0)² + D·(x−X0)³
type CubicSplineSegment =
    {
        /// Left-knot abscissa of this interval.
        X0: float
        /// Constant coefficient — equals y at the left knot.
        A: float
        /// Linear coefficient.
        B: float
        /// Quadratic coefficient (half the second derivative at X0).
        C: float
        /// Cubic coefficient.
        D: float
    }

/// <summary>
/// Static helpers for spline and polynomial computations on raw (x, y) tabular data.
/// </summary>
[<AbstractClass; Sealed>]
type PropertyTableMath private () =

    /// <summary>
    /// Computes natural cubic spline coefficients for a set of (x, y) knots.
    /// Natural boundary conditions enforce zero second derivative at both ends (c₀ = cₙ = 0).
    /// </summary>
    /// <param name="points">Unsorted list of (x, y) knots; duplicate x-values produce undefined results.</param>
    /// <returns>
    /// One <see cref="CubicSplineSegment"/> per interval between adjacent knots,
    /// or an empty list when fewer than two knots are supplied.
    /// </returns>
    static member CubicSplineCoefficients(points: (float * float) list) : CubicSplineSegment list =
        let sorted = points |> List.sortBy fst
        let n = List.length sorted - 1

        if n < 1 then
            []
        else
            let xs = sorted |> List.map fst |> Array.ofList
            let ys = sorted |> List.map snd |> Array.ofList
            let h = Array.init n (fun i -> xs.[i + 1] - xs.[i])

            // (n+1)-sized tridiagonal system; rows 0 and n are identity rows → c[0]=c[n]=0.
            let lo = Array.zeroCreate (n + 1)
            let diag = Array.create (n + 1) 1.0
            let up = Array.zeroCreate (n + 1)
            let rhs = Array.zeroCreate (n + 1)

            for i in 1 .. n - 1 do
                lo.[i] <- h.[i - 1]
                diag.[i] <- 2.0 * (h.[i - 1] + h.[i])
                up.[i] <- h.[i]
                rhs.[i] <- 3.0 * ((ys.[i + 1] - ys.[i]) / h.[i] - (ys.[i] - ys.[i - 1]) / h.[i - 1])

            // Thomas algorithm — forward elimination.
            let diagW = Array.copy diag
            let rhsW = Array.copy rhs

            for i in 1..n do
                let m = lo.[i] / diagW.[i - 1]
                diagW.[i] <- diagW.[i] - m * up.[i - 1]
                rhsW.[i] <- rhsW.[i] - m * rhsW.[i - 1]

            // Back substitution — second-derivative coefficients c.
            let c = Array.zeroCreate (n + 1)
            c.[n] <- rhsW.[n] / diagW.[n]

            for i in n - 1 .. -1 .. 0 do
                c.[i] <- (rhsW.[i] - up.[i] * c.[i + 1]) / diagW.[i]

            [ for i in 0 .. n - 1 ->
                  let a = ys.[i]
                  let b = (ys.[i + 1] - ys.[i]) / h.[i] - h.[i] * (2.0 * c.[i] + c.[i + 1]) / 3.0
                  let dco = (c.[i + 1] - c.[i]) / (3.0 * h.[i])

                  { X0 = xs.[i]
                    A = a
                    B = b
                    C = c.[i]
                    D = dco } ]

    /// <summary>
    /// Evaluates a cubic spline at the given abscissa using pre-computed segment coefficients.
    /// For x below the first knot the first segment is used; for x above the last knot the last segment is used.
    /// </summary>
    /// <param name="segments">Segment list returned by <see cref="CubicSplineCoefficients"/>.</param>
    /// <param name="x">Query abscissa.</param>
    /// <returns>Interpolated value, or <see cref="Double.NaN"/> when the segment list is empty.</returns>
    static member CubicSplineEvaluate (segments: CubicSplineSegment list) (x: float) : float =
        if List.isEmpty segments then
            Double.NaN
        else
            let sorted = segments |> List.sortBy (fun s -> s.X0)

            let seg =
                sorted
                |> List.tryFindBack (fun s -> s.X0 <= x)
                |> Option.defaultValue (List.head sorted)

            let dx = x - seg.X0
            seg.A + seg.B * dx + seg.C * dx * dx + seg.D * dx * dx * dx

    /// <summary>
    /// Evaluates a Lagrange interpolating polynomial of the specified degree at <paramref name="x"/>.
    /// Selects the <c>degree+1</c> knots whose x-values bracket <paramref name="x"/> most closely,
    /// reducing Runge-phenomenon amplification near dataset boundaries.
    /// </summary>
    /// <param name="degree">Polynomial degree n; requires at least n+1 data points.</param>
    /// <param name="points">Tabulated (x, y) data; need not be sorted.</param>
    /// <param name="x">Query abscissa.</param>
    /// <returns>
    /// Lagrange-interpolated value, or <see cref="Double.NaN"/> when fewer than <c>degree+1</c>
    /// data points are available.
    /// </returns>
    static member LagrangeEvaluate (degree: int) (points: (float * float) list) (x: float) : float =
        let sorted = points |> List.sortBy fst |> Array.ofList
        let m = degree + 1

        if sorted.Length < m then
            Double.NaN
        else
            // Centre a window of m knots around the last knot with xi <= x.
            let pivot =
                sorted
                |> Array.tryFindIndexBack (fun (xi, _) -> xi <= x)
                |> Option.defaultValue 0

            let start = max 0 (min (sorted.Length - m) (pivot - m / 2))
            let knots = sorted.[start .. start + m - 1]

            knots
            |> Array.sumBy (fun (xk, yk) ->
                let basis =
                    knots
                    |> Array.fold
                        (fun acc (xj, _) ->
                            // Skip the self-term; float equality is safe here (same array element).
                            if xj = xk then acc else acc * (x - xj) / (xk - xj))
                        1.0

                yk * basis)
