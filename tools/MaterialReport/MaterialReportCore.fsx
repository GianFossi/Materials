// MaterialReportCore.fsx
//
// Shared read-only model of ASME_Materials.db used by Export-MaterialReport.fsx and
// Test-MaterialReport.fsx. Nothing here writes to the database: it is copied to a temporary file
// and read from the copy, so no -wal/-shm side files appear next to the packaged database.

module MaterialReportCore

#r "nuget: Microsoft.Data.Sqlite, 9.0.0"

open System
open System.Collections.Generic
open System.Globalization
open System.IO
open Microsoft.Data.Sqlite

let inv = CultureInfo.InvariantCulture

// ───────────────────────────── database access ─────────────────────────────

type Row = Dictionary<string, obj>

let readRows (conn: SqliteConnection) (sql: string) : Row list =
    use cmd = conn.CreateCommand()
    cmd.CommandText <- sql
    use reader = cmd.ExecuteReader()

    [ while reader.Read() do
          let row = Row()

          for i in 0 .. reader.FieldCount - 1 do
              row.[reader.GetName i] <- (if reader.IsDBNull i then null else reader.GetValue i)

          yield row ]

let tableExists (conn: SqliteConnection) (name: string) =
    readRows conn $"SELECT name FROM sqlite_master WHERE type IN ('table','view') AND name = '{name}'"
    |> List.isEmpty
    |> not

let str (r: Row) (k: string) : string option =
    match r.TryGetValue k with
    | true, v when not (isNull v) ->
        let s = Convert.ToString(v, inv)
        if String.IsNullOrWhiteSpace s then None else Some(s.Trim())
    | _ -> None

let flt (r: Row) (k: string) : float option =
    match r.TryGetValue k with
    | true, v when not (isNull v) ->
        match v with
        | :? float as f -> Some f
        | :? int64 as i -> Some(float i)
        | :? string as s ->
            match Double.TryParse(s, NumberStyles.Float, inv) with
            | true, f -> Some f
            | _ -> None
        | _ -> None
    | _ -> None

let intOf (r: Row) (k: string) : int = flt r k |> Option.map int |> Option.defaultValue 0

/// An "included" flag column; a missing value means the bound is inclusive.
let isIncluded (r: Row) (k: string) = flt r k |> Option.forall (fun v -> v <> 0.0)

// ───────────────────────────── domain shapes ─────────────────────────────

/// (temperature degC, value) pairs sorted by temperature; empty cells are dropped.
type Curve = (float * float) list

type Size =
    { Min: float option
      MinIncl: bool
      Max: float option
      MaxIncl: bool }

/// Allowable-stress line. Austenitic stainless steels carry two lines per size range: the normal
/// one (<= 2/3 Sy) and a higher one (note G5, up to 90 % of Sy). The database attaches the G5 note
/// to BOTH rows, so the higher line is identified by its stress level, not by the note alone.
type StressCase =
    | Normal
    | HighG5
    /// A single line carrying note G5: the pair partner is not in the database.
    | SingleG5

let caseText =
    function
    | Normal -> "normale"
    | HighG5 -> "ALTO G5"
    | SingleG5 -> "G5 riga unica"

type Band =
    { Source: string
      Case: StressCase
      Size: Size
      MaxTemp: float option
      CreepTemp: float option
      Notes: string option
      Curve: Curve }

let curveOf (r: Row) : Curve =
    r.Keys
    |> Seq.choose (fun k ->
        if k.StartsWith "T_" then
            match Double.TryParse(k.Substring 2, NumberStyles.Float, inv), flt r k with
            | (true, t), Some v -> Some(t, v)
            | _ -> None
        else
            None)
    |> Seq.sortBy fst
    |> List.ofSeq

let sizeOf (r: Row) : Size =
    { Min = flt r "SizeThkMIN"
      MinIncl = isIncluded r "SizeThkMIN_Included"
      Max = flt r "SizeThkMAX"
      MaxIncl = isIncluded r "SizeThkMAX_Included" }

let noSize =
    { Min = None
      MinIncl = true
      Max = None
      MaxIncl = true }

/// ASME note G5 marks the higher allowable-stress line of Tables 1A/1B/3.
let hasNote (code: string) (notes: string option) =
    notes
    |> Option.exists (fun n -> n.Split(',') |> Array.exists (fun p -> p.Trim() = code))

/// Value of a curve at the given temperature (exact grid point only).
let valueAt (temperature: float) (curve: Curve) =
    curve |> List.tryFind (fun (t, _) -> t = temperature) |> Option.map snd

// ───────────────────────────── derived thermal expansion ─────────────────────────────

/// The database stores column A of the ASME TE tables, the INSTANTANEOUS coefficient. The mean
/// coefficient between 20 degC and T (column B) is derived by trapezoidal integration of A.
let meanFromInstantaneous (inst: Curve) : Curve =
    match inst with
    | [] -> []
    | (t0, a0) :: rest ->
        let folder (acc: (float * float) list, area: float, prevT: float, prevA: float) (t, a) =
            let area' = area + (t - prevT) * (prevA + a) / 2.0
            ((t, area' / (t - t0)) :: acc, area', t, a)

        let acc, _, _, _ = rest |> List.fold folder ([ t0, a0 ], 0.0, t0, a0)
        List.rev acc

// ───────────────────────────── loaded database ─────────────────────────────

type Db =
    { Materials: Row list
      Sy: IDictionary<int, Row list>
      Su: IDictionary<int, Row list>
      As1: IDictionary<int, Row list>
      As2: IDictionary<int, Row list>
      As3: IDictionary<int, Row list>
      Weld: IDictionary<int, Row list>
      Cp: IDictionary<int, Row list>
      GroupMap: IDictionary<int, Row>
      Elastic: IDictionary<int, Row>
      Expansion: IDictionary<int, Row>
      Conductivity: IDictionary<int, Row>
      Diffusivity: IDictionary<int, Row>
      ExtPressure: IDictionary<int, Row list>
      ChartPoints: IDictionary<string, int>
      Notes: IDictionary<(string * string), string> }

/// Copies the database to a temporary file, reads everything into memory and deletes the copy.
let load (dbPath: string) : Db =
    if not (File.Exists dbPath) then
        failwithf "Database not found: %s" dbPath

    let tempCopy = Path.Combine(Path.GetTempPath(), $"asme-report-{Guid.NewGuid():N}.db")
    File.Copy(dbPath, tempCopy, true)

    try
        let connectionString =
            SqliteConnectionStringBuilder(DataSource = tempCopy, Mode = SqliteOpenMode.ReadOnly, Pooling = false)
                .ToString()

        use conn = new SqliteConnection(connectionString)
        conn.Open()

        let byMaterial (sql: string) =
            readRows conn sql |> List.groupBy (fun r -> intOf r "MaterialID") |> dict

        let byId (sql: string) =
            readRows conn sql |> List.map (fun r -> intOf r "ID", r) |> dict

        let optionalByMaterial table =
            if tableExists conn table then
                byMaterial $"SELECT * FROM {table} ORDER BY ID"
            else
                dict []

        let chartPoints =
            if tableExists conn "ExternalPressureChart" then
                readRows conn "SELECT Figure, COUNT(*) AS N FROM ExternalPressureChart GROUP BY Figure"
                |> List.choose (fun r -> str r "Figure" |> Option.map (fun f -> f, intOf r "N"))
                |> dict
            else
                dict []

        let notes =
            readRows conn "SELECT SourceTable, NoteCode, NoteText FROM NotesDatabase"
            |> List.choose (fun r ->
                match str r "SourceTable", str r "NoteCode", str r "NoteText" with
                | Some t, Some c, Some x -> Some((t, c), x)
                | _ -> None)
            |> dict

        { Materials = readRows conn "SELECT * FROM Materials ORDER BY ID"
          Sy = byMaterial "SELECT * FROM YieldStrengthTable ORDER BY ID"
          Su = byMaterial "SELECT * FROM UltimateStrengthTable ORDER BY ID"
          As1 = byMaterial "SELECT * FROM AllowableStress1Table ORDER BY ID"
          As2 = byMaterial "SELECT * FROM AllowableStress2Table ORDER BY ID"
          As3 = byMaterial "SELECT * FROM AllowableStress3Table ORDER BY ID"
          Weld = byMaterial "SELECT * FROM DataTableASME ORDER BY ID"
          Cp = byMaterial "SELECT * FROM SpecificHeatTable ORDER BY ID"
          GroupMap = byId "SELECT MaterialID AS ID, * FROM MaterialGroupMap"
          Elastic = byId "SELECT * FROM ElasticModulusTable"
          Expansion = byId "SELECT * FROM ThermalExpansionTable"
          Conductivity = byId "SELECT * FROM ThermalConductivityTable"
          Diffusivity = byId "SELECT * FROM ThermalDiffusivityTable"
          ExtPressure = optionalByMaterial "ExternalPressureTable"
          ChartPoints = chartPoints
          Notes = notes }
    finally
        SqliteConnection.ClearAllPools()
        File.Delete tempCopy

// ───────────────────────────── per-material model ─────────────────────────────

/// Structured data of one material: what the report prints and what the tests check.
type MaterialData =
    { Id: int
      Specification: string
      Grade: string
      ClassCondition: string
      Uns: string
      Composition: string
      ProductForm: string
      Smys: float option
      Smts: float option
      ElongationLong: float option
      ElongationTransverse: float option
      Sy: Band list
      Su: Band list
      Div1: Band list
      Div2: Band list
      /// Tables 1A/1B (or bolting Table 3) list the material but no row has a VIII-1 maximum temperature.
      Div1NotPermitted: bool
      /// Welding rows: P-number, G-number, thickness qualifier.
      Welding: (string option * string option * string option) list
      Elastic: Curve
      AlphaInstantaneous: Curve
      AlphaMean: Curve
      SpecificHeat: Curve
      Conductivity: Curve
      Diffusivity: Curve
      Density: float option
      Poisson: float option
      Charts: string list
      ExternalPressureNotes: string list }

let private getOrEmpty (d: IDictionary<int, Row list>) id =
    match d.TryGetValue id with
    | true, v -> v
    | _ -> []

let private tableTag (r: Row) =
    (str r "ReferenceData" |> Option.defaultValue "").Replace("Table_", "")

let private groupCurve (db: Db) (table: IDictionary<int, Row>) (materialId: int) (column: string) : Curve =
    match db.GroupMap.TryGetValue materialId with
    | true, g ->
        match flt g column with
        | Some gid ->
            match table.TryGetValue(int gid) with
            | true, row -> curveOf row
            | _ -> []
        | None -> []
    | _ -> []

let private bandOf source case maxTemp creepTemp (r: Row) =
    { Source = source
      Case = case
      Size = sizeOf r
      MaxTemp = maxTemp
      CreepTemp = creepTemp
      Notes = str r "Notes"
      Curve = curveOf r }

let private strengthBands (rows: Row list) =
    rows |> List.map (fun r -> bandOf "Sy/Su" Normal None None r)

let private sizeKey (r: Row) =
    (str r "ReferenceData", flt r "SizeThkMIN", flt r "SizeThkMAX", isIncluded r "SizeThkMIN_Included", isIncluded r "SizeThkMAX_Included")

/// Assigns the stress case to each row: rows are grouped by table and thickness range; inside a
/// group with G5 notes the line with the higher stresses is HighG5 and the other ones are Normal.
let private classifyRows (rows: Row list) : (Row * StressCase) list =
    rows
    |> List.groupBy sizeKey
    |> List.collect (fun (_, group) ->
        let withG5 = group |> List.filter (fun r -> hasNote "G5" (str r "Notes"))

        if withG5.IsEmpty then
            group |> List.map (fun r -> r, Normal)
        elif group.Length = 1 then
            group |> List.map (fun r -> r, SingleG5)
        else
            // Compare on the temperatures every row of the group has a value for.
            let curves = group |> List.map curveOf
            let common = curves |> List.map (List.map fst >> set) |> List.reduce Set.intersect

            let score (r: Row) =
                curveOf r |> List.filter (fun (t, _) -> common.Contains t) |> List.sumBy snd

            let top = group |> List.maxBy score
            group |> List.map (fun r -> r, (if obj.ReferenceEquals(r, top) && hasNote "G5" (str r "Notes") then HighG5 else Normal)))

/// Division 1 (VIII-1): Tables 1A/1B rows permitted in VIII-1, plus bolting Table 3.
let private div1Bands (db: Db) id =
    let fromTables =
        getOrEmpty db.As1 id
        |> List.filter (fun r -> (flt r "MaxTemp_VIII1").IsSome)
        |> classifyRows
        |> List.map (fun (r, case) -> bandOf (tableTag r) case (flt r "MaxTemp_VIII1") (flt r "CreepTemperature") r)

    let bolting =
        getOrEmpty db.As3 id
        |> List.filter (fun r -> (flt r "MaxTemp_VIII1").IsSome)
        |> classifyRows
        |> List.map (fun (r, case) -> bandOf "T3 bulloneria" case (flt r "MaxTemp_VIII1") (flt r "CreepTemperature") r)

    fromTables @ bolting

/// Division 2 (VIII-2): Tables 5A/5B, plus bolting Table 3 rows permitted in VIII-2.
let private div2Bands (db: Db) id =
    let fromTables =
        getOrEmpty db.As2 id
        |> List.map (fun r -> bandOf (tableTag r) Normal (flt r "MaximumTemperature") (flt r "CreepTemperature") r)

    let bolting =
        getOrEmpty db.As3 id
        |> List.filter (fun r -> (flt r "MaxTemp_VIII2").IsSome)
        |> classifyRows
        |> List.map (fun (r, case) -> bandOf "T3 bulloneria" case (flt r "MaxTemp_VIII2") (flt r "CreepTemperature") r)

    fromTables @ bolting

/// Note texts attached to the material's allowable-stress rows that talk about external pressure.
let private externalPressureNotes (db: Db) id : string list =
    let codesOf (rows: Row list) =
        rows
        |> List.collect (fun r ->
            match str r "Notes" with
            | Some n -> n.Split(',') |> Array.map (fun c -> tableTag r, c.Trim()) |> List.ofArray
            | None -> [])

    (codesOf (getOrEmpty db.As1 id) @ codesOf (getOrEmpty db.As2 id))
    |> List.distinct
    |> List.choose (fun key ->
        match db.Notes.TryGetValue key with
        | true, text when text.Contains("external pressure", StringComparison.OrdinalIgnoreCase) -> Some $"{snd key}: {text}"
        | _ -> None)

let buildData (db: Db) (m: Row) : MaterialData =
    let id = intOf m "ID"
    let text key = str m key |> Option.defaultValue ""
    let alphaInst = groupCurve db db.Expansion id "ThermalExpansionGroupID"

    { Id = id
      Specification = text "Specification"
      Grade = text "TypeGrade"
      ClassCondition = text "ClassConditionTemper"
      Uns = text "AlloyDesignationNumber"
      Composition = text "NominalComposition"
      ProductForm = text "ProductForm"
      Smys = flt m "SMYS"
      Smts = flt m "SMTS"
      ElongationLong = flt m "RuptureElongationLong"
      ElongationTransverse = flt m "RuptureElongationTransv"
      Sy = strengthBands (getOrEmpty db.Sy id)
      Su = strengthBands (getOrEmpty db.Su id)
      Div1 = div1Bands db id
      Div2 = div2Bands db id
      Div1NotPermitted =
        (div1Bands db id).IsEmpty
        && (not (getOrEmpty db.As1 id).IsEmpty || not (getOrEmpty db.As3 id).IsEmpty)
      Welding =
        getOrEmpty db.Weld id
        |> List.map (fun r -> str r "Pnum", str r "Gnum", str r "PnumSizeThk")
      Elastic = groupCurve db db.Elastic id "ElasticModulusGroupID"
      AlphaInstantaneous = alphaInst
      AlphaMean = meanFromInstantaneous alphaInst
      SpecificHeat =
        getOrEmpty db.Cp id
        |> List.tryHead
        |> Option.map curveOf
        |> Option.defaultValue []
      Conductivity = groupCurve db db.Conductivity id "ThermalConductivityGroupID"
      Diffusivity = groupCurve db db.Diffusivity id "ThermalDiffusivityGroupID"
      Density = flt m "Density"
      Poisson = flt m "PoissonFactor"
      Charts =
        getOrEmpty db.ExtPressure id
        |> List.choose (fun r -> str r "ReferenceData")
        |> List.distinct
      ExternalPressureNotes = externalPressureNotes db id }

// ───────────────────────────── data groups under control ─────────────────────────────

/// Missing inputs for the monotonic stress-strain curve (needs E, Sy, Su and nu).
let stressStrainMissing (d: MaterialData) : string list =
    [ if d.Elastic.IsEmpty then "E"
      if d.Sy.IsEmpty then "Sy"
      if d.Su.IsEmpty then "Su"
      if d.Poisson.IsNone then "nu" ]

/// Every data group the report audits, with a presence predicate. The order is the report order.
let groups: (string * (MaterialData -> bool)) list =
    [ "SMYS", (fun d -> d.Smys.IsSome)
      "SMTS", (fun d -> d.Smts.IsSome)
      "Allungamento a rottura", (fun d -> d.ElongationLong.IsSome || d.ElongationTransverse.IsSome)
      "P/G Number ASME IX", (fun d -> not d.Welding.IsEmpty)
      "Ammissibile Div1", (fun d -> not d.Div1.IsEmpty)
      "Ammissibile Div2", (fun d -> not d.Div2.IsEmpty)
      "Sy", (fun d -> not d.Sy.IsEmpty)
      "Su", (fun d -> not d.Su.IsEmpty)
      "Modulo elastico", (fun d -> not d.Elastic.IsEmpty)
      "Densita", (fun d -> d.Density.IsSome)
      "Dilatazione termica", (fun d -> not d.AlphaInstantaneous.IsEmpty)
      "Calore specifico", (fun d -> not d.SpecificHeat.IsEmpty)
      "Conducibilita termica", (fun d -> not d.Conductivity.IsEmpty)
      "Diffusivita termica", (fun d -> not d.Diffusivity.IsEmpty)
      "External Pressure Chart", (fun d -> not d.Charts.IsEmpty)
      "Curva Stress-Strain", (fun d -> (stressStrainMissing d).IsEmpty)
      // Kcss/Ncss (Table 3-D.2M) are not stored anywhere in the database.
      "Curva Stress-Strain ciclica", (fun _ -> false) ]

let missingGroups (d: MaterialData) : string list =
    groups |> List.filter (fun (_, present) -> not (present d)) |> List.map fst
