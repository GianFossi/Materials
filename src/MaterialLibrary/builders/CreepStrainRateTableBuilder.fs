namespace MaterialLibrary.Domain

open System

/// CULTURE RULE: Numeric parsing and formatting for XML/JSON persistence must always use CultureInfo.InvariantCulture.
/// <summary>
/// Builder functions for constructing creep strain-rate reference tables (temperature vs. stress at
/// a fixed reference creep rate) and attaching them to a material's average/minimum lists.
/// </summary>
module CreepStrainRateTableBuilder =
    let private isFinite (value: float) : bool =
        not (Double.IsNaN value || Double.IsInfinity value)

    /// <summary>Creates a validated temperature-vs-stress creep strain-rate table.</summary>
    /// <param name="referenceCreepRatePercentPer1000Hours">Reference creep-rate criterion (%/1000h), e.g. 0.01.</param>
    /// <param name="description">Human-readable description.</param>
    /// <param name="points">(Temperature degC, Stress MPa) points.</param>
    /// <returns>The constructed <see cref="CreepStrainRateTable"/> or a validation error.</returns>
    let create
        (referenceCreepRatePercentPer1000Hours: float)
        (description: string)
        (points: (float * float) list)
        : Result<CreepStrainRateTable, MaterialError> =
        if not (isFinite referenceCreepRatePercentPer1000Hours) || referenceCreepRatePercentPer1000Hours <= 0.0 then
            Error(MaterialError.InvalidOperation "Reference creep rate must be finite and > 0 %/1000h")
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
            |> Result.map (fun table -> CreepStrainRateTable.create table referenceCreepRatePercentPer1000Hours)
            |> Result.bind CreepStrainRateTable.validate

    let private addOrReplaceIn
        (selector: Material -> CreepStrainRateTable list)
        (updater: Material -> CreepStrainRateTable list -> Material)
        (table: CreepStrainRateTable)
        (material: Material)
        : Result<Material, MaterialError> =
        CreepStrainRateTable.validate table
        |> Result.map (fun validTable ->
            let filtered =
                selector material
                |> List.filter (fun t ->
                    t.ReferenceCreepRatePercentPer1000Hours <> validTable.ReferenceCreepRatePercentPer1000Hours)

            updater
                material
                ((filtered @ [ validTable ])
                 |> List.sortBy (fun t -> t.ReferenceCreepRatePercentPer1000Hours)))

    /// <summary>Adds or replaces one average creep strain-rate table by reference creep rate.</summary>
    /// <param name="table">Table to insert or replace.</param>
    /// <param name="material">The source material to update.</param>
    /// <returns>Updated <see cref="Material"/> with refreshed <c>LastModified</c>, or a validation error.</returns>
    let addOrReplaceAverage (table: CreepStrainRateTable) (material: Material) : Result<Material, MaterialError> =
        addOrReplaceIn
            (fun m -> m.StrengthProperties.AverageCreepStrainRateStress)
            (fun m updated ->
                { m with
                    StrengthProperties =
                        { m.StrengthProperties with
                            AverageCreepStrainRateStress = updated }
                    LastModified = DateTime.UtcNow })
            table
            material

    /// <summary>Adds or replaces one minimum creep strain-rate table by reference creep rate.</summary>
    /// <param name="table">Table to insert or replace.</param>
    /// <param name="material">The source material to update.</param>
    /// <returns>Updated <see cref="Material"/> with refreshed <c>LastModified</c>, or a validation error.</returns>
    let addOrReplaceMinimum (table: CreepStrainRateTable) (material: Material) : Result<Material, MaterialError> =
        addOrReplaceIn
            (fun m -> m.StrengthProperties.MinimumCreepStrainRateStress)
            (fun m updated ->
                { m with
                    StrengthProperties =
                        { m.StrengthProperties with
                            MinimumCreepStrainRateStress = updated }
                    LastModified = DateTime.UtcNow })
            table
            material
