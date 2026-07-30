namespace MaterialLibrary.Excel

open ExcelDna.Integration
open MaterialLibrary
open MaterialLibrary.Domain

/// <summary>
/// Excel worksheet functions for writing and reading individual materials and whole material
/// libraries as JSON files (see <see cref="MaterialSerialization"/> and
/// <see cref="MaterialLibrarySerialization"/> in the core library).
/// </summary>
/// <remarks>
/// Loading a whole library (replacing the JSON-sourced side of the cache) is <c>MatOpenJsonLibrary</c>
/// in <c>MaterialSearchFunctions</c>; the functions here add single-material save/load and saving the
/// currently loaded library back out to a file.
/// </remarks>
module MaterialPersistenceFunctions =

    [<ExcelFunction(Category = "MaterialLibrary", Description = "Saves one material to a JSON file (complete: includes its own physical properties).")>]
    let MatSaveMaterial
        ([<ExcelArgument(Description = "Material ID.")>] materialId: string)
        ([<ExcelArgument(Description = "Destination JSON file path.")>] filePath: string)
        : obj =
        ExcelHelpers.withMaterial materialId (fun material -> MaterialSerialization.saveToFile filePath material)
        |> Result.map (fun () -> sprintf "Saved %s to %s" materialId filePath)
        |> ExcelHelpers.ofStringResult

    [<ExcelFunction(Category = "MaterialLibrary", Description = "Loads one complete material JSON file (as saved by MatSaveMaterial) and adds it to (or replaces it in) the currently loaded library.")>]
    let MatLoadMaterial
        ([<ExcelArgument(Description = "Source JSON file path.")>] filePath: string)
        : obj =
        MaterialSerialization.loadFromFileComplete filePath
        |> Result.map (fun material ->
            LibraryCache.addOrReplaceJsonMaterial material
            sprintf "Loaded %s from %s" material.Id filePath)
        |> ExcelHelpers.ofStringResult

    [<ExcelFunction(Category = "MaterialLibrary", Description = "Saves every currently loaded material to a JSON material-library file (see MaterialLibrarySerialization).")>]
    let MatSaveLibrary
        ([<ExcelArgument(Description = "Destination JSON file path.")>] filePath: string)
        ([<ExcelArgument(Description = "Free-text library version recorded in the file; blank uses \"1.0\".")>] version: obj)
        ([<ExcelArgument(Description = "Optional free-text description recorded in the file.")>] description: obj)
        : obj =
        let library = LibraryCache.current ()


        MaterialLibrary.saveToFile filePath (Args.optionalText "1.0" version) (Args.optionalTextOption description) library
        |> Result.map (fun () -> sprintf "Saved %d materials to %s" library.Count filePath)
        |> ExcelHelpers.ofStringResult
