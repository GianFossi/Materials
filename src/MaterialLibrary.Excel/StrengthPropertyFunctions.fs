namespace MaterialLibrary.Excel

open System
open ExcelDna.Integration
open MaterialLibrary
open MaterialLibrary.Domain
open MaterialLibrary.Interpolation

/// <summary>
/// Excel worksheet functions for the temperature/size/time/cycle-dependent strength properties
/// stored under <c>Material.StrengthProperties</c>: tensile/compression strength, allowable stress,
/// stress-strain curves, cyclic strain-strain data, external-pressure charts, creep (experimental
/// curves and stored analytical models), stress-rupture, fatigue, Larson-Miller, and Code Case 2964.
/// </summary>
module StrengthPropertyFunctions =

    let private selectByTemperature
        (label: string)
        (temperature: float)
        (rows: (float * 'a) list)
        : Result<'a, MaterialError> =
        match rows |> List.tryFind (fun (t, _) -> t = temperature) with
        | Some(_, row) -> Ok row
        | None ->
            let available =
                rows |> List.map fst |> List.sort |> List.map (sprintf "%.4g") |> String.concat ", "

            Error(
                MaterialError.InvalidOperation(
                    sprintf "No stored %s data at %.4g degC; available: %s" label temperature available
                )
            )

    // ── Tensile / compression properties vs temperature ──────────────────────

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Complete tensile-properties table: temperature (degC), yield strength, tensile strength (MPa), elongation %, reduction of area %.")>]
    let MatTensilePropertiesTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        : obj[,] =
        ExcelHelpers.withMaterial materialId (fun material -> Ok material.StrengthProperties.TensileProperties)
        |> Result.map (fun rows ->
            rows
            |> List.sortBy (fun r -> r.Temperature)
            |> List.map (fun r ->
                [ box r.Temperature
                  box r.YieldStrength
                  box r.TensileStrength
                  box r.ElongationPercent
                  box r.ReductionOfAreaPercent ])
            |> ExcelHelpers.gridOfRows
                [ "Temperature_degC"; "YieldStrength_MPa"; "TensileStrength_MPa"; "Elongation_pct"; "ReductionOfArea_pct" ])
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
            |> ExcelHelpers.gridOfRows [ "Temperature_degC"; "CompressiveStrength_MPa"; "CompressiveYield_MPa" ])
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

    let private parseAllowableStressSource (text: string option) : AllowableStressSource =
        match text |> Option.map (fun s -> s.Trim().ToUpperInvariant()) with
        | Some("S1H" | "DIVISION1HIGH" | "HIGH") -> Division1HighAllowableStress
        | Some("S2" | "DIVISION2") -> Division2AllowableStress
        | Some("BOLTING" | "S3") -> BoltingAllowableStress
        | _ -> Division1AllowableStress

    let private containsSize (size: float) (dataset: AllowableStressDataset) : bool =
        (dataset.SizeMinimum |> Option.forall (fun lo -> size >= lo))
        && (dataset.SizeMaximum |> Option.forall (fun hi -> size <= hi))

    let private selectAllowableStressDataset
        (material: Material)
        (source: AllowableStressSource)
        (sizeMm: float option)
        : Result<AllowableStressDataset, MaterialError> =
        match material.StrengthProperties.AllowableStressDatasets |> List.filter (fun d -> d.Source = source) with
        | [] -> Error(MaterialError.InvalidOperation(sprintf "No allowable-stress dataset for source %A" source))
        | candidates ->
            match sizeMm with
            | Some size ->
                match candidates |> List.tryFind (containsSize size) with
                | Some dataset -> Ok dataset
                | None ->
                    Error(
                        MaterialError.InvalidOperation(
                            sprintf "No %A allowable-stress dataset covers size %.3f mm" source size
                        )
                    )
            | None ->
                match candidates with
                | [ only ] -> Ok only
                | multiple ->
                    Error(
                        MaterialError.InvalidOperation(
                            sprintf
                                "%d %A allowable-stress datasets exist; pass sizeMm to select one"
                                (List.length multiple)
                                source
                        )
                    )

    let private allowableStressSourceLabel (dataset: AllowableStressDataset) : string =
        match dataset.SizeMinimum, dataset.SizeMaximum with
        | None, None -> "all sizes"
        | Some lo, None -> sprintf ">= %.3f mm" lo
        | None, Some hi -> sprintf "<= %.3f mm" hi
        | Some lo, Some hi -> sprintf "%.3f - %.3f mm" lo hi

    let private allowableStressSourceGrid (datasets: AllowableStressDataset list) : obj[,] =
        let xs =
            datasets
            |> List.collect (fun d -> d.Table.Columns |> List.collect (fun c -> c.Entries))
            |> List.map (fun e -> e.X)
            |> List.distinct
            |> List.sort

        let rows =
            xs
            |> List.map (fun x ->
                let cells =
                    datasets
                    |> List.map (fun d ->
                        d.Table.Columns
                        |> List.collect (fun c -> c.Entries)
                        |> List.tryFind (fun e -> e.X = x)
                        |> Option.map (fun e -> box e.Value)
                        |> Option.defaultValue (box ""))

                box x :: cells)

        ExcelHelpers.gridOfRows ("Temperature_degC" :: (datasets |> List.map allowableStressSourceLabel)) rows

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Interpolated allowable stress (MPa) at a given temperature (degC) and size (mm). source: \"S1\" (default, ASME I/VIII-1), \"S1H\" (high strength), \"S2\" (VIII-2), \"Bolting\".")>]
    let MatAllowableStress
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Query temperature, degC.")>] temperatureC: float)
        ([<ExcelArgument(Description = "Nominal size/thickness, mm. Required whenever more than one size range exists for the chosen source.")>] sizeMm: obj)
        ([<ExcelArgument(Description = "Allowable-stress source: S1 (default), S1H, S2, Bolting.")>] source: obj)
        : obj =
        ExcelHelpers.withMaterial materialId (fun material ->
            selectAllowableStressDataset material (parseAllowableStressSource (Args.optionalTextOption source)) (Args.optionalNumberOption sizeMm)
            |> Result.bind (fun dataset -> PropertyTable.lookup1D temperatureC dataset.Table)
            |> Result.map (fun result -> result.Value))
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Complete allowable-stress table for one source. Pass sizeMm to see only the size range that covers it; leave blank to see every size range side by side.")>]
    let MatAllowableStressTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Allowable-stress source: S1 (default), S1H, S2, Bolting.")>] source: obj)
        ([<ExcelArgument(Description = "Optional nominal size/thickness, mm, to show only its size range.")>] sizeMm: obj)
        : obj[,] =
        let result =
            ExcelHelpers.withMaterial materialId (fun material ->
                let sourceValue = parseAllowableStressSource (Args.optionalTextOption source)

                match Args.optionalNumberOption sizeMm with
                | Some size ->
                    selectAllowableStressDataset material sourceValue (Some size)
                    |> Result.map (fun dataset -> ExcelHelpers.table1DToGrid dataset.Table)
                | None ->
                    match material.StrengthProperties.AllowableStressDatasets |> List.filter (fun d -> d.Source = sourceValue) with
                    | [] -> Error(MaterialError.InvalidOperation(sprintf "No allowable-stress dataset for source %A" sourceValue))
                    | datasets -> Ok(allowableStressSourceGrid datasets))

        ExcelHelpers.ofGridResult result

    // ── Stress-strain curves ──────────────────────────────────────────────

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
            |> ExcelHelpers.gridOfRows [ "Temperature_degC"; "A"; "n"; "m" ])
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
            |> ExcelHelpers.gridOfRows [ "Temperature_degC"; "A"; "n"; "m"; "alpha_per_MPa"; "Q_J_mol" ])
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
            |> ExcelHelpers.gridOfRows [ "Temperature_degC"; "A1"; "N1"; "M1"; "A2"; "N2"; "M2"; "Description" ])
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
                |> ExcelHelpers.gridOfRows [ "Time_hours"; "Strain_pct" ]))
        |> ExcelHelpers.ofGridResult

    // ── Creep: reference stress tables (rupture at fixed duration, stress at fixed creep rate) ──

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
                |> ExcelHelpers.gridOfRows [ "LarsonMillerParameter"; "Stress_MPa" ]))
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
                [ "Temperature_degC"; "A0"; "A1"; "A2"; "A3"; "A4"; "B0"; "B1"; "B2"; "B3"; "B4"; "Notes" ])
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
                [ "Temperature_degC"; "TemperatureF"; "StrengthRatioR"; "M2"; "EpsPrimeP"; "MaterialFamily"; "StrengthRatioSource" ]
                [ [ box values.Temperature
                    box values.TemperatureF
                    box values.StrengthRatioR
                    box values.M2
                    box values.EpsPrimeP
                    box (sprintf "%A" values.MaterialFamily)
                    box values.StrengthRatioSource ] ])
        |> ExcelHelpers.ofGridResult
