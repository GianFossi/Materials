namespace MaterialLibrary.Excel

open System
open ExcelDna.Integration
open MaterialLibrary
open MaterialLibrary.Domain
open MaterialLibrary.Interpolation

/// <summary>
/// External-pressure chart worksheet functions, including Code Case 2964 generation.
/// </summary>
/// <remarks>
/// Split out of the original single <c>StrengthPropertyFunctions</c> module. Excel-DNA
/// discovers worksheet functions from every public module, and the worksheet names come
/// from the <c>ExcelFunction</c> attributes, so the split does not change any formula.
/// </remarks>
module ExternalPressureFunctions =
    let private selectExternalPressureTable
        (material: Material)
        (temperatureC: float)
        (durationHours: float option)
        : Result<ExternalPressureTable, MaterialError> =
        match
            material.StrengthProperties.ExternalPressureTables
            |> List.filter (fun t -> t.ReferenceTemperature = temperatureC && t.ReferenceDurationHours = durationHours)
        with
        | [ table ] -> Ok table
        | [] ->
            Error(
                MaterialError.InvalidOperation(
                    sprintf "No external-pressure table at %.4g degC and duration %A" temperatureC durationHours
                )
            )
        | _ ->
            Error(
                MaterialError.InvalidOperation(
                    sprintf "Multiple external-pressure tables match %.4g degC and duration %A" temperatureC durationHours
                )
            )

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Interpolated allowable compressive stress Sc (MPa) at a given Factor A, temperature (degC), and optional duration (hours).")>]
    let MatExternalPressureStress
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Chart temperature, degC (exact match).")>] temperatureC: float)
        ([<ExcelArgument(Description = "Reference duration, hours; blank selects time-independent data.")>] durationHours: obj)
        ([<ExcelArgument(Description = "Query Factor A (dimensionless).")>] factorA: float)
        ([<ExcelArgument(Description = "Interpolation mode: Linear (default), CubicSpline, Constant, Lagrange.")>] mode: obj)
        ([<ExcelArgument(Description = "Lagrange polynomial degree, used only when mode is Lagrange (default 3).")>] lagrangeDegree: obj)
        : obj =
        LibraryCache
            .current()
            .GetExternalPressureAllowableCompressiveStress(
                materialId,
                temperatureC,
                Args.optionalNumberOption durationHours,
                factorA,
                Args.interpolationMode mode lagrangeDegree
            )
        |> Result.map (fun lookup -> lookup.Value)
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Complete external-pressure table (Factor A, allowable compressive stress MPa) at a given temperature and optional duration (hours).")>]
    let MatExternalPressureTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Chart temperature, degC (exact match).")>] temperatureC: float)
        ([<ExcelArgument(Description = "Reference duration, hours; blank selects time-independent data.")>] durationHours: obj)
        : obj[,] =
        ExcelHelpers.withMaterial materialId (fun material ->
            selectExternalPressureTable material temperatureC (Args.optionalNumberOption durationHours)
            |> Result.map (fun table -> ExcelHelpers.table1DToGrid table.Table))
        |> ExcelHelpers.ofGridResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Generates a Code Case 2964 A-vs-Sc external-pressure table (Factor A, MPa) from the material's stored Appendix III inputs, without storing it.")>]
    let MatCodeCase2964GeneratedExternalPressureTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Assessment/generation temperature, degC.")>] temperatureC: float)
        ([<ExcelArgument(Description = "Reference duration, hours; blank generates time-independent data.")>] durationHours: obj)
        ([<ExcelArgument(Description = "Chart description.")>] description: string)
        ([<ExcelArgument(Description = "Number of generated points (minimum 2, default 12).")>] pointCount: obj)
        : obj[,] =
        LibraryCache
            .current()
            .GenerateExternalPressureTableFromStoredCodeCase2964Inputs(
                materialId,
                temperatureC,
                Args.optionalNumberOption durationHours,
                description,
                Args.optionalNumber 12.0 pointCount |> int
            )
        |> Result.map (fun table -> ExcelHelpers.table1DToGrid table.Table)
        |> ExcelHelpers.ofGridResult

    // ── Creep: experimental curves ─────────────────────────────────────────
