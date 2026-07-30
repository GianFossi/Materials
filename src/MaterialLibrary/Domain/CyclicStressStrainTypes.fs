namespace MaterialLibrary.Domain

// ========== CYCLIC STRESS-STRAIN CURVES ==========

/// <summary>
/// A single data point on a cyclic stress-strain curve: strain amplitude vs stress amplitude.
/// </summary>
type CyclicStressStrainPoint =
    {
        /// Stress amplitude σ_a (MPa).
        StressAmplitude: float
        /// Cyclic strain amplitude ε_ta (dimensionless).
        StrainAmplitude: float
    }

/// <summary>
/// A point on the stress-range versus strain-range relation, used by
/// <see cref="CyclicStrainTable.HysteresisRangeTable"/>. This relation is a genuine monotonic
/// function of stress range (unlike a hysteresis loop plotted as stress vs. strain, which is
/// bi-valued and therefore not representable as a single ascending-X <see cref="PropertyTable"/>);
/// this project deliberately stores only this monotonic range-vs-range table, not raw loop
/// coordinates.
/// </summary>
type HysteresisRangePoint =
    {
        /// Stress range σ_r (MPa).
        StressRange: float
        /// Strain range ε_tr (dimensionless).
        StrainRange: float
    }

