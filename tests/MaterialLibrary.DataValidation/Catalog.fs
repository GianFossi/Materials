/// The materials under validation. To validate more materials (for example the ones used most
/// often during the year) add entries to <c>frequentlyUsed</c> or create a new list and append it
/// to <c>all</c>: every entry is picked up by all the data-validation tests automatically.
module MaterialLibrary.DataValidation.Catalog

open System
open MaterialReportCore

/// A request: a label for reports and a predicate that selects the matching database materials
/// (an entry may match several rows: product forms, classes, conditions).
type Entry =
    { Label: string
      Matches: MaterialData -> bool }

let private sameText (a: string) (b: string) =
    String.Equals(a, b, StringComparison.OrdinalIgnoreCase)

/// A specification and the accepted TypeGrade spellings; an empty grade list selects every grade.
let specGrade (spec: string) (grades: string list) : Entry =
    let label =
        match grades with
        | [] -> $"{spec} (all grades)"
        | g :: _ -> $"{spec} Gr {g}"

    { Label = label
      Matches =
        fun d ->
            d.Specification = spec
            && (grades.IsEmpty || grades |> List.exists (sameText d.Grade)) }

/// Every product form and specification of an alloy designation (UNS).
let uns (code: string) : Entry =
    { Label = $"UNS {code} (all products)"
      Matches = fun d -> d.Uns = code }

/// Every product form and grade whose nominal composition satisfies the predicate.
let composition (label: string) (predicate: string -> bool) : Entry =
    { Label = label
      Matches = fun d -> predicate d.Composition }

/// Carbon and low-alloy steels, pressure vessel and piping grades.
let carbonAndLowAlloy: Entry list =
    [ specGrade "SA-516" [ "60" ]
      specGrade "SA-516" [ "70" ]
      specGrade "SA-266" [ "2" ]
      specGrade "SA-266" [ "4" ]
      specGrade "SA-105" []
      specGrade "SA-765" [ "II" ]
      specGrade "SA-765" [ "IV" ]
      specGrade "SA-179" []
      specGrade "SA-210" []
      specGrade "SA-387" [ "1" ]
      specGrade "SA-387" [ "2" ]
      specGrade "SA-387" [ "11" ]
      specGrade "SA-387" [ "12" ]
      specGrade "SA-387" [ "22" ]
      specGrade "SA-387" [ "22V" ]
      specGrade "SA-336" [ "F1" ]
      specGrade "SA-336" [ "F2" ]
      specGrade "SA-336" [ "F11" ]
      specGrade "SA-336" [ "F12" ]
      specGrade "SA-336" [ "F22" ]
      specGrade "SA-336" [ "F22V" ]
      specGrade "SA-213" [ "TP11"; "T11" ]
      specGrade "SA-213" [ "TP22"; "T22" ]
      specGrade "SA-204" [ "A" ]
      specGrade "SA-204" [ "B" ]
      specGrade "SA-106" [ "B" ]
      specGrade "SA-106" [ "C" ]
      specGrade "SA-333" [ "6" ] ]

/// Low-temperature and nickel steels.
let lowTemperature: Entry list =
    [ // The database has one SA-350 LF2 row without a class: Class 1 and Class 2 are not split.
      specGrade "SA-350" [ "LF2" ]
      specGrade "SA-350" [ "LF3" ]
      specGrade "SA-203" []
      specGrade "SA-553" []
      specGrade "SA-353" [] ]

/// Austenitic stainless steels: plate, forgings and tubes.
let stainless: Entry list =
    [ for g in [ "304"; "304L"; "304H"; "316"; "316L"; "316H"; "317"; "317L"; "321"; "347"; "347H" ] -> specGrade "SA-240" [ g ]
      for g in [ "F304"; "F304L"; "F304H"; "F316"; "F316L"; "F316H"; "F317"; "F321"; "F347"; "F347H" ] -> specGrade "SA-965" [ g ]
      for g in [ "TP304"; "TP316"; "TP317"; "TP321"; "TP347"; "TP304L"; "TP316L"; "TP317L"; "TP321L"; "TP347L" ] -> specGrade "SA-213" [ g ] ]

/// Nickel alloys and Cr-Mo steels selected by UNS or composition across all products.
let alloys: Entry list =
    [ uns "N06625"
      uns "N08800"
      uns "N08810"
      uns "N08811"
      composition "5Cr (all products and grades)" (fun c -> c.StartsWith("5Cr-", StringComparison.Ordinal))
      composition "9Cr-1Mo (all products and grades)" (fun c -> c = "9Cr-1Mo")
      composition "9Cr-1Mo-V (all products and grades)" (fun c -> c = "9Cr-1Mo-V") ]

/// The materials of the technical handbook (Data/prontuario-materials.csv): the list to check after
/// every change of the database. Add rows to the csv to extend it.
let prontuario: Entry list =
    Prontuario.rows.Value
    |> List.map (fun row ->
        { Label = $"Prontuario: {Prontuario.label row}"
          Matches = Prontuario.matches row })

/// Add here other materials used often during the year.
let frequentlyUsed: Entry list = prontuario

let all: Entry list =
    carbonAndLowAlloy @ lowTemperature @ stainless @ alloys @ frequentlyUsed
