namespace MaterialLibrary.Interpolation

open System
open MaterialLibrary.Domain

// Fatigue-curve interpolation and its dedicated mode selector.

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
