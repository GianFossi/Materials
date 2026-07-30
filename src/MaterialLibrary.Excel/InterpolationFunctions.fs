namespace MaterialLibrary.Excel

open System
open ExcelDna.Integration
open MaterialLibrary.Domain
open MaterialLibrary.Interpolation

/// <summary>
/// Excel worksheet functions exposing the interpolation algorithms from <c>Interpolations.fs</c>
/// directly: generic mode-aware interpolation over arbitrary user data (<c>PropertyTableMath</c>,
/// <c>TemperatureGrid</c>), and material-bound stress-strain/creep/stress-rupture lookups that
/// support the full <see cref="InterpolationMode"/> range rather than the always-linear
/// <c>PropertyTable.lookup1D</c> path used by the corresponding functions in
/// <c>StrengthPropertyFunctions</c>.
/// </summary>
module InterpolationFunctions =

    let private ofNumeric (v: obj) : float option =
        match v with
        | :? float as d -> Some d
        | :? int as i -> Some(float i)
        | _ -> None

    /// Reads a range argument as a flat array of numbers, regardless of whether Excel-DNA marshaled
    /// it as a 1D array (obj[]) or a 2D range (obj[,]).
    let private flattenNumeric (value: obj) : float[] =
        match value with
        | :? (obj[]) as arr -> arr |> Array.choose ofNumeric
        | :? (obj[,]) as arr2 ->
            [| for i in 0 .. Array2D.length1 arr2 - 1 do
                   for j in 0 .. Array2D.length2 arr2 - 1 do
                       match ofNumeric arr2.[i, j] with
                       | Some v -> yield v
                       | None -> () |]
        | _ -> ofNumeric value |> Option.toArray

    let private zipXy (xValues: obj) (yValues: obj) : (float * float) list =
        let xs = flattenNumeric xValues
        let ys = flattenNumeric yValues
        let n = min xs.Length ys.Length
        [ for i in 0 .. n - 1 -> xs.[i], ys.[i] ]

    /// <summary>
    /// Interpolates arbitrary (x, y) pairs under the requested <see cref="InterpolationMode"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately reuses the public <see cref="PropertyTableMath"/> spline/Lagrange machinery
    /// instead of re-deriving it, so this is the one place in the Excel add-in that exposes the full
    /// mode range on data the material domain model does not own.
    /// </remarks>
    let private interpolateGeneric (mode: InterpolationMode) (points: (float * float) list) (x: float) : Result<float, string> =
        let sorted = points |> List.sortBy fst

        match sorted with
        | [] -> Error "No data points supplied"
        | [ (_, y) ] -> Ok y
        | _ ->
            let minX = fst (List.head sorted)
            let maxX = fst (List.last sorted)

            if x < minX || x > maxX then
                Error(sprintf "x=%.6g is outside the supplied range [%.6g, %.6g]" x minX maxX)
            else
                match mode with
                | Constant -> sorted |> List.minBy (fun (xi, _) -> abs (xi - x)) |> snd |> Ok
                | Linear ->
                    match sorted |> List.tryFindBack (fun (xi, _) -> xi <= x), sorted |> List.tryFind (fun (xi, _) -> xi >= x) with
                    | Some(x0, y0), Some(x1, y1) -> Ok(if x0 = x1 then y0 else y0 + (y1 - y0) * (x - x0) / (x1 - x0))
                    | _ -> Error "Could not bracket the query value"
                | CubicSpline ->
                    let value = PropertyTableMath.CubicSplineEvaluate (PropertyTableMath.CubicSplineCoefficients sorted) x

                    if Double.IsNaN value then Error "Cubic-spline evaluation failed" else Ok value
                | LagrangePolynomial degree ->
                    let value = PropertyTableMath.LagrangeEvaluate degree sorted x

                    if Double.IsNaN value then
                        Error(sprintf "Lagrange degree %d needs at least %d points" degree (degree + 1))
                    else
                        Ok value

    // ── Generic interpolation over arbitrary worksheet data ───────────────

    [<ExcelFunction(Category = "MaterialLibrary.Interpolation", Description = "Interpolates y at x from arbitrary (x, y) data ranges. mode: Linear (default), CubicSpline, Constant, Lagrange.")>]
    let MatInterpolate
        ([<ExcelArgument(Description = "Range or array of x values.")>] xValues: obj)
        ([<ExcelArgument(Description = "Range or array of y values, same length as xValues.")>] yValues: obj)
        ([<ExcelArgument(Description = "Query x value.")>] x: float)
        ([<ExcelArgument(Description = "Interpolation mode: Linear (default), CubicSpline, Constant, Lagrange.")>] mode: obj)
        ([<ExcelArgument(Description = "Lagrange polynomial degree, used only when mode is Lagrange (default 3).")>] lagrangeDegree: obj)
        : obj =
        match interpolateGeneric (Args.interpolationMode mode lagrangeDegree) (zipXy xValues yValues) x with
        | Ok value -> box value
        | Error message -> box (sprintf "#VALUE! %s" message)

    [<ExcelFunction(Category = "MaterialLibrary.Interpolation", Description = "Natural cubic-spline interpolation of y at x from arbitrary (x, y) data ranges (zero second derivative at both ends).")>]
    let MatCubicSplineInterpolate
        ([<ExcelArgument(Description = "Range or array of x values.")>] xValues: obj)
        ([<ExcelArgument(Description = "Range or array of y values, same length as xValues.")>] yValues: obj)
        ([<ExcelArgument(Description = "Query x value.")>] x: float)
        : obj =
        match interpolateGeneric CubicSpline (zipXy xValues yValues) x with
        | Ok value -> box value
        | Error message -> box (sprintf "#VALUE! %s" message)

    [<ExcelFunction(Category = "MaterialLibrary.Interpolation", Description = "Lagrange polynomial interpolation of y at x from arbitrary (x, y) data ranges, using the degree+1 knots nearest x.")>]
    let MatLagrangeInterpolate
        ([<ExcelArgument(Description = "Range or array of x values.")>] xValues: obj)
        ([<ExcelArgument(Description = "Range or array of y values, same length as xValues.")>] yValues: obj)
        ([<ExcelArgument(Description = "Query x value.")>] x: float)
        ([<ExcelArgument(Description = "Polynomial degree (default 3); requires at least degree+1 data points.")>] degree: obj)
        : obj =
        match interpolateGeneric (LagrangePolynomial(Args.optionalNumber 3.0 degree |> int)) (zipXy xValues yValues) x with
        | Ok value -> box value
        | Error message -> box (sprintf "#VALUE! %s" message)

    // ── Preset/custom temperature grids ───────────────────────────────────

    [<ExcelFunction(Category = "MaterialLibrary.Interpolation", Description = "Returns a preset ASME temperature grid, or a custom evenly-spaced grid. preset: ASME_Table1A_1B (degF), ASME_Table5A_5B (degC), SyAndSu (degC), Custom (uses t0/t1/dt, degC).")>]
    let MatTemperatureGrid
        ([<ExcelArgument(Description = "Preset name: ASME_Table1A_1B, ASME_Table5A_5B, SyAndSu, Custom.")>] preset: string)
        ([<ExcelArgument(Description = "Custom grid start T0, degC (Custom preset only).")>] t0: obj)
        ([<ExcelArgument(Description = "Custom grid end T1, degC (Custom preset only).")>] t1: obj)
        ([<ExcelArgument(Description = "Custom grid step, degC (Custom preset only).")>] dt: obj)
        : obj[] =
        let grid =
            match preset.Trim().ToLowerInvariant() with
            | "asme_table1a_1b" -> TemperatureGrid.toList ASME_Table1A_1B
            | "asme_table5a_5b" -> TemperatureGrid.toList ASME_Table5A_5B
            | "syandsu" -> TemperatureGrid.toList SyAndSu
            | "custom" ->
                TemperatureGrid.toList (
                    CustomRange(Args.optionalNumber 0.0 t0, Args.optionalNumber 0.0 t1, Args.optionalNumber 1.0 dt)
                )
            | _ -> []

        if List.isEmpty grid then
            [| box (sprintf "#VALUE! Unknown or empty preset: %s" preset) |]
        else
            grid |> List.map box |> Array.ofList

    // ── Material-bound, mode-aware lookups (StressStrainInterpolation, CreepInterpolation, StressRuptureInterpolation) ──

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Interpolated stress (MPa) at a given strain (%), using the requested interpolation mode. Unlike MatStressFromStrain (always linear), this supports CubicSpline and Lagrange.")>]
    let MatStressFromStrainMode
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Curve temperature, degC (exact match).")>] temperatureC: float)
        ([<ExcelArgument(Description = "Query engineering strain, %.")>] strainPercent: float)
        ([<ExcelArgument(Description = "Isochronous reference duration, hours; blank selects the time-independent curve.")>] durationHours: obj)
        ([<ExcelArgument(Description = "Interpolation mode: Linear (default), CubicSpline, Constant, Lagrange.")>] mode: obj)
        ([<ExcelArgument(Description = "Lagrange polynomial degree, used only when mode is Lagrange (default 3).")>] lagrangeDegree: obj)
        : obj =
        let interpolationMode = Args.interpolationMode mode lagrangeDegree
        let durationOption = Args.optionalNumberOption durationHours

        ExcelHelpers.withMaterial materialId (fun material ->
            match
                material.StrengthProperties.StressStrainTables
                |> List.tryFind (fun t -> t.ReferenceTemperature = temperatureC && t.ReferenceDurationHours = durationOption)
            with
            | None ->
                Error(
                    MaterialError.InvalidOperation(
                        sprintf "No stress-strain table at %.4g degC and duration %A" temperatureC durationOption
                    )
                )
            | Some table ->
                StressStrainInterpolation.stressFromStrain interpolationMode strainPercent table
                |> Result.mapError MaterialError.InterpolationError)
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Interpolated creep strain (%) at a given time (hours) on the experimental curve matching the applied stress (MPa), using the requested interpolation mode. Unlike MatCreepStrainFromCurve (always linear), this supports CubicSpline and Lagrange.")>]
    let MatCreepStrainFromCurveMode
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Applied stress, MPa (exact match to a stored curve).")>] appliedStressMPa: float)
        ([<ExcelArgument(Description = "Query elapsed time, hours.")>] timeHours: float)
        ([<ExcelArgument(Description = "Interpolation mode: Linear (default), CubicSpline, Constant, Lagrange.")>] mode: obj)
        ([<ExcelArgument(Description = "Lagrange polynomial degree, used only when mode is Lagrange (default 3).")>] lagrangeDegree: obj)
        : obj =
        let interpolationMode = Args.interpolationMode mode lagrangeDegree

        ExcelHelpers.withMaterial materialId (fun material ->
            match
                material.StrengthProperties.CreepTables
                |> List.tryFind (fun t -> t.AppliedStress = Some appliedStressMPa)
            with
            | None -> Error(MaterialError.InvalidOperation(sprintf "No creep curve for %.4g MPa" appliedStressMPa))
            | Some table ->
                CreepInterpolation.strainFromTime interpolationMode timeHours table
                |> Result.mapError MaterialError.InterpolationError)
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Interpolated rupture stress (MPa) at a given time to rupture (hours) at an exact temperature (degC), using the requested interpolation mode. Unlike MatStressRupture (always linear), this supports CubicSpline and Lagrange.")>]
    let MatStressRuptureMode
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Curve temperature, degC (exact match).")>] temperatureC: float)
        ([<ExcelArgument(Description = "Query time to rupture, hours.")>] timeToRuptureHours: float)
        ([<ExcelArgument(Description = "Interpolation mode: Linear (default), CubicSpline, Constant, Lagrange.")>] mode: obj)
        ([<ExcelArgument(Description = "Lagrange polynomial degree, used only when mode is Lagrange (default 3).")>] lagrangeDegree: obj)
        : obj =
        let interpolationMode = Args.interpolationMode mode lagrangeDegree

        ExcelHelpers.withMaterial materialId (fun material ->
            match
                material.StrengthProperties.StressRuptureCurves
                |> List.tryFind (fun t -> t.ReferenceTemperature = temperatureC)
            with
            | None -> Error(MaterialError.InvalidOperation(sprintf "No stress-rupture curve at %.4g degC" temperatureC))
            | Some table ->
                StressRuptureInterpolation.stressFromTimeToRupture interpolationMode timeToRuptureHours table
                |> Result.mapError MaterialError.InterpolationError)
        |> ExcelHelpers.ofFloatResult
