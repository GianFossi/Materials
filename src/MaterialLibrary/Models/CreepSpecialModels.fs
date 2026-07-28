namespace MaterialLibrary.Domain

open System

/// <summary>
/// Pure functions implementing the Norton (Bailey-Norton) Power Law creep model.
/// </summary>
/// <remarks>
/// Model equation: ε = A · σ^n · t^m
/// where ε is creep strain (%), σ is stress (MPa), t is time (hours),
/// A is the pre-exponential coefficient, n is the stress exponent, and m is the time exponent.
/// Reference: Norton, F.H. (1929) "The Creep of Steel at High Temperatures", McGraw-Hill.
/// </remarks>
module NortonPowerLaw =
    let private isFinite value =
        not (Double.IsNaN value || Double.IsInfinity value)

    let private validate A n m sigma time =
        if [ A; n; m; sigma; time ] |> List.exists (isFinite >> not) then
            Error(MaterialError.CreepModelError "Norton inputs must be finite")
        elif A < 0.0 || n < 0.0 || m < 0.0 then
            Error(MaterialError.CreepModelError "Norton coefficients and exponents must be non-negative")
        elif sigma < 0.0 || time < 0.0 then
            Error(MaterialError.CreepModelError "Norton stress and time must be non-negative")
        else
            Ok()

    /// <summary>Computes the cumulative creep strain at a given time using the Norton Power Law.</summary>
    /// <param name="A">Pre-exponential coefficient (calibrated at a specific temperature).</param>
    /// <param name="n">Stress exponent (dimensionless). Typically 3–7 for metals.</param>
    /// <param name="m">Time exponent (dimensionless). Typically 0.2–0.4 for primary creep, 1.0 for steady-state.</param>
    /// <param name="sigma">Applied stress (MPa).</param>
    /// <param name="time">Elapsed time (hours).</param>
    /// <returns>Creep strain ε (%) at the specified time.</returns>
    let creepStrain
        (A: float)
        (n: float)
        (m: float)
        (sigma: float)
        (time: float)
        : Result<float, MaterialError> =
        validate A n m sigma time
        |> Result.bind (fun () ->
            let strain = A * sigma ** n * time ** m

            if isFinite strain then
                Ok strain
            else
                Error(MaterialError.CreepModelError "Norton creep strain overflowed"))

    /// <summary>Computes the instantaneous creep rate dε/dt at a given time using the Norton Power Law.</summary>
    /// <param name="A">Pre-exponential coefficient.</param>
    /// <param name="n">Stress exponent.</param>
    /// <param name="m">Time exponent.</param>
    /// <param name="sigma">Applied stress (MPa).</param>
    /// <param name="time">Elapsed time (hours).</param>
    /// <returns>Creep rate dε/dt (%/hour) at the specified time.</returns>
    let creepRate
        (A: float)
        (n: float)
        (m: float)
        (sigma: float)
        (time: float)
        : Result<float, MaterialError> =
        validate A n m sigma time
        |> Result.bind (fun () ->
            if time = 0.0 && m < 1.0 then
                Error(MaterialError.CreepModelError "Norton creep rate is singular at time zero when m < 1")
            else
                let rate = A * m * sigma ** n * time ** (m - 1.0)

                if isFinite rate then
                    Ok rate
                else
                    Error(MaterialError.CreepModelError "Norton creep rate overflowed"))

/// <summary>
/// Pure functions implementing the Garofalo (hyperbolic-sine) creep model.
/// </summary>
/// <remarks>
/// Temperature-calibrated model equation: ε = A · [sinh(ασ)]^n · t^m.
/// Use <c>creepStrainWithActivationEnergy</c> when A excludes the Arrhenius term.
/// The sinh function provides a smooth bridge between the low-stress (power-law) and
/// high-stress (exponential) regimes, making this model accurate over a wide range of stresses.
/// Reference: Garofalo, F. (1965) "Fundamentals of Creep and Creep Rupture in Metals", Macmillan.
/// </remarks>
module GarofaloModel =
    let private isFinite value =
        not (Double.IsNaN value || Double.IsInfinity value)

    /// <summary>
    /// Computes creep strain using a temperature-calibrated effective A coefficient.
    /// This overload does not apply a separate Arrhenius activation-energy term.
    /// </summary>
    /// <param name="A">Pre-exponential coefficient.</param>
    /// <param name="n">Stress exponent (dimensionless).</param>
    /// <param name="m">Time exponent (dimensionless).</param>
    /// <param name="alpha">Stress-scaling constant α (MPa⁻¹). Controls the power-law / exponential transition.</param>
    /// <param name="sigma">Applied stress (MPa).</param>
    /// <param name="time">Elapsed time (hours).</param>
    /// <returns>Creep strain ε (%) at the specified time.</returns>
    let creepStrain
        (A: float)
        (n: float)
        (m: float)
        (alpha: float)
        (sigma: float)
        (time: float)
        : Result<float, MaterialError> =
        if [ A; n; m; alpha; sigma; time ] |> List.exists (isFinite >> not) then
            Error(MaterialError.CreepModelError "Garofalo inputs must be finite")
        elif A < 0.0 || n < 0.0 || m < 0.0 || alpha < 0.0 then
            Error(MaterialError.CreepModelError "Garofalo coefficients and exponents must be non-negative")
        elif sigma < 0.0 || time < 0.0 then
            Error(MaterialError.CreepModelError "Garofalo stress and time must be non-negative")
        else
            let strain = A * Math.Sinh(alpha * sigma) ** n * time ** m

            if isFinite strain then
                Ok strain
            else
                Error(MaterialError.CreepModelError "Garofalo creep strain overflowed")

    /// <summary>
    /// Computes creep strain with the Arrhenius factor exp(-Q/(R*T)).
    /// Temperature is supplied in degC, Q in J/mol, and R in J/(mol*K).
    /// </summary>
    let creepStrainWithActivationEnergy
        (A: float)
        (n: float)
        (m: float)
        (alpha: float)
        (Q: float)
        (temperatureCelsius: float)
        (sigma: float)
        (time: float)
        : Result<float, MaterialError> =
        let absoluteTemperature = temperatureCelsius + 273.15

        if not (isFinite Q) || Q < 0.0 then
            Error(MaterialError.CreepModelError "Garofalo activation energy Q must be finite and non-negative")
        elif not (isFinite temperatureCelsius) || absoluteTemperature <= 0.0 then
            Error(MaterialError.CreepModelError "Garofalo temperature must be above absolute zero")
        else
            creepStrain A n m alpha sigma time
            |> Result.bind (fun unadjusted ->
                let gasConstant = 8.31446261815324
                let strain = unadjusted * Math.Exp(-Q / (gasConstant * absoluteTemperature))

                if isFinite strain then
                    Ok strain
                else
                    Error(MaterialError.CreepModelError "Garofalo Arrhenius creep strain overflowed"))

/// <summary>
/// Numerical integration functions for the Kachanov–Robinson Continuum Damage Mechanics model.
/// </summary>
/// <remarks>
/// The model consists of two coupled ODEs integrated forward in time with explicit Euler stepping:
///   Creep rate  : dε/dt = A1 · σ^N1 / (1 - ω)^M1
///   Damage rate : dω/dt = A2 · σ^N2 / (1 - ω)^M2
/// The damage variable ω is initialised at zero and clamped to [0, 1].
/// As ω → 1 the creep rate accelerates, reproducing tertiary creep and rupture.
/// Reference: Kachanov, L.M. (1958) Izv. AN SSSR Otd. Tekhn. Nauk, 8, 26.
/// </remarks>
module KachanovOmega =
    [<Literal>]
    let private MaximumTimeSteps = 1_000_000

    let private isFinite value =
        not (Double.IsNaN value || Double.IsInfinity value)

    let private validate
        (coefficients: float list)
        (exponents: float list)
        (sigma: float)
        (timeSteps: int)
        (totalTime: float)
        : Result<unit, MaterialError> =
        if coefficients |> List.exists (fun value -> not (isFinite value) || value < 0.0) then
            Error(MaterialError.CreepModelError "Kachanov coefficients must be finite and non-negative")
        elif exponents |> List.exists (fun value -> not (isFinite value) || value < 0.0) then
            Error(MaterialError.CreepModelError "Kachanov exponents must be finite and non-negative")
        elif not (isFinite sigma) || sigma <= 0.0 then
            Error(MaterialError.CreepModelError "Applied stress must be finite and > 0 MPa")
        elif timeSteps <= 0 then
            Error(MaterialError.CreepModelError "timeSteps must be > 0")
        elif timeSteps > MaximumTimeSteps then
            Error(
                MaterialError.CreepModelError
                    $"timeSteps must not exceed {MaximumTimeSteps} to prevent excessive memory allocation"
            )
        elif not (isFinite totalTime) || totalTime < 0.0 then
            Error(MaterialError.CreepModelError "totalTime must be finite and >= 0 hours")
        else
            Ok()

    /// <summary>
    /// Integrates the damage evolution equation dω/dt = A2 · σ^N2 / (1 - ω)^M2 using explicit Euler steps.
    /// </summary>
    /// <param name="A2">Damage-rate coefficient.</param>
    /// <param name="N2">Stress exponent for the damage-rate equation.</param>
    /// <param name="M2">Damage exponent for the damage-rate equation.</param>
    /// <param name="sigma">Constant applied stress (MPa).</param>
    /// <param name="timeSteps">Number of equal time steps for the numerical integration.</param>
    /// <param name="totalTime">Total simulation time (hours).</param>
    /// <returns>
    /// A list of <c>timeSteps + 1</c> damage values ω(t), from t = 0 to t = totalTime.
    /// Values are clamped to [0, 1]; ω = 1 indicates material rupture.
    /// </returns>
    let omegaEvolution
        (A2: float)
        (N2: float)
        (M2: float)
        (sigma: float)
        (timeSteps: int)
        (totalTime: float)
        : Result<float list, MaterialError> =

        validate [ A2 ] [ N2; M2 ] sigma timeSteps totalTime
        |> Result.bind (fun () ->
            let dt = totalTime / float timeSteps
            let omegas = Array.zeroCreate<float> (timeSteps + 1)

            for i in 1..timeSteps do
                let previous = omegas.[i - 1]

                if previous >= 1.0 then
                    omegas.[i] <- 1.0
                else
                    let remainingIntegrity = 1.0 - previous
                    let damageRate = A2 * sigma ** N2 / remainingIntegrity ** M2
                    omegas.[i] <- min 1.0 (previous + damageRate * dt)

            if omegas |> Array.exists (isFinite >> not) then
                Error(MaterialError.CreepModelError "Kachanov damage integration produced a non-finite value")
            else
                Ok(Array.toList omegas))

    /// <summary>
    /// Integrates both the creep-rate and damage-rate equations simultaneously to obtain ε(t) with damage coupling.
    /// </summary>
    /// <param name="A1">Creep-rate coefficient.</param>
    /// <param name="N1">Stress exponent for the creep-rate equation.</param>
    /// <param name="M1">Damage exponent for the creep-rate equation.</param>
    /// <param name="A2">Damage-rate coefficient.</param>
    /// <param name="N2">Stress exponent for the damage-rate equation.</param>
    /// <param name="M2">Damage exponent for the damage-rate equation.</param>
    /// <param name="sigma">Constant applied stress (MPa).</param>
    /// <param name="timeSteps">Number of equal time steps for the numerical integration.</param>
    /// <param name="totalTime">Total simulation time (hours).</param>
    /// <returns>
    /// A list of <c>timeSteps + 1</c> cumulative creep strain values ε(t) (%).
    /// The increasing slope in the later time steps reflects tertiary creep driven by damage.
    /// </returns>
    let creepStrainWithDamage
        (A1: float)
        (N1: float)
        (M1: float)
        (A2: float)
        (N2: float)
        (M2: float)
        (sigma: float)
        (timeSteps: int)
        (totalTime: float)
        : Result<float list, MaterialError> =

        validate [ A1; A2 ] [ N1; M1; N2; M2 ] sigma timeSteps totalTime
        |> Result.bind (fun () ->
            omegaEvolution A2 N2 M2 sigma timeSteps totalTime
            |> Result.bind (fun values ->
                let omegas = values |> List.toArray
                let dt = totalTime / float timeSteps
                let strains = Array.zeroCreate<float> (timeSteps + 1)

                for i in 1..timeSteps do
                    let damage = omegas.[i - 1]

                    if damage >= 1.0 then
                        strains.[i] <- strains.[i - 1]
                    else
                        let remainingIntegrity = 1.0 - damage
                        let strainRate = A1 * sigma ** N1 / remainingIntegrity ** M1
                        strains.[i] <- strains.[i - 1] + strainRate * dt

                if strains |> Array.exists (isFinite >> not) then
                    Error(MaterialError.CreepModelError "Kachanov creep integration produced a non-finite value")
                else
                    Ok(Array.toList strains)))

    let private maxAlignedDifference (coarse: float list) (fine: float list) =
        let coarseValues = coarse |> List.toArray
        let fineValues = fine |> List.toArray

        coarseValues
        |> Array.mapi (fun index value -> abs (value - fineValues.[index * 2]))
        |> Array.max

    let private ruptureTime totalTime values =
        let items = values |> List.toArray
        let steps = items.Length - 1

        items
        |> Array.tryFindIndex (fun value -> value >= 1.0)
        |> Option.map (fun index -> totalTime * float index / float steps)

    let private integrateUntilConverged
        (integrate: int -> Result<float list, MaterialError>)
        (totalTime: float)
        (initialSteps: int)
        (tolerance: float)
        (maxRefinements: int)
        (includeRuptureTime: bool)
        : Result<KachanovIntegrationHistory, MaterialError> =
        if initialSteps <= 0 then
            Error(MaterialError.CreepModelError "initialSteps must be > 0")
        elif not (isFinite tolerance) || tolerance <= 0.0 then
            Error(MaterialError.CreepModelError "convergence tolerance must be finite and > 0")
        elif maxRefinements < 1 then
            Error(MaterialError.CreepModelError "maxRefinements must be >= 1")
        else
            let rec refine refinement steps coarse =
                if steps > MaximumTimeSteps / 2 then
                    Error(MaterialError.CreepModelError "Kachanov refinement exceeded the supported step count")
                else
                    let fineSteps = steps * 2

                    integrate fineSteps
                    |> Result.bind (fun fine ->
                        let difference = maxAlignedDifference coarse fine

                        if difference <= tolerance then
                            Ok
                                { TimeStep = totalTime / float fineSteps
                                  Values = fine
                                  RuptureTime =
                                    if includeRuptureTime then
                                        ruptureTime totalTime fine
                                    else
                                        None }
                        elif refinement >= maxRefinements then
                            Error(
                                MaterialError.CreepModelError(
                                    sprintf
                                        "Kachanov integration did not converge after %d refinements; max difference %.6g"
                                        maxRefinements
                                        difference
                                )
                            )
                        else
                            refine (refinement + 1) fineSteps fine)

            integrate initialSteps |> Result.bind (refine 1 initialSteps)

    /// Integrates damage with grid refinement until successive histories agree within tolerance.
    let omegaEvolutionConverged
        (A2: float)
        (N2: float)
        (M2: float)
        (sigma: float)
        (initialSteps: int)
        (totalTime: float)
        (tolerance: float)
        (maxRefinements: int)
        : Result<KachanovIntegrationHistory, MaterialError> =
        integrateUntilConverged
            (fun steps -> omegaEvolution A2 N2 M2 sigma steps totalTime)
            totalTime
            initialSteps
            tolerance
            maxRefinements
            true

    /// Integrates creep strain with grid refinement until successive histories agree within tolerance.
    let creepStrainWithDamageConverged
        (A1: float)
        (N1: float)
        (M1: float)
        (A2: float)
        (N2: float)
        (M2: float)
        (sigma: float)
        (initialSteps: int)
        (totalTime: float)
        (tolerance: float)
        (maxRefinements: int)
        : Result<KachanovIntegrationHistory, MaterialError> =
        integrateUntilConverged
            (fun steps -> creepStrainWithDamage A1 N1 M1 A2 N2 M2 sigma steps totalTime)
            totalTime
            initialSteps
            tolerance
            maxRefinements
            false
