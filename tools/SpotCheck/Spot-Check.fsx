// Author: Claude
// Last revision: 2026-11-01
// Purpose: Print the product data sheet of one or more materials, plus spot-check evidence (ASME source rows vs stored curves, XML provenance vs material identity).
//
// Usage (from the repository root; the libraries must be built once into tools/ProductSheet/bin):
//   dotnet build src/MaterialLibrary.Crud/MaterialLibrary.Crud.fsproj -c Release -f net10.0 -o tools/ProductSheet/bin
//   dotnet fsi tools/SpotCheck/Spot-Check.fsx --spec SA-516 --grade 70
//   dotnet fsi tools/SpotCheck/Spot-Check.fsx --id 985 --design 350,400,475 --operating 100,200,250
//   dotnet fsi tools/SpotCheck/Spot-Check.fsx --random 5 --seed 42 --out ./spot
// Options:
//   --db <file>                 database (default: database/data/materials_2025.db), opened read-only
//   --id <n[,n...]>             Main.id values            | --spec/--grade/--condition/--uns  exact identity (case-insensitive)
//   --random <N> [--seed <s>]   N random materials that have both mechanical curves and imported physical data (reproducible with --seed)
//   --room <degC> --design <a,b,c> --operating <a,b,c>   sheet temperatures (defaults 20 / 100,200,300 / 50,100,150; max 3 each)
//   --out <folder>              write one Markdown file per material instead of printing the sheet
//   --no-sheet                  print only the checks
// Checks printed for every material:
//   1. each mechanical curve compared with the ASME row it was imported from (SourceRowComparison);
//   2. each physical curve / constant with its XML record and a flag when the record's UNS or composition differs from the material.
// The script never writes to the database. Units: temperatures degC, stresses MPa, sizes mm.

// The project libraries are referenced from the folder where Generate-ProductSheet.ps1 (or the build command above) puts them.
#r "../ProductSheet/bin/MaterialLibrary.dll"
#r "../ProductSheet/bin/MaterialLibrary.Crud.dll"
// SQLite access; the version matches the one the libraries were built against.
#r "nuget: Microsoft.Data.Sqlite, 10.0.13"

open System
open System.Globalization
open System.IO
open System.Text.Json
open Microsoft.Data.Sqlite
open MaterialLibrary.Crud
open MaterialLibrary.Domain.Database.Lookup

// ---------------------------------------------------------------------------------------------------------------
// Command line
// ---------------------------------------------------------------------------------------------------------------

/// <summary>Arguments after the script name (fsi puts the script path first).</summary>
let args = fsi.CommandLineArgs |> Array.skip 1

/// <summary>Value that follows a named option, or None when the option is absent or has no value.</summary>
let option name = args |> Array.tryFindIndex ((=) name) |> Option.bind (fun i -> if i + 1 < args.Length then Some args[i + 1] else None)

/// <summary>True when a value-less switch is present.</summary>
let flag name = args |> Array.contains name

/// <summary>Invariant culture: numbers on the command line always use the decimal point.</summary>
let inv = CultureInfo.InvariantCulture

/// <summary>Parse a comma/semicolon separated list of temperatures in degC; use the fallback when the option is missing.</summary>
let temperatures (text: string option) (fallback: float list) =
    match text with
    | None -> fallback
    | Some t -> t.Split([| ','; ';' |], StringSplitOptions.RemoveEmptyEntries) |> Array.map (fun v -> Double.Parse(v.Trim(), inv)) |> Array.toList

// ---------------------------------------------------------------------------------------------------------------
// Database and sheet conditions
// ---------------------------------------------------------------------------------------------------------------

/// <summary>Database file; the default is the shipped reference.</summary>
let dbPath = option "--db" |> Option.defaultValue "database/data/materials_2025.db" |> Path.GetFullPath
if not (File.Exists dbPath) then failwithf "Database not found: %s" dbPath

/// <summary>Read-only connection: a spot check must never modify the database it inspects.</summary>
let db = Asme2025Data.openRead dbPath

/// <summary>Temperature columns of the sheet (room, up to 3 design, up to 3 operating), all in degC.</summary>
let conditions =
    { RoomTemperature = option "--room" |> Option.map (fun v -> Double.Parse(v, inv)) |> Option.defaultValue 20.
      DesignTemperatures = temperatures (option "--design") [ 100.; 200.; 300. ]
      OperatingTemperatures = temperatures (option "--operating") [ 50.; 100.; 150. ] }

// ---------------------------------------------------------------------------------------------------------------
// Choose the materials to inspect
// ---------------------------------------------------------------------------------------------------------------

/// <summary>Every identity of the database (four-field key plus composition).</summary>
let all = Asme2025Data.materials db

/// <summary>Case-insensitive equality; an absent filter (None) accepts every value.</summary>
let same (expected: string option) (actual: string) = expected |> Option.forall (fun e -> String.Equals(e, actual, StringComparison.OrdinalIgnoreCase))

/// <summary>The materials to inspect: explicit ids, an identity filter, or a reproducible random sample.</summary>
let chosen : Asme2025Material list =
    match option "--id", option "--spec", option "--random" with
    | Some ids, _, _ ->
        // Explicit ids win over every other selector.
        let wanted = ids.Split(',') |> Array.map (fun v -> Int64.Parse(v.Trim())) |> Set.ofArray
        all |> List.filter (fun m -> wanted.Contains m.Id)
    | None, Some spec, _ ->
        // Identity filter: specification is required, the other fields narrow the match.
        let found = all |> List.filter (fun m -> same (Some spec) m.Specification && same (option "--grade") m.Grade && same (option "--condition") m.Condition && same (option "--uns") m.UNS)
        if found.Length > 5 then printfn "%d materials match: printing the first 5 (add --grade/--condition/--uns to narrow)." found.Length
        found |> List.truncate 5
    | None, None, Some n ->
        // Only materials with mechanical curves AND imported physical data make a meaningful spot check.
        let candidates =
            Asme2025Data.query db "SELECT DISTINCT m.id FROM Main m WHERE EXISTS (SELECT 1 FROM Material_Properties p WHERE p.id_main=m.id) AND EXISTS (SELECT 1 FROM Physical_Properties q WHERE q.id_main=m.id)" []
            |> List.map (fun r -> Convert.ToInt64 r[0]) |> Set.ofList
        let pool = all |> List.filter (fun m -> candidates.Contains m.Id) |> List.toArray
        // The seed makes a sample reproducible, so a finding can be rechecked later on the same materials.
        let rnd = Random(option "--seed" |> Option.map int |> Option.defaultValue 2025)
        let count = min (int n) pool.Length
        // Partial Fisher–Yates: the first `count` positions get a uniform random sample without repetition.
        // Loop ends after `count` swaps; each swap exchanges position i with a random position j >= i.
        for i in 0 .. count - 1 do
            let j = rnd.Next(i, pool.Length)
            let tmp = pool[i]
            pool[i] <- pool[j]
            pool[j] <- tmp
        pool |> Array.take count |> Array.toList
    | _ -> failwith "Choose materials with --id, --spec [--grade ...] or --random N."
if chosen.IsEmpty then failwith "No material matches."

// ---------------------------------------------------------------------------------------------------------------
// Checks
// ---------------------------------------------------------------------------------------------------------------

/// <summary>Text of a nullable database cell ("" for NULL).</summary>
let text (v: obj) = if isNull v || Convert.IsDBNull v then "" else string v

/// <summary>Normalise an identity string for comparison: trim, unify dash variants, upper case.</summary>
let norm (s: string) = (if isNull s then "" else s).Trim().Replace('–', '-').ToUpperInvariant()

/// <summary>
/// Check 1: compare every imported mechanical curve of a material with the ASME table row it came from.
/// Curves created by hand (Fingerprint = 'User') have no printed row and are skipped.
/// Prints the number of curves compared and, for each inconsistent curve, the columns that differ.
/// </summary>
let sourceRowCheck (material: Asme2025Material) =
    let ids =
        Asme2025Data.query db "SELECT p.id FROM Material_Properties p JOIN Property_Source s ON s.id_property=p.id WHERE p.id_main=$m AND s.Fingerprint<>'User' ORDER BY p.id" [ "$m", box material.Id ]
        |> List.map (fun r -> Convert.ToInt64 r[0])
    let problems =
        ids |> List.choose (fun id ->
            match SourceRowComparison.compare db id with
            | Ok view when view.IsConsistent -> None   // every column equals the printed value (or both are empty)
            | Ok view -> Some(id, view.Cells |> List.filter (fun c -> c.Status <> SourceCellStatus.Match && c.Status <> SourceCellStatus.Empty))
            | Error _ -> Some(id, []))                  // no readable source row: reported without cell details
    printfn "  Curve ASME confrontate con la riga stampata: %d; incoerenti: %d" ids.Length problems.Length
    for (id, cells) in problems do
        printfn "    curva #%d:" id
        for c in cells do
            printfn "      T=%g °C  fonte=%s  db=%s  %A" c.Temperature c.SourceText (if c.DatabaseValue.HasValue then string c.DatabaseValue.Value else "—") c.Status

/// <summary>
/// Check 2: list the XML records behind the physical data of a material (E, Alpha, k, a, density, Nu...)
/// and flag the cases where the record's UNS / composition does not match the material, or the record carries no identity key.
/// The association is made by UNS or exact nominal composition, so this is the evidence to eyeball.
/// </summary>
let xmlCheck (material: Asme2025Material) =
    let rows =
        Asme2025Data.query db "SELECT a.PropertyCode,a.RecordKey,a.TargetId,a.RecordJson,d.File FROM Xml_Property_Associations a JOIN Xml_Import_Documents d ON d.SHA256=a.DocumentSHA256 WHERE a.id_main=$m ORDER BY a.PropertyCode" [ "$m", box material.Id ]
    if rows.IsEmpty then printfn "  Dati fisici da XML: nessuno"
    else
        printfn "  Dati fisici da XML: %d associazioni (materiale: UNS=%s, composizione=%s)" rows.Length (if material.UNS = "" then "—" else material.UNS) (if material.NominalComposition = "" then "—" else material.NominalComposition)
        for r in rows do
            // RecordJson is the compact stored record: identity fields and notes, no points.
            use json = JsonDocument.Parse(text r[3])
            let get (name: string) = match json.RootElement.TryGetProperty name with | true, v -> v.GetString() | _ -> ""
            let uns, composition = get "UNS", get "Composition"
            let unsMatch = uns <> "" && norm uns = norm material.UNS
            let compositionMatch = composition <> "" && norm composition = norm material.NominalComposition
            // Verdict in decreasing strength: same UNS, same composition, no key at all, different key.
            let verdict =
                if unsMatch then "UNS uguale"
                elif compositionMatch then "composizione uguale"
                elif uns = "" && composition = "" then "nessuna chiave nel record: VERIFICARE"
                else "DIFFERENTE: VERIFICARE"
            // Short label: XML file name plus the record id (the part of the key after the last '/').
            let key = text r[1]
            let label = Path.GetFileName(text r[4]) + " / " + key.Substring(key.LastIndexOf('/') + 1)
            printfn "    %-18s %-30s record: UNS=%-8s comp=%-22s  → %s" (text r[0]) label (if uns = "" then "—" else uns) (if composition = "" then "—" else composition) verdict

// ---------------------------------------------------------------------------------------------------------------
// Run
// ---------------------------------------------------------------------------------------------------------------

/// <summary>Output folder for per-material Markdown files; None prints the sheets to the console.</summary>
let outFolder = option "--out"
outFolder |> Option.iter (fun f -> Directory.CreateDirectory f |> ignore)

/// <summary>File-name-safe text: keeps letters, digits, dash and underscore.</summary>
let safe (s: string) = String(s |> Seq.map (fun c -> if Char.IsLetterOrDigit c || c = '-' || c = '_' then c else '_') |> Seq.toArray)

for material in chosen do
    let title = [ material.Specification; material.Grade; material.Condition; material.UNS ] |> List.filter ((<>) "") |> String.concat " · "
    printfn "%s" (String('=', 100))
    printfn "MATERIALE id=%d  %s" material.Id title
    printfn "%s" (String('=', 100))
    // The sheet itself: all size ranges, room/design/operating columns, limits, notes.
    match ProductSheet.build db material.Id conditions with
    | Error e -> printfn "  Scheda non generata: %s" e
    | Ok markdown ->
        match outFolder with
        | Some folder ->
            let file = Path.Combine(folder, sprintf "scheda-%d-%s.md" material.Id (safe (String.Join("_", [ material.Specification; material.Grade ] |> List.filter ((<>) "")))))
            File.WriteAllText(file, markdown)
            printfn "  Scheda scritta: %s" file
        | None -> if not (flag "--no-sheet") then printfn "%s" markdown
    // The evidence for the spot check.
    printfn "CONTROLLI"
    sourceRowCheck material
    xmlCheck material
    printfn ""
printfn "Controllati %d materiali (database: %s)." chosen.Length dbPath
