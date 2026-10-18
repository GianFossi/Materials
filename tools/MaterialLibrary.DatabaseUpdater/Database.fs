namespace MaterialLibrary.DatabaseUpdater

open System
open System.IO
open System.Reflection
open System.Collections.Generic
open Microsoft.Data.Sqlite
open Domain

/// <summary>Comparison identity and source-row multiset for a material in an earlier database.</summary>
type Snapshot = { Id: int64; Identity: Identity; Fingerprints: string list option }

/// <summary>Database writing and read-only baseline inspection.</summary>
module Database =
    /// <summary>Open a database with explicit mode, foreign keys and no connection pooling.</summary>
    let connect path mode =
        let options = SqliteConnectionStringBuilder()
        options.DataSource <- path
        options.Mode <- mode
        options.ForeignKeys <- Nullable true
        options.Pooling <- false
        let db = new SqliteConnection(options.ConnectionString)
        db.Open()
        db

    /// <summary>Run a SQL statement using bound values.</summary>
    let execute (db: SqliteConnection) (tx: SqliteTransaction option) sql (parameters: (string * obj) list) =
        use command = db.CreateCommand()
        command.CommandText <- sql
        tx |> Option.iter (fun transaction -> command.Transaction <- transaction)
        for name, value in parameters do command.Parameters.AddWithValue(name, if isNull value then box DBNull.Value else value) |> ignore
        command.ExecuteNonQuery() |> ignore

    /// <summary>Read query rows as detached CLR values.</summary>
    let query (db: SqliteConnection) sql =
        use command = db.CreateCommand()
        command.CommandText <- sql
        use reader = command.ExecuteReader()
        let rows = ResizeArray<obj array>()
        while reader.Read() do
            rows.Add(Array.init reader.FieldCount (fun i -> if reader.IsDBNull i then null else reader.GetValue i))
        List.ofSeq rows

    /// <summary>Create complete MAIN matrices; curve counts and populated-value availability are separate checks.</summary>
    let private createCoverageViews db =
        let counts = mechanicalCodes |> List.map (fun code ->
            "(SELECT count(*) FROM Material_Properties p WHERE p.id_main=m.id AND p.PropertyCode='" + code + "') AS \"" + code + "\"") |> String.concat ","
        execute db None ("""CREATE VIEW VW_Main_Mechanical_Counts AS
            SELECT m.id AS MaterialId,m.Specification,m.Grade,m.ClassConditionTemper,m.AlloyDesignation AS UNS,m.NominalComposition,i.NaturalKey,
            """ + counts + """,a.HasSy,a.HasSu,a.HasS1,a.HasS2,a.HasSb1,a.HasSb2
            FROM Main m JOIN Material_Identity i ON i.id_main=m.id JOIN VW_Material_Strength_Availability a ON a.id=m.id""") []
        let physical = ["E",["E";"ElasticModulus"]; "Alpha",["Alpha";"ThermalExpansion"]; "c",["c";"Cp";"SpecificHeat"];
                        "k",["k";"TC";"ThermalConductivity"]; "a",["a";"TD";"ThermalDiffusivity"];
                        "density",["density";"Density"]; "Nu",["Nu";"nu";"PoissonRatio"]]
        let flags = physical |> List.map (fun (label,codes) ->
            let names = codes |> List.map (fun code -> "'" + code + "'") |> String.concat ","
            let scalar =
                match label with
                | "density" -> " OR EXISTS(SELECT 1 FROM Extension e WHERE e.id_main=m.id AND e.ConstantDensity IS NOT NULL)"
                | "Nu" -> " OR EXISTS(SELECT 1 FROM Extension e WHERE e.id_main=m.id AND e.ConstantPoissonRatio IS NOT NULL)"
                | _ -> ""
            "(EXISTS(SELECT 1 FROM Physical_Properties p JOIN Physical_Property_Values v ON v.id_physical_property=p.id WHERE p.id_main=m.id AND p.PropertyCode IN (" + names + "))" + scalar + ") AS \"" + label + "\"") |> String.concat ","
        execute db None ("""CREATE VIEW VW_Main_Physical_Availability AS
            SELECT m.id AS MaterialId,m.Specification,m.Grade,m.ClassConditionTemper,m.AlloyDesignation AS UNS,m.NominalComposition,i.NaturalKey,
            """ + flags + " FROM Main m JOIN Material_Identity i ON i.id_main=m.id") []

    /// <summary>Load a supported baseline without modifying or deleting it.</summary>
    let loadBaseline path =
        use db = connect path SqliteOpenMode.ReadOnly
        let tables = query db "SELECT name FROM sqlite_master WHERE type='table'" |> List.map (fun r -> string r[0]) |> Set.ofList
        let normalized = tables.Contains "Main"
        let hasIdentity = tables.Contains "Material_Identity"
        let sql =
            if normalized then "SELECT id,Specification,Grade,AlloyDesignation,ClassConditionTemper FROM Main"
            elif tables.Contains "Materials" then
                "SELECT ID,Specification,TypeGrade,AlloyDesignationNumber,ClassConditionTemper FROM Materials"
            else failwith "Baseline has neither Main nor legacy Materials schema"
        let fingerprints =
            if tables.Contains "Material_Source" then
                query db "SELECT id_main,Fingerprint FROM Material_Source WHERE upper(SourceTable) NOT IN ('6A','6B','6C','6D')" |> List.groupBy (fun r -> Convert.ToInt64 r[0]) |> List.map (fun (id,rs) -> id, (rs |> List.map (fun r -> string r[1]))) |> Map.ofList
            elif tables.Contains "Property_Source" then
                query db "SELECT p.id_main,s.Fingerprint FROM Material_Properties p JOIN Property_Source s ON s.id_property=p.id WHERE upper(s.SourceTable) NOT IN ('6A','6B','6C','6D')" |> List.groupBy (fun r -> Convert.ToInt64 r[0]) |> List.map (fun (id,rs) -> id, (rs |> List.map (fun r -> string r[1]))) |> Map.ofList
            else Map.empty
        let data = query db sql |> List.map (fun row ->
            let id = Convert.ToInt64 row[0]
            let identity = { Specification=normalize (string row[1]); Grade=normalize (string row[2]); Alloy=normalize (string row[3]); Condition=normalize (string row[4]) }
            { Id=id; Identity=identity; Fingerprints=if tables.Contains "Property_Source" || tables.Contains "Material_Source" then Some (Map.tryFind id fingerprints |> Option.defaultValue []) else None })
        if hasIdentity then
            let saved = query db "SELECT id_main,NaturalKey FROM Material_Identity" |> List.map (fun r -> Convert.ToInt64 r[0], string r[1]) |> Map.ofList
            for item in data do
                let stored = Map.tryFind item.Id saved |> Option.defaultWith (fun () -> failwith "Missing baseline identity metadata")
                let parts = System.Text.Json.JsonSerializer.Deserialize<string array>(stored)
                let canonical =
                    match parts.Length with
                    | 4 -> json [|normalize parts[0];normalize parts[1];normalize parts[2];normalize parts[3]|]
                    | 5 -> json [|normalize parts[0];normalize parts[1];normalize parts[3];normalize parts[2]|]
                    | _ -> failwith "Unsupported baseline identity metadata"
                if canonical <> key item.Identity then failwith "Baseline identity metadata is inconsistent"
        let edition =
            if tables.Contains "Database_Metadata" then
                query db "SELECT Value FROM Database_Metadata WHERE Key='Edition'" |> List.tryHead |> Option.map (fun r -> string r[0]) |> Option.defaultValue "unknown"
            else "unknown (legacy database)"
        (if tables.Contains "Material_Source" then data |> List.filter (fun item -> Map.containsKey item.Id fingerprints) else data), edition

    /// <summary>Create a fresh edition database; source rows, size limits and notes remain individually auditable.</summary>
    let create path edition (input: InputData) (baseline: Snapshot list) =
        use db = connect path SqliteOpenMode.ReadWriteCreate
        use resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("MaterialLibrary.DatabaseUpdater.Schema.sql")
        if isNull resource then failwith "Embedded database schema not found"
        use reader = new StreamReader(resource)
        execute db None (reader.ReadToEnd()) []
        execute db None """
        CREATE TABLE Database_Metadata (Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
        CREATE TABLE Material_Identity (id_main INTEGER PRIMARY KEY REFERENCES Main(id) ON DELETE CASCADE, NaturalKey TEXT NOT NULL UNIQUE);
        CREATE TABLE Material_Source (
            id_main INTEGER NOT NULL REFERENCES Main(id) ON DELETE CASCADE,
            SourceTable TEXT NOT NULL, File TEXT NOT NULL, CsvLine INTEGER NOT NULL,
            RawFieldsJson TEXT NOT NULL, Fingerprint TEXT NOT NULL, PRIMARY KEY(File,CsvLine));
        CREATE INDEX IDX_MaterialSource_Main ON Material_Source(id_main);
        CREATE TABLE Import_Files (Path TEXT PRIMARY KEY, SHA256 TEXT NOT NULL, RowCount INTEGER NOT NULL);
        CREATE TABLE Import_Omitted_Rows (SourceTable TEXT NOT NULL, File TEXT NOT NULL, CsvLine INTEGER NOT NULL, Reason TEXT NOT NULL, RawFieldsJson TEXT NOT NULL, PRIMARY KEY(File,CsvLine));
        CREATE TABLE Source_Documents (SourceTable TEXT PRIMARY KEY, SHA256 TEXT NOT NULL, Contents TEXT NOT NULL);
        CREATE TABLE Source_Notes (SourceTable TEXT NOT NULL, Code TEXT NOT NULL, Kind TEXT NOT NULL, NoteText TEXT NOT NULL, PRIMARY KEY(SourceTable,Code));
        CREATE TABLE Property_Source (
            id_property INTEGER PRIMARY KEY REFERENCES Material_Properties(id) ON DELETE CASCADE,
            SourceTable TEXT NOT NULL, File TEXT NOT NULL, CsvLine INTEGER NOT NULL, Page TEXT, SourceLine TEXT, SourceRowNumber TEXT,
            SizeText TEXT, SizeAxis TEXT NOT NULL, MinYieldMPa REAL, MinTensileMPa REAL, RawFieldsJson TEXT NOT NULL, Fingerprint TEXT NOT NULL);
        CREATE INDEX IDX_PropertySource_Table ON Property_Source(SourceTable);
        CREATE TABLE Property_Note_Links (
            id_property INTEGER NOT NULL REFERENCES Material_Properties(id) ON DELETE CASCADE,
            SourceTable TEXT NOT NULL, Code TEXT NOT NULL,
            PRIMARY KEY(id_property,SourceTable,Code), FOREIGN KEY(SourceTable,Code) REFERENCES Source_Notes(SourceTable,Code));
        CREATE TABLE Sb1 (
            id_property INTEGER PRIMARY KEY REFERENCES Material_Properties(id) ON DELETE CASCADE,
            id_main INTEGER NOT NULL REFERENCES Main(id) ON DELETE CASCADE,
            id_size INTEGER NOT NULL REFERENCES Sizes(id));
        CREATE TABLE Sb2 (
            id_property INTEGER PRIMARY KEY REFERENCES Material_Properties(id) ON DELETE CASCADE,
            id_main INTEGER NOT NULL REFERENCES Main(id) ON DELETE CASCADE,
            id_size INTEGER NOT NULL REFERENCES Sizes(id));
        CREATE TRIGGER Validate_Sb1_Insert BEFORE INSERT ON Sb1 BEGIN
            SELECT CASE WHEN NOT EXISTS(SELECT 1 FROM Material_Properties p JOIN Property_Source s ON s.id_property=p.id
                WHERE p.id=NEW.id_property AND p.id_main=NEW.id_main AND p.id_size=NEW.id_size AND p.PropertyCode='Sb1' AND s.SourceTable='3')
                THEN RAISE(ABORT,'Sb1 accepts only Table 3 bolting curves') END;
        END;
        CREATE TRIGGER Validate_Sb2_Insert BEFORE INSERT ON Sb2 BEGIN
            SELECT CASE WHEN NOT EXISTS(SELECT 1 FROM Material_Properties p JOIN Property_Source s ON s.id_property=p.id
                WHERE p.id=NEW.id_property AND p.id_main=NEW.id_main AND p.id_size=NEW.id_size AND p.PropertyCode='Sb2' AND s.SourceTable='4')
                THEN RAISE(ABORT,'Sb2 accepts only Table 4 bolting curves') END;
        END;
        CREATE VIEW VW_Property_Notes_All AS
            SELECT l.id_property,n.SourceTable,n.Code,n.Kind,n.NoteText FROM Property_Note_Links l
                JOIN Source_Notes n ON n.SourceTable=l.SourceTable AND n.Code=l.Code
            UNION ALL
            SELECT s.id_property,n.SourceTable,n.Code,n.Kind,n.NoteText FROM Property_Source s
                JOIN Source_Notes n ON n.SourceTable=s.SourceTable AND n.Kind='General';
        CREATE VIEW S1 AS SELECT m.id AS id_main,m.Specification,m.Grade,m.AlloyDesignation,m.ClassConditionTemper,p.ProductForm,
            p.id AS id_property,s.SourceTable,s.File,s.CsvLine,s.Page,s.SourceLine,s.SizeText,s.SizeAxis,
            z.SizeMin,z.SizeMinIncluded,z.SizeMax,z.SizeMaxIncluded,v.Temperature,v.StressValue
            FROM Main m JOIN Material_Properties p ON p.id_main=m.id JOIN Property_Source s ON s.id_property=p.id
                JOIN Sizes z ON z.id=p.id_size JOIN Property_Values v ON v.id_property=p.id WHERE p.PropertyCode='S1';
        CREATE VIEW S2 AS SELECT m.id AS id_main,m.Specification,m.Grade,m.AlloyDesignation,m.ClassConditionTemper,p.ProductForm,
            p.id AS id_property,s.SourceTable,s.File,s.CsvLine,s.Page,s.SourceLine,s.SizeText,s.SizeAxis,
            z.SizeMin,z.SizeMinIncluded,z.SizeMax,z.SizeMaxIncluded,v.Temperature,v.StressValue
            FROM Main m JOIN Material_Properties p ON p.id_main=m.id JOIN Property_Source s ON s.id_property=p.id
                JOIN Sizes z ON z.id=p.id_size JOIN Property_Values v ON v.id_property=p.id WHERE p.PropertyCode='S2';
        CREATE VIEW VW_Material_Strength_Availability AS
            SELECT m.id,i.NaturalKey,m.Specification,m.Grade,m.AlloyDesignation,m.ClassConditionTemper,m.NominalComposition,
                EXISTS(SELECT 1 FROM Material_Properties p JOIN Property_Values v ON v.id_property=p.id WHERE p.id_main=m.id AND p.PropertyCode='Sy') AS HasSy,
                EXISTS(SELECT 1 FROM Material_Properties p JOIN Property_Values v ON v.id_property=p.id WHERE p.id_main=m.id AND p.PropertyCode='Su') AS HasSu,
                EXISTS(SELECT 1 FROM Material_Properties p JOIN Property_Values v ON v.id_property=p.id WHERE p.id_main=m.id AND p.PropertyCode='S1') AS HasS1,
                EXISTS(SELECT 1 FROM Material_Properties p JOIN Property_Values v ON v.id_property=p.id WHERE p.id_main=m.id AND p.PropertyCode='S2') AS HasS2,
                EXISTS(SELECT 1 FROM Material_Properties p JOIN Property_Values v ON v.id_property=p.id WHERE p.id_main=m.id AND p.PropertyCode='Sb1') AS HasSb1,
                EXISTS(SELECT 1 FROM Material_Properties p JOIN Property_Values v ON v.id_property=p.id WHERE p.id_main=m.id AND p.PropertyCode='Sb2') AS HasSb2
            FROM Main m JOIN Material_Identity i ON i.id_main=m.id;
        CREATE VIEW VW_Missing_Strength AS SELECT * FROM VW_Material_Strength_Availability WHERE (HasS1 OR HasS2 OR HasSb1 OR HasSb2) AND (NOT HasSy OR NOT HasSu);
        """ []
        createCoverageViews db
        use transaction = db.BeginTransaction()
        let run sql parameters = execute db (Some transaction) sql parameters
        let insert table (values: (string * obj) list) =
            let names = values |> List.map (fun (k,_) -> "\"" + k + "\"") |> String.concat ","
            let parameters = values |> List.mapi (fun i (_,v) -> "$p" + string i, v)
            run ("INSERT INTO " + table + " (" + names + ") VALUES (" + (parameters |> List.map fst |> String.concat ",") + ")") parameters
            use command = db.CreateCommand()
            command.Transaction <- transaction
            command.CommandText <- "SELECT last_insert_rowid()"
            Convert.ToInt64(command.ExecuteScalar())
        let nullable = function Some value -> box value | None -> box DBNull.Value
        let text value = if missing value then box DBNull.Value else box value
        let sourceMetadata = ["SourceKind",input.SourceKind; "SourcePath",input.SourcePath; "SourceSHA256",input.ArchiveHash] @ (if input.SourceKind="ZIP" then ["ArchiveSHA256",input.ArchiveHash] else [])
        for name,value in sourceMetadata @ ["Edition",edition; "Units","MPa; degrees Celsius; mm"; "Scope","MAIN=source tables excluding 6A-6D;Sy,Su,S1=1A+1B,S2=5A+5B,Sb1=3,Sb2=4"; "IdentityPolicy","Specification,Grade,ClassConditionTemper,UNS only; ProductForm belongs only to S1/S2"; "SchemaVersion","2025-import-v3"] do
            insert "Database_Metadata" ["Key",box name; "Value",box value] |> ignore
        for file,sha,count in input.Files do insert "Import_Files" ["Path",box file; "SHA256",box sha; "RowCount",box count] |> ignore
        for row in input.Omitted do insert "Import_Omitted_Rows" ["SourceTable",box row.Table; "File",box row.File; "CsvLine",box row.Line; "Reason",box row.Reason; "RawFieldsJson",box (row.Fields |> Map.toSeq |> dict |> json)] |> ignore
        for table,contents,sha in input.Documents do insert "Source_Documents" ["SourceTable",box table; "SHA256",box sha; "Contents",box contents] |> ignore
        for note in input.Notes do insert "Source_Notes" ["SourceTable",box note.Table; "Code",box note.Code; "Kind",box note.Kind; "NoteText",box note.Text] |> ignore
        let baselineKeys = baseline |> List.groupBy (fun row -> key row.Identity) |> Map.ofList
        let mutable nextId = baseline |> List.map (fun row -> row.Id) |> List.fold max 0L
        let ids = Dictionary<string,int64>()
        let groups = input.Rows |> List.groupBy (fun row -> key row.Identity) |> List.sortBy fst
        let unique values = match values |> List.choose id |> List.distinct with [one] -> Some one | _ -> None
        for materialKey,rows in groups do
            let first = List.head rows
            let old = Map.tryFind materialKey baselineKeys |> Option.defaultValue []
            let id = match old with [] -> nextId <- nextId + 1L; nextId | existing -> existing |> List.minBy (fun item -> item.Id) |> fun item -> item.Id
            ids.Add(materialKey,id)
            let identity = first.Identity
            let revisions = rows |> List.map (fun r -> field "Revision" r.Fields) |> List.filter (missing >> not) |> List.distinct
            let revision = match revisions with [one] -> text one | _ -> box DBNull.Value
            insert "Main" ["id",box id; "Revision",revision; "Specification",box identity.Specification; "Grade",text identity.Grade;
                "AlloyDesignation",text identity.Alloy; "ClassConditionTemper",text identity.Condition; "NominalComposition",nullable (nominalComposition rows);
                "SMYS",nullable (unique (rows |> List.map (fun r -> r.Sy))); "SMUS",nullable (unique (rows |> List.map (fun r -> r.Su)))] |> ignore
            insert "Material_Identity" ["id_main",box id; "NaturalKey",box materialKey] |> ignore
        let sizes = Dictionary<SizeBand,int64>()
        let defined = input.Notes |> List.map (fun n -> (n.Table,n.Code),n) |> Map.ofList
        // Every source table contributes identities and exact audit rows, even when its values are not imported yet.
        for row in input.Rows do
            insert "Material_Source" ["id_main",box ids[key row.Identity]; "SourceTable",box row.Table; "File",box row.File; "CsvLine",box row.Line;
                "RawFieldsJson",box (row.Fields |> Map.toSeq |> dict |> json); "Fingerprint",box row.Fingerprint] |> ignore
        // Mechanical families remain separate, including the two bolting-only tables.
        let mechanicalRows = input.Rows |> List.filter (fun row -> isMechanicalTable row.Table)
        for row in mechanicalRows do
            let sizeId =
                match sizes.TryGetValue row.Size with
                | true, value -> value
                | _ ->
                    let value = insert "Sizes" ["SizeMin",nullable row.Size.Minimum; "SizeMinIncluded",box row.Size.MinimumIncluded; "SizeMax",nullable row.Size.Maximum; "SizeMaxIncluded",box row.Size.MaximumIncluded]
                    sizes.Add(row.Size,value)
                    value
            let pid = insert "Material_Properties" ["id_main",box ids[key row.Identity]; "id_size",box sizeId; "PropertyCode",box (propertyCode row.Table); "ProductForm",text row.ProductForm]
            insert "Property_Source" ["id_property",box pid; "SourceTable",box row.Table; "File",box row.File; "CsvLine",box row.Line;
                "Page",text (field "Page No." row.Fields); "SourceLine",text (field "Line No." row.Fields); "SourceRowNumber",text (field "" row.Fields);
                "SizeText",text (field "Size/Thickness, mm" row.Fields); "SizeAxis",box row.Size.Axis; "MinYieldMPa",nullable row.Sy; "MinTensileMPa",nullable row.Su;
                "RawFieldsJson",box (row.Fields |> Map.toSeq |> dict |> json); "Fingerprint",box row.Fingerprint] |> ignore
            if row.Table="3" || row.Table="4" then
                insert (propertyCode row.Table) ["id_property",box pid; "id_main",box ids[key row.Identity]; "id_size",box sizeId] |> ignore
            for temp,value in row.Values do insert "Property_Values" ["id_property",box pid; "Temperature",box temp; "StressValue",box value] |> ignore
            if not (missing (field "Notes" row.Fields)) then insert "Property_Notes" ["id_property",box pid; "note",box (field "Notes" row.Fields); "note_type",box (row.Table + ":RawCodes")] |> ignore
            for code in List.distinct row.NoteCodes do
                match Map.tryFind (row.Table,code) defined with
                | Some note ->
                    insert "Property_Note_Links" ["id_property",box pid; "SourceTable",box row.Table; "Code",box code] |> ignore
                    insert "Property_Notes" ["id_property",box pid; "note",box note.Text; "note_type",box (row.Table + ":" + code)] |> ignore
                | None -> ()
        transaction.Commit()
        let expectedRows = mechanicalRows.Length
        let expectedValues = input.Rows |> List.sumBy (fun row -> row.Values.Length)
        let count table = query db ("SELECT count(*) FROM " + table) |> List.head |> Array.head |> Convert.ToInt64
        if count "Material_Properties" <> int64 expectedRows || count "Property_Values" <> int64 expectedValues then failwith "Imported row/value counts do not match input"
        for table,source in ["Sb1","3";"Sb2","4"] do
            let expected = mechanicalRows |> List.filter (fun row -> row.Table=source) |> List.length |> int64
            if count table <> expected then failwith ("Bolting-only table count does not match source: " + table)
        if count "Material_Source" <> int64 input.Rows.Length then failwith "Not every source row was linked to MAIN"
        if count "Main" <> int64 groups.Length || count "Source_Notes" <> int64 input.Notes.Length then failwith "Imported material/note counts do not match input"
        if not (query db "SELECT 1 FROM Main GROUP BY Specification,COALESCE(Grade,''),COALESCE(ClassConditionTemper,''),COALESCE(AlloyDesignation,'') HAVING count(*)>1").IsEmpty then failwith "Duplicate four-field MAIN identity"
        if query db "PRAGMA integrity_check" <> [[|box "ok"|]] then failwith "Database integrity check failed"
        if not (query db "PRAGMA foreign_key_check").IsEmpty then failwith "Database foreign key check failed"
        ids |> Seq.map (fun pair -> pair.Key,pair.Value) |> Map.ofSeq
