namespace MaterialLibrary.Domain

// ========== STRESS-STRAIN CURVE ==========

/// <summary>
/// Defines whether stress/strain values are Engineering (nominal) or True values.
/// </summary>
type StressStrainBasis =
    | Engineering
    | True

/// <summary>Origin or generation method for a stress-strain table.</summary>
type StressStrainTableSource =
    | StressStrainDatabase
    | GeneratedAsmeVIII2Annex3D
    | GeneratedApi579Annex10B5

/// <summary>A single (strain, stress) data point on a stress-strain curve.</summary>
type StressStrainPoint =
    {
        /// Engineering strain (% elongation).
        Strain: float
        /// Engineering stress (MPa).
        Stress: float
    }

