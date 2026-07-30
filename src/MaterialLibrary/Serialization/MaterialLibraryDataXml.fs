namespace MaterialLibrary.Domain

open System
open System.IO
open System.Xml.Linq

/// <summary>Metadata and XML content for one staged XML data file under MaterialLibrary/data.</summary>
type MaterialLibraryXmlDataFile =
    { /// Path relative to the data root, using '/' separators.
      RelativePath: string
      /// Name of the folder directly containing this XML file.
      Folder: string
      /// XML file name.
      FileName: string
      /// XML root element name.
      RootName: string
      /// XML root attributes.
      RootAttributes: Map<string, string>
      /// Parsed XML document.
      Document: XDocument }

/// <summary>Read and write helpers for staged XML files shipped under MaterialLibrary/data.</summary>
module MaterialLibraryDataXml =
    let private normalizeRelativePath (path: string) =
        path.Replace('\\', '/')

    let private fullRootPath (dataRoot: string) =
        Path.GetFullPath(dataRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)

    let private combineUnderRoot (dataRoot: string) (relativePath: string) =
        if Path.IsPathRooted(relativePath) then
            Error(MaterialError.InvalidOperation $"XML data path must be relative: {relativePath}")
        else
            let root = fullRootPath dataRoot
            let fullPath = Path.GetFullPath(Path.Combine(root, relativePath))

            if
                fullPath.Equals(root, StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith(root + string Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith(root + string Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            then
                Ok fullPath
            else
                Error(MaterialError.InvalidOperation $"XML data path escapes data root: {relativePath}")

    let private toRelativePath (root: string) (path: string) =
        Path.GetRelativePath(fullRootPath root, path) |> normalizeRelativePath

    let private attributes (element: XElement) =
        element.Attributes()
        |> Seq.map (fun attribute -> attribute.Name.LocalName, attribute.Value)
        |> Map.ofSeq

    let private toDataFile (dataRoot: string) (path: string) =
        let document = XDocument.Load(path, LoadOptions.PreserveWhitespace ||| LoadOptions.SetLineInfo)
        let root =
            match document.Root with
            | null -> ""
            | value -> value.Name.LocalName

        let rootAttributes =
            match document.Root with
            | null -> Map.empty
            | value -> attributes value

        { RelativePath = toRelativePath dataRoot path
          Folder = Path.GetFileName(Path.GetDirectoryName(path))
          FileName = Path.GetFileName(path)
          RootName = root
          RootAttributes = rootAttributes
          Document = document }

    let private candidateDataRoots () =
        let baseDirectory = AppContext.BaseDirectory
        let currentDirectory = Directory.GetCurrentDirectory()

        [ Path.Combine(baseDirectory, "data")
          Path.Combine(baseDirectory, "contentFiles", "any", "any", "data")
          Path.Combine(currentDirectory, "data")
          Path.Combine(currentDirectory, "src", "MaterialLibrary", "data") ]

    /// <summary>Finds the first existing MaterialLibrary/data folder near the app base directory or current working directory.</summary>
    let tryFindDefaultDataRoot () : string option =
        candidateDataRoots ()
        |> List.tryFind Directory.Exists

    /// <summary>Reads one XML file and reports root metadata together with the parsed document.</summary>
    let readFile (dataRoot: string) (relativePath: string) : Result<MaterialLibraryXmlDataFile, MaterialError> =
        try
            combineUnderRoot dataRoot relativePath
            |> Result.bind (fun fullPath ->
                if not (File.Exists fullPath) then
                    Error(MaterialError.NotFound $"XML data file not found: {fullPath}")
                else
                    Ok(toDataFile dataRoot fullPath))
        with ex ->
            Error(MaterialError.InvalidOperation $"Unable to read XML data file '{relativePath}': {ex.Message}")

    /// <summary>Reads all XML files in a data subfolder, recursively, ordered by relative path.</summary>
    let readFolder (dataRoot: string) (relativeFolder: string) : Result<MaterialLibraryXmlDataFile list, MaterialError> =
        try
            combineUnderRoot dataRoot relativeFolder
            |> Result.bind (fun fullFolder ->
                if not (Directory.Exists fullFolder) then
                    Error(MaterialError.NotFound $"XML data folder not found: {fullFolder}")
                else
                    Directory.EnumerateFiles(fullFolder, "*.xml", SearchOption.AllDirectories)
                    |> Seq.sortBy (toRelativePath dataRoot)
                    |> Seq.map (toDataFile dataRoot)
                    |> Seq.toList
                    |> Ok)
        with ex ->
            Error(MaterialError.InvalidOperation $"Unable to read XML data folder '{relativeFolder}': {ex.Message}")

    /// <summary>Reads every XML file below a data root, recursively, ordered by relative path.</summary>
    let readAll (dataRoot: string) : Result<MaterialLibraryXmlDataFile list, MaterialError> =
        readFolder dataRoot "."

    /// <summary>Reads every XML file below the discovered default data root.</summary>
    let readDefaultAll () : Result<MaterialLibraryXmlDataFile list, MaterialError> =
        match tryFindDefaultDataRoot () with
        | Some dataRoot -> readAll dataRoot
        | None -> Error(MaterialError.NotFound "MaterialLibrary data folder was not found.")

    /// <summary>Writes an XML document below a data root, creating folders when needed.</summary>
    let writeFile (dataRoot: string) (relativePath: string) (document: XDocument) : Result<string, MaterialError> =
        try
            combineUnderRoot dataRoot relativePath
            |> Result.bind (fun fullPath ->
                if isNull (box document) then
                    Error(MaterialError.InvalidOperation "XML document cannot be null")
                else
                    let directory = Path.GetDirectoryName(fullPath)

                    if not (String.IsNullOrWhiteSpace directory) then
                        Directory.CreateDirectory(directory) |> ignore

                    let tempPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp"

                    try
                        document.Save(tempPath)
                        File.Move(tempPath, fullPath, true)
                        Ok(fullPath)
                    with ex ->
                        try
                            if File.Exists(tempPath) then
                                File.Delete(tempPath)
                        with _ ->
                            ()

                        Error(MaterialError.InvalidOperation $"Unable to write XML data file '{relativePath}': {ex.Message}"))
        with ex ->
            Error(MaterialError.InvalidOperation $"Unable to write XML data file '{relativePath}': {ex.Message}")
