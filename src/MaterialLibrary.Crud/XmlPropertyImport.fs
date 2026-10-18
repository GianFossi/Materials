namespace MaterialLibrary.Crud

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Microsoft.Data.Sqlite
open MaterialLibrary.Domain.Database.Lookup

/// <summary>Atomic, provenance-preserving import of explicitly approved physical records and pressure charts into a working database.</summary>
module XmlPropertyImport =
    /// <summary>Execute a bound SQL mutation inside the current transaction.</summary>
    let private run (db: SqliteConnection) sql parameters =
        use cmd = db.CreateCommand()
        cmd.CommandText <- sql
        for name,value in parameters do cmd.Parameters.AddWithValue(name,if isNull value then box DBNull.Value else value) |> ignore
        cmd.ExecuteNonQuery() |> ignore
    /// <summary>Ensure audit/provenance tables without changing original ASME source documents.</summary>
    let private schema db =
        Asme2025Crud.ensureSchema db
        run db """CREATE TABLE IF NOT EXISTS Xml_Import_Documents(SHA256 TEXT PRIMARY KEY,File TEXT NOT NULL,Contents TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Xml_Property_Associations(id_main INTEGER NOT NULL REFERENCES Main(id) ON DELETE CASCADE,
                PropertyCode TEXT NOT NULL,RecordKey TEXT NOT NULL,DocumentSHA256 TEXT NOT NULL REFERENCES Xml_Import_Documents(SHA256),
                TargetId INTEGER NOT NULL,RecordJson TEXT NOT NULL,ImportedUtc TEXT NOT NULL,
                PRIMARY KEY(id_main,PropertyCode,RecordKey));
            CREATE TABLE IF NOT EXISTS Xml_Import_History(Id INTEGER PRIMARY KEY,ImportedUtc TEXT NOT NULL,MaterialId INTEGER NOT NULL,PropertyCode TEXT NOT NULL,RecordKey TEXT NOT NULL,BeforeJson TEXT NOT NULL,AfterJson TEXT NOT NULL);""" []
    /// <summary>Fingerprint all existing target values and links to detect changes after preview.</summary>
    let private snapshot (db: SqliteConnection) materialId code =
        let rows =
            if code="EP" then Asme2025Data.query db "SELECT c.Chart_Name,v.Temperature,p.Factor_A,p.Factor_B FROM Material_EP_Charts m JOIN External_Pressure_Charts c ON c.id=m.id_ep_chart LEFT JOIN EP_Chart_Curves v ON v.id_ep_chart=c.id LEFT JOIN EP_Chart_Points p ON p.id_ep_curve=v.id WHERE m.id_main=$id ORDER BY c.id,v.id,p.id" ["$id",box materialId]
            elif code="density" || code="Nu" then
                let curves = Asme2025Data.curves db materialId code
                let constant = Asme2025Data.query db "SELECT ConstantDensity,ConstantPoissonRatio FROM Extension WHERE id_main=$id" ["$id",box materialId]
                [ [|box (JsonSerializer.Serialize curves);box (JsonSerializer.Serialize constant)|] ]
            else [ [|box (Asme2025Data.curves db materialId code |> JsonSerializer.Serialize)|] ]
        JsonSerializer.Serialize rows
    /// <summary>Return a stable preview version for the complete target state.</summary>
    let version db materialId code = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot db materialId code)))
    /// <summary>Validate an entire record; incomplete or duplicate points are rejected without partial import.</summary>
    let validate (record: XmlPropertyRecord) reviewed =
        if record.RequiresReview && not reviewed then Error "Confermare la verifica del record estratto da testo PDF."
        elif XmlPropertyReader.unitScale record.PropertyCode record.Unit <> Some 1. then Error "I punti da importare devono già essere convertiti nelle unità SI della famiglia."
        elif record.Points.Length=0 then Error "Il record non contiene punti numerici verificati."
        elif record.Points |> Array.exists (fun p -> not (Double.IsFinite p.Temperature && Double.IsFinite p.X && Double.IsFinite p.Y)) then Error "Punti non finiti."
        elif record.IsConstant && (not (["density";"Nu"] |> List.contains record.PropertyCode) || record.Points.Length<>1) then Error "Le costanti sono ammesse solo per densità e Nu con un valore."
        elif record.PropertyCode="EP" then
            if record.ChartName="" then Error "Nome diagramma mancante."
            elif record.Points |> Array.exists (fun p -> p.X<=0. || p.Y<0.) then Error "External pressure: A deve essere positivo e B non negativo."
            elif record.Points |> Array.distinctBy (fun p -> p.Temperature,p.X) |> Array.length <> record.Points.Length then Error "Punti A duplicati alla stessa temperatura."
            else Ok ()
        elif not (Asme2025Data.families |> Array.exists (fun (c,_) -> c=record.PropertyCode && not (["Sy";"Su";"S1";"S2";"Sb1";"Sb2"] |> List.contains c))) then Error "Famiglia fisica non supportata."
        elif record.Points |> Array.distinctBy (fun p -> p.Temperature) |> Array.length <> record.Points.Length then Error "Temperature duplicate nel record."
        elif record.Points |> Array.exists (fun p -> if record.PropertyCode="Nu" then p.Y <= -1. || p.Y>=0.5 else record.PropertyCode<>"Alpha" && record.PropertyCode<>"AlphaInstantaneous" && p.Y<0.) then Error "Valori fisici non validi."
        else Ok ()
    /// <summary>Import a batch atomically; replacement requires explicit consent and repeated imports reuse their own target.</summary>
    let apply (path: string) (assignments: XmlPropertyAssignment array) =
        try
            if not (path.EndsWith(".working.db",StringComparison.OrdinalIgnoreCase)) then failwith "Importare solo in una copia .working.db."
            let settings = SqliteConnectionStringBuilder(DataSource=path,Mode=SqliteOpenMode.ReadWrite,Pooling=false,ForeignKeys=Nullable true)
            use db = new SqliteConnection(settings.ConnectionString)
            db.Open()
            if not (Asme2025Data.isNormalized db) then failwith "Database 2025 richiesto."
            schema db
            use tx = db.BeginTransaction()
            let duplicate = assignments |> Array.countBy (fun a -> a.MaterialId,a.Record.PropertyCode) |> Array.exists (fun (_,count) -> count>1)
            if duplicate then failwith "Più record selezionati per lo stesso materiale/proprietà: risolvere l'ambiguità."
            for a in assignments do
                validate a.Record a.Reviewed |> Result.defaultWith failwith
                if version db a.MaterialId a.Record.PropertyCode<>a.ExpectedVersion then failwith "Dati modificati dopo l'anteprima: aggiornare le associazioni."
            let lastId () = Asme2025Data.query db "SELECT last_insert_rowid()" [] |> List.head |> Array.head |> Convert.ToInt64
            for a in assignments do
                let r = a.Record
                let existing = Asme2025Data.query db "SELECT TargetId FROM Xml_Property_Associations WHERE id_main=$m AND PropertyCode=$c AND RecordKey=$key" ["$m",box a.MaterialId;"$c",box r.PropertyCode;"$key",box r.Key] |> List.tryHead
                let before = snapshot db a.MaterialId r.PropertyCode
                let target =
                    if r.PropertyCode="EP" then
                        let links = Asme2025Data.query db "SELECT id_ep_chart FROM Material_EP_Charts WHERE id_main=$m" ["$m",box a.MaterialId]
                        if not links.IsEmpty && existing.IsNone && not a.ReplaceExisting then failwith "Diagramma già associato: scegliere Sostituisci."
                        // Version chart content rather than overwriting a chart shared with other materials.
                        let contentKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize r.Points))).Substring(0,12)
                        let name = r.ChartName+" [XML "+contentKey+"]"
                        run db "INSERT OR IGNORE INTO External_Pressure_Charts(Chart_Name) VALUES($name)" ["$name",box name]
                        let chartId = Asme2025Data.query db "SELECT id FROM External_Pressure_Charts WHERE Chart_Name=$name" ["$name",box name] |> List.head |> Array.head |> Convert.ToInt64
                        if (Asme2025Data.query db "SELECT 1 FROM EP_Chart_Curves WHERE id_ep_chart=$id" ["$id",box chartId]).IsEmpty then
                            for t,points in r.Points |> Array.groupBy (fun p -> p.Temperature) do
                                run db "INSERT INTO EP_Chart_Curves(id_ep_chart,Temperature) VALUES($id,$t)" ["$id",box chartId;"$t",box t]
                                let curve = lastId ()
                                for p in points |> Array.sortBy (fun p -> p.X) do run db "INSERT INTO EP_Chart_Points(id_ep_curve,Factor_A,Factor_B) VALUES($id,$a,$b)" ["$id",box curve;"$a",box p.X;"$b",box p.Y]
                        run db "INSERT INTO Material_EP_Charts(id_main,id_ep_chart) VALUES($m,$chart) ON CONFLICT(id_main) DO UPDATE SET id_ep_chart=excluded.id_ep_chart" ["$m",box a.MaterialId;"$chart",box chartId]
                        chartId
                    else
                        let curves = Asme2025Data.curves db a.MaterialId r.PropertyCode
                        let column = if r.PropertyCode="density" then "ConstantDensity" else "ConstantPoissonRatio"
                        let hasConstant = if r.PropertyCode="density" || r.PropertyCode="Nu" then not (Asme2025Data.query db $"SELECT 1 FROM Extension WHERE id_main=$id AND {column} IS NOT NULL" ["$id",box a.MaterialId]).IsEmpty else false
                        if (not curves.IsEmpty || hasConstant) && existing.IsNone && not a.ReplaceExisting then failwith "Proprietà già presente: scegliere Sostituisci oppure un altro materiale."
                        if a.ReplaceExisting then
                            for curve in curves do run db "DELETE FROM Physical_Properties WHERE id=$id" ["$id",box curve.Id]
                            run db "DELETE FROM Xml_Property_Associations WHERE id_main=$m AND PropertyCode=$c" ["$m",box a.MaterialId;"$c",box r.PropertyCode]
                        if r.IsConstant then
                            if not curves.IsEmpty && not a.ReplaceExisting && existing.IsSome then failwith "Curve tabulate presenti: scegliere Sostituisci per passare a una costante."
                            run db $"INSERT INTO Extension(id_main,{column}) VALUES($id,$v) ON CONFLICT(id_main) DO UPDATE SET {column}=excluded.{column}" ["$id",box a.MaterialId;"$v",box r.Points[0].Y]
                            0L
                        else
                            if hasConstant then run db $"UPDATE Extension SET {column}=NULL WHERE id_main=$id" ["$id",box a.MaterialId]
                            let id =
                                match existing with
                                | Some row when not a.ReplaceExisting && Convert.ToInt64 row[0]>0L -> Convert.ToInt64 row[0]
                                | _ ->
                                    run db "INSERT INTO Physical_Properties(id_main,PropertyCode) VALUES($id,$c)" ["$id",box a.MaterialId;"$c",box (Asme2025Data.storageCode r.PropertyCode)]
                                    lastId ()
                            run db "DELETE FROM Physical_Property_Values WHERE id_physical_property=$id" ["$id",box id]
                            for p in r.Points |> Array.sortBy (fun p -> p.Temperature) do run db "INSERT INTO Physical_Property_Values(id_physical_property,Temperature,PropertyValue) VALUES($id,$t,$v)" ["$id",box id;"$t",box p.Temperature;"$v",box p.Y]
                            id
                run db "INSERT OR IGNORE INTO Xml_Import_Documents(SHA256,File,Contents) VALUES($sha,$file,$xml)" ["$sha",box r.SHA256;"$file",box r.File;"$xml",box r.Xml]
                run db "INSERT INTO Xml_Property_Associations(id_main,PropertyCode,RecordKey,DocumentSHA256,TargetId,RecordJson,ImportedUtc) VALUES($m,$c,$key,$sha,$id,$json,$utc) ON CONFLICT(id_main,PropertyCode,RecordKey) DO UPDATE SET DocumentSHA256=excluded.DocumentSHA256,TargetId=excluded.TargetId,RecordJson=excluded.RecordJson,ImportedUtc=excluded.ImportedUtc" ["$m",box a.MaterialId;"$c",box r.PropertyCode;"$key",box r.Key;"$sha",box r.SHA256;"$id",box target;"$json",box (JsonSerializer.Serialize r);"$utc",box (DateTime.UtcNow.ToString("O"))]
                run db "INSERT INTO Xml_Import_History(ImportedUtc,MaterialId,PropertyCode,RecordKey,BeforeJson,AfterJson) VALUES($utc,$m,$c,$key,$before,$after)" ["$utc",box (DateTime.UtcNow.ToString("O"));"$m",box a.MaterialId;"$c",box r.PropertyCode;"$key",box r.Key;"$before",box before;"$after",box (JsonSerializer.Serialize a)]
            tx.Commit()
            Ok assignments.Length
        with ex -> Error ex.Message
