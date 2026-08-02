namespace MaterialLibrary.Domain

open System
open System.Text.Json
open System.Text.Json.Serialization
open ROP

// Serialization of a whole material library (metadata plus materials).

/// <summary>
/// JSON representation of a Material library (collection of materials with metadata).
/// </summary>
[<JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)>]
type MaterialLibraryJson =
    { [<JsonPropertyName("schemaVersion")>]
      SchemaVersion: int
      [<JsonPropertyName("version")>]
      Version: string
      [<JsonPropertyName("createdDate")>]
      CreatedDate: DateTime
      [<JsonPropertyName("lastModified")>]
      LastModified: DateTime
      [<JsonPropertyName("description")>]
      Description: string option
      [<JsonPropertyName("materials")>]
      Materials: MaterialJson list }

/// <summary>
/// Serialization for material libraries (collections of materials).
/// </summary>
module MaterialLibrarySerialization =

    // Helper to convert list of Results to Result of list
    let private sequenceResultList (xs: Result<'a, 'e> list) : Result<'a list, 'e> =
        xs
        |> List.fold
            (fun acc item ->
                match acc, item with
                | Ok ys, Ok x -> Ok(x :: ys)
                | Error e, _ -> Error e
                | _, Error e -> Error e)
            (Ok [])
        |> Result.map List.rev

    /// <summary>
    /// Serializes a list of materials to a <see cref="MaterialLibraryJson"/> with metadata.
    /// </summary>
    let toJson (version: string) (description: string option) (materials: Material list) : MaterialLibraryJson =
        { SchemaVersion = MaterialSerialization.CurrentSchemaVersion
          Version = version
          CreatedDate = DateTime.UtcNow
          LastModified = DateTime.UtcNow
          Description = description
          Materials = materials |> List.map MaterialSerialization.toJson }

    /// <summary>
    /// Deserializes a <see cref="MaterialLibraryJson"/> back to a list of materials.
    /// Requires physical properties for each material reconstruction.
    /// </summary>
    let fromJson
        (json: MaterialLibraryJson)
        (physicalProperties: PhysicalProperties)
        : Result<Material list, MaterialError> =
        result {
            do!
                match json.SchemaVersion with
                | MaterialSerialization.CurrentSchemaVersion -> Ok()
                | unsupported ->
                    Error(
                        MaterialError.InvalidOperation(
                            $"Unsupported material-library JSON schema version: {unsupported}"
                        )
                    )

            let! materials =
                json.Materials
                |> List.map (fun mJson -> MaterialSerialization.fromJson mJson physicalProperties)
                |> sequenceResultList

            return materials
        }

    /// <summary>Deserializes a complete material library using each material's embedded physical properties.</summary>
    let fromJsonComplete (json: MaterialLibraryJson) : Result<Material list, MaterialError> =
        match json.SchemaVersion with
        | MaterialSerialization.CurrentSchemaVersion ->
            json.Materials
            |> List.map MaterialSerialization.fromJsonComplete
            |> sequenceResultList
        | unsupported ->
            Error(
                MaterialError.InvalidOperation(
                    $"Unsupported material-library JSON schema version: {unsupported}"
                )
            )

    /// <summary>Serializes a material list to a JSON string.</summary>
    let toJsonString (version: string) (description: string option) (materials: Material list) : string =
        JsonSerializer.Serialize(toJson version description materials, JsonOptions.value)

    /// <summary>Deserializes a JSON string to a material list.</summary>
    let fromJsonString (json: string) (physicalProperties: PhysicalProperties) : Result<Material list, MaterialError> =
        try
            let parsed = JsonSerializer.Deserialize<MaterialLibraryJson>(json, JsonOptions.value)

            if obj.ReferenceEquals(box parsed, null) then
                Error(MaterialError.InvalidOperation "Deserialized JSON was null")
            else
                fromJson parsed physicalProperties
        with ex ->
            Error(MaterialError.InvalidOperation(sprintf "JSON deserialization failed: %s" ex.Message))

    /// <summary>Deserializes a complete material-library JSON string without external fallback data.</summary>
    let fromJsonStringComplete (json: string) : Result<Material list, MaterialError> =
        try
            let parsed = JsonSerializer.Deserialize<MaterialLibraryJson>(json, JsonOptions.value)

            if obj.ReferenceEquals(box parsed, null) then
                Error(MaterialError.InvalidOperation "Deserialized JSON was null")
            else
                fromJsonComplete parsed
        with ex ->
            Error(MaterialError.InvalidOperation(sprintf "JSON deserialization failed: %s" ex.Message))

    /// <summary>Saves a material library to a JSON file.</summary>
    let saveToFile
        (filePath: string)
        (version: string)
        (description: string option)
        (materials: Material list)
        : Result<unit, MaterialError> =
        try
            System.IO.File.WriteAllText(filePath, toJsonString version description materials)
            Ok()
        with ex ->
            Error(MaterialError.InvalidOperation(sprintf "File write failed: %s" ex.Message))

    /// <summary>Loads a material library from a JSON file.</summary>
    let loadFromFile
        (filePath: string)
        (physicalProperties: PhysicalProperties)
        : Result<Material list, MaterialError> =
        try
            let json = System.IO.File.ReadAllText(filePath)
            fromJsonString json physicalProperties
        with ex ->
            Error(MaterialError.InvalidOperation(sprintf "File read failed: %s" ex.Message))

    /// <summary>Loads a complete material library using embedded physical properties.</summary>
    let loadFromFileComplete (filePath: string) : Result<Material list, MaterialError> =
        try
            let json = System.IO.File.ReadAllText(filePath)
            fromJsonStringComplete json
        with ex ->
            Error(MaterialError.InvalidOperation(sprintf "File read failed: %s" ex.Message))
