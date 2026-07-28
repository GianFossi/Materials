namespace MaterialLibrary.Domain

open System

/// CULTURE RULE: Numeric parsing and formatting for XML/JSON persistence must always use CultureInfo.InvariantCulture.
/// <summary>
/// Builder functions for constructing cyclic stress-strain tables and attaching them to a material.
/// </summary>
module CyclicStrainTableBuilder =

    let private isFinite (v: float) =
        not (Double.IsNaN v || Double.IsInfinity v)

    // ── point normalisation ──────────────────────────────────────────────────

    let private normalizeCyclicPoints
        (points: CyclicStressStrainPoint list)
        : Result<CyclicStressStrainPoint list, MaterialError> =

        if List.length points < 2 then
            Error(MaterialError.InvalidOperation "Cyclic curve requires at least two points")
        elif
            points
            |> List.exists (fun p ->
                not (isFinite p.StressAmplitude)
                || not (isFinite p.StrainAmplitude)
                || p.StressAmplitude <= 0.0
                || p.StrainAmplitude <= 0.0)
        then
            Error(MaterialError.InvalidOperation "Cyclic curve points contain invalid values")
        else
            let sorted = points |> List.sortBy (fun p -> p.StressAmplitude)

            let hasDuplicate =
                sorted
                |> List.pairwise
                |> List.exists (fun (a, b) -> a.StressAmplitude = b.StressAmplitude)

            if hasDuplicate then
                Error(MaterialError.InvalidOperation "Cyclic curve points contain duplicate stress-amplitude values")
            else
                Ok sorted

    let private normalizeHysteresisPoints
        (points: HysteresisRangePoint list)
        : Result<HysteresisRangePoint list, MaterialError> =

        if List.length points < 2 then
            Error(MaterialError.InvalidOperation "Hysteresis loop requires at least two points")
        elif
            points
            |> List.exists (fun p ->
                not (isFinite p.StressRange)
                || not (isFinite p.StrainRange)
                || p.StressRange <= 0.0
                || p.StrainRange <= 0.0)
        then
            Error(MaterialError.InvalidOperation "Hysteresis loop points contain invalid values")
        else
            let sorted = points |> List.sortBy (fun p -> p.StressRange)

            let hasDuplicate =
                sorted
                |> List.pairwise
                |> List.exists (fun (a, b) -> a.StressRange = b.StressRange)

            if hasDuplicate then
                Error(MaterialError.InvalidOperation "Hysteresis loop points contain duplicate stress-range values")
            else
                Ok sorted

    let private interpolateRange (points: HysteresisRangePoint list) (stressRange: float) =
        if stressRange = 0.0 then
            0.0
        else
            let augmented = { StressRange = 0.0; StrainRange = 0.0 } :: points
            let below = augmented |> List.tryFindBack (fun point -> point.StressRange <= stressRange)
            let above = augmented |> List.tryFind (fun point -> point.StressRange >= stressRange)

            match below, above with
            | Some lower, Some upper when lower.StressRange = upper.StressRange -> lower.StrainRange
            | Some lower, Some upper ->
                lower.StrainRange
                + (upper.StrainRange - lower.StrainRange)
                  * (stressRange - lower.StressRange)
                  / (upper.StressRange - lower.StressRange)
            | _ -> nan

    let private buildHysteresisLoops
        (cyclicPoints: CyclicStressStrainPoint list)
        (rangePoints: HysteresisRangePoint list)
        : Result<HysteresisLoop list, MaterialError> =
        let maxRange = rangePoints |> List.last |> fun point -> point.StressRange

        cyclicPoints
        |> List.map (fun cyclicPoint ->
            let fullStressRange = 2.0 * cyclicPoint.StressAmplitude

            if fullStressRange > maxRange then
                Error(
                    MaterialError.InvalidOperation
                        "Hysteresis range data must cover twice every cyclic stress amplitude"
                )
            else
                let stressIncrements =
                    0.0
                    :: (rangePoints
                        |> List.map (fun point -> point.StressRange)
                        |> List.filter (fun stressRange -> stressRange > 0.0 && stressRange < fullStressRange))
                    @ [ fullStressRange ]

                let loading =
                    stressIncrements
                    |> List.map (fun increment ->
                        ({ Strain = -cyclicPoint.StrainAmplitude + interpolateRange rangePoints increment
                           Stress = -cyclicPoint.StressAmplitude + increment
                           Branch = Loading }: HysteresisLoopPoint))

                let unloading =
                    stressIncrements
                    |> List.map (fun increment ->
                        ({ Strain = cyclicPoint.StrainAmplitude - interpolateRange rangePoints increment
                           Stress = cyclicPoint.StressAmplitude - increment
                           Branch = Unloading }: HysteresisLoopPoint))

                Ok
                    ({ StressAmplitude = cyclicPoint.StressAmplitude
                       StrainAmplitude = cyclicPoint.StrainAmplitude
                       Points = loading @ unloading }: HysteresisLoop))
        |> List.fold
            (fun state item ->
                state
                |> Result.bind (fun loops -> item |> Result.map (fun loop -> loop :: loops)))
            (Ok [])
        |> Result.map List.rev

    // ── construction helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Creates one validated cyclic table from explicitly provided point lists.
    /// </summary>
    /// <param name="temperature">Assessment temperature (degC).</param>
    /// <param name="kcss">K_css — cyclic strength coefficient (MPa).</param>
    /// <param name="ncss">n_css — cyclic strain hardening exponent (dimensionless).</param>
    /// <param name="materialDescription">Material/grade description matching Table 3-D.2M.</param>
    /// <param name="description">Human-readable description.</param>
    /// <param name="cyclicPoints">Cyclic strain amplitude points (σ_a, ε_ta).</param>
    /// <param name="hysteresisPoints">Hysteresis loop points (σ_r, ε_tr).</param>
    /// <returns>Validated <see cref="CyclicStrainTable"/> or a validation error.</returns>
    let create
        (temperature: float)
        (kcss: float)
        (ncss: float)
        (materialDescription: string)
        (description: string)
        (cyclicPoints: CyclicStressStrainPoint list)
        (hysteresisPoints: HysteresisRangePoint list)
        : Result<CyclicStrainTable, MaterialError> =

        if not (isFinite temperature) then
            Error(MaterialError.InvalidOperation "Cyclic strain table temperature must be finite")
        elif kcss <= 0.0 then
            Error(MaterialError.InvalidOperation "Kcss must be > 0 MPa")
        elif ncss <= 0.0 then
            Error(MaterialError.InvalidOperation "Ncss must be > 0")
        elif String.IsNullOrWhiteSpace materialDescription then
            Error(MaterialError.InvalidOperation "MaterialDescription cannot be empty")
        elif String.IsNullOrWhiteSpace description then
            Error(MaterialError.InvalidOperation "Description cannot be empty")
        else
            match normalizeCyclicPoints cyclicPoints, normalizeHysteresisPoints hysteresisPoints with
            | Error err, _ -> Error err
            | _, Error err -> Error err
            | Ok sortedCyclic, Ok sortedHysteresis ->
                let loops = buildHysteresisLoops sortedCyclic sortedHysteresis

                PropertyTable.create1D
                    description
                    "Stress Amplitude"
                    "MPa"
                    "Strain Amplitude"
                    ""
                    XBoundaryPolicy.FlatExtrapolate
                    (sortedCyclic
                     |> List.map (fun point ->
                         { X = point.StressAmplitude
                           Value = point.StrainAmplitude }))
                |> Result.bind (fun cyclicTable ->
                    PropertyTable.create1D
                        $"{description} - Hysteresis"
                        "Stress Range"
                        "MPa"
                        "Strain Range"
                        ""
                        XBoundaryPolicy.FlatExtrapolate
                        (sortedHysteresis
                         |> List.map (fun point ->
                             { X = point.StressRange
                               Value = point.StrainRange }))
                    |> Result.bind (fun hysteresisTable ->
                        loops
                        |> Result.map (fun hysteresisLoops ->
                            CyclicStrainTable.create
                                cyclicTable
                                hysteresisTable
                                hysteresisLoops
                                temperature
                                kcss
                                ncss
                                materialDescription
                                description))
                    |> Result.bind CyclicStrainTable.validate)

    /// <summary>
    /// Generates a <see cref="CyclicStrainTable"/> analytically from Table 3-D.2M parameters using
    /// <see cref="CyclicStrainModel.generateCyclicPoints"/> and <see cref="CyclicStrainModel.generateHysteresisPoints"/>.
    /// </summary>
    /// <param name="temperature">Assessment temperature (degC).</param>
    /// <param name="elasticModulus">Modulus of elasticity E_y at temperature (MPa).</param>
    /// <param name="kcss">K_css from Table 3-D.2M (MPa).</param>
    /// <param name="ncss">n_css from Table 3-D.2M (dimensionless).</param>
    /// <param name="materialDescription">Material/grade tag matching Table 3-D.2M.</param>
    /// <param name="minStress">Lower bound of the stress grid (MPa).</param>
    /// <param name="maxStress">Upper bound of the stress grid (MPa).</param>
    /// <param name="pointCount">Number of log-spaced points to generate (minimum 2).</param>
    /// <returns>Generated <see cref="CyclicStrainTable"/> or a validation error.</returns>
    let generate
        (temperature: float)
        (elasticModulus: float)
        (kcss: float)
        (ncss: float)
        (materialDescription: string)
        (minStress: float)
        (maxStress: float)
        (pointCount: int)
        : Result<CyclicStrainTable, MaterialError> =

        let input =
            { Kcss = kcss
              Ncss = ncss
              ElasticModulus = elasticModulus }

        match
            CyclicStrainModel.generateCyclicPoints input minStress maxStress pointCount,
            CyclicStrainModel.generateHysteresisPoints input (2.0 * minStress) (2.0 * maxStress) pointCount
        with
        | Error err, _ -> Error err
        | _, Error err -> Error err
        | Ok cyclicPoints, Ok hysteresisPoints ->
            let description =
                sprintf "Generated cyclic curve — K_css=%.1f MPa, n_css=%.4f @ %.0f degC" kcss ncss temperature

            create temperature kcss ncss materialDescription description cyclicPoints hysteresisPoints

    // ── material attachment ──────────────────────────────────────────────────

    /// <summary>
    /// Adds or replaces a <see cref="CyclicStrainTable"/> using temperature and material description.
    /// </summary>
    /// <param name="table">Table to add or replace.</param>
    /// <param name="material">Material to update.</param>
    /// <returns>Updated material with refreshed <c>LastModified</c>, or a conversion error.</returns>
    let addOrReplace (table: CyclicStrainTable) (material: Material) : Result<Material, MaterialError> =
        CyclicStrainTable.validate table
        |> Result.map (fun validTable ->
            let sameKey (other: CyclicStrainTable) =
                other.ReferenceTemperature = validTable.ReferenceTemperature
                && other.MaterialDescription = validTable.MaterialDescription

            let filtered =
                material.StrengthProperties.CyclicStrainTables |> List.filter (sameKey >> not)

            { material with
                StrengthProperties =
                    { material.StrengthProperties with
                        CyclicStrainTables = filtered @ [ validTable ] }
                LastModified = DateTime.UtcNow })
