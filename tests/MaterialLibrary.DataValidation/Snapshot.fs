/// Snapshot of the values of every property table of a material at its Tmin, its Tmax and at 100,
/// 200, ... 600 degC. A snapshot taken after the values were verified against the Code is the
/// baseline: every later change of the database shows up as a difference to review.
module MaterialLibrary.DataValidation.Snapshot

open System
open System.Globalization
open System.IO
open System.Text
open MaterialReportCore

let private inv = CultureInfo.InvariantCulture

type Point =
    { MaterialId: int
      Material: string
      Table: string
      /// "Tmin", "Tmax", "100" ... "600" or "value" for scalars.
      Point: string
      Temperature: float option
      Value: float option
      /// True when the temperature is above the Tmax of the handbook row.
      BeyondTmax: bool
      /// Where the value comes from: the Code text (physical properties) or the database.
      Source: string }

/// Temperatures (degC) sampled in addition to Tmin and Tmax.
let sampleTemperatures = [ 100.0; 200.0; 300.0; 400.0; 500.0; 600.0 ]

/// Every property table of the material as a named curve.
let tablesOf (d: MaterialData) : (string * Curve) list =
    // Two lines of the same table and thickness range (for example seamless and welded product)
    // get an ordinal so that every table of the snapshot has a unique name.
    let banded (prefix: string) (bands: Band list) =
        let names =
            bands
            |> List.map (fun b ->
                let size =
                    match b.Size.Min, b.Size.Max with
                    | None, None -> "all thicknesses"
                    | lo, hi -> $"thk {defaultArg lo nan}..{defaultArg hi nan}"

                $"{prefix}[{b.Source}, {caseText b.Case}, {size}]")

        List.zip names bands
        |> List.mapi (fun i (name, b) ->
            let sameName = names |> List.filter ((=) name) |> List.length

            if sameName = 1 then
                name, b.Curve
            else
                let ordinal = (names |> List.take (i + 1) |> List.filter ((=) name) |> List.length)
                $"{name} #{ordinal}", b.Curve)

    banded "Sy" d.Sy
    @ banded "Su" d.Su
    @ banded "Div1 allowable" d.Div1
    @ banded "Div2 allowable" d.Div2
    @ [ "E (GPa)", d.Elastic
        "Alpha instantaneous (1e-6/degC)", d.AlphaInstantaneous
        "Alpha mean (1e-6/degC)", d.AlphaMean
        "Specific heat (J/kg/K)", d.SpecificHeat
        "Conductivity (W/m/K)", d.Conductivity
        "Diffusivity (mm2/s)", d.Diffusivity ]

/// The sampled points of one curve: Tmin, Tmax and the grid temperatures that the curve lists;
/// a sample temperature inside the curve range that the table does not list is recorded empty.
let private pointsOf (d: MaterialData) (name: string) (curve: Curve) (source: string) (tmax: float option) : Point list =
    let make point (temperature: float option) (value: float option) =
        { MaterialId = d.Id
          Material = $"{d.Specification} {d.Grade} {d.ClassCondition} {d.Uns}".Replace("  ", " ").Trim()
          Table = name
          Point = point
          Temperature = temperature
          Value = value
          BeyondTmax =
            (match temperature, tmax with
             | Some t, Some limit -> t > limit
             | _ -> false)
          Source = source }

    match curve with
    | [] -> [ make "Tmin" None None; make "Tmax" None None ]
    | (t0, v0) :: _ ->
        let tN, vN = List.last curve

        [ yield make "Tmin" (Some t0) (Some v0)
          yield make "Tmax" (Some tN) (Some vN)

          for t in sampleTemperatures do
              if t >= t0 && t <= tN then
                  yield make (t.ToString("0", inv)) (Some t) (valueAt t curve) ]

/// Full snapshot of a material; <c>tmax</c> is the handbook Tmax used to flag the points beyond it.
/// With <c>reference</c> the physical properties are the ones of the Code text instead of the
/// database values: that is the snapshot a baseline is made of.
let take (d: MaterialData) (tmax: float option) (reference: PhysicalReference.PhysicalProperties option) : Point list =
    let label = $"{d.Specification} {d.Grade} {d.ClassCondition} {d.Uns}".Replace("  ", " ").Trim()

    let scalar name (value: float option) (source: string) =
        { MaterialId = d.Id
          Material = label
          Table = name
          Point = "value"
          Temperature = None
          Value = value
          BeyondTmax = false
          Source = source }

    let scalarOf (database: float option) (pick: PhysicalReference.PhysicalProperties -> PhysicalReference.Reference) =
        match reference with
        | Some r ->
            let x = pick r
            (x.Curve |> List.tryHead |> Option.map snd), x.Source
        | None -> database, "Database"

    let density, densitySource = scalarOf d.Density (fun r -> r.Density)
    let poisson, poissonSource = scalarOf d.Poisson (fun r -> r.Poisson)

    let physical =
        match reference with
        | Some r ->
            [ "E (GPa)", (r.Elastic.Curve, r.Elastic.Source)
              "Alpha instantaneous (1e-6/degC)", (r.AlphaInstantaneous.Curve, r.AlphaInstantaneous.Source)
              "Alpha mean (1e-6/degC)", (r.AlphaMean.Curve, r.AlphaMean.Source)
              "Specific heat (J/kg/K)", (r.SpecificHeat.Curve, r.SpecificHeat.Source)
              "Conductivity (W/m/K)", (r.Conductivity.Curve, r.Conductivity.Source)
              "Diffusivity (mm2/s)", (r.Diffusivity.Curve, r.Diffusivity.Source) ]
            |> dict
        | None -> dict []

    [ yield scalar "SMYS (MPa)" d.Smys "Database"
      yield scalar "SMTS (MPa)" d.Smts "Database"
      yield scalar "Density (kg/m3)" density densitySource
      yield scalar "Poisson ratio" poisson poissonSource

      for name, curve in tablesOf d do
          match physical.TryGetValue name with
          | true, (referenceCurve, source) -> yield! pointsOf d name referenceCurve source tmax
          | _ -> yield! pointsOf d name curve "Database" tmax ]

let private fmt (v: float option) =
    match v with
    | Some x -> x.ToString("0.######", inv)
    | None -> ""

let header = "MaterialId;Material;Table;Point;Temperature;Value;BeyondTmax;Source"

let toLine (p: Point) : string =
    String.Join(";", [ string p.MaterialId; p.Material; p.Table; p.Point; fmt p.Temperature; fmt p.Value; (if p.BeyondTmax then "Y" else ""); p.Source ])

let write (path: string) (points: Point list) =
    Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
    File.WriteAllLines(path, header :: (points |> List.map toLine), UTF8Encoding(true))

let private parseOpt (s: string) =
    if s = "" then None else Some(Double.Parse(s, inv))

let read (path: string) : Point list =
    File.ReadAllLines(path, Encoding.UTF8)
    |> Array.skip 1
    |> Array.filter (fun l -> l.Trim() <> "")
    |> Array.map (fun l ->
        let c = l.Split(';')

        { MaterialId = int c.[0]
          Material = c.[1]
          Table = c.[2]
          Point = c.[3]
          Temperature = parseOpt c.[4]
          Value = parseOpt c.[5]
          BeyondTmax = (c.[6] = "Y")
          Source = (if c.Length > 7 then c.[7] else "") })
    |> List.ofArray

let private key (p: Point) = p.MaterialId, p.Table, p.Point

/// Tolerance of a table: the mean expansion is derived by integration in the database and printed
/// with one decimal in the Code; the specific heat is a formula; every other value is exact.
let private sameValue (table: string) (a: float option) (b: float option) =
    match a, b with
    | None, None -> true
    | Some x, Some y ->
        if table.StartsWith("Alpha mean", StringComparison.Ordinal) then abs (x - y) <= 0.1
        elif table.StartsWith("Specific heat", StringComparison.Ordinal) then abs (x - y) <= 1e-3 * max 1.0 (abs y)
        else abs (x - y) < 1e-6
    | _ -> false

let private sameTemperature (a: float option) (b: float option) =
    match a, b with
    | None, None -> true
    | Some x, Some y -> abs (x - y) < 1e-6
    | _ -> false

/// Differences between a baseline and the current snapshot, one readable line each.
let differences (baseline: Point list) (current: Point list) : string list =
    let now = current |> List.map (fun p -> key p, p) |> dict
    let before = baseline |> List.map (fun p -> key p, p) |> dict

    [ for p in baseline do
          match now.TryGetValue(key p) with
          | true, c when sameValue p.Table c.Value p.Value && sameTemperature c.Temperature p.Temperature -> ()
          | true, c ->
              yield $"changed  {p.Material} | {p.Table} | {p.Point}: baseline {fmt p.Temperature} degC = {fmt p.Value}, now {fmt c.Temperature} degC = {fmt c.Value}"
          | _ -> yield $"missing  {p.Material} | {p.Table} | {p.Point}: baseline {fmt p.Value}, no longer in the database"

      for p in current do
          if not (before.ContainsKey(key p)) then
              yield $"new      {p.Material} | {p.Table} | {p.Point}: {fmt p.Temperature} degC = {fmt p.Value}" ]
