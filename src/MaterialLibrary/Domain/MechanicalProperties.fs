namespace MaterialLibrary.Domain

/// <summary>Tensile properties measured at a specific temperature.</summary>
/// <remarks>Values are typically taken from ASME Section II Part D, Table Y-1 or from material certifications.</remarks>
type TensileProperties =
    {
        /// Test temperature (°C).
        Temperature: float
        /// 0.2 % proof (yield) strength (MPa).
        YieldStrength: float
        /// Ultimate tensile strength (MPa).
        TensileStrength: float
        /// Minimum elongation at fracture (%).
        ElongationPercent: float
        /// Minimum reduction of area at fracture (%).
        ReductionOfAreaPercent: float
    }

/// <summary>A point used to create an external-pressure material table.</summary>
type ExternalPressureTablePoint =
    {
        /// External pressure chart factor A (dimensionless).
        FactorA: float
        /// Allowable compressive stress Sc (MPa).
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

/// <summary>ASME allowable stress values at a specific temperature.</summary>
/// <remarks>
/// Service levels A through D correspond to increasing severity of loading conditions,
/// as defined in ASME Section III Subsection NB and NE.
/// </remarks>
type AllowableStress =
    {
        /// Temperature at which the allowable stress applies (°C).
        Temperature: float
        /// Allowable stress for Service Level A (normal operating conditions), MPa.
        Section_I_ServiceLevel_A: float option
        /// Allowable stress for Service Level B (upset conditions), MPa.
        Section_I_ServiceLevel_B: float option
        /// Allowable stress for Service Level C (emergency conditions), MPa.
        Section_I_ServiceLevel_C: float option
        /// Allowable stress for Service Level D (faulted conditions), MPa.
        Section_I_ServiceLevel_D: float option
        /// Allowable stress for weld joints per ASME Section II, MPa.
        Section_II_Weld: float option
    }
