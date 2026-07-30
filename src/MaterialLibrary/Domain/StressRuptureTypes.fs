namespace MaterialLibrary.Domain

// ========== STRESS RUPTURE ==========
//
// Stress-rupture curves themselves (time to rupture vs. stress at constant temperature) are stored
// as StressRuptureTable (see Tables/StressRuptureTable.fs), a PropertyTable wrapper consistent with
// every other curve type. This file only defines the Larson-Miller correlation, which is not
// PropertyTable-backed today.

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
