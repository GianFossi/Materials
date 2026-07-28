namespace MaterialLibrary.Domain

open System

/// CULTURE RULE: Numeric parsing and formatting for XML/JSON persistence must always use CultureInfo.InvariantCulture.
/// <summary>
/// Builder functions for constructing time-independent and isochronous stress-strain tables.
/// </summary>
module StressStrainTableBuilder =

    let private normalizePoints (points: StressStrainPoint list) : Result<StressStrainPoint list, MaterialError> =
        StressStrainModels.normalizePoints points

    let private basisCode basis =
        match basis with
        | Engineering -> 1
        | True -> 2

    let private create
        temperature
        referenceDurationHours
        source
        strainBasis
        stressBasis
        description
        points
        yieldStress
        ultimateStress
        =
        if not (StressStrainModels.isFinite temperature) then
            Error(MaterialError.InvalidOperation "Stress-strain temperature must be finite")
        elif String.IsNullOrWhiteSpace description then
            Error(MaterialError.InvalidOperation "Stress-strain description cannot be empty")
        else
            normalizePoints points
            |> Result.bind (fun normalized ->
                PropertyTable.create1D
                    (sprintf "Stress-Strain - %s" description)
                    "Strain"
                    "%"
                    "Stress"
                    "MPa"
                    XBoundaryPolicy.FlatExtrapolate
                    (normalized |> List.map (fun point -> { X = point.Strain; Value = point.Stress }))
                |> Result.map (fun table ->
                    StressStrainTable.createWithMetadata
                        table
                        temperature
                        referenceDurationHours
                        source
                        (basisCode strainBasis)
                        (basisCode stressBasis)
                        yieldStress
                        ultimateStress)
                |> Result.bind StressStrainTable.validate)

    /// <summary>
    /// Creates a time-independent stress-strain table for a specific material temperature.
    /// </summary>
    /// <param name="temperature">Test temperature (degC).</param>
    /// <param name="strainBasis">Strain basis (Engineering or True).</param>
    /// <param name="stressBasis">Stress basis (Engineering or True).</param>
    /// <param name="description">Human-readable description.</param>
    /// <param name="points">Stress-strain points (strain %, stress MPa).</param>
    /// <param name="yieldStress">Optional 0.2% proof stress (MPa).</param>
    /// <param name="ultimateStress">Optional ultimate stress (MPa).</param>
    /// <returns>The constructed <see cref="StressStrainTable"/> or a validation error.</returns>
    let createTimeIndependent
        (temperature: float)
        (strainBasis: StressStrainBasis)
        (stressBasis: StressStrainBasis)
        (description: string)
        (points: StressStrainPoint list)
        (yieldStress: float option)
        (ultimateStress: float option)
        : Result<StressStrainTable, MaterialError> =
        create
            temperature
            None
            StressStrainDatabase
            strainBasis
            stressBasis
            description
            points
            yieldStress
            ultimateStress

    /// <summary>
    /// Creates an isochronous stress-strain table for a specific temperature and reference duration.
    /// </summary>
    /// <param name="temperature">Test temperature (degC).</param>
    /// <param name="referenceDurationHours">Reference duration (hours) for creep-dependent dataset.</param>
    /// <param name="strainBasis">Strain basis (Engineering or True).</param>
    /// <param name="stressBasis">Stress basis (Engineering or True).</param>
    /// <param name="description">Human-readable description.</param>
    /// <param name="points">Stress-strain points (strain %, stress MPa).</param>
    /// <param name="yieldStress">Optional 0.2% proof stress (MPa).</param>
    /// <param name="ultimateStress">Optional ultimate stress (MPa).</param>
    /// <returns>The constructed <see cref="StressStrainTable"/> or a validation error.</returns>
    let createIsochronous
        (temperature: float)
        (referenceDurationHours: float)
        (strainBasis: StressStrainBasis)
        (stressBasis: StressStrainBasis)
        (description: string)
        (points: StressStrainPoint list)
        (yieldStress: float option)
        (ultimateStress: float option)
        : Result<StressStrainTable, MaterialError> =

        if
            not (StressStrainModels.isFinite referenceDurationHours)
            || referenceDurationHours <= 0.0
        then
            Error(
                MaterialError.InvalidOperation
                    "Reference duration must be > 0 hours for time-dependent stress-strain curves"
            )
        else
            create
                temperature
                (Some referenceDurationHours)
                StressStrainDatabase
                strainBasis
                stressBasis
                description
                points
                yieldStress
                ultimateStress

    /// <summary>Adds or replaces a validated stress-strain table by temperature and optional duration.</summary>
    let addOrReplaceTable
        (table: StressStrainTable)
        (material: Material)
        : Result<Material, MaterialError> =
        StressStrainTable.validate table
        |> Result.map (fun validTable ->
            let sameKey (other: StressStrainTable) =
                other.ReferenceTemperature = validTable.ReferenceTemperature
                && other.ReferenceDurationHours = validTable.ReferenceDurationHours

            let filtered =
                material.StrengthProperties.StressStrainTables |> List.filter (sameKey >> not)

            { material with
                StrengthProperties =
                    { material.StrengthProperties with
                        StressStrainTables = filtered @ [ validTable ] }
                LastModified = DateTime.UtcNow })

    /// <summary>Adds or replaces a validated stress-strain table.</summary>
    let addOrReplace (table: StressStrainTable) (material: Material) : Result<Material, MaterialError> =
        addOrReplaceTable table material
