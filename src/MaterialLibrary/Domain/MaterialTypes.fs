namespace MaterialLibrary.Domain

// ========== GROUPED PROPERTY RECORDS ==========

/// <summary>
/// Tensile, compressive, creep, fatigue, and rupture data grouped together.
/// Includes experimental curves and analytical model coefficients.
/// All curve types are now unified as PropertyTable instances with domain metadata.
/// </summary>
/// <remarks>
/// Units: Temperature in degC, Stress in MPa, Time in hours, Strain in %.
/// PropertyTable metadata fields capture curve semantics such as temperature and optional duration.
/// </remarks>
type StrengthProperties =
    {
        /// ASME allowable stresses at one or more temperatures (degC → MPa, DIV 1 or DIV 2).
        AllowableStresses: AllowableStress list
        /// Database allowable-stress rows with source table, case, and size-range identity preserved.
        /// Grouped by Size/Diameter/Thickness band, split between Division 1 (normal and high
        /// alternative), Division 2, and bolting.
        AllowableStressDatasets: AllowableStressDataset list
        /// Governing minimum-strength curve: Sy(T) and Su(T) in MPa, with no size dependence.
        /// Elongation and reduction of area are room-temperature scalars and live in BasicProperties.
        TensileProperties: TensileProperties list
        /// Sy(T) and Su(T) curves kept one per published Size/Diameter/Thickness band, so a heavy
        /// section is not silently given the strength of a light one.
        TensileStrengthDatasets: TensileStrengthDataset list
        /// Optional compressive property sets at one or more temperatures (strengths in MPa).
        CompressionProperties: CompressionProperties list option
        /// Stress-strain curves (X=strain %, Y=stress MPa) at one or more temperatures.
        StressStrainTables: StressStrainTable list
        /// Cyclic stress-strain curves (K_css, n_css) at one or more temperatures.
        CyclicStrainTables: CyclicStrainTable list
        /// External-pressure material tables (X=Factor A, Y=Factor B / allowable compressive stress MPa).
        ExternalPressureTables: ExternalPressureTable list
        /// Creep models: Norton Power Law coefficients at one or more temperatures.
        NortonModels: NortonPowerLawCoefficients list
        /// Creep models: Garofalo hyperbolic-sine coefficients at one or more temperatures.
        GarofaloModels: GarofaloCoefficients list
        /// Creep models: Kachanov–Robinson damage model coefficients at one or more temperatures.
        KachanovOmegaModels: KachanovOmegaModel list
        /// Creep curves (X=time h, Y=strain %) at one or more conditions.
        CreepTables: CreepTable list
        /// Average stress (MPa) vs temperature to reach a reference creep-rate criterion (e.g. SC = average
        /// stress for 0.01%/1000h creep rate); one table per reference rate.
        AverageCreepStrainRateStress: CreepStrainRateTable list
        /// Minimum stress (MPa) vs temperature to reach a reference creep-rate criterion; one table per
        /// reference rate.
        MinimumCreepStrainRateStress: CreepStrainRateTable list
        /// Stress-rupture curves (X=time to rupture h, Y=stress MPa) at constant T.
        StressRuptureCurves: StressRuptureTable list
        /// Average rupture stress (SRavg, MPa) vs temperature at a reference duration; one table per
        /// reference duration (e.g. 100,000 h).
        AverageCreepRuptureStress: CreepStressRuptureTable list
        /// Minimum rupture stress (SRmin, MPa) vs temperature at a reference duration; one table per
        /// reference duration (e.g. 100,000 h).
        MinimumCreepRuptureStress: CreepStressRuptureTable list
        /// Larson–Miller master curves for time-temperature parameter correlation.
        LarsonMillerCurves: LarsonMillerCurve list
        /// Fatigue S-N curves (X=cycles, Y=stress amplitude Sa MPa) at one or more temperatures.
        FatigueCurves: FatigueTable list
    }

/// <summary>
/// Physical property tables: density, elastic modulus, Poisson ratio, thermal expansion, conductivity, specific heat.
/// </summary>
/// <remarks>
/// Units: T in degC, α in 1/degC, E in MPa, Cp in J/(kg*K), ρ in kg/m^3, κ in W/(m*K).
/// Reuses existing <see cref="PhysicalPropertiesTable"/> definition.
/// </remarks>
type PhysicalProperties = PhysicalPropertiesTable

/// <summary>
/// Special ASME Code Case 2964 data for time-dependent analysis and external pressure calculations.
/// </summary>
/// <remarks>
/// Data reported inside Domain.MaterialDatabases.fs.
/// </remarks>
type SpecialProperties =
    {
        /// Code Case 2964 Appendix III constants A_i and B_i by temperature.
        AppendixIIIConstants: CodeCase2964AppendixIIIConstants list
        /// Code Case 2964 Appendix III material-family factor rule for m2 and ε′p.
        AppendixIIIFactorRule: CodeCase2964AppendixIIIFactorRule option
    }

/// <summary>
/// Maximum allowable service temperatures across ASME Section VIII divisions.
/// </summary>
/// <remarks>
/// Units: all values in degC.
/// </remarks>
type MaximumAllowableTemperature =
    {
        /// Maximum allowable temperature for ASME VIII-I (degC).
        AsmeViiiI: float option
        /// Maximum allowable temperature for ASME VIII-1 (degC).
        AsmeViii1: float option
        /// Maximum allowable temperature for ASME VIII-2 (degC).
        AsmeViii2: float option
    }

/// <summary>
/// Welding classification metadata.
/// </summary>
type WeldingInfo =
    {
        /// ASME P-Number classification.
        PNumber: string
        /// ASME Group Number classification.
        GNumber: string
    }

// ========== COMPLETE MATERIAL RECORD ==========

/// Allowable-stress curve family selected for this material instance.
type MaterialAllowableStressLevel =
    | StandardAllowableStress
    | HighAllowableStress

/// ASME construction code supported by the selected material data.
type AsmeCode =
    | AsmeSectionI
    | AsmeSectionVIII1
    | AsmeSectionVIII2

/// ASME material family used for database classification.
type AsmeMaterialFamily =
    | CS
    | QT
    | LTCS
    | LAS1_00
    | LAS1_25
    | LAS2_25
    | LAS5_00
    | LAS9_00
    | SSA
    | SSF
    | SSM
    | SSD
    | SSDPlus

module AsmeMaterialFamily =
    let code =
        function
        | CS -> "CS"
        | QT -> "QT"
        | LTCS -> "LTCS"
        | LAS1_00 -> "LAS1.00"
        | LAS1_25 -> "LAS1.25"
        | LAS2_25 -> "LAS2.25"
        | LAS5_00 -> "LAS5.00"
        | LAS9_00 -> "LAS9.00"
        | SSA -> "SSA"
        | SSF -> "SSF"
        | SSM -> "SSM"
        | SSD -> "SSD"
        | SSDPlus -> "SSD+"

/// <summary>
/// Aggregate material record conforming to ASME Section II Part D — Version 3.
/// Holds all temperature-independent and temperature-dependent properties, plus
/// creep models, experimental curves, and metadata, organized into semantic groups.
/// </summary>
/// <remarks>
/// Fixed units used across this model (also for serialization/deserialization):
/// Temperature=degC, Stress/Strength=MPa, Time=hours, Density=kg/m^3,
/// SpecificHeat=J/(kg*K), ThermalConductivity=W/(m*K), ThermalExpansion=1/degC.
/// </remarks>
type Material =
    {
        /// Unique material identifier (e.g. "SA-106-B").
        Id: string
        /// Full material name composed as: Specification + Grade + Class/Condition/Tempering + UNS.
        Name: string
        /// Product form (e.g. plate, pipe, forging).
        ProductForm: string
        /// Nominal composition text (e.g. "2 1/4Cr-1Mo").
        NominalComposition: string
        /// Material specification used for naming and identification (e.g. "ASME SA-106").
        Specification: string
        /// ASME material specification (e.g. "ASME SA-106").
        /// Kept for backward compatibility with existing API/JSON naming.
        ASMESpecification: string
        /// Material grade designation (e.g. "B").
        Grade: string
        /// Class / condition / tempering designation.
        Class_Condition_Tempering: string
        /// UNS alloy identifier.
        AlloyIdentification_UNS: string
        /// Optional database family classification (CS, LTCS, LAS, or stainless family).
        Family: AsmeMaterialFamily option
        /// Standard or high allowable-stress curve selected for this material instance.
        AllowableStressLevel: MaterialAllowableStressLevel
        /// ASME construction codes for which allowable-stress data are available.
        ApplicableAsmeCodes: AsmeCode list
        /// ASME Section II-D references attached directly to material-level data such as Sy and Su.
        AsmeNoteReferences: AsmeNoteReference list

        // === BASIC PROPERTIES ===
        /// Temperature-independent, room-temperature properties.
        /// Units: SMYS/SMUTS in MPa.
        BasicProperties: BasicProperties

        // === GROUPED PROPERTY COLLECTIONS ===
        /// Physical properties: Density, E, Nu, Alpha, k, Cp vs temperature.
        PhysicalProperties: PhysicalProperties
        /// Strength properties: Tensile, compressive, allowable stress, creep, fatigue, rupture data.
        StrengthProperties: StrengthProperties
        /// Special properties: ASME Code Case 2964 data.
        SpecialProperties: SpecialProperties

        /// Maximum allowable temperatures by ASME Section VIII division (degC).
        MaximumAllowableTemperature: MaximumAllowableTemperature
        /// Start temperature for time-dependent behavior (degC).
        TimeDepenedingStartTemperature: float option
        /// Welding classification metadata.
        WeldingInfo: WeldingInfo option

        // === METADATA ===
        /// UTC timestamp when this record was first created.
        CreatedDate: System.DateTime
        /// UTC timestamp of the most recent modification.
        LastModified: System.DateTime
        /// Optional free-text notes (data sources, limitations, revision history).
        Notes: string option
    }

/// <summary>Factory and builder functions for constructing <see cref="Material"/> records.</summary>
/// <remarks>
/// All <c>add*</c> functions follow an immutable-update pattern: they return a new <see cref="Material"/>
/// with the relevant field replaced and <c>LastModified</c> refreshed to the current UTC time.
/// </remarks>
module Material =
    let private normalizeIdentityPart (value: string) : string option =
        if System.String.IsNullOrWhiteSpace value then
            None
        else
            Some(value.Trim())

    /// <summary>
    /// Composes the canonical material name using Specification + Grade + Class/Condition/Tempering + UNS.
    /// Empty parts are skipped.
    /// </summary>
    let composeMaterialName
        (specification: string)
        (grade: string)
        (classConditionTempering: string)
        (alloyIdentificationUns: string)
        : string =
        [ specification; grade; classConditionTempering; alloyIdentificationUns ]
        |> List.choose normalizeIdentityPart
        |> String.concat " "

    /// <summary>Creates a new <see cref="Material"/> with required fields and all optional collections empty.</summary>
    /// <param name="id">Unique string identifier (e.g. <c>"SA-516-70"</c>).</param>
    /// <param name="name">Human-readable material name.</param>
    /// <param name="spec">ASME specification number (e.g. <c>"SA-516"</c>).</param>
    /// <param name="grade">Material grade or class (e.g. <c>"Grade 70"</c>).</param>
    /// <param name="basicProps">Minimum mechanical properties (SMYS, SMUTS, density, Poisson's ratio).</param>
    /// <param name="physicalTable">Temperature-dependent physical properties tables.</param>
    /// <returns>A <see cref="Material"/> record with all property lists initialised to empty and timestamps set to UTC now.</returns>
    let create id name spec grade basicProps physicalTable =
        let classConditionTempering = ""
        let alloyIdentificationUns = ""

        let composedName =
            composeMaterialName spec grade classConditionTempering alloyIdentificationUns

        let resolvedName =
            if System.String.IsNullOrWhiteSpace composedName then
                name
            else
                composedName

        { Id = id
          Name = resolvedName
          ProductForm = ""
          NominalComposition = ""
          Specification = spec
          ASMESpecification = spec
          Grade = grade
          Class_Condition_Tempering = classConditionTempering
          AlloyIdentification_UNS = alloyIdentificationUns
          Family = None
          AllowableStressLevel = StandardAllowableStress
          ApplicableAsmeCodes = []
          AsmeNoteReferences = []
          BasicProperties = basicProps
          PhysicalProperties = physicalTable
          StrengthProperties =
            { AllowableStresses = []
              AllowableStressDatasets = []
              TensileProperties = []
              TensileStrengthDatasets = []
              CompressionProperties = None
              StressStrainTables = []
              CyclicStrainTables = []
              ExternalPressureTables = []
              NortonModels = []
              GarofaloModels = []
              KachanovOmegaModels = []
              CreepTables = []
              AverageCreepStrainRateStress = []
              MinimumCreepStrainRateStress = []
              StressRuptureCurves = []
              AverageCreepRuptureStress = []
              MinimumCreepRuptureStress = []
              LarsonMillerCurves = []
              FatigueCurves = [] }
          SpecialProperties =
            { AppendixIIIConstants = []
              AppendixIIIFactorRule = None }
          MaximumAllowableTemperature =
            { AsmeViiiI = None
              AsmeViii1 = None
              AsmeViii2 = None }
          TimeDepenedingStartTemperature = None
          WeldingInfo = None
          CreatedDate = System.DateTime.UtcNow
          LastModified = System.DateTime.UtcNow
          Notes = None }

    /// <summary>
    /// Sets material identity metadata and refreshes <c>Name</c> using
    /// Specification + Grade + Class/Condition/Tempering + UNS.
    /// </summary>
    /// <param name="productForm">Product form (free text).</param>
    /// <param name="nominalComposition">Nominal composition (free text).</param>
    /// <param name="specification">Specification string used in the composed material name.</param>
    /// <param name="grade">Grade string used in the composed material name.</param>
    /// <param name="classConditionTempering">Class/condition/tempering string used in the composed material name.</param>
    /// <param name="alloyIdentificationUns">UNS identifier used in the composed material name.</param>
    /// <param name="mat">Source material.</param>
    /// <returns>Updated material with refreshed <c>Name</c> and <c>LastModified</c>.</returns>
    let setIdentity
        (productForm: string)
        (nominalComposition: string)
        (specification: string)
        (grade: string)
        (classConditionTempering: string)
        (alloyIdentificationUns: string)
        (mat: Material)
        : Material =
        let normalizeOrFallback (candidate: string) (fallback: string) =
            if System.String.IsNullOrWhiteSpace candidate then
                fallback
            else
                candidate.Trim()

        let resolvedSpecification = normalizeOrFallback specification mat.Specification
        let resolvedGrade = normalizeOrFallback grade mat.Grade

        let resolvedProductForm =
            if System.String.IsNullOrWhiteSpace productForm then
                ""
            else
                productForm.Trim()

        let resolvedNominalComposition =
            if System.String.IsNullOrWhiteSpace nominalComposition then
                ""
            else
                nominalComposition.Trim()

        let resolvedClassConditionTempering =
            if System.String.IsNullOrWhiteSpace classConditionTempering then
                ""
            else
                classConditionTempering.Trim()

        let resolvedAlloyIdentificationUns =
            if System.String.IsNullOrWhiteSpace alloyIdentificationUns then
                ""
            else
                alloyIdentificationUns.Trim()

        let composedName =
            composeMaterialName
                resolvedSpecification
                resolvedGrade
                resolvedClassConditionTempering
                resolvedAlloyIdentificationUns

        let resolvedName =
            if System.String.IsNullOrWhiteSpace composedName then
                mat.Name
            else
                composedName

        { mat with
            Name = resolvedName
            ProductForm = resolvedProductForm
            NominalComposition = resolvedNominalComposition
            Specification = resolvedSpecification
            ASMESpecification = resolvedSpecification
            Grade = resolvedGrade
            Class_Condition_Tempering = resolvedClassConditionTempering
            AlloyIdentification_UNS = resolvedAlloyIdentificationUns
            LastModified = System.DateTime.UtcNow }

    /// <summary>
    /// Sets maximum allowable temperatures by ASME Section VIII divisions.
    /// </summary>
    /// <param name="asmeViiiI">Maximum allowable temperature for ASME VIII-I (degC).</param>
    /// <param name="asmeViii1">Maximum allowable temperature for ASME VIII-1 (degC).</param>
    /// <param name="asmeViii2">Maximum allowable temperature for ASME VIII-2 (degC).</param>
    /// <param name="mat">Source material.</param>
    /// <returns>Updated material with refreshed <c>LastModified</c>.</returns>
    let setMaximumAllowableTemperature
        (asmeViiiI: float option)
        (asmeViii1: float option)
        (asmeViii2: float option)
        (mat: Material)
        : Material =
        { mat with
            MaximumAllowableTemperature =
                { AsmeViiiI = asmeViiiI
                  AsmeViii1 = asmeViii1
                  AsmeViii2 = asmeViii2 }
            LastModified = System.DateTime.UtcNow }

    /// <summary>
    /// Sets the temperature threshold where time-dependent behavior starts.
    /// </summary>
    /// <param name="value">Threshold temperature (degC), or None when not defined.</param>
    /// <param name="mat">Source material.</param>
    /// <returns>Updated material with refreshed <c>LastModified</c>.</returns>
    let setTimeDepenedingStartTemperature (value: float option) (mat: Material) : Material =
        { mat with
            TimeDepenedingStartTemperature = value
            LastModified = System.DateTime.UtcNow }

    /// <summary>
    /// Sets welding classification metadata (P-Number and G-Number).
    /// </summary>
    /// <param name="pNumber">ASME P-Number.</param>
    /// <param name="gNumber">ASME Group Number.</param>
    /// <param name="mat">Source material.</param>
    /// <returns>Updated material with refreshed <c>LastModified</c>.</returns>
    let setWeldingInfo (pNumber: string) (gNumber: string) (mat: Material) : Material =
        let normalize s =
            if System.String.IsNullOrWhiteSpace s then "" else s.Trim()

        { mat with
            WeldingInfo =
                Some
                    { PNumber = normalize pNumber
                      GNumber = normalize gNumber }
            LastModified = System.DateTime.UtcNow }

    /// <summary>
    /// Returns the material strength ratio SMYS/SMUTS.
    /// </summary>
    /// <param name="mat">The material record.</param>
    /// <returns>
    /// <c>Some ratio</c> when SMUTS is strictly positive; otherwise <c>None</c>.
    /// </returns>
    let tryGetSmysToSmutsRatio (mat: Material) : float option =
        let smys = mat.BasicProperties.SpecifiedMinimumYieldStrength
        let smuts = mat.BasicProperties.SpecifiedMinimumUltimateStrength

        if smuts > 0.0 then Some(smys / smuts) else None

    /// <summary>Returns a new material with the tensile properties list replaced.</summary>
    /// <param name="props">List of <see cref="TensileProperties"/> records (one per temperature).</param>
    /// <param name="mat">The source material to update.</param>
    /// <returns>Updated <see cref="Material"/> with refreshed <c>LastModified</c>.</returns>
    let addTensileProperties props mat =
        { mat with
            StrengthProperties =
                { mat.StrengthProperties with
                    TensileProperties = props |> List.sortBy (fun p -> p.Temperature) }
            LastModified = System.DateTime.UtcNow }

    /// <summary>Returns a new material with the stress-strain table list replaced.</summary>
    /// <param name="tables">Stress-strain tables keyed by temperature and optional duration.</param>
    /// <param name="mat">The source material to update.</param>
    /// <returns>Updated <see cref="Material"/> with refreshed <c>LastModified</c>.</returns>
    let addStressStrainTables tables mat =
        { mat with
            StrengthProperties =
                { mat.StrengthProperties with
                    StressStrainTables = tables |> List.sortBy (fun table -> table.ReferenceTemperature) }
            LastModified = System.DateTime.UtcNow }

    /// <summary>Returns a new material with the Norton Power Law model list replaced.</summary>
    /// <param name="models">List of <see cref="NortonPowerLawCoefficients"/> records (one per temperature).</param>
    /// <param name="mat">The source material to update.</param>
    /// <returns>Updated <see cref="Material"/> with refreshed <c>LastModified</c>.</returns>
    let addNortonModels models mat =
        { mat with
            StrengthProperties =
                { mat.StrengthProperties with
                    NortonModels = models |> List.sortBy (fun m -> m.Temperature) }
            LastModified = System.DateTime.UtcNow }

    /// <summary>Returns a new material with the Garofalo model list replaced.</summary>
    /// <param name="models">List of <see cref="GarofaloCoefficients"/> records (one per temperature).</param>
    /// <param name="mat">The source material to update.</param>
    /// <returns>Updated <see cref="Material"/> with refreshed <c>LastModified</c>.</returns>
    let addGarofaloModels models mat =
        { mat with
            StrengthProperties =
                { mat.StrengthProperties with
                    GarofaloModels = models |> List.sortBy (fun m -> m.Temperature) }
            LastModified = System.DateTime.UtcNow }

    /// <summary>Returns a new material with the Kachanov–Robinson damage model list replaced.</summary>
    /// <param name="models">List of <see cref="KachanovOmegaModel"/> records (one per temperature).</param>
    /// <param name="mat">The source material to update.</param>
    /// <returns>Updated <see cref="Material"/> with refreshed <c>LastModified</c>.</returns>
    let addKachanovOmegaModels models mat =
        { mat with
            StrengthProperties =
                { mat.StrengthProperties with
                    KachanovOmegaModels = models |> List.sortBy (fun m -> m.Temperature) }
            LastModified = System.DateTime.UtcNow }

    /// <summary>Returns a new material with the experimental creep curve list replaced.</summary>
    /// <param name="curves">Creep tables, one per temperature/stress condition.</param>
    /// <param name="mat">The source material to update.</param>
    /// <returns>Updated <see cref="Material"/> with refreshed <c>LastModified</c>.</returns>
    let addCreepTables tables mat =
        { mat with
            StrengthProperties =
                { mat.StrengthProperties with
                    CreepTables = tables |> List.sortBy (fun table -> table.ReferenceTemperature) }
            LastModified = System.DateTime.UtcNow }

    /// <summary>Returns a new material with the external-pressure table list replaced.</summary>
    /// <param name="tables">External-pressure material tables.</param>
    /// <param name="mat">The source material to update.</param>
    /// <returns>Updated <see cref="Material"/> with refreshed <c>LastModified</c>.</returns>
    let addExternalPressureTables tables mat =
        { mat with
            StrengthProperties =
                { mat.StrengthProperties with
                    ExternalPressureTables = tables }
            LastModified = System.DateTime.UtcNow }

    /// <summary>Returns a new material with the Code Case 2964 Appendix III constants list replaced.</summary>
    /// <param name="constants">List of <see cref="CodeCase2964AppendixIIIConstants"/> records.</param>
    /// <param name="mat">The source material to update.</param>
    /// <returns>Updated <see cref="Material"/> with refreshed <c>LastModified</c>.</returns>
    let addCodeCase2964AppendixIIIConstants constants mat =
        { mat with
            SpecialProperties =
                { mat.SpecialProperties with
                    AppendixIIIConstants = constants }
            LastModified = System.DateTime.UtcNow }

    /// <summary>Returns a new material with the Code Case 2964 Appendix III factor rule replaced.</summary>
    /// <param name="factorRule">The factor rule to store.</param>
    /// <param name="mat">The source material to update.</param>
    /// <returns>Updated <see cref="Material"/> with refreshed <c>LastModified</c>.</returns>
    let setCodeCase2964AppendixIIIFactorRule factorRule mat =
        { mat with
            SpecialProperties =
                { mat.SpecialProperties with
                    AppendixIIIFactorRule = Some factorRule }
            LastModified = System.DateTime.UtcNow }

    /// <summary>Adds or replaces one external-pressure table by temperature, duration, and source.</summary>
    /// <param name="table">Table to insert or replace.</param>
    /// <param name="mat">The source material to update.</param>
    /// <returns>Updated <see cref="Material"/> with refreshed <c>LastModified</c>, or a conversion error.</returns>
    let addOrReplaceExternalPressureTable
        (table: ExternalPressureTable)
        (mat: Material)
        : Result<Material, MaterialError> =
        ExternalPressureTable.validate table
        |> Result.map (fun _ ->
            let filtered =
                mat.StrengthProperties.ExternalPressureTables
                |> List.filter (fun item ->
                    item.ReferenceTemperature <> table.ReferenceTemperature
                    || item.ReferenceDurationHours <> table.ReferenceDurationHours
                    || item.Source <> table.Source)

            { mat with
                StrengthProperties =
                    { mat.StrengthProperties with
                        ExternalPressureTables =
                            (filtered @ [ table ]) |> List.sortBy (fun item -> item.ReferenceTemperature) }
                LastModified = System.DateTime.UtcNow })

    /// <summary>Adds or replaces one Code Case 2964 Appendix III constants row by temperature.</summary>
    /// <param name="constants">Constants row to insert or replace.</param>
    /// <param name="mat">The source material to update.</param>
    /// <returns>Updated <see cref="Material"/> with refreshed <c>LastModified</c>.</returns>
    let addOrReplaceCodeCase2964AppendixIIIConstants (constants: CodeCase2964AppendixIIIConstants) (mat: Material) =
        let filtered =
            mat.SpecialProperties.AppendixIIIConstants
            |> List.filter (fun item -> item.Temperature <> constants.Temperature)

        { mat with
            SpecialProperties =
                { mat.SpecialProperties with
                    AppendixIIIConstants = (filtered @ [ constants ]) |> List.sortBy (fun c -> c.Temperature) }
            LastModified = System.DateTime.UtcNow }

    /// <summary>Returns a new material with the stress-rupture curve list replaced.</summary>
    /// <param name="curves">List of <see cref="StressRuptureTable"/> records (one per temperature).</param>
    /// <param name="mat">The source material to update.</param>
    /// <returns>Updated <see cref="Material"/> with refreshed <c>LastModified</c>.</returns>
    let addStressRuptureCurves curves mat =
        { mat with
            StrengthProperties =
                { mat.StrengthProperties with
                    StressRuptureCurves = curves |> List.sortBy (fun c -> c.ReferenceTemperature) }
            LastModified = System.DateTime.UtcNow }

    /// <summary>Returns a new material with the fatigue curve list replaced.</summary>
    /// <param name="curves">List of <see cref="FatigueTable"/> records (one per environment/temperature).</param>
    /// <param name="mat">The source material to update.</param>
    /// <returns>Updated <see cref="Material"/> with refreshed <c>LastModified</c>.</returns>
    let addFatigueCurves curves mat =
        { mat with
            StrengthProperties =
                { mat.StrengthProperties with
                    FatigueCurves = curves }
            LastModified = System.DateTime.UtcNow }

    /// <summary>
    /// Adds or replaces one compression-properties row (matched by temperature).
    /// </summary>
    /// <param name="compression">Compression row to insert or replace.</param>
    /// <param name="mat">The source material to update.</param>
    /// <returns>Updated <see cref="Material"/> with refreshed <c>LastModified</c>.</returns>
    let addOrReplaceCompressionProperties (compression: CompressionProperties) (mat: Material) : Material =
        let existing = defaultArg mat.StrengthProperties.CompressionProperties []

        let filtered =
            existing
            |> List.filter (fun item -> item.Temperature <> compression.Temperature)

        { mat with
            StrengthProperties =
                { mat.StrengthProperties with
                    CompressionProperties = Some(filtered @ [ compression ]) }
            LastModified = System.DateTime.UtcNow }

