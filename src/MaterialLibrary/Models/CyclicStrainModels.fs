namespace MaterialLibrary.Domain

open System

/// <summary>
/// Input parameters for the ASME BPVC VIII.2-2025 §3-D.4 cyclic stress-strain model.
/// All stress values in MPa; all strain values dimensionless.
/// </summary>
/// <remarks>
/// Implements Eqs. 3-D.15 and 3-D.16.
/// K_css and n_css are obtained from Table 3-D.2M for the material at the design temperature.
/// </remarks>
type CyclicStrainModelInput =
    {
        /// K_css — cyclic strength coefficient (MPa). Table 3-D.2M.
        Kcss: float
        /// n_css — cyclic strain hardening exponent (dimensionless). Table 3-D.2M.
        Ncss: float
        /// Modulus of elasticity at temperature (MPa). E_y
        ElasticModulus: float
    }

/// <summary>
/// Result of evaluating the cyclic strain amplitude at one stress amplitude point.
/// </summary>
type CyclicStrainAmplitudeResult =
    {
        /// Stress amplitude σ_a (MPa).
        StressAmplitude: float
        /// Elastic strain amplitude σ_a / E_y (dimensionless).
        ElasticStrainAmplitude: float
        /// Plastic strain amplitude (σ_a / K_css)^(1/n_css) (dimensionless).
        PlasticStrainAmplitude: float
        /// Total cyclic strain amplitude ε_ta = σ_a/E_y + (σ_a/K_css)^(1/n_css) (dimensionless). Eq. 3-D.15.
        TotalStrainAmplitude: float
    }

/// <summary>
/// Result of evaluating the hysteresis loop strain range at one stress range point.
/// </summary>
type HysteresisStrainRangeResult =
    {
        /// Stress range σ_r (MPa).
        StressRange: float
        /// Elastic strain range σ_r / E_y (dimensionless).
        ElasticStrainRange: float
        /// Plastic strain range 2 · (σ_r / (2 K_css))^(1/n_css) (dimensionless).
        PlasticStrainRange: float
        /// Total strain range ε_tr = σ_r/E_y + 2·(σ_r/(2K_css))^(1/n_css) (dimensionless). Eq. 3-D.16.
        TotalStrainRange: float
    }

/// <summary>
/// Pure calculation module for the ASME BPVC VIII.2-2025 §3-D.4 cyclic stress-strain model.
/// Equations 3-D.15 and 3-D.16.
/// </summary>
module CyclicStrainModel =

    let private isFinite (v: float) =
        not (Double.IsNaN v || Double.IsInfinity v)

    let private validateInput (input: CyclicStrainModelInput) : Result<unit, MaterialError> =
        if input.Kcss <= 0.0 then
            Error(MaterialError.InvalidOperation "Kcss must be > 0 MPa")
        elif input.Ncss <= 0.0 then
            Error(MaterialError.InvalidOperation "Ncss must be > 0")
        elif input.ElasticModulus <= 0.0 then
            Error(MaterialError.InvalidOperation "ElasticModulus must be > 0 MPa")
        else
            Ok()

    /// <summary>
    /// Computes the cyclic strain amplitude ε_ta at a given stress amplitude σ_a.
    /// </summary>
    /// <remarks>
    /// Eq. 3-D.15: ε_ta = σ_a/E_y + (σ_a/K_css)^(1/n_css)
    /// </remarks>
    /// <param name="input">Model parameters (K_css, n_css, E_y).</param>
    /// <param name="stressAmplitude">Stress amplitude σ_a (MPa).</param>
    /// <returns>Strain amplitude result or a validation error.</returns>
    let computeStrainAmplitude
        (input: CyclicStrainModelInput)
        (stressAmplitude: float)
        : Result<CyclicStrainAmplitudeResult, MaterialError> =

        match validateInput input with
        | Error err -> Error err
        | Ok() ->
            if not (isFinite stressAmplitude) || stressAmplitude <= 0.0 then
                Error(MaterialError.InvalidOperation "StressAmplitude must be finite and > 0 MPa")
            else
                let elasticPart = stressAmplitude / input.ElasticModulus
                let plasticPart = (stressAmplitude / input.Kcss) ** (1.0 / input.Ncss)
                let totalStrain = elasticPart + plasticPart

                Ok
                    { StressAmplitude = stressAmplitude
                      ElasticStrainAmplitude = elasticPart
                      PlasticStrainAmplitude = plasticPart
                      TotalStrainAmplitude = totalStrain }

    /// <summary>
    /// Computes the hysteresis loop strain range ε_tr at a given stress range σ_r.
    /// </summary>
    /// <remarks>
    /// Eq. 3-D.16: ε_tr = σ_r/E_y + 2·(σ_r/(2·K_css))^(1/n_css)
    /// </remarks>
    /// <param name="input">Model parameters (K_css, n_css, E_y).</param>
    /// <param name="stressRange">Stress range σ_r (MPa).</param>
    /// <returns>Strain range result or a validation error.</returns>
    let computeStrainRange
        (input: CyclicStrainModelInput)
        (stressRange: float)
        : Result<HysteresisStrainRangeResult, MaterialError> =

        match validateInput input with
        | Error err -> Error err
        | Ok() ->
            if not (isFinite stressRange) || stressRange <= 0.0 then
                Error(MaterialError.InvalidOperation "StressRange must be finite and > 0 MPa")
            else
                let elasticPart = stressRange / input.ElasticModulus
                let plasticPart = 2.0 * (stressRange / (2.0 * input.Kcss)) ** (1.0 / input.Ncss)
                let totalStrain = elasticPart + plasticPart

                Ok
                    { StressRange = stressRange
                      ElasticStrainRange = elasticPart
                      PlasticStrainRange = plasticPart
                      TotalStrainRange = totalStrain }

    /// <summary>
    /// Generates a cyclic strain amplitude curve over a log-spaced stress grid.
    /// </summary>
    /// <param name="input">Model parameters.</param>
    /// <param name="minStress">Minimum stress amplitude in the grid (MPa).</param>
    /// <param name="maxStress">Maximum stress amplitude in the grid (MPa).</param>
    /// <param name="pointCount">Number of curve points (minimum 2).</param>
    /// <returns>List of curve points or a validation error.</returns>
    let generateCyclicPoints
        (input: CyclicStrainModelInput)
        (minStress: float)
        (maxStress: float)
        (pointCount: int)
        : Result<CyclicStressStrainPoint list, MaterialError> =

        if pointCount < 2 then
            Error(MaterialError.InvalidOperation "pointCount must be >= 2")
        elif not (isFinite minStress) || minStress <= 0.0 then
            Error(MaterialError.InvalidOperation "minStress must be finite and > 0 MPa")
        elif not (isFinite maxStress) || maxStress <= minStress then
            Error(MaterialError.InvalidOperation "maxStress must be > minStress")
        else
            let logMin = Math.Log10 minStress
            let logMax = Math.Log10 maxStress

            [ 0 .. pointCount - 1 ]
            |> List.fold
                (fun state i ->
                    state
                    |> Result.bind (fun points ->
                    let fraction = float i / float (pointCount - 1)
                    let sigma = 10.0 ** (logMin + fraction * (logMax - logMin))
                    computeStrainAmplitude input sigma
                    |> Result.map (fun result ->
                        ({ StressAmplitude = result.StressAmplitude
                           StrainAmplitude = result.TotalStrainAmplitude }: CyclicStressStrainPoint)
                        :: points)))
                (Ok [])
            |> Result.map List.rev

    /// <summary>
    /// Generates a hysteresis loop (strain range) curve over a log-spaced stress grid.
    /// </summary>
    /// <param name="input">Model parameters.</param>
    /// <param name="minStress">Minimum stress range in the grid (MPa).</param>
    /// <param name="maxStress">Maximum stress range in the grid (MPa).</param>
    /// <param name="pointCount">Number of curve points (minimum 2).</param>
    /// <returns>List of curve points or a validation error.</returns>
    let generateHysteresisPoints
        (input: CyclicStrainModelInput)
        (minStress: float)
        (maxStress: float)
        (pointCount: int)
        : Result<HysteresisRangePoint list, MaterialError> =

        if pointCount < 2 then
            Error(MaterialError.InvalidOperation "pointCount must be >= 2")
        elif not (isFinite minStress) || minStress <= 0.0 then
            Error(MaterialError.InvalidOperation "minStress must be finite and > 0 MPa")
        elif not (isFinite maxStress) || maxStress <= minStress then
            Error(MaterialError.InvalidOperation "maxStress must be > minStress")
        else
            let logMin = Math.Log10 minStress
            let logMax = Math.Log10 maxStress

            [ 0 .. pointCount - 1 ]
            |> List.fold
                (fun state i ->
                    state
                    |> Result.bind (fun points ->
                    let fraction = float i / float (pointCount - 1)
                    let sigma = 10.0 ** (logMin + fraction * (logMax - logMin))
                    computeStrainRange input sigma
                    |> Result.map (fun result ->
                        ({ StressRange = result.StressRange
                           StrainRange = result.TotalStrainRange }: HysteresisRangePoint)
                        :: points)))
                (Ok [])
            |> Result.map List.rev
