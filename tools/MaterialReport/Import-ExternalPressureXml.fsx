// Import-ExternalPressureXml.fsx
//
// Parses the staged ASME II-D external-pressure chart XML files (src/MaterialLibrary/data/
// external-pressure-xml), validates every chart, and optionally imports the valid ones into a COPY
// of ASME_Materials.db together with a material -> figure association.
//
// The XML files are raw PDF extractions ("raw-tokenized-review-required"): exponents are omitted on
// continuation rows, tables wrap across pages, and some figures use other layouts. Every chart is
// therefore parsed with a strict state machine and checked (A strictly ascending, B non-decreasing,
// even number of values, exponents resolved). A chart that does not pass is reported and NEVER
// imported; nothing is guessed.
//
// Usage (from the repository root):
//   dotnet fsi tools/MaterialReport/Import-ExternalPressureXml.fsx
//       parse + validate only, write the reports (no database access)
//   dotnet fsi tools/MaterialReport/Import-ExternalPressureXml.fsx --make-template map.csv
//       write a material -> figure template (one row per material, Figura empty)
//   dotnet fsi tools/MaterialReport/Import-ExternalPressureXml.fsx --apply --target-db out.db [--map map.csv]
//       copy the source database to out.db and import valid charts (and the map associations)
//
// Options:
//   --xml-dir <dir>      chart XML folder (default: src/MaterialLibrary/data/external-pressure-xml)
//   --db <path>          source database (default: src/MaterialLibrary/data/ASME_Materials.db)
//   --out <dir>          report directory (default: tools/MaterialReport/output)
//   --sep <char>         CSV separator (default ";")
//   --map <csv>          material -> figure map (see below)
//   --make-template <f>  write the map template and exit
//   --apply              import into --target-db (never into the source database)
//   --target-db <path>   database copy to write (must differ from --db)
//
// Map CSV columns (header required): ID;Specifica;Grado;Classe_Condizione_Tempra;UNS;Figura
//   - a row with ID applies to that material;
//   - a row without ID is a rule: every non-empty Specifica/Grado/Classe_Condizione_Tempra/UNS
//     must match (case-insensitive); empty ones match anything. The rule with more filled fields
//     wins; two rules of equal weight giving different figures are reported and skipped.
//
// What is written (only with --apply):
//   ExternalPressureChart  (new)  Figure, Curve, TempFrom, TempTo, PointIndex, FactorA, FactorB_MPa
//   ExternalPressureTable         one row per mapped material: ReferenceData = figure, no T_ values
// Nothing is guessed: materials without a map entry get no chart.

#r "nuget: Microsoft.Data.Sqlite, 9.0.0"

open System
open System.Collections.Generic
open System.Diagnostics
open System.Globalization
open System.IO
open System.Text
open System.Text.RegularExpressions
open System.Xml.Linq
open Microsoft.Data.Sqlite

let inv = CultureInfo.InvariantCulture
let stopwatch = Stopwatch.StartNew()

// ───────────────────────────── command line ─────────────────────────────

let rec parseArgs (acc: Map<string, string>) (args: string list) =
    match args with
    | key :: value :: rest when key.StartsWith "--" && not (value.StartsWith "--") -> parseArgs (acc.Add(key, value)) rest
    | key :: rest when key.StartsWith "--" -> parseArgs (acc.Add(key, "true")) rest
    | _ :: rest -> parseArgs acc rest
    | [] -> acc

let options = fsi.CommandLineArgs |> Array.skip 1 |> Array.toList |> parseArgs Map.empty
let repoRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))
let dataDir = Path.Combine(repoRoot, "src", "MaterialLibrary", "data")

let fullPath (p: string) = Path.GetFullPath p

let xmlDir =
    options.TryFind "--xml-dir"
    |> Option.defaultValue (Path.Combine(dataDir, "external-pressure-xml"))
    |> fullPath

let sourceDb =
    options.TryFind "--db"
    |> Option.defaultValue (Path.Combine(dataDir, "ASME_Materials.db"))
    |> fullPath

let outDir =
    options.TryFind "--out"
    |> Option.defaultValue (Path.Combine(__SOURCE_DIRECTORY__, "output"))
    |> fullPath

let separator = options.TryFind "--sep" |> Option.defaultValue ";"
let mapPath = options.TryFind "--map" |> Option.map fullPath
let templatePath = options.TryFind "--make-template" |> Option.map fullPath
let apply = options.ContainsKey "--apply"
let targetDb = options.TryFind "--target-db" |> Option.map fullPath

Directory.CreateDirectory outDir |> ignore

let fail (message: string) =
    eprintfn "ERRORE: %s" message
    exit 2

if apply && targetDb.IsNone then
    fail "--apply richiede --target-db <percorso>"

if apply && targetDb.IsSome && String.Equals(targetDb.Value, sourceDb, StringComparison.OrdinalIgnoreCase) then
    fail "--target-db deve essere diverso da --db: il database pacchettizzato non viene mai modificato sul posto"

// ───────────────────────────── parsing ─────────────────────────────

type Item =
    { Mantissa: float
      Text: string
      Exponent: int option
      Line: string }

type Block =
    { Label: string
      TempFrom: float
      TempTo: float
      Items: List<Item> }

type ChartCurve =
    { Label: string
      TempFrom: float
      TempTo: float
      Points: (float * float) list }

type Chart =
    { Figure: string
      File: string
      Curves: ChartCurve list
      Problems: string list
      Warnings: string list }

let noisePatterns =
    [ @"^--[`,\-]*$"
      @"^ASME BPVC"
      @"^Copyright"
      @"^Provided by"
      @"^Licensee="
      @"^Not for Resale"
      @"^No reproduction"
      @"^\d{4}$"
      @"^\.\.\.$"
      @"^(Copy|Provid|No re)$" ]
    |> List.map (fun p -> Regex(p, RegexOptions.Compiled))

let headerPattern =
    Regex(@"^(A|B|B, MPa|MPa|Yield Strength,|Temp\., °C|Class/Temp\., °C|°C)$", RegexOptions.Compiled)

let sideBySidePattern =
    Regex(@"^(Temperature( up to)? \d+\s*°C,?|E = .*)$", RegexOptions.Compiled)

let numberPattern =
    Regex(@"^(\d+(?:\.\d+)?)(?:\s+([+-]\d{2}))?(?:\s+P\.L\.)?$", RegexOptions.Compiled)

let upToPattern = Regex(@"^Up to (\d+)$", RegexOptions.Compiled)
let roomPattern = Regex(@"^Room temp\.$", RegexOptions.Compiled)
let classPattern = Regex(@"^Class (\d+) up to (\d+)$", RegexOptions.Compiled)
let rangePattern = Regex(@"^(\d+)\s*(?:to|[–-])\s*(\d+)$", RegexOptions.Compiled)
let labelledPattern = Regex(@"^(\d{1,3}) \((.+)\)$", RegexOptions.Compiled)
let integerPattern = Regex(@"^\d{1,3}$", RegexOptions.Compiled)

let isNoise (line: string) = noisePatterns |> List.exists (fun r -> r.IsMatch line)

/// Splits the raw text of one chart into temperature blocks of (mantissa, exponent) items.
let tokenize (rawText: string) : Block list * string list =
    let problems = List<string>()
    let blocks = List<Block>()

    let startBlock label (tempFrom: float) (tempTo: float) =
        // The same temperature repeated right after a page header continues the previous block.
        match Seq.tryLast blocks with
        | Some last when last.Label = label && last.TempFrom = tempFrom && last.TempTo = tempTo -> ()
        | _ ->
            blocks.Add
                { Label = label
                  TempFrom = tempFrom
                  TempTo = tempTo
                  Items = List<Item>() }

    let expectingA () =
        blocks.Count = 0 || blocks.[blocks.Count - 1].Items.Count % 2 = 0

    let lines =
        rawText.Split('\n')
        |> Array.map (fun l -> l.Trim())
        |> Array.filter (fun l -> l <> "" && not (isNoise l))

    for line in lines do
        let m = numberPattern.Match line

        if headerPattern.IsMatch line then
            ()
        elif sideBySidePattern.IsMatch line then
            problems.Add "layout a colonne affiancate (curve per temperatura in colonne parallele): non supportato"
        elif upToPattern.IsMatch line then
            let t = float (upToPattern.Match(line).Groups.[1].Value)
            startBlock "" t t
        elif roomPattern.IsMatch line then
            startBlock "Room temp." 20.0 20.0
        elif classPattern.IsMatch line then
            let g = classPattern.Match(line).Groups
            let t = float g.[2].Value
            startBlock ("Class " + g.[1].Value) t t
        elif rangePattern.IsMatch line then
            let g = rangePattern.Match(line).Groups
            startBlock $"{g.[1].Value}-{g.[2].Value}" (float g.[1].Value) (float g.[2].Value)
        elif labelledPattern.IsMatch line then
            let g = labelledPattern.Match(line).Groups
            let t = float g.[1].Value
            startBlock g.[2].Value t t
        elif integerPattern.IsMatch line && expectingA () then
            let t = float line
            startBlock "" t t
        elif m.Success then
            if blocks.Count = 0 then
                problems.Add $"valore '{line}' prima di qualsiasi temperatura"
            else
                blocks.[blocks.Count - 1].Items.Add
                    { Mantissa = Double.Parse(m.Groups.[1].Value, inv)
                      Text = m.Groups.[1].Value
                      Exponent =
                        (if m.Groups.[2].Success then
                             Some(int m.Groups.[2].Value)
                         else
                             None)
                      Line = line }
        else
            problems.Add $"riga non riconosciuta: '{line}'"

    List.ofSeq blocks, problems |> Seq.distinct |> List.ofSeq

/// Turns the (mantissa, exponent) items into (A, B) points. Exponent handling is decided per role
/// inside each temperature block: when any value of that role in the block carries an explicit
/// exponent the role is in scientific notation and an omitted exponent repeats the previous one of
/// the same role (reset at each new block, kept across a page continuation); otherwise the values
/// of that role are plain decimals (some blocks print B without exponents).
let buildCurves (blocks: Block list) : ChartCurve list * string list =
    let problems = List<string>()
    let roleIsScientific (b: Block) (role: int) =
        b.Items |> Seq.mapi (fun i it -> i % 2 = role && it.Exponent.IsSome) |> Seq.exists id

    let curves =
        blocks
        |> List.map (fun b ->
            let persisted = [| None; None |]
            let scientific = [| roleIsScientific b 0; roleIsScientific b 1 |]
            let values = List<float>()

            b.Items
            |> Seq.iteri (fun i it ->
                let role = i % 2

                if scientific.[role] then
                    let exponent =
                        match it.Exponent with
                        | Some e ->
                            persisted.[role] <- Some e
                            Some e
                        | None -> persisted.[role]

                    match exponent with
                    | Some e when it.Exponent.IsSome || it.Mantissa < 10.0 -> values.Add(Double.Parse($"{it.Text}E{e}", inv))
                    | Some _ ->
                        problems.Add $"mantissa {it.Line} senza esponente a T={b.TempTo}: ambigua"
                        values.Add nan
                    // Before any explicit exponent a small bare mantissa is a plain value (e.g. 0.965).
                    | None when it.Mantissa < 10.0 -> values.Add it.Mantissa
                    | None ->
                        problems.Add $"esponente mancante per '{it.Line}' a T={b.TempTo}"
                        values.Add nan
                else
                    values.Add it.Mantissa)

            if values.Count % 2 <> 0 then
                problems.Add $"T={b.TempTo} {b.Label}: numero dispari di valori ({values.Count})"

            let points =
                [ for i in 0 .. values.Count / 2 - 1 -> values.[2 * i], values.[2 * i + 1] ]

            { Label = b.Label
              TempFrom = b.TempFrom
              TempTo = b.TempTo
              Points = points })

    curves, problems |> Seq.distinct |> List.ofSeq

let validateCurve (c: ChartCurve) : string list * string list =
    let tag = $"T={c.TempTo} {c.Label}".TrimEnd()
    let problems = List<string>()
    let warnings = List<string>()

    if c.Points.Length < 3 then
        problems.Add $"{tag}: meno di 3 punti ({c.Points.Length})"

    if c.Points |> List.exists (fun (a, b) -> Double.IsNaN a || Double.IsNaN b || a <= 0.0 || b <= 0.0) then
        problems.Add $"{tag}: valori non positivi o non risolti"
    else
        for (a1, b1), (a2, b2) in List.pairwise c.Points do
            if a2 <= a1 then
                problems.Add $"{tag}: A non crescente ({a1:G4} -> {a2:G4})"

            if b2 < b1 * 0.999 then
                problems.Add $"{tag}: B decrescente ({b1:G4} -> {b2:G4} MPa)"


    List.ofSeq problems, List.ofSeq warnings

let parseChart (path: string) : Chart =
    let doc = XDocument.Load path
    let root = doc.Root
    let figure = root.Attribute(XName.Get "figure").Value
    let rawText = root.Element(XName.Get "RawText").Value
    let blocks, tokenProblems = tokenize rawText
    let curves, valueProblems = buildCurves blocks
    let checks = curves |> List.map validateCurve

    let structural =
        [ if curves.IsEmpty then
              "nessuna curva riconosciuta" ]

    // The same label and temperature twice means a block was split or duplicated by the extraction.
    let duplicates =
        curves
        |> List.countBy (fun c -> c.Label, c.TempFrom, c.TempTo)
        |> List.choose (fun ((label, _, tempTo), n) ->
            if n > 1 then Some $"curva duplicata per '{label}' a T={tempTo}" else None)

    let ordering = duplicates

    { Figure = figure
      File = Path.GetFileName path
      Curves = curves |> List.sortBy (fun c -> c.Label, c.TempTo)
      Problems = structural @ tokenProblems @ valueProblems @ ordering @ (checks |> List.collect fst)
      Warnings = checks |> List.collect snd }

// ───────────────────────────── load charts ─────────────────────────────

if not (Directory.Exists xmlDir) then
    failwithf "XML folder not found: %s" xmlDir

let charts =
    Directory.GetFiles(xmlDir, "*.xml")
    |> Array.filter (fun f -> Path.GetFileName f <> "index.xml")
    |> Array.sortBy (fun f ->
        // Natural order: CS-1, CS-2, ..., HA-10 after HA-9.
        let name = Path.GetFileNameWithoutExtension f
        let m = Regex.Match(name, @"^([A-Za-z]+)-(\d+)$")
        if m.Success then (m.Groups.[1].Value, int m.Groups.[2].Value) else (name, 0))
    |> Array.map parseChart
    |> List.ofArray

let validCharts = charts |> List.filter (fun c -> c.Problems.IsEmpty)
let reviewCharts = charts |> List.filter (fun c -> not c.Problems.IsEmpty)

// ───────────────────────────── csv helpers ─────────────────────────────

let csvCell (value: string) =
    if value.IndexOfAny([| separator.[0]; '"'; '\n'; '\r' |]) >= 0 then
        "\"" + value.Replace("\"", "\"\"") + "\""
    else
        value

let writeCsv (path: string) (header: string list) (rows: string list seq) =
    use writer = new StreamWriter(path, false, UTF8Encoding(true))
    writer.WriteLine(header |> List.map csvCell |> String.concat separator)

    for row in rows do
        writer.WriteLine(row |> List.map csvCell |> String.concat separator)

let fmtNum (v: float) = v.ToString("R", inv)

/// Reads a CSV with a header row; handles quoted cells.
let readCsv (path: string) : Map<string, string> list =
    let splitLine (line: string) =
        let cells = List<string>()
        let sb = StringBuilder()
        let mutable inQuotes = false
        let mutable i = 0

        while i < line.Length do
            let ch = line.[i]

            if inQuotes then
                if ch = '"' && i + 1 < line.Length && line.[i + 1] = '"' then
                    sb.Append '"' |> ignore
                    i <- i + 1
                elif ch = '"' then
                    inQuotes <- false
                else
                    sb.Append ch |> ignore
            elif ch = '"' then
                inQuotes <- true
            elif ch = separator.[0] then
                cells.Add(sb.ToString())
                sb.Clear() |> ignore
            else
                sb.Append ch |> ignore

            i <- i + 1

        cells.Add(sb.ToString())
        List.ofSeq cells

    match File.ReadAllLines(path, Encoding.UTF8) |> Array.toList |> List.filter (fun l -> l.Trim() <> "") with
    | [] -> []
    | header :: rows ->
        let names = splitLine (header.TrimStart('﻿')) |> List.map (fun h -> h.Trim())

        rows
        |> List.map (fun r ->
            let cells = splitLine r
            List.zip names (cells @ List.replicate (max 0 (names.Length - cells.Length)) "" |> List.truncate names.Length)
            |> Map.ofList)

// ───────────────────────────── reports ─────────────────────────────

let reportLines = List<string>()
let say (s: string) = reportLines.Add s

say "================ IMPORT EXTERNAL PRESSURE CHARTS ================"
say $"Cartella XML : {xmlDir}"
say $"Grafici letti: {charts.Length}   validi: {validCharts.Length}   da rivedere: {reviewCharts.Length}"
say ""
say "Esito per figura:"

for c in charts do
    let points = c.Curves |> List.sumBy (fun x -> x.Points.Length)

    if c.Problems.IsEmpty then
        let warn = if c.Warnings.IsEmpty then "" else $"  [{c.Warnings.Length} avvisi]"
        say $"  OK       {c.Figure,-8} {c.Curves.Length,3} curve {points,4} punti{warn}"
    else
        say $"  RIVEDERE {c.Figure,-8} {c.Curves.Length,3} curve {points,4} punti"

        for p in c.Problems |> List.truncate 4 do
            say $"             - {p}"

        if c.Problems.Length > 4 then
            say $"             - ... altri {c.Problems.Length - 4} problemi"

let warned = validCharts |> List.filter (fun c -> not c.Warnings.IsEmpty)

if not warned.IsEmpty then
    say ""
    say "Avvisi sui grafici validi:"

    for c in warned do
        for w in c.Warnings do
            say $"  {c.Figure}: {w}"

// Normalized points of the valid charts.
let chartsCsv = Path.Combine(outDir, "external-pressure-charts.csv")

writeCsv
    chartsCsv
    [ "Figura"; "Curva"; "Temp_da_C"; "Temp_a_C"; "Punto"; "FattoreA"; "B_MPa" ]
    (validCharts
     |> Seq.collect (fun c ->
         c.Curves
         |> Seq.collect (fun curve ->
             curve.Points
             |> Seq.mapi (fun i (a, b) ->
                 [ c.Figure; curve.Label; fmtNum curve.TempFrom; fmtNum curve.TempTo; string (i + 1); fmtNum a; fmtNum b ]))))

// ───────────────────────────── materials / map / template ─────────────────────────────

type Material =
    { Id: int
      Specification: string
      Grade: string
      ClassCondition: string
      Uns: string
      ProductForm: string
      Composition: string }

/// Reads the materials from a temporary copy of the database (no side files next to the original).
let readMaterials (dbPath: string) : Material list =
    let temp = Path.Combine(Path.GetTempPath(), $"asme-ep-{Guid.NewGuid():N}.db")
    File.Copy(dbPath, temp, true)

    try
        let cs = SqliteConnectionStringBuilder(DataSource = temp, Mode = SqliteOpenMode.ReadOnly, Pooling = false)
        use conn = new SqliteConnection(cs.ToString())
        conn.Open()
        use cmd = conn.CreateCommand()

        cmd.CommandText <-
            "SELECT ID, Specification, TypeGrade, ClassConditionTemper, AlloyDesignationNumber, ProductForm, NominalComposition
             FROM Materials ORDER BY ID"

        use reader = cmd.ExecuteReader()
        let text (i: int) = if reader.IsDBNull i then "" else reader.GetString(i).Trim()

        [ while reader.Read() do
              yield
                  { Id = reader.GetInt32 0
                    Specification = text 1
                    Grade = text 2
                    ClassCondition = text 3
                    Uns = text 4
                    ProductForm = text 5
                    Composition = text 6 } ]
    finally
        SqliteConnection.ClearAllPools()
        File.Delete temp

match templatePath with
| Some path ->
    let materials = readMaterials sourceDb

    writeCsv
        path
        [ "ID"; "Specifica"; "Grado"; "Classe_Condizione_Tempra"; "UNS"; "Prodotto"; "Composizione_Nominale"; "Figura" ]
        (materials
         |> Seq.map (fun m -> [ string m.Id; m.Specification; m.Grade; m.ClassCondition; m.Uns; m.ProductForm; m.Composition; "" ]))

    let figures = charts |> List.map (fun c -> c.Figure) |> String.concat ", "
    printfn "Template scritto: %s (%d materiali). Compila la colonna Figura con una di: %s" path materials.Length figures
    exit 0
| None -> ()

type Rule =
    { Id: int option
      Specification: string
      Grade: string
      ClassCondition: string
      Uns: string
      Figure: string
      Source: string }

let ruleWeight (r: Rule) =
    [ r.Specification; r.Grade; r.ClassCondition; r.Uns ] |> List.filter (fun s -> s <> "") |> List.length

let sameText (a: string) (b: string) =
    String.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase)

let ruleMatches (m: Material) (r: Rule) =
    match r.Id with
    | Some id -> id = m.Id
    | None ->
        (r.Specification = "" || sameText r.Specification m.Specification)
        && (r.Grade = "" || sameText r.Grade m.Grade)
        && (r.ClassCondition = "" || sameText r.ClassCondition m.ClassCondition)
        && (r.Uns = "" || sameText r.Uns m.Uns)

let rules: Rule list =
    match mapPath with
    | None -> []
    | Some path ->
        readCsv path
        |> List.mapi (fun i row ->
            let get key = row.TryFind key |> Option.defaultValue "" |> fun s -> s.Trim()

            { Id =
                (match Int32.TryParse(get "ID") with
                 | true, id -> Some id
                 | _ -> None)
              Specification = get "Specifica"
              Grade = get "Grado"
              ClassCondition = get "Classe_Condizione_Tempra"
              Uns = get "UNS"
              Figure = get "Figura"
              Source = $"riga {i + 2}" })
        |> List.filter (fun r -> r.Figure <> "")

type Assignment =
    { Material: Material
      Figure: string
      Rule: Rule }

let assignments, ruleProblems =
    match rules with
    | [] -> [], []
    | _ ->
        let materials = readMaterials sourceDb
        let problems = List<string>()

        let assigned =
            [ for m in materials do
                  let matching = rules |> List.filter (ruleMatches m)

                  // Rules written for a single ID beat generic rules; then the more specific rule wins.
                  let best =
                      matching
                      |> List.groupBy (fun r -> if r.Id.IsSome then 1000 else ruleWeight r)
                      |> List.sortByDescending fst
                      |> List.tryHead

                  match best with
                  | None -> ()
                  | Some(_, group) ->
                      match group |> List.map (fun r -> r.Figure) |> List.distinct with
                      | [ figure ] -> yield { Material = m; Figure = figure; Rule = group.Head }
                      | figures ->
                          let names = String.Join(", ", figures)
                          problems.Add $"materiale {m.Id} ({m.Specification} {m.Grade}): regole in conflitto ({names}), saltato" ]

        assigned, List.ofSeq problems

let knownFigures = charts |> List.map (fun c -> c.Figure) |> set
let validFigures = validCharts |> List.map (fun c -> c.Figure) |> set

let importable =
    assignments |> List.filter (fun a -> validFigures.Contains a.Figure)

let skipped =
    assignments
    |> List.filter (fun a -> not (validFigures.Contains a.Figure))
    |> List.map (fun a ->
        let reason =
            if knownFigures.Contains a.Figure then
                "figura da rivedere (non validata)"
            else
                "figura sconosciuta"

        $"materiale {a.Material.Id} ({a.Material.Specification} {a.Material.Grade}) -> {a.Figure}: {reason}")

if mapPath.IsSome then
    say ""
    say $"Mappa materiale -> figura: {rules.Length} regole, {assignments.Length} materiali associati"
    say $"  importabili: {importable.Length}   saltati: {skipped.Length + ruleProblems.Length}"

    for p in (ruleProblems @ skipped) |> List.truncate 30 do
        say $"  - {p}"

// ───────────────────────────── apply ─────────────────────────────

let note = "Imported by Import-ExternalPressureXml.fsx"

if apply then
    match targetDb with
    | None -> failwith "--apply requires --target-db <path>"
    | Some target when String.Equals(target, sourceDb, StringComparison.OrdinalIgnoreCase) ->
        failwith "--target-db must differ from --db: the packaged database is never edited in place"
    | Some target ->
        File.Copy(sourceDb, target, true)

        // Drop stale side files of a previous copy so the new file is opened clean.
        for suffix in [ "-wal"; "-shm" ] do
            if File.Exists(target + suffix) then
                File.Delete(target + suffix)

        let cs = SqliteConnectionStringBuilder(DataSource = target, Pooling = false)
        use conn = new SqliteConnection(cs.ToString())
        conn.Open()

        let exec (sql: string) =
            use cmd = conn.CreateCommand()
            cmd.CommandText <- sql
            cmd.ExecuteNonQuery() |> ignore

        exec "PRAGMA foreign_keys = ON"

        exec
            "CREATE TABLE IF NOT EXISTS ExternalPressureChart (
                 ID INTEGER PRIMARY KEY AUTOINCREMENT,
                 Figure TEXT NOT NULL,
                 Curve TEXT NOT NULL,
                 TempFrom REAL NOT NULL,
                 TempTo REAL NOT NULL,
                 PointIndex INTEGER NOT NULL,
                 FactorA REAL NOT NULL,
                 FactorB_MPa REAL NOT NULL,
                 SourceFile TEXT,
                 UNIQUE (Figure, Curve, TempFrom, TempTo, PointIndex)
             )"

        use transaction = conn.BeginTransaction()

        let run (sql: string) (parameters: (string * obj) list) =
            use cmd = conn.CreateCommand()
            cmd.Transaction <- transaction
            cmd.CommandText <- sql

            for name, value in parameters do
                cmd.Parameters.AddWithValue(name, value) |> ignore

            cmd.ExecuteNonQuery() |> ignore

        for c in validCharts do
            run "DELETE FROM ExternalPressureChart WHERE Figure = $f" [ "$f", box c.Figure ]

            for curve in c.Curves do
                curve.Points
                |> List.iteri (fun i (a, b) ->
                    run
                        "INSERT INTO ExternalPressureChart (Figure, Curve, TempFrom, TempTo, PointIndex, FactorA, FactorB_MPa, SourceFile)
                         VALUES ($f, $c, $t0, $t1, $i, $a, $b, $file)"
                        [ "$f", box c.Figure
                          "$c", box curve.Label
                          "$t0", box curve.TempFrom
                          "$t1", box curve.TempTo
                          "$i", box (i + 1)
                          "$a", box a
                          "$b", box b
                          "$file", box c.File ])

        // Re-running the import replaces the associations written by this script only.
        run "DELETE FROM ExternalPressureTable WHERE Notes LIKE $n" [ "$n", box (note + "%") ]

        for a in importable do
            run
                "INSERT INTO ExternalPressureTable (MaterialID, ReferenceData, Notes) VALUES ($id, $f, $n)"
                [ "$id", box a.Material.Id
                  "$f", box a.Figure
                  "$n", box $"{note}; map {a.Rule.Source}" ]

        transaction.Commit()

        // Leave a single clean file: no -wal/-shm next to the copy.
        exec "PRAGMA wal_checkpoint(TRUNCATE)"
        exec "PRAGMA journal_mode = DELETE"

        use countCmd = conn.CreateCommand()
        countCmd.CommandText <- "SELECT (SELECT COUNT(*) FROM ExternalPressureChart), (SELECT COUNT(*) FROM ExternalPressureTable)"
        use reader = countCmd.ExecuteReader()
        reader.Read() |> ignore
        say ""
        say $"Database scritto: {target}"
        say $"  ExternalPressureChart: {reader.GetInt64 0} punti   ExternalPressureTable: {reader.GetInt64 1} associazioni"
        reader.Close()
        conn.Close()
        SqliteConnection.ClearAllPools()

// ───────────────────────────── final summary ─────────────────────────────

say ""
say "================ RIEPILOGO FINALE ================"
say $"Grafici validi        : {validCharts.Length} / {charts.Length}"
let reviewNames = String.Join(", ", reviewCharts |> List.map (fun c -> c.Figure))
let reviewSuffix = if reviewCharts.IsEmpty then "" else $"  ({reviewNames})"
say $"Grafici da rivedere   : {reviewCharts.Length}{reviewSuffix}"
say $"Punti validati        : {validCharts |> List.sumBy (fun c -> c.Curves |> List.sumBy (fun x -> x.Points.Length))}"

if mapPath.IsNone then
    say "Associazioni materiale: nessuna mappa fornita (usa --make-template e poi --map)."
else
    say $"Materiali associati   : {importable.Length} importabili, {skipped.Length + ruleProblems.Length} saltati"

say (if apply then "Importazione           : eseguita" else "Importazione           : non eseguita (usa --apply --target-db)")
say $"File: {chartsCsv}"

let reportPath = Path.Combine(outDir, "external-pressure-import-report.txt")
say $"File: {reportPath}"
say $"Tempo di esecuzione: {stopwatch.Elapsed.TotalSeconds:F1} s"
say "=================================================="
File.WriteAllLines(reportPath, reportLines, UTF8Encoding(true))

for l in reportLines do
    printfn "%s" l
