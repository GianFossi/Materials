namespace MaterialLibrary.Excel

open System
open ExcelDna.Integration
open MaterialLibrary
open MaterialLibrary.Domain
open MaterialLibrary.Interpolation

/// <summary>
/// Tensile, yield, ultimate, and compression worksheet functions.
/// </summary>
/// <remarks>
/// Split out of the original single <c>StrengthPropertyFunctions</c> module. Excel-DNA
/// discovers worksheet functions from every public module, and the worksheet names come
/// from the <c>ExcelFunction</c> attributes, so the split does not change any formula.
/// </remarks>
module TensileFunctions =

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Governing minimum-strength table: temperature (degC), yield strength Sy, tensile strength Su (MPa). Elongation and reduction of area are room-temperature scalars; see MatBasicPropertiesTable.")>]
    let MatTensilePropertiesTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        : obj[,] =
        ExcelHelpers.withMaterial materialId (fun material -> Ok material.StrengthProperties.TensileProperties)
        |> Result.map (fun rows ->
            rows
            |> List.sortBy (fun r -> r.Temperature)
            |> List.map (fun r ->
                [ box r.Temperature; box r.YieldStrength; box r.TensileStrength ])
            |> ExcelHelpers.gridOfRows [ "Temperature"; "YieldStrength"; "TensileStrength" ])
        |> ExcelHelpers.ofGridResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Interpolated yield strength (MPa) at a given temperature (degC). Linear interpolation between tabulated values.")>]
    let MatYieldStrength
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Query temperature, degC.")>] temperatureC: float)
        : obj =
        ExcelHelpers.withMaterial materialId (fun material ->
            let points =
                material.StrengthProperties.TensileProperties
                |> List.map (fun r -> r.Temperature, r.YieldStrength)

            AdHocTable.interpolate "YieldStrength" "Temperature" "degC" "YieldStrength" "MPa" points temperatureC)
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Complete yield-strength table Sy: temperature (degC), yield strength (MPa).")>]
    let MatYieldStrengthTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        : obj[,] =
        ExcelHelpers.withMaterial materialId (fun material -> Ok material.StrengthProperties.TensileProperties)
        |> Result.map (fun rows ->
            rows
            |> List.sortBy (fun r -> r.Temperature)
            |> List.map (fun r -> [ box r.Temperature; box r.YieldStrength ])
            |> ExcelHelpers.gridOfRows [ "Temperature"; "Sy" ])
        |> ExcelHelpers.ofGridResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Interpolated ultimate tensile strength (MPa) at a given temperature (degC). Linear interpolation between tabulated values.")>]
    let MatUltimateStrength
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Query temperature, degC.")>] temperatureC: float)
        : obj =
        ExcelHelpers.withMaterial materialId (fun material ->
            let points =
                material.StrengthProperties.TensileProperties
                |> List.map (fun r -> r.Temperature, r.TensileStrength)

            AdHocTable.interpolate "UltimateStrength" "Temperature" "degC" "TensileStrength" "MPa" points temperatureC)
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Complete ultimate-strength table Su: temperature (degC), ultimate tensile strength (MPa).")>]
    let MatUltimateStrengthTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        : obj[,] =
        ExcelHelpers.withMaterial materialId (fun material -> Ok material.StrengthProperties.TensileProperties)
        |> Result.map (fun rows ->
            rows
            |> List.sortBy (fun r -> r.Temperature)
            |> List.map (fun r -> [ box r.Temperature; box r.TensileStrength ])
            |> ExcelHelpers.gridOfRows [ "Temperature"; "Su" ])
        |> ExcelHelpers.ofGridResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Complete compression-properties table: temperature (degC), compressive strength, compressive yield (MPa), if the material has one.")>]
    let MatCompressionPropertiesTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        : obj[,] =
        ExcelHelpers.withMaterial materialId (fun material ->
            match material.StrengthProperties.CompressionProperties with
            | None -> Error(MaterialError.InvalidOperation "No compression properties defined")
            | Some rows -> Ok rows)
        |> Result.map (fun rows ->
            rows
            |> List.sortBy (fun r -> r.Temperature)
            |> List.map (fun r -> [ box r.Temperature; box r.CompressiveStrength; box r.CompressiveYield ])
            |> ExcelHelpers.gridOfRows [ "Temperature"; "CompressiveStrength"; "CompressiveYield" ])
        |> ExcelHelpers.ofGridResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Interpolated compressive strength (MPa) at a given temperature (degC), if the material has compression data.")>]
    let MatCompressiveStrength
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Query temperature, degC.")>] temperatureC: float)
        : obj =
        ExcelHelpers.withMaterial materialId (fun material ->
            let points =
                defaultArg material.StrengthProperties.CompressionProperties []
                |> List.map (fun r -> r.Temperature, r.CompressiveStrength)

            AdHocTable.interpolate "CompressiveStrength" "Temperature" "degC" "CompressiveStrength" "MPa" points temperatureC)
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Interpolated compressive yield (MPa) at a given temperature (degC), if the material has compression data.")>]
    let MatCompressiveYield
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Query temperature, degC.")>] temperatureC: float)
        : obj =
        ExcelHelpers.withMaterial materialId (fun material ->
            let points =
                defaultArg material.StrengthProperties.CompressionProperties []
                |> List.map (fun r -> r.Temperature, r.CompressiveYield)

            AdHocTable.interpolate "CompressiveYield" "Temperature" "degC" "CompressiveYield" "MPa" points temperatureC)
        |> ExcelHelpers.ofFloatResult

    // ── Allowable stress (ASME Section I / VIII-1 / VIII-2 / Bolting, by size range) ──
