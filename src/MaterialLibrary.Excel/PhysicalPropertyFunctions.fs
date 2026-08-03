namespace MaterialLibrary.Excel

open ExcelDna.Integration
open MaterialLibrary.Domain

/// <summary>
/// Excel worksheet functions for temperature-independent basic properties and temperature-dependent
/// physical properties (density, elastic modulus, Poisson's ratio, specific heat, thermal expansion,
/// thermal conductivity).
/// </summary>
module PhysicalPropertyFunctions =

    // ── Basic (room-temperature) properties ─────────────────────────────────

    [<ExcelFunction(Category = "MaterialLibrary.Physical", Description = "Returns the room-temperature tensile-test results (elongation % longitudinal and transverse, reduction of area %, SMYS, SMUTS) as a 5-row table. Blank where a direction is not reported.")>]
    let MatBasicPropertiesTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        : obj[,] =
        ExcelHelpers.withMaterial materialId (fun material -> Ok material.BasicProperties)
        |> Result.map (fun bp ->
            ExcelHelpers.gridOfRows
                [ "Property"; "Value" ]
                [ [ box "ElongationLongitudinalPercent"; ExcelHelpers.boxOptional bp.ElongationLongitudinalPercent ]
                  [ box "ElongationTransversePercent"; ExcelHelpers.boxOptional bp.ElongationTransversePercent ]
                  [ box "ReductionOfAreaPercent"; box bp.ReductionOfAreaPercent ]
                  [ box "SpecifiedMinimumYieldStrength"; box bp.SpecifiedMinimumYieldStrength ]
                  [ box "SpecifiedMinimumUltimateStrength"; box bp.SpecifiedMinimumUltimateStrength ] ])
        |> ExcelHelpers.ofGridResult

    // ── Density ───────────────────────────────────────────────────────────

    [<ExcelFunction(Category = "MaterialLibrary.Physical", Description = "Interpolated mass density (kg/m^3) at a given temperature (degC).")>]
    let MatDensity
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Query temperature, degC.")>] temperatureC: float)
        ([<ExcelArgument(Description = "Interpolation mode: Linear (default), CubicSpline, Constant, Lagrange.")>] mode: obj)
        ([<ExcelArgument(Description = "Lagrange polynomial degree, used only when mode is Lagrange (default 3).")>] lagrangeDegree: obj)
        : obj =
        let interpolationMode = Args.interpolationMode mode lagrangeDegree

        LibraryCache.current().GetDensity(materialId, temperatureC, interpolationMode)
        |> Result.map (fun lookup -> lookup.Value)
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Physical", Description = "Complete density vs temperature table (degC, kg/m^3).")>]
    let MatDensityTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        : obj[,] =
        ExcelHelpers.withMaterial materialId (fun material -> Ok material.PhysicalProperties.DensityTable)
        |> Result.map (fun points ->
            points
            |> List.sortBy (fun p -> p.Temperature)
            |> List.map (fun p -> [ box p.Temperature; box p.Density ])
            |> ExcelHelpers.gridOfRows [ "Temperature"; "Density" ])
        |> ExcelHelpers.ofGridResult

    // ── Elastic modulus / Poisson's ratio / shear modulus ────────────────────

    [<ExcelFunction(Category = "MaterialLibrary.Physical", Description = "Interpolated Young's modulus of elasticity E (MPa) at a given temperature (degC). Linear interpolation between tabulated values.")>]
    let MatElasticModulus
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Query temperature, degC.")>] temperatureC: float)
        : obj =
        ExcelHelpers.withMaterial materialId (fun material ->
            let points =
                material.PhysicalProperties.ElasticModulusTable
                |> List.map (fun p -> p.Temperature, p.ElasticModulus)

            AdHocTable.interpolate "ElasticModulus" "Temperature" "degC" "ElasticModulus" "MPa" points temperatureC)
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Physical", Description = "Interpolated Poisson's ratio (dimensionless) at a given temperature (degC), from rows that specify it. Linear interpolation between tabulated values.")>]
    let MatPoissonRatio
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Query temperature, degC.")>] temperatureC: float)
        : obj =
        ExcelHelpers.withMaterial materialId (fun material ->
            let points =
                material.PhysicalProperties.ElasticModulusTable
                |> List.choose (fun p -> p.PoissonRatio |> Option.map (fun nu -> p.Temperature, nu))

            AdHocTable.interpolate "PoissonRatio" "Temperature" "degC" "PoissonRatio" "" points temperatureC)
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Physical", Description = "Shear modulus G = E / (2*(1+nu)) (MPa) at a given temperature (degC), from independently interpolated E and Poisson's ratio.")>]
    let MatShearModulus
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Query temperature, degC.")>] temperatureC: float)
        : obj =
        ExcelHelpers.withMaterial materialId (fun material ->
            let elasticModulusPoints =
                material.PhysicalProperties.ElasticModulusTable
                |> List.map (fun p -> p.Temperature, p.ElasticModulus)

            let poissonPoints =
                material.PhysicalProperties.ElasticModulusTable
                |> List.choose (fun p -> p.PoissonRatio |> Option.map (fun nu -> p.Temperature, nu))

            AdHocTable.interpolate "ElasticModulus" "Temperature" "degC" "ElasticModulus" "MPa" elasticModulusPoints temperatureC
            |> Result.bind (fun elasticModulus ->
                AdHocTable.interpolate "PoissonRatio" "Temperature" "degC" "PoissonRatio" "" poissonPoints temperatureC
                |> Result.map (fun poissonRatio -> ElasticModulusTablePoint.ComputeShearModulus(elasticModulus, poissonRatio))))
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Physical", Description = "Complete elastic-modulus table: temperature (degC), E (MPa), Poisson's ratio (blank where not specified).")>]
    let MatElasticModulusTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        : obj[,] =
        ExcelHelpers.withMaterial materialId (fun material -> Ok material.PhysicalProperties.ElasticModulusTable)
        |> Result.map (fun points ->
            points
            |> List.sortBy (fun p -> p.Temperature)
            |> List.map (fun p ->
                [ box p.Temperature
                  box p.ElasticModulus
                  p.PoissonRatio |> Option.map box |> Option.defaultValue (box "") ])
            |> ExcelHelpers.gridOfRows [ "Temperature"; "ElasticModulus"; "PoissonRatio" ])
        |> ExcelHelpers.ofGridResult

    // ── Specific heat ─────────────────────────────────────────────────────

    [<ExcelFunction(Category = "MaterialLibrary.Physical", Description = "Interpolated specific heat Cp (J/(kg*K)) at a given temperature (degC).")>]
    let MatSpecificHeat
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Query temperature, degC.")>] temperatureC: float)
        ([<ExcelArgument(Description = "Interpolation mode: Linear (default), CubicSpline, Constant, Lagrange.")>] mode: obj)
        ([<ExcelArgument(Description = "Lagrange polynomial degree, used only when mode is Lagrange (default 3).")>] lagrangeDegree: obj)
        : obj =
        let interpolationMode = Args.interpolationMode mode lagrangeDegree

        LibraryCache.current().GetSpecificHeatFromTable(materialId, temperatureC, interpolationMode)
        |> Result.map (fun lookup -> lookup.Value)
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Physical", Description = "Complete specific-heat table (degC, J/(kg*K)), if the material has one.")>]
    let MatSpecificHeatTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        : obj[,] =
        ExcelHelpers.withMaterial materialId (fun material ->
            match material.PhysicalProperties.SpecificHeatTable with
            | None -> Error(MaterialError.InvalidOperation "No specific heat table defined")
            | Some points -> Ok points)
        |> Result.map (fun points ->
            points
            |> List.sortBy (fun p -> p.Temperature)
            |> List.map (fun p -> [ box p.Temperature; box p.SpecificHeat ])
            |> ExcelHelpers.gridOfRows [ "Temperature"; "SpecificHeat" ])
        |> ExcelHelpers.ofGridResult

    // ── Thermal expansion ─────────────────────────────────────────────────

    [<ExcelFunction(Category = "MaterialLibrary.Physical", Description = "Interpolated mean coefficient of thermal expansion alpha (1/degC) at a given temperature (degC), from the reference temperature stored with the material.")>]
    let MatThermalExpansion
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Query temperature, degC.")>] temperatureC: float)
        : obj =
        ExcelHelpers.withMaterial materialId (fun material ->
            let points =
                material.PhysicalProperties.ThermalExpansionTable
                |> List.map (fun p -> p.Temperature, p.ExpansionCoefficient)

            AdHocTable.interpolate "ThermalExpansion" "Temperature" "degC" "ExpansionCoefficient" "1/degC" points temperatureC)
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Physical", Description = "Reference temperature (degC) used by the mean thermal-expansion table.")>]
    let MatThermalExpansionReferenceTemperature
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        : obj =
        ExcelHelpers.withMaterial materialId (fun material -> Ok material.PhysicalProperties.ThermalExpansionReferenceTemperature)
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Physical", Description = "Complete mean thermal-expansion table (degC, 1/degC).")>]
    let MatThermalExpansionTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        : obj[,] =
        ExcelHelpers.withMaterial materialId (fun material -> Ok material.PhysicalProperties.ThermalExpansionTable)
        |> Result.map (fun points ->
            points
            |> List.sortBy (fun p -> p.Temperature)
            |> List.map (fun p -> [ box p.Temperature; box p.ExpansionCoefficient ])
            |> ExcelHelpers.gridOfRows [ "Temperature"; "ExpansionCoefficient" ])
        |> ExcelHelpers.ofGridResult

    // ── Thermal conductivity ──────────────────────────────────────────────

    [<ExcelFunction(Category = "MaterialLibrary.Physical", Description = "Interpolated thermal conductivity kappa (W/(m*K)) at a given temperature (degC), if the material has one.")>]
    let MatThermalConductivity
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Query temperature, degC.")>] temperatureC: float)
        : obj =
        ExcelHelpers.withMaterial materialId (fun material ->
            match material.PhysicalProperties.ThermalConductivityTable with
            | None -> Error(MaterialError.InvalidOperation "No thermal conductivity table defined")
            | Some points ->
                AdHocTable.interpolate "ThermalConductivity" "Temperature" "degC" "ThermalConductivity" "W/(m*K)" points temperatureC)
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Physical", Description = "Complete thermal-conductivity table (degC, W/(m*K)), if the material has one.")>]
    let MatThermalConductivityTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        : obj[,] =
        ExcelHelpers.withMaterial materialId (fun material ->
            match material.PhysicalProperties.ThermalConductivityTable with
            | None -> Error(MaterialError.InvalidOperation "No thermal conductivity table defined")
            | Some points -> Ok points)
        |> Result.map (fun points ->
            points
            |> List.sortBy fst
            |> List.map (fun (t, k) -> [ box t; box k ])
            |> ExcelHelpers.gridOfRows [ "Temperature"; "ThermalConductivity" ])
        |> ExcelHelpers.ofGridResult
