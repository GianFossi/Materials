namespace MaterialLibrary.Domain.Database.Lookup

open System
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Microsoft.Data.Sqlite

/// <summary>A temperature/value point: Celsius and the fixed unit declared by the curve family.</summary>
[<CLIMutable>]
type Asme2025Point = { Temperature: float; Value: float }

/// <summary>One independently editable 2025 curve; sizes are mm, stresses MPa, temperatures Celsius.</summary>
[<CLIMutable>]
type Asme2025Curve = {
    Id: int64
    MaterialId: int64
    Code: string
    ProductForm: string
    SizeMinimum: Nullable<float>
    SizeMaximum: Nullable<float>
    MinimumIncluded: bool
    MaximumIncluded: bool
    SizeAxis: string
    SourceTable: string
    SourceNotes: string
    UserNotes: string
    Points: Asme2025Point array
    Version: string
}

/// <summary>Four-field MAIN identity and composition, without product-form duplication.</summary>
[<CLIMutable>]
type Asme2025Material = {
    Id: int64
    Specification: string
    Grade: string
    Condition: string
    UNS: string
    NominalComposition: string
}

/// <summary>Native read access shared by the desktop editor, library and Excel; it never modifies a reference database.</summary>
module Asme2025Data =
    /// <summary>All editable families with fixed units; alpha is mean expansion in 1/C and a is diffusivity in m²/s.</summary>
    let families = [|"Sy","MPa";"Su","MPa";"S1","MPa";"S2","MPa";"Sb1","MPa";"Sb2","MPa";"E","MPa";"Alpha","1/degC";"AlphaInstantaneous","1/degC";"c","J/(kg*K)";"k","W/(m*K)";"a","m2/s";"density","kg/m3";"Nu","dimensionless"|]

    /// <summary>Normalize public property aliases to the family names shown by the editor.</summary>
    let canonicalCode (code: string) =
        match code.Trim().ToUpperInvariant() with
        | "SY" -> "Sy" | "SU" -> "Su" | "SB1" -> "Sb1" | "SB2" -> "Sb2"
        | "ALPHAINSTANTANEOUS" | "ALPHA_INSTANTANEOUS" -> "AlphaInstantaneous"
        | "ALPHAMEAN" | "ALPHA_MEAN" | "ALPHA" -> "Alpha" | "C" | "CP" -> "c" | "K" | "TC" -> "k" | "A" | "TD" -> "a"
        | "DENSITY" | "RHO" -> "density" | "NU" -> "Nu" | other -> other

    /// <summary>Return the normalized storage identifier used by Physical_Properties.</summary>
    let storageCode code =
        match canonicalCode code with "c" -> "Cp" | "k" -> "TC" | "a" -> "TD" | "density" -> "Density" | value -> value

    /// <summary>Read detached values with bound SQL parameters, representing NULL as null.</summary>
    let query (db: SqliteConnection) sql (parameters: (string * obj) list) =
        use command = db.CreateCommand()
        command.CommandText <- sql
        for name,value in parameters do command.Parameters.AddWithValue(name,if isNull value then box DBNull.Value else value) |> ignore
        use reader = command.ExecuteReader()
        let rows = ResizeArray<obj array>()
        while reader.Read() do rows.Add(Array.init reader.FieldCount (fun i -> if reader.IsDBNull i then null else reader.GetValue i))
        List.ofSeq rows

    /// <summary>Detect the imported schema without relying on the file name.</summary>
    let isNormalized (db: SqliteConnection) =
        not (query db "SELECT 1 FROM sqlite_master WHERE type='table' AND name='Main' COLLATE NOCASE" []).IsEmpty

    /// <summary>Open an existing database read-only with no pooling.</summary>
    let openRead path =
        let options = SqliteConnectionStringBuilder(DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false)
        let db = new SqliteConnection(options.ConnectionString)
        db.Open()
        db

    /// <summary>List all four-field identities; raw empty fields remain empty and are not guessed.</summary>
    let materials (db: SqliteConnection) =
        query db "SELECT id,Specification,Grade,ClassConditionTemper,AlloyDesignation,NominalComposition FROM Main ORDER BY Specification,Grade,ClassConditionTemper,AlloyDesignation,id" []
        |> List.map (fun row -> { Id=Convert.ToInt64 row[0];Specification=string row[1];Grade=string row[2];Condition=string row[3];UNS=string row[4];NominalComposition=string row[5] })

    /// <summary>Hash the complete editable curve and its source context for optimistic concurrency checks.</summary>
    let fingerprint (curve: Asme2025Curve) =
        let json = JsonSerializer.Serialize({curve with Version=""})
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes json))

    /// <summary>Load every variant for one material and family, with original notes and explicit size boundaries.</summary>
    let curves (db: SqliteConnection) materialId requestedCode =
        let code = canonicalCode requestedCode
        let mechanical = ["Sy";"Su";"S1";"S2";"Sb1";"Sb2"] |> List.contains code
        let rows =
            if mechanical then
                query db """SELECT p.id,p.ProductForm,z.SizeMin,z.SizeMax,z.SizeMinIncluded,z.SizeMaxIncluded,
                    COALESCE(s.SizeAxis,'thickness-or-unspecified'),COALESCE(s.SourceTable,'User')
                    FROM Material_Properties p JOIN Sizes z ON z.id=p.id_size
                    LEFT JOIN Property_Source s ON s.id_property=p.id
                    WHERE p.id_main=$id AND p.PropertyCode=$code ORDER BY p.id""" ["$id",box materialId;"$code",box code]
            else
                query db """SELECT id,NULL,NULL,NULL,0,0,'','User' FROM Physical_Properties
                    WHERE id_main=$id AND PropertyCode=$code ORDER BY id""" ["$id",box materialId;"$code",box (storageCode code)]
        rows |> List.map (fun row ->
            let id = Convert.ToInt64 row[0]
            let valueTable,key,value = if mechanical then "Property_Values","id_property","StressValue" else "Physical_Property_Values","id_physical_property","PropertyValue"
            let points = query db $"SELECT Temperature,{value} FROM {valueTable} WHERE {key}=$id ORDER BY Temperature" ["$id",box id] |> List.map (fun row -> { Temperature=Convert.ToDouble row[0];Value=Convert.ToDouble row[1] }) |> List.toArray
            let sourceNotes =
                if mechanical then
                    query db "SELECT SourceTable,Code,Kind,NoteText FROM VW_Property_Notes_All WHERE id_property=$id ORDER BY SourceTable,Kind,Code" ["$id",box id]
                    |> List.map (fun n -> $"{n[0]} / {n[1]} ({n[2]}): {n[3]}") |> String.concat "\n\n"
                else ""
            let xmlProvenance =
                if mechanical || (query db "SELECT 1 FROM sqlite_master WHERE name='Xml_Property_Associations'" []).IsEmpty then ""
                else query db "SELECT a.RecordJson,d.File FROM Xml_Property_Associations a JOIN Xml_Import_Documents d ON d.SHA256=a.DocumentSHA256 WHERE a.id_main=$m AND a.PropertyCode=$c AND a.TargetId=$id" ["$m",box materialId;"$c",box code;"$id",box id]
                     |> List.map (fun r ->
                         use doc = JsonDocument.Parse(string r[0])
                         let notes = doc.RootElement.GetProperty("Notes").GetString()
                         $"XML: {r[1]}\n{notes}") |> String.concat "\n"
            let metadata =
                if (query db "SELECT 1 FROM sqlite_master WHERE name='Property_Curve_Metadata'" []).IsEmpty then None
                else query db "SELECT SizeAxis,UserNotes FROM Property_Curve_Metadata WHERE Code=$c AND CurveId=$id" ["$c",box code;"$id",box id] |> List.tryHead
            let userNotes = metadata |> Option.map (fun r -> string r[1]) |> Option.defaultValue ""
            let axis = metadata |> Option.map (fun r -> string r[0]) |> Option.defaultValue (string row[6])
            let nullable (value: obj) = if isNull value then Nullable() else Nullable(Convert.ToDouble value)
            let curve = { Id=id;MaterialId=materialId;Code=code;ProductForm=string row[1];SizeMinimum=nullable row[2];SizeMaximum=nullable row[3];MinimumIncluded=Convert.ToBoolean row[4];MaximumIncluded=Convert.ToBoolean row[5];SizeAxis=axis;SourceTable=string row[7];SourceNotes=(if mechanical then sourceNotes else xmlProvenance);UserNotes=userNotes;Points=points;Version="" }
            {curve with Version=fingerprint curve})

    /// <summary>Interpolate only inside the selected curve; ambiguous variants require an explicit curve ID.</summary>
    let lookup (curves: Asme2025Curve list) (temperature: float) (size: float option) (productForm: string option) (curveId: int64 option) =
        let finite value = Double.IsFinite value
        if not (finite temperature) || (size |> Option.exists (finite >> not)) then Error "Temperature and size must be finite."
        else
            let candidates = curves |> List.filter (fun c ->
                (curveId |> Option.forall ((=) c.Id)) && (productForm |> Option.forall (fun p -> String.Equals(c.ProductForm,p,StringComparison.OrdinalIgnoreCase)))
                && (size |> Option.forall (fun s -> (not c.SizeMinimum.HasValue || (if c.MinimumIncluded then s>=c.SizeMinimum.Value else s>c.SizeMinimum.Value)) && (not c.SizeMaximum.HasValue || (if c.MaximumIncluded then s<=c.SizeMaximum.Value else s<c.SizeMaximum.Value)))))
            match candidates with
            | [curve] ->
                match curve.Points |> Array.tryFind (fun p -> p.Temperature=temperature) with
                | Some point -> Ok point.Value
                | None ->
                    curve.Points |> Array.pairwise |> Array.tryFind (fun (lo,hi) -> lo.Temperature<temperature && temperature<hi.Temperature)
                    |> Option.map (fun (lo,hi) -> Ok(lo.Value+(hi.Value-lo.Value)*(temperature-lo.Temperature)/(hi.Temperature-lo.Temperature)))
                    |> Option.defaultValue (Error "Temperature outside the available curve; extrapolation is disabled.")
            | [] -> Error "No matching property curve."
            | _ -> Error "Several curves match. Specify product form, size and/or curve ID."

    /// <summary>Lookup a selected tabulated variant, or an explicitly stored density/Nu constant when no curve exists.</summary>
    let lookupValue db materialId code temperature size productForm (curveId: int64 option) =
        let code = canonicalCode code
        let available = curves db materialId code
        if available.IsEmpty && (code="density" || code="Nu") && curveId.IsNone && Double.IsFinite temperature then
            let column = if code="density" then "ConstantDensity" else "ConstantPoissonRatio"
            match query db $"SELECT {column} FROM Extension WHERE id_main=$id" ["$id",box materialId] with
            | [row] when not (isNull row[0]) -> Ok(Convert.ToDouble row[0])
            | _ -> Error "No tabulated property or stored constant."
        else lookup available temperature size productForm curveId

    /// <summary>Read material-linked pressure curves; X is dimensionless Factor A and Y is Factor B in MPa.</summary>
    let pressureCurves (db: SqliteConnection) materialId =
        query db """SELECT v.id,c.Chart_Name,v.Temperature FROM Material_EP_Charts m
            JOIN External_Pressure_Charts c ON c.id=m.id_ep_chart JOIN EP_Chart_Curves v ON v.id_ep_chart=c.id
            WHERE m.id_main=$id ORDER BY v.Temperature,v.id""" ["$id",box materialId]
        |> List.map (fun row ->
            let id=Convert.ToInt64 row[0]
            let points=query db "SELECT Factor_A,Factor_B FROM EP_Chart_Points WHERE id_ep_curve=$id ORDER BY Factor_A" ["$id",box id] |> List.map (fun p -> Convert.ToDouble p[0],Convert.ToDouble p[1])
            id,string row[1],Convert.ToDouble row[2],points)
