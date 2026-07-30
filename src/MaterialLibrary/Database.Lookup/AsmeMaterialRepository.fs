namespace MaterialLibrary.Domain.Database.Lookup

open System
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

        let basic =
            BasicProperties.create
                (optionalNumber reader "RuptureElongationLong" |> Option.defaultValue 0.0)
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
                        SizeMinimum = optionalNumber reader "SizeThkMIN"
                        SizeMaximum = optionalNumber reader "SizeThkMAX"
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
            |> List.groupBy (fun (dataset, _) -> dataset.SizeMinimum, dataset.SizeMaximum)
            |> List.collect (snd >> classifyGroup)
            |> List.map AllowableStressDataset.validate
            |> List.fold
                (fun state item ->
                    state
                    |> Result.bind (fun values -> item |> Result.map (fun value -> value :: values)))
                (Ok [])
            |> Result.map List.rev

    let private strengthTableReference tableName =
        match tableName with
        | "YieldStrengthTable" -> TableSy
        | "UltimateStrengthTable" -> TableSu
        | _ -> TableSy

    let private loadStrengthTable (connection: SqliteConnection) materialId tableName =
        use command = connection.CreateCommand()
        command.CommandText <- $"SELECT * FROM {tableName} WHERE MaterialID = $materialId ORDER BY ID"
        command.Parameters.AddWithValue("$materialId", materialId) |> ignore
        use reader = command.ExecuteReader()
        let rows = ResizeArray<Map<int, float> * AsmeNoteReference list * int64>()

        while reader.Read() do
            let values =
                temperatures
                |> List.choose (fun temperature ->
                    optionalNumber reader $"T_{temperature}"
                    |> Option.map (fun value -> temperature, value))
                |> Map.ofList

            if not values.IsEmpty then
                let noteReferences =
                    optionalText reader "Notes"
                    |> AsmeNoteReference.parse (strengthTableReference tableName)

                rows.Add(values, noteReferences, reader.GetInt64(reader.GetOrdinal "ID"))

        rows
        |> Seq.toList
        |> List.sortBy (fun (values, _, rowId) -> values |> Map.toList |> List.sumBy snd, rowId)
        |> List.tryHead

    let private loadTensileProperties (connection: SqliteConnection) materialId elongation =
        match
            loadStrengthTable connection materialId "YieldStrengthTable",
            loadStrengthTable connection materialId "UltimateStrengthTable"
        with
        | Some(yieldValues, yieldNotes, _), Some(ultimateValues, ultimateNotes, _) ->
            let tensileProperties =
                temperatures
                |> List.choose (fun temperature ->
                    match Map.tryFind temperature yieldValues, Map.tryFind temperature ultimateValues with
                    | Some yieldStrength, Some tensileStrength ->
                        Some
                            { Temperature = float temperature
                              YieldStrength = yieldStrength
                              TensileStrength = tensileStrength
                              ElongationPercent = elongation
                              ReductionOfAreaPercent = 0.0 }
                    | _ -> None)

            tensileProperties, List.distinct (yieldNotes @ ultimateNotes)
        | _ -> [], []

    let private allowableDatasetSortKey (dataset: AllowableStressDataset) =
        let lower = dataset.SizeMinimum |> Option.defaultValue Double.NegativeInfinity
        let upper = dataset.SizeMaximum |> Option.defaultValue Double.PositiveInfinity
        dataset.Source, lower, upper, dataset.DatabaseRowId

    let private hydrate (connection: SqliteConnection) (material: Material) =
        let databaseId = Int64.Parse(material.Id)
        let tensileProperties, tensileNotes =
            loadTensileProperties
                connection
                databaseId
                material.BasicProperties.ElongationPercent

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
                StrengthProperties =
                    { material.StrengthProperties with
                        TensileProperties = tensileProperties
                        AllowableStressDatasets = datasets |> List.sortBy allowableDatasetSortKey } })

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
