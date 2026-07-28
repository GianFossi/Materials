namespace MaterialLibrary.Domain

open System

/// <summary>
/// Pure helpers for validating and normalizing creep curve points.
/// </summary>
module CreepTableModels =

    let isFinite (value: float) : bool =
        not (Double.IsNaN value || Double.IsInfinity value)

    /// <summary>
    /// Validates and normalizes creep points by sorting them by time.
    /// </summary>
    /// <param name="points">Raw creep points (time h, strain %).</param>
    /// <returns>Sorted points or a <see cref="MaterialError.InvalidOperation"/>.</returns>
    let normalizePoints (points: CreepPoint list) : Result<CreepPoint list, MaterialError> =
        if List.length points < 2 then
            Error(MaterialError.InvalidOperation "Creep curve requires at least two points")
        elif
            points
            |> List.exists (fun p -> not (isFinite p.Time) || not (isFinite p.Strain))
        then
            Error(MaterialError.InvalidOperation "Creep points contain non-finite values")
        elif points |> List.exists (fun p -> p.Time < 0.0) then
            Error(MaterialError.InvalidOperation "Creep time cannot be negative")
        else
            let sorted = points |> List.sortBy (fun p -> p.Time)

            let hasDuplicateTime =
                sorted |> List.pairwise |> List.exists (fun (a, b) -> a.Time = b.Time)

            if hasDuplicateTime then
                Error(MaterialError.InvalidOperation "Creep points contain duplicate time values")
            else
                Ok sorted
