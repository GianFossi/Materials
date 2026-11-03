// Author: Claude
// Last revision: 2026-11-03
// Purpose: Command-line front end of MaterialLibrary.Crud.ProductSheet: write the Markdown product data sheet of one material.
//
// Options:
//   --db <file>            native 2025 database (reference or working copy), opened read-only
//   --material <id>        Main.id of the material, or
//   --spec <text> [--grade <text>] [--condition <text>] [--uns <text>]   exact identity fields (case-insensitive)
//   --room <degC>          room temperature column (default 20)
//   --design <a,b,c>       up to three design temperatures, degC
//   --operating <a,b,c>    up to three operating temperatures, degC
//   --out <file>           output Markdown (default: standard output)
// Run it through tools/Generate-ProductSheet.ps1, which builds the Crud project into tools/ProductSheet/bin first.
// Units: temperatures degC, stresses MPa, sizes mm.

#r "bin/MaterialLibrary.dll"
#r "bin/MaterialLibrary.Crud.dll"
#r "nuget: Microsoft.Data.Sqlite, 10.0.13"

open System
open System.Globalization
open System.IO
open MaterialLibrary.Crud
open MaterialLibrary.Domain.Database.Lookup

let args = fsi.CommandLineArgs |> Array.skip 1
let option name = args |> Array.tryFindIndex ((=) name) |> Option.bind (fun i -> if i + 1 < args.Length then Some args[i + 1] else None)
let required name = option name |> Option.defaultWith (fun () -> failwithf "Missing option %s" name)

/// <summary>Parse a comma separated list of temperatures (invariant culture).</summary>
let temperatures (value: string option) =
    match value with
    | None -> []
    | Some text ->
        text.Split([| ','; ';' |], StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun t -> Double.Parse(t.Trim(), CultureInfo.InvariantCulture))
        |> Array.toList

let db = Asme2025Data.openRead (Path.GetFullPath(required "--db"))
let same (expected: string option) (actual: string) = expected |> Option.forall (fun e -> String.Equals(e, actual, StringComparison.OrdinalIgnoreCase))
let materialId =
    match option "--material" with
    | Some id -> Int64.Parse id
    | None ->
        let spec = Some(required "--spec")
        let matches = Asme2025Data.materials db |> List.filter (fun m -> same spec m.Specification && same (option "--grade") m.Grade && same (option "--condition") m.Condition && same (option "--uns") m.UNS)
        match matches with
        | [ m ] -> m.Id
        | [] -> failwith "No material matches the given identity."
        | many -> failwithf "%d materials match: add --grade/--condition/--uns or use --material <id>." many.Length

let conditions =
    { RoomTemperature = option "--room" |> Option.map (fun v -> Double.Parse(v, CultureInfo.InvariantCulture)) |> Option.defaultValue 20.
      DesignTemperatures = temperatures (option "--design")
      OperatingTemperatures = temperatures (option "--operating") }

match ProductSheet.build db materialId conditions with
| Ok markdown ->
    match option "--out" with
    | Some path -> File.WriteAllText(path, markdown); printfn "Sheet written: %s" path
    | None -> printf "%s" markdown
| Error message -> eprintfn "%s" message; exit 1
