namespace MaterialLibrary.Crud

open System
open System.IO
open System.Xml.Linq
open MaterialLibrary.Domain

/// <summary>Import/export helpers for staged XML data files under MaterialLibrary/data.</summary>
module XmlDataCrud =
    let private markerPrefix = "[MaterialLibrary.Crud.XmlData]"

    let private sanitizeSegment (value: string) =
        let invalid = Path.GetInvalidFileNameChars() |> Set.ofArray

        value
        |> Seq.map (fun c -> if invalid.Contains c then '_' else c)
        |> Seq.toArray
        |> String

    let private materialXmlFolder (materialId: string) =
        Path.Combine("materials", sanitizeSegment materialId)

    let private appendXmlReference (relativePath: string) (notes: string option) =
        let line = $"{markerPrefix} {relativePath.Replace('\\', '/')}"

        match notes with
        | Some existing when existing.Contains(line, StringComparison.OrdinalIgnoreCase) -> Some existing
        | Some existing when String.IsNullOrWhiteSpace existing -> Some line
        | Some existing -> Some(existing.TrimEnd() + Environment.NewLine + line)
        | None -> Some line

    let private referencePaths (notes: string option) =
        match notes with
        | None -> []
        | Some value ->
            value.Split([| "\r\n"; "\n" |], StringSplitOptions.None)
            |> Array.choose (fun line ->
                let trimmed = line.Trim()

                if trimmed.StartsWith(markerPrefix, StringComparison.OrdinalIgnoreCase) then
                    let path = trimmed.Substring(markerPrefix.Length).Trim()
                    if String.IsNullOrWhiteSpace path then None else Some path
                else
                    None)
            |> Array.distinct
            |> Array.toList

    /// <summary>Reads all staged XML files below a data root.</summary>
    let readAll dataRoot =
        MaterialLibraryDataXml.readAll dataRoot

    /// <summary>Reads one staged XML file below a data root.</summary>
    let readFile dataRoot relativePath =
        MaterialLibraryDataXml.readFile dataRoot relativePath

    /// <summary>Writes one staged XML file below a data root.</summary>
    let writeFile dataRoot relativePath document =
        MaterialLibraryDataXml.writeFile dataRoot relativePath document

    /// <summary>
    /// Imports one XML data file into a material-specific data subfolder and stores a traceable reference
    /// on the material notes.
    /// </summary>
    let importFileIntoMaterial
        (dataRoot: string)
        (material: Material)
        (sourceRelativePath: string)
        : Result<Material * MaterialLibraryXmlDataFile, MaterialError> =
        MaterialLibraryDataXml.readFile dataRoot sourceRelativePath
        |> Result.bind (fun xml ->
            let targetRelativePath =
                Path.Combine(materialXmlFolder material.Id, xml.FileName)

            MaterialLibraryDataXml.writeFile dataRoot targetRelativePath xml.Document
            |> Result.map (fun _ ->
                let normalizedTarget = targetRelativePath.Replace('\\', '/')

                { material with
                    Notes = appendXmlReference normalizedTarget material.Notes
                    LastModified = DateTime.UtcNow },
                { xml with RelativePath = normalizedTarget }))

    /// <summary>Imports several XML data files into the same material-specific data subfolder.</summary>
    let importFilesIntoMaterial
        (dataRoot: string)
        (material: Material)
        (sourceRelativePaths: string list)
        : Result<Material * MaterialLibraryXmlDataFile list, MaterialError> =
        ((Ok(material, []): Result<Material * MaterialLibraryXmlDataFile list, MaterialError>), sourceRelativePaths)
        ||> List.fold (fun state relativePath ->
            state
            |> Result.bind (fun (currentMaterial, imported) ->
                importFileIntoMaterial dataRoot currentMaterial relativePath
                |> Result.map (fun (updated, xml) -> updated, xml :: imported)))
        |> Result.map (fun (updated, imported) -> updated, List.rev imported)

    /// <summary>Returns XML files currently referenced by a material's notes.</summary>
    let readMaterialXmlFiles (dataRoot: string) (material: Material) : Result<MaterialLibraryXmlDataFile list, MaterialError> =
        ((Ok []: Result<MaterialLibraryXmlDataFile list, MaterialError>), referencePaths material.Notes)
        ||> List.fold (fun state relativePath ->
            state
            |> Result.bind (fun files ->
                MaterialLibraryDataXml.readFile dataRoot relativePath
                |> Result.map (fun xml -> xml :: files)))
        |> Result.map List.rev

    /// <summary>Exports all XML files referenced by a material into an external folder.</summary>
    let exportMaterialXmlFiles
        (dataRoot: string)
        (material: Material)
        (exportFolder: string)
        : Result<string list, MaterialError> =
        readMaterialXmlFiles dataRoot material
        |> Result.bind (fun files ->
            try
                Directory.CreateDirectory(exportFolder) |> ignore

                let written =
                    files
                    |> List.map (fun file ->
                        let target = Path.Combine(exportFolder, file.FileName)
                        let tempPath = target + "." + Guid.NewGuid().ToString("N") + ".tmp"

                        try
                            file.Document.Save(tempPath)
                            File.Move(tempPath, target, true)
                            target
                        with ex ->
                            try
                                if File.Exists(tempPath) then
                                    File.Delete(tempPath)
                            with _ ->
                                ()

                            raise ex)

                Ok written
            with ex ->
                Error(MaterialError.InvalidOperation $"Unable to export XML data for material '{material.Id}': {ex.Message}"))
