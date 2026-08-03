namespace MaterialLibrary.Excel

open System
open ExcelDna.Integration
open MaterialLibrary
open MaterialLibrary.Domain
open MaterialLibrary.Interpolation

/// <summary>
/// Allowable-stress worksheet functions and ASME source selection.
/// </summary>
/// <remarks>
/// Split out of the original single <c>StrengthPropertyFunctions</c> module. Excel-DNA
/// discovers worksheet functions from every public module, and the worksheet names come
/// from the <c>ExcelFunction</c> attributes, so the split does not change any formula.
/// </remarks>
module AllowableStressFunctions =
    let private parseAllowableStressSource (text: string option) : AllowableStressSource =
        match text |> Option.map (fun s -> s.Trim().ToUpperInvariant()) with
        | Some("S1H" | "DIVISION1HIGH" | "HIGH") -> Division1HighAllowableStress
        | Some("S2" | "DIVISION2") -> Division2AllowableStress
        | Some("BOLTING" | "S3") -> BoltingAllowableStress
        | _ -> Division1AllowableStress

    let private containsSize (size: float) (dataset: AllowableStressDataset) : bool =
        (dataset.SizeMinimum |> Option.forall (fun lo -> size >= lo))
        && (dataset.SizeMaximum |> Option.forall (fun hi -> size <= hi))

    let private effectiveAllowableStressSource (material: Material) (source: AllowableStressSource) =
        let isBoltingOnlyMaterial =
            material.StrengthProperties.AllowableStressDatasets
            |> List.exists (fun d -> d.Source = BoltingAllowableStress)
            && material.StrengthProperties.AllowableStressDatasets
               |> List.forall (fun d -> d.Source = BoltingAllowableStress)

        match source with
        | Division1AllowableStress
        | Division1HighAllowableStress
        | Division2AllowableStress when isBoltingOnlyMaterial -> BoltingAllowableStress
        | _ -> source

    let private allowableStressDatasetsForSource (material: Material) (source: AllowableStressSource) =
        let effectiveSource = effectiveAllowableStressSource material source

        material.StrengthProperties.AllowableStressDatasets
        |> List.filter (fun d -> d.Source = effectiveSource)

    let private selectAllowableStressDataset
        (material: Material)
        (source: AllowableStressSource)
        (sizeMm: float option)
        : Result<AllowableStressDataset, MaterialError> =
        let effectiveSource = effectiveAllowableStressSource material source

        match allowableStressDatasetsForSource material source with
        | [] -> Error(MaterialError.InvalidOperation(sprintf "No allowable-stress dataset for source %A" source))
        | candidates ->
            match sizeMm with
            | Some size ->
                match candidates |> List.tryFind (containsSize size) with
                | Some dataset -> Ok dataset
                | None ->
                    Error(
                        MaterialError.InvalidOperation(
                            sprintf "No %A allowable-stress dataset covers size %.3f mm" effectiveSource size
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

    let private allowableStressSizeSortKey (dataset: AllowableStressDataset) =
        let lower = dataset.SizeMinimum |> Option.defaultValue Double.NegativeInfinity
        let upper = dataset.SizeMaximum |> Option.defaultValue Double.PositiveInfinity
        lower, upper, dataset.DatabaseRowId

    let private allowableStressSourceGrid (datasets: AllowableStressDataset list) : obj[,] =
        let orderedDatasets = datasets |> List.sortBy allowableStressSizeSortKey

        let xs =
            orderedDatasets
            |> List.collect (fun d -> d.Table.Columns |> List.collect (fun c -> c.Entries))
            |> List.map (fun e -> e.X)
            |> List.distinct
            |> List.sort

        let rows =
            xs
            |> List.map (fun x ->
                let cells =
                    orderedDatasets
                    |> List.map (fun d ->
                        d.Table.Columns
                        |> List.collect (fun c -> c.Entries)
                        |> List.tryFind (fun e -> e.X = x)
                        |> Option.map (fun e -> box e.Value)
                        |> Option.defaultValue (box ""))

                box x :: cells)

        ExcelHelpers.gridOfRows ("Temperature" :: (orderedDatasets |> List.map allowableStressSourceLabel)) rows

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Interpolated allowable stress (MPa) at a given temperature (degC) and size (mm). source: \"S1\" (default), \"S1H\"/High, \"S2\", \"Bolting\"/S3. Bolting-only materials use S3 from ASME_Materials.db for S1/S1H/S2 requests.")>]
    let MatAllowableStress
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Query temperature, degC.")>] temperatureC: float)
        ([<ExcelArgument(Description = "Nominal size/thickness, mm. Required whenever more than one size range exists for the chosen source.")>] sizeMm: obj)
        ([<ExcelArgument(Description = "Allowable-stress source: S1 (default), S1H/High, S2, Bolting/S3.")>] source: obj)
        : obj =
        ExcelHelpers.withMaterial materialId (fun material ->
            selectAllowableStressDataset material (parseAllowableStressSource (Args.optionalTextOption source)) (Args.optionalNumberOption sizeMm)
            |> Result.bind (fun dataset -> PropertyTable.lookup1D temperatureC dataset.Table)
            |> Result.map (fun result -> result.Value))
        |> ExcelHelpers.ofFloatResult

    [<ExcelFunction(Category = "MaterialLibrary.Strength", Description = "Complete allowable-stress table for one source. Pass sizeMm to select one size range. Bolting-only materials use S3 from ASME_Materials.db for S1/S1H/S2 requests.")>]
    let MatAllowableStressTable
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Allowable-stress source: S1 (default), S1H/High, S2, Bolting/S3.")>] source: obj)
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
                    match allowableStressDatasetsForSource material sourceValue with
                    | [] -> Error(MaterialError.InvalidOperation(sprintf "No allowable-stress dataset for source %A" sourceValue))
                    | datasets -> Ok(allowableStressSourceGrid datasets))

        ExcelHelpers.ofGridResult result

    // ── Stress-strain curves ──────────────────────────────────────────────
