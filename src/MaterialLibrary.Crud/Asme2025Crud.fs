namespace MaterialLibrary.Crud

open System
open System.IO
open System.Text.Json
open Microsoft.Data.Sqlite
open MaterialLibrary.Domain.Database.Lookup

/// <summary>Transactional native 2025 editing; only explicit .working.db copies are writable.</summary>
module Asme2025Crud =
    /// <summary>Execute a bound mutation on the current SQLite transaction.</summary>
    let private run (db: SqliteConnection) sql parameters =
        use command = db.CreateCommand()
        command.CommandText <- sql
        for name,value in parameters do command.Parameters.AddWithValue(name,if isNull value then box DBNull.Value else value) |> ignore
        command.ExecuteNonQuery() |> ignore

    /// <summary>Add editor metadata and an append-only change journal without modifying source documents or notes.</summary>
    let ensureSchema (db: SqliteConnection) =
        run db """CREATE TABLE IF NOT EXISTS Property_Curve_Metadata (
                Code TEXT NOT NULL,CurveId INTEGER NOT NULL,SizeAxis TEXT,UserNotes TEXT,
                UserNoteItemsJson TEXT NOT NULL DEFAULT '[]',S1StressLevel TEXT,PRIMARY KEY(Code,CurveId));
            CREATE TABLE IF NOT EXISTS Property_Edit_History (
                Id INTEGER PRIMARY KEY,MaterialId INTEGER NOT NULL,Code TEXT NOT NULL,CurveId INTEGER NOT NULL,
                EditedUtc TEXT NOT NULL,Operation TEXT NOT NULL,BeforeJson TEXT,AfterJson TEXT);""" []
        let columns = Asme2025Data.query db "PRAGMA table_info(Property_Curve_Metadata)" [] |> List.map (fun row -> string row[1])
        if not (columns |> List.contains "UserNoteItemsJson") then
            run db "ALTER TABLE Property_Curve_Metadata ADD COLUMN UserNoteItemsJson TEXT NOT NULL DEFAULT '[]'" []
        if not (columns |> List.contains "S1StressLevel") then
            run db "ALTER TABLE Property_Curve_Metadata ADD COLUMN S1StressLevel TEXT" []
        if not (columns |> List.contains "CodeLimitsJson") then
            // NULL means the curve uses its own Section II-D source-row limits unchanged.
            run db "ALTER TABLE Property_Curve_Metadata ADD COLUMN CodeLimitsJson TEXT" []

    /// <summary>Open an existing working copy and reject reference files and unsupported schemas.</summary>
    let private writable (path: string) action =
        try
            if not (Path.GetFileName(path).EndsWith(".working.db",StringComparison.OrdinalIgnoreCase)) then
                Error "Edit a .working.db copy; the reference database cannot be modified."
            elif not (File.Exists path) then Error "Working database not found."
            else
                let options = SqliteConnectionStringBuilder(DataSource=path,Mode=SqliteOpenMode.ReadWrite,ForeignKeys=Nullable true,Pooling=false)
                use db = new SqliteConnection(options.ConnectionString)
                db.Open()
                if not (Asme2025Data.isNormalized db) then Error "The property editor requires the normalized ASME 2025 database."
                else
                    ensureSchema db
                    action db
        with ex -> Error ex.Message

    /// <summary>A Section limit is blank, "NP", or a °C number between -273.15 and 1200, optionally followed by a qualifier such as "(Cl. 3 only)".</summary>
    let private validTemperatureLimit (raw: string) =
        let text = if isNull raw then "" else raw.Trim()
        if text="" || String.Equals(text,"NP",StringComparison.OrdinalIgnoreCase) then true
        else
            let m = Text.RegularExpressions.Regex.Match(text,@"^(-?\d+(?:\.\d+)?)(\s*\(.+\))?$")
            m.Success && (let v = Double.Parse(m.Groups[1].Value,Globalization.CultureInfo.InvariantCulture) in v >= -273.15 && v <= 1200.)

    /// <summary>Validate fixed units, finite values, unique temperatures, and legal product-form/size metadata.</summary>
    let validate (curve: Asme2025Curve) =
        let code = Asme2025Data.canonicalCode curve.Code
        let positive = ["Sy";"Su";"S1";"S2";"Sb1";"Sb2";"E";"c";"k";"a";"density"] |> List.contains code
        let limits = Asme2025Data.normalizeCodeLimits curve.CodeLimits
        if not (Asme2025Data.families |> Array.exists (fun (c,_) -> c=code)) then Error "Unsupported property family."
        elif isNull curve.Points || curve.Points.Length=0 then Error "Enter at least one temperature/value point."
        elif curve.Points |> Array.exists (fun p -> not (Double.IsFinite p.Temperature && Double.IsFinite p.Value)) then Error "All temperatures and values must be finite."
        elif curve.Points |> Array.map (fun p -> p.Temperature) |> Array.distinct |> Array.length <> curve.Points.Length then Error "Duplicate temperatures are not allowed in one curve."
        elif curve.Points |> Array.exists (fun p -> (positive && p.Value<0.) || (code="Nu" && (p.Value <= -1. || p.Value >= 0.5))) then Error "Invalid property value: use nonnegative strengths/physical values and -1 < Nu < 0.5."
        elif curve.SizeMinimum.HasValue && (not (Double.IsFinite curve.SizeMinimum.Value) || curve.SizeMinimum.Value<0.) || curve.SizeMaximum.HasValue && (not (Double.IsFinite curve.SizeMaximum.Value) || curve.SizeMaximum.Value<0.) then Error "Size bounds must be finite nonnegative mm."
        elif curve.SizeMinimum.HasValue && curve.SizeMaximum.HasValue && (curve.SizeMinimum.Value>curve.SizeMaximum.Value || (curve.SizeMinimum.Value=curve.SizeMaximum.Value && not (curve.MinimumIncluded && curve.MaximumIncluded))) then Error "Size bounds do not describe a nonempty interval."
        elif code<>"S1" && code<>"S2" && not (String.IsNullOrWhiteSpace curve.ProductForm) then Error "Product form applies only to S1/S2."
        elif code="S1" && not (["Normal";"High"] |> List.contains curve.S1StressLevel) then Error "Select Normal Stress or High Stress (G5) for S1."
        elif not (["Sy";"Su";"S1";"S2";"Sb1";"Sb2"] |> List.contains code) && (curve.SizeMinimum.HasValue || curve.SizeMaximum.HasValue) then Error "Physical properties do not have size bounds."
        elif [limits.MaxTempSectionI;limits.MaxTempSectionIII;limits.MaxTempSectionVIII1;limits.MaxTempSectionVIII2;limits.MaxTempSectionXII] |> List.exists (validTemperatureLimit >> not) then
            Error "Section temperature limits must be blank, NP, or a °C value (-273.15 to 1200), optionally followed by a qualifier in parentheses."
        elif not (["S1";"S2";"Sb1";"Sb2"] |> List.contains code) && limits <> Asme2025Data.emptyCodeLimits then Error "P-No., Group No. and Section limits apply only to S1, S2, Sb1 and Sb2 curves."
        else Ok {curve with Code=code;CodeLimits=limits;Points=curve.Points |> Array.sortBy (fun p -> p.Temperature)}

    /// <summary>Record a complete before/after snapshot in the same transaction as the property modification.</summary>
    let private audit db operation (before: Asme2025Curve option) (after: Asme2025Curve) =
        run db "INSERT INTO Property_Edit_History(MaterialId,Code,CurveId,EditedUtc,Operation,BeforeJson,AfterJson) VALUES($m,$c,$id,$utc,$op,$before,$after)"
            ["$m",box after.MaterialId;"$c",box after.Code;"$id",box after.Id;"$utc",box (DateTime.UtcNow.ToString("O"));"$op",box operation;
             "$before",(before |> Option.map (fun c -> box (JsonSerializer.Serialize c)) |> Option.defaultValue (box DBNull.Value));"$after",box (JsonSerializer.Serialize after)]

    /// <summary>Create or replace exactly one curve; preserve other variants, source note links and the original provenance.</summary>
    let saveCurve path (requested: Asme2025Curve) =
        validate requested |> Result.bind (fun curve -> writable path (fun db ->
            use tx = db.BeginTransaction()
            let existing = Asme2025Data.curves db curve.MaterialId curve.Code |> List.tryFind (fun c -> c.Id=curve.Id)
            if curve.Id<>0L && (existing.IsNone || existing.Value.Version<>curve.Version) then Error "Curve changed externally; reload before saving."
            else
                let mechanical = ["Sy";"Su";"S1";"S2";"Sb1";"Sb2"] |> List.contains curve.Code
                let scalar (v: Nullable<float>) = if v.HasValue then box v.Value else box DBNull.Value
                let lastId () = Asme2025Data.query db "SELECT last_insert_rowid()" [] |> List.head |> Array.head |> Convert.ToInt64
                let size =
                    if mechanical then
                        run db "INSERT INTO Sizes(SizeMin,SizeMax,SizeMinIncluded,SizeMaxIncluded) VALUES($lo,$hi,$li,$ui)" ["$lo",scalar curve.SizeMinimum;"$hi",scalar curve.SizeMaximum;"$li",box curve.MinimumIncluded;"$ui",box curve.MaximumIncluded]
                        lastId ()
                    else 0L
                let id =
                    if curve.Id=0L then
                        if mechanical then run db "INSERT INTO Material_Properties(id_main,id_size,PropertyCode,ProductForm) VALUES($m,$s,$c,$f)" ["$m",box curve.MaterialId;"$s",box size;"$c",box curve.Code;"$f",(if String.IsNullOrWhiteSpace curve.ProductForm then box DBNull.Value else box curve.ProductForm)]
                        else run db "INSERT INTO Physical_Properties(id_main,PropertyCode) VALUES($m,$c)" ["$m",box curve.MaterialId;"$c",box (Asme2025Data.storageCode curve.Code)]
                        lastId ()
                    else curve.Id
                if mechanical then
                    run db "UPDATE Material_Properties SET id_size=$s,ProductForm=$f WHERE id=$id AND id_main=$m AND PropertyCode=$c" ["$s",box size;"$f",(if String.IsNullOrWhiteSpace curve.ProductForm then box DBNull.Value else box curve.ProductForm);"$id",box id;"$m",box curve.MaterialId;"$c",box curve.Code]
                    // The bolting side tables mirror curve ownership; source provenance remains untouched.
                    if curve.Code="Sb1" || curve.Code="Sb2" then
                        if curve.Id=0L then
                            // Imported bolting insert guards require Table 3/4 provenance, so create an explicit user source row.
                            let source = if curve.Code="Sb1" then "3" else "4"
                            run db "INSERT INTO Property_Source(id_property,SourceTable,File,CsvLine,SizeAxis,RawFieldsJson,Fingerprint) VALUES($id,$table,$file,1,$axis,'{}','User')" ["$id",box id;"$table",box source;"$file",box $"User/curve-{id}";"$axis",box curve.SizeAxis]
                            run db $"INSERT INTO {curve.Code}(id_property,id_main,id_size) VALUES($id,$m,$s)" ["$id",box id;"$m",box curve.MaterialId;"$s",box size]
                        else run db $"UPDATE {curve.Code} SET id_size=$s WHERE id_property=$id" ["$s",box size;"$id",box id]
                let table,key,value = if mechanical then "Property_Values","id_property","StressValue" else "Physical_Property_Values","id_physical_property","PropertyValue"
                run db $"DELETE FROM {table} WHERE {key}=$id" ["$id",box id]
                for point in curve.Points do run db $"INSERT INTO {table}({key},Temperature,{value}) VALUES($id,$t,$v)" ["$id",box id;"$t",box point.Temperature;"$v",box point.Value]
                let notesJson = if isNull curve.UserNoteItems then "[]" else JsonSerializer.Serialize curve.UserNoteItems
                // Store limits only when they differ from the curve's source row, so reference corrections still flow through.
                let limitsJson =
                    if curve.CodeLimits = Asme2025Data.sourceCodeLimits db id then box DBNull.Value
                    else box (JsonSerializer.Serialize curve.CodeLimits)
                run db """INSERT INTO Property_Curve_Metadata(Code,CurveId,SizeAxis,UserNotes,UserNoteItemsJson,S1StressLevel,CodeLimitsJson)
                    VALUES($c,$id,$axis,$notes,$items,$stress,$limits)
                    ON CONFLICT(Code,CurveId) DO UPDATE SET SizeAxis=excluded.SizeAxis,UserNotes=excluded.UserNotes,
                    UserNoteItemsJson=excluded.UserNoteItemsJson,S1StressLevel=excluded.S1StressLevel,CodeLimitsJson=excluded.CodeLimitsJson"""
                    ["$c",box curve.Code;"$id",box id;"$axis",box curve.SizeAxis;"$notes",box curve.UserNotes;"$items",box notesJson;"$stress",(if curve.Code="S1" then box curve.S1StressLevel else box DBNull.Value);"$limits",limitsJson]
                let saved = {curve with Id=id}
                audit db (if curve.Id=0L then "Create" else "Update") existing saved
                tx.Commit()
                Ok id))

    /// <summary>Delete exactly the selected, unchanged curve and retain an audit snapshot.</summary>
    let deleteCurve path (curve: Asme2025Curve) =
        writable path (fun db ->
            use tx = db.BeginTransaction()
            let existing = Asme2025Data.curves db curve.MaterialId curve.Code |> List.tryFind (fun c -> c.Id=curve.Id)
            if existing.IsNone || existing.Value.Version<>curve.Version then Error "Curve changed externally; reload before deleting."
            else
                let table = if ["Sy";"Su";"S1";"S2";"Sb1";"Sb2"] |> List.contains curve.Code then "Material_Properties" else "Physical_Properties"
                audit db "Delete" existing curve
                run db $"DELETE FROM {table} WHERE id=$id AND id_main=$m" ["$id",box curve.Id;"$m",box curve.MaterialId]
                run db "DELETE FROM Property_Curve_Metadata WHERE Code=$c AND CurveId=$id" ["$c",box curve.Code;"$id",box curve.Id]
                tx.Commit()
                Ok ())

    /// <summary>Set or clear constant density (kg/m³) and Poisson ratio; NULL means absent, never a guessed value.</summary>
    let saveConstants path materialId (density: Nullable<float>) (nu: Nullable<float>) =
        if density.HasValue && (not (Double.IsFinite density.Value) || density.Value<=0.) || nu.HasValue && (not (Double.IsFinite nu.Value) || nu.Value <= -1. || nu.Value >= 0.5) then Error "Use positive density and -1 < Nu < 0.5, or leave blank."
        else writable path (fun db ->
            use tx = db.BeginTransaction()
            let value (v: Nullable<float>) = if v.HasValue then box v.Value else box DBNull.Value
            run db "INSERT INTO Extension(id_main,ConstantDensity,ConstantPoissonRatio) VALUES($id,$rho,$nu) ON CONFLICT(id_main) DO UPDATE SET ConstantDensity=excluded.ConstantDensity,ConstantPoissonRatio=excluded.ConstantPoissonRatio" ["$id",box materialId;"$rho",value density;"$nu",value nu]
            tx.Commit()
            Ok ())

    /// <summary>Create or edit a four-field MAIN identity without replacing any property curves or source documents.</summary>
    let saveIdentity path (identity: Asme2025Material) =
        if String.IsNullOrWhiteSpace identity.Specification then Error "Specification is required."
        else writable path (fun db ->
            use tx = db.BeginTransaction()
            let normalize (value: string) = if String.IsNullOrWhiteSpace value then "" else System.Text.RegularExpressions.Regex.Replace(value.Trim(),"\\s+"," ").ToUpperInvariant()
            let specification,grade,condition,uns = normalize identity.Specification,normalize identity.Grade,normalize identity.Condition,normalize identity.UNS
            let naturalKey = JsonSerializer.Serialize [|specification;grade;condition;uns|]
            let text value = if String.IsNullOrWhiteSpace value then box DBNull.Value else box value
            let args = ["$s",box specification;"$g",text grade;"$c",text condition;"$u",text uns;"$n",text identity.NominalComposition]
            let id =
                if identity.Id=0L then
                    run db "INSERT INTO Main(Specification,Grade,ClassConditionTemper,AlloyDesignation,NominalComposition) VALUES($s,$g,$c,$u,$n)" args
                    Asme2025Data.query db "SELECT last_insert_rowid()" [] |> List.head |> Array.head |> Convert.ToInt64
                else
                    run db "UPDATE Main SET Specification=$s,Grade=$g,ClassConditionTemper=$c,AlloyDesignation=$u,NominalComposition=$n WHERE id=$id" (args @ ["$id",box identity.Id])
                    identity.Id
            run db "INSERT INTO Material_Identity(id_main,NaturalKey) VALUES($id,$key) ON CONFLICT(id_main) DO UPDATE SET NaturalKey=excluded.NaturalKey" ["$id",box id;"$key",box naturalKey]
            run db "INSERT INTO Property_Edit_History(MaterialId,Code,CurveId,EditedUtc,Operation,AfterJson) VALUES($id,'MAIN',$id,$utc,'Identity',$json)" ["$id",box id;"$utc",box (DateTime.UtcNow.ToString("O"));"$json",box (JsonSerializer.Serialize identity)]
            tx.Commit()
            Ok id)

    /// <summary>Resolve a shared user-writable 2025 working database, reusing edits instead of overwriting them.</summary>
    let prepareWorkingCopy (source: string) =
        try
            if source.EndsWith(".working.db",StringComparison.OrdinalIgnoreCase) then
                if File.Exists source then Ok source else Error "Working database not found."
            elif not (File.Exists source) then Error $"ASME 2025 reference not found: {source}"
            else
                let folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MaterialLibrary","ASME2025")
                Directory.CreateDirectory folder |> ignore
                let target = Path.Combine(folder,"asme_materials_2025.working.db")
                if not (File.Exists target) then
                    use original = Asme2025Data.openRead source
                    if not (Asme2025Data.isNormalized original) then failwith "Select a native ASME 2025 database."
                    let options = SqliteConnectionStringBuilder(DataSource=target,Mode=SqliteOpenMode.ReadWriteCreate,Pooling=false)
                    use destination = new SqliteConnection(options.ConnectionString)
                    destination.Open()
                    original.BackupDatabase(destination)
                Ok target
        with ex -> Error ex.Message
