// Export-MaterialReport.fsx
//
// Repeatable audit of ASME_Materials.db: one row per material with identity, strength, welding,
// allowable-stress, physical-property, external-pressure and curve-generation data, the list of
// materials missing each audited data group, and a completeness summary.
//
// Usage (from the repository root):
//   dotnet fsi tools/MaterialReport/Export-MaterialReport.fsx
//   dotnet fsi tools/MaterialReport/Export-MaterialReport.fsx --db <path> --out <dir> --sep "," --long
//
// Options:
//   --db <path>    SQLite database (default: src/MaterialLibrary/data/ASME_Materials.db)
//   --out <dir>    output directory (default: tools/MaterialReport/output)
//   --sep <char>   CSV separator (default: ";" so Italian Excel opens it directly)
//   --spec <text>  only materials whose Specification equals <text> (e.g. SA-516)
//   --long         also write material-report-long.csv (one row per temperature point)
//
// Outputs (in --out):
//   material-report.csv          one row per material
//   material-report-missing.csv  one row per (data group, material lacking that group)
//   material-report-summary.txt  completeness summary (also printed at the end)
//   material-report-long.csv     only with --long
//
// The database is never modified.

#load "MaterialReportCore.fsx"

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Text
open MaterialReportCore

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

let dbPath =
    options.TryFind "--db"
    |> Option.defaultValue (Path.Combine(repoRoot, "src", "MaterialLibrary", "data", "ASME_Materials.db"))
    |> Path.GetFullPath

let outDir =
    options.TryFind "--out"
    |> Option.defaultValue (Path.Combine(__SOURCE_DIRECTORY__, "output"))
    |> Path.GetFullPath

let separator = options.TryFind "--sep" |> Option.defaultValue ";"
let specFilter = options.TryFind "--spec"
let writeLong = options.ContainsKey "--long"

Directory.CreateDirectory outDir |> ignore

// ───────────────────────────── formatting ─────────────────────────────

let fmt (v: float) = v.ToString("0.######", inv)

let fmtOpt (v: float option) =
    v |> Option.map fmt |> Option.defaultValue ""

let fmtSize (s: Size) =
    match s.Min, s.Max with
    | None, None -> "tutti gli spessori"
    | lo, hi ->
        let l = lo |> Option.map (fun v -> (if s.MinIncl then ">=" else ">") + fmt v)
        let h = hi |> Option.map (fun v -> (if s.MaxIncl then "<=" else "<") + fmt v)
        "spessore " + String.Join(" ", [ l; h ] |> List.choose id) + " mm"

let fmtCurve (c: Curve) =
    c |> List.map (fun (t, v) -> $"{fmt t}:{fmt v}") |> String.concat " "

let isUnbounded (s: Size) = s.Min.IsNone && s.Max.IsNone

let joinBands (parts: string list) = String.Join(" || ", parts)

/// "[label] curve" per band; the label is dropped when there is a single unbounded band.
let fmtBandedCurves (label: Band -> string) (bands: Band list) =
    match bands with
    | [ b ] when isUnbounded b.Size && b.Case = Normal -> fmtCurve b.Curve
    | _ ->
        bands
        |> List.filter (fun b -> not b.Curve.IsEmpty)
        |> List.map (fun b -> $"[{label b}] {fmtCurve b.Curve}")
        |> joinBands

let fmtBandedScalar (label: Band -> string) (pick: Band -> float option) (bands: Band list) =
    let items = bands |> List.choose (fun b -> pick b |> Option.map (fun v -> b, v))

    match items with
    | [ b, v ] when isUnbounded b.Size -> fmt v
    | _ -> items |> List.map (fun (b, v) -> $"[{label b}] {fmt v}") |> joinBands

let sourceLabel (b: Band) =
    String.Join(", ", [ b.Source; caseText b.Case; fmtSize b.Size ])

let sizeLabel (b: Band) = fmtSize b.Size

let okNo (present: bool) = if present then "ok" else "no"

let distinctNotes (bands: Band list) =
    bands |> List.choose (fun b -> b.Notes) |> List.distinct |> String.concat " | "

// ───────────────────────────── one report row ─────────────────────────────

let cellsOf (d: MaterialData) : (string * string) list =
    let ssMissing = stressStrainMissing d
    let hasElongation = d.ElongationLong.IsSome || d.ElongationTransverse.IsSome

    let weldDetail =
        d.Welding
        |> List.map (fun (p, g, thk) ->
            let thkText = thk |> Option.map (fun s -> $" [{s}]") |> Option.defaultValue ""
            let pText = defaultArg p "-"
            let gText = defaultArg g "-"
            $"P{pText}/G{gText}{thkText}")
        |> joinBands

    let distinct (pick: string option * string option * string option -> string option) =
        d.Welding |> List.choose pick |> List.distinct |> String.concat " | "

    [ "ID", string d.Id
      "Specifica", d.Specification
      "Grado", d.Grade
      "Classe_Condizione_Tempra", d.ClassCondition
      "UNS", d.Uns
      "Composizione_Nominale", d.Composition
      "Prodotto", d.ProductForm
      "SMYS_MPa", fmtOpt d.Smys
      "SMTS_MPa", fmtOpt d.Smts
      "Allungamento_Long_pct", fmtOpt d.ElongationLong
      "Allungamento_Trasv_pct", fmtOpt d.ElongationTransverse
      "Allungamento_Range_Spessori", (if hasElongation then "non modellato nel DB" else "")
      "ASME_IX_P_No", distinct (fun (p, _, _) -> p)
      "ASME_IX_G_No", distinct (fun (_, g, _) -> g)
      "ASME_IX_Dettaglio", weldDetail
      "Div1_Tmax_C", fmtBandedScalar sourceLabel (fun b -> b.MaxTemp) d.Div1
      "Div1_T_tempo_dipendente_C", fmtBandedScalar sourceLabel (fun b -> b.CreepTemp) d.Div1
      "Div1_Ammissibile_MPa", fmtBandedCurves sourceLabel d.Div1
      "Div1_Stress_Alto_G5",
      (if d.Div1.IsEmpty then ""
       elif d.Div1 |> List.exists (fun b -> b.Case = HighG5) then "SI"
       elif d.Div1 |> List.exists (fun b -> b.Case = SingleG5) then "RIGA UNICA G5"
       else "NO")
      "Div1_Non_Ammesso", (if d.Div1NotPermitted then "SI: tabella 1A/1B/3 presente ma senza Tmax VIII-1" else "")
      "Div1_Note", distinctNotes d.Div1
      "Div2_Tmax_C", fmtBandedScalar sourceLabel (fun b -> b.MaxTemp) d.Div2
      "Div2_T_tempo_dipendente_C", fmtBandedScalar sourceLabel (fun b -> b.CreepTemp) d.Div2
      "Div2_Ammissibile_MPa", fmtBandedCurves sourceLabel d.Div2
      "Div2_Note", distinctNotes d.Div2
      "Sy_MPa", fmtBandedCurves sizeLabel d.Sy
      "Su_MPa", fmtBandedCurves sizeLabel d.Su
      "E_GPa", fmtCurve d.Elastic
      "Densita_kg_m3_20C", fmtOpt d.Density
      "Poisson", fmtOpt d.Poisson
      "Alfa_Istantaneo_1e-6_per_C", fmtCurve d.AlphaInstantaneous
      "Alfa_Medio_1e-6_per_C_derivato", fmtCurve d.AlphaMean
      "Calore_Specifico_J_kgK", fmtCurve d.SpecificHeat
      "Conducibilita_W_mK", fmtCurve d.Conductivity
      "Diffusivita_mm2_s", fmtCurve d.Diffusivity
      "ExtPressure_Chart", d.Charts |> String.concat " | "
      "ExtPressure_Note_Ammissibili", String.Join(" || ", d.ExternalPressureNotes)
      "StressStrain_Dati",
      String.Join(
          " ",
          [ "E:" + okNo (not d.Elastic.IsEmpty)
            "Sy:" + okNo (not d.Sy.IsEmpty)
            "Su:" + okNo (not d.Su.IsEmpty)
            "nu:" + okNo d.Poisson.IsSome ]
      )
      "StressStrain_Pronto", (if ssMissing.IsEmpty then "SI" else "NO: manca " + String.Join(", ", ssMissing))
      "Ciclica_Dati", "Kcss/Ncss non presenti nel DB"
      "Ciclica_Pronto", "NO: manca " + String.Join(", ", ssMissing @ [ "Kcss/Ncss" ])
      "Dati_Mancanti", String.Join("; ", missingGroups d) ]

let longRowsOf (d: MaterialData) : string list list =
    let rows (property: string) (unit': string) (source: string) (case: string) (size: Size) (curve: Curve) =
        curve
        |> List.map (fun (t, v) ->
            [ string d.Id; property; source; case; fmtOpt size.Min; fmtOpt size.Max; fmt t; fmt v; unit' ])

    let caseOf (b: Band) = caseText b.Case

    [ for b in d.Sy do
          yield! rows "Sy" "MPa" "Table_Y" "" b.Size b.Curve
      for b in d.Su do
          yield! rows "Su" "MPa" "Table_U" "" b.Size b.Curve
      for b in d.Div1 do
          yield! rows "Allowable_Div1" "MPa" b.Source (caseOf b) b.Size b.Curve
      for b in d.Div2 do
          yield! rows "Allowable_Div2" "MPa" b.Source (caseOf b) b.Size b.Curve
      yield! rows "E" "GPa" "ElasticModulusTable" "" noSize d.Elastic
      yield! rows "Alfa_istantaneo" "1e-6/degC" "ThermalExpansionTable (A)" "" noSize d.AlphaInstantaneous
      yield! rows "Alfa_medio_derivato" "1e-6/degC" "integrazione di A da 20 degC" "" noSize d.AlphaMean
      yield! rows "Cp" "J/kg/K" "SpecificHeatTable" "" noSize d.SpecificHeat
      yield! rows "Lambda" "W/m/K" "ThermalConductivityTable" "" noSize d.Conductivity
      yield! rows "Diffusivita" "mm2/s" "ThermalDiffusivityTable" "" noSize d.Diffusivity ]

// ───────────────────────────── run ─────────────────────────────

let db = load dbPath

let selected =
    match specFilter with
    | Some s -> db.Materials |> List.filter (fun m -> str m "Specification" = Some s)
    | None -> db.Materials

let data = selected |> List.map (buildData db)

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

let written = List<string>()

let reportPath = Path.Combine(outDir, "material-report.csv")

match data with
| first :: _ ->
    writeCsv reportPath (cellsOf first |> List.map fst) (data |> Seq.map (fun d -> cellsOf d |> List.map snd))
    written.Add reportPath
| [] -> ()

// Materials lacking each audited data group (complete list, one row per group/material).
let missingPath = Path.Combine(outDir, "material-report-missing.csv")

let missingRows =
    [ for name, present in groups do
          for d in data do
              if not (present d) then
                  yield [ name; string d.Id; d.Specification; d.Grade; d.ClassCondition; d.Uns; d.ProductForm ] ]

writeCsv missingPath [ "Gruppo_Dati"; "ID"; "Specifica"; "Grado"; "Classe_Condizione_Tempra"; "UNS"; "Prodotto" ] missingRows
written.Add missingPath

if writeLong then
    let longPath = Path.Combine(outDir, "material-report-long.csv")

    writeCsv
        longPath
        [ "MaterialID"; "Proprieta"; "Fonte"; "Caso"; "Spessore_Min_mm"; "Spessore_Max_mm"; "Temperatura_C"; "Valore"; "Unita" ]
        (data |> Seq.collect longRowsOf)

    written.Add longPath

// ───────────────────────────── summary ─────────────────────────────

let summary = StringBuilder()
let line (s: string) = summary.AppendLine s |> ignore

let total = data.Length
let complete = data |> List.filter (fun d -> (missingGroups d).IsEmpty) |> List.length

// A material is "core complete" when every group the database can hold is present; the three
// groups below are known gaps of the database as shipped and are reported separately.
let knownGaps = set [ "Allungamento a rottura"; "External Pressure Chart"; "Curva Stress-Strain ciclica" ]

let coreComplete =
    data
    |> List.filter (fun d -> missingGroups d |> List.forall knownGaps.Contains)
    |> List.length

line "================ RIEPILOGO FINALE ================"
line $"Database           : {dbPath}"
line $"Materiali analizzati: {total}"
line $"Completi (tutti i gruppi)                : {complete}"
let knownGapsText = String.Join(", ", knownGaps)
line $"Completi sui gruppi presenti nel DB      : {coreComplete}   (esclusi: {knownGapsText})"
line $"Con lacune nei gruppi che il DB puo' contenere: {total - coreComplete}"
let div1NotPermitted = data |> List.filter (fun d -> d.Div1NotPermitted) |> List.length
line $"  di cui Div1 mancante perche' la tabella 1A/1B/3 non da' Tmax VIII-1 (non ammesso): {div1NotPermitted}"
line ""
line "Materiali privi di dati per gruppo controllato:"

for name, present in groups do
    let lacking = data |> List.filter (fun d -> not (present d))
    let percent = if total = 0 then 0.0 else 100.0 * float lacking.Length / float total
    line $"  {name,-28} {lacking.Length,5} / {total}  ({percent:F1}%%)"

    // Small groups are listed inline; the complete lists are in material-report-missing.csv.
    if lacking.Length > 0 && lacking.Length <= 20 then
        for d in lacking do
            let grade = if d.Grade = "" then "" else " " + d.Grade
            let cond = if d.ClassCondition = "" then "" else " " + d.ClassCondition
            let entry = $"      ID {d.Id}: {d.Specification}{grade}{cond} {d.Uns}"
            line (entry.TrimEnd())

line ""
line "Note di lettura:"
line "  - Il DB memorizza il coefficiente di dilatazione ISTANTANEO (colonna A delle tabelle TE ASME)."
line "    Il coefficiente MEDIO 20 degC -> T (colonna B) e' derivato per integrazione trapezoidale."
line "  - Allungamento a rottura: vuoto nel DB; i range di spessore non sono modellati."
line "  - Div1 include solo righe con temperatura massima VIII-1 definita; Div2 le righe Tabella 5A/5B;"
line "    la bulloneria (Tabella 3) compare in Div1/Div2 con fonte 'T3 bulloneria'."
line "  - Stress alto (nota G5): il DB mette G5 su entrambe le righe di un materiale; la riga ALTA e' quella"
line "    con le tensioni maggiori nel gruppo (stessa tabella e stesso range di spessore). 'G5 riga unica' = manca la riga gemella."
line "  - External Pressure Chart: popolato solo dopo Import-ExternalPressureXml.fsx con una mappa materiale -> figura."
line "  - Curva ciclica (VIII-2 3-D.4): servono Kcss/Ncss (Tabella 3-D.2M), assenti dal DB."
line ""
line "File scritti:"

let summaryPath = Path.Combine(outDir, "material-report-summary.txt")
written.Add summaryPath

for path in written do
    line $"  {path}"

line $"Tempo di esecuzione: {stopwatch.Elapsed.TotalSeconds:F1} s"
line "=================================================="

File.WriteAllText(summaryPath, summary.ToString(), UTF8Encoding(true))
printfn "%s" (summary.ToString())
