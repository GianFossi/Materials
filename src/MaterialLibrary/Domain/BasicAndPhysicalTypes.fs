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
/// Temperature-independent material properties, measured at room temperature.
/// These are the guaranteed minimum values per ASME Section II Part D, Tables Y-1 and U.
/// </summary>
type BasicProperties =
    {
        /// Minimum elongation at fracture (%), from the standard tensile coupon test.
        /// Use the governing direction per source data (longitudinal or transverse).
        ElongationPercent: float
        /// Minimum reduction of cross-sectional area at fracture (%).
        ReductionOfAreaPercent: float
        /// Specified Minimum Yield Strength — SMYS (MPa). See ASME Section II Part D, Table Y-1.
        SpecifiedMinimumYieldStrength: float
        /// Specified Minimum Ultimate Tensile Strength — SMUTS (MPa). See ASME Section II Part D, Table U.
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
    }

// ========== HELPER MODULES ==========

/// <summary>Factory functions for constructing and querying <see cref="BasicProperties"/> records.</summary>
module BasicProperties =
    /// <summary>Creates a <see cref="BasicProperties"/> record from individual arguments.</summary>
    /// <param name="elongation">Minimum elongation at fracture (%) in the governing direction (longitudinal or transverse).</param>
    /// <param name="ductility">Minimum reduction of area at fracture (%).</param>
    /// <param name="smys">Specified Minimum Yield Strength (MPa).</param>
    /// <param name="smuts">Specified Minimum Ultimate Tensile Strength (MPa).</param>
    /// <returns>A fully populated <see cref="BasicProperties"/> record.</returns>
    let create (elongation: float) (ductility: float) (smys: float) (smuts: float) =
        { ElongationPercent = elongation
          ReductionOfAreaPercent = ductility
          SpecifiedMinimumYieldStrength = smys
          SpecifiedMinimumUltimateStrength = smuts }

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
    /// <returns>A fully populated <see cref="PhysicalPropertiesTable"/> record.</returns>
    let create
        (thermalExpReferenceTemperature: float option)
        (thermalExp: ThermalExpansionTablePoint list)
        (elasticMod: ElasticModulusTablePoint list)
        (specificHeatOpt: SpecificHeatTablePoint list option)
        (densityTable: DensityTablePoint list)
        (thermalCondOpt: (float * float) list option)
        =
        let referenceTemperature = defaultArg thermalExpReferenceTemperature 20.0

        { ThermalExpansionReferenceTemperature = referenceTemperature
          ThermalExpansionTable = thermalExp
          ElasticModulusTable = elasticMod
          SpecificHeatTable = specificHeatOpt
          DensityTable = densityTable
          ThermalConductivityTable = thermalCondOpt }

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
