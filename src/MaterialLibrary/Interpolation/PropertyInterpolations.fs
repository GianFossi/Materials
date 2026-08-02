namespace MaterialLibrary.Interpolation

open System
open MaterialLibrary.Domain

// Interpolation of specific heat, density, stress-strain, creep, and stress-rupture tables.

/// <summary>Interpolation functions for temperature-dependent specific heat Cp(T) tables.</summary>
module SpecificHeatInterpolation =
    /// <summary>Evaluates Cp at a target temperature by interpolating a tabulated Cp(T) dataset.</summary>
    /// <param name="mode">Interpolation algorithm to use (see <see cref="InterpolationMode"/>).
    /// <c>CubicSpline</c> and <c>LagrangePolynomial n</c> are evaluated directly.</param>
    /// <param name="targetTemp">Query temperature (°C). Must lie within the table range.</param>
    /// <param name="table">List of <see cref="SpecificHeatTablePoint"/> entries that define Cp(T).</param>
    /// <returns>
    /// <c>Ok cp</c> — interpolated specific heat (J⋅kg⁻¹⋅K⁻¹). <br/>
    /// <c>Error InsufficientData</c> — the table is empty or the required bracket is missing. <br/>
    /// <c>Error (OutOfRange (T_min, T_max))</c> — <paramref name="targetTemp"/> is outside the table range.
    /// </returns>
    let interpolate
        (mode: InterpolationMode)
        (targetTemp: float)
        (table: SpecificHeatTablePoint list)
        : Result<float, InterpolationError> =

        if List.isEmpty table then
            Error InterpolationError.InsufficientData
        else
            interpolate1D mode (table |> List.map (fun p -> float p.Temperature, float p.SpecificHeat)) targetTemp

// ========== DENSITY INTERPOLATION ==========

/// <summary>Interpolation functions for temperature-dependent density ρ(T) tables.</summary>
module DensityInterpolation =
    /// <summary>Evaluates density at a target temperature by interpolating a tabulated ρ(T) dataset.</summary>
    /// <param name="mode">Interpolation algorithm to use (see <see cref="InterpolationMode"/>).</param>
    /// <param name="targetTemp">Query temperature (°C). Must lie within the table range.</param>
    /// <param name="table">List of <see cref="DensityTablePoint"/> entries that define ρ(T).</param>
    /// <returns>
    /// <c>Ok rho</c> — interpolated density (kg⋅m⁻³). <br/>
    /// <c>Error InsufficientData</c> — the table is empty or the required bracket is missing. <br/>
    /// <c>Error (OutOfRange (T_min, T_max))</c> — <paramref name="targetTemp"/> is outside the table range.
    /// </returns>
    let interpolate
        (mode: InterpolationMode)
        (targetTemp: float)
        (table: DensityTablePoint list)
        : Result<float, InterpolationError> =

        if List.isEmpty table then
            Error InterpolationError.InsufficientData
        else
            interpolate1D mode (table |> List.map (fun p -> float p.Temperature, float p.Density)) targetTemp

// ========== STRESS-STRAIN TABLE INTERPOLATION ==========

/// <summary>Interpolation functions for stress-strain tables sigma(epsilon) at a given temperature.</summary>
module StressStrainInterpolation =
    /// <summary>Evaluates stress at a target strain by interpolating a stress-strain table.</summary>
    /// <param name="mode">Interpolation algorithm to use (see <see cref="InterpolationMode"/>).</param>
    /// <param name="targetStrain">Query strain (dimensionless, e.g. 0.002 for 0.2%). Must lie within the curve range.</param>
    /// <param name="table">A <see cref="StressStrainTable"/> containing tabulated strain/stress data.</param>
    /// <returns>
    /// <c>Ok σ</c> — interpolated stress (MPa). <br/>
    /// <c>Error InsufficientData</c> — the curve contains no points or the bracket is missing. <br/>
    /// <c>Error (OutOfRange (ε_min, ε_max))</c> — <paramref name="targetStrain"/> is outside the curve range.
    /// </returns>
    let stressFromStrain
        (mode: InterpolationMode)
        (targetStrain: float)
        (table: StressStrainTable)
        : Result<float, InterpolationError> =
        match table.Table.Columns with
        | [ column ] when not (List.isEmpty column.Entries) ->
            column.Entries
            |> List.map (fun entry -> entry.X, entry.Value)
            |> fun points -> interpolate1D mode points targetStrain
        | _ -> Error InterpolationError.InsufficientData

// ========== CREEP CURVE INTERPOLATION ==========

/// <summary>Interpolation functions for experimental creep tables at a given temperature and stress.</summary>
module CreepInterpolation =
    /// <summary>Evaluates creep strain at a target time by interpolating a creep table.</summary>
    /// <param name="mode">Interpolation algorithm to use (see <see cref="InterpolationMode"/>).</param>
    /// <param name="targetTime">Query time (hours). Must lie within the curve's time range.</param>
    /// <param name="table">A <see cref="CreepTable"/> containing tabulated time/strain data.</param>
    /// <returns>
    /// <c>Ok ε</c> — interpolated creep strain (%). <br/>
    /// <c>Error InsufficientData</c> — the curve contains no points or the bracket is missing. <br/>
    /// <c>Error (OutOfRange (t_min, t_max))</c> — <paramref name="targetTime"/> is outside the curve range.
    /// </returns>
    let strainFromTime
        (mode: InterpolationMode)
        (targetTime: float)
        (table: CreepTable)
        : Result<float, InterpolationError> =

        match table.Table.Columns with
        | [ column ] when not (List.isEmpty column.Entries) ->
            interpolate1D mode (column.Entries |> List.map (fun entry -> entry.X, entry.Value)) targetTime
        | _ -> Error InterpolationError.InsufficientData

// ========== STRESS-RUPTURE INTERPOLATION ==========

/// <summary>Interpolation functions for stress-rupture (creep-rupture) curves at a given temperature.</summary>
/// <remarks>
/// Stress-rupture curves plot the stress required to cause fracture in a given time at elevated temperature.
/// They are a key design input for ASME Section II Part D allowable stress determination.
/// </remarks>
module StressRuptureInterpolation =
    /// <summary>Evaluates the rupture stress at a target time to rupture by interpolating a stress-rupture table.</summary>
    /// <param name="mode">Interpolation algorithm to use (see <see cref="InterpolationMode"/>).</param>
    /// <param name="targetTime">Query time to rupture (hours). Must lie within the table's time range.</param>
    /// <param name="table">A <see cref="StressRuptureTable"/> containing the tabulated (t_r, σ_r) data points.</param>
    /// <returns>
    /// <c>Ok σ_r</c> — interpolated rupture stress (MPa). <br/>
    /// <c>Error InsufficientData</c> — the table contains no points or the bracket is missing. <br/>
    /// <c>Error (OutOfRange (t_min, t_max))</c> — <paramref name="targetTime"/> is outside the table range.
    /// </returns>
    let stressFromTimeToRupture
        (mode: InterpolationMode)
        (targetTime: float)
        (table: StressRuptureTable)
        : Result<float, InterpolationError> =

        match table.Table.Columns with
        | [ column ] when not (List.isEmpty column.Entries) ->
            interpolate1D mode (column.Entries |> List.map (fun entry -> entry.X, entry.Value)) targetTime
        | _ -> Error InterpolationError.InsufficientData

// ========== FATIGUE INTERPOLATION ==========
