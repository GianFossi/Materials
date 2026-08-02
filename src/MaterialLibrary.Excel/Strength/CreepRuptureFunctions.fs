namespace MaterialLibrary.Excel

open System
open ExcelDna.Integration
open MaterialLibrary
open MaterialLibrary.Domain
open MaterialLibrary.Interpolation

/// <summary>
/// Creep-rupture and creep-strain-rate worksheet functions.
/// </summary>
/// <remarks>
/// Split out of the original single <c>StrengthPropertyFunctions</c> module. Excel-DNA
/// discovers worksheet functions from every public module, and the worksheet names come
/// from the <c>ExcelFunction</c> attributes, so the split does not change any formula.
/// </remarks>
module CreepRuptureFunctions =
    let private selectByReference
        (label: string)
        (referenceValue: float option)
        (formatReference: float -> string)
        (tables: (float * 'a) list)
        : Result<'a, MaterialError> =
        match referenceValue with
        | Some value ->
            match tables |> List.tryFind (fun (reference, _) -> reference = value) with
            | Some(_, table) -> Ok table
            | None ->
                Error(
                    MaterialError.InvalidOperation(sprintf "No %s table for reference %s" label (formatReference value))
                )
        | None ->
            match tables with
            | [ (_, only) ] -> Ok only
            | [] -> Error(MaterialError.InvalidOperation(sprintf "No %s tables stored for this material" label))
            | multiple ->
                let references = multiple |> List.map (fst >> formatReference) |> String.concat ", "

                Error(
                    MaterialError.InvalidOperation(
                        sprintf
                            "%d %s tables exist; pass a reference value to select one: %s"
                            (List.length multiple)
                            label
                            references
                    )
                )

    let private formatDuration (hours: float) = sprintf "%.4g h" hours
    let private formatCreepRate (rate: float) = sprintf "%.4g %%/1000h" rate

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Interpolated average creep-rupture stress (MPa) at a given temperature (degC), at a reference duration (hours). Omit referenceDurationHours when only one such table is stored.")>]
    let MatAverageCreepRuptureStress
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Query temperature, degC.")>] temperatureC: float)
        ([<ExcelArgument(Description = "Reference duration, hours (e.g. 100000); required when more than one table is stored.")>] referenceDurationHours: obj)
        : obj =
        ExcelHelpers.withMaterial materialId (fun material ->
            selectByReference
                "average creep-rupture"
                (Args.optionalNumberOption referenceDurationHours)
                formatDuration
                (material.StrengthProperties.AverageCreepRuptureStress |> List.map (fun t -> t.ReferenceDurationHours, t))
            |> Result.bind (fun table -> PropertyTable.lookup1D temperatureC table.Table)
            |> Result.map (fun result -> result.Value))
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Interpolated minimum creep-rupture stress (MPa) at a given temperature (degC), at a reference duration (hours). Omit referenceDurationHours when only one such table is stored.")>]
    let MatMinimumCreepRuptureStress
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Query temperature, degC.")>] temperatureC: float)
        ([<ExcelArgument(Description = "Reference duration, hours (e.g. 100000); required when more than one table is stored.")>] referenceDurationHours: obj)
        : obj =
        ExcelHelpers.withMaterial materialId (fun material ->
            selectByReference
                "minimum creep-rupture"
                (Args.optionalNumberOption referenceDurationHours)
                formatDuration
                (material.StrengthProperties.MinimumCreepRuptureStress |> List.map (fun t -> t.ReferenceDurationHours, t))
            |> Result.bind (fun table -> PropertyTable.lookup1D temperatureC table.Table)
            |> Result.map (fun result -> result.Value))
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Complete average creep-rupture table (temperature degC, stress MPa) at a reference duration (hours).")>]
    let MatAverageCreepRuptureStressTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Reference duration, hours (e.g. 100000); required when more than one table is stored.")>] referenceDurationHours: obj)
        : obj[,] =
        ExcelHelpers.withMaterial materialId (fun material ->
            selectByReference
                "average creep-rupture"
                (Args.optionalNumberOption referenceDurationHours)
                formatDuration
                (material.StrengthProperties.AverageCreepRuptureStress |> List.map (fun t -> t.ReferenceDurationHours, t))
            |> Result.map (fun table -> ExcelHelpers.table1DToGrid table.Table))
        |> ExcelHelpers.ofGridResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Complete minimum creep-rupture table (temperature degC, stress MPa) at a reference duration (hours).")>]
    let MatMinimumCreepRuptureStressTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Reference duration, hours (e.g. 100000); required when more than one table is stored.")>] referenceDurationHours: obj)
        : obj[,] =
        ExcelHelpers.withMaterial materialId (fun material ->
            selectByReference
                "minimum creep-rupture"
                (Args.optionalNumberOption referenceDurationHours)
                formatDuration
                (material.StrengthProperties.MinimumCreepRuptureStress |> List.map (fun t -> t.ReferenceDurationHours, t))
            |> Result.map (fun table -> ExcelHelpers.table1DToGrid table.Table))
        |> ExcelHelpers.ofGridResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Interpolated average stress (MPa) to reach a reference creep-rate criterion (%/1000h) at a given temperature (degC). Omit referenceCreepRate when only one such table is stored.")>]
    let MatAverageCreepStrainRateStress
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Query temperature, degC.")>] temperatureC: float)
        ([<ExcelArgument(Description = "Reference creep rate, %/1000h (e.g. 0.01); required when more than one table is stored.")>] referenceCreepRate: obj)
        : obj =
        ExcelHelpers.withMaterial materialId (fun material ->
            selectByReference
                "average creep strain-rate"
                (Args.optionalNumberOption referenceCreepRate)
                formatCreepRate
                (material.StrengthProperties.AverageCreepStrainRateStress
                 |> List.map (fun t -> t.ReferenceCreepRatePercentPer1000Hours, t))
            |> Result.bind (fun table -> PropertyTable.lookup1D temperatureC table.Table)
            |> Result.map (fun result -> result.Value))
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Interpolated minimum stress (MPa) to reach a reference creep-rate criterion (%/1000h) at a given temperature (degC). Omit referenceCreepRate when only one such table is stored.")>]
    let MatMinimumCreepStrainRateStress
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Query temperature, degC.")>] temperatureC: float)
        ([<ExcelArgument(Description = "Reference creep rate, %/1000h (e.g. 0.01); required when more than one table is stored.")>] referenceCreepRate: obj)
        : obj =
        ExcelHelpers.withMaterial materialId (fun material ->
            selectByReference
                "minimum creep strain-rate"
                (Args.optionalNumberOption referenceCreepRate)
                formatCreepRate
                (material.StrengthProperties.MinimumCreepStrainRateStress
                 |> List.map (fun t -> t.ReferenceCreepRatePercentPer1000Hours, t))
            |> Result.bind (fun table -> PropertyTable.lookup1D temperatureC table.Table)
            |> Result.map (fun result -> result.Value))
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Complete average creep strain-rate table (temperature degC, stress MPa) at a reference creep rate (%/1000h).")>]
    let MatAverageCreepStrainRateStressTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Reference creep rate, %/1000h (e.g. 0.01); required when more than one table is stored.")>] referenceCreepRate: obj)
        : obj[,] =
        ExcelHelpers.withMaterial materialId (fun material ->
            selectByReference
                "average creep strain-rate"
                (Args.optionalNumberOption referenceCreepRate)
                formatCreepRate
                (material.StrengthProperties.AverageCreepStrainRateStress
                 |> List.map (fun t -> t.ReferenceCreepRatePercentPer1000Hours, t))
            |> Result.map (fun table -> ExcelHelpers.table1DToGrid table.Table))
        |> ExcelHelpers.ofGridResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Complete minimum creep strain-rate table (temperature degC, stress MPa) at a reference creep rate (%/1000h).")>]
    let MatMinimumCreepStrainRateStressTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Reference creep rate, %/1000h (e.g. 0.01); required when more than one table is stored.")>] referenceCreepRate: obj)
        : obj[,] =
        ExcelHelpers.withMaterial materialId (fun material ->
            selectByReference
                "minimum creep strain-rate"
                (Args.optionalNumberOption referenceCreepRate)
                formatCreepRate
                (material.StrengthProperties.MinimumCreepStrainRateStress
                 |> List.map (fun t -> t.ReferenceCreepRatePercentPer1000Hours, t))
            |> Result.map (fun table -> ExcelHelpers.table1DToGrid table.Table))
        |> ExcelHelpers.ofGridResult

    // ── Stress rupture ─────────────────────────────────────────────────────

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Interpolated rupture stress (MPa) at a given time to rupture (hours) on the stress-rupture curve at an exact temperature (degC).")>]
    let MatStressRupture
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Curve temperature, degC (exact match).")>] temperatureC: float)
        ([<ExcelArgument(Description = "Query time to rupture, hours.")>] timeToRuptureHours: float)
        : obj =
        LibraryCache.current().GetStressFromStressRupture(materialId, temperatureC, timeToRuptureHours)
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Complete stress-rupture table (time to rupture hours, stress MPa) at an exact temperature (degC).")>]
    let MatStressRuptureTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Curve temperature, degC (exact match).")>] temperatureC: float)
        : obj[,] =
        ExcelHelpers.withMaterial materialId (fun material ->
            match
                material.StrengthProperties.StressRuptureCurves
                |> List.tryFind (fun t -> t.ReferenceTemperature = temperatureC)
            with
            | Some table -> Ok(ExcelHelpers.table1DToGrid table.Table)
            | None -> Error(MaterialError.InvalidOperation(sprintf "No stress-rupture curve at %.4g degC" temperatureC)))
        |> ExcelHelpers.ofGridResult

    // ── Fatigue (S-N curves) ───────────────────────────────────────────────

    let private parseFatigueMode (text: obj) : FatigueInterpolationMode =
        match (Args.optionalText "LogLog" text).Trim().ToLowerInvariant() with
        | "linear" -> FatigueLinear
        | "logcycle" -> FatigueLogCycle
        | _ -> FatigueLogLog

    let private selectFatigueTable
        (material: Material)
        (temperatureC: float)
        (durationHours: float option)
        : Result<FatigueTable, MaterialError> =
        match
            material.StrengthProperties.FatigueCurves
            |> List.filter (fun t ->
                t.ReferenceTemperature = temperatureC
                && (durationHours.IsNone || t.ReferenceDurationHours = durationHours))
        with
        | [ only ] -> Ok only
        | [] -> Error(MaterialError.InvalidOperation(sprintf "No fatigue table at %.4g degC" temperatureC))
        | multiple ->
            Error(
                MaterialError.InvalidOperation(
                    sprintf
                        "%d fatigue tables at %.4g degC; pass referenceDurationHours to select one"
                        (List.length multiple)
                        temperatureC
                )
            )

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Interpolated fatigue stress amplitude Sa (MPa) at a given cycle count and temperature (degC). mode: LogLog (default, standard S-N presentation), LogCycle, Linear.")>]
    let MatFatigueStressAmplitudeFromCycles
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Curve temperature, degC (exact match).")>] temperatureC: float)
        ([<ExcelArgument(Description = "Query number of cycles.")>] cycles: float)
        ([<ExcelArgument(Description = "Reference duration, hours, when more than one fatigue table exists at this temperature.")>] durationHours: obj)
        ([<ExcelArgument(Description = "Fatigue interpolation mode: LogLog (default), LogCycle, Linear.")>] mode: obj)
        : obj =
        ExcelHelpers.withMaterial materialId (fun material ->
            selectFatigueTable material temperatureC (Args.optionalNumberOption durationHours)
            |> Result.bind (fun table ->
                FatigueInterpolation.stressAmplitudeFromCycles (parseFatigueMode mode) cycles table
                |> Result.mapError MaterialError.InterpolationError))
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Cycle count at a given fatigue stress amplitude Sa (MPa) and temperature (degC). mode: LogLog (default), LogCycle, Linear.")>]
    let MatCyclesFromFatigueStressAmplitude
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Curve temperature, degC (exact match).")>] temperatureC: float)
        ([<ExcelArgument(Description = "Query stress amplitude, MPa.")>] stressAmplitudeMPa: float)
        ([<ExcelArgument(Description = "Reference duration, hours, when more than one fatigue table exists at this temperature.")>] durationHours: obj)
        ([<ExcelArgument(Description = "Fatigue interpolation mode: LogLog (default), LogCycle, Linear.")>] mode: obj)
        : obj =
        ExcelHelpers.withMaterial materialId (fun material ->
            selectFatigueTable material temperatureC (Args.optionalNumberOption durationHours)
            |> Result.bind (fun table ->
                FatigueInterpolation.cyclesFromStressAmplitude (parseFatigueMode mode) stressAmplitudeMPa table
                |> Result.mapError MaterialError.InterpolationError))
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Complete fatigue S-N table (cycles, stress amplitude Sa MPa) at a given temperature (degC).")>]
    let MatFatigueTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Curve temperature, degC (exact match).")>] temperatureC: float)
        ([<ExcelArgument(Description = "Reference duration, hours, when more than one fatigue table exists at this temperature.")>] durationHours: obj)
        : obj[,] =
        ExcelHelpers.withMaterial materialId (fun material ->
            selectFatigueTable material temperatureC (Args.optionalNumberOption durationHours)
            |> Result.map (fun table -> ExcelHelpers.table1DToGrid table.Table))
        |> ExcelHelpers.ofGridResult

    // ── Larson-Miller master curves ────────────────────────────────────────

    let private selectLarsonMillerCurve
        (material: Material)
        (curveName: string option)
        : Result<LarsonMillerCurve, MaterialError> =
        match curveName with
        | Some name ->
            material.StrengthProperties.LarsonMillerCurves
            |> List.tryFind (fun c -> String.Equals(c.Material, name, StringComparison.OrdinalIgnoreCase))
            |> function
                | Some curve -> Ok curve
                | None -> Error(MaterialError.InvalidOperation(sprintf "No Larson-Miller curve named \"%s\"" name))
        | None ->
            match material.StrengthProperties.LarsonMillerCurves with
            | [ only ] -> Ok only
            | [] -> Error(MaterialError.InvalidOperation "No Larson-Miller curves stored for this material")
            | multiple ->
                let names = multiple |> List.map (fun c -> c.Material) |> String.concat ", "

                Error(
                    MaterialError.InvalidOperation(
                        sprintf "%d Larson-Miller curves stored; pass curveName to select one: %s" (List.length multiple) names
                    )
                )

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Lists the Larson-Miller curves stored for this material (name, description, point count).")>]
    let MatLarsonMillerCurveNames
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        : obj[,] =
        ExcelHelpers.withMaterial materialId (fun material -> Ok material.StrengthProperties.LarsonMillerCurves)
        |> Result.map (fun curves ->
            curves
            |> List.map (fun c -> [ box c.Material; box c.Description; box (List.length c.Points) ])
            |> ExcelHelpers.gridOfRows [ "Material"; "Description"; "PointCount" ])
        |> ExcelHelpers.ofGridResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Interpolated rupture stress (MPa) at a given Larson-Miller parameter P.")>]
    let MatLarsonMillerStress
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Query Larson-Miller parameter P.")>] parameterP: float)
        ([<ExcelArgument(Description = "Curve name, from MatLarsonMillerCurveNames; required when more than one curve is stored.")>] curveName: obj)
        : obj =
        ExcelHelpers.withMaterial materialId (fun material ->
            selectLarsonMillerCurve material (Args.optionalTextOption curveName)
            |> Result.bind (fun curve ->
                let points =
                    curve.Points |> List.map (fun p -> p.LarsonMillerParameter, p.Stress)

                AdHocTable.interpolate "LarsonMiller" "LarsonMillerParameter" "" "Stress" "MPa" points parameterP))
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Complete Larson-Miller table (parameter P, stress MPa).")>]
    let MatLarsonMillerTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Curve name, from MatLarsonMillerCurveNames; required when more than one curve is stored.")>] curveName: obj)
        : obj[,] =
        ExcelHelpers.withMaterial materialId (fun material ->
            selectLarsonMillerCurve material (Args.optionalTextOption curveName)
            |> Result.map (fun curve ->
                curve.Points
                |> List.sortBy (fun p -> p.LarsonMillerParameter)
                |> List.map (fun p -> [ box p.LarsonMillerParameter; box p.Stress ])
                |> ExcelHelpers.gridOfRows [ "LarsonMillerParameter"; "Stress" ]))
        |> ExcelHelpers.ofGridResult

    // ── Code Case 2964 ──────────────────────────────────────────────────────

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Stored Code Case 2964 Appendix III constants (temperature, A0..A4, B0..B4, notes) for this material.")>]
    let MatCodeCase2964AppendixIIIConstantsTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        : obj[,] =
        LibraryCache.current().GetCodeCase2964AppendixIIIConstants materialId
        |> Result.map (fun rows ->
            rows
            |> List.sortBy (fun r -> r.Temperature)
            |> List.map (fun r ->
                [ box r.Temperature
                  box r.A0; box r.A1; box r.A2; box r.A3; box r.A4
                  box r.B0; box r.B1; box r.B2; box r.B3; box r.B4
                  box (defaultArg r.Notes "") ])
            |> ExcelHelpers.gridOfRows
                [ "Temperature"; "A0"; "A1"; "A2"; "A3"; "A4"; "B0"; "B1"; "B2"; "B3"; "B4"; "Notes" ])
        |> ExcelHelpers.ofGridResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Stored Code Case 2964 Appendix III factor rule (material family, temperature limit degF, m2 coefficient, eps'p, notes) for this material.")>]
    let MatCodeCase2964FactorRuleTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        : obj[,] =
        LibraryCache.current().GetCodeCase2964AppendixIIIFactorRule materialId
        |> Result.bind (function
            | None -> Error(MaterialError.InvalidOperation "No Code Case 2964 Appendix III factor rule stored")
            | Some rule ->
                Ok(
                    ExcelHelpers.gridOfRows
                        [ "MaterialFamily"; "TemperatureLimitF"; "M2Coefficient"; "EpsPrimeP"; "Notes" ]
                        [ [ box (sprintf "%A" rule.MaterialFamily)
                            box rule.TemperatureLimitF
                            box rule.M2Coefficient
                            box rule.EpsPrimeP
                            box (defaultArg rule.Notes "") ] ]
                ))
        |> ExcelHelpers.ofGridResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Evaluates the stored Code Case 2964 Appendix III factor rule at an assessment temperature (degC): strength ratio R, m2, eps'p.")>]
    let MatCodeCase2964EvaluatedFactors
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Assessment temperature, degC.")>] temperatureC: float)
        : obj[,] =
        LibraryCache.current().GetCodeCase2964EvaluatedFactorValues(materialId, temperatureC)
        |> Result.map (fun values ->
            ExcelHelpers.gridOfRows
                [ "Temperature"; "TemperatureF"; "StrengthRatioR"; "M2"; "EpsPrimeP"; "MaterialFamily"; "StrengthRatioSource" ]
                [ [ box values.Temperature
                    box values.TemperatureF
                    box values.StrengthRatioR
                    box values.M2
                    box values.EpsPrimeP
                    box (sprintf "%A" values.MaterialFamily)
                    box values.StrengthRatioSource ] ])
        |> ExcelHelpers.ofGridResult
