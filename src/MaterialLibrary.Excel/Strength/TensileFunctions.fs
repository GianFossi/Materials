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

    /// <summary>
    /// Returns the union of all temperatures across the columns of a PropertyTable.
    /// Used to enumerate all temperatures when projecting a 2D Sy/Su table.
    /// </summary>
    let private allTemperatures (table: PropertyTable) =
        table.Columns
        |> List.collect (fun col -> col.Entries |> List.map (fun e -> e.X))
        |> List.distinct
        |> List.sort

    /// <summary>
    /// Selects the Sy or Su value at a given temperature from the first column that contains it,
    /// or interpolates within the single (or only) column if there is no exact match.
    /// When the table has multiple size-range columns and no size is specified, returns the
    /// value from the first column that covers the temperature, to preserve legacy behaviour.
    /// </summary>
    let private lookupStrengthAtTemp (table: PropertyTable) (tempC: float) : Result<float, MaterialError> =
        // Try exact match first across all columns.
        let exactMatch =
            table.Columns
            |> List.tryPick (fun col ->
                col.Entries
                |> List.tryFind (fun e -> e.X = tempC)
                |> Option.map (fun e -> e.Value))

        match exactMatch with
        | Some value -> Ok value
        | None ->
            // Fall back: interpolate within the first column that brackets the temperature.
            let bracketing =
                table.Columns
                |> List.tryPick (fun col ->
                    let entries = col.Entries
                    if entries.IsEmpty then None
                    else
                        let lo = (List.head entries).X
                        let hi = (List.last entries).X
                        if tempC >= lo && tempC <= hi then Some col else None)

            match bracketing with
            | None ->
                Error(MaterialError.InvalidOperation $"No Sy/Su data at temperature {tempC} degC")
            | Some col ->
                PropertyTable.lookup1D tempC { table with Columns = [ col ] }
                |> Result.map (fun r -> r.Value)

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Complete Sy table: temperature (degC), yield strength (MPa). Returns one column per size range when the material has size-dependent Sy.")>]
    let MatYieldStrengthTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        : obj[,] =
        ExcelHelpers.withMaterial materialId (fun material ->
            match material.StrengthProperties.SyTable with
            | None -> Error(MaterialError.InvalidOperation "No Sy table stored in material")
            | Some table -> Ok(ExcelHelpers.table1DToGrid table))
        |> ExcelHelpers.ofGridResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Interpolated yield strength Sy (MPa) at a given temperature (degC). Linear interpolation between tabulated values.")>]
    let MatYieldStrength
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Query temperature, degC.")>] temperatureC: float)
        : obj =
        ExcelHelpers.withMaterial materialId (fun material ->
            match material.StrengthProperties.SyTable with
            | None -> Error(MaterialError.InvalidOperation "No Sy table stored in material")
            | Some table -> lookupStrengthAtTemp table temperatureC)
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Complete Su table: temperature (degC), ultimate tensile strength (MPa). Returns one column per size range when the material has size-dependent Su.")>]
    let MatUltimateStrengthTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        : obj[,] =
        ExcelHelpers.withMaterial materialId (fun material ->
            match material.StrengthProperties.SuTable with
            | None -> Error(MaterialError.InvalidOperation "No Su table stored in material")
            | Some table -> Ok(ExcelHelpers.table1DToGrid table))
        |> ExcelHelpers.ofGridResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Interpolated ultimate tensile strength Su (MPa) at a given temperature (degC). Linear interpolation between tabulated values.")>]
    let MatUltimateStrength
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Query temperature, degC.")>] temperatureC: float)
        : obj =
        ExcelHelpers.withMaterial materialId (fun material ->
            match material.StrengthProperties.SuTable with
            | None -> Error(MaterialError.InvalidOperation "No Su table stored in material")
            | Some table -> lookupStrengthAtTemp table temperatureC)
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Combined Sy / Su summary table: temperature (degC), Sy (MPa), Su (MPa). Uses the first column of each size-ranged table when multiple columns exist.")>]
    let MatTensilePropertiesTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        : obj[,] =
        ExcelHelpers.withMaterial materialId (fun material ->
            let sp = material.StrengthProperties
            match sp.SyTable, sp.SuTable with
            | None, None -> Error(MaterialError.InvalidOperation "No Sy or Su table stored in material")
            | syOpt, suOpt ->
                let temps =
                    [ yield! syOpt |> Option.map allTemperatures |> Option.defaultValue []
                      yield! suOpt |> Option.map allTemperatures |> Option.defaultValue [] ]
                    |> List.distinct
                    |> List.sort

                let rows =
                    temps
                    |> List.map (fun t ->
                        let sy = syOpt |> Option.bind (fun table -> lookupStrengthAtTemp table t |> Result.toOption) |> Option.map box |> Option.defaultValue (box "")
                        let su = suOpt |> Option.bind (fun table -> lookupStrengthAtTemp table t |> Result.toOption) |> Option.map box |> Option.defaultValue (box "")
                        [ box t; sy; su ])

                Ok(ExcelHelpers.gridOfRows [ "Temperature"; "Sy"; "Su" ] rows))
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
