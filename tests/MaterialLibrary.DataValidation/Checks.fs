/// Data checks on one material of ASME_Materials.db, grouped by the kind of data they validate.
/// Pure functions over <c>MaterialData</c>: the tests decide how to report the outcomes.
module MaterialLibrary.DataValidation.Checks

open System
open System.Globalization
open MaterialReportCore

type Outcome =
    | Pass
    /// Known limitation of the database, identical for every material: never fails a test.
    | Info
    | Fail

type Check =
    { Name: string
      Outcome: Outcome
      Detail: string }

let fmt (v: float) = v.ToString("0.###", CultureInfo.InvariantCulture)

let private result name outcomeIfBad ok detail =
    { Name = name
      Outcome = (if ok then Pass else outcomeIfBad)
      Detail = (if ok then "" else detail) }

/// A requirement: Fail when not satisfied.
let check name ok detail = result name Fail ok detail

/// A situation that is legitimate for some materials or a known database limitation.
let infoIf name condition detail =
    result name Info (not condition) detail

let isNonIncreasing (curve: Curve) =
    curve |> List.pairwise |> List.forall (fun ((_, a), (_, b)) -> b <= a + 1e-9)

let inRange lo hi v = v >= lo && v <= hi

/// Bands that cover the same thickness range as <c>size</c>; falls back to the only band when unique.
let matchingBand (size: Size) (bands: Band list) =
    match bands |> List.tryFind (fun b -> b.Size = size) with
    | Some b -> Some b
    | None ->
        match bands with
        | [ only ] -> Some only
        | _ -> None

/// Tolerance on the Code criteria: tabulated values are rounded and the limits use temperature ratios.
let tolerance = 1.03

let private join (items: string list) = String.Join("; ", items |> List.truncate 3)

/// SMYS/SMTS, Sy and Su tables.
let strengthChecks (d: MaterialData) : Check list =
    let syVsSu =
        [ for sy in d.Sy do
              match matchingBand sy.Size d.Su with
              | Some su ->
                  for t, s in sy.Curve do
                      match valueAt t su.Curve with
                      | Some u when s > u * 1.001 -> yield $"{fmt t} degC: Sy {fmt s} > Su {fmt u}"
                      | _ -> ()
              | None -> () ]

    [ check
          "SMYS and SMTS present with SMYS < SMTS"
          (match d.Smys, d.Smts with
           | Some y, Some u -> y < u
           | _ -> false)
          $"SMYS = {d.Smys}, SMTS = {d.Smts}"
      check "Sy table present" (not d.Sy.IsEmpty) "no yield strength rows"
      check "Su table present" (not d.Su.IsEmpty) "no ultimate strength rows"
      check "Sy <= Su at every temperature" syVsSu.IsEmpty (join syVsSu) ]

/// ASME IX P-number / G-number.
let weldingChecks (d: MaterialData) : Check list =
    [ check "ASME IX P/G number present" (not d.Welding.IsEmpty) "no DataTableASME row" ]

/// Allowable-stress tables presence (Division 1 and Division 2).
let allowableChecks (d: MaterialData) : Check list =
    [ if d.Div1NotPermitted then
          yield infoIf "Division 1 allowable" true "tables 1A/1B list the material without a VIII-1 maximum temperature: not permitted in VIII-1"
      else
          yield check "Division 1 allowable present" (not d.Div1.IsEmpty) "no rows in tables 1A/1B/3"

      yield
          check
              "Division 1 rows carry Tmax and values"
              (d.Div1 |> List.forall (fun b -> b.MaxTemp.IsSome && not b.Curve.IsEmpty))
              "a Division 1 row has no Tmax or no values"

      if d.Div2.IsEmpty then
          yield infoIf "Division 2 allowable" true "no rows in tables 5A/5B" ]

/// Physical property tables: E, density, Poisson, expansion, Cp, conductivity, diffusivity.
let physicalChecks (d: MaterialData) : Check list =
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

    [ check "Elastic modulus present" (not d.Elastic.IsEmpty) "no elastic modulus group"
      check "Elastic modulus decreases with temperature" (isNonIncreasing d.Elastic) "E is not monotonic"
      check "E(25 degC) between 150 and 230 GPa" (eAt25 |> Option.exists (inRange 150.0 230.0)) $"E(25) = {eAt25}"
      check "Density between 7000 and 9000 kg/m3" (d.Density |> Option.exists (inRange 7000.0 9000.0)) $"density = {d.Density}"
      check "Poisson ratio between 0.25 and 0.35" (d.Poisson |> Option.exists (inRange 0.25 0.35)) $"nu = {d.Poisson}"
      check "Instantaneous thermal expansion present" (not d.AlphaInstantaneous.IsEmpty) "no thermal expansion group"

      check
          "Thermal expansion between 8 and 25 e-6/degC"
          (d.AlphaInstantaneous |> List.forall (fun (_, a) -> inRange 8.0 25.0 a))
          "values out of range"

      check
          "Derived mean expansion starts at the instantaneous value (mean(20) = inst(20))"
          (match alphaAt20, meanAt20 with
           | Some a, Some m -> abs (a - m) < 1e-9
           | _ -> false)
          "mean(20) differs from instantaneous(20)"

      check "Specific heat present" (not d.SpecificHeat.IsEmpty) "no specific heat row"
      check "Thermal conductivity present" (not d.Conductivity.IsEmpty) "no conductivity group"
      check "Thermal diffusivity present" (not d.Diffusivity.IsEmpty) "no diffusivity group"

      check
          "Cp = lambda / (rho * a) at the first common temperature (within 2%)"
          (match thermalTriple, d.Density with
           | Some(_, cp, l, a), Some rho -> abs (cp - l / (rho * a * 1e-6)) / cp < 0.02
           | _ -> false)
          "Cp, conductivity, diffusivity and density are not consistent" ]

/// Inputs for the monotonic stress-strain curve (E, Sy, Su, nu).
let curveChecks (d: MaterialData) : Check list =
    let missing = stressStrainMissing d
    [ check "Stress-strain curve inputs complete" missing.IsEmpty ("missing " + String.Join(", ", missing)) ]

/// Allowable stresses against the Code criteria, with <see cref="tolerance"/>.
///   Division 1: S &lt;= SMTS/3.5; normal line S &lt;= 2/3 Sy(T); higher line (G5) S &lt;= 0.9 Sy(T)
///   Division 2: S &lt;= SMTS/2.4; S &lt;= 0.9 Sy(T) (austenitic grades may exceed 2/3 Sy)
let criteriaChecks (d: MaterialData) : Check list =
    let violations (bands: Band list) (suDivisor: float) (normalSyFactor: float) =
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
                              let hint = if b.Case = Normal && normalSyFactor < 0.9 then " (note G5 missing in the database?)" else ""
                              yield $"{b.Source} {caseText b.Case} {fmt t} degC: S {fmt s} > {fmt factor} Sy = {fmt limit}{hint}"
                      | None -> ()
                  | None -> () ]

    [ check "Division 1 allowable within SMTS/3.5 and 2/3 Sy (0.9 Sy for G5), tol 3%" (violations d.Div1 3.5 (2.0 / 3.0)).IsEmpty (join (violations d.Div1 3.5 (2.0 / 3.0)))
      check "Division 2 allowable within SMTS/2.4 and 0.9 Sy, tol 3%" (violations d.Div2 2.4 0.9).IsEmpty (join (violations d.Div2 2.4 0.9)) ]

/// Data the database is known not to hold: reported, never failing.
let knownGaps (d: MaterialData) : Check list =
    [ infoIf "Rupture elongation" (d.ElongationLong.IsNone && d.ElongationTransverse.IsNone) "empty in the database"
      infoIf "External pressure chart" d.Charts.IsEmpty "no chart associated"
      infoIf "Cyclic curve data (Kcss/Ncss)" true "not stored in the database" ]

/// Every check of a material, used by the summary.
let allChecks (d: MaterialData) : (string * Check list) list =
    [ "Strength", strengthChecks d
      "Welding", weldingChecks d
      "Allowable", allowableChecks d
      "Physical", physicalChecks d
      "Curve inputs", curveChecks d
      "Code criteria", criteriaChecks d
      "Known gaps", knownGaps d ]
