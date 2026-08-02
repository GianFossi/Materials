namespace MaterialLibrary.Interpolation

open System
open MaterialLibrary.Domain

// Interpolation of ASME external-pressure charts.

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
