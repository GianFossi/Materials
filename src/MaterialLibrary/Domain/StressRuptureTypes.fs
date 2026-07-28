namespace MaterialLibrary.Domain

// ========== STRESS RUPTURE ==========

/// <summary>A single data point on a stress-rupture curve.</summary>
type StressRupturePoint =
    {
        /// Time to rupture under the given stress (hours).
        TimeToRupture: float
        /// Stress that causes rupture at this time (MPa).
        StressAtRupture: float
    }

/// <summary>A complete stress-rupture curve at constant temperature (σ_rupture vs t_rupture).</summary>
/// <remarks>
/// Used to determine the maximum allowable stress for a given design life and temperature,
/// per ASME Section II Part D, Table 1A/1B footnotes and ASME Section III NH.
/// </remarks>
type StressRuptureCurve =
    {
        /// Temperature at which the curve applies (°C).
        Temperature: float
        /// Human-readable label.
        Description: string
        /// Ordered list of (t_rupture, σ) data points.
        Points: StressRupturePoint list
    }

/// <summary>A single data point on a Larson–Miller rupture correlation.</summary>
/// <remarks>
/// The Larson–Miller parameter P = T · (C + log₁₀ t_r), where T is absolute temperature (K or °R),
/// t_r is time to rupture (hours), and C is a material constant (≈7–12 for steels).
/// Reference: Larson, F.R. &amp; Miller, J. (1952) Trans. ASME 74, 765.
/// </remarks>
type LarsonMillerPoint =
    {
        /// Larson–Miller parameter P (dimensionless).
        LarsonMillerParameter: float
        /// Corresponding rupture stress (MPa).
        Stress: float
    }

/// <summary>A Larson–Miller master rupture curve for a specific material.</summary>
type LarsonMillerCurve =
    {
        /// Material identifier or name.
        Material: string
        /// Human-readable description of the curve source.
        Description: string
        /// Ordered list of (P, σ) data points.
        Points: LarsonMillerPoint list
    }
