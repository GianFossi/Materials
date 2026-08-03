namespace MaterialLibrary.Domain.Database.Lookup

open System
open System.Globalization
open System.IO
open Microsoft.Data.Sqlite
open MaterialLibrary.Domain

module AsmeMaterialRepository =
    let private temperatures =
        [ 40
          65
          100
          125
          150
          175
          200
          225
          250
          275
          300
          325
          350
          375
          400
          425
          450
          475
          500
          525
          550
          575
          600
          625
          650
          675
          700
          725
          750
          775
          800
          825
          850
          875
          900 ]

    let private text (reader: SqliteDataReader) name =
        let ordinal = reader.GetOrdinal name

        if reader.IsDBNull ordinal then
            ""
        else
            reader.GetString ordinal

    let private optionalText (reader: SqliteDataReader) name =
        let value = text reader name
        if String.IsNullOrWhiteSpace value then None else Some value

    let private number (reader: SqliteDataReader) name =
        let ordinal = reader.GetOrdinal name

        if reader.IsDBNull ordinal then
            0.0
        else
            reader.GetDouble ordinal

    let private optionalNumber (reader: SqliteDataReader) name =
        let ordinal = reader.GetOrdinal name

        if reader.IsDBNull ordinal then
            None
        else
            Some(reader.GetDouble ordinal)

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
                [ { Temperature = 20.0
                    Density = density } ]
                None
                None

        Material.create $"{databaseId}" $"{specification} {grade}" specification grade basic physical
        |> Material.setIdentity productForm composition specification grade classCondition uns
        |> fun material ->
            { material with
                Family = AsmeMaterialFamilyClassification.classify specification grade classCondition composition uns
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

    /// <summary>Reports whether a table exposes a given column name.</summary>
    /// <param name="connection">Open connection.</param>
    /// <param name="tableName">Table to inspect.</param>
    /// <param name="columnName">Column name to look for (case-insensitive).</param>
    /// <returns><c>true</c> when the column exists.</returns>
    let private tableHasColumn (connection: SqliteConnection) (tableName: string) (columnName: string) : bool =
        if not (tableExists connection tableName) then
            false
        else
            use command = connection.CreateCommand()
            command.CommandText <- $"PRAGMA table_info({tableName})"
            use reader = command.ExecuteReader()
            let mutable found = false

            while not found && reader.Read() do
                found <-
                    String.Equals(
                        reader.GetString(reader.GetOrdinal "name"),
                        columnName,
                        StringComparison.OrdinalIgnoreCase
                    )

            found

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
                        Notes = None }
                     : AllowableStressDataset),
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
                           |> List.exists (fun note -> note.Code.Equals("G5", StringComparison.OrdinalIgnoreCase)))
                    ->
                    let lowRowId =
                        multiple |> List.minBy snd |> fst |> (fun dataset -> dataset.DatabaseRowId)

                    let highRowId =
                        multiple |> List.maxBy snd |> fst |> (fun dataset -> dataset.DatabaseRowId)

                    multiple
                    |> List.map (fun (dataset, _) ->
                        if dataset.DatabaseRowId = highRowId then
                            { dataset with
                                Source = Division1HighAllowableStress
                                Case = HighStrengthAllowableStress }
                        elif dataset.DatabaseRowId = lowRowId then
                            { dataset with
                                Case = StandardStrengthAllowableStress }
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

    let private loadStrengthTable2D
        (connection: SqliteConnection)
        (materialId: int64)
        (tableName: string)
        (yAxisName: string)
        : Result<PropertyTable option * AsmeNoteReference list, MaterialError> =

        use command = connection.CreateCommand()
        command.CommandText <- $"SELECT * FROM {tableName} WHERE MaterialID = $materialId ORDER BY ID"
        command.Parameters.AddWithValue("$materialId", materialId) |> ignore
        use reader = command.ExecuteReader()

        let columnAccumulator = ResizeArray<TableColumn * AsmeNoteReference list>()
        let mutable error: MaterialError option = None

        while error.IsNone && reader.Read() do
            let entries =
                temperatures
                |> List.choose (fun temperature ->
                    optionalNumber reader $"T_{temperature}"
                    |> Option.map (fun value -> PropertyTable.entry (float temperature) value))

            if not entries.IsEmpty then
                let sizeMin = optionalNumber reader "SizeThkMIN"
                let sizeMax = optionalNumber reader "SizeThkMAX"

                // Explicitly typed to avoid resolution ambiguity with SizeRangeBoundJson.
                let lower: SizeRangeBound option =
                    sizeMin |> Option.map (fun v -> { Value = v; Inclusion = Exclusive })

                let upper: SizeRangeBound option =
                    sizeMax |> Option.map (fun v -> { Value = v; Inclusion = Inclusive })

                let sizeRange: SizeColumnRange =
                    { Lower = lower
                      Upper = upper
                      Label = None }

                let noteReferences =
                    optionalText reader "Notes"
                    |> AsmeNoteReference.parse (strengthTableReference tableName)

                columnAccumulator.Add(
                    { SizeRange = sizeRange
                      Entries = entries },
                    noteReferences
                )

        match error with
        | Some e -> Error e
        | None ->
            let columns = columnAccumulator |> Seq.toList

            if columns.IsEmpty then
                Ok(None, [])
            else
                let allNotes = columns |> List.collect snd |> List.distinct
                let tableColumns = columns |> List.map fst

                let dimensionType =
                    if
                        tableColumns
                        |> List.forall (fun c -> c.SizeRange.Lower.IsNone && c.SizeRange.Upper.IsNone)
                    then
                        NoDimension
                    else
                        Thickness

                let tableResult =
                    if dimensionType = NoDimension then
                        match tableColumns with
                        | [ single ] ->
                            PropertyTable.create1D
                                tableName
                                "Temperature"
                                "degC"
                                yAxisName
                                "MPa"
                                FlatExtrapolate
                                single.Entries
                        | _ ->
                            // Multiple rows with no size range: pick the highest-sum row.
                            let best =
                                tableColumns |> List.maxBy (fun c -> c.Entries |> List.sumBy (fun e -> e.Value))

                            PropertyTable.create1D
                                tableName
                                "Temperature"
                                "degC"
                                yAxisName
                                "MPa"
                                FlatExtrapolate
                                best.Entries
                    else
                        PropertyTable.create2D
                            tableName
                            "Temperature"
                            "degC"
                            yAxisName
                            "MPa"
                            Thickness
                            "mm"
                            FlatExtrapolate
                            tableColumns

                tableResult |> Result.map (fun table -> Some table, allNotes)

    /// <summary>Orders datasets by source, then from the lightest size band to the heaviest.</summary>
    /// <param name="dataset">Dataset to rank.</param>
    /// <returns>A tuple usable directly with <c>List.sortBy</c>.</returns>
    let private allowableDatasetSortKey (dataset: AllowableStressDataset) =
        let lower = dataset.SizeMinimum |> Option.defaultValue Double.NegativeInfinity
        let upper = dataset.SizeMaximum |> Option.defaultValue Double.PositiveInfinity
        dataset.Source, lower, upper, dataset.DatabaseRowId

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

            if reader.Read() then
                unpivotTemperatureRow reader scale
            else
                []

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

    type private PropertyGroups =
        { ElasticModulusGroupID: int64 option
          ThermalExpansionGroupID: int64 option
          ThermalConductivityGroupID: int64 option
          ThermalDiffusivityGroupID: int64 option
          SpecificHeatGroupID: int64 option
          DensityGroupID: int64 option
          PoissonRatioGroupID: int64 option }

    /// <summary>Resolves the property-group identifiers a material belongs to.</summary>
    /// <param name="connection">Open connection.</param>
    /// <param name="materialId">Value of <c>Materials.ID</c>.</param>
    /// <returns>
    /// Group identifiers for physical-property tables; each field is <c>None</c> when the mapping row
    /// or column is absent.
    /// </returns>
    let private propertyGroups (connection: SqliteConnection) (materialId: int64) : PropertyGroups =
        let empty =
            { ElasticModulusGroupID = None
              ThermalExpansionGroupID = None
              ThermalConductivityGroupID = None
              ThermalDiffusivityGroupID = None
              SpecificHeatGroupID = None
              DensityGroupID = None
              PoissonRatioGroupID = None }

        if not (tableExists connection "MaterialGroupMap") then
            empty
        else
            let selectColumns =
                [ "ElasticModulusGroupID"
                  "ThermalExpansionGroupID"
                  "ThermalConductivityGroupID"
                  "ThermalDiffusivityGroupID"
                  // Optional columns for databases that also group these properties.
                  "SpecificHeatGroupID"
                  "DensityGroupID"
                  "PoissonRatioGroupID" ]
                |> List.filter (tableHasColumn connection "MaterialGroupMap")

            if selectColumns.IsEmpty then
                empty
            else
                let getInt64 (reader: SqliteDataReader) (name: string) =
                    let ordinal = reader.GetOrdinal name

                    if reader.IsDBNull ordinal then
                        None
                    else
                        Some(reader.GetInt64 ordinal)

                let selected = String.Join(", ", selectColumns)

                use command = connection.CreateCommand()

                command.CommandText <- $"SELECT {selected} FROM MaterialGroupMap WHERE MaterialID = $id"

                command.Parameters.AddWithValue("$id", materialId) |> ignore
                use reader = command.ExecuteReader()

                if reader.Read() then
                    { ElasticModulusGroupID =
                        if selectColumns |> List.contains "ElasticModulusGroupID" then
                            getInt64 reader "ElasticModulusGroupID"
                        else
                            None
                      ThermalExpansionGroupID =
                        if selectColumns |> List.contains "ThermalExpansionGroupID" then
                            getInt64 reader "ThermalExpansionGroupID"
                        else
                            None
                      ThermalConductivityGroupID =
                        if selectColumns |> List.contains "ThermalConductivityGroupID" then
                            getInt64 reader "ThermalConductivityGroupID"
                        else
                            None
                      ThermalDiffusivityGroupID =
                        if selectColumns |> List.contains "ThermalDiffusivityGroupID" then
                            getInt64 reader "ThermalDiffusivityGroupID"
                        else
                            None
                      SpecificHeatGroupID =
                        if selectColumns |> List.contains "SpecificHeatGroupID" then
                            getInt64 reader "SpecificHeatGroupID"
                        else
                            None
                      DensityGroupID =
                        if selectColumns |> List.contains "DensityGroupID" then
                            getInt64 reader "DensityGroupID"
                        else
                            None
                      PoissonRatioGroupID =
                        if selectColumns |> List.contains "PoissonRatioGroupID" then
                            getInt64 reader "PoissonRatioGroupID"
                        else
                            None }
                else
                    empty

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
        let groups = propertyGroups connection materialId

        let byGroupOrMaterial tableName group scale =
            // Prefer compiled group rows first (shared ASME groups), then fallback to the material
            // keyed row when that exists in a database variant.
            let byGroupRows =
                match group with
                | Some id when tableHasColumn connection tableName "ID" ->
                    loadWideTable connection tableName "ID" id scale
                | _ -> []

            if not byGroupRows.IsEmpty then
                byGroupRows
            elif tableHasColumn connection tableName "MaterialID" then
                loadWideTable connection tableName "MaterialID" materialId scale
            elif tableHasColumn connection tableName "ID" then
                // Last fallback: some datasets use ID as the material key directly.
                loadWideTable connection tableName "ID" materialId scale
            else
                []

        let scalarPoisson = materialScalar connection "PoissonFactor" materialId

        let poissionReference20C =
            byGroupOrMaterial "PoissonRatioTable" groups.PoissonRatioGroupID 1.0
            |> List.tryFind (fun (temperature, _) -> abs (temperature - 20.0) < 0.0001)
            |> Option.map snd

        let poissonRatio = poissionReference20C |> Option.orElse scalarPoisson

        let densityAt20C =
            byGroupOrMaterial "DensityTable" groups.DensityGroupID 1.0
            |> List.tryFind (fun (temperature, _) -> abs (temperature - 20.0) < 0.0001)
            |> Option.map snd

        let thermalExpansion =
            byGroupOrMaterial "ThermalExpansionTable" groups.ThermalExpansionGroupID 1e-6
            |> List.map (fun (temperature, coefficient) ->
                { Temperature = temperature
                  ExpansionCoefficient = coefficient })

        let elasticModulus =
            byGroupOrMaterial "ElasticModulusTable" groups.ElasticModulusGroupID 1000.0
            |> List.map (fun (temperature, modulus) -> ElasticModulusTablePoint.create temperature modulus poissonRatio)

        let thermalConductivity =
            byGroupOrMaterial "ThermalConductivityTable" groups.ThermalConductivityGroupID 1.0

        // The database publishes diffusivity in mm^2/s; the domain uses coherent SI, matching how
        // thermal expansion is converted from um/m/degC.
        let thermalDiffusivity =
            byGroupOrMaterial "ThermalDiffusivityTable" groups.ThermalDiffusivityGroupID 1e-6

        let specificHeat =
            byGroupOrMaterial "SpecificHeatTable" groups.SpecificHeatGroupID 1.0
            |> List.map (fun (temperature, value) ->
                { Temperature = temperature
                  SpecificHeat = value })

        let densityTable =
            match densityAt20C with
            | Some density ->
                [ { Temperature = 20.0
                    Density = density } ]
            | None -> existing.DensityTable

        { existing with
            ThermalExpansionTable = thermalExpansion
            ElasticModulusTable = elasticModulus
            DensityTable = densityTable
            // Absent data stays None rather than becoming an empty list, so callers can tell
            // "not recorded" from "recorded as empty".
            SpecificHeatTable = (if specificHeat.IsEmpty then None else Some specificHeat)
            ThermalConductivityTable =
                (if thermalConductivity.IsEmpty then
                     None
                 else
                     Some thermalConductivity)
            ThermalDiffusivityTable =
                (if thermalDiffusivity.IsEmpty then
                     None
                 else
                     Some thermalDiffusivity) }

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

        let sources =
            [ Division1AllowableStress
              if tableExists connection "AllowableStress1HTable" then
                  Division1HighAllowableStress
              Division2AllowableStress
              BoltingAllowableStress ]

        loadStrengthTable2D connection databaseId "YieldStrengthTable" "Sy"
        |> Result.bind (fun (syTable, syNotes) ->
            loadStrengthTable2D connection databaseId "UltimateStrengthTable" "Su"
            |> Result.bind (fun (suTable, suNotes) ->
                let tensileNotes = List.distinct (syNotes @ suNotes)

                sources
                |> List.map (loadSourceRows connection databaseId)
                |> List.fold
                    (fun state item ->
                        state
                        |> Result.bind (fun datasets -> item |> Result.map (fun values -> values @ datasets)))
                    (Ok [])
                |> Result.map (fun allowableDatasets ->
                    let applicableCodes =
                        allowableDatasets
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
                                SyTable = syTable
                                SuTable = suTable
                                AllowableStressDatasets = allowableDatasets |> List.sortBy allowableDatasetSortKey } })))

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

                    if reader.Read() then
                        Some(materialFromReader reader)
                    else
                        None

                match candidate with
                | Some material -> hydrate connection material
                | None -> Error(MaterialError.NotFound $"Material not found in the ASME reference tables: {databaseId}")
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
                Error(
                    MaterialError.InvalidOperation
                        $"ASME database at '{databasePath}' could not be opened: {ex.Message}"
                )

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
                            |> Result.bind (fun materials ->
                                item |> Result.map (fun material -> material :: materials)))
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
