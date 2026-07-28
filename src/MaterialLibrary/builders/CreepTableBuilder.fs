namespace MaterialLibrary.Domain

open System

/// CULTURE RULE: Numeric parsing and formatting for XML/JSON persistence must always use CultureInfo.InvariantCulture.
/// <summary>
/// Builder functions for constructing creep tables in the time domain for a material and stress level.
/// </summary>
module CreepTableBuilder =

    let private normalizePoints (points: CreepPoint list) : Result<CreepPoint list, MaterialError> =
        CreepTableModels.normalizePoints points

    /// <summary>Creates a validated creep table at constant temperature and applied stress.</summary>
    /// <param name="temperature">Test temperature (degC).</param>
    /// <param name="appliedStress">Applied stress (MPa).</param>
    /// <param name="description">Human-readable description.</param>
    /// <param name="points">Creep points (time h, strain %).</param>
    /// <returns>The constructed <see cref="CreepTable"/> or a validation error.</returns>
    let private createWithSource
        (source: CreepTableSource)
        (temperature: float)
        (appliedStress: float)
        (description: string)
        (points: CreepPoint list)
        : Result<CreepTable, MaterialError> =

        if not (CreepTableModels.isFinite temperature) then
            Error(MaterialError.InvalidOperation "Creep temperature must be finite")
        elif not (CreepTableModels.isFinite appliedStress) || appliedStress <= 0.0 then
            Error(MaterialError.InvalidOperation "Applied stress must be > 0 MPa")
        elif String.IsNullOrWhiteSpace description then
            Error(MaterialError.InvalidOperation "Creep description cannot be empty")
        else
            normalizePoints points
            |> Result.bind (fun normalized ->
                PropertyTable.create1D
                    description
                    "Time"
                    "h"
                    "Creep Strain"
                    "%"
                    XBoundaryPolicy.FlatExtrapolate
                    (normalized |> List.map (fun point -> { X = point.Time; Value = point.Strain }))
                |> Result.map (fun table ->
                    CreepTable.createWithAppliedStress
                        table
                        temperature
                        (Some appliedStress)
                        source
                        (Some description))
                |> Result.map (fun table ->
                    { table with
                        ApplicabilityWarning = CreepModelApplicability.warning source })
                |> Result.bind CreepTable.validate)

    /// <summary>Creates a database-backed creep table.</summary>
    let create temperature appliedStress description points =
        createWithSource CreepDatabase temperature appliedStress description points

    /// <summary>Adds or replaces one validated creep table by temperature and applied stress.</summary>
    let addOrReplaceTable (table: CreepTable) (material: Material) : Result<Material, MaterialError> =
        CreepTable.validate table
        |> Result.map (fun _ ->
            let sameKey (other: CreepTable) =
                other.ReferenceTemperature = table.ReferenceTemperature
                && other.AppliedStress = table.AppliedStress

            let filtered =
                material.StrengthProperties.CreepTables |> List.filter (sameKey >> not)

            { material with
                StrengthProperties =
                    { material.StrengthProperties with
                        CreepTables = filtered @ [ table ] }
                LastModified = DateTime.UtcNow })

    /// <summary>
    /// Adds or replaces a validated creep table using key (temperature, applied stress).
    /// </summary>
    let addOrReplace (table: CreepTable) (material: Material) : Result<Material, MaterialError> =
        addOrReplaceTable table material

    /// <summary>Generates a creep table with the explicitly selected Norton model.</summary>
    let generateWithNorton
        (temperature: float)
        (appliedStress: float)
        (description: string)
        (times: float list)
        (A: float)
        (n: float)
        (m: float)
        : Result<CreepTable, MaterialError> =
        times
        |> List.map (fun time ->
            NortonPowerLaw.creepStrain A n m appliedStress time
            |> Result.map (fun strain -> { Time = time; Strain = strain }))
        |> List.fold
            (fun state item ->
                state
                |> Result.bind (fun points -> item |> Result.map (fun point -> point :: points)))
            (Ok [])
        |> Result.bind (List.rev >> createWithSource GeneratedNortonPowerLaw temperature appliedStress description)

    /// <summary>Generates a creep table with the explicitly selected Garofalo model.</summary>
    let generateWithGarofalo
        (temperature: float)
        (appliedStress: float)
        (description: string)
        (times: float list)
        (A: float)
        (n: float)
        (m: float)
        (alpha: float)
        (Q: float)
        : Result<CreepTable, MaterialError> =
        times
        |> List.map (fun time ->
            GarofaloModel.creepStrainWithActivationEnergy A n m alpha Q temperature appliedStress time
            |> Result.map (fun strain -> { Time = time; Strain = strain }))
        |> List.fold
            (fun state item ->
                state
                |> Result.bind (fun points -> item |> Result.map (fun point -> point :: points)))
            (Ok [])
        |> Result.bind (List.rev >> createWithSource GeneratedGarofalo temperature appliedStress description)

    /// <summary>
    /// Generates a secondary-to-tertiary creep table using the explicitly selected Kachanov-Omega model.
    /// Primary creep is not represented by this implementation.
    /// </summary>
    let generateWithKachanovOmega
        (temperature: float)
        (appliedStress: float)
        (description: string)
        (timeSteps: int)
        (totalTime: float)
        (A1: float)
        (N1: float)
        (M1: float)
        (A2: float)
        (N2: float)
        (M2: float)
        : Result<CreepTable, MaterialError> =
        KachanovOmega.creepStrainWithDamage
            A1
            N1
            M1
            A2
            N2
            M2
            appliedStress
            timeSteps
            totalTime
        |> Result.bind (fun strains ->
            strains
            |> List.mapi (fun index strain ->
                { Time = totalTime * float index / float timeSteps
                  Strain = strain })
            |> createWithSource GeneratedKachanovOmega temperature appliedStress description)
