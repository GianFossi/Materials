namespace MaterialLibrary.Excel

open ExcelDna.Integration
open MaterialLibrary
open MaterialLibrary.Domain
open MaterialLibrary.Domain.Database.Lookup

/// <summary>
/// Excel worksheet functions for loading material sources and searching/describing materials.
/// </summary>
module MaterialSearchFunctions =

    let private allFamilies =
        [ CS; QT; LTCS; LAS1_00; LAS1_25; LAS2_25; LAS5_00; LAS9_00; SSA; SSF; SSM; SSD; SSDPlus ]

    /// Matches a family code (e.g. "LAS2.25", case-insensitive) against AsmeMaterialFamily.code;
    /// unrecognised text is treated as "no family filter" rather than "match nothing", since this
    /// is a convenience search box, not a validating form field.
    let private parseFamily (text: string option) : AsmeMaterialFamily option =
        text
        |> Option.bind (fun value ->
            allFamilies
            |> List.tryFind (fun family -> System.String.Equals(AsmeMaterialFamily.code family, value, System.StringComparison.OrdinalIgnoreCase)))

    [<ExcelFunction(Category = "MaterialLibrary", Description = "Loads (or reloads) materials from the ASME SQLite database. Leave path blank to use the configured default.")>]
    let MatOpenDatabase
        ([<ExcelArgument(Description = "ASME material SQLite database path; blank uses the configured default.")>] path: obj)
        : obj =
        match LibraryCache.loadDatabase (Args.optionalTextOption path) with
        | Ok count -> box (sprintf "Loaded %d materials from the ASME database." count)
        | Error message -> box (sprintf "#VALUE! %s" message)

    [<ExcelFunction(Category = "MaterialLibrary", Description = "Loads (or reloads) a complete material-library JSON file (stress-strain, creep, fatigue, etc.).")>]
    let MatOpenJsonLibrary
        ([<ExcelArgument(Description = "Path to a JSON file produced by MaterialLibrarySerialization.saveToFile.")>] path: string)
        : obj =
        match LibraryCache.loadJsonLibrary path with
        | Ok count -> box (sprintf "Loaded %d materials from the JSON library." count)
        | Error message -> box (sprintf "#VALUE! %s" message)

    [<ExcelFunction(Category = "MaterialLibrary", Description = "Reports which material sources are currently loaded and how many materials each contributes.")>]
    let MatLibraryStatus () : string = LibraryCache.status ()

    [<ExcelFunction(Category = "MaterialLibrary", Description = "Searches loaded materials by specification, grade, class/condition/tempering, UNS, nominal composition, product form, and/or exact family code. Every argument is optional; blank matches all. Spills one row per match.")>]
    let MatSearch
        ([<ExcelArgument(Description = "Specification substring, e.g. \"SA-516\".")>] specification: obj)
        ([<ExcelArgument(Description = "Grade substring, e.g. \"70\".")>] grade: obj)
        ([<ExcelArgument(Description = "Class/Condition/Tempering substring.")>] classConditionTemper: obj)
        ([<ExcelArgument(Description = "UNS alloy designation substring.")>] uns: obj)
        ([<ExcelArgument(Description = "Nominal composition substring, e.g. \"Carbon steel\".")>] nominalComposition: obj)
        ([<ExcelArgument(Description = "Product form substring, e.g. \"Plate\".")>] productForm: obj)
        ([<ExcelArgument(Description = "Exact ASME family code, e.g. \"LAS2.25\", \"SSA\", \"CS\".")>] family: obj)
        : obj[,] =
        let contains value =
            value |> Option.map Contains

        let criteria =
            { MaterialSearchCriteria.empty with
                Specification = Args.optionalTextOption specification |> contains
                Grade = Args.optionalTextOption grade |> contains
                ClassConditionTemper = Args.optionalTextOption classConditionTemper |> contains
                Uns = Args.optionalTextOption uns |> contains
                NominalComposition = Args.optionalTextOption nominalComposition |> contains
                ProductForm = Args.optionalTextOption productForm |> contains
                Family = parseFamily (Args.optionalTextOption family) }

        let matches =
            LibraryCache.current().ListAllMaterials()
            |> MaterialFiltering.findMany criteria

        if List.isEmpty matches then
            ExcelHelpers.errorGrid "#N/A no material matches the given criteria"
        else
            let rows =
                matches
                |> List.map (fun material ->
                    [ box material.Id
                      box material.Specification
                      box material.Grade
                      box material.Class_Condition_Tempering
                      box material.AlloyIdentification_UNS
                      box material.NominalComposition
                      box material.ProductForm
                      box (material.Family |> Option.map AsmeMaterialFamily.code |> Option.defaultValue "") ])

            ExcelHelpers.gridOfRows
                [ "Id"
                  "Specification"
                  "Grade"
                  "ClassConditionTempering"
                  "UNS"
                  "NominalComposition"
                  "ProductForm"
                  "Family" ]
                rows

    [<ExcelFunction(Category = "MaterialLibrary", Description = "Returns a formatted multi-line summary of a material's identity and available data inventory.")>]
    let MatDescribe
        ([<ExcelArgument(Description = "Material ID, e.g. \"123\" or an ID returned by MatSearch.")>] materialId: string)
        : obj =
        LibraryCache.current().DescribeMaterial materialId
        |> ExcelHelpers.ofStringResult
