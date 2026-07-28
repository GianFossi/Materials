namespace MaterialLibrary.Domain

/// <summary>Material-family classification used by Code Case 2964 Appendix III factor rules.</summary>
type CodeCase2964MaterialFamily =
    | FerrousSteel
    | StainlessSteelOrNickelBasedAlloy
    | DuplexStainlessSteel

/// <summary>Calibration strategy used when generating Code Case 2964 charts from stored Appendix III inputs.</summary>
type CodeCase2964CalibrationMode =
    /// Generate directly from stored Appendix III inputs with no reference-based calibration.
    | Off
    /// Apply scale-only log-domain mapping when a matching stored reference chart is available.
    | ScaleOnlyLog
    /// Apply scale-only log-domain mapping, but keep the raw generated chart if calibration does not improve MAPE.
    | ScaleOnlyLogWithFallback

/// <summary>Appendix III constants A_i and B_i for one material at one assessment temperature.</summary>
type CodeCase2964AppendixIIIConstants =
    {
        /// Assessment temperature at which the constants apply (degC).
        Temperature: float
        /// Constant A0 (dimensionless in the published fitted equation form).
        A0: float
        /// Constant A1.
        A1: float
        /// Constant A2.
        A2: float
        /// Constant A3.
        A3: float
        /// Constant A4.
        A4: float
        /// Constant B0.
        B0: float
        /// Constant B1.
        B1: float
        /// Constant B2.
        B2: float
        /// Constant B3.
        B3: float
        /// Constant B4.
        B4: float
        /// Optional notes/limitations copied from the source table.
        Notes: string option
    }

/// <summary>Appendix III factor rule for m2 and ε′p, keyed by material family.</summary>
type CodeCase2964AppendixIIIFactorRule =
    {
        /// Material family classification used by the rule.
        MaterialFamily: CodeCase2964MaterialFamily
        /// Upper temperature limit of the rule (degF) from the published table.
        TemperatureLimitF: float
        /// Coefficient c in m2 = c * (1 - R).
        M2Coefficient: float
        /// Factor ε′p from the published table.
        EpsPrimeP: float
        /// Optional notes/limitations copied from the source table.
        Notes: string option
    }

/// <summary>Evaluated Code Case 2964 factor values derived from stored material inputs at one assessment temperature.</summary>
type CodeCase2964EvaluatedFactorValues =
    {
        /// Assessment temperature (degC).
        Temperature: float
        /// Assessment temperature (degF).
        TemperatureF: float
        /// Strength ratio R = σ_y / σ_ult used in the rule.
        StrengthRatioR: float
        /// Evaluated factor m2.
        M2: float
        /// Evaluated factor ε′p.
        EpsPrimeP: float
        /// Material family used by the rule.
        MaterialFamily: CodeCase2964MaterialFamily
        /// Description of where the strength ratio R came from.
        StrengthRatioSource: string
    }

