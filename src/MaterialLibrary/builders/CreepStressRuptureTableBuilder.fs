namespace MaterialLibrary.Domain

open System

/// CULTURE RULE: Numeric parsing and formatting for XML/JSON persistence must always use CultureInfo.InvariantCulture.
/// <summary>
/// Builder functions for constructing creep stress-rupture reference tables (temperature vs. stress
/// at a fixed reference duration) and attaching them to a material's average/minimum lists.
/// </summary>
module CreepStressRuptureTableBuilder =
    let private isFinite (value: float) : bool =
        not (Double.IsNaN value || Double.IsInfinity value)

    /// <summary>Creates a validated temperature-vs-stress creep stress-rupture table.</summary>
    /// <param name="referenceDurationHours">Reference duration for the rupture criterion (hours), e.g. 100000.</param>
    /// <param name="description">Human-readable description.</param>
    /// <param name="points">(Temperature degC, Stress MPa) points.</param>
    /// <returns>The constructed <see cref="CreepStressRuptureTable"/> or a validation error.</returns>
    let create
        (referenceDurationHours: float)
        (description: string)
        (points: (float * float) list)
        : Result<CreepStressRuptureTable, MaterialError> =
        if not (isFinite referenceDurationHours) || referenceDurationHours <= 0.0 then
            Error(MaterialError.InvalidOperation "Reference duration must be finite and > 0 hours")
        elif String.IsNullOrWhiteSpace description then
            Error(MaterialError.InvalidOperation "Description cannot be empty")
        else
            PropertyTable.create1D
                description
                "Temperature"
                "degC"
                "Stress"
                "MPa"
                XBoundaryPolicy.FlatExtrapolate
                (points |> List.map (fun (temperature, stress) -> PropertyTable.entry temperature stress))
            |> Result.map (fun table -> CreepStressRuptureTable.create table referenceDurationHours)
            |> Result.bind CreepStressRuptureTable.validate

    let private addOrReplaceIn
        (selector: Material -> CreepStressRuptureTable list)
        (updater: Material -> CreepStressRuptureTable list -> Material)
        (table: CreepStressRuptureTable)
        (material: Material)
        : Result<Material, MaterialError> =
        CreepStressRuptureTable.validate table
        |> Result.map (fun validTable ->
            let filtered =
                selector material
                |> List.filter (fun t -> t.ReferenceDurationHours <> validTable.ReferenceDurationHours)

            updater material ((filtered @ [ validTable ]) |> List.sortBy (fun t -> t.ReferenceDurationHours)))

    /// <summary>Adds or replaces one average creep stress-rupture table by reference duration.</summary>
    /// <param name="table">Table to insert or replace.</param>
    /// <param name="material">The source material to update.</param>
    /// <returns>Updated <see cref="Material"/> with refreshed <c>LastModified</c>, or a validation error.</returns>
    let addOrReplaceAverage (table: CreepStressRuptureTable) (material: Material) : Result<Material, MaterialError> =
        addOrReplaceIn
            (fun m -> m.StrengthProperties.AverageCreepRuptureStress)
            (fun m updated ->
                { m with
                    StrengthProperties =
                        { m.StrengthProperties with
                            AverageCreepRuptureStress = updated }
                    LastModified = DateTime.UtcNow })
            table
            material

    /// <summary>Adds or replaces one minimum creep stress-rupture table by reference duration.</summary>
    /// <param name="table">Table to insert or replace.</param>
    /// <param name="material">The source material to update.</param>
    /// <returns>Updated <see cref="Material"/> with refreshed <c>LastModified</c>, or a validation error.</returns>
    let addOrReplaceMinimum (table: CreepStressRuptureTable) (material: Material) : Result<Material, MaterialError> =
        addOrReplaceIn
            (fun m -> m.StrengthProperties.MinimumCreepRuptureStress)
            (fun m updated ->
                { m with
                    StrengthProperties =
                        { m.StrengthProperties with
                            MinimumCreepRuptureStress = updated }
                    LastModified = DateTime.UtcNow })
            table
            material
