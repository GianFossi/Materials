namespace MaterialLibrary.Excel

open System
open ExcelDna.Integration
open MaterialLibrary
open MaterialLibrary.Domain
open MaterialLibrary.Interpolation

/// <summary>
/// Stress-strain curve worksheet functions.
/// </summary>
/// <remarks>
/// Split out of the original single <c>StrengthPropertyFunctions</c> module. Excel-DNA
/// discovers worksheet functions from every public module, and the worksheet names come
/// from the <c>ExcelFunction</c> attributes, so the split does not change any formula.
/// </remarks>
module StressStrainFunctions =
    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Interpolated stress (MPa) at a given strain (%) on the stress-strain curve at the given temperature. Pass durationHours for the isochronous curve at that duration; leave blank for the time-independent curve.")>]
    let MatStressFromStrain
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Curve temperature, degC (exact match).")>] temperatureC: float)
        ([<ExcelArgument(Description = "Query engineering strain, %.")>] strainPercent: float)
        ([<ExcelArgument(Description = "Isochronous reference duration, hours; blank selects the time-independent curve.")>] durationHours: obj)
        : obj =
        let library = LibraryCache.current ()

        match Args.optionalNumberOption durationHours with
        | Some duration -> library.GetStressFromStrainAtDuration(materialId, temperatureC, duration, strainPercent)
        | None -> library.GetStressFromStrain(materialId, temperatureC, strainPercent)
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Complete stress-strain table (strain %, stress MPa) at the given temperature. Pass durationHours for the isochronous curve at that duration; leave blank for the time-independent curve.")>]
    let MatStressStrainTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Curve temperature, degC (exact match).")>] temperatureC: float)
        ([<ExcelArgument(Description = "Isochronous reference duration, hours; blank selects the time-independent curve.")>] durationHours: obj)
        : obj[,] =
        let durationOption = Args.optionalNumberOption durationHours

        ExcelHelpers.withMaterial materialId (fun material ->
            match
                material.StrengthProperties.StressStrainTables
                |> List.tryFind (fun t -> t.ReferenceTemperature = temperatureC && t.ReferenceDurationHours = durationOption)
            with
            | Some table -> Ok(ExcelHelpers.table1DToGrid table.Table)
            | None ->
                Error(
                    MaterialError.InvalidOperation(
                        sprintf "No stress-strain table at %.4g degC and duration %A" temperatureC durationOption
                    )
                ))
        |> ExcelHelpers.ofGridResult

    // ── Cyclic strain-strain (ASME VIII-2 Annex 3-D) ──────────────────────
