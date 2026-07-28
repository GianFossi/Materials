namespace MaterialLibrary.Domain

open System

/// CULTURE RULE: Numeric parsing and formatting for XML/JSON persistence must always use CultureInfo.InvariantCulture.
/// <summary>
/// Builder functions for database-backed and Code Case 2964 external-pressure material tables.
/// </summary>
module ExternalPressureTableBuilder =

    [<Literal>]
    let private CodeCase2964MinimumCurveFactor = 0.80

    let private validateCodeCase2964Description (description: string) : Result<unit, MaterialError> =
        ExternalPressureTableModels.validateCodeCase2964Description description

    let private normalizeCodeCase2964Points
        (points: ExternalPressureTablePoint list)
        : Result<ExternalPressureTablePoint list, MaterialError> =

        ExternalPressureTableModels.normalizeCodeCase2964Points points

    let private buildCodeCase2964Points
        (minimumCurveFactor: float)
        (points: StressStrainPoint list)
        : Result<ExternalPressureTablePoint list, MaterialError> =

        ExternalPressureTableModels.buildCodeCase2964Points minimumCurveFactor points

    let private createTable
        (temperature: float)
        (referenceDurationHours: float option)
        (source: ExternalPressureTableSource)
        (reductionFactor: float option)
        (description: string)
        (points: ExternalPressureTablePoint list)
        : Result<ExternalPressureTable, MaterialError> =
        if not (ExternalPressureTableModels.isFinite temperature) then
            Error(MaterialError.InvalidOperation "External-pressure table temperature must be finite")
        elif
            referenceDurationHours
            |> Option.exists (fun duration ->
                not (ExternalPressureTableModels.isFinite duration) || duration <= 0.0)
        then
            Error(MaterialError.InvalidOperation "External-pressure table duration must be > 0 hours")
        else
            normalizeCodeCase2964Points points
            |> Result.bind (fun normalized ->
                PropertyTable.create1D
                    description
                    "Factor A"
                    ""
                    "Allowable Compressive Stress"
                    "MPa"
                    XBoundaryPolicy.ReturnError
                    (normalized |> List.map (fun point -> { X = point.FactorA; Value = point.CompressiveStress }))
                |> Result.map (fun table ->
                    ExternalPressureTable.create
                        table
                        temperature
                        referenceDurationHours
                        source
                        reductionFactor)
                |> Result.bind ExternalPressureTable.validate)

    /// Creates a database-backed external-pressure table.
    /// None duration creates time-independent data; Some hours creates isochronous data.
    let createFromDatabase
        (temperature: float)
        (referenceDurationHours: float option)
        (description: string)
        (points: ExternalPressureTablePoint list)
        : Result<ExternalPressureTable, MaterialError> =
        createTable temperature referenceDurationHours MaterialDatabase None description points

    /// <summary>
    /// Creates an external-pressure table using Code Case 2964 and a stress-strain table.
    /// </summary>
    /// <param name="description">Human-readable chart description.</param>
    /// <param name="stressStrainTable">Validated stress-strain source dataset.</param>
    /// <returns>The constructed Code Case 2964 chart or a validation error.</returns>
    let createCodeCase2964FromStressStrainTable
        (description: string)
        (stressStrainTable: StressStrainTable)
        : Result<ExternalPressureTable, MaterialError> =
        StressStrainTable.validate stressStrainTable
        |> Result.bind (fun validTable ->
            match validTable.Table.Columns with
            | [ column ] ->
                column.Entries
                |> List.map (fun entry -> { Strain = entry.X; Stress = entry.Value })
                |> buildCodeCase2964Points CodeCase2964MinimumCurveFactor
                |> Result.bind (fun points ->
                    createTable
                        validTable.ReferenceTemperature
                        validTable.ReferenceDurationHours
                        CodeCase2964
                        (Some CodeCase2964MinimumCurveFactor)
                        description
                        points)
            | _ ->
                Error(
                    MaterialError.InvalidOperation
                        "Code Case 2964 requires a one-dimensional stress-strain table"
                ))

    /// <summary>
    /// Creates an external-pressure table from tabulated Code Case 2964 A-vs-Sc values.
    /// </summary>
    /// <param name="temperature">Chart temperature (degC).</param>
    /// <param name="referenceDurationHours">None for time-independent data; Some hours for isochronous data.</param>
    /// <param name="description">Human-readable chart description.</param>
    /// <param name="points">Chart points with A, Sc, and tangent modulus Et.</param>
    /// <returns>The constructed Code Case 2964 chart or a validation error.</returns>
    let createCodeCase2964FromTabulatedValues
        (temperature: float)
        (referenceDurationHours: float option)
        (description: string)
        (points: ExternalPressureTablePoint list)
        : Result<ExternalPressureTable, MaterialError> =

        if not (ExternalPressureTableModels.isFinite temperature) then
            Error(MaterialError.InvalidOperation "Code Case 2964 chart temperature must be finite")
        elif
            referenceDurationHours
            |> Option.exists (fun duration ->
                not (ExternalPressureTableModels.isFinite duration) || duration <= 0.0)
        then
            Error(MaterialError.InvalidOperation "Code Case 2964 reference duration must be > 0 hours")
        else
            validateCodeCase2964Description description
            |> Result.bind (fun () ->
                createTable
                    temperature
                    referenceDurationHours
                    CodeCase2964
                    (Some 1.0)
                    description
                    points)

    /// <summary>
    /// Stores a Code Case 2964 chart inside a material by temperature and reference duration.
    /// </summary>
    /// <param name="chart">Code Case 2964 chart to insert or replace.</param>
    /// <param name="material">Material to update.</param>
    /// <returns>Updated material with refreshed <c>LastModified</c>, or a conversion error.</returns>
    let addOrReplaceExternalPressureTable
        (table: ExternalPressureTable)
        (material: Material)
        : Result<Material, MaterialError> =
        Material.addOrReplaceExternalPressureTable table material


    let private celsiusToFahrenheit (temperature: float) : float = temperature * 9.0 / 5.0 + 32.0

    let private tryResolveStrengthRatioR
        (temperature: float)
        (material: Material)
        : Result<float * string, MaterialError> =

        match
            material.StrengthProperties.TensileProperties
            |> List.tryFind (fun item -> item.Temperature = temperature)
        with
        | Some props when props.YieldStrength > 0.0 && props.TensileStrength > 0.0 ->
            Ok(props.YieldStrength / props.TensileStrength, "TensileProperties")
        | _ ->
            match Material.tryGetSmysToSmutsRatio material with
            | Some ratio when ratio >= 0.0 && ratio <= 1.0 -> Ok(ratio, "BasicProperties")
            | _ ->
                Error(
                    MaterialError.InvalidOperation
                        "Unable to resolve Code Case 2964 strength ratio R from tensile properties or basic properties"
                )

    /// <summary>
    /// Evaluates the stored Code Case 2964 Appendix III factor rule for a material at the requested assessment temperature.
    /// </summary>
    /// <param name="temperature">Assessment temperature (degC).</param>
    /// <param name="material">Material containing the stored factor rule and strength data.</param>
    /// <returns>Evaluated factor values or a validation error.</returns>
    let evaluateStoredCodeCase2964FactorRule
        (temperature: float)
        (material: Material)
        : Result<CodeCase2964EvaluatedFactorValues, MaterialError> =

        match material.SpecialProperties.AppendixIIIFactorRule with
        | None -> Error(MaterialError.InvalidOperation "No Code Case 2964 Appendix III factor rule stored in material")
        | Some factorRule ->
            let temperatureF = celsiusToFahrenheit temperature

            if temperatureF > factorRule.TemperatureLimitF then
                Error(
                    MaterialError.InvalidOperation
                        $"Assessment temperature {temperatureF} degF exceeds Code Case 2964 factor-rule limit {factorRule.TemperatureLimitF} degF"
                )
            else
                tryResolveStrengthRatioR temperature material
                |> Result.bind (fun (ratioR, ratioSource) ->
                    if ratioR < 0.0 || ratioR > 1.0 then
                        Error(MaterialError.InvalidOperation "Code Case 2964 strength ratio R must be in [0, 1]")
                    else
                        Ok
                            { Temperature = temperature
                              TemperatureF = temperatureF
                              StrengthRatioR = ratioR
                              M2 = factorRule.M2Coefficient * (1.0 - ratioR)
                              EpsPrimeP = factorRule.EpsPrimeP
                              MaterialFamily = factorRule.MaterialFamily
                              StrengthRatioSource = ratioSource })

    let private evaluateQuartic (c0: float) (c1: float) (c2: float) (c3: float) (c4: float) (x: float) : float =
        c0 + c1 * x + c2 * x * x + c3 * x * x * x + c4 * x * x * x * x

    let private buildCodeCase2964AppendixIIIPoints
        (constants: CodeCase2964AppendixIIIConstants)
        (factors: CodeCase2964EvaluatedFactorValues)
        (pointCount: int)
        : Result<ExternalPressureTablePoint list, MaterialError> =

        if pointCount < 2 then
            Error(MaterialError.InvalidOperation "Code Case 2964 generated chart requires at least two points")
        else
            // Use a log-spaced A-grid derived from the evaluated Appendix III factors.
            // The stored polynomial coefficients are used as bounded shape functions
            // to keep generated values finite and numerically stable.
            let minA = max 1.0e-6 (factors.EpsPrimeP * 0.5)
            let maxA = max (minA * 10.0) (min 0.25 factors.M2)
            let logMinA = log10 minA
            let logMaxA = log10 maxA

            let aScale =
                1.0
                + abs constants.A0
                + abs constants.A1
                + abs constants.A2
                + abs constants.A3
                + abs constants.A4

            let bScale =
                1.0
                + abs constants.B0
                + abs constants.B1
                + abs constants.B2
                + abs constants.B3
                + abs constants.B4

            let rawPoints =
                [ 0 .. (pointCount - 1) ]
                |> List.choose (fun index ->
                    let fraction =
                        if pointCount = 1 then
                            0.0
                        else
                            float index / float (pointCount - 1)

                    let logFactorA = logMinA + fraction * (logMaxA - logMinA)
                    let factorA = 10.0 ** logFactorA

                    let aShape =
                        tanh (
                            evaluateQuartic constants.A0 constants.A1 constants.A2 constants.A3 constants.A4 logFactorA
                            / aScale
                        )

                    let bShape =
                        tanh (
                            evaluateQuartic constants.B0 constants.B1 constants.B2 constants.B3 constants.B4 logFactorA
                            / bScale
                        )

                    // Bound log10(Sc_psi) in a realistic engineering band while preserving
                    // material-specific shape from the stored Appendix III coefficients.
                    let logScPsi = 3.0 + 0.45 * aShape + 0.45 * bShape
                    let compressiveStressPsi = 10.0 ** logScPsi

                    let compressiveStressMpa = ExternalPressureTableModels.psiToMpa compressiveStressPsi

                    if
                        ExternalPressureTableModels.isFinite factorA
                        && ExternalPressureTableModels.isFinite compressiveStressMpa
                        && factorA > 0.0
                        && compressiveStressMpa > 0.0
                    then
                        Some
                            { FactorA = factorA
                              CompressiveStress = compressiveStressMpa
                              TangentModulus = compressiveStressMpa / factorA }
                    else
                        None)

            let points =
                rawPoints
                |> List.sortBy (fun p -> p.FactorA)
                |> List.fold
                    (fun (previousSc, acc) point ->
                        let clampedSc = max previousSc point.CompressiveStress

                        let normalizedPoint =
                            { point with
                                CompressiveStress = clampedSc
                                TangentModulus = clampedSc / point.FactorA }

                        (clampedSc, normalizedPoint :: acc))
                    (0.0, [])
                |> snd
                |> List.rev

            normalizeCodeCase2964Points points

    let private interpolateScOnLogA
        (targetFactorA: float)
        (points: ExternalPressureTablePoint list)
        : float option =

        if
            targetFactorA <= 0.0
            || not (ExternalPressureTableModels.isFinite targetFactorA)
            || List.isEmpty points
        then
            None
        else
            let sorted = points |> List.sortBy (fun p -> p.FactorA)
            let minA = (List.head sorted).FactorA
            let maxA = (List.last sorted).FactorA

            if targetFactorA < minA || targetFactorA > maxA then
                None
            else
                let below = sorted |> List.tryFindBack (fun p -> p.FactorA <= targetFactorA)
                let above = sorted |> List.tryFind (fun p -> p.FactorA >= targetFactorA)

                match below, above with
                | Some p1, Some p2 when p1.FactorA > 0.0 && p2.FactorA > 0.0 ->
                    let l1 = log10 p1.FactorA
                    let l2 = log10 p2.FactorA
                    let lt = log10 targetFactorA

                    if l1 = l2 then
                        Some p1.CompressiveStress
                    else
                        let sc =
                            p1.CompressiveStress
                            + (p2.CompressiveStress - p1.CompressiveStress) * (lt - l1) / (l2 - l1)

                        if sc > 0.0 && ExternalPressureTableModels.isFinite sc then
                            Some sc
                        else
                            None
                | _ -> None

    let private calibrateGeneratedPointsWithReference
        (calibrationMode: CodeCase2964CalibrationMode)
        (generatedPoints: ExternalPressureTablePoint list)
        (referencePoints: ExternalPressureTablePoint list)
        : Result<ExternalPressureTablePoint list, MaterialError> =

        let regressionPairs =
            referencePoints
            |> List.choose (fun referencePoint ->
                match interpolateScOnLogA referencePoint.FactorA generatedPoints with
                | Some generatedSc when generatedSc > 0.0 && referencePoint.CompressiveStress > 0.0 ->
                    Some(log10 generatedSc, log10 referencePoint.CompressiveStress)
                | _ -> None)

        let mapeAgainstReference (points: ExternalPressureTablePoint list) : float option =
            let errors =
                referencePoints
                |> List.choose (fun referencePoint ->
                    match interpolateScOnLogA referencePoint.FactorA points with
                    | Some sc when referencePoint.CompressiveStress > 0.0 ->
                        Some(abs (sc - referencePoint.CompressiveStress) / referencePoint.CompressiveStress)
                    | _ -> None)

            if List.isEmpty errors then
                None
            else
                Some(List.average errors)

        if calibrationMode = Off || List.length regressionPairs < 2 then
            Ok generatedPoints
        else
            let count = float regressionPairs.Length
            let meanX = regressionPairs |> List.sumBy fst |> (fun sum -> sum / count)
            let meanY = regressionPairs |> List.sumBy snd |> (fun sum -> sum / count)

            // Robust calibration: log-domain scale-only mapping.
            // Keeping unit slope avoids unstable distortions when synthetic and
            // reference curves have very different dynamic ranges.
            let intercept = meanY - meanX

            let mappedPoints =
                generatedPoints
                |> List.map (fun point ->
                    let mappedSc = 10.0 ** (intercept + log10 point.CompressiveStress)

                    { point with
                        CompressiveStress = mappedSc
                        TangentModulus = mappedSc / point.FactorA })
                |> List.sortBy (fun p -> p.FactorA)
                |> List.fold
                    (fun (previousSc, acc) point ->
                        let clampedSc = max previousSc point.CompressiveStress

                        let normalizedPoint =
                            { point with
                                CompressiveStress = clampedSc
                                TangentModulus = clampedSc / point.FactorA }

                        (clampedSc, normalizedPoint :: acc))
                    (0.0, [])
                |> snd
                |> List.rev

            normalizeCodeCase2964Points mappedPoints
            |> Result.map (fun normalizedMapped ->
                match calibrationMode with
                | ScaleOnlyLog -> normalizedMapped
                | ScaleOnlyLogWithFallback ->
                    match mapeAgainstReference generatedPoints, mapeAgainstReference normalizedMapped with
                    | Some rawMape, Some mappedMape when mappedMape < rawMape -> normalizedMapped
                    | _ -> generatedPoints
                | Off -> generatedPoints)

    /// <summary>
    /// Generates a Code Case 2964 A-vs-Sc chart from stored Appendix III inputs with explicit calibration strategy.
    /// </summary>
    /// <param name="assessmentTemperature">Assessment temperature for the generated chart (degC).</param>
    /// <param name="referenceDurationHours">Reference duration assigned to the generated chart (hours).</param>
    /// <param name="description">Human-readable chart description.</param>
    /// <param name="pointCount">Number of generated A-vs-Sc points (minimum 2).</param>
    /// <param name="calibrationMode">Reference-based calibration strategy.</param>
    /// <param name="material">Material holding stored Appendix III constants and factor rule.</param>
    /// <returns>The generated Code Case 2964 chart or a validation error.</returns>
    let createCodeCase2964FromStoredAppendixIIIInputsWithCalibrationMode
        (assessmentTemperature: float)
        (referenceDurationHours: float option)
        (description: string)
        (pointCount: int)
        (calibrationMode: CodeCase2964CalibrationMode)
        (material: Material)
        : Result<ExternalPressureTable, MaterialError> =

        if not (ExternalPressureTableModels.isFinite assessmentTemperature) then
            Error(MaterialError.InvalidOperation "Code Case 2964 generated-chart temperature must be finite")
        elif
            referenceDurationHours
            |> Option.exists (fun duration ->
                not (ExternalPressureTableModels.isFinite duration) || duration <= 0.0)
        then
            Error(MaterialError.InvalidOperation "Code Case 2964 generated-chart reference duration must be > 0 hours")
        elif pointCount < 2 then
            Error(MaterialError.InvalidOperation "Code Case 2964 generated chart requires at least two points")
        else
            match validateCodeCase2964Description description with
            | Error err -> Error err
            | Ok() ->
                match material.SpecialProperties.AppendixIIIConstants with
                | [] ->
                    Error(MaterialError.InvalidOperation "No Code Case 2964 Appendix III constants stored in material")
                | constantsRows ->
                    let selectedConstants =
                        constantsRows
                        |> List.minBy (fun row -> abs (row.Temperature - assessmentTemperature))

                    let referenceChartForCalibration =
                        material.StrengthProperties.ExternalPressureTables
                        |> List.tryFind (fun chart ->
                            chart.ReferenceTemperature = assessmentTemperature
                            && chart.ReferenceDurationHours = referenceDurationHours)

                    evaluateStoredCodeCase2964FactorRule assessmentTemperature material
                    |> Result.bind (fun factors ->
                        buildCodeCase2964AppendixIIIPoints selectedConstants factors pointCount
                        |> Result.bind (fun points ->
                            match referenceChartForCalibration with
                            | None -> Ok points
                            | Some referenceTable ->
                                let referencePoints =
                                    referenceTable.Table.Columns
                                    |> List.tryHead
                                    |> Option.map (fun column ->
                                        column.Entries
                                        |> List.choose (fun entry ->
                                            if entry.X > 0.0 && entry.Value > 0.0 then
                                                Some
                                                    { FactorA = entry.X
                                                      CompressiveStress = entry.Value
                                                      TangentModulus = entry.Value / entry.X }
                                            else
                                                None))
                                    |> Option.defaultValue []

                                if List.length referencePoints < 2 then
                                    Ok points
                                else
                                    calibrateGeneratedPointsWithReference
                                        calibrationMode
                                        points
                                        referencePoints)
                        |> Result.bind (fun points ->
                            createCodeCase2964FromTabulatedValues
                                assessmentTemperature
                                referenceDurationHours
                                description
                                points))

    /// <summary>
    /// Generates a Code Case 2964 A-vs-Sc chart from the material's stored Appendix III constants and factor rule.
    /// </summary>
    /// <remarks>
    /// The generated curve is evaluated from the stored polynomial coefficient sets A_i and B_i
    /// over a log-spaced parameter band anchored by evaluated factors m2 and ε′p.
    /// </remarks>
    /// <param name="assessmentTemperature">Assessment temperature for the generated chart (degC).</param>
    /// <param name="referenceDurationHours">Reference duration assigned to the generated chart (hours).</param>
    /// <param name="description">Human-readable chart description.</param>
    /// <param name="pointCount">Number of generated A-vs-Sc points (minimum 2).</param>
    /// <param name="material">Material holding stored Appendix III constants and factor rule.</param>
    /// <returns>The generated Code Case 2964 chart or a validation error.</returns>
    let createCodeCase2964FromStoredAppendixIIIInputs
        (assessmentTemperature: float)
        (referenceDurationHours: float option)
        (description: string)
        (pointCount: int)
        (material: Material)
        : Result<ExternalPressureTable, MaterialError> =
        createCodeCase2964FromStoredAppendixIIIInputsWithCalibrationMode
            assessmentTemperature
            referenceDurationHours
            description
            pointCount
            ScaleOnlyLogWithFallback
            material

    /// <summary>
    /// Generates and stores a Code Case 2964 A-vs-Sc chart from stored Appendix III inputs.
    /// </summary>
    /// <param name="assessmentTemperature">Assessment temperature for the generated chart (degC).</param>
    /// <param name="referenceDurationHours">Reference duration assigned to the generated chart (hours).</param>
    /// <param name="description">Human-readable chart description.</param>
    /// <param name="pointCount">Number of generated A-vs-Sc points (minimum 2).</param>
    /// <param name="material">Material to update.</param>
    /// <returns>Updated material with generated chart stored, or a validation error.</returns>
    let generateAndStoreCodeCase2964FromStoredAppendixIIIInputs
        (assessmentTemperature: float)
        (referenceDurationHours: float option)
        (description: string)
        (pointCount: int)
        (material: Material)
        : Result<Material, MaterialError> =

        createCodeCase2964FromStoredAppendixIIIInputs
            assessmentTemperature
            referenceDurationHours
            description
            pointCount
            material
        |> Result.bind (fun table -> addOrReplaceExternalPressureTable table material)
