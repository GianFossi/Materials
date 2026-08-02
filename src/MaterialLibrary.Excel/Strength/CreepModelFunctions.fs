namespace MaterialLibrary.Excel

open System
open ExcelDna.Integration
open MaterialLibrary
open MaterialLibrary.Domain
open MaterialLibrary.Interpolation

/// <summary>
/// Creep worksheet functions: experimental curves and the stored Norton, Garofalo, and Kachanov models.
/// </summary>
/// <remarks>
/// Split out of the original single <c>StrengthPropertyFunctions</c> module. Excel-DNA
/// discovers worksheet functions from every public module, and the worksheet names come
/// from the <c>ExcelFunction</c> attributes, so the split does not change any formula.
/// </remarks>
module CreepModelFunctions =
    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Interpolated creep strain (%) at a given time (hours) on the experimental creep curve matching the applied stress (MPa).")>]
    let MatCreepStrainFromCurve
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Applied stress, MPa (exact match to a stored curve).")>] appliedStressMPa: float)
        ([<ExcelArgument(Description = "Query elapsed time, hours.")>] timeHours: float)
        : obj =
        LibraryCache.current().GetCreepStrainFromCurve(materialId, appliedStressMPa, timeHours)
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Complete experimental creep table (time hours, strain %) for the curve matching the applied stress (MPa).")>]
    let MatCreepTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Applied stress, MPa (exact match to a stored curve).")>] appliedStressMPa: float)
        : obj[,] =
        ExcelHelpers.withMaterial materialId (fun material ->
            match
                material.StrengthProperties.CreepTables
                |> List.tryFind (fun t -> t.AppliedStress = Some appliedStressMPa)
            with
            | Some table -> Ok(ExcelHelpers.table1DToGrid table.Table)
            | None -> Error(MaterialError.InvalidOperation(sprintf "No creep curve for %.4g MPa" appliedStressMPa)))
        |> ExcelHelpers.ofGridResult

    // ── Creep: stored analytical models (Norton, Garofalo, Kachanov-Omega) ────

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Stored Norton Power Law creep-model coefficients (temperature, A, n, m) for this material.")>]
    let MatNortonModelsTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        : obj[,] =
        ExcelHelpers.withMaterial materialId (fun material -> Ok material.StrengthProperties.NortonModels)
        |> Result.map (fun rows ->
            rows
            |> List.sortBy (fun r -> r.Temperature)
            |> List.map (fun r -> [ box r.Temperature; box r.A; box r.N; box r.M ])
            |> ExcelHelpers.gridOfRows [ "Temperature"; "A"; "n"; "m" ])
        |> ExcelHelpers.ofGridResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Creep strain (%) from the Norton Power Law model stored for this material at an exact temperature (degC).")>]
    let MatNortonCreepStrainAtTemperature
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Temperature, degC (exact match to a stored model).")>] temperatureC: float)
        ([<ExcelArgument(Description = "Applied stress, MPa.")>] sigma: float)
        ([<ExcelArgument(Description = "Elapsed time, hours.")>] timeHours: float)
        : obj =
        ExcelHelpers.withMaterial materialId (fun material ->
            selectByTemperature "Norton" temperatureC (material.StrengthProperties.NortonModels |> List.map (fun m -> m.Temperature, m))
            |> Result.bind (fun m -> NortonPowerLaw.creepStrain m.A m.N m.M sigma timeHours))
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Stored Garofalo creep-model coefficients (temperature, A, n, m, alpha, Q) for this material.")>]
    let MatGarofaloModelsTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        : obj[,] =
        ExcelHelpers.withMaterial materialId (fun material -> Ok material.StrengthProperties.GarofaloModels)
        |> Result.map (fun rows ->
            rows
            |> List.sortBy (fun r -> r.Temperature)
            |> List.map (fun r -> [ box r.Temperature; box r.A; box r.N; box r.M; box r.Alpha; box r.Q ])
            |> ExcelHelpers.gridOfRows [ "Temperature"; "A"; "n"; "m"; "alpha"; "Q" ])
        |> ExcelHelpers.ofGridResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Creep strain (%) from the Garofalo model stored for this material at an exact temperature (degC), including the Arrhenius activation-energy term.")>]
    let MatGarofaloCreepStrainAtTemperature
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Temperature, degC (exact match to a stored model).")>] temperatureC: float)
        ([<ExcelArgument(Description = "Applied stress, MPa.")>] sigma: float)
        ([<ExcelArgument(Description = "Elapsed time, hours.")>] timeHours: float)
        : obj =
        ExcelHelpers.withMaterial materialId (fun material ->
            selectByTemperature "Garofalo" temperatureC (material.StrengthProperties.GarofaloModels |> List.map (fun m -> m.Temperature, m))
            |> Result.bind (fun m ->
                GarofaloModel.creepStrainWithActivationEnergy m.A m.N m.M m.Alpha m.Q temperatureC sigma timeHours))
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Stored Kachanov-Omega creep-damage model coefficients (temperature, A1, N1, M1, A2, N2, M2, description) for this material.")>]
    let MatKachanovModelsTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        : obj[,] =
        ExcelHelpers.withMaterial materialId (fun material -> Ok material.StrengthProperties.KachanovOmegaModels)
        |> Result.map (fun rows ->
            rows
            |> List.sortBy (fun r -> r.Temperature)
            |> List.map (fun r ->
                [ box r.Temperature; box r.A1; box r.N1; box r.M1; box r.A2; box r.N2; box r.M2; box r.Description ])
            |> ExcelHelpers.gridOfRows [ "Temperature"; "A1"; "N1"; "M1"; "A2"; "N2"; "M2"; "Description" ])
        |> ExcelHelpers.ofGridResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Final cumulative creep strain (%) at totalTimeHours from the Kachanov-Omega model stored for this material at an exact temperature (degC), integrated with timeSteps explicit-Euler steps.")>]
    let MatKachanovCreepStrainAtTemperature
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Temperature, degC (exact match to a stored model).")>] temperatureC: float)
        ([<ExcelArgument(Description = "Applied stress, MPa.")>] sigma: float)
        ([<ExcelArgument(Description = "Number of explicit-Euler integration steps (default 100).")>] timeSteps: obj)
        ([<ExcelArgument(Description = "Total simulation time, hours.")>] totalTimeHours: float)
        : obj =
        ExcelHelpers.withMaterial materialId (fun material ->
            selectByTemperature "Kachanov-Omega" temperatureC (material.StrengthProperties.KachanovOmegaModels |> List.map (fun m -> m.Temperature, m))
            |> Result.bind (fun m ->
                KachanovOmega.creepStrainWithDamage
                    m.A1
                    m.N1
                    m.M1
                    m.A2
                    m.N2
                    m.M2
                    sigma
                    (Args.optionalNumber 100.0 timeSteps |> int)
                    totalTimeHours)
            |> Result.map List.last)
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Full cumulative creep-strain history (time hours, strain %) from the Kachanov-Omega model stored for this material at an exact temperature (degC).")>]
    let MatKachanovCreepStrainHistory
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Temperature, degC (exact match to a stored model).")>] temperatureC: float)
        ([<ExcelArgument(Description = "Applied stress, MPa.")>] sigma: float)
        ([<ExcelArgument(Description = "Number of explicit-Euler integration steps (default 100).")>] timeSteps: obj)
        ([<ExcelArgument(Description = "Total simulation time, hours.")>] totalTimeHours: float)
        : obj[,] =
        let steps = Args.optionalNumber 100.0 timeSteps |> int

        ExcelHelpers.withMaterial materialId (fun material ->
            selectByTemperature "Kachanov-Omega" temperatureC (material.StrengthProperties.KachanovOmegaModels |> List.map (fun m -> m.Temperature, m))
            |> Result.bind (fun m ->
                KachanovOmega.creepStrainWithDamage m.A1 m.N1 m.M1 m.A2 m.N2 m.M2 sigma steps totalTimeHours)
            |> Result.map (fun strains ->
                strains
                |> List.mapi (fun i strain -> [ box (totalTimeHours * float i / float steps); box strain ])
                |> ExcelHelpers.gridOfRows [ "Time"; "Strain" ]))
        |> ExcelHelpers.ofGridResult

    // ── Creep: reference stress tables (rupture at fixed duration, stress at fixed creep rate) ──
