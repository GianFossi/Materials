namespace MaterialLibrary.Crud

open System
open Microsoft.Data.Sqlite
open MaterialLibrary.Domain

/// <summary>
/// Definition and provisioning of the application-owned tables added to an ASME material database.
/// </summary>
/// <remarks>
/// <para>
/// The shipped <c>asme_materials.db</c> covers only part of the <see cref="Material"/> object: it has
/// no home for density rows, tensile rows, compression properties, per-material physical-property
/// rows, the ASME family classification, welding numbers, maximum allowable temperatures, or any of
/// the creep, stress-strain, fatigue, and Code Case 2964 data. Rather than reshaping the reference
/// tables, this module adds a set of extension tables and links them to the existing
/// <c>Materials</c> table.
/// </para>
/// <para>
/// Design rules for every extension table:
/// </para>
/// <list type="number">
/// <item>
/// Created with <c>CREATE TABLE IF NOT EXISTS</c>, so provisioning is idempotent and safe to run on
/// every connection.
/// </item>
/// <item>
/// Linked by <c>MaterialID INTEGER NOT NULL REFERENCES Materials(ID) ON DELETE CASCADE</c>, so
/// deleting a material removes its rows and no orphan data can accumulate. Callers must enable
/// <c>PRAGMA foreign_keys = ON</c> for the cascade to fire; <c>ensureSchema</c> does this.
/// </item>
/// <item>
/// Stored in normalized long form - one row per temperature - rather than the pivoted
/// <c>T_40 ... T_900</c> layout of the legacy tables, because the domain models these tables as
/// <c>(temperature, value)</c> lists and the legacy temperature grids differ per table.
/// </item>
/// <item>
/// Named with a <c>Material</c> prefix and a <c>Rows</c> suffix so they are visibly distinct from the
/// original ASME tables in any SQLite browser.
/// </item>
/// </list>
/// <para>
/// Units follow the project-wide fixed conventions: temperature in degC, stress and strength in MPa,
/// density in kg/m^3, specific heat in J/(kg*K), thermal conductivity in W/(m*K), thermal expansion
/// coefficient in 1/degC, elongation and reduction of area in percent.
/// </para>
/// </remarks>
module MaterialDatabaseSchema =

    /// <summary>Table holding one row of scalar metadata per material.</summary>
    [<Literal>]
    let ExtensionTable = "MaterialLibraryExtension"

    /// <summary>Table holding the lossless serialized form of each material.</summary>
    [<Literal>]
    let DocumentTable = "MaterialDocumentStore"

    /// <summary>
    /// DDL for every application-owned table, keyed by table name.
    /// </summary>
    /// <remarks>
    /// <c>MaterialLibraryExtension</c> also carries <c>MaterialKey</c>, the string
    /// <c>Material.Id</c>. The ASME <c>Materials</c> table keys on an integer, but domain material
    /// IDs are free-text (for example <c>"SA-516-70"</c>), so the mapping between the two has to be
    /// stored rather than computed.
    /// </remarks>
    let private tableDefinitions: (string * string) list =
        [ ExtensionTable,
          $"""
            CREATE TABLE IF NOT EXISTS {ExtensionTable} (
                MaterialID INTEGER PRIMARY KEY REFERENCES Materials(ID) ON DELETE CASCADE,
                MaterialKey TEXT NOT NULL UNIQUE,
                Name TEXT,
                Family TEXT,
                AllowableStressLevel TEXT,
                MaxTempAsmeViiiI REAL,
                MaxTempAsmeViii1 REAL,
                MaxTempAsmeViii2 REAL,
                TimeDependentStartTemperature REAL,
                WeldingPNumber TEXT,
                WeldingGNumber TEXT,
                ReductionOfAreaPercent REAL,
                ThermalExpansionReferenceTemperature REAL,
                CreatedDate TEXT,
                LastModified TEXT
            )"""

          "MaterialThermalExpansionRows",
          """
            CREATE TABLE IF NOT EXISTS MaterialThermalExpansionRows (
                ID INTEGER PRIMARY KEY AUTOINCREMENT,
                MaterialID INTEGER NOT NULL REFERENCES Materials(ID) ON DELETE CASCADE,
                Temperature REAL NOT NULL,
                ExpansionCoefficient REAL NOT NULL
            )"""

          "MaterialElasticModulusRows",
          """
            CREATE TABLE IF NOT EXISTS MaterialElasticModulusRows (
                ID INTEGER PRIMARY KEY AUTOINCREMENT,
                MaterialID INTEGER NOT NULL REFERENCES Materials(ID) ON DELETE CASCADE,
                Temperature REAL NOT NULL,
                ElasticModulus REAL NOT NULL,
                PoissonRatio REAL
            )"""

          "MaterialDensityRows",
          """
            CREATE TABLE IF NOT EXISTS MaterialDensityRows (
                ID INTEGER PRIMARY KEY AUTOINCREMENT,
                MaterialID INTEGER NOT NULL REFERENCES Materials(ID) ON DELETE CASCADE,
                Temperature REAL NOT NULL,
                Density REAL NOT NULL
            )"""

          "MaterialSpecificHeatRows",
          """
            CREATE TABLE IF NOT EXISTS MaterialSpecificHeatRows (
                ID INTEGER PRIMARY KEY AUTOINCREMENT,
                MaterialID INTEGER NOT NULL REFERENCES Materials(ID) ON DELETE CASCADE,
                Temperature REAL NOT NULL,
                SpecificHeat REAL NOT NULL
            )"""

          "MaterialThermalConductivityRows",
          """
            CREATE TABLE IF NOT EXISTS MaterialThermalConductivityRows (
                ID INTEGER PRIMARY KEY AUTOINCREMENT,
                MaterialID INTEGER NOT NULL REFERENCES Materials(ID) ON DELETE CASCADE,
                Temperature REAL NOT NULL,
                Conductivity REAL NOT NULL
            )"""

          "MaterialTensileRows",
          """
            CREATE TABLE IF NOT EXISTS MaterialTensileRows (
                ID INTEGER PRIMARY KEY AUTOINCREMENT,
                MaterialID INTEGER NOT NULL REFERENCES Materials(ID) ON DELETE CASCADE,
                Temperature REAL NOT NULL,
                YieldStrength REAL NOT NULL,
                TensileStrength REAL NOT NULL,
                ElongationPercent REAL NOT NULL,
                ReductionOfAreaPercent REAL NOT NULL
            )"""

          "MaterialAllowableStressRows",
          """
            CREATE TABLE IF NOT EXISTS MaterialAllowableStressRows (
                ID INTEGER PRIMARY KEY AUTOINCREMENT,
                MaterialID INTEGER NOT NULL REFERENCES Materials(ID) ON DELETE CASCADE,
                Temperature REAL NOT NULL,
                SectionIServiceLevelA REAL,
                SectionIServiceLevelB REAL,
                SectionIServiceLevelC REAL,
                SectionIServiceLevelD REAL,
                SectionIIWeld REAL
            )"""

          "MaterialCompressionRows",
          """
            CREATE TABLE IF NOT EXISTS MaterialCompressionRows (
                ID INTEGER PRIMARY KEY AUTOINCREMENT,
                MaterialID INTEGER NOT NULL REFERENCES Materials(ID) ON DELETE CASCADE,
                Temperature REAL NOT NULL,
                CompressiveStrength REAL NOT NULL,
                CompressiveYield REAL NOT NULL
            )"""

          "MaterialAsmeCodeRows",
          """
            CREATE TABLE IF NOT EXISTS MaterialAsmeCodeRows (
                ID INTEGER PRIMARY KEY AUTOINCREMENT,
                MaterialID INTEGER NOT NULL REFERENCES Materials(ID) ON DELETE CASCADE,
                AsmeCode TEXT NOT NULL
            )"""

          DocumentTable,
          $"""
            CREATE TABLE IF NOT EXISTS {DocumentTable} (
                MaterialID INTEGER PRIMARY KEY REFERENCES Materials(ID) ON DELETE CASCADE,
                Format TEXT NOT NULL,
                SchemaVersion INTEGER NOT NULL,
                Payload TEXT NOT NULL,
                LastModified TEXT NOT NULL
            )""" ]

    /// <summary>Indexes created alongside the extension tables, keyed by index name.</summary>
    /// <remarks>
    /// Every row table is queried by <c>MaterialID</c> when a material is loaded or replaced, so each
    /// gets a covering index. Without them a load degrades to a full scan of every row table.
    /// </remarks>
    let private indexDefinitions: (string * string) list =
        // Both arms use an explicit 'yield'. Mixing a '->' comprehension with a trailing literal
        // element silently discards the literal, which would leave the MaterialKey index uncreated.
        [ for table in
              [ "MaterialThermalExpansionRows"
                "MaterialElasticModulusRows"
                "MaterialDensityRows"
                "MaterialSpecificHeatRows"
                "MaterialThermalConductivityRows"
                "MaterialTensileRows"
                "MaterialAllowableStressRows"
                "MaterialCompressionRows"
                "MaterialAsmeCodeRows" ] do
              let indexName = $"IX_{table}_MaterialID"
              yield indexName, $"CREATE INDEX IF NOT EXISTS {indexName} ON {table}(MaterialID)"

          yield
              "IX_MaterialLibraryExtension_MaterialKey",
              $"CREATE UNIQUE INDEX IF NOT EXISTS IX_MaterialLibraryExtension_MaterialKey ON {ExtensionTable}(MaterialKey)" ]

    /// <summary>Names of every application-owned table, in creation order.</summary>
    let allTableNames: string list = tableDefinitions |> List.map fst

    /// <summary>Runs a statement that returns no rows.</summary>
    /// <param name="connection">Open connection.</param>
    /// <param name="sql">Statement to execute.</param>
    let private execute (connection: SqliteConnection) (sql: string) =
        use command = connection.CreateCommand()
        command.CommandText <- sql
        command.ExecuteNonQuery() |> ignore

    /// <summary>Reports whether a table already exists in the database.</summary>
    /// <param name="connection">Open connection.</param>
    /// <param name="tableName">Table to look for.</param>
    /// <returns><c>true</c> when a table of that name is present.</returns>
    let tableExists (connection: SqliteConnection) (tableName: string) : bool =
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name"
        command.Parameters.AddWithValue("$name", tableName) |> ignore
        Convert.ToInt64(command.ExecuteScalar()) > 0L

    /// <summary>Lists the application-owned tables that are not yet present.</summary>
    /// <param name="connection">Open connection.</param>
    /// <returns>Names of the tables <see cref="ensureSchema"/> would create.</returns>
    let missingTables (connection: SqliteConnection) : string list =
        allTableNames |> List.filter (fun name -> not (tableExists connection name))

    /// <summary>
    /// Creates every missing application-owned table, index, and foreign-key link.
    /// </summary>
    /// <param name="connection">Open read-write connection.</param>
    /// <returns>
    /// <c>Ok createdTables</c> listing the tables that did not exist beforehand (empty when the
    /// schema was already complete), or an error describing the failure.
    /// </returns>
    /// <remarks>
    /// Idempotent: safe to call on every connection. The whole provisioning runs inside one
    /// transaction, so a failure part-way leaves the database exactly as it was rather than
    /// half-migrated. Requires that the base <c>Materials</c> table exists, since every extension
    /// table references it.
    /// </remarks>
    let ensureSchema (connection: SqliteConnection) : Result<string list, MaterialError> =
        if not (tableExists connection "Materials") then
            Error(
                MaterialError.InvalidOperation
                    "Database does not contain a 'Materials' table; it is not an ASME material database."
            )
        else
            try
                // Record what was missing before creating anything, so the caller can report it.
                let created = missingTables connection

                // Foreign keys are off by default in SQLite and are per-connection, not per-database.
                execute connection "PRAGMA foreign_keys = ON"

                use transaction = connection.BeginTransaction()

                for _, ddl in tableDefinitions do
                    execute connection ddl

                for _, ddl in indexDefinitions do
                    execute connection ddl

                transaction.Commit()
                Ok created
            with ex ->
                Error(MaterialError.InvalidOperation(sprintf "Schema provisioning failed: %s" ex.Message))
