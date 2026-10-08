/// Locates and loads ASME_Materials.db once for the whole test run.
module MaterialLibrary.DataValidation.Database

open System
open System.IO
open MaterialReportCore

/// The database under test: the ASME_MATERIALS_DB environment variable when set, otherwise the
/// packaged src/MaterialLibrary/data/ASME_Materials.db found by walking up from the test binaries.
let path: string =
    match Environment.GetEnvironmentVariable "ASME_MATERIALS_DB" with
    | null
    | "" ->
        let relative = Path.Combine("src", "MaterialLibrary", "data", "ASME_Materials.db")

        let rec search (dir: DirectoryInfo) =
            if isNull dir then
                failwith "ASME_Materials.db not found: set ASME_MATERIALS_DB to its path"
            else
                let candidate = Path.Combine(dir.FullName, relative)
                if File.Exists candidate then candidate else search dir.Parent

        search (DirectoryInfo AppContext.BaseDirectory)
    | custom -> custom

/// The whole database, read once (the file is copied, never modified).
let db: Lazy<Db> = lazy (load path)

/// Every material of the database with its data.
let materials: Lazy<MaterialData list> =
    lazy (db.Value.Materials |> List.map (buildData db.Value))

/// The materials selected by a catalog entry.
let select (entry: Catalog.Entry) : MaterialData list =
    materials.Value |> List.filter entry.Matches

let describe (d: MaterialData) : string =
    let part (s: string) = if s = "" then "" else " " + s
    $"ID {d.Id} {d.Specification}{part d.Grade}{part d.ClassCondition}{part d.Uns} ({d.ProductForm})"

/// Root of the repository (the folder that contains tests/MaterialLibrary.DataValidation).
let repositoryRoot: string =
    let rec search (dir: DirectoryInfo) =
        if isNull dir then
            failwith "repository root not found"
        elif Directory.Exists(Path.Combine(dir.FullName, "tests", "MaterialLibrary.DataValidation")) then
            dir.FullName
        else
            search dir.Parent

    search (DirectoryInfo AppContext.BaseDirectory)

/// Staged Code text (ASME BPVC II-D Subpart 2) used as the reference of the physical properties.
let codeReference: Lazy<CodeText.CodeReference> =
    lazy (CodeText.load (Path.Combine(repositoryRoot, "src", "MaterialLibrary", "data", "physical-properties-xml")))

/// The verified snapshot of the handbook materials.
let baselinePath: string =
    Path.Combine(repositoryRoot, "tests", "MaterialLibrary.DataValidation", "Baseline", "prontuario-baseline.csv")

/// Composition lists of the notes of the Code tables (group of each steel).
let codeGroups: Lazy<CodeGroups.GroupLists> =
    lazy (CodeGroups.load (Path.Combine(repositoryRoot, "src", "MaterialLibrary", "data", "physical-properties-xml")))

/// The physical properties a material must have according to the Code.
let physical (d: MaterialData) : PhysicalReference.PhysicalProperties =
    PhysicalReference.of' codeReference.Value codeGroups.Value d
