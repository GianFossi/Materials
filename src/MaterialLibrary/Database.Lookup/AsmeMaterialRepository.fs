namespace MaterialLibrary.Domain.Database.Lookup

open System
open System.Globalization
open System.IO
open Microsoft.Data.Sqlite
open MaterialLibrary.Domain

module AsmeMaterialRepository =
    let private temperatures =
        [ 40; 65; 100; 125; 150; 175; 200; 225; 250; 275; 300; 325; 350; 375; 400; 425
          450; 475; 500; 525; 550; 575; 600; 625; 650; 675; 700; 725; 750; 775; 800; 825
          850; 875; 900 ]

    let private text (reader: SqliteDataReader) name =
        let ordinal = reader.GetOrdinal name
        if reader.IsDBNull ordinal then "" else reader.GetString ordinal

    let private optionalText (reader: SqliteDataReader) name =
        let value = text reader name
        if String.IsNullOrWhiteSpace value then None else Some value

    let private number (reader: SqliteDataReader) name =
        let ordinal = reader.GetOrdinal name
        if reader.IsDBNull ordinal then 0.0 else reader.GetDouble ordinal

    let private optionalNumber (reader: SqliteDataReader) name =
        let ordinal = reader.GetOrdinal name
        if reader.IsDBNull ordinal then None else Some(reader.GetDouble ordinal)

    let private materialFromReader (reader: SqliteDataReader) =
        let databaseId = reader.GetInt64(reader.GetOrdinal "ID")
        let specification = text reader "Specification"
        let grade = text reader "TypeGrade"
        let productForm = text reader "ProductForm"
        let composition = text reader "NominalComposition"
        let classCondition = text reader "ClassConditionTemper"
        let uns = text reader "AlloyDesignationNumber"
        let density = number reader "Density"

        // Both elongations and the reduction of area come from the room-temperature tensile coupon
        // test, so they are read as scalars here and never spread across the Sy/Su curves. The
        // reference schema has no reduction-of-area column; the application stores it in its own
        // extension table, which is why it starts at zero.
        let basic =
            BasicProperties.create
                (optionalNumber reader "RuptureElongationLong")
                (optionalNumber reader "RuptureElongationTransv")
                0.0
                (number reader "SMYS")
                (number reader "SMTS")

        let physical =
            PhysicalPropertiesTable.create
                None
                []
                []
                None
                [ { Temperature = 20.0; Density = density } ]
                None
                None

        Material.create
            $"{databaseId}"
            $"{specification} {grade}"
            specification
            grade
            basic
            physical
        |> Material.setIdentity productForm composition specification grade classCondition uns
        |> fun material ->
            { material with
                Family =
                    AsmeMaterialFamilyClassification.classify
                        specification
                        grade
                        classCondition
                        composition
                        uns
                Notes = optionalText reader "Notes" }

    let private loadCandidates (connection: SqliteConnection) =
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT * FROM Materials ORDER BY ID"
        use reader = command.ExecuteReader()
        let materials = ResizeArray<Material>()
        while reader.Read() do
            materials.Add(materialFromReader reader)
        materials |> Seq.toList

    let private sourceDefinition =
        function
        | Division1AllowableStress -> "AllowableStress1Table", "MaximumTemperature"
        | Division1HighAllowableStress -> "AllowableStress1HTable", "MaximumTemperature"
        | Division2AllowableStress -> "AllowableStress2Table", "MaximumTemperature"
        | BoltingAllowableStress -> "AllowableStress3Table", "MaxTemp_VIII1"

    let private tableExists (connection: SqliteConnection) tableName =
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $tableName"
        command.Parameters.AddWithValue("$tableName", tableName) |> ignore
        Convert.ToInt64(command.ExecuteScalar()) > 0L

    let private noteTable source referenceData =
        match source, referenceData with
        | (Division1AllowableStress | Division1HighAllowableStress), "Table_1A" -> Some Table1A
        | (Division1AllowableStress | Division1HighAllowableStress), "Table_1B" -> Some Table1B
        | Division2AllowableStress, "Table_5A" -> Some Table5A
        | Division2AllowableStress, "Table_5B" -> Some Table5B
        | BoltingAllowableStress, "Table_3" -> Some TableSBolting
        | _ -> None

    /// <summary>Reads the Size/Thickness band from the four columns every strength table carries.</summary>
    /// <param name="reader">Reader positioned on the row.</param>
    /// <returns>The band the row applies to (mm).</returns>
    /// <remarks>
    /// The <c>_Included</c> columns are stored as 0/1 and matter: adjacent ASME bands such as
    /// "up to 5 incl." and "over 5" share a boundary, and treating both ends as inclusive would make
    /// a 5 mm section match two bands at once. A missing column defaults to inclusive, which is how
    /// ASME prints an unqualified limit.
    /// </remarks>
    let private sizeRangeFromReader (reader: SqliteDataReader) : SizeThicknessRange =
        let included name =
            let ordinal = reader.GetOrdinal name
            reader.IsDBNull ordinal || reader.GetInt64 ordinal <> 0L

        SizeThicknessRange.create
            (optionalNumber reader "SizeThkMIN")
            (included "SizeThkMIN_Included")
            (optionalNumber reader "SizeThkMAX")
            (included "SizeThkMAX_Included")

    let private loadSourceRows
        (connection: SqliteConnection)
        materialId
        source
        : Result<AllowableStressDataset list, MaterialError> =
        let tableName, maximumTemperatureColumn = sourceDefinition source
        use command = connection.CreateCommand()
        command.CommandText <- $"SELECT * FROM {tableName} WHERE MaterialID = $materialId ORDER BY ID"
        command.Parameters.AddWithValue("$materialId", materialId) |> ignore
        use reader = command.ExecuteReader()
        let datasets = ResizeArray<AllowableStressDataset * float>()
        let mutable error = None

        while error.IsNone && reader.Read() do
            let entries =
                temperatures
                |> List.choose (fun temperature ->
                    optionalNumber reader $"T_{temperature}"
                    |> Option.map (fun value -> PropertyTable.entry (float temperature) value))

            match
                PropertyTable.create1D
                    $"{source} allowable stress"
                    "Temperature"
                    "degC"
                    "Allowable Stress"
                    "MPa"
                    ReturnError
                    entries
            with
            | Error materialError -> error <- Some materialError
            | Ok table ->
                let score = entries |> List.sumBy (fun entry -> entry.Value)
                let referenceData = text reader "ReferenceData"
                let sourceNotes = optionalText reader "Notes"
                datasets.Add(
                    (({ DatabaseRowId = reader.GetInt64(reader.GetOrdinal "ID")
                        Source = source
                        Case =
                            if source = Division1HighAllowableStress then
                                HighStrengthAllowableStress
                            else
                                StandardStrengthAllowableStress
                        Table = table
                        SizeRange = sizeRangeFromReader reader
                        MaximumTemperature = optionalNumber reader maximumTemperatureColumn
                        CreepTemperature = optionalNumber reader "CreepTemperature"
                        AsmeNoteReferences =
                            noteTable source referenceData
                            |> Option.map (fun noteSource -> AsmeNoteReference.parse noteSource sourceNotes)
                            |> Option.defaultValue []
                        Notes = None }: AllowableStressDataset),
                     score)
                )

        match error with
        | Some materialError -> Error materialError
        | None ->
            let classifyGroup (rows: (AllowableStressDataset * float) list) =
                match rows with
                | []
                | [ _ ] -> rows |> List.map fst
                | multiple when
                    source = Division1AllowableStress
                    && multiple
                    |> List.forall (fun (dataset, _) ->
                        dataset.AsmeNoteReferences
                        |> List.exists (fun note ->
                            note.Code.Equals("G5", StringComparison.OrdinalIgnoreCase)))
                    ->
                    let lowRowId = multiple |> List.minBy snd |> fst |> fun dataset -> dataset.DatabaseRowId
                    let highRowId = multiple |> List.maxBy snd |> fst |> fun dataset -> dataset.DatabaseRowId

                    multiple
                    |> List.map (fun (dataset, _) ->
                        if dataset.DatabaseRowId = highRowId then
                            { dataset with
                                Source = Division1HighAllowableStress
                                Case = HighStrengthAllowableStress }
                        elif dataset.DatabaseRowId = lowRowId then
                            { dataset with Case = StandardStrengthAllowableStress }
                        else
                            dataset)
                | multiple -> multiple |> List.map fst

            datasets
            |> Seq.toList
            |> List.groupBy (fun (dataset, _) -> dataset.SizeRange)
            |> List.collect (snd >> classifyGroup)
            |> List.map AllowableStressDataset.validate
            |> List.fold
                (fun state item ->
                    state
                    |> Result.bind (fun values -> item |> Result.map (fun value -> value :: values)))
                (Ok [])
            |> Result.map List.rev

    // -- Minimum strengths Sy(T) and Su(T) -------------------------------------
    //
    // Both tables are pivoted the same way as the allowable-stress tables, and both may publish
    // several rows per material: one per Size/Diameter/Thickness band, because the guaranteed
    // minimum strength falls as the section gets heavier. Every row is kept as its own dataset;
    // the flat TensileProperties list keeps only the governing pairing, for callers that ask for
    // Sy and Su without naming a section size.

    /// <summary>Maps a reference table name to the Section II-D table its notes are printed in.</summary>
    /// <param name="tableName">Reference database table name.</param>
    /// <returns>The matching ASME table identifier.</returns>
    let private strengthTableReference tableName =
        match tableName with
        | "YieldStrengthTable" -> TableSy
        | "UltimateStrengthTable" -> TableSu
        | _ -> TableSy

    /// <summary>One row of a pivoted strength table, before it becomes a dataset.</summary>
    type private StrengthRow =
        { RowId: int64
          Values: Map<int, float>
          SizeRange: SizeThicknessRange
          NoteReferences: AsmeNoteReference list }

    /// <summary>Reads every published row of one minimum-strength table.</summary>
    /// <param name="connection">Open connection to the reference database.</param>
    /// <param name="materialId">Value of <c>Materials.ID</c>.</param>
    /// <param name="tableName">Either <c>YieldStrengthTable</c> or <c>UltimateStrengthTable</c>.</param>
    /// <returns>Rows carrying values in MPa keyed by temperature in degC, empty ones dropped.</returns>
    let private loadStrengthRows (connection: SqliteConnection) materialId tableName =
        use command = connection.CreateCommand()
        command.CommandText <- $"SELECT * FROM {tableName} WHERE MaterialID = $materialId ORDER BY ID"
        command.Parameters.AddWithValue("$materialId", materialId) |> ignore
        use reader = command.ExecuteReader()
        let rows = ResizeArray<StrengthRow>()

        while reader.Read() do
            let values =
                temperatures
                |> List.choose (fun temperature ->
                    optionalNumber reader $"T_{temperature}"
                    |> Option.map (fun value -> temperature, value))
                |> Map.ofList

            // A row with no populated temperature column carries no curve at all; keeping it would
            // add an empty size band the caller could select and get nothing from.
            if not values.IsEmpty then
                rows.Add
                    { RowId = reader.GetInt64(reader.GetOrdinal "ID")
                      Values = values
                      SizeRange = sizeRangeFromReader reader
                      NoteReferences =
                        optionalText reader "Notes"
                        |> AsmeNoteReference.parse (strengthTableReference tableName) }

        rows |> Seq.toList

    /// <summary>Turns one pivoted strength row into a size-banded dataset.</summary>
    /// <param name="kind">Whether the row is Sy or Su.</param>
    /// <param name="row">Row read from the reference table.</param>
    /// <returns><c>Ok dataset</c>, or the curve-construction error.</returns>
    let private strengthDataset kind (row: StrengthRow) : Result<TensileStrengthDataset, MaterialError> =
        let entries =
            row.Values
            |> Map.toList
            |> List.map (fun (temperature, value) -> PropertyTable.entry (float temperature) value)

        PropertyTable.create1D
            $"{TensileStrengthDataset.kindSymbol kind} vs temperature"
            "Temperature"
            "degC"
            (TensileStrengthDataset.kindSymbol kind)
            "MPa"
            ReturnError
            entries
        |> Result.map (fun table ->
            { DatabaseRowId = row.RowId
              Kind = kind
              Table = table
              SizeRange = row.SizeRange
              AsmeNoteReferences = row.NoteReferences
              Notes = None })

    /// <summary>Picks the governing row of a strength table.</summary>
    /// <param name="rows">Rows of one table.</param>
    /// <returns>The lowest-valued row, or <c>None</c> when the table has none.</returns>
    /// <remarks>
    /// The lowest curve is the conservative choice when no section size has been supplied, and it
    /// keeps the flat <c>TensileProperties</c> list safe to use without consulting the size bands.
    /// Ties break on row identity so the result does not depend on read order.
    /// </remarks>
    let private governingStrengthRow (rows: StrengthRow list) =
        rows
        |> List.sortBy (fun row -> row.Values |> Map.toList |> List.sumBy snd, row.RowId)
        |> List.tryHead

    /// <summary>Loads the governing Sy/Su pairing plus one dataset per published size band.</summary>
    /// <param name="connection">Open connection to the reference database.</param>
    /// <param name="materialId">Value of <c>Materials.ID</c>.</param>
    /// <returns>The governing curve, the size-banded datasets, and the notes both tables carry.</returns>
    let private loadTensileProperties (connection: SqliteConnection) materialId =
        let yieldRows = loadStrengthRows connection materialId "YieldStrengthTable"
        let ultimateRows = loadStrengthRows connection materialId "UltimateStrengthTable"

        let datasets =
            (yieldRows |> List.map (strengthDataset YieldStrengthSy))
            @ (ultimateRows |> List.map (strengthDataset UltimateTensileStrengthSu))
            |> List.choose (function
                | Ok dataset -> Some dataset
                | Error _ -> None)
            |> List.sortBy TensileStrengthDataset.sortKey

        let governing =
            match governingStrengthRow yieldRows, governingStrengthRow ultimateRows with
            | Some yieldRow, Some ultimateRow ->
                temperatures
                |> List.choose (fun temperature ->
                    match Map.tryFind temperature yieldRow.Values, Map.tryFind temperature ultimateRow.Values with
                    | Some yieldStrength, Some tensileStrength ->
                        Some
                            { Temperature = float temperature
                              YieldStrength = yieldStrength
                              TensileStrength = tensileStrength }
                    | _ -> None)
            | _ -> []

        let notes =
            (yieldRows @ ultimateRows)
            |> List.collect (fun row -> row.NoteReferences)
            |> List.distinct

        governing, datasets, notes

    // -- Physical properties from the reference schema -------------------------
    //
    // These tables are stored pivoted: one column per temperature, named T_<degC>. Three of them
    // are keyed by a *group* rather than by the material, through MaterialGroupMap, because ASME
    // publishes them per material group; SpecificHeatTable is keyed by MaterialID directly.
    //
    // Unit conversions applied here, verified against the shipped database:
    //   ThermalExpansionTable    um/m/degC -> 1/degC  (x 1e-6)
    //   ElasticModulusTable      GPa       -> MPa     (x 1000)
    //   SpecificHeatTable        J/(kg*K)             (no conversion)
    //   ThermalConductivityTable W/(m*K)              (no conversion)

    /// <summary>Converts a wide <c>T_&lt;degC&gt;</c> row into sorted (temperature, value) pairs.</summary>
    /// <param name="reader">Reader positioned on the row.</param>
    /// <param name="scale">Factor applied to each value to reach the domain's fixed unit.</param>
    /// <returns>Pairs for every non-null temperature column, ordered by temperature.</returns>
    let private unpivotTemperatureRow (reader: SqliteDataReader) (scale: float) : (float * float) list =
        [ for index in 0 .. reader.FieldCount - 1 do
              let name = reader.GetName index

              if name.StartsWith("T_", StringComparison.Ordinal) && not (reader.IsDBNull index) then
                  match Double.TryParse(name.Substring 2, NumberStyles.Float, CultureInfo.InvariantCulture) with
                  | true, temperature -> yield temperature, reader.GetDouble index * scale
                  | _ -> () ]
        |> List.sortBy fst

    /// <summary>Reads one pivoted physical-property row and unpivots it.</summary>
    /// <param name="connection">Open connection.</param>
    /// <param name="tableName">Table to read.</param>
    /// <param name="keyColumn">Column identifying the row (<c>ID</c> or <c>MaterialID</c>).</param>
    /// <param name="keyValue">Value of that column.</param>
    /// <param name="scale">Unit conversion factor.</param>
    /// <returns>(temperature, value) pairs, or an empty list when the table or row is absent.</returns>
    let private loadWideTable
        (connection: SqliteConnection)
        (tableName: string)
        (keyColumn: string)
        (keyValue: int64)
        (scale: float)
        : (float * float) list =
        if not (tableExists connection tableName) then
            []
        else
            use command = connection.CreateCommand()
            command.CommandText <- $"SELECT * FROM {tableName} WHERE {keyColumn} = $key LIMIT 1"
            command.Parameters.AddWithValue("$key", keyValue) |> ignore
            use reader = command.ExecuteReader()
            if reader.Read() then unpivotTemperatureRow reader scale else []

    /// <summary>Reads a single numeric column from the material's <c>Materials</c> row.</summary>
    /// <param name="connection">Open connection.</param>
    /// <param name="columnName">Column to read.</param>
    /// <param name="materialId">Value of <c>Materials.ID</c>.</param>
    /// <returns><c>Some value</c>, or <c>None</c> when absent or NULL.</returns>
    let private materialScalar (connection: SqliteConnection) (columnName: string) (materialId: int64) : float option =
        use command = connection.CreateCommand()
        command.CommandText <- $"SELECT {columnName} FROM Materials WHERE ID = $id"
        command.Parameters.AddWithValue("$id", materialId) |> ignore

        match command.ExecuteScalar() with
        | null -> None
        | value when value.Equals(box DBNull.Value) -> None
        | value -> Some(Convert.ToDouble(value, CultureInfo.InvariantCulture))

    /// <summary>Resolves the property-group identifiers a material belongs to.</summary>
    /// <param name="connection">Open connection.</param>
    /// <param name="materialId">Value of <c>Materials.ID</c>.</param>
    /// <returns>
    /// Elastic-modulus, thermal-expansion, thermal-conductivity, and thermal-diffusivity group
    /// identifiers, each <c>None</c> when the mapping row or column is absent.
    /// </returns>
    let private propertyGroups
        (connection: SqliteConnection)
        (materialId: int64)
        : int64 option * int64 option * int64 option * int64 option =
        if not (tableExists connection "MaterialGroupMap") then
            None, None, None, None
        else
            use command = connection.CreateCommand()

            command.CommandText <-
                "SELECT ElasticModulusGroupID, ThermalExpansionGroupID, ThermalConductivityGroupID,
                        ThermalDiffusivityGroupID
                 FROM MaterialGroupMap WHERE MaterialID = $id"

            command.Parameters.AddWithValue("$id", materialId) |> ignore
            use reader = command.ExecuteReader()

            if reader.Read() then
                let get index =
                    if reader.IsDBNull index then None else Some(reader.GetInt64 index)

                get 0, get 1, get 2, get 3
            else
                None, None, None, None

    /// <summary>Builds the physical-properties table for a reference material.</summary>
    /// <param name="connection">Open connection.</param>
    /// <param name="materialId">Value of <c>Materials.ID</c>.</param>
    /// <param name="existing">Table already carrying the scalar density point.</param>
    /// <returns>A table populated from every physical-property source the schema provides.</returns>
    let private loadPhysicalProperties
        (connection: SqliteConnection)
        (materialId: int64)
        (existing: PhysicalPropertiesTable)
        : PhysicalPropertiesTable =
        let elasticGroup, expansionGroup, conductivityGroup, diffusivityGroup =
            propertyGroups connection materialId
        let poissonRatio = materialScalar connection "PoissonFactor" materialId

        let byGroup tableName group scale =
            match group with
            | Some id -> loadWideTable connection tableName "ID" id scale
            | None -> []

        let thermalExpansion =
            byGroup "ThermalExpansionTable" expansionGroup 1e-6
            |> List.map (fun (temperature, coefficient) ->
                { Temperature = temperature
                  ExpansionCoefficient = coefficient })

        let elasticModulus =
            byGroup "ElasticModulusTable" elasticGroup 1000.0
            |> List.map (fun (temperature, modulus) ->
                ElasticModulusTablePoint.create temperature modulus poissonRatio)

        let thermalConductivity = byGroup "ThermalConductivityTable" conductivityGroup 1.0

        // The database publishes diffusivity in mm^2/s; the domain uses coherent SI, matching how
        // thermal expansion is converted from um/m/degC.
        let thermalDiffusivity = byGroup "ThermalDiffusivityTable" diffusivityGroup 1e-6

        let specificHeat =
            loadWideTable connection "SpecificHeatTable" "MaterialID" materialId 1.0
            |> List.map (fun (temperature, value) ->
                { Temperature = temperature
                  SpecificHeat = value })

        { existing with
            ThermalExpansionTable = thermalExpansion
            ElasticModulusTable = elasticModulus
            // Absent data stays None rather than becoming an empty list, so callers can tell
            // "not recorded" from "recorded as empty".
            SpecificHeatTable = (if specificHeat.IsEmpty then None else Some specificHeat)
            ThermalConductivityTable =
                (if thermalConductivity.IsEmpty then None else Some thermalConductivity)
            ThermalDiffusivityTable =
                (if thermalDiffusivity.IsEmpty then None else Some thermalDiffusivity) }

    /// <summary>Reads the ASME P-Number and Group-Number classification for a material.</summary>
    /// <param name="connection">Open connection.</param>
    /// <param name="materialId">Value of <c>Materials.ID</c>.</param>
    /// <returns><c>Some</c> welding info when either number is recorded, otherwise <c>None</c>.</returns>
    let private loadWeldingInfo (connection: SqliteConnection) (materialId: int64) : WeldingInfo option =
        if not (tableExists connection "DataTableASME") then
            None
        else
            use command = connection.CreateCommand()
            command.CommandText <- "SELECT Pnum, Gnum FROM DataTableASME WHERE MaterialID = $id LIMIT 1"
            command.Parameters.AddWithValue("$id", materialId) |> ignore
            use reader = command.ExecuteReader()

            if reader.Read() then
                let value index =
                    if reader.IsDBNull index then "" else reader.GetString index

                let pNumber = value 0
                let gNumber = value 1

                if String.IsNullOrWhiteSpace pNumber && String.IsNullOrWhiteSpace gNumber then
                    None
                else
                    Some { PNumber = pNumber; GNumber = gNumber }
            else
                None

    let private hydrate (connection: SqliteConnection) (material: Material) =
        let databaseId = Int64.Parse(material.Id)

        let tensileProperties, tensileDatasets, tensileNotes =
            loadTensileProperties connection databaseId

        let sources =
            [ Division1AllowableStress
              if tableExists connection "AllowableStress1HTable" then
                  Division1HighAllowableStress
              Division2AllowableStress
              BoltingAllowableStress ]

        sources
        |> List.map (loadSourceRows connection databaseId)
        |> List.fold
            (fun state item ->
                state
                |> Result.bind (fun datasets -> item |> Result.map (fun values -> values @ datasets)))
            (Ok [])
        |> Result.map (fun datasets ->
            let applicableCodes =
                datasets
                |> List.collect (fun dataset ->
                    match dataset.Source with
                    | Division1AllowableStress -> [ AsmeSectionI; AsmeSectionVIII1 ]
                    | Division1HighAllowableStress -> [ AsmeSectionI; AsmeSectionVIII1 ]
                    | Division2AllowableStress -> [ AsmeSectionVIII2 ]
                    | BoltingAllowableStress -> [ AsmeSectionI; AsmeSectionVIII1; AsmeSectionVIII2 ])
                |> List.distinct

            { material with
                AsmeNoteReferences = List.distinct (material.AsmeNoteReferences @ tensileNotes)
                ApplicableAsmeCodes = applicableCodes
                PhysicalProperties = loadPhysicalProperties connection databaseId material.PhysicalProperties
                WeldingInfo = loadWeldingInfo connection databaseId
                StrengthProperties =
                    { material.StrengthProperties with
                        TensileProperties = tensileProperties
                        TensileStrengthDatasets = tensileDatasets
                        AllowableStressDatasets = datasets |> List.sortBy AllowableStressDataset.sortKey } })

    let findMany databasePath criteria =
        if String.IsNullOrWhiteSpace databasePath || not (File.Exists databasePath) then
            Error(MaterialError.NotFound $"ASME material database not found: {databasePath}")
        else
            try
                use connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly")
                connection.Open()

                loadCandidates connection
                |> MaterialFiltering.findMany criteria
                |> List.map (hydrate connection)
                |> List.fold
                    (fun state item ->
                        state
                        |> Result.bind (fun materials -> item |> Result.map (fun material -> material :: materials)))
                    (Ok [])
                |> Result.map List.rev
            with ex ->
                Error(MaterialError.InvalidOperation $"ASME database lookup failed: {ex.Message}")

    /// <summary>
    /// Loads one reference material by its integer <c>Materials.ID</c> primary key.
    /// </summary>
    /// <param name="databasePath">Path to the ASME SQLite database.</param>
    /// <param name="databaseId">Value of <c>Materials.ID</c>.</param>
    /// <returns><c>Ok material</c> fully hydrated from the reference tables, or an error.</returns>
    /// <remarks>
    /// The criteria-based lookups match on specification and grade, which can legitimately return
    /// several rows. Selecting a row in the user interface identifies exactly one material, so that
    /// selection needs a key-based lookup rather than a search that might resolve elsewhere.
    /// </remarks>
    let findById (databasePath: string) (databaseId: int64) : Result<Material, MaterialError> =
        if String.IsNullOrWhiteSpace databasePath || not (File.Exists databasePath) then
            Error(MaterialError.NotFound $"ASME material database not found: {databasePath}")
        else
            try
                use connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly")
                connection.Open()

                // The reader is closed before hydrate runs, because hydrate issues further
                // commands on the same connection and SQLite will not allow that while it is open.
                let candidate =
                    use command = connection.CreateCommand()
                    command.CommandText <- "SELECT * FROM Materials WHERE ID = $id"
                    command.Parameters.AddWithValue("$id", databaseId) |> ignore
                    use reader = command.ExecuteReader()

                    if reader.Read() then Some(materialFromReader reader) else None

                match candidate with
                | Some material -> hydrate connection material
                | None ->
                    Error(MaterialError.NotFound $"Material not found in the ASME reference tables: {databaseId}")
            with ex ->
                Error(MaterialError.InvalidOperation $"ASME database lookup failed: {ex.Message}")

    let findUnique databasePath criteria =
        findMany databasePath criteria
        |> Result.bind (MaterialFiltering.findUnique criteria)

    /// <summary>
    /// Checks that the ASME database at <paramref name="databasePath"/> exists and can actually be
    /// opened read-only as a SQLite database, without loading any material rows.
    /// </summary>
    /// <remarks>
    /// File.Exists alone does not guarantee the file is readable or a valid SQLite database (locked
    /// file, corrupted header, wrong file type); this opens a real read-only connection and runs a
    /// trivial query against <c>sqlite_master</c> to confirm it.
    /// </remarks>
    let checkAccessible (databasePath: string) : Result<unit, MaterialError> =
        if String.IsNullOrWhiteSpace databasePath || not (File.Exists databasePath) then
            Error(MaterialError.NotFound $"ASME material database not found: {databasePath}")
        else
            try
                use connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly")
                connection.Open()
                use command = connection.CreateCommand()
                command.CommandText <- "SELECT COUNT(*) FROM sqlite_master"
                command.ExecuteScalar() |> ignore
                Ok()
            with ex ->
                Error(MaterialError.InvalidOperation $"ASME database at '{databasePath}' could not be opened: {ex.Message}")

    /// <summary>
    /// Resolves several search criteria against one open connection and one load of the Materials
    /// table, in the order the criteria are supplied.
    /// </summary>
    /// <remarks>
    /// <see cref="findMany"/> (and therefore <see cref="findUnique"/>) opens a fresh connection and
    /// re-reads the whole Materials table for every call, which is wasteful when resolving a fixed
    /// batch of known materials (e.g. <c>RequestedMaterialLibrary</c>). This function loads the
    /// candidate rows once and reuses them for every criterion.
    /// </remarks>
    /// <param name="databasePath">Path to the ASME material SQLite database.</param>
    /// <param name="criteriaList">Search criteria to resolve, one material expected per entry.</param>
    /// <returns>Materials in the same order as <paramref name="criteriaList"/>, or the first error encountered.</returns>
    let findUniqueMany databasePath (criteriaList: MaterialSearchCriteria list) : Result<Material list, MaterialError> =
        if String.IsNullOrWhiteSpace databasePath || not (File.Exists databasePath) then
            Error(MaterialError.NotFound $"ASME material database not found: {databasePath}")
        else
            try
                use connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly")
                connection.Open()

                let candidates = loadCandidates connection

                let resolveOne criteria =
                    candidates
                    |> MaterialFiltering.findMany criteria
                    |> List.map (hydrate connection)
                    |> List.fold
                        (fun state item ->
                            state
                            |> Result.bind (fun materials -> item |> Result.map (fun material -> material :: materials)))
                        (Ok [])
                    |> Result.map List.rev
                    |> Result.bind (MaterialFiltering.findUnique criteria)

                criteriaList
                |> List.map resolveOne
                |> List.fold
                    (fun state item ->
                        state
                        |> Result.bind (fun materials -> item |> Result.map (fun material -> material :: materials)))
                    (Ok [])
                |> Result.map List.rev
            with ex ->
                Error(MaterialError.InvalidOperation $"ASME database lookup failed: {ex.Message}")
