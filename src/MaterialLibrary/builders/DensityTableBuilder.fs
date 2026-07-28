namespace MaterialLibrary.Domain

open System

/// CULTURE RULE: Numeric parsing and formatting for XML/JSON persistence must always use CultureInfo.InvariantCulture.
/// <summary>
/// Builder functions for constructing density tables and estimating density variation with temperature.
/// </summary>
module DensityTableBuilder =

    let private isFinite (value: float) : bool =
        not (Double.IsNaN value || Double.IsInfinity value)

    /// <summary>
    /// Creates one density table point after validating temperature and density values.
    /// </summary>
    /// <param name="temperature">Temperature (degC).</param>
    /// <param name="density">Mass density (kg/m^3).</param>
    /// <returns>The constructed <see cref="DensityTablePoint"/> or a validation error.</returns>
    let createPoint (temperature: float) (density: float) : Result<DensityTablePoint, MaterialError> =
        if not (isFinite temperature) then
            Error(MaterialError.InvalidOperation "Density point temperature must be finite")
        elif not (isFinite density) || density <= 0.0 then
            Error(MaterialError.InvalidOperation "Density must be > 0 kg/m^3")
        else
            Ok
                { Temperature = temperature
                  Density = density }

    /// <summary>
    /// Validates and normalizes density points by sorting by temperature and rejecting duplicates.
    /// </summary>
    /// <param name="points">Raw density points (temperature degC, density kg/m^3).</param>
    /// <returns>Sorted points or a validation error.</returns>
    let normalizePoints (points: DensityTablePoint list) : Result<DensityTablePoint list, MaterialError> =
        if List.length points < 2 then
            Error(MaterialError.InvalidOperation "Density table requires at least two points")
        elif
            points
            |> List.exists (fun p -> not (isFinite p.Temperature) || not (isFinite p.Density) || p.Density <= 0.0)
        then
            Error(MaterialError.InvalidOperation "Density points contain invalid values")
        else
            let sorted = points |> List.sortBy (fun p -> p.Temperature)

            let hasDuplicateTemperature =
                sorted
                |> List.pairwise
                |> List.exists (fun (a, b) -> a.Temperature = b.Temperature)

            if hasDuplicateTemperature then
                Error(MaterialError.InvalidOperation "Density points contain duplicate temperature values")
            else
                Ok sorted

    /// <summary>
    /// Estimates metal density variation with temperature from a reference density and mean linear expansion coefficient.
    /// </summary>
    /// <remarks>
    /// Uses isotropic volumetric expansion approximation:
    /// rho(T) = rho_ref / (1 + alpha * (T - T_ref))^3
    /// where alpha is the mean linear expansion coefficient (1/degC).
    /// </remarks>
    /// <param name="referenceTemperature">Reference temperature T_ref (degC).</param>
    /// <param name="referenceDensity">Reference density rho_ref at T_ref (kg/m^3).</param>
    /// <param name="meanLinearExpansion">Mean linear expansion coefficient alpha (1/degC).</param>
    /// <param name="temperatures">Target temperatures where density will be estimated (degC).</param>
    /// <returns>Estimated and normalized density table or a validation error.</returns>
    let estimateFromMeanExpansion
        (referenceTemperature: float)
        (referenceDensity: float)
        (meanLinearExpansion: float)
        (temperatures: float list)
        : Result<DensityTablePoint list, MaterialError> =

        if not (isFinite referenceTemperature) then
            Error(MaterialError.InvalidOperation "Reference temperature must be finite")
        elif not (isFinite referenceDensity) || referenceDensity <= 0.0 then
            Error(MaterialError.InvalidOperation "Reference density must be > 0 kg/m^3")
        elif not (isFinite meanLinearExpansion) || meanLinearExpansion < 0.0 then
            Error(MaterialError.InvalidOperation "Mean linear expansion coefficient must be >= 0")
        elif List.length temperatures < 2 then
            Error(MaterialError.InvalidOperation "At least two target temperatures are required")
        elif temperatures |> List.exists (isFinite >> not) then
            Error(MaterialError.InvalidOperation "Target temperatures contain non-finite values")
        else
            let estimated =
                temperatures
                |> List.map (fun t ->
                    let deltaT = t - referenceTemperature
                    let scale = 1.0 + meanLinearExpansion * deltaT

                    if scale <= 0.0 then
                        { Temperature = t
                          Density = Double.NaN }
                    else
                        { Temperature = t
                          Density = referenceDensity / (scale ** 3.0) })

            if estimated |> List.exists (fun p -> not (isFinite p.Density) || p.Density <= 0.0) then
                Error(
                    MaterialError.InvalidOperation
                        "Estimated density produced non-physical values; verify alpha and temperature range"
                )
            else
                normalizePoints estimated

    /// <summary>
    /// Replaces a material's density table with validated points.
    /// </summary>
    /// <param name="points">Density table points to set.</param>
    /// <param name="material">Material to update.</param>
    /// <returns>Updated material with refreshed <c>LastModified</c>.</returns>
    let setDensityTable (points: DensityTablePoint list) (material: Material) : Result<Material, MaterialError> =
        normalizePoints points
        |> Result.map (fun normalized ->
            { material with
                PhysicalProperties =
                    { material.PhysicalProperties with
                        DensityTable = normalized }
                LastModified = DateTime.UtcNow })
