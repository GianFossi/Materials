namespace MaterialLibrary.Crud

open System
open System.IO
open Microsoft.Data.Sqlite
open MaterialLibrary
open MaterialLibrary.Domain

/// <summary>Summary of one material as listed from the database.</summary>
type DatabaseMaterialSummary =
    { /// Integer primary key in the ASME <c>Materials</c> table.
      DatabaseId: int64
      /// Domain material identifier (<c>Material.Id</c>).
      MaterialKey: string
      /// Composed material name, when the extension row supplies one.
      Name: string
      /// Specification text from the <c>Materials</c> table.
      Specification: string
      /// Grade text (<c>TypeGrade</c>) from the <c>Materials</c> table.
      Grade: string
      /// Whether a lossless serialized document is stored for this material.
      HasDocument: bool }

/// <summary>
/// Full create/read/update/delete access to an ASME material database, including provisioning of the
/// application-owned tables described by <see cref="MaterialDatabaseSchema"/>.
/// </summary>
/// <remarks>
/// <para>
/// Writing works on whichever file the caller opens. The application is expected to operate on a
/// working copy rather than the shipped reference database; <see cref="createWorkingCopy"/> exists
/// for that, and every write path here assumes the caller has already made that choice.
/// </para>
/// <para>
/// Each material is persisted twice, deliberately. The scalar identity goes into the ASME
/// <c>Materials</c> row and the tabular data into the normalized extension tables, so the values stay
/// queryable with ordinary SQL. In parallel, the complete material is written to
/// <c>MaterialDocumentStore</c> as its canonical JSON. The tables are the queryable projection; the
/// document is the source of truth on read, which is what guarantees that data with no dedicated
/// table - creep models, stress-strain curves, fatigue curves, Code Case 2964 constants - survives a
/// round trip without loss.
/// </para>
/// </remarks>
module MaterialDatabaseCrud =

    /// <summary>Format tag written into <c>MaterialDocumentStore.Format</c>.</summary>
    [<Literal>]
    let DocumentFormat = "json"

    // ── Connection helpers ────────────────────────────────────────────────────

    /// <summary>Opens a read-write connection with foreign-key enforcement enabled.</summary>
    /// <param name="databasePath">Path to the SQLite file.</param>
    /// <returns>An open connection; the caller owns its lifetime.</returns>
    let private openConnection (databasePath: string) : SqliteConnection =
        let connection = new SqliteConnection($"Data Source={databasePath}")
        connection.Open()

        // Foreign keys are per-connection in SQLite, so the ON DELETE CASCADE links only work
        // when this pragma is set on the same connection that performs the delete.
        use pragma = connection.CreateCommand()
        pragma.CommandText <- "PRAGMA foreign_keys = ON"
        pragma.ExecuteNonQuery() |> ignore

        connection

    /// <summary>Runs an action against an open database, converting exceptions into typed errors.</summary>
    /// <param name="databasePath">Path to the SQLite file.</param>
    /// <param name="action">Work to perform with the open connection.</param>
    /// <returns>The action's result, or an error describing the failure.</returns>
    let private withConnection
        (databasePath: string)
        (action: SqliteConnection -> Result<'T, MaterialError>)
        : Result<'T, MaterialError> =
        if String.IsNullOrWhiteSpace databasePath then
            Error(MaterialError.InvalidOperation "Database path is empty.")
        elif not (File.Exists databasePath) then
            Error(MaterialError.NotFound $"Database not found: {databasePath}")
        else
            try
                use connection = openConnection databasePath
                action connection
            with ex ->
                Error(MaterialError.InvalidOperation(sprintf "Database operation failed: %s" ex.Message))

    /// <summary>Adds a parameter, mapping <c>None</c> to SQL NULL.</summary>
    /// <param name="command">Command being built.</param>
    /// <param name="name">Parameter name including the <c>$</c> prefix.</param>
    /// <param name="value">Optional value.</param>
    let private addOptional (command: SqliteCommand) (name: string) (value: float option) =
        match value with
        | Some v -> command.Parameters.AddWithValue(name, v) |> ignore
        | None -> command.Parameters.AddWithValue(name, DBNull.Value) |> ignore

    /// <summary>Adds a string parameter, mapping blank text to SQL NULL.</summary>
    /// <param name="command">Command being built.</param>
    /// <param name="name">Parameter name including the <c>$</c> prefix.</param>
    /// <param name="value">Optional text.</param>
    let private addOptionalText (command: SqliteCommand) (name: string) (value: string option) =
        match value with
        | Some v when not (String.IsNullOrWhiteSpace v) -> command.Parameters.AddWithValue(name, v) |> ignore
        | _ -> command.Parameters.AddWithValue(name, DBNull.Value) |> ignore

    /// <summary>Reads a nullable REAL column as an option.</summary>
    /// <param name="reader">Positioned reader.</param>
    /// <param name="name">Column name.</param>
    /// <returns><c>Some value</c>, or <c>None</c> when the column is NULL.</returns>
    let private optionalReal (reader: SqliteDataReader) (name: string) : float option =
        let ordinal = reader.GetOrdinal name
        if reader.IsDBNull ordinal then None else Some(reader.GetDouble ordinal)

    /// <summary>Reads a nullable TEXT column as an option.</summary>
    /// <param name="reader">Positioned reader.</param>
    /// <param name="name">Column name.</param>
    /// <returns><c>Some text</c>, or <c>None</c> when the column is NULL or blank.</returns>
    let private optionalText (reader: SqliteDataReader) (name: string) : string option =
        let ordinal = reader.GetOrdinal name

        if reader.IsDBNull ordinal then
            None
        else
            let value = reader.GetString ordinal
            if String.IsNullOrWhiteSpace value then None else Some value

    /// <summary>Reads a TEXT column, substituting empty string for NULL.</summary>
    /// <param name="reader">Positioned reader.</param>
    /// <param name="name">Column name.</param>
    /// <returns>The column text, or an empty string.</returns>
    let private text (reader: SqliteDataReader) (name: string) : string =
        optionalText reader name |> Option.defaultValue ""

    // ── Identity mapping ──────────────────────────────────────────────────────

    /// <summary>
    /// Finds the integer <c>Materials.ID</c> that a domain material key maps to.
    /// </summary>
    /// <param name="connection">Open connection.</param>
    /// <param name="materialKey">Domain <c>Material.Id</c>.</param>
    /// <returns><c>Some id</c> when the material is already stored, otherwise <c>None</c>.</returns>
    /// <remarks>
    /// Two resolution paths, in order. A material whose key is a plain integer is an ASME database
    /// material and maps to that row directly. Otherwise the mapping was recorded in the extension
    /// table when the material was first written, because free-text IDs such as <c>"SA-516-70"</c>
    /// cannot be derived from an integer primary key.
    /// </remarks>
    let private tryResolveDatabaseId (connection: SqliteConnection) (materialKey: string) : int64 option =
        use command = connection.CreateCommand()
        command.CommandText <-
            $"SELECT MaterialID FROM {MaterialDatabaseSchema.ExtensionTable} WHERE MaterialKey = $key"
        command.Parameters.AddWithValue("$key", materialKey) |> ignore

        match command.ExecuteScalar() with
        | null ->
            // Not written by this application; fall back to a direct integer key.
            match Int64.TryParse materialKey with
            | true, parsed ->
                use exists = connection.CreateCommand()
                exists.CommandText <- "SELECT COUNT(*) FROM Materials WHERE ID = $id"
                exists.Parameters.AddWithValue("$id", parsed) |> ignore
                if Convert.ToInt64(exists.ExecuteScalar()) > 0L then Some parsed else None
            | _ -> None
        | value -> Some(Convert.ToInt64 value)

    /// <summary>Allocates the next free <c>Materials.ID</c>.</summary>
    /// <param name="connection">Open connection.</param>
    /// <returns>An identifier one past the current maximum.</returns>
    let private nextDatabaseId (connection: SqliteConnection) : int64 =
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT COALESCE(MAX(ID), 0) + 1 FROM Materials"
        Convert.ToInt64(command.ExecuteScalar())

    // ── Row writers ───────────────────────────────────────────────────────────

    /// <summary>Deletes every extension row belonging to a material.</summary>
    /// <param name="connection">Open connection.</param>
    /// <param name="databaseId">Material primary key.</param>
    /// <remarks>
    /// Writes are replace-all rather than differential: the editor always presents a complete table,
    /// so clearing first is both simpler and the only way a removed row can actually disappear.
    /// </remarks>
    let private clearMaterialRows (connection: SqliteConnection) (databaseId: int64) =
        let rowTables =
            MaterialDatabaseSchema.allTableNames
            |> List.filter (fun name -> name <> MaterialDatabaseSchema.ExtensionTable)

        for table in rowTables do
            use command = connection.CreateCommand()
            command.CommandText <- $"DELETE FROM {table} WHERE MaterialID = $id"
            command.Parameters.AddWithValue("$id", databaseId) |> ignore
            command.ExecuteNonQuery() |> ignore

    /// <summary>Inserts the rows of one table for a material.</summary>
    /// <param name="connection">Open connection.</param>
    /// <param name="databaseId">Material primary key.</param>
    /// <param name="table">Target table name.</param>
    /// <param name="columns">Value column names, excluding <c>MaterialID</c>.</param>
    /// <param name="rows">Values per row, aligned with <paramref name="columns"/>.</param>
    let private insertRows
        (connection: SqliteConnection)
        (databaseId: int64)
        (table: string)
        (columns: string list)
        (rows: (float option) list list)
        =
        if not rows.IsEmpty then
            let columnList = String.Join(", ", "MaterialID" :: columns)
            let parameterList = String.Join(", ", "$materialId" :: (columns |> List.map (fun c -> "$" + c)))

            for row in rows do
                use command = connection.CreateCommand()
                command.CommandText <- $"INSERT INTO {table} ({columnList}) VALUES ({parameterList})"
                command.Parameters.AddWithValue("$materialId", databaseId) |> ignore

                List.iter2 (fun column value -> addOptional command ("$" + column) value) columns row

                command.ExecuteNonQuery() |> ignore

    /// <summary>Writes the ASME <c>Materials</c> row for a material.</summary>
    /// <param name="connection">Open connection.</param>
    /// <param name="databaseId">Material primary key.</param>
    /// <param name="material">Material being stored.</param>
    /// <remarks>
    /// Maps the domain identity onto the reference table's own column names. <c>SMTS</c> and
    /// <c>SMYS</c> carry the specified minimum ultimate and yield strengths (MPa);
    /// <c>RuptureElongationLong</c> carries the elongation (%). Reduction of area has no column in
    /// the reference schema and is kept in the extension row instead.
    /// </remarks>
    let private writeMaterialsRow (connection: SqliteConnection) (databaseId: int64) (material: Material) =
        use command = connection.CreateCommand()

        command.CommandText <-
            """
            INSERT INTO Materials
                (ID, NominalComposition, ProductForm, Specification, TypeGrade,
                 ClassConditionTemper, AlloyDesignationNumber, SMTS, SMYS,
                 RuptureElongationLong, Notes)
            VALUES
                ($id, $nominalComposition, $productForm, $specification, $typeGrade,
                 $classConditionTemper, $alloyDesignationNumber, $smts, $smys,
                 $elongation, $notes)
            ON CONFLICT(ID) DO UPDATE SET
                NominalComposition = excluded.NominalComposition,
                ProductForm = excluded.ProductForm,
                Specification = excluded.Specification,
                TypeGrade = excluded.TypeGrade,
                ClassConditionTemper = excluded.ClassConditionTemper,
                AlloyDesignationNumber = excluded.AlloyDesignationNumber,
                SMTS = excluded.SMTS,
                SMYS = excluded.SMYS,
                RuptureElongationLong = excluded.RuptureElongationLong,
                Notes = excluded.Notes"""

        command.Parameters.AddWithValue("$id", databaseId) |> ignore
        command.Parameters.AddWithValue("$nominalComposition", material.NominalComposition) |> ignore
        command.Parameters.AddWithValue("$productForm", material.ProductForm) |> ignore
        command.Parameters.AddWithValue("$specification", material.Specification) |> ignore
        command.Parameters.AddWithValue("$typeGrade", material.Grade) |> ignore
        command.Parameters.AddWithValue("$classConditionTemper", material.Class_Condition_Tempering) |> ignore
        command.Parameters.AddWithValue("$alloyDesignationNumber", material.AlloyIdentification_UNS) |> ignore
        command.Parameters.AddWithValue("$smts", material.BasicProperties.SpecifiedMinimumUltimateStrength) |> ignore
        command.Parameters.AddWithValue("$smys", material.BasicProperties.SpecifiedMinimumYieldStrength) |> ignore
        command.Parameters.AddWithValue("$elongation", material.BasicProperties.ElongationPercent) |> ignore
        addOptionalText command "$notes" material.Notes
        command.ExecuteNonQuery() |> ignore

    /// <summary>Writes the extension row carrying the scalar fields the ASME schema has no column for.</summary>
    /// <param name="connection">Open connection.</param>
    /// <param name="databaseId">Material primary key.</param>
    /// <param name="material">Material being stored.</param>
    let private writeExtensionRow (connection: SqliteConnection) (databaseId: int64) (material: Material) =
        use command = connection.CreateCommand()

        command.CommandText <-
            $"""
            INSERT INTO {MaterialDatabaseSchema.ExtensionTable}
                (MaterialID, MaterialKey, Name, Family, AllowableStressLevel,
                 MaxTempAsmeViiiI, MaxTempAsmeViii1, MaxTempAsmeViii2,
                 TimeDependentStartTemperature, WeldingPNumber, WeldingGNumber,
                 ReductionOfAreaPercent, ThermalExpansionReferenceTemperature,
                 CreatedDate, LastModified)
            VALUES
                ($materialId, $key, $name, $family, $stressLevel,
                 $maxI, $max1, $max2, $timeDependent, $pNumber, $gNumber,
                 $reductionOfArea, $expansionReference, $created, $modified)
            ON CONFLICT(MaterialID) DO UPDATE SET
                MaterialKey = excluded.MaterialKey,
                Name = excluded.Name,
                Family = excluded.Family,
                AllowableStressLevel = excluded.AllowableStressLevel,
                MaxTempAsmeViiiI = excluded.MaxTempAsmeViiiI,
                MaxTempAsmeViii1 = excluded.MaxTempAsmeViii1,
                MaxTempAsmeViii2 = excluded.MaxTempAsmeViii2,
                TimeDependentStartTemperature = excluded.TimeDependentStartTemperature,
                WeldingPNumber = excluded.WeldingPNumber,
                WeldingGNumber = excluded.WeldingGNumber,
                ReductionOfAreaPercent = excluded.ReductionOfAreaPercent,
                ThermalExpansionReferenceTemperature = excluded.ThermalExpansionReferenceTemperature,
                LastModified = excluded.LastModified"""

        command.Parameters.AddWithValue("$materialId", databaseId) |> ignore
        command.Parameters.AddWithValue("$key", material.Id) |> ignore
        command.Parameters.AddWithValue("$name", material.Name) |> ignore
        addOptionalText command "$family" (material.Family |> Option.map AsmeMaterialFamily.code)

        let stressLevel =
            match material.AllowableStressLevel with
            | StandardAllowableStress -> "Standard"
            | HighAllowableStress -> "High"

        command.Parameters.AddWithValue("$stressLevel", stressLevel) |> ignore
        addOptional command "$maxI" material.MaximumAllowableTemperature.AsmeViiiI
        addOptional command "$max1" material.MaximumAllowableTemperature.AsmeViii1
        addOptional command "$max2" material.MaximumAllowableTemperature.AsmeViii2
        addOptional command "$timeDependent" material.TimeDepenedingStartTemperature
        addOptionalText command "$pNumber" (material.WeldingInfo |> Option.map (fun w -> w.PNumber))
        addOptionalText command "$gNumber" (material.WeldingInfo |> Option.map (fun w -> w.GNumber))
        command.Parameters.AddWithValue("$reductionOfArea", material.BasicProperties.ReductionOfAreaPercent) |> ignore

        command.Parameters.AddWithValue(
            "$expansionReference",
            material.PhysicalProperties.ThermalExpansionReferenceTemperature
        )
        |> ignore

        command.Parameters.AddWithValue("$created", material.CreatedDate.ToString("O")) |> ignore
        command.Parameters.AddWithValue("$modified", material.LastModified.ToString("O")) |> ignore
        command.ExecuteNonQuery() |> ignore

    /// <summary>Writes every normalized property-row table for a material.</summary>
    /// <param name="connection">Open connection.</param>
    /// <param name="databaseId">Material primary key.</param>
    /// <param name="material">Material being stored.</param>
    let private writePropertyRows (connection: SqliteConnection) (databaseId: int64) (material: Material) =
        let physical = material.PhysicalProperties
        let strength = material.StrengthProperties

        insertRows connection databaseId "MaterialThermalExpansionRows" [ "Temperature"; "ExpansionCoefficient" ] (
            physical.ThermalExpansionTable
            |> List.map (fun row -> [ Some row.Temperature; Some row.ExpansionCoefficient ])
        )

        insertRows connection databaseId "MaterialElasticModulusRows" [ "Temperature"; "ElasticModulus"; "PoissonRatio" ] (
            physical.ElasticModulusTable
            |> List.map (fun row -> [ Some row.Temperature; Some row.ElasticModulus; row.PoissonRatio ])
        )

        insertRows connection databaseId "MaterialDensityRows" [ "Temperature"; "Density" ] (
            physical.DensityTable
            |> List.map (fun row -> [ Some row.Temperature; Some row.Density ])
        )

        insertRows connection databaseId "MaterialSpecificHeatRows" [ "Temperature"; "SpecificHeat" ] (
            physical.SpecificHeatTable
            |> Option.defaultValue []
            |> List.map (fun row -> [ Some row.Temperature; Some row.SpecificHeat ])
        )

        insertRows connection databaseId "MaterialThermalConductivityRows" [ "Temperature"; "Conductivity" ] (
            physical.ThermalConductivityTable
            |> Option.defaultValue []
            |> List.map (fun (temperature, conductivity) -> [ Some temperature; Some conductivity ])
        )

        // MaterialTensileRows and MaterialAllowableStressRows are legacy projection tables that
        // mapped to the old flat TensileProperties and AllowableStresses lists. Those lists have
        // been replaced by SyTable and AllowableStressDatasets (stored in the JSON document).
        // The extension tables are kept for schema compatibility but are no longer written;
        // the canonical JSON document in MaterialDocumentStore is the source of truth.

        insertRows
            connection
            databaseId
            "MaterialCompressionRows"
            [ "Temperature"; "CompressiveStrength"; "CompressiveYield" ]
            (strength.CompressionProperties
             |> Option.defaultValue []
             |> List.map (fun row -> [ Some row.Temperature; Some row.CompressiveStrength; Some row.CompressiveYield ]))

        // ASME codes are text, so they do not fit the numeric insertRows helper.
        for code in material.ApplicableAsmeCodes do
            use command = connection.CreateCommand()
            command.CommandText <- "INSERT INTO MaterialAsmeCodeRows (MaterialID, AsmeCode) VALUES ($id, $code)"
            command.Parameters.AddWithValue("$id", databaseId) |> ignore
            command.Parameters.AddWithValue("$code", string code) |> ignore
            command.ExecuteNonQuery() |> ignore

    /// <summary>Writes the lossless serialized document for a material.</summary>
    /// <param name="connection">Open connection.</param>
    /// <param name="databaseId">Material primary key.</param>
    /// <param name="material">Material being stored.</param>
    let private writeDocument (connection: SqliteConnection) (databaseId: int64) (material: Material) =
        use command = connection.CreateCommand()

        command.CommandText <-
            $"""
            INSERT INTO {MaterialDatabaseSchema.DocumentTable}
                (MaterialID, Format, SchemaVersion, Payload, LastModified)
            VALUES ($id, $format, $schemaVersion, $payload, $modified)
            ON CONFLICT(MaterialID) DO UPDATE SET
                Format = excluded.Format,
                SchemaVersion = excluded.SchemaVersion,
                Payload = excluded.Payload,
                LastModified = excluded.LastModified"""

        command.Parameters.AddWithValue("$id", databaseId) |> ignore
        command.Parameters.AddWithValue("$format", DocumentFormat) |> ignore
        command.Parameters.AddWithValue("$schemaVersion", MaterialSerialization.CurrentSchemaVersion) |> ignore
        command.Parameters.AddWithValue("$payload", MaterialSerialization.toJsonString material) |> ignore
        command.Parameters.AddWithValue("$modified", material.LastModified.ToString("O")) |> ignore
        command.ExecuteNonQuery() |> ignore

    // ── Public operations ─────────────────────────────────────────────────────

    /// <summary>
    /// Copies a database to a working location so edits never touch the original file.
    /// </summary>
    /// <param name="sourcePath">Database to copy.</param>
    /// <param name="workingPath">Destination path; overwritten when it already exists.</param>
    /// <returns><c>Ok workingPath</c>, or an error when the copy fails.</returns>
    let createWorkingCopy (sourcePath: string) (workingPath: string) : Result<string, MaterialError> =
        if not (File.Exists sourcePath) then
            Error(MaterialError.NotFound $"Database not found: {sourcePath}")
        else
            try
                let directory = Path.GetDirectoryName(Path.GetFullPath workingPath)

                if not (String.IsNullOrWhiteSpace directory) then
                    Directory.CreateDirectory directory |> ignore

                File.Copy(sourcePath, workingPath, overwrite = true)
                Ok workingPath
            with ex ->
                Error(MaterialError.InvalidOperation(sprintf "Could not create working copy: %s" ex.Message))

    /// <summary>
    /// Creates any application-owned tables the database is missing.
    /// </summary>
    /// <param name="databasePath">Database to provision.</param>
    /// <returns>
    /// <c>Ok createdTables</c> naming the tables that were added (empty when nothing was missing).
    /// </returns>
    let ensureSchema (databasePath: string) : Result<string list, MaterialError> =
        withConnection databasePath MaterialDatabaseSchema.ensureSchema

    /// <summary>Lists the materials present in the database.</summary>
    /// <param name="databasePath">Database to read.</param>
    /// <returns>One summary per material, ordered by database identifier.</returns>
    /// <remarks>
    /// Left-joins the extension table so materials that predate this application - the 2129 shipped
    /// ASME rows - are listed too, with their integer identifier standing in for the material key.
    /// </remarks>
    let listMaterials (databasePath: string) : Result<DatabaseMaterialSummary list, MaterialError> =
        withConnection databasePath (fun connection ->
            let hasExtension = MaterialDatabaseSchema.tableExists connection MaterialDatabaseSchema.ExtensionTable
            let hasDocuments = MaterialDatabaseSchema.tableExists connection MaterialDatabaseSchema.DocumentTable

            use command = connection.CreateCommand()

            command.CommandText <-
                if hasExtension then
                    $"""
                    SELECT m.ID, m.Specification, m.TypeGrade,
                           e.MaterialKey AS MaterialKey, e.Name AS Name,
                           {if hasDocuments then
                                $"(SELECT COUNT(*) FROM {MaterialDatabaseSchema.DocumentTable} d WHERE d.MaterialID = m.ID)"
                            else
                                "0"} AS DocumentCount
                    FROM Materials m
                    LEFT JOIN {MaterialDatabaseSchema.ExtensionTable} e ON e.MaterialID = m.ID
                    ORDER BY m.ID"""
                else
                    """
                    SELECT m.ID, m.Specification, m.TypeGrade,
                           NULL AS MaterialKey, NULL AS Name, 0 AS DocumentCount
                    FROM Materials m
                    ORDER BY m.ID"""

            use reader = command.ExecuteReader()
            let results = ResizeArray<DatabaseMaterialSummary>()

            while reader.Read() do
                let databaseId = reader.GetInt64(reader.GetOrdinal "ID")

                results.Add
                    { DatabaseId = databaseId
                      // Materials never written by this application have no key; the integer ID is
                      // the only identifier they have.
                      MaterialKey = optionalText reader "MaterialKey" |> Option.defaultValue (string databaseId)
                      Name = text reader "Name"
                      Specification = text reader "Specification"
                      Grade = text reader "TypeGrade"
                      HasDocument = reader.GetInt64(reader.GetOrdinal "DocumentCount") > 0L }

            Ok(List.ofSeq results))

    /// <summary>Reads one material back from the database.</summary>
    /// <param name="databasePath">Database to read.</param>
    /// <param name="materialKey">Domain <c>Material.Id</c>, or the integer database identifier.</param>
    /// <returns><c>Ok material</c>, or an error when the material is absent or unreadable.</returns>
    /// <remarks>
    /// Reads the stored document, which is the lossless form. A material present only in the legacy
    /// ASME tables has no document; it must be imported through <c>AsmeMaterialRepository</c> and
    /// saved once before this returns it.
    /// </remarks>
    let readMaterial (databasePath: string) (materialKey: string) : Result<Material, MaterialError> =
        withConnection databasePath (fun connection ->
            if not (MaterialDatabaseSchema.tableExists connection MaterialDatabaseSchema.DocumentTable) then
                Error(
                    MaterialError.NotFound
                        "This database has no application tables yet; provision the schema and save a material first."
                )
            else
                match tryResolveDatabaseId connection materialKey with
                | None -> Error(MaterialError.NotFound $"Material not found in database: {materialKey}")
                | Some databaseId ->
                    use command = connection.CreateCommand()

                    command.CommandText <-
                        $"SELECT Payload FROM {MaterialDatabaseSchema.DocumentTable} WHERE MaterialID = $id"

                    command.Parameters.AddWithValue("$id", databaseId) |> ignore

                    match command.ExecuteScalar() with
                    | null ->
                        Error(
                            MaterialError.NotFound
                                $"Material {materialKey} exists in the reference tables but has not been saved by this application."
                        )
                    | payload -> MaterialSerialization.fromJsonStringComplete (string payload))

    /// <summary>
    /// Creates or replaces a material and all of its tables, provisioning any missing schema first.
    /// </summary>
    /// <param name="databasePath">Database to write.</param>
    /// <param name="material">Material to store.</param>
    /// <returns><c>Ok change</c> describing the operation, or an error.</returns>
    /// <remarks>
    /// The whole write - <c>Materials</c> row, extension row, every property-row table, and the
    /// document - runs in one transaction, so a material is never left half-written. Property rows
    /// are cleared and reinserted rather than merged, which is the only way a row deleted in the
    /// editor actually disappears from the database.
    /// </remarks>
    let upsertMaterial (databasePath: string) (material: Material) : Result<CrudChange, MaterialError> =
        if isNull (box material) || String.IsNullOrWhiteSpace material.Id then
            CrudResult.invalid "Material must have a non-empty ID"
        else
            withConnection databasePath (fun connection ->
                MaterialDatabaseSchema.ensureSchema connection
                |> Result.bind (fun _ ->
                    try
                        let existing = tryResolveDatabaseId connection material.Id
                        let databaseId = existing |> Option.defaultWith (fun () -> nextDatabaseId connection)

                        use transaction = connection.BeginTransaction()

                        writeMaterialsRow connection databaseId material
                        writeExtensionRow connection databaseId material
                        clearMaterialRows connection databaseId
                        writePropertyRows connection databaseId material
                        writeDocument connection databaseId material

                        transaction.Commit()

                        let operation = if existing.IsSome then Update else Create

                        let message =
                            if existing.IsSome then
                                $"Material updated in database (ID {databaseId})"
                            else
                                $"Material created in database (ID {databaseId})"

                        CrudResult.changed operation (Some material.Id) None message
                    with ex ->
                        Error(MaterialError.InvalidOperation(sprintf "Database write failed: %s" ex.Message))))

    /// <summary>Deletes a material and, through the foreign keys, all of its rows.</summary>
    /// <param name="databasePath">Database to write.</param>
    /// <param name="materialKey">Domain <c>Material.Id</c>, or the integer database identifier.</param>
    /// <returns><c>Ok change</c>, or an error when the material is absent.</returns>
    let deleteMaterial (databasePath: string) (materialKey: string) : Result<CrudChange, MaterialError> =
        withConnection databasePath (fun connection ->
            match tryResolveDatabaseId connection materialKey with
            | None -> CrudResult.notFound $"Material not found in database: {materialKey}"
            | Some databaseId ->
                try
                    use transaction = connection.BeginTransaction()

                    // Extension rows are removed explicitly as well as by cascade, so the delete
                    // still works on a connection where foreign keys happen to be disabled.
                    clearMaterialRows connection databaseId

                    for table in [ MaterialDatabaseSchema.ExtensionTable; MaterialDatabaseSchema.DocumentTable ] do
                        if MaterialDatabaseSchema.tableExists connection table then
                            use command = connection.CreateCommand()
                            command.CommandText <- $"DELETE FROM {table} WHERE MaterialID = $id"
                            command.Parameters.AddWithValue("$id", databaseId) |> ignore
                            command.ExecuteNonQuery() |> ignore

                    use command = connection.CreateCommand()
                    command.CommandText <- "DELETE FROM Materials WHERE ID = $id"
                    command.Parameters.AddWithValue("$id", databaseId) |> ignore
                    command.ExecuteNonQuery() |> ignore

                    transaction.Commit()
                    CrudResult.changed Delete (Some materialKey) None "Material deleted from database"
                with ex ->
                    Error(MaterialError.InvalidOperation(sprintf "Database delete failed: %s" ex.Message)))

    /// <summary>Saves every material of an in-memory library into the database.</summary>
    /// <param name="databasePath">Database to write.</param>
    /// <param name="materials">Materials to store.</param>
    /// <returns><c>Ok changes</c>, one per material, or the first error encountered.</returns>
    let upsertMaterials
        (databasePath: string)
        (materials: Material list)
        : Result<CrudChange list, MaterialError> =
        materials
        |> List.fold
            (fun state material ->
                state
                |> Result.bind (fun changes ->
                    upsertMaterial databasePath material |> Result.map (fun change -> change :: changes)))
            (Ok [])
        |> Result.map List.rev
