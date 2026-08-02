namespace MaterialLibrary.Excel

open System
open ExcelDna.Integration
open MaterialLibrary
open MaterialLibrary.Domain
open MaterialLibrary.Interpolation

/// <summary>
/// Cyclic strain and hysteresis worksheet functions.
/// </summary>
/// <remarks>
/// Split out of the original single <c>StrengthPropertyFunctions</c> module. Excel-DNA
/// discovers worksheet functions from every public module, and the worksheet names come
/// from the <c>ExcelFunction</c> attributes, so the split does not change any formula.
/// </remarks>
module CyclicStrainFunctions =
    let private selectCyclicStrainTable
        (material: Material)
        (temperatureC: float)
        (materialDescription: string option)
        : Result<CyclicStrainTable, MaterialError> =
        let candidates =
            material.StrengthProperties.CyclicStrainTables
            |> List.filter (fun t -> t.ReferenceTemperature = temperatureC)

        let filtered =
            match materialDescription with
            | Some description ->
                candidates
                |> List.filter (fun t -> String.Equals(t.MaterialDescription, description, StringComparison.OrdinalIgnoreCase))
            | None -> candidates

        match filtered with
        | [ only ] -> Ok only
        | [] ->
            Error(MaterialError.InvalidOperation(sprintf "No cyclic strain-strain table at %.4g degC" temperatureC))
        | multiple ->
            let names = multiple |> List.map (fun t -> t.MaterialDescription) |> String.concat ", "

            Error(
                MaterialError.InvalidOperation(
                    sprintf
                        "%d cyclic strain-strain tables at %.4g degC; pass materialDescription to select one: %s"
                        (List.length multiple)
                        temperatureC
                        names
                )
            )

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Interpolated cyclic strain amplitude (dimensionless) at a given stress amplitude (MPa) and temperature (degC).")>]
    let MatCyclicStrainAmplitude
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Curve temperature, degC (exact match).")>] temperatureC: float)
        ([<ExcelArgument(Description = "Query stress amplitude, MPa.")>] stressAmplitudeMPa: float)
        ([<ExcelArgument(Description = "Material/grade description, needed only when more than one table exists at this temperature.")>] materialDescription: obj)
        : obj =
        ExcelHelpers.withMaterial materialId (fun material ->
            selectCyclicStrainTable material temperatureC (Args.optionalTextOption materialDescription)
            |> Result.bind (fun table -> PropertyTable.lookup1D stressAmplitudeMPa table.Table)
            |> Result.map (fun result -> result.Value))
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Interpolated hysteresis-loop strain range (dimensionless) at a given stress range (MPa) and temperature (degC).")>]
    let MatCyclicHysteresisStrainRange
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Curve temperature, degC (exact match).")>] temperatureC: float)
        ([<ExcelArgument(Description = "Query stress range, MPa.")>] stressRangeMPa: float)
        ([<ExcelArgument(Description = "Material/grade description, needed only when more than one table exists at this temperature.")>] materialDescription: obj)
        : obj =
        ExcelHelpers.withMaterial materialId (fun material ->
            selectCyclicStrainTable material temperatureC (Args.optionalTextOption materialDescription)
            |> Result.bind (fun table -> PropertyTable.lookup1D stressRangeMPa table.HysteresisRangeTable)
            |> Result.map (fun result -> result.Value))
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Complete cyclic strain-amplitude table (stress amplitude MPa, strain amplitude) at a given temperature.")>]
    let MatCyclicStrainTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Curve temperature, degC (exact match).")>] temperatureC: float)
        ([<ExcelArgument(Description = "Material/grade description, needed only when more than one table exists at this temperature.")>] materialDescription: obj)
        : obj[,] =
        ExcelHelpers.withMaterial materialId (fun material ->
            selectCyclicStrainTable material temperatureC (Args.optionalTextOption materialDescription)
            |> Result.map (fun table -> ExcelHelpers.table1DToGrid table.Table))
        |> ExcelHelpers.ofGridResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Complete hysteresis-range table (stress range MPa, strain range) at a given temperature.")>]
    let MatCyclicHysteresisTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Curve temperature, degC (exact match).")>] temperatureC: float)
        ([<ExcelArgument(Description = "Material/grade description, needed only when more than one table exists at this temperature.")>] materialDescription: obj)
        : obj[,] =
        ExcelHelpers.withMaterial materialId (fun material ->
            selectCyclicStrainTable material temperatureC (Args.optionalTextOption materialDescription)
            |> Result.map (fun table -> ExcelHelpers.table1DToGrid table.HysteresisRangeTable))
        |> ExcelHelpers.ofGridResult

    // ── External pressure (material tables and Code Case 2964) ───────────
