namespace MaterialLibrary.Domain

// ========== FATIGUE CURVES ==========

/// <summary>A single data point on a fatigue (S-N) curve.</summary>
type FatigueCurvePoint =
    {
        /// Number of cycles to failure N.
        Cycles: float
        /// Stress range Δσ (MPa), or stress amplitude depending on convention.
        StressRange: float
    }

/// <summary>A complete fatigue (S-N) curve at a given temperature and stress ratio.</summary>
/// <remarks>
/// S-N curves are typically presented on a log-log scale. The applicable design curves
/// for pressure vessels are given in ASME Section III, NB-3222.4.
/// </remarks>
type FatigueCurve =
    {
        /// Test temperature (°C).
        Temperature: float
        /// Stress basis used for fatigue stress values: Engineering or True.
        StressBasis: StressStrainBasis
        /// Stress ratio R = σ_min / σ_max (dimensionless). R = -1 for fully reversed loading.
        RValue: float
        /// Human-readable label.
        Description: string
        /// Ordered list of (N, Δσ) data points.
        Points: FatigueCurvePoint list
    }
