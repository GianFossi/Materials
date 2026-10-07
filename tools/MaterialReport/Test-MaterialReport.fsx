// Test-MaterialReport.fsx
//
// Functional check of ASME_Materials.db on a fixed list of commonly used pressure-vessel
// materials. For every requested specification/grade it verifies that the material exists and that
// the data the report relies on is present and physically consistent.
//
// Usage (from the repository root):
//   dotnet fsi tools/MaterialReport/Test-MaterialReport.fsx
//   dotnet fsi tools/MaterialReport/Test-MaterialReport.fsx --db <path> --strict --quiet
//
// Options:
//   --db <path>   SQLite database (default: src/MaterialLibrary/data/ASME_Materials.db)
//   --strict      also fail when a requested material is not in the database
//   --quiet       print only materials with FAIL/WARN and the final summary
//
// Severity:
//   FAIL  required data missing or inconsistent (SMYS/SMTS, Sy, Su, E, density, thermal
//         expansion, Cp/conductivity/diffusivity, Division 1 allowable, welding P/G number,
//         stress-strain readiness, cross-checks between tables)
//   WARN  data the database is known not to hold or that is optional for the material
//         (rupture elongation, external pressure chart, cyclic curve data, Division 2 allowable)
//
// Exit code: 0 when no FAIL (and, with --strict, no missing material); 1 otherwise.

#load "MaterialReportCore.fsx"

open System
open System.IO
open MaterialReportCore

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

let strict = options.ContainsKey "--strict"
let quiet = options.ContainsKey "--quiet"

// ───────────────────────────── requested materials ─────────────────────────────

/// (specification, accepted TypeGrade spellings). An empty grade list selects every grade of the
/// specification. "TP11"/"T11" are both accepted because SA-213 names them T11/T22 in the database.
let requested: (string * string list) list =
    [ "SA-516", [ "60" ]
      "SA-516", [ "70" ]
      "SA-266", [ "2" ]
      "SA-266", [ "4" ]
      "SA-105", []
      "SA-765", [ "II" ]
      "SA-765", [ "IV" ]
      "SA-179", []
      "SA-210", []
      "SA-387", [ "1" ]
      "SA-387", [ "2" ]
      "SA-387", [ "11" ]
      "SA-387", [ "22" ]
      "SA-387", [ "22V" ]
      "SA-336", [ "F1" ]
      "SA-336", [ "F2" ]
      "SA-336", [ "F11" ]
      "SA-336", [ "F22" ]
      "SA-336", [ "F22V" ]
      "SA-213", [ "TP11"; "T11" ]
      "SA-213", [ "TP22"; "T22" ]
      "SA-204", [ "A" ]
      "SA-204", [ "B" ]
      "SA-240", [ "304" ]
      "SA-240", [ "304L" ]
      "SA-240", [ "304H" ]
      "SA-240", [ "316" ]
      "SA-240", [ "316L" ]
      "SA-240", [ "316H" ]
      "SA-240", [ "317" ]
      "SA-240", [ "317L" ]
      "SA-240", [ "321" ]
      "SA-240", [ "347" ]
      "SA-240", [ "347H" ]
      "SA-965", [ "F304" ]
      "SA-965", [ "F304L" ]
      "SA-965", [ "F304H" ]
      "SA-965", [ "F316" ]
      "SA-965", [ "F316L" ]
      "SA-965", [ "F316H" ]
      "SA-965", [ "F317" ]
      "SA-965", [ "F321" ]
      "SA-965", [ "F347" ]
      "SA-965", [ "F347H" ]
      "SA-213", [ "TP304" ]
      "SA-213", [ "TP316" ]
      "SA-213", [ "TP317" ]
      "SA-213", [ "TP321" ]
      "SA-213", [ "TP347" ]
      "SA-213", [ "TP304L" ]
      "SA-213", [ "TP316L" ]
      "SA-213", [ "TP317L" ]
      "SA-213", [ "TP321L" ]
      "SA-213", [ "TP347L" ]
      "SA-106", [ "B" ]
      "SA-106", [ "C" ]
      // The database has one SA-350 LF2 row without a class: Class 1 and Class 2 are not split.
      "SA-350", [ "LF2" ]
      "SA-350", [ "LF3" ]
      "SA-203", []
      "SA-553", []
      "SA-353", []
      "SA-333", [ "6" ]
      "SA-387", [ "12" ]
      "SA-336", [ "F12" ] ]

/// Groups selected by alloy designation (UNS) or by nominal composition, across every product form
/// and specification. Each entry: label, predicate.
let requestedGroups: (string * (MaterialData -> bool)) list =
    let uns (code: string) = (fun (d: MaterialData) -> d.Uns = code)

    [ "UNS N06625 (tutti i prodotti)", uns "N06625"
      "UNS N08800 (tutti i prodotti)", uns "N08800"
      "UNS N08810 (tutti i prodotti)", uns "N08810"
      "UNS N08811 (tutti i prodotti)", uns "N08811"
      "5Cr (tutti i prodotti e gradi)", (fun d -> d.Composition.StartsWith("5Cr-", StringComparison.Ordinal))
      "9Cr-1Mo (tutti i prodotti e gradi)", (fun d -> d.Composition = "9Cr-1Mo")
      "9Cr-1Mo-V (tutti i prodotti e gradi)", (fun d -> d.Composition = "9Cr-1Mo-V") ]

/// Minimum yield / tensile strengths (MPa) of the Code, for a spot check of the scalar columns.
/// (specification, grade) -> (SMYS, SMTS). Only values that are unambiguous for the grade.
let reference: ((string * string) * (float * float)) list =
    [ ("SA-516", "60"), (220.0, 415.0)
      ("SA-516", "70"), (260.0, 485.0)
      ("SA-105", ""), (250.0, 485.0)
      ("SA-179", ""), (180.0, 325.0)
      ("SA-240", "304"), (205.0, 515.0)
      ("SA-240", "304L"), (170.0, 485.0)
      ("SA-240", "316"), (205.0, 515.0)
      ("SA-240", "316L"), (170.0, 485.0)
      ("SA-240", "321"), (205.0, 515.0)
      ("SA-240", "347"), (205.0, 515.0)
      ("SA-213", "TP304"), (205.0, 515.0)
      ("SA-213", "TP316"), (205.0, 515.0)
      ("SA-213", "TP304L"), (170.0, 485.0)
      ("SA-213", "TP316L"), (170.0, 485.0) ]

// ───────────────────────────── checks ─────────────────────────────

type Outcome =
    | Pass
    /// Known limitation of the database, identical for every material: never changes the verdict.
    | Info
    | Warn
    | Fail

type Check =
    { Name: string
      Outcome: Outcome
      Detail: string }

let fmt (v: float) = v.ToString("0.###", inv)

/// A consistency screen against the Code criteria: a deviation is reported for verification (WARN),
/// not as a failure, because the criteria use ratios and rounding that the tables only approximate.
let screen name ok detail =
    { Name = name
      Outcome = (if ok then Pass else Warn)
      Detail = (if ok then "" else detail) }

let check name ok detail =
    { Name = name
      Outcome = (if ok then Pass else Fail)
      Detail = (if ok then "" else detail) }

let warnIf name missing detail =
    { Name = name
      Outcome = (if missing then Warn else Pass)
      Detail = (if missing then detail else "") }

let infoIf name missing detail =
    { Name = name
      Outcome = (if missing then Info else Pass)
      Detail = (if missing then detail else "") }

let isNonIncreasing (curve: Curve) =
    curve |> List.pairwise |> List.forall (fun ((_, a), (_, b)) -> b <= a + 1e-9)

let inRange lo hi v = v >= lo && v <= hi

/// Bands that cover the same thickness range as `size`; falls back to the only band when unique.
let matchingBand (size: Size) (bands: Band list) =
    match bands |> List.tryFind (fun b -> b.Size = size) with
    | Some b -> Some b
    | None ->
        match bands with
        | [ only ] -> Some only
        | _ -> None

let tolerance = 1.03

let checksFor (d: MaterialData) : Check list =
    let eAt25 = valueAt 25.0 d.Elastic
    let alphaAt20 = valueAt 20.0 d.AlphaInstantaneous
    let meanAt20 = valueAt 20.0 d.AlphaMean
    // Lowest temperature at which specific heat, conductivity and diffusivity are all tabulated.
    let thermalTriple =
        d.SpecificHeat
        |> List.tryPick (fun (t, cp) ->
            match valueAt t d.Conductivity, valueAt t d.Diffusivity with
            | Some l, Some a -> Some(t, cp, l, a)
            | _ -> None)

    let syVsSu =
        [ for sy in d.Sy do
              match matchingBand sy.Size d.Su with
              | Some su ->
                  for t, s in sy.Curve do
                      match valueAt t su.Curve with
                      | Some u when s > u * 1.001 -> yield $"{fmt t} degC: Sy {fmt s} > Su {fmt u}"
                      | _ -> ()
              | None -> () ]

    // Upper bounds that every allowable-stress line must respect, whatever the creep criteria:
    //   Division 1: S <= SMTS/3.5; normal line S <= 2/3 Sy(T); higher line (G5) S <= 0.9 Sy(T)
    //   Division 2: S <= SMTS/2.4; S <= 0.9 Sy(T) (austenitic grades may exceed 2/3 Sy)
    let allowableViolations (bands: Band list) (suDivisor: float) (normalSyFactor: float) =
        [ for b in bands do
              for t, s in b.Curve do
                  match d.Smts with
                  | Some smts when s > smts / suDivisor * tolerance ->
                      yield $"{b.Source} {fmt t} degC: S {fmt s} > SMTS/{fmt suDivisor} = {fmt (smts / suDivisor)}"
                  | _ -> ()

                  match matchingBand b.Size d.Sy with
                  | Some sy ->
                      match valueAt t sy.Curve with
                      | Some y ->
                          let factor = if b.Case = Normal then normalSyFactor else 0.9
                          let limit = factor * y

                          if s > limit * tolerance then
                              let hint = if b.Case = Normal && normalSyFactor < 0.9 then " (nota G5 mancante nel DB?)" else ""
                              yield $"{b.Source} {caseText b.Case} {fmt t} degC: S {fmt s} > {fmt factor} Sy = {fmt limit}{hint}"
                      | None -> ()
                  | None -> () ]

    let div1Violations = allowableViolations d.Div1 3.5 (2.0 / 3.0)
    let div2Violations = allowableViolations d.Div2 2.4 0.9

    let referenceCheck =
        reference
        |> List.tryFind (fun ((spec, grade), _) ->
            spec = d.Specification && (grade = d.Grade || (grade = "" && d.Grade = "")))
        |> Option.map (fun (_, (smys, smts)) ->
            let shown (v: float option) = v |> Option.map fmt |> Option.defaultValue "-"

            check
                "SMYS/SMTS uguali ai valori di Codice"
                (d.Smys = Some smys && d.Smts = Some smts)
                $"DB: SMYS {shown d.Smys} / SMTS {shown d.Smts}, atteso {fmt smys} / {fmt smts}")

    [ yield check "SMYS e SMTS presenti con SMYS < SMTS" (match d.Smys, d.Smts with
                                                         | Some y, Some u -> y < u
                                                         | _ -> false) "SMYS/SMTS mancanti o incoerenti"
      yield! Option.toList referenceCheck
      yield check "Sy presente" (not d.Sy.IsEmpty) "tabella Sy assente"
      yield check "Su presente" (not d.Su.IsEmpty) "tabella Su assente"
      yield check "Sy <= Su a ogni temperatura" syVsSu.IsEmpty (String.Join("; ", syVsSu |> List.truncate 3))
      yield check "Modulo elastico presente" (not d.Elastic.IsEmpty) "nessun gruppo modulo elastico"
      yield check "E decrescente con la temperatura" (isNonIncreasing d.Elastic) "E non monotono"
      yield check "E(25 degC) tra 150 e 230 GPa" (eAt25 |> Option.exists (inRange 150.0 230.0)) $"E(25) = {eAt25}"
      yield check "Densita tra 7000 e 9000 kg/m3" (d.Density |> Option.exists (inRange 7000.0 9000.0)) $"densita = {d.Density}"
      yield check "Poisson tra 0.25 e 0.35" (d.Poisson |> Option.exists (inRange 0.25 0.35)) $"nu = {d.Poisson}"
      yield check "Dilatazione istantanea presente" (not d.AlphaInstantaneous.IsEmpty) "nessun gruppo dilatazione"

      yield
          check
              "Dilatazione tra 8 e 25 e-6/degC"
              (d.AlphaInstantaneous |> List.forall (fun (_, a) -> inRange 8.0 25.0 a))
              "valori fuori campo"

      yield
          check
              "Dilatazione media derivata coerente (media(20) = istantanea(20))"
              (match alphaAt20, meanAt20 with
               | Some a, Some m -> abs (a - m) < 1e-9
               | _ -> false)
              "media(20) diversa da istantanea(20)"

      yield check "Calore specifico presente" (not d.SpecificHeat.IsEmpty) "tabella Cp assente per il materiale"
      yield check "Conducibilita presente" (not d.Conductivity.IsEmpty) "nessun gruppo conducibilita"
      yield check "Diffusivita presente" (not d.Diffusivity.IsEmpty) "nessun gruppo diffusivita"

      yield
          check
              "Cp = lambda / (rho * a) alla prima temperatura comune (entro 2%)"
              (match thermalTriple, d.Density with
               | Some(_, cp, l, a), Some rho -> abs (cp - l / (rho * a * 1e-6)) / cp < 0.02
               | _ -> false)
              "Cp, conducibilita, diffusivita e densita non coerenti"

      yield check "P/G Number ASME IX presenti" (not d.Welding.IsEmpty) "nessuna riga DataTableASME"
      yield
          (if d.Div1NotPermitted then
               warnIf "Ammissibile Div1 presente" true "tabella 1A/1B presente ma senza Tmax VIII-1: materiale non ammesso in VIII-1"
           else
               check "Ammissibile Div1 presente" (not d.Div1.IsEmpty) "nessuna riga nelle tabelle 1A/1B/3")

      yield
          check
              "Ammissibile Div1 con Tmax e valori"
              (d.Div1 |> List.forall (fun b -> b.MaxTemp.IsSome && not b.Curve.IsEmpty))
              "riga Div1 senza Tmax o senza valori"

      yield screen "Ammissibile Div1 entro SMTS/3.5, 2/3 Sy (0.9 Sy se G5), tol 3%" div1Violations.IsEmpty (String.Join("; ", div1Violations |> List.truncate 3))
      yield screen "Ammissibile Div2 entro SMTS/2.4 e 0.9 Sy, tol 3%" div2Violations.IsEmpty (String.Join("; ", div2Violations |> List.truncate 3))
      yield check "Dati per curva stress-strain completi" (stressStrainMissing d).IsEmpty ("manca " + String.Join(", ", stressStrainMissing d))
      yield warnIf "Ammissibile Div2 presente" d.Div2.IsEmpty "nessuna riga Tabella 5A/5B"
      yield infoIf "Allungamento a rottura" (d.ElongationLong.IsNone && d.ElongationTransverse.IsNone) "campo vuoto nel DB"
      yield infoIf "External Pressure Chart" d.Charts.IsEmpty "nessuna figura associata"
      yield infoIf "Dati curva ciclica (Kcss/Ncss)" true "non presenti nel DB" ]

// ───────────────────────────── helper self-test ─────────────────────────────

/// Mean coefficient derivation against column B of ASME TE-1 (Group 1: carbon and low alloy
/// steels), read from the staged XML of the repository when available.
let helperChecks (db: Db) : Check list =
    let group1 = db.Expansion.[1]
    let inst = curveOf group1
    let mean = meanFromInstantaneous inst
    // ASME II-D Metric 2025, Table TE-1, Group 1, column B (mean coefficient from 20 degC).
    let columnB = [ 50.0, 11.8; 100.0, 12.1; 200.0, 12.7; 300.0, 13.3; 400.0, 13.8; 500.0, 14.4 ]

    let deviations =
        [ for t, b in columnB do
              match valueAt t mean with
              | Some m when abs (m - b) > 0.1 -> yield $"{fmt t} degC: derivato {fmt m} vs ASME {fmt b}"
              | None -> yield $"{fmt t} degC: assente"
              | _ -> () ]

    [ check "Coefficiente medio derivato = colonna B ASME TE-1 (Gruppo 1, tol 0.1)" deviations.IsEmpty (String.Join("; ", deviations)) ]

// ───────────────────────────── run ─────────────────────────────

let db = load dbPath
let all = db.Materials |> List.map (buildData db)

let line (s: string) = printfn "%s" s

line "================ TEST MATERIALI ================"
line $"Database: {dbPath}"
line ""

let helperResults = helperChecks db

for c in helperResults do
    let tag = if c.Outcome = Pass then "PASS" else "FAIL"
    line $"[{tag}] {c.Name} {c.Detail}"

line ""

let label (spec: string) (grades: string list) =
    match grades with
    | [] -> $"{spec} (tutti i gradi)"
    | g :: _ -> $"{spec} Gr {g}"

type Request =
    { Label: string
      Matches: MaterialData -> bool }

let requests: Request list =
    [ for spec, grades in requested ->
          { Label = label spec grades
            Matches =
              fun d ->
                  d.Specification = spec
                  && (grades.IsEmpty || grades |> List.exists (fun g -> String.Equals(g, d.Grade, StringComparison.OrdinalIgnoreCase))) }
      for name, predicate in requestedGroups ->
          { Label = name
            Matches = predicate } ]

type Result =
    { Request: string
      Material: MaterialData option
      Checks: Check list }

let results =
    [ for request in requests do
          let matches = all |> List.filter request.Matches

          if matches.IsEmpty then
              yield
                  { Request = request.Label
                    Material = None
                    Checks = [] }
          else
              for d in matches do
                  yield
                      { Request = request.Label
                        Material = Some d
                        Checks = checksFor d } ]

let outcomeOf (r: Result) =
    if r.Material.IsNone then "NOT FOUND"
    elif r.Checks |> List.exists (fun c -> c.Outcome = Fail) then "FAIL"
    elif r.Checks |> List.exists (fun c -> c.Outcome = Warn) then "WARN"
    else "PASS"

for r in results do
    let outcome = outcomeOf r

    match r.Material with
    | None -> line $"[NOT FOUND] {r.Request}: materiale non presente nel database"
    | Some d ->
        let condition = if d.ClassCondition = "" then "" else $" Cl/Cond {d.ClassCondition}"
        let judged = r.Checks |> List.filter (fun c -> c.Outcome <> Info)
        let passed = judged |> List.filter (fun c -> c.Outcome = Pass) |> List.length
        let infos = r.Checks |> List.filter (fun c -> c.Outcome = Info) |> List.map (fun c -> c.Name)

        if not quiet || outcome <> "PASS" then
            line $"[{outcome}] ID {d.Id} {d.Specification} {d.Grade}{condition} ({d.ProductForm}) - {passed}/{judged.Length} controlli superati"

            for c in r.Checks do
                if c.Outcome = Fail || c.Outcome = Warn then
                    let tag = if c.Outcome = Fail then "FAIL" else "WARN"
                    line $"        {tag}: {c.Name} - {c.Detail}"

            if not infos.IsEmpty && not quiet then
                let names = String.Join(", ", infos)
                line $"        lacune note del DB: {names}"

// ───────────────────────────── final summary ─────────────────────────────

let count (name: string) = results |> List.filter (fun r -> outcomeOf r = name) |> List.length
let passCount = count "PASS"
let warnCount = count "WARN"
let notFound = results |> List.filter (fun r -> r.Material.IsNone) |> List.map (fun r -> r.Request)
let failed = count "FAIL"
let helperFailed = helperResults |> List.exists (fun c -> c.Outcome = Fail)

line ""
line "================ RIEPILOGO FINALE ================"
line $"Richieste (specifica/grado/gruppo)  : {requests.Length}"
line $"Materiali trovati e verificati    : {results.Length - notFound.Length}"
line $"  PASS                            : {passCount}"
line $"  WARN (da verificare)            : {warnCount}"
line $"  FAIL                            : {failed}"
line $"Richieste senza materiale nel DB  : {notFound.Length}"

if not notFound.IsEmpty then
    let names = String.Join(", ", notFound)
    line $"  non presenti: {names}"

if failed > 0 then
    line ""
    line "Materiali con FAIL (dato richiesto mancante o incoerente):"

    for r in results do
        match r.Material with
        | Some d when r.Checks |> List.exists (fun c -> c.Outcome = Fail) ->
            let names = r.Checks |> List.filter (fun c -> c.Outcome = Fail) |> List.map (fun c -> c.Name)
            let grade = if d.Grade = "" then "" else " " + d.Grade
            let cond = if d.ClassCondition = "" then "" else " " + d.ClassCondition
            let text = String.Join("; ", names)
            line $"  ID {d.Id} {d.Specification}{grade}{cond} {d.Uns}: {text}"
        | _ -> ()

line ""
line "WARN = Div2 assente, Div1 assente perche' il materiale non e' ammesso in VIII-1, o scostamento dai"
line "criteri del Codice da verificare. Le lacune note del DB (allungamento a rottura, External Pressure"
line "Chart, Kcss/Ncss) valgono per tutti i materiali e non cambiano l'esito."

let failedRun = failed > 0 || helperFailed || (strict && not notFound.IsEmpty)
line (if failedRun then "ESITO: FALLITO" else "ESITO: OK")
line "=================================================="

if failedRun then
    exit 1
