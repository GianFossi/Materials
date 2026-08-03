namespace MaterialLibrary.Domain

/// <summary>A point used to create an external-pressure material table.</summary>
/// <remarks>
/// <c>CompressiveStress</c> is the ASME external-pressure chart's Factor B: a material-chart value,
/// dimensioned as a stress (MPa), read at a given Factor A and used directly in the UG-28 formulas.
/// </remarks>
type ExternalPressureTablePoint =
    {
        /// External pressure chart factor A (dimensionless).
        FactorA: float
        /// Factor B / allowable compressive stress Sc (MPa).
        CompressiveStress: float
        /// Tangent modulus Et (MPa) used to compute A = Sc / Et.
        TangentModulus: float
    }

/// <summary>Compressive properties at a specific temperature.</summary>
type CompressionProperties =
    {
        /// Test temperature (°C).
        Temperature: float
        /// Ultimate compressive strength (MPa).
        CompressiveStrength: float
        /// 0.2 % compressive yield strength (MPa).
        CompressiveYield: float
    }
