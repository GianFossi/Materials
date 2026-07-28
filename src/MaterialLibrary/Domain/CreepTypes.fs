namespace MaterialLibrary.Domain

// ========== CREEP MODELS ==========

/// <summary>Origin or selected generation model for a creep table.</summary>
type CreepTableSource =
    | CreepDatabase
    | GeneratedNortonPowerLaw
    | GeneratedGarofalo
    | GeneratedKachanovOmega

/// <summary>Applicability information for the supported creep-table generation models.</summary>
module CreepModelApplicability =
    /// <summary>Returns the mandatory engineering limitation warning for a source/model.</summary>
    let warning source =
        match source with
        | CreepDatabase ->
            "Database data: phase coverage is determined by the source dataset and must be verified by the user."
        | GeneratedNortonPowerLaw ->
            "Norton is an empirical power-time law. It does not model a complete primary, secondary, and tertiary creep curve. The user must verify the calibrated regime."
        | GeneratedGarofalo ->
            "Garofalo is an empirical stress law with power-time evolution. It does not model a complete primary, secondary, and tertiary creep curve. The user must verify the calibrated regime."
        | GeneratedKachanovOmega ->
            "Kachanov-Omega neglects primary creep in this implementation and represents secondary creep followed by damage-driven tertiary creep. The user must verify applicability."

/// Creep Norton Power Law Model: ε_c = A * σ^n * t^m
type NortonPowerLawCoefficients =
    { Temperature: float
      A: float // Coefficient
      N: float // Stress exponent
      M: float } // Time exponent (typically 0.2-0.4 for primary, 1.0 for secondary)

/// Creep Garofalo Model (Hyperbolic Sine): ε_c = A * [sinh(α*σ)]^n * t^m * exp(-Q/RT)
type GarofaloCoefficients =
    { Temperature: float
      A: float // Coefficient
      N: float // Stress exponent
      M: float // Time exponent
      Alpha: float // Material constant (1/MPa)
      Q: float } // Activation energy (J/mol)

/// Creep Kachanov-Rabotnov model:
/// dε/dt = A1*σ^N1/(1-ω)^M1, dω/dt = A2*σ^N2/(1-ω)^M2
/// ω is a damage variable ranging from 0 (no damage) to 1 (rupture)
type KachanovOmegaModel =
    {
        Temperature: float

        /// Creep rate function: ε̇ = A1 * σ^N1 / (1 - ω)^M1
        A1: float // Coefficient
        N1: float // Stress exponent
        M1: float // Omega exponent (creep rate)

        /// Damage evolution: ω̇ = A2 * σ^N2 / (1 - ω)^M2
        A2: float // Damage coefficient
        N2: float // Stress exponent
        M2: float // Omega exponent (damage evolution)

        Description: string
    }

/// Convergence-controlled Kachanov integration history.
type KachanovIntegrationHistory =
    {
        /// Uniform time increment used by the accepted solution (hours).
        TimeStep: float
        /// Values from time zero through total time, inclusive.
        Values: float list
        /// First time at which damage reached one, when rupture occurred.
        RuptureTime: float option
    }

/// <summary>A single measured data point on a creep curve (ε vs t at constant σ and T).</summary>
type CreepPoint =
    {
        /// Elapsed time (hours).
        Time: float
        /// Measured creep strain at this time (%).
        Strain: float
    }

