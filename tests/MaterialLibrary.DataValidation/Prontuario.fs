/// The "Prontuario" list: the materials to check after every change of the database, with the
/// reference values of the technical handbook (Prontuario Tecnico Materiali Caldareria): minimum
/// strengths, elongation and the temperature range. The list lives in Data/prontuario-materials.csv;
/// add a row there to put one more material under the same checks.
module MaterialLibrary.DataValidation.Prontuario

open System
open System.Globalization
open System.IO
open System.Text
open MaterialReportCore

let private inv = CultureInfo.InvariantCulture

/// One row of the handbook with the rules that select the matching database materials.
type Row =
    { Description: string
      Specs: string list
      /// Accepted TypeGrade spellings; empty = any grade.
      Grades: string list
      /// Accepted ClassConditionTemper spellings; empty = any.
      Classes: string list
      Uns: string list
      /// When true the UNS number selects the material (grades that the database leaves empty).
      UnsFilter: bool
      Smys: float
      Smts: float
      ElongationPct: float option
      TminC: float
      TmaxC: float
      TmaxNote: string }

let private splitList (text: string) =
    text.Split('|') |> Array.map (fun s -> s.Trim()) |> Array.filter (fun s -> s <> "") |> List.ofArray

let private number (text: string) = Double.Parse(text.Trim().Replace(',', '.'), inv)

/// Reads a ';' separated file with an optional BOM and a header row.
let private readRows (path: string) : (string * string) list list =
    let lines =
        File.ReadAllLines(path, Encoding.UTF8)
        |> Array.filter (fun l -> l.Trim() <> "")
        |> List.ofArray

    match lines with
    | [] -> []
    | header :: rows ->
        let names = header.TrimStart('﻿').Split(';') |> Array.map (fun h -> h.Trim())

        rows
        |> List.map (fun r ->
            let cells = r.Split(';')
            [ for i in 0 .. names.Length - 1 -> names.[i], (if i < cells.Length then cells.[i].Trim() else "") ])

/// The handbook rows, from Data/prontuario-materials.csv next to the test binaries.
let rows: Lazy<Row list> =
    lazy
        (let path = Path.Combine(AppContext.BaseDirectory, "Data", "prontuario-materials.csv")

         readRows path
         |> List.map (fun cells ->
             let get key = cells |> List.find (fun (k, _) -> k = key) |> snd

             { Description = get "Description"
               Specs = splitList (get "Specs")
               Grades = splitList (get "Grades")
               Classes = splitList (get "Classes")
               Uns = splitList (get "Uns")
               UnsFilter = (get "UnsFilter" = "Y")
               Smys = number (get "Smys")
               Smts = number (get "Smts")
               ElongationPct = (match get "ElongationPct" with "" -> None | s -> Some(number s))
               TminC = number (get "TminC")
               TmaxC = number (get "TmaxC")
               TmaxNote = get "TmaxNote" }))

let sameText (a: string) (b: string) =
    String.Equals(a, b, StringComparison.OrdinalIgnoreCase)

/// Does the database material belong to the handbook row?
let matches (row: Row) (d: MaterialData) : bool =
    row.Specs |> List.exists (sameText d.Specification)
    && (row.Grades.IsEmpty || row.Grades |> List.exists (sameText d.Grade))
    && (row.Classes.IsEmpty || row.Classes |> List.exists (sameText d.ClassCondition))
    && (not row.UnsFilter || row.Uns |> List.exists (sameText d.Uns))

/// Short label of a row for test names.
let label (row: Row) : string =
    let part (items: string list) = if items.IsEmpty then "" else " " + String.Join("/", items)
    let specs = String.Join("/", row.Specs)
    $"{row.Description} [{specs}{part row.Grades}{part row.Classes}]"

// ───────────────────────────── checks against the handbook row ─────────────────────────────

open MaterialLibrary.DataValidation.Checks

let private lastTemperature (curve: Curve) =
    match List.tryLast curve with
    | Some(t, _) -> Some t
    | None -> None

/// Largest application temperature of the allowable-stress tables (Division 1 and Division 2).
let applicationTmax (d: MaterialData) : float option =
    (d.Div1 @ d.Div2) |> List.choose (fun b -> b.MaxTemp) |> function
    | [] -> None
    | temps -> Some(List.max temps)

/// Temperatures above the handbook Tmax must still be complete: no gap in the temperature grid,
/// no zero or negative value, and no increase with temperature for E and Sy.
let private beyondTmaxProblems (tmax: float) (name: string) (curve: Curve) : string list =
    let anchor = curve |> List.filter (fun (t, _) -> t <= tmax) |> List.tryLast
    let above = curve |> List.filter (fun (t, _) -> t > tmax)
    let sequence = (Option.toList anchor) @ above

    [ for (t1, _), (t2, _) in List.pairwise sequence do
          if t2 - t1 > 50.0 then
              yield $"{name}: gap between {fmt t1} and {fmt t2} degC"

      for t, v in above do
          if v <= 0.0 then
              yield $"{name}: non-positive value {fmt v} at {fmt t} degC"

      if name.StartsWith("E ", StringComparison.Ordinal) || name.StartsWith("Sy", StringComparison.Ordinal) then
          for (t1, v1), (t2, v2) in List.pairwise sequence do
              if v2 > v1 * 1.005 then
                  yield $"{name}: increases from {fmt v1} at {fmt t1} degC to {fmt v2} at {fmt t2} degC" ]

/// Checks of one database material against its handbook row.
let checksFor (row: Row) (d: MaterialData) : Check list =
    let shown (v: float option) = v |> Option.map fmt |> Option.defaultValue "-"
    let tables = Snapshot.tablesOf d |> List.filter (fun (_, c) -> not c.IsEmpty)

    let tooShort =
        [ for name, curve in tables do
              match lastTemperature curve with
              | Some t when t + 50.0 < row.TmaxC -> yield $"{name} ends at {fmt t} degC"
              | _ -> () ]

    let beyond = [ for name, curve in tables do yield! beyondTmaxProblems row.TmaxC name curve ]

    let dbTmax = applicationTmax d
    let eFirst = d.Elastic |> List.tryHead |> Option.map fst

    [ check "SMYS equals the handbook" (d.Smys = Some row.Smys) $"database {shown d.Smys}, handbook {fmt row.Smys}"
      check "SMTS equals the handbook" (d.Smts = Some row.Smts) $"database {shown d.Smts}, handbook {fmt row.Smts}"

      (let unsList = String.Join("/", row.Uns)

       if row.Uns.IsEmpty then
           check "UNS" true ""
       else
           check "UNS equals the handbook" (row.Uns |> List.exists (sameText d.Uns)) $"database '{d.Uns}', handbook {unsList}")

      (match row.ElongationPct, d.ElongationLong |> Option.orElse d.ElongationTransverse with
       | _, None -> infoIf "Rupture elongation" true $"empty in the database (handbook {shown row.ElongationPct} %%)"
       | Some expected, Some actual ->
           check "Rupture elongation equals the handbook" (abs (actual - expected) <= 0.5) $"database {fmt actual} %%, handbook {fmt expected} %%"
       | None, Some _ -> check "Rupture elongation" true "")

      (match dbTmax with
       | None -> infoIf "Application Tmax" true "no allowable stress table with a maximum temperature"
       | Some t ->
           check "Allowable-stress Tmax covers the handbook Tmax" (t >= row.TmaxC) $"database Tmax {fmt t} degC, handbook {fmt row.TmaxC} degC")

      check "Every property table reaches the handbook Tmax" tooShort.IsEmpty (String.Join("; ", tooShort |> List.truncate 4))
      check "Values above the handbook Tmax are complete" beyond.IsEmpty (String.Join("; ", beyond |> List.truncate 4))

      (if row.TminC < 20.0 then
           check "Elastic modulus covers the handbook Tmin" (eFirst |> Option.exists (fun t -> t <= row.TminC)) $"E starts at {shown eFirst} degC, handbook Tmin {fmt row.TminC} degC"
       else
           check "Tmin" true "")

      infoIf
          "Tmin below 20 degC"
          (row.TminC < 20.0)
          $"expansion, specific heat, conductivity and diffusivity are tabulated from 20 degC; handbook Tmin {fmt row.TminC} degC" ]
