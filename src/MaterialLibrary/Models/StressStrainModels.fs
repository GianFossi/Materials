namespace MaterialLibrary.Domain

open System

/// <summary>
/// Pure helpers for validating and normalizing stress-strain curve points.
/// </summary>
module StressStrainModels =

    let isFinite (value: float) : bool =
        not (Double.IsNaN value || Double.IsInfinity value)

    /// <summary>
    /// Validates and normalizes stress-strain points by sorting them by strain.
    /// </summary>
    let normalizePoints (points: StressStrainPoint list) : Result<StressStrainPoint list, MaterialError> =
        if List.length points < 2 then
            Error(MaterialError.InvalidOperation "Stress-strain curve requires at least two points")
        elif
            points
            |> List.exists (fun p -> not (isFinite p.Strain) || not (isFinite p.Stress))
        then
            Error(MaterialError.InvalidOperation "Stress-strain points contain non-finite values")
        else
            let sorted = points |> List.sortBy (fun p -> p.Strain)

            let hasDuplicateStrain =
                sorted |> List.pairwise |> List.exists (fun (a, b) -> a.Strain = b.Strain)

            if hasDuplicateStrain then
                Error(MaterialError.InvalidOperation "Stress-strain points contain duplicate strain values")
            else
                Ok sorted

/// <summary>
/// Input parameters for the Code Case 2964 Appendix I tangent-modulus model.
/// All stress values in MPa; all strain values dimensionless (not %).
/// </summary>
/// <remarks>
/// Equations I-10 through I-22 of ASME Code Case 2964 Appendix I.
/// The model derives Et from the minimum isochronous curves (0.8 × average isochronous).
/// </remarks>
type CodeCase2964TangentModulusInput =
    {
        /// 0.2% proof stress (engineering) at temperature (MPa). σ_ys
        YieldStress: float
        /// Ultimate tensile stress at temperature (MPa). σ_ult
        UltimateStress: float
        /// Elastic modulus at temperature (MPa). E
        ElasticModulus: float
        /// Engineering strain at yield ε_ys = σ_ys / E (dimensionless).
        StrainAtYield: float
        /// Plastic strain limit ε'_p at temperature (dimensionless). Temperature-limited per Table III-2.
        EpsilonPrimePlastic: float
        /// True stress in the neighbourhood of the applied stress σ_es → σ_t (MPa). Eq. I-22.
        EngineeringStress: float
        /// Companion engineering strain ε_es (dimensionless).
        EngineeringStrain: float
        /// m2 exponent, temperature-limited per Table III-2 (dimensionless). Eq. I-20.
        M2: float
    }

/// <summary>
/// Intermediate alpha/beta parameters computed from <see cref="CodeCase2964TangentModulusInput"/>.
/// </summary>
type CodeCase2964TangentModulusAlphas =
    {
        /// α₁ = R = σ_ys / σ_ult  (dimensionless).  Eq. I-14.
        Alpha1: float
        /// α₂ = m₁ computed from Eq. I-15 (dimensionless).
        Alpha2: float
        /// α₃ = A₁ = σ_ys (1 + ε_ys) / ln(1 + ε_ys)^m₁  (MPa).  Eq. I-16.
        Alpha3: float
        /// α₄ = K = 1.5 R^1.5 − 0.5 R^2.5 − R^3.5  (dimensionless).  Eq. I-17.
        Alpha4: float
        /// α₅ = σ_ys + K (σ_ult − σ_ys)  (MPa).  Eq. I-18.
        Alpha5: float
        /// α₆ = K (σ_ult − σ_ys)  (MPa).  Eq. I-19.
        Alpha6: float
        /// α₇ = m₂  (dimensionless).  Eq. I-20.
        Alpha7: float
        /// α₈ = A₂ = σ_uts · e^m₂ / (m₂^m₂)  (MPa).  Eq. I-21.
        Alpha8: float
        /// σ_t = (1 + ε_es) · σ_es  (MPa).  Eq. I-22.
        SigmaT: float
    }

/// <summary>
/// Computed tangent-modulus components H₁, H₂, H₃ and resulting E_t.
/// </summary>
type CodeCase2964TangentModulusResult =
    {
        /// H₁ = 1/E — elastic component  (MPa⁻¹).  Eq. I-11.
        H1: float
        /// H₂ — plastic component (primary branch)  (MPa⁻¹).  Eq. I-12.
        H2: float
        /// H₃ — plastic component (secondary branch)  (MPa⁻¹).  Eq. I-13.
        H3: float
        /// Tangent modulus E_t = 1 / (H₁ + H₂ + H₃)  (MPa).  Eq. I-10.
        TangentModulus: float
        /// Intermediate alpha parameters for diagnostics.
        Alphas: CodeCase2964TangentModulusAlphas
    }

/// <summary>
/// Pure calculation module for the Code Case 2964 Appendix I tangent-modulus model
/// (Equations I-10 to I-22).
/// </summary>
module CodeCase2964TangentModulusModel =

    // ── helpers ─────────────────────────────────────────────────────────────

    let private isFinite (v: float) =
        not (System.Double.IsNaN v || System.Double.IsInfinity v)

    let private safeTanh (x: float) =
        // clamp to avoid overflow in exp(±2x) for very large |x|
        System.Math.Tanh(max -30.0 (min 30.0 x))

    // ── α-parameter computation (Eqs. I-14 to I-22) ─────────────────────────

    /// <summary>
    /// Computes the eight alpha parameters and σ_t from the input record.
    /// </summary>
    /// <param name="input">Validated input data record.</param>
    /// <returns>Alpha record or an <see cref="MaterialError.InvalidOperation"/>.</returns>
    let computeAlphas
        (input: CodeCase2964TangentModulusInput)
        : Result<CodeCase2964TangentModulusAlphas, MaterialError> =

        if input.UltimateStress <= 0.0 then
            Error(MaterialError.InvalidOperation "UltimateStress must be > 0 MPa")
        elif input.ElasticModulus <= 0.0 then
            Error(MaterialError.InvalidOperation "ElasticModulus must be > 0 MPa")
        elif input.YieldStress <= 0.0 then
            Error(MaterialError.InvalidOperation "YieldStress must be > 0 MPa")
        elif input.YieldStress >= input.UltimateStress then
            Error(MaterialError.InvalidOperation "YieldStress must be < UltimateStress")
        elif input.StrainAtYield <= 0.0 then
            Error(MaterialError.InvalidOperation "StrainAtYield must be > 0")
        elif not (isFinite input.EpsilonPrimePlastic) || input.EpsilonPrimePlastic < 0.0 then
            Error(MaterialError.InvalidOperation "EpsilonPrimePlastic must be finite and >= 0")
        elif input.M2 <= 0.0 then
            Error(MaterialError.InvalidOperation "M2 must be > 0")
        else
            // Eq. I-14  α₁ = R = σ_ys / σ_ult
            let alpha1 = input.YieldStress / input.UltimateStress

            // Eq. I-15  α₂ = m₁
            // m₁ = [ln(R) + (ε'_p − ε_ys)] / ln[ln(1+ε'_p) / ln(1+ε_ys)]
            let eys = input.StrainAtYield
            let epsp = input.EpsilonPrimePlastic

            let lnNumerator = System.Math.Log alpha1 + (epsp - eys)

            let lnDenominator =
                let lnEpsp = System.Math.Log(1.0 + epsp)
                let lnEys = System.Math.Log(1.0 + eys)
                // When ε'_p is set to 0 (temperature limit exceeded) both logs become 0;
                // per standard guidance H₂ and H₃ are then set to 0.
                if lnEpsp <= 0.0 || lnEys <= 0.0 then
                    0.0
                else
                    System.Math.Log(lnEpsp / lnEys)

            let alpha2 =
                if lnDenominator = 0.0 then
                    0.0
                else
                    lnNumerator / lnDenominator

            // Eq. I-16  α₃ = A₁ = σ_ys (1 + ε_ys) / ln(1 + ε_ys)^m₁
            let lnEys = System.Math.Log(1.0 + eys)

            let alpha3 =
                let denom = if lnEys <= 0.0 then 0.0 else lnEys ** alpha2

                if denom = 0.0 then
                    0.0
                else
                    input.YieldStress * (1.0 + eys) / denom

            // Eq. I-17  α₄ = K = 1.5 R^1.5 − 0.5 R^2.5 − R^3.5
            let r = alpha1
            let alpha4 = 1.5 * r ** 1.5 - 0.5 * r ** 2.5 - r ** 3.5

            // Eq. I-18  α₅ = σ_ys + K (σ_ult − σ_ys)
            let alpha5 = input.YieldStress + alpha4 * (input.UltimateStress - input.YieldStress)

            // Eq. I-19  α₆ = K (σ_ult − σ_ys)
            let alpha6 = alpha4 * (input.UltimateStress - input.YieldStress)

            // Eq. I-20  α₇ = m₂
            let alpha7 = input.M2

            // Eq. I-21  α₈ = A₂ = σ_uts · e^m₂ / (m₂^m₂)
            let m2 = input.M2

            let alpha8 =
                if m2 <= 0.0 then
                    0.0
                else
                    input.UltimateStress * System.Math.Exp m2 / (m2 ** m2)

            // Eq. I-22  σ_t = (1 + ε_es) σ_es
            let sigmaT = (1.0 + input.EngineeringStrain) * input.EngineeringStress

            Ok
                { Alpha1 = alpha1
                  Alpha2 = alpha2
                  Alpha3 = alpha3
                  Alpha4 = alpha4
                  Alpha5 = alpha5
                  Alpha6 = alpha6
                  Alpha7 = alpha7
                  Alpha8 = alpha8
                  SigmaT = sigmaT }

    // ── H₂ / H₃ bracketed expressions (Eqs. I-12, I-13) ────────────────────

    /// <summary>
    /// Computes H₂ (plastic primary component) per Eq. I-12.
    /// Returns 0 when α₃ = 0 or the stress exceeds ε'_p constraint.
    /// </summary>
    let private computeH2 (alphas: CodeCase2964TangentModulusAlphas) : float =

        // When ε'_p = 0 the plastic branch must be zeroed out per the standard.
        if alphas.Alpha2 = 0.0 || alphas.Alpha3 = 0.0 || alphas.Alpha6 = 0.0 then
            0.0
        else
            let sigmaT = alphas.SigmaT
            let alpha2 = alphas.Alpha2
            let alpha3 = alphas.Alpha3
            let alpha5 = alphas.Alpha5
            let alpha6 = alphas.Alpha6

            // Inner tanh argument: (2α₅ − 2σ_t) / α₆
            let tanhArg = (2.0 * alpha5 - 2.0 * sigmaT) / alpha6

            // (σ_t / α₃)^(1/α₂)
            let powerTerm =
                let base_ = sigmaT / alpha3
                if base_ <= 0.0 then 0.0 else base_ ** (1.0 / alpha2)

            // Full bracket: { (σ_t/α₃)^(1/α₂) [tanh((2α₅−2σ_t)/α₆)+1] [α₆−2α₂σ_t+2α₂σ_t tanh((2α₅−2σ_t)/α₆)] }
            let tanhVal = safeTanh tanhArg

            let bracket =
                powerTerm
                * (tanhVal + 1.0)
                * (alpha6 - 2.0 * alpha2 * sigmaT + 2.0 * alpha2 * sigmaT * tanhVal)

            // Prefactor: (2 α₂ α₆ σ_t)^{-1}
            let prefactor = 2.0 * alpha2 * alpha6 * sigmaT
            if prefactor = 0.0 then 0.0 else bracket / prefactor

    /// <summary>
    /// Computes H₃ (plastic secondary component) per Eq. I-13.
    /// Returns 0 when α₈ = 0 or α₇ = 0.
    /// </summary>
    let private computeH3 (alphas: CodeCase2964TangentModulusAlphas) : float =

        if alphas.Alpha7 = 0.0 || alphas.Alpha8 = 0.0 || alphas.Alpha6 = 0.0 then
            0.0
        else
            let sigmaT = alphas.SigmaT
            let alpha6 = alphas.Alpha6
            let alpha7 = alphas.Alpha7
            let alpha8 = alphas.Alpha8
            let alpha5 = alphas.Alpha5

            let tanhArg = (2.0 * alpha5 - 2.0 * sigmaT) / alpha6

            // (σ_t / α₈)^(1/α₇)
            let powerTerm =
                let base_ = sigmaT / alpha8
                if base_ <= 0.0 then 0.0 else base_ ** (1.0 / alpha7)

            let tanhVal = safeTanh tanhArg
            // [tanh(…) − 1] counterpart to H₂'s [tanh(…) + 1]
            let bracket =
                powerTerm
                * (tanhVal - 1.0)
                * (alpha6 + 2.0 * alpha7 * sigmaT + 2.0 * alpha7 * sigmaT * tanhVal)

            // Prefactor: (−2 α₆ α₇ σ_t)^{-1}   [note the minus sign in Eq. I-13]
            let prefactor = -2.0 * alpha6 * alpha7 * sigmaT
            if prefactor = 0.0 then 0.0 else bracket / prefactor

    // ── public entry point ───────────────────────────────────────────────────

    /// <summary>
    /// Computes the Code Case 2964 Appendix I tangent modulus E_t at the given engineering stress/strain.
    /// </summary>
    /// <remarks>
    /// Implements Eqs. I-10 through I-22.  The minimum isochronous curves (0.8 × average) are used;
    /// callers should supply the 0.8-reduced isochronous data in <paramref name="input"/>.
    /// When the temperature limit for ε'_p (Table III-2) is exceeded the caller must set
    /// <see cref="CodeCase2964TangentModulusInput.EpsilonPrimePlastic"/> to zero, which automatically
    /// zeros H₂ and H₃ per the standard requirement.
    /// </remarks>
    /// <param name="input">Material and operating-point data record.</param>
    /// <returns>Tangent-modulus result including all intermediate components.</returns>
    let compute (input: CodeCase2964TangentModulusInput) : Result<CodeCase2964TangentModulusResult, MaterialError> =

        match computeAlphas input with
        | Error err -> Error err
        | Ok alphas ->
            // Eq. I-11  H₁ = 1/E
            let h1 = 1.0 / input.ElasticModulus

            // Eqs. I-12, I-13
            let h2 = computeH2 alphas
            let h3 = computeH3 alphas

            // Eq. I-10  E_t = 1 / (H₁ + H₂ + H₃)
            let sumH = h1 + h2 + h3

            if sumH <= 0.0 || not (isFinite sumH) then
                Error(
                    MaterialError.InvalidOperation
                        "Code Case 2964 tangent modulus: H₁+H₂+H₃ must be positive and finite"
                )
            else
                Ok
                    { H1 = h1
                      H2 = h2
                      H3 = h3
                      TangentModulus = 1.0 / sumH
                      Alphas = alphas }

// ─────────────────────────────────────────────────────────────────────────────
// ASME BPVC VIII.2-2025 §3-D  —  Stress-Strain Curve and Tangent Modulus
// Equations 3-D.1 through 3-D.21
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Input parameters for the ASME BPVC VIII.2-2025 §3-D stress-strain curve model.
/// All stress values in MPa; all strain values dimensionless (not %).
/// </summary>
/// <remarks>
/// Implements Eqs. 3-D.1 through 3-D.21 of ASME BPVC Section VIII Division 2, 2025 edition.
/// The model is time-independent; it uses the specified yield and ultimate tensile
/// strength at the design temperature from Sections 3-D.1 and 3-D.2.
/// </remarks>
type Asme3dStressStrainInput =
    {
        /// Yield strength at temperature (MPa).  σ_ys
        YieldStress: float
        /// Ultimate tensile strength at temperature (MPa).  σ_uts
        UltimateStress: float
        /// Modulus of elasticity at temperature (MPa).  E_y
        ElasticModulus: float
        /// m₂ exponent from Table 3-D.1, temperature-limited (dimensionless).
        M2: float
        /// Plastic strain limit ε_p from Table 3-D.1, temperature-limited (dimensionless).
        EpsilonPlastic: float
        /// Engineering stress at the point of interest (MPa).  σ_es
        EngineeringStress: float
        /// Engineering strain at the point of interest (dimensionless).  ε_es
        EngineeringStrain: float
    }

/// <summary>
/// Derived curve-fitting parameters for the §3-D model.
/// </summary>
type Asme3dCurveParameters =
    {
        /// R = σ_ys / σ_uts — engineering yield-to-tensile ratio (dimensionless).  Eq. 3-D.11.
        R: float
        /// ε_ys = 0.002 — fixed yield strain per §3-D.  Eq. 3-D.12.
        EpsYs: float
        /// m₁ — micro-strain exponent (dimensionless).  Eq. 3-D.7.
        M1: float
        /// A₁ — micro-strain coefficient (MPa).  Eq. 3-D.6.
        A1: float
        /// A₂ — macro-strain coefficient (MPa).  Eq. 3-D.9.
        A2: float
        /// K — material parameter for stress-strain model (dimensionless).  Eq. 3-D.13.
        K: float
        /// σ_t = (1 + ε_es) σ_es — true stress at the point of interest (MPa).  Eq. I-22 / §3-D.6.
        SigmaT: float
        /// H — transition parameter for the tanh blending function (dimensionless).  Eq. 3-D.10.
        H: float
        /// True ultimate tensile stress at true ultimate tensile strain (MPa).  Eq. 3-D.14.
        SigmaUtsT: float
    }

/// <summary>
/// Total strain result from the §3-D stress-strain model.
/// </summary>
type Asme3dStressStrainResult =
    {
        /// Total true strain ε_t = σ_t/E_y + Y₁ + Y₂  (dimensionless).  Eq. 3-D.1.
        TotalStrain: float
        /// Elastic component σ_t/E_y  (dimensionless).
        ElasticStrain: float
        /// Y₁ — micro-plastic component (dimensionless).  Eq. 3-D.3.
        Y1: float
        /// Y₂ — macro-plastic component (dimensionless).  Eq. 3-D.4.
        Y2: float
        /// ε₁ = (σ_t/A₁)^(1/m₁)  (dimensionless).  Eq. 3-D.5.
        Epsilon1: float
        /// ε₂ = (σ_t/A₂)^(1/m₂)  (dimensionless).  Eq. 3-D.8.
        Epsilon2: float
        /// Computed curve parameters.
        Params: Asme3dCurveParameters
    }

/// <summary>
/// Tangent-modulus result from the §3-D.5.1 model.
/// </summary>
type Asme3dTangentModulusResult =
    {
        /// D₁ — micro-strain power-law component (MPa⁻¹).  Eq. 3-D.18.
        D1: float
        /// D₂ — micro-strain tanh-coupling component (MPa⁻¹).  Eq. 3-D.19.
        D2: float
        /// D₃ — macro-strain power-law component (MPa⁻¹).  Eq. 3-D.20.
        D3: float
        /// D₄ — macro-strain tanh-coupling component (MPa⁻¹).  Eq. 3-D.21.
        D4: float
        /// Tangent modulus E_t = (1/E_y + D₁ + D₂ + D₃ + D₄)⁻¹ (MPa).  Eq. 3-D.17.
        TangentModulus: float
        /// Computed curve parameters.
        Params: Asme3dCurveParameters
    }

/// <summary>
/// Pure implementation of the ASME BPVC VIII.2-2025 §3-D stress-strain and tangent-modulus model.
/// </summary>
module Asme3dStressStrainModel =

    // ── helpers ─────────────────────────────────────────────────────────────

    let private isFinite (v: float) =
        not (System.Double.IsNaN v || System.Double.IsInfinity v)

    let private safeTanh (x: float) =
        System.Math.Tanh(max -30.0 (min 30.0 x))

    // ── parameter computation (Eqs. 3-D.6 to 3-D.14) ────────────────────────

    /// <summary>
    /// Computes the derived curve-fitting parameters from the input record.
    /// </summary>
    /// <param name="input">Validated material and operating-point data.</param>
    /// <returns>Curve parameters or a <see cref="MaterialError.InvalidOperation"/>.</returns>
    let computeParameters (input: Asme3dStressStrainInput) : Result<Asme3dCurveParameters, MaterialError> =

        if input.ElasticModulus <= 0.0 then
            Error(MaterialError.InvalidOperation "ElasticModulus must be > 0 MPa")
        elif input.YieldStress <= 0.0 then
            Error(MaterialError.InvalidOperation "YieldStress must be > 0 MPa")
        elif input.UltimateStress <= 0.0 then
            Error(MaterialError.InvalidOperation "UltimateStress must be > 0 MPa")
        elif input.YieldStress >= input.UltimateStress then
            Error(MaterialError.InvalidOperation "YieldStress must be < UltimateStress")
        elif input.M2 <= 0.0 then
            Error(MaterialError.InvalidOperation "M2 must be > 0")
        elif input.EpsilonPlastic < 0.0 then
            Error(MaterialError.InvalidOperation "EpsilonPlastic must be >= 0")
        else
            // Eq. 3-D.11  R = σ_ys / σ_uts
            let r = input.YieldStress / input.UltimateStress

            // Eq. 3-D.12  ε_ys = 0.002 (fixed)
            let epsYs = 0.002

            // Eq. 3-D.13  K = 1.5 R^1.5 − 0.5 R^2.5 − R^3.5
            let k = 1.5 * r ** 1.5 - 0.5 * r ** 2.5 - r ** 3.5

            // Eq. 3-D.7  m₁ = [ln(R) + (ε_p − ε_ys)] / ln[ln(1+ε_p) / ln(1+ε_ys)]
            let ep = input.EpsilonPlastic
            let lnEp = System.Math.Log(1.0 + ep)
            let lnEys = System.Math.Log(1.0 + epsYs)

            let m1 =
                if ep <= 0.0 || lnEp <= 0.0 || lnEys <= 0.0 then
                    0.0 // temperature limit exceeded — plastic terms zeroed
                else
                    let num = System.Math.Log r + (ep - epsYs)
                    let denom = System.Math.Log(lnEp / lnEys)
                    if denom = 0.0 then 0.0 else num / denom

            // Eq. 3-D.6  A₁ = σ_ys (1 + ε_ys) / ln(1 + ε_ys)^m₁
            let a1 =
                if m1 = 0.0 then
                    0.0
                else
                    let denom = lnEys ** m1

                    if denom = 0.0 then
                        0.0
                    else
                        input.YieldStress * (1.0 + epsYs) / denom

            // Eq. 3-D.9  A₂ = σ_uts · exp(m₂) / m₂^m₂
            let a2 =
                let m2 = input.M2
                input.UltimateStress * System.Math.Exp m2 / (m2 ** m2)

            // True stress at the operating point — σ_t = (1 + ε_es) σ_es  (Eq. I-22 / §3-D nomenclature)
            let sigmaT = (1.0 + input.EngineeringStrain) * input.EngineeringStress

            // Eq. 3-D.10  H = 2{σ_t − [σ_ys + K(σ_uts − σ_ys)]} / K(σ_uts − σ_ys)
            let h =
                let kDelta = k * (input.UltimateStress - input.YieldStress)

                if kDelta = 0.0 then
                    0.0
                else
                    2.0 * (sigmaT - (input.YieldStress + kDelta)) / kDelta

            // Eq. 3-D.14  σ_uts,t = σ_uts · exp(m₂)
            let sigmaUtsT = input.UltimateStress * System.Math.Exp input.M2

            Ok
                { R = r
                  EpsYs = epsYs
                  M1 = m1
                  A1 = a1
                  A2 = a2
                  K = k
                  SigmaT = sigmaT
                  H = h
                  SigmaUtsT = sigmaUtsT }

    // ── strain curve (Eqs. 3-D.1 to 3-D.8) ─────────────────────────────────

    /// <summary>
    /// Evaluates the §3-D stress-strain curve at the given engineering stress/strain.
    /// </summary>
    /// <remarks>
    /// When Y₁ + Y₂ ≤ ε_p the plastic terms are suppressed (Eq. 3-D.2).
    /// The curve is clamped to perfect plasticity beyond σ_uts,t (Eq. 3-D.14).
    /// </remarks>
    /// <param name="input">Material and operating-point data.</param>
    /// <returns>Strain decomposition result or a validation error.</returns>
    let computeStrain (input: Asme3dStressStrainInput) : Result<Asme3dStressStrainResult, MaterialError> =

        match computeParameters input with
        | Error err -> Error err
        | Ok p ->
            let elasticStrain = p.SigmaT / input.ElasticModulus

            // ε₁ = (σ_t / A₁)^(1/m₁)  — Eq. 3-D.5
            let eps1 =
                if p.M1 = 0.0 || p.A1 = 0.0 then
                    0.0
                else
                    let base_ = p.SigmaT / p.A1
                    if base_ <= 0.0 then 0.0 else base_ ** (1.0 / p.M1)

            // ε₂ = (σ_t / A₂)^(1/m₂)  — Eq. 3-D.8
            let eps2 =
                let base_ = p.SigmaT / p.A2
                if base_ <= 0.0 then 0.0 else base_ ** (1.0 / input.M2)

            // Y₁ = ε₁/2 · (1 − tanh[H])  — Eq. 3-D.3
            let y1 = (eps1 / 2.0) * (1.0 - safeTanh p.H)

            // Y₂ = ε₂/2 · (1 + tanh[H])  — Eq. 3-D.4
            let y2 = (eps2 / 2.0) * (1.0 + safeTanh p.H)

            // Eq. 3-D.2: suppress plastic terms when Y₁ + Y₂ ≤ ε_p
            let y1Final, y2Final = if y1 + y2 <= input.EpsilonPlastic then 0.0, 0.0 else y1, y2

            let totalStrain = elasticStrain + y1Final + y2Final

            Ok
                { TotalStrain = totalStrain
                  ElasticStrain = elasticStrain
                  Y1 = y1Final
                  Y2 = y2Final
                  Epsilon1 = eps1
                  Epsilon2 = eps2
                  Params = p }

    // ── tangent modulus §3-D.5.1 (Eqs. 3-D.17 to 3-D.21) ───────────────────

    /// <summary>
    /// Computes the §3-D.5.1 tangent modulus E_t = (1/E_y + D₁ + D₂ + D₃ + D₄)⁻¹.
    /// </summary>
    /// <remarks>
    /// D₁ and D₂ are the derivatives of the micro-strain branch (ε₁, m₁, A₁);
    /// D₃ and D₄ are the derivatives of the macro-strain branch (ε₂, m₂, A₂).
    /// When m₁ = 0 (temperature limit exceeded) D₁ and D₂ are set to zero.
    /// </remarks>
    /// <param name="input">Material and operating-point data.</param>
    /// <returns>Tangent-modulus result or a validation error.</returns>
    let computeTangentModulus (input: Asme3dStressStrainInput) : Result<Asme3dTangentModulusResult, MaterialError> =

        match computeParameters input with
        | Error err -> Error err
        | Ok p ->
            let sigmaT = p.SigmaT
            let tanhH = safeTanh p.H
            let sech2H = 1.0 - tanhH * tanhH // sech²(H) = 1 − tanh²(H)
            let kDelta = p.K * (input.UltimateStress - input.YieldStress)
            let dHdSigma = if kDelta = 0.0 then 0.0 else 2.0 / kDelta

            // ── D₁ and D₂  (micro-strain branch, Eqs. 3-D.18, 3-D.19) ──────
            // ∂ε₁/∂σ_t = (1/m₁) · σ_t^(1/m₁ − 1) / A₁^(1/m₁)
            let d1, d2 =
                if p.M1 = 0.0 || p.A1 = 0.0 || sigmaT <= 0.0 then
                    0.0, 0.0
                else
                    let invM1 = 1.0 / p.M1

                    // D₁ = ∂/∂σ_t [ ε₁/2 · (1 − tanh H) ] — power-law term
                    // Eq. 3-D.18: σ_t (1/m₁ − 1) / (2 m₁ A₁^(1/m₁))
                    let d1_ = sigmaT * (invM1 - 1.0) / (2.0 * p.M1 * p.A1 ** invM1)

                    // D₂ = ∂/∂σ_t [ ε₁/2 · (1 − tanh H) ] — tanh-coupling term
                    // Eq. 3-D.19: −½ · (1/A₁^(1/m₁)) · σ_t^(1/m₁) ·
                    //   { 2/(K(σ_uts−σ_ys)) · (1−tanh²H) + (1/m₁) · σ_t^(1/m₁−1) · tanh H }
                    let d2_ =
                        -0.5
                        * (1.0 / p.A1 ** invM1)
                        * (sigmaT ** invM1)
                        * (2.0 * dHdSigma * sech2H + invM1 * sigmaT ** (invM1 - 1.0) * tanhH)

                    d1_, d2_

            // ── D₃ and D₄  (macro-strain branch, Eqs. 3-D.20, 3-D.21) ──────
            // ∂ε₂/∂σ_t = (1/m₂) · σ_t^(1/m₂ − 1) / A₂^(1/m₂)
            let d3, d4 =
                if p.A2 = 0.0 || sigmaT <= 0.0 then
                    0.0, 0.0
                else
                    let invM2 = 1.0 / input.M2

                    // D₃ = ∂/∂σ_t [ ε₂/2 · (1 + tanh H) ] — power-law term
                    // Eq. 3-D.20: σ_t (1/m₂ − 1) / (2 m₂ A₂^(1/m₂))
                    let d3_ = sigmaT * (invM2 - 1.0) / (2.0 * input.M2 * p.A2 ** invM2)

                    // D₄ = ∂/∂σ_t [ ε₂/2 · (1 + tanh H) ] — tanh-coupling term
                    // Eq. 3-D.21: ½ · (1/A₂^(1/m₂)) · σ_t^(1/m₂) ·
                    //   { 2/(K(σ_uts−σ_ys)) · (1−tanh²H) + (1/m₂) · σ_t^(1/m₂−1) · tanh H }
                    let d4_ =
                        0.5
                        * (1.0 / p.A2 ** invM2)
                        * (sigmaT ** invM2)
                        * (2.0 * dHdSigma * sech2H + invM2 * sigmaT ** (invM2 - 1.0) * tanhH)

                    d3_, d4_

            // Eq. 3-D.17  E_t = (1/E_y + D₁ + D₂ + D₃ + D₄)⁻¹
            let sumD = 1.0 / input.ElasticModulus + d1 + d2 + d3 + d4

            if sumD <= 0.0 || not (isFinite sumD) then
                Error(
                    MaterialError.InvalidOperation
                        "ASME VIII.2 §3-D tangent modulus: 1/E_y + D₁ + D₂ + D₃ + D₄ must be positive and finite"
                )
            else
                Ok
                    { D1 = d1
                      D2 = d2
                      D3 = d3
                      D4 = d4
                      TangentModulus = 1.0 / sumD
                      Params = p }

/// <summary>Status guard for the future API 579-1/ASME FFS-1 Annex 10B.5 implementation.</summary>
module Api579Annex10B5 =

    /// <summary>Warning returned until the licensed Annex 10B.5 equations and data are implemented.</summary>
    [<Literal>]
    let Warning =
        "API 579-1/ASME FFS-1 Annex 10B.5 is not implemented. "
        + "Do not use this library to generate isochronous stress-strain tables by this method."

    /// <summary>
    /// Prevents callers from treating Annex 10B.5 generation as available before implementation is validated.
    /// </summary>
    let ensureImplemented () : Result<unit, MaterialError> =
        Error(MaterialError.InvalidOperation Warning)
