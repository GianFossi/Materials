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
/// A point on the stress-range versus strain-range relation used to construct hysteresis loops.
/// </summary>
type HysteresisRangePoint =
    {
        /// Stress range σ_r (MPa).
        StressRange: float
        /// Strain range ε_tr (dimensionless).
        StrainRange: float
    }

/// Identifies the direction of travel around a hysteresis loop.
type HysteresisBranch =
    | Loading
    | Unloading

/// One ordered stress-strain coordinate on a hysteresis loop.
type HysteresisLoopPoint =
    {
        /// Signed strain coordinate (dimensionless).
        Strain: float
        /// Signed stress coordinate (MPa).
        Stress: float
        /// Loading or unloading branch containing this point.
        Branch: HysteresisBranch
    }

/// One closed, fully reversed hysteresis loop identified by its amplitudes.
type HysteresisLoop =
    {
        StressAmplitude: float
        StrainAmplitude: float
        /// Ordered loading branch followed by ordered unloading branch.
        Points: HysteresisLoopPoint list
    }

