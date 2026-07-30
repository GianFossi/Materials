namespace MaterialLibrary.Crud

open MaterialLibrary
open MaterialLibrary.Domain

/// <summary>In-memory CRUD repository for complete material records.</summary>
type MaterialCrudRepository(initialMaterials: Material list) =
    let mutable materialMap =
        initialMaterials
        |> List.filter (fun material -> not (isNull (box material)) && not (System.String.IsNullOrWhiteSpace material.Id))
        |> List.map (fun material -> material.Id, material)
        |> Map.ofList

    new() = MaterialCrudRepository([])

    /// <summary>Returns an immutable <see cref="MaterialLibrary"/> view of the current repository contents.</summary>
    member this.ToMaterialLibrary() : Result<MaterialLibrary.MaterialLibrary, MaterialError> =
        materialMap |> Map.values |> Seq.toList |> MaterialLibrary.create

    /// <summary>Returns every stored material.</summary>
    member this.List() : Material list =
        materialMap |> Map.values |> Seq.toList

    /// <summary>Reads one material by ID.</summary>
    member this.Read(id: string) : Result<Material, MaterialError> =
        materialMap
        |> Map.tryFind id
        |> Option.map Ok
        |> Option.defaultWith (fun () -> Error(MaterialError.NotFound $"Material not found: {id}"))

    /// <summary>Creates a new material. Fails when the ID is already present.</summary>
    member this.Create(material: Material) : Result<CrudChange, MaterialError> =
        if isNull (box material) || System.String.IsNullOrWhiteSpace material.Id then
            CrudResult.invalid "Material must have a non-empty ID"
        elif materialMap |> Map.containsKey material.Id then
            CrudResult.invalid $"Material already exists: {material.Id}"
        else
            materialMap <- materialMap |> Map.add material.Id material
            CrudResult.changed Create (Some material.Id) None "Material created"

    /// <summary>Creates or replaces a material by ID.</summary>
    member this.Upsert(material: Material) : Result<CrudChange, MaterialError> =
        if isNull (box material) || System.String.IsNullOrWhiteSpace material.Id then
            CrudResult.invalid "Material must have a non-empty ID"
        else
            let exists = materialMap |> Map.containsKey material.Id
            materialMap <- materialMap |> Map.add material.Id { material with LastModified = System.DateTime.UtcNow }

            CrudResult.changed
                (if exists then Update else Create)
                (Some material.Id)
                None
                (if exists then "Material updated" else "Material created")

    /// <summary>Updates an existing material through a transformation function.</summary>
    member this.Update(id: string, update: Material -> Result<Material, MaterialError>) : Result<CrudChange, MaterialError> =
        this.Read(id)
        |> Result.bind update
        |> Result.bind this.Upsert

    /// <summary>Deletes a material by ID.</summary>
    member this.Delete(id: string) : Result<CrudChange, MaterialError> =
        if materialMap |> Map.containsKey id then
            materialMap <- materialMap |> Map.remove id
            CrudResult.changed Delete (Some id) None "Material deleted"
        else
            CrudResult.notFound $"Material not found: {id}"

    /// <summary>Imports one staged XML file into a stored material and updates the material record.</summary>
    member this.ImportXmlDataIntoMaterial
        (dataRoot: string, materialId: string, sourceRelativePath: string)
        : Result<CrudChange, MaterialError> =
        this.Update(
            materialId,
            fun material ->
                XmlDataCrud.importFileIntoMaterial dataRoot material sourceRelativePath
                |> Result.map fst
        )

    /// <summary>Exports XML files referenced by a stored material into an external folder.</summary>
    member this.ExportMaterialXmlData
        (dataRoot: string, materialId: string, exportFolder: string)
        : Result<string list, MaterialError> =
        this.Read(materialId)
        |> Result.bind (fun material -> XmlDataCrud.exportMaterialXmlFiles dataRoot material exportFolder)

    /// <summary>Saves the current materials as a complete JSON material library.</summary>
    member this.SaveToFile(path: string, version: string, description: string option) : Result<unit, MaterialError> =
        MaterialLibrarySerialization.saveToFile path version description (this.List())

    /// <summary>Loads a repository from a complete JSON material library.</summary>
    static member LoadFromFile(path: string) : Result<MaterialCrudRepository, MaterialError> =
        MaterialLibrarySerialization.loadFromFileComplete path
        |> Result.map MaterialCrudRepository
