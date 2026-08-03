namespace MaterialLibrary.Domain

/// <summary>Minimum strengths at one temperature.</summary>
/// <remarks>
/// <para>
/// Values are typically taken from ASME Section II Part D, Tables Y-1 (Sy) and U (Su), or from
/// material certifications. Units: temperature in degC, strengths in MPa.
/// </para>
/// <para>
/// Elongation and reduction of area are deliberately absent: they come from the room-temperature
/// tensile coupon test and are single scalars, so they belong to
/// <see cref="BasicProperties"/> rather than to a per-temperature row.
/// </para>
/// <para>
/// This record holds the governing curve, with no size dependence. When Sy and Su vary with
/// section size or thickness, each group's curve is preserved separately in
/// <see cref="StrengthProperties.TensileStrengthDatasets"/>.
/// </para>
/// </remarks>
type TensileProperties =
    {
        /// Test temperature (°C).
        Temperature: float
        /// 0.2 % proof (yield) strength Sy (MPa).
        YieldStrength: float
        /// Ultimate tensile strength Su (MPa).
        TensileStrength: float
    }

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
