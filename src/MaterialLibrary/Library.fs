namespace MaterialLibrary

open System
open System.Xml.Linq
open MaterialLibrary.Domain
open MaterialLibrary.Domain.Database.Lookup
open MaterialLibrary.Interpolation

/// CULTURE RULE: Numeric parsing and formatting for XML/JSON persistence must always use CultureInfo.InvariantCulture.
/// <summary>
/// Carries the result of a property lookup together with the query context used to obtain it.
/// </summary>
/// <typeparam name="T">The type of the retrieved property value (e.g. <c>float&lt;MPa&gt;</c>).</typeparam>
type PropertyLookup<'T> =
    {
        /// <summary>The interpolated or exact property value.</summary>
        Value: 'T
        /// <summary>The temperature at which the lookup was performed (°C).</summary>
        Temperature: float
        /// <summary>The interpolation algorithm that produced this result.</summary>
        InterpolationMode: InterpolationMode
    }

/// <summary>
/// Primary API for the ASME Section II Part D Material Library, package version 1.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MaterialLibrary"/> is an immutable collection of <see cref="Material"/> records.
/// All query methods return <c>Result</c> types so callers can handle not-found and interpolation errors
/// without exceptions.
/// </para>
/// <para>
/// Use the companion <c>module MaterialLibrary</c> functions (<c>create</c>, <c>empty</c>,
/// <c>addMaterial</c>) to construct instances without calling the constructor directly.
/// </para>
/// </remarks>
type MaterialLibrary(inputMaterials: Material list) =
    let materials =
        if isNull (box inputMaterials) then
            []
        else
            inputMaterials
            |> List.filter (fun material ->
                not (isNull (box material))
                && not (String.IsNullOrWhiteSpace material.Id))
            |> List.rev
            |> List.distinctBy (fun material -> material.Id)
            |> List.rev

    let materialMap = materials |> List.map (fun m -> m.Id, m) |> Map.ofList

    // ========== MATERIAL QUERIES ==========

    /// <summary>Finds a material by its unique identifier.</summary>
    /// <param name="id">The material ID string (e.g. <c>"SA-516-70"</c>).</param>
    /// <returns><c>Some material</c> if found; <c>None</c> otherwise.</returns>
    member this.GetMaterialById(id: string) : Material option = Map.tryFind id materialMap

    /// <summary>Returns all materials whose name contains the given substring (case-insensitive).</summary>
    /// <param name="substring">Text to search for within material names.</param>
    /// <returns>Possibly empty list of matching <see cref="Material"/> records.</returns>
    member this.SearchByName(substring: string) : Material list =
        if isNull substring then
            []
        else
            materials
            |> List.filter (fun material ->
                not (isNull material.Name)
                && material.Name.Contains(substring, StringComparison.OrdinalIgnoreCase))

    /// <summary>
    /// Searches materials using ASME identity fields: specification, grade, class/condition/tempering,
    /// UNS, nominal composition, product form, and family.
    /// </summary>
    /// <param name="criteria">Optional identity criteria combined with AND semantics.</param>
    /// <returns>Matching materials ordered by material ID.</returns>
    member this.Search(criteria: MaterialSearchCriteria) : Material list =
        MaterialFiltering.findMany criteria materials

    /// <summary>
    /// Searches materials using the same identity fields exposed by Excel <c>MatSearch</c>.
    /// Blank or <c>None</c> values are ignored; text fields use case-insensitive contains matching.
    /// </summary>
    member this.SearchMaterials
        (
            specification: string option,
            grade: string option,
            classConditionTempering: string option,
            uns: string option,
            nominalComposition: string option,
            productForm: string option,
            family: AsmeMaterialFamily option
        ) : Material list =
        let contains value =
            value
            |> Option.filter (String.IsNullOrWhiteSpace >> not)
            |> Option.map Contains

        { MaterialSearchCriteria.empty with
            Specification = contains specification
            Grade = contains grade
            ClassConditionTemper = contains classConditionTempering
            Uns = contains uns
            NominalComposition = contains nominalComposition
            ProductForm = contains productForm
            Family = family }
        |> this.Search

    /// <summary>Returns the complete list of materials in the library.</summary>
    /// <returns>All <see cref="Material"/> records, in insertion order.</returns>
    member this.ListAllMaterials() : Material list = materials

    /// <summary>Total number of materials in the library.</summary>
    member this.Count: int = materials.Length

    // ========== BASIC PROPERTIES ==========

    /// <summary>Retrieves the minimum mechanical properties (SMYS, SMUTS) for a material.</summary>
    /// <param name="materialId">The material ID string.</param>
    /// <returns><c>Ok BasicProperties</c>, or <c>Error (NotFound id)</c>.</returns>
    member this.GetBasicProperties(materialId: string) : Result<BasicProperties, MaterialError> =
        match this.GetMaterialById materialId with
        | None -> Error(MaterialError.NotFound materialId)
        | Some material -> Ok material.BasicProperties

    /// <summary>
    /// Returns mass density ρ at a given temperature by interpolating the material's ρ(T) table.
    /// </summary>
    /// <param name="materialId">The material ID string.</param>
    /// <param name="temperature">Query temperature (°C).</param>
    /// <param name="mode">Interpolation algorithm to apply.</param>
    /// <returns>
    /// <c>Ok lookup</c> — a <see cref="PropertyLookup{T}"/> containing interpolated density (kg⋅m⁻³). <br/>
    /// <c>Error (NotFound id)</c> — no material with that ID. <br/>
    /// <c>Error (InterpolationError e)</c> — temperature is out of range or insufficient data.
    /// </returns>
    member this.GetDensity
        (materialId: string, temperature: float, mode: InterpolationMode)
        : Result<PropertyLookup<float>, MaterialError> =
        match this.GetMaterialById materialId with
        | None -> Error(MaterialError.NotFound materialId)
        | Some material ->
            match DensityInterpolation.interpolate mode temperature material.PhysicalProperties.DensityTable with
            | Ok value ->
                Ok
                    { Value = value
                      Temperature = temperature
                      InterpolationMode = mode }
            | Error err -> Error(MaterialError.InterpolationError err)

    /// <summary>Returns the Poisson’s ratio of a material.</summary>
    /// <param name="materialId">The material ID string.</param>
    /// <returns><c>Ok ν</c> (dimensionless), or <c>Error (NotFound id)</c>.</returns>
    member this.GetPoissonRatio(materialId: string) : Result<float, MaterialError> =
        match this.GetMaterialById materialId with
        | None -> Error(MaterialError.NotFound materialId)
        | Some material ->
            match material.PhysicalProperties.ElasticModulusTable with
            | first :: _ -> Ok(defaultArg first.PoissonRatio 0.30)
            | [] -> Ok 0.30

    // ========== PHYSICAL PROPERTIES ==========

    /// <summary>
    /// Returns the specific heat Cp at a given temperature by interpolating the material’s Cp(T) table.
    /// </summary>
    /// <param name="materialId">The material ID string.</param>
    /// <param name="temperature">Query temperature (°C).</param>
    /// <param name="mode">Interpolation algorithm to apply.</param>
    /// <returns>
    /// <c>Ok lookup</c> — a <see cref="PropertyLookup{T}"/> containing the interpolated Cp (J⋅kg⁻¹⋅K⁻¹). <br/>
    /// <c>Error (NotFound id)</c> — no material with that ID. <br/>
    /// <c>Error (InvalidOperation msg)</c> — the material has no specific heat table. <br/>
    /// <c>Error (InterpolationError e)</c> — temperature is out of range or insufficient data.
    /// </returns>
    member this.GetSpecificHeatFromTable
        (materialId: string, temperature: float, mode: InterpolationMode)
        : Result<PropertyLookup<float>, MaterialError> =
        match this.GetMaterialById materialId with
        | None -> Error(MaterialError.NotFound materialId)
        | Some material ->
            match material.PhysicalProperties.SpecificHeatTable with
            | None -> Error(MaterialError.InvalidOperation "No specific heat table defined")
            | Some table ->
                match SpecificHeatInterpolation.interpolate mode temperature table with
                | Ok value ->
                    Ok
                        { Value = value
                          Temperature = temperature
                          InterpolationMode = mode }
                | Error err -> Error(MaterialError.InterpolationError err)

    // ========== STRESS-STRAIN CURVES ==========

    /// <summary>Returns all stress-strain curves available for a material (one per temperature).</summary>
    /// <param name="materialId">The material ID string.</param>
    /// <returns><c>Ok curves</c> (possibly empty list), or <c>Error (NotFound id)</c>.</returns>
    member this.GetStressStrainTables(materialId: string) : Result<StressStrainTable list, MaterialError> =
        match this.GetMaterialById materialId with
        | None -> Error(MaterialError.NotFound materialId)
        | Some material -> Ok material.StrengthProperties.StressStrainTables

    /// <summary>Evaluates stress at a given strain on the stress-strain curve closest to the specified temperature.</summary>
    /// <param name="materialId">The material ID string.</param>
    /// <param name="temperature">Temperature (°C) used to select the matching curve (exact match required).</param>
    /// <param name="strain">Query engineering strain (dimensionless). Must be within the curve’s strain range.</param>
    /// <returns>
    /// <c>Ok σ</c> (MPa), or an appropriate <see cref="MaterialError"/>.
    /// </returns>
    member this.GetStressFromStrain
        (materialId: string, temperature: float, strain: float)
        : Result<float, MaterialError> =
        match this.GetMaterialById materialId with
        | None -> Error(MaterialError.NotFound materialId)
        | Some material ->
            let curves = material.StrengthProperties.StressStrainTables

            match
                curves
                |> List.tryFind (fun c ->
                    c.ReferenceTemperature = temperature
                    && c.ReferenceDurationHours = None)
            with
            | None -> Error(MaterialError.InvalidOperation $"No stress-strain curve at {temperature}degC")
            | Some table ->
                let unwrappedTable = table.Table

                match PropertyTable.lookup1D strain unwrappedTable with
                | Ok result -> Ok result.Value
                | Error err -> Error err

    /// <summary>Evaluates stress on an isochronous stress-strain table at temperature and duration.</summary>
    member this.GetStressFromStrainAtDuration
        (materialId: string, temperature: float, referenceDurationHours: float, strain: float)
        : Result<float, MaterialError> =
        match this.GetMaterialById materialId with
        | None -> Error(MaterialError.NotFound materialId)
        | Some material ->
            match
                material.StrengthProperties.StressStrainTables
                |> List.tryFind (fun table ->
                    table.ReferenceTemperature = temperature
                    && table.ReferenceDurationHours = Some referenceDurationHours)
            with
            | None ->
                Error(
                    MaterialError.InvalidOperation
                        $"No isochronous stress-strain table at {temperature}degC and {referenceDurationHours} hours"
                )
            | Some table ->
                PropertyTable.lookup1D strain table.Table
                |> Result.map (fun result -> result.Value)

    // ========== CREEP MODELS ==========

    /// <summary>Computes the Norton Power Law creep strain for a registered material.</summary>
    /// <param name="materialId">The material ID string (used only to verify the material exists).</param>
    /// <param name="A">Pre-exponential coefficient.</param>
    /// <param name="n">Stress exponent.</param>
    /// <param name="m">Time exponent.</param>
    /// <param name="sigma">Applied stress (MPa).</param>
    /// <param name="time">Elapsed time (hours).</param>
    /// <returns><c>Ok ε</c> (%), or <c>Error (NotFound id)</c>.</returns>
    member this.GetNortonCreepStrain
        (materialId: string, A: float, n: float, m: float, sigma: float, time: float)
        : Result<float, MaterialError> =
        match this.GetMaterialById materialId with
        | None -> Error(MaterialError.NotFound materialId)
        | Some _ -> NortonPowerLaw.creepStrain A n m sigma time

    /// <summary>Computes the Garofalo (hyperbolic-sine) creep strain for a registered material.</summary>
    /// <param name="materialId">The material ID string (used only to verify the material exists).</param>
    /// <param name="A">Pre-exponential coefficient.</param>
    /// <param name="n">Stress exponent.</param>
    /// <param name="m">Time exponent.</param>
    /// <param name="alpha">Stress-scaling constant α (MPa⁻¹).</param>
    /// <param name="sigma">Applied stress (MPa).</param>
    /// <param name="time">Elapsed time (hours).</param>
    /// <returns><c>Ok ε</c> (%), or <c>Error (NotFound id)</c>.</returns>
    member this.GetGarofaloCreepStrain
        (materialId: string, A: float, n: float, m: float, alpha: float, sigma: float, time: float)
        : Result<float, MaterialError> =
        match this.GetMaterialById materialId with
        | None -> Error(MaterialError.NotFound materialId)
        | Some _ -> GarofaloModel.creepStrain A n m alpha sigma time

    /// <summary>Integrates the Kachanov–Robinson damage evolution equation to obtain the ω(t) sequence.</summary>
    /// <param name="materialId">The material ID string (used only to verify the material exists).</param>
    /// <param name="A2">Damage-rate coefficient.</param>
    /// <param name="N2">Stress exponent for damage rate.</param>
    /// <param name="M2">Damage exponent for damage rate.</param>
    /// <param name="sigma">Applied stress (MPa).</param>
    /// <param name="timeSteps">Number of Euler integration steps.</param>
    /// <param name="totalTime">Total simulation time (hours).</param>
    /// <returns><c>Ok [ω_0; ...; ω_N]</c> (values in [0, 1]), or <c>Error (NotFound id)</c>.</returns>
    member this.GetKachanovOmegaDamage
        (materialId: string, A2: float, N2: float, M2: float, sigma: float, timeSteps: int, totalTime: float)
        : Result<float list, MaterialError> =
        match this.GetMaterialById materialId with
        | None -> Error(MaterialError.NotFound materialId)
        | Some _ -> KachanovOmega.omegaEvolution A2 N2 M2 sigma timeSteps totalTime

    /// <summary>
    /// Integrates the coupled Kachanov–Robinson creep and damage ODEs to obtain cumulative creep strain ε(t).
    /// </summary>
    /// <param name="materialId">The material ID string (used only to verify the material exists).</param>
    /// <param name="A1">Creep-rate coefficient.</param>
    /// <param name="N1">Stress exponent for creep rate.</param>
    /// <param name="M1">Damage exponent for creep rate.</param>
    /// <param name="A2">Damage-rate coefficient.</param>
    /// <param name="N2">Stress exponent for damage rate.</param>
    /// <param name="M2">Damage exponent for damage rate.</param>
    /// <param name="sigma">Applied stress (MPa).</param>
    /// <param name="timeSteps">Number of Euler integration steps.</param>
    /// <param name="totalTime">Total simulation time (hours).</param>
    /// <returns><c>Ok [ε_0; ...; ε_N]</c> (%), or <c>Error (NotFound id)</c>.</returns>
    member this.GetKachanovCreepStrain
        (
            materialId: string,
            A1: float,
            N1: float,
            M1: float,
            A2: float,
            N2: float,
            M2: float,
            sigma: float,
            timeSteps: int,
            totalTime: float
        ) : Result<float list, MaterialError> =
        match this.GetMaterialById materialId with
        | None -> Error(MaterialError.NotFound materialId)
        | Some _ -> KachanovOmega.creepStrainWithDamage A1 N1 M1 A2 N2 M2 sigma timeSteps totalTime

    // ========== CREEP CURVES ==========

    /// <summary>Returns all experimental creep curves available for a material.</summary>
    /// <param name="materialId">The material ID string.</param>
    /// <returns><c>Ok curves</c> (possibly empty), or <c>Error (NotFound id)</c>.</returns>
    member this.GetCreepTables(materialId: string) : Result<CreepTable list, MaterialError> =
        match this.GetMaterialById materialId with
        | None -> Error(MaterialError.NotFound materialId)
        | Some material -> Ok material.StrengthProperties.CreepTables

    /// <summary>Evaluates creep strain at a given time on the experimental creep curve matching the applied stress.</summary>
    /// <param name="materialId">The material ID string.</param>
    /// <param name="appliedStress">Stress (MPa) used to select the matching creep curve (exact match required).</param>
    /// <param name="time">Query elapsed time (hours). Must be within the curve’s time range.</param>
    /// <returns><c>Ok ε</c> (%), or an appropriate <see cref="MaterialError"/>.</returns>
    member this.GetCreepStrainFromCurve
        (materialId: string, appliedStress: float, time: float)
        : Result<float, MaterialError> =
        match this.GetMaterialById materialId with
        | None -> Error(MaterialError.NotFound materialId)
        | Some material ->
            let curves = material.StrengthProperties.CreepTables

            match
                curves
                |> List.tryFind (fun c -> c.AppliedStress = Some appliedStress)
            with
            | None -> Error(MaterialError.InvalidOperation $"No creep curve for {appliedStress} MPa")
            | Some table ->
                let unwrappedTable = table.Table

                match PropertyTable.lookup1D time unwrappedTable with
                | Ok result -> Ok result.Value
                | Error err -> Error err

    // ========== EXTERNAL PRESSURE TABLES ==========

    /// <summary>Returns all stored external-pressure material tables for a material.</summary>
    /// <param name="materialId">The material ID string.</param>
    /// <returns><c>Ok charts</c> (possibly empty), or <c>Error (NotFound id)</c>.</returns>
    member this.GetExternalPressureTables
        (materialId: string)
        : Result<ExternalPressureTable list, MaterialError> =
        match this.GetMaterialById materialId with
        | None -> Error(MaterialError.NotFound materialId)
        | Some material -> Ok material.StrengthProperties.ExternalPressureTables

    /// <summary>Returns all stored Code Case 2964 Appendix III constants rows available for a material.</summary>
    /// <param name="materialId">The material ID string.</param>
    /// <returns><c>Ok constants</c> (possibly empty), or <c>Error (NotFound id)</c>.</returns>
    member this.GetCodeCase2964AppendixIIIConstants
        (materialId: string)
        : Result<CodeCase2964AppendixIIIConstants list, MaterialError> =
        match this.GetMaterialById materialId with
        | None -> Error(MaterialError.NotFound materialId)
        | Some material -> Ok material.SpecialProperties.AppendixIIIConstants

    /// <summary>Returns the stored Code Case 2964 Appendix III factor rule for a material, if available.</summary>
    /// <param name="materialId">The material ID string.</param>
    /// <returns><c>Ok rule</c> (possibly <c>None</c>), or <c>Error (NotFound id)</c>.</returns>
    member this.GetCodeCase2964AppendixIIIFactorRule
        (materialId: string)
        : Result<CodeCase2964AppendixIIIFactorRule option, MaterialError> =
        match this.GetMaterialById materialId with
        | None -> Error(MaterialError.NotFound materialId)
        | Some material -> Ok material.SpecialProperties.AppendixIIIFactorRule

    /// <summary>Evaluates stored Code Case 2964 factor values for a material at the requested assessment temperature.</summary>
    /// <param name="materialId">The material ID string.</param>
    /// <param name="temperature">Assessment temperature (degC).</param>
    /// <returns><c>Ok values</c>, or an appropriate <see cref="MaterialError"/>.</returns>
    member this.GetCodeCase2964EvaluatedFactorValues
        (materialId: string, temperature: float)
        : Result<CodeCase2964EvaluatedFactorValues, MaterialError> =
        match this.GetMaterialById materialId with
        | None -> Error(MaterialError.NotFound materialId)
        | Some material -> ExternalPressureTableBuilder.evaluateStoredCodeCase2964FactorRule temperature material

    /// <summary>
    /// Generates a Code Case 2964 A-vs-Sc chart from stored Appendix III inputs for a material.
    /// </summary>
    /// <param name="materialId">The material ID string.</param>
    /// <param name="temperature">Assessment/generation temperature (degC).</param>
    /// <param name="referenceDurationHours">None for a time-independent table; Some hours for an isochronous table.</param>
    /// <param name="description">Human-readable chart description.</param>
    /// <param name="pointCount">Number of generated A-vs-Sc points (minimum 2).</param>
    /// <returns><c>Ok chart</c>, or an appropriate <see cref="MaterialError"/>.</returns>
    member this.GenerateExternalPressureTableFromStoredCodeCase2964Inputs
        (
            materialId: string,
            temperature: float,
            referenceDurationHours: float option,
            description: string,
            pointCount: int
        )
        : Result<ExternalPressureTable, MaterialError> =
        match this.GetMaterialById materialId with
        | None -> Error(MaterialError.NotFound materialId)
        | Some material ->
            ExternalPressureTableBuilder.createCodeCase2964FromStoredAppendixIIIInputs
                temperature
                referenceDurationHours
                description
                pointCount
                material

    /// <summary>
    /// Generates a Code Case 2964 A-vs-Sc chart from stored Appendix III inputs with explicit calibration strategy.
    /// </summary>
    /// <param name="materialId">The material ID string.</param>
    /// <param name="temperature">Assessment/generation temperature (degC).</param>
    /// <param name="referenceDurationHours">None for a time-independent table; Some hours for an isochronous table.</param>
    /// <param name="description">Human-readable chart description.</param>
    /// <param name="pointCount">Number of generated A-vs-Sc points (minimum 2).</param>
    /// <param name="calibrationMode">Reference-based calibration strategy.</param>
    /// <returns><c>Ok chart</c>, or an appropriate <see cref="MaterialError"/>.</returns>
    member this.GenerateExternalPressureTableFromStoredCodeCase2964InputsWithCalibrationMode
        (
            materialId: string,
            temperature: float,
            referenceDurationHours: float option,
            description: string,
            pointCount: int,
            calibrationMode: CodeCase2964CalibrationMode
        ) : Result<ExternalPressureTable, MaterialError> =
        match this.GetMaterialById materialId with
        | None -> Error(MaterialError.NotFound materialId)
        | Some material ->
            ExternalPressureTableBuilder.createCodeCase2964FromStoredAppendixIIIInputsWithCalibrationMode
                temperature
                referenceDurationHours
                description
                pointCount
                calibrationMode
                material

    /// <summary>
    /// Evaluates Code Case 2964 compressive stress Sc at a given factor A on the stored chart matching temperature and time.
    /// </summary>
    /// <param name="materialId">The material ID string.</param>
    /// <param name="temperature">Chart temperature (°C) used to select the matching chart (exact match required).</param>
    /// <param name="referenceDurationHours">None selects time-independent data; Some hours selects matching isochronous data.</param>
    /// <param name="factorA">Query factor A (dimensionless). Must be within the chart range.</param>
    /// <param name="mode">Interpolation algorithm to apply.</param>
    /// <returns>
    /// <c>Ok lookup</c> — a <see cref="PropertyLookup{T}"/> containing interpolated Sc (MPa). <br/>
    /// <c>Error (NotFound id)</c> — no material with that ID. <br/>
    /// <c>Error (InvalidOperation msg)</c> — no matching Code Case 2964 chart found. <br/>
    /// <c>Error (InterpolationError e)</c> — factor A is out of range or insufficient data.
    /// </returns>
    member this.GetExternalPressureAllowableCompressiveStress
        (
            materialId: string,
            temperature: float,
            referenceDurationHours: float option,
            factorA: float,
            mode: InterpolationMode
        )
        : Result<PropertyLookup<float>, MaterialError> =
        match this.GetMaterialById materialId with
        | None -> Error(MaterialError.NotFound materialId)
        | Some material ->
            let matches =
                material.StrengthProperties.ExternalPressureTables
                |> List.filter (fun chart ->
                    chart.ReferenceTemperature = temperature
                    && chart.ReferenceDurationHours = referenceDurationHours)

            match matches with
            | [] ->
                Error(
                    MaterialError.InvalidOperation
                        $"No external-pressure table at {temperature}degC and duration {referenceDurationHours}"
                )
            | [ table ] ->
                match ExternalPressureTableInterpolation.compressiveStressFromFactorA mode factorA table with
                | Ok value ->
                    Ok
                        { Value = value
                          Temperature = temperature
                          InterpolationMode = mode }
                | Error err -> Error(MaterialError.InterpolationError err)
            | _ ->
                Error(
                    MaterialError.InvalidOperation
                        $"Multiple external-pressure tables match {temperature}degC and duration {referenceDurationHours}; select a source explicitly"
                )

    /// <summary>
    /// Evaluates Code Case 2964 compressive stress Sc at a given factor A using linear interpolation on log10(A).
    /// </summary>
    /// <param name="materialId">The material ID string.</param>
    /// <param name="temperature">Chart temperature (°C) used to select the matching chart (exact match required).</param>
    /// <param name="referenceDurationHours">None selects time-independent data; Some hours selects matching isochronous data.</param>
    /// <param name="factorA">Query factor A (dimensionless). Must be strictly positive and within the chart range.</param>
    /// <returns>
    /// <c>Ok lookup</c> — a <see cref="PropertyLookup{T}"/> containing interpolated Sc (MPa). <br/>
    /// <c>Error (NotFound id)</c> — no material with that ID. <br/>
    /// <c>Error (InvalidOperation msg)</c> — no matching Code Case 2964 chart found. <br/>
    /// <c>Error (InterpolationError e)</c> — factor A is invalid, out of range, or insufficient data.
    /// </returns>
    member this.GetExternalPressureAllowableCompressiveStressLogA
        (materialId: string, temperature: float, referenceDurationHours: float option, factorA: float)
        : Result<PropertyLookup<float>, MaterialError> =
        match this.GetMaterialById materialId with
        | None -> Error(MaterialError.NotFound materialId)
        | Some material ->
            let matches =
                material.StrengthProperties.ExternalPressureTables
                |> List.filter (fun chart ->
                    chart.ReferenceTemperature = temperature
                    && chart.ReferenceDurationHours = referenceDurationHours)

            match matches with
            | [] ->
                Error(
                    MaterialError.InvalidOperation
                        $"No external-pressure table at {temperature}degC and duration {referenceDurationHours}"
                )
            | [ table ] ->
                match ExternalPressureTableInterpolation.compressiveStressFromFactorALogScale factorA table with
                | Ok value ->
                    Ok
                        { Value = value
                          Temperature = temperature
                          InterpolationMode = Linear }
                | Error err -> Error(MaterialError.InterpolationError err)
            | _ ->
                Error(
                    MaterialError.InvalidOperation
                        $"Multiple external-pressure tables match {temperature}degC and duration {referenceDurationHours}; select a source explicitly"
                )

    // ========== STRESS-RUPTURE CURVES ==========

    /// <summary>Returns all stress-rupture curves available for a material.</summary>
    /// <param name="materialId">The material ID string.</param>
    /// <returns><c>Ok curves</c> (possibly empty), or <c>Error (NotFound id)</c>.</returns>
    member this.GetStressRuptureCurves(materialId: string) : Result<StressRuptureTable list, MaterialError> =
        match this.GetMaterialById materialId with
        | None -> Error(MaterialError.NotFound materialId)
        | Some material -> Ok material.StrengthProperties.StressRuptureCurves

    /// <summary>Evaluates the rupture stress at a given time to rupture on the stress-rupture curve matching the given temperature.</summary>
    /// <param name="materialId">The material ID string.</param>
    /// <param name="temperature">Curve temperature (°C), matched exactly against stored stress-rupture curves.</param>
    /// <param name="timeToRupture">Query time to rupture (hours). Must be within the curve’s time range.</param>
    /// <returns><c>Ok σ_r</c> (MPa), or an appropriate <see cref="MaterialError"/>.</returns>
    member this.GetStressFromStressRupture
        (materialId: string, temperature: float, timeToRupture: float)
        : Result<float, MaterialError> =
        match this.GetMaterialById materialId with
        | None -> Error(MaterialError.NotFound materialId)
        | Some material ->
            match
                material.StrengthProperties.StressRuptureCurves
                |> List.tryFind (fun table -> table.ReferenceTemperature = temperature)
            with
            | None -> Error(MaterialError.InvalidOperation $"No stress-rupture curve at {temperature}degC")
            | Some table ->
                let unwrappedTable = StressRuptureTable.unwrap table

                match PropertyTable.lookup1D timeToRupture unwrappedTable with
                | Ok result -> Ok result.Value
                | Error err -> Error err

    // ========== DESCRIPTION HELPER ==========

    /// <summary>
    /// Returns a formatted multi-line summary of a material’s identity and available data inventory.
    /// </summary>
    /// <param name="materialId">The material ID string.</param>
    /// <returns>
    /// <c>Ok description</c> — a box-drawn text block listing the material name, spec, grade,
    /// and the count of each model/curve type. <br/>
    /// <c>Error (NotFound id)</c> — no material with that ID.
    /// </returns>
    member this.DescribeMaterial(materialId: string) : Result<string, MaterialError> =
        match this.GetMaterialById materialId with
        | None -> Error(MaterialError.NotFound materialId)
        | Some material ->
            let sb = System.Text.StringBuilder()

            sb.AppendLine($"╔═══════════════════════════════════════════════════════╗")
            |> ignore

            sb.AppendLine($"║ Material: {material.Name}") |> ignore
            sb.AppendLine($"║ ASME Spec: {material.ASMESpecification}") |> ignore
            sb.AppendLine($"║ Grade: {material.Grade}") |> ignore

            if not (System.String.IsNullOrWhiteSpace material.Class_Condition_Tempering) then
                sb.AppendLine($"║ Class/Condition/Tempering: {material.Class_Condition_Tempering}")
                |> ignore

            if not (System.String.IsNullOrWhiteSpace material.AlloyIdentification_UNS) then
                sb.AppendLine($"║ UNS: {material.AlloyIdentification_UNS}") |> ignore

            if not (System.String.IsNullOrWhiteSpace material.ProductForm) then
                sb.AppendLine($"║ Product Form: {material.ProductForm}") |> ignore

            if not (System.String.IsNullOrWhiteSpace material.NominalComposition) then
                sb.AppendLine($"║ Nominal Composition: {material.NominalComposition}") |> ignore

            let matViiiI =
                material.MaximumAllowableTemperature.AsmeViiiI
                |> Option.map (fun v -> sprintf "%.1f degC" v)
                |> Option.defaultValue "n/a"

            let matViii1 =
                material.MaximumAllowableTemperature.AsmeViii1
                |> Option.map (fun v -> sprintf "%.1f degC" v)
                |> Option.defaultValue "n/a"

            let matViii2 =
                material.MaximumAllowableTemperature.AsmeViii2
                |> Option.map (fun v -> sprintf "%.1f degC" v)
                |> Option.defaultValue "n/a"

            sb.AppendLine($"║ Tmax VIII-I: {matViiiI} | VIII-1: {matViii1} | VIII-2: {matViii2}")
            |> ignore

            match material.TimeDepenedingStartTemperature with
            | Some t -> sb.AppendLine($"║ Time-Dependent Start Temperature: {t:F1} degC") |> ignore
            | None -> ()

            match material.WeldingInfo with
            | Some w ->
                sb.AppendLine($"║ Welding: P-Number={w.PNumber}, G-Number={w.GNumber}")
                |> ignore
            | None -> ()

            sb.AppendLine($"╠═══════════════════════════════════════════════════════╣")
            |> ignore

            sb.AppendLine($"║ CURVES & MODELS:") |> ignore

            sb.AppendLine($"║   • Stress-Strain Tables: {material.StrengthProperties.StressStrainTables.Length}")
            |> ignore

            sb.AppendLine($"║   • Creep Tables: {material.StrengthProperties.CreepTables.Length}")
            |> ignore

            sb.AppendLine($"║   • Stress-Rupture Curves: {material.StrengthProperties.StressRuptureCurves.Length}")
            |> ignore

            sb.AppendLine($"║   • Fatigue Curves: {material.StrengthProperties.FatigueCurves.Length}")
            |> ignore

            sb.AppendLine($"║   • Norton Models: {material.StrengthProperties.NortonModels.Length}")
            |> ignore

            sb.AppendLine($"║   • Garofalo Models: {material.StrengthProperties.GarofaloModels.Length}")
            |> ignore

            sb.AppendLine($"║   • Kachanov Omega Models: {material.StrengthProperties.KachanovOmegaModels.Length}")
            |> ignore

            sb.AppendLine($"╚═══════════════════════════════════════════════════════╝")
            |> ignore

            Ok(sb.ToString())

    // ========== STAGED XML DATA ==========

    /// <summary>Reads one staged XML file below a MaterialLibrary/data root.</summary>
    member this.ReadXmlDataFile(dataRoot: string, relativePath: string) : Result<MaterialLibraryXmlDataFile, MaterialError> =
        MaterialLibraryDataXml.readFile dataRoot relativePath

    /// <summary>Reads all staged XML files in a MaterialLibrary/data subfolder.</summary>
    member this.ReadXmlDataFolder(dataRoot: string, relativeFolder: string) : Result<MaterialLibraryXmlDataFile list, MaterialError> =
        MaterialLibraryDataXml.readFolder dataRoot relativeFolder

    /// <summary>Reads every staged XML file below a MaterialLibrary/data root.</summary>
    member this.ReadAllXmlData(dataRoot: string) : Result<MaterialLibraryXmlDataFile list, MaterialError> =
        MaterialLibraryDataXml.readAll dataRoot

    /// <summary>Reads every staged XML file below the discovered default MaterialLibrary/data root.</summary>
    member this.ReadDefaultXmlData() : Result<MaterialLibraryXmlDataFile list, MaterialError> =
        MaterialLibraryDataXml.readDefaultAll()

    /// <summary>Writes one staged XML file below a MaterialLibrary/data root.</summary>
    member this.WriteXmlDataFile(dataRoot: string, relativePath: string, document: XDocument) : Result<string, MaterialError> =
        MaterialLibraryDataXml.writeFile dataRoot relativePath document

/// <summary>Functional construction helpers for <see cref="MaterialLibrary"/>.</summary>
module MaterialLibrary =
    let private validateMaterials (materials: Material list) =
        if isNull (box materials) then
            Error(MaterialError.InvalidOperation "Material list cannot be null")
        elif materials |> List.exists (fun material -> isNull (box material)) then
            Error(MaterialError.InvalidOperation "Material list cannot contain null values")
        elif materials |> List.exists (fun material -> String.IsNullOrWhiteSpace material.Id) then
            Error(MaterialError.InvalidOperation "Every material must have a non-empty ID")
        else
            let duplicateId =
                materials
                |> List.countBy (fun material -> material.Id)
                |> List.tryFind (fun (_, count) -> count > 1)
                |> Option.map fst

            match duplicateId with
            | Some id -> Error(MaterialError.InvalidOperation $"Duplicate material ID: {id}")
            | None -> Ok materials

    /// <summary>Constructs a validated <see cref="MaterialLibrary"/> from a list of materials.</summary>
    /// <param name="materials">Initial list of <see cref="Material"/> records.</param>
    /// <returns>A new library, or a validation error.</returns>
    let create materials =
        validateMaterials materials |> Result.map MaterialLibrary

    /// <summary>Creates an empty <see cref="MaterialLibrary"/> with no materials.</summary>
    /// <returns>A <see cref="MaterialLibrary"/> with zero materials.</returns>
    let empty () = MaterialLibrary([])

    /// <summary>Returns a new library with the given material added or replacing the same ID.</summary>
    /// <param name="material">The <see cref="Material"/> to add.</param>
    /// <param name="lib">The existing library to extend.</param>
    /// <returns>A new library, or a validation error.</returns>
    let addMaterial (material: Material) (lib: MaterialLibrary) : Result<MaterialLibrary, MaterialError> =
        if isNull (box material) || String.IsNullOrWhiteSpace material.Id then
            Error(MaterialError.InvalidOperation "Material must have a non-empty ID")
        else
            lib.ListAllMaterials()
            |> List.filter (fun existing -> existing.Id <> material.Id)
            |> fun materials -> MaterialLibrary(material :: materials)
            |> Ok

    /// <summary>Serializes every material in <paramref name="lib"/> to a JSON library file (see <see cref="MaterialLibrarySerialization"/>).</summary>
    /// <param name="filePath">Destination file path.</param>
    /// <param name="version">Free-text library version recorded in the file.</param>
    /// <param name="description">Optional free-text description recorded in the file.</param>
    /// <param name="lib">The library to save.</param>
    /// <returns><c>Ok ()</c>, or a serialization/file-write error.</returns>
    let saveToFile
        (filePath: string)
        (version: string)
        (description: string option)
        (lib: MaterialLibrary)
        : Result<unit, MaterialError> =
        MaterialLibrarySerialization.saveToFile filePath version description (lib.ListAllMaterials())

    /// <summary>Loads a <see cref="MaterialLibrary"/> from a JSON library file, using <paramref name="physicalProperties"/> as a legacy fallback for materials that omit their own.</summary>
    /// <param name="filePath">Source JSON file path.</param>
    /// <param name="physicalProperties">Fallback physical properties for materials serialized before that field was added.</param>
    /// <returns>A validated <see cref="MaterialLibrary"/>, or a deserialization/validation error.</returns>
    let loadFromFile (filePath: string) (physicalProperties: PhysicalProperties) : Result<MaterialLibrary, MaterialError> =
        MaterialLibrarySerialization.loadFromFile filePath physicalProperties
        |> Result.bind create

    /// <summary>Loads a complete <see cref="MaterialLibrary"/> from a JSON library file whose materials all embed their own physical properties.</summary>
    /// <param name="filePath">Source JSON file path.</param>
    /// <returns>A validated <see cref="MaterialLibrary"/>, or a deserialization/validation error.</returns>
    let loadFromFileComplete (filePath: string) : Result<MaterialLibrary, MaterialError> =
        MaterialLibrarySerialization.loadFromFileComplete filePath
        |> Result.bind create

    /// <summary>Reads one staged XML file below a MaterialLibrary/data root.</summary>
    let readXmlDataFile dataRoot relativePath =
        MaterialLibraryDataXml.readFile dataRoot relativePath

    /// <summary>Reads all staged XML files in a MaterialLibrary/data subfolder.</summary>
    let readXmlDataFolder dataRoot relativeFolder =
        MaterialLibraryDataXml.readFolder dataRoot relativeFolder

    /// <summary>Reads every staged XML file below a MaterialLibrary/data root.</summary>
    let readAllXmlData dataRoot =
        MaterialLibraryDataXml.readAll dataRoot

    /// <summary>Reads every staged XML file below the discovered default MaterialLibrary/data root.</summary>
    let readDefaultXmlData () =
        MaterialLibraryDataXml.readDefaultAll()

    /// <summary>Writes one staged XML file below a MaterialLibrary/data root.</summary>
    let writeXmlDataFile dataRoot relativePath document =
        MaterialLibraryDataXml.writeFile dataRoot relativePath document
