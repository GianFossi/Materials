namespace MaterialLibrary.Domain

// ========== ERROR TYPES ==========

/// <summary>Errors that can arise during table lookup and interpolation.</summary>
type InterpolationError =
    /// The requested value lies outside the tabulated temperature range [min, max].
    | OutOfRange of float * float
    /// The table contains fewer than two points, making interpolation impossible.
    | InsufficientData
    /// The input arguments are logically invalid (described in the message).
    | InvalidInput of string

/// <summary>Top-level errors returned by MaterialLibrary API calls.</summary>
type MaterialError =
    /// No material with the given identifier was found in the library.
    | NotFound of string
    /// An interpolation operation failed; wraps the underlying InterpolationError.
    | InterpolationError of InterpolationError
    /// The requested operation cannot be performed in the current state (reason in the message).
    | InvalidOperation of string
    /// A creep model evaluation failed (reason in the message).
    | CreepModelError of string

// ========== BASIC PROPERTIES (FIXED, ROOM TEMPERATURE) ==========

/// <summary>
/// Results of the room-temperature tensile coupon test, and only those.
/// </summary>
/// <remarks>
/// <para>
/// Every value here is measured at room temperature on a standard tensile coupon: elongation at
/// fracture in each rolling direction, reduction of area, and the two specified minimum strengths.
/// They are single scalars, not curves, and they must not be confused with the temperature-dependent
/// minimum strengths Sy(T) and Su(T) — those live in
/// <see cref="StrengthProperties.TensileStrengthDatasets"/>, where each size/thickness group carries
/// its own curve.
/// </para>
/// <para>
/// Units: elongation and reduction of area in percent; SMYS and SMUTS in MPa.
/// See ASME Section II Part D, Tables Y-1 and U.
/// </para>
/// </remarks>
type BasicProperties =
    {
        /// Minimum elongation at fracture (%) on a coupon cut parallel to the rolling direction,
        /// at room temperature. <c>None</c> when the source does not report it.
        ElongationLongitudinalPercent: float option
        /// Minimum elongation at fracture (%) on a coupon cut across the rolling direction,
        /// at room temperature. <c>None</c> when the source does not report it.
        ElongationTransversePercent: float option
        /// Minimum reduction of cross-sectional area at fracture (%), at room temperature.
        ReductionOfAreaPercent: float
        /// Specified Minimum Yield Strength — SMYS (MPa), at room temperature.
        /// See ASME Section II Part D, Table Y-1.
        SpecifiedMinimumYieldStrength: float
        /// Specified Minimum Ultimate Tensile Strength — SMUTS (MPa), at room temperature.
        /// See ASME Section II Part D, Table U.
        SpecifiedMinimumUltimateStrength: float
    }

// ========== PHYSICAL PROPERTIES TABLES (Temperature-Dependent) ==========

/// <summary>A single data point for the mean coefficient of thermal expansion α(T).</summary>
/// <remarks>
/// α(T) is defined as the mean linear coefficient from a reference temperature to T,
/// per ASME Section II Part D, Table TE. When loading from ASME databases that expose
/// both instantaneous and average thermal-expansion tables, this model uses the average table.
/// </remarks>
type ThermalExpansionTablePoint =
    {
        /// Temperature at which α is evaluated (°C).
        Temperature: float
        /// Mean coefficient of linear thermal expansion from reference temperature to T (1/°C).
        ExpansionCoefficient: float
    }

/// <summary>A single data point for the Young's modulus of elasticity E(T).</summary>
/// <remarks>E(T) values are tabulated in ASME Section II Part D, Table TM.</remarks>
type ElasticModulusTablePoint =
    {
        /// Temperature at which E is evaluated (°C).
        Temperature: float
        /// Modulus of elasticity (MPa).
        ElasticModulus: float
        /// Optional Poisson's ratio ν (dimensionless).
        PoissonRatio: float option
    }

/// <summary>Helpers for deriving elastic quantities from <see cref="ElasticModulusTablePoint"/>.</summary>
type ElasticModulusTablePoint with
    /// <summary>Computes shear modulus G from E and ν using G = E / (2 * (1 + ν)).</summary>
    /// <param name="elasticModulus">Young's modulus E (MPa).</param>
    /// <param name="poissonRatio">Poisson's ratio ν (dimensionless).</param>
    /// <returns>Shear modulus G (MPa).</returns>
    static member ComputeShearModulus(elasticModulus: float, poissonRatio: float) : float =
        elasticModulus / (2.0 * (1.0 + poissonRatio))

    /// <summary>Computes G for this row when Poisson's ratio is available.</summary>
    /// <returns><c>Some G</c> (MPa) when ν is known; otherwise <c>None</c>.</returns>
    member this.TryGetShearModulus() : float option =
        this.PoissonRatio
        |> Option.map (fun nu -> ElasticModulusTablePoint.ComputeShearModulus(this.ElasticModulus, nu))

/// <summary>A single data point for the specific heat capacity Cp(T).</summary>
type SpecificHeatTablePoint =
    {
        /// Temperature at which Cp is evaluated (°C).
        Temperature: float
        /// Specific heat at constant pressure (J/(kg·K)).
        SpecificHeat: float
    }

/// <summary>A single data point for mass density ρ(T).</summary>
type DensityTablePoint =
    {
        /// Temperature at which density is evaluated (°C).
        Temperature: float
        /// Mass density at this temperature (kg/m^3).
        Density: float
    }

/// <summary>
/// Temperature-dependent physical properties, independent of section thickness.
/// Stores lookup tables for α(T), E(T), Cp(T), ρ(T), and optionally κ(T).
/// </summary>
type PhysicalPropertiesTable =
    {
        /// Reference temperature used by the thermal expansion average table (°C). Default is 20 °C.
        ThermalExpansionReferenceTemperature: float

        /// Table of mean thermal expansion coefficient α vs temperature. See ASME Table TE.
        ThermalExpansionTable: ThermalExpansionTablePoint list

        /// Table of Young's modulus E vs temperature. See ASME Table TM.
        ElasticModulusTable: ElasticModulusTablePoint list

        /// Optional table of specific heat Cp vs temperature (J/(kg·K)).
        SpecificHeatTable: SpecificHeatTablePoint list option

        /// Table of mass density ρ vs temperature (kg/m^3).
        DensityTable: DensityTablePoint list

        /// Optional table of thermal conductivity κ vs temperature (W/(m·K)), stored as (T, κ) pairs.
        ThermalConductivityTable: (float * float) list option

        /// Optional table of thermal diffusivity a vs temperature (m^2/s), stored as (T, a) pairs.
        /// Grouped with specific heat and thermal conductivity because the three describe the same
        /// heat-transfer behaviour and ASME publishes them together per material group.
        ThermalDiffusivityTable: (float * float) list option
    }

// ========== HELPER MODULES ==========

/// <summary>Factory functions for constructing and querying <see cref="BasicProperties"/> records.</summary>
module BasicProperties =
    /// <summary>Creates a <see cref="BasicProperties"/> record from individual arguments.</summary>
    /// <param name="elongationLongitudinal">Room-temperature elongation at fracture (%) along the rolling direction, or <c>None</c>.</param>
    /// <param name="elongationTransverse">Room-temperature elongation at fracture (%) across the rolling direction, or <c>None</c>.</param>
    /// <param name="ductility">Room-temperature minimum reduction of area at fracture (%).</param>
    /// <param name="smys">Specified Minimum Yield Strength (MPa).</param>
    /// <param name="smuts">Specified Minimum Ultimate Tensile Strength (MPa).</param>
    /// <returns>A fully populated <see cref="BasicProperties"/> record.</returns>
    let create
        (elongationLongitudinal: float option)
        (elongationTransverse: float option)
        (ductility: float)
        (smys: float)
        (smuts: float)
        =
        { ElongationLongitudinalPercent = elongationLongitudinal
          ElongationTransversePercent = elongationTransverse
          ReductionOfAreaPercent = ductility
          SpecifiedMinimumYieldStrength = smys
          SpecifiedMinimumUltimateStrength = smuts }

    /// <summary>
    /// Elongation (%) to use when a single governing value is required.
    /// </summary>
    /// <param name="properties">Room-temperature tensile-test results.</param>
    /// <returns>
    /// The smaller of the two reported directions, the one that is reported when only one is, or
    /// <c>None</c> when neither is.
    /// </returns>
    /// <remarks>
    /// Transverse coupons are normally the weaker direction, so taking the minimum keeps the
    /// governing value conservative without assuming which direction the source measured.
    /// </remarks>
    let governingElongationPercent (properties: BasicProperties) : float option =
        match properties.ElongationLongitudinalPercent, properties.ElongationTransversePercent with
        | Some longitudinal, Some transverse -> Some(min longitudinal transverse)
        | Some value, None
        | None, Some value -> Some value
        | None, None -> None

/// <summary>Factory helpers for <see cref="ElasticModulusTablePoint"/>.</summary>
module ElasticModulusTablePoint =
    /// <summary>
    /// Creates one elastic-modulus table row.
    /// </summary>
    /// <param name="temperature">Temperature (°C).</param>
    /// <param name="elasticModulus">Young's modulus E (MPa).</param>
    /// <param name="poissonRatio">Optional Poisson's ratio ν (dimensionless).</param>
    /// <returns>Normalized elastic-modulus row with E and optional ν. Compute G on demand.</returns>
    let create (temperature: float) (elasticModulus: float) (poissonRatio: float option) : ElasticModulusTablePoint =
        { Temperature = temperature
          ElasticModulus = elasticModulus
          PoissonRatio = poissonRatio }

/// <summary>Factory and utility functions for <see cref="PhysicalPropertiesTable"/> records.</summary>
module PhysicalPropertiesTable =
    /// <summary>Creates a <see cref="PhysicalPropertiesTable"/> from individual table arguments.</summary>
    /// <param name="thermalExpReferenceTemperature">Reference temperature for average thermal expansion α(T). Defaults to 20 °C when <c>None</c>.</param>
    /// <param name="thermalExp">Table of thermal expansion coefficient α(T) data points.</param>
    /// <param name="elasticMod">Table of Young's modulus E(T) data points.</param>
    /// <param name="specificHeatOpt">Optional table of specific heat Cp(T) data points.</param>
    /// <param name="densityTable">Table of mass density ρ(T) data points.</param>
    /// <param name="thermalCondOpt">Optional table of thermal conductivity κ(T) as (T, κ) pairs.</param>
    /// <param name="thermalDiffOpt">Optional table of thermal diffusivity a(T) as (T, a) pairs, in m^2/s.</param>
    /// <returns>A fully populated <see cref="PhysicalPropertiesTable"/> record.</returns>
    let create
        (thermalExpReferenceTemperature: float option)
        (thermalExp: ThermalExpansionTablePoint list)
        (elasticMod: ElasticModulusTablePoint list)
        (specificHeatOpt: SpecificHeatTablePoint list option)
        (densityTable: DensityTablePoint list)
        (thermalCondOpt: (float * float) list option)
        (thermalDiffOpt: (float * float) list option)
        =
        let referenceTemperature = defaultArg thermalExpReferenceTemperature 20.0

        { ThermalExpansionReferenceTemperature = referenceTemperature
          ThermalExpansionTable = thermalExp
          ElasticModulusTable = elasticMod
          SpecificHeatTable = specificHeatOpt
          DensityTable = densityTable
          ThermalConductivityTable = thermalCondOpt
          ThermalDiffusivityTable = thermalDiffOpt }

    /// <summary>Returns the minimum and maximum temperatures covered by the thermal expansion table.</summary>
    /// <param name="table">The physical properties table to query.</param>
    /// <returns><c>Some (T_min, T_max)</c>, or <c>None</c> if the table is empty.</returns>
    let getTempRangeExpansion (table: PhysicalPropertiesTable) : (float * float) option =
        if List.isEmpty table.ThermalExpansionTable then
            None
        else
            let sorted =
                table.ThermalExpansionTable |> List.sortBy (fun p -> float p.Temperature)

            Some((List.head sorted).Temperature, (List.last sorted).Temperature)

    /// <summary>Returns the minimum and maximum temperatures covered by the elastic modulus table.</summary>
    /// <param name="table">The physical properties table to query.</param>
    /// <returns><c>Some (T_min, T_max)</c>, or <c>None</c> if the table is empty.</returns>
    let getTempRangeElastic (table: PhysicalPropertiesTable) : (float * float) option =
        if List.isEmpty table.ElasticModulusTable then
            None
        else
            let sorted = table.ElasticModulusTable |> List.sortBy (fun p -> float p.Temperature)
            Some((List.head sorted).Temperature, (List.last sorted).Temperature)
