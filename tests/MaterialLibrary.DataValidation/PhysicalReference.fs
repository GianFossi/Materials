/// The physical properties a material must have according to the Code (ASME BPVC II-D Subpart 2),
/// resolved from the staged Code text. The column of the Code a database curve was taken from is
/// found by value (exact match) or, when the database stored the values shifted along the
/// temperature axis, by the shifted match; the Code values are then the reference.
module MaterialLibrary.DataValidation.PhysicalReference

open System
open System.Text.RegularExpressions
open MaterialReportCore

/// A reference curve and where it comes from.
type Reference =
    { Curve: Curve
      /// "Code TE-4 'Coefficients for N06690 ...'" or "Database (not verified)" when the Code row
      /// could not be determined.
      Source: string
      /// True when the values come from the Code text, false when the database value was kept.
      FromCode: bool
      /// The Code column the values were read from.
      Column: CodeText.CodeColumn option }

type PhysicalProperties =
    { Elastic: Reference
      AlphaInstantaneous: Reference
      AlphaMean: Reference
      Conductivity: Reference
      Diffusivity: Reference
      SpecificHeat: Reference
      Density: Reference
      Poisson: Reference }

let private tolerance = 0.0005

let private shortHeading (column: CodeText.CodeColumn) = "Code " + CodeText.columnName column

/// The Code column of a quantity for a material:
///  1. when the Code lists the UNS number of the material, the column of that number;
///  2. otherwise the group the Code notes assign to the composition, found by the name of the group
///     in the table (Table TM-1 row, Table TE-1 column, Table TCD group);
///  3. none: the caller keeps the database value and marks it as not verified.
/// The link between a material and a group stored in the database is never used.
let columnFor (quantity: string) (reference: CodeText.CodeReference) (groups: CodeGroups.GroupLists) (d: MaterialData) (database: Curve) : CodeText.CodeColumn option =
    let byUns = if d.Uns = "" then [] else CodeText.columnsForUns reference quantity d.Uns

    let columnsOf (table: string) =
        reference.Columns |> List.filter (fun c -> c.Quantity = quantity && c.Table = table)

    let byLabel (table: string) (matches: string -> bool) =
        columnsOf table |> List.tryFind (fun c -> matches (CodeGroups.normalize c.Label))

    match byUns with
    | first :: _ ->
        // The same UNS can be listed in more than one place: prefer the column the database reproduces.
        let reproduces c =
            match CodeText.diagnoseAgainst tolerance c database with
            | CodeText.Exact _ -> true
            | _ -> false

        Some(byUns |> List.tryFind reproduces |> Option.defaultValue first)
    | [] ->
        let startsWith (prefix: string) (x: string) = x.StartsWith(CodeGroups.normalize prefix, StringComparison.Ordinal)
        let contains (part: string) (x: string) = x.Contains(CodeGroups.normalize part)

        match quantity with
        | "E" ->
            match CodeGroups.modulusGroup groups d.Composition with
            | Some "Carbon steels with C" -> byLabel "TM-1" (startsWith "Carbon steels with C ≤")
            | Some letter -> byLabel "TM-1" (startsWith $"Material Group {letter} ")
            | None -> None
        | "AlphaInstantaneous" ->
            match CodeGroups.expansionGroup groups d.Composition with
            | Some key -> byLabel "TE-1" (contains key)
            | None -> None
        | "Conductivity"
        | "Diffusivity" ->
            match CodeGroups.conductivityGroup groups d.Composition with
            | Some key -> byLabel "TCD" (fun label -> label = CodeGroups.normalize key)
            | None -> None
        | _ -> None

let private poissonDensityFor (reference: CodeText.CodeReference) (d: MaterialData) : CodeText.PoissonDensity option =
    let uns = d.Uns
    let unsPattern = Regex(@"[A-Z]\d{5}", RegexOptions.Compiled)

    let byUns =
        if uns = "" then
            None
        else
            reference.PoissonDensity
            |> List.tryFind (fun p -> unsPattern.Matches(p.Material) |> Seq.exists (fun m -> m.Value = uns))

    let byLabel (label: string) =
        reference.PoissonDensity |> List.tryFind (fun p -> p.Material.EndsWith(label, StringComparison.Ordinal))

    let duplex =
        set [ "S31803"; "S32101"; "S32205"; "S32304"; "S32550"; "S32750"; "S32760"; "S32906"; "S32950" ]

    match byUns with
    | Some p -> Some p
    | None when uns.StartsWith("K") || uns.StartsWith("G") || uns.StartsWith("H") ->
        // Carbon, low-alloy and Cr-Mo steels, bolting steels: all rows of the ferritic group are 7750 kg/m3, 0.30.
        byLabel "Carbon steels"
    | None when duplex.Contains uns -> byLabel "High alloy steels (duplex/ austenitic–ferritic)"
    | None when uns.StartsWith("S2") -> byLabel "High alloy steels (200 series)"
    | None when uns.StartsWith("S3") -> byLabel "High alloy steels (300 series)"
    | None when uns.StartsWith("S4") -> byLabel "High alloy steels (400 series)"
    | None when uns = "" && d.Composition.StartsWith("Carbon steel", StringComparison.Ordinal) -> byLabel "Carbon steels"
    | None -> None

/// Code values of the physical properties of a material. Nothing is invented: a property whose Code
/// row cannot be determined keeps the database value and is marked as not verified.
let of' (reference: CodeText.CodeReference) (groups: CodeGroups.GroupLists) (d: MaterialData) : PhysicalProperties =
    let resolve (quantity: string) (database: Curve) : Reference * CodeText.CodeColumn option =
        match columnFor quantity reference groups d database with
        | Some c -> { Curve = c.Values; Source = shortHeading c; FromCode = true; Column = Some c }, Some c
        | None when database.IsEmpty -> { Curve = []; Source = "Database (empty)"; FromCode = false; Column = None }, None
        | None -> { Curve = database; Source = "Database (not verified: group not found in the Code)"; FromCode = false; Column = None }, None

    let elastic, _ = resolve "E" d.Elastic
    let alphaInst, alphaColumn = resolve "AlphaInstantaneous" d.AlphaInstantaneous
    let conductivity, _ = resolve "Conductivity" d.Conductivity
    let diffusivity, _ = resolve "Diffusivity" d.Diffusivity

    let alphaMean =
        match alphaColumn |> Option.bind (CodeText.meanColumnOf reference) with
        | Some mean -> { Curve = mean.Values; Source = shortHeading mean; FromCode = true; Column = Some mean }
        | None -> { Curve = d.AlphaMean; Source = "Database (derived, not verified)"; FromCode = false; Column = None }

    let pd = poissonDensityFor reference d

    let density =
        match pd with
        | Some p -> { Curve = [ 20.0, p.Density ]; Source = $"Code PRD '{p.Material}'"; FromCode = true; Column = None }
        | None -> { Curve = (d.Density |> Option.map (fun v -> 20.0, v) |> Option.toList); Source = "Database (not verified)"; FromCode = false; Column = None }

    let poisson =
        match pd with
        | Some p -> { Curve = [ 20.0, p.Poisson ]; Source = $"Code PRD '{p.Material}'"; FromCode = true; Column = None }
        | None -> { Curve = (d.Poisson |> Option.map (fun v -> 20.0, v) |> Option.toList); Source = "Database (not verified)"; FromCode = false; Column = None }

    // Specific heat is not tabulated by the Code: it follows from the Code conductivity,
    // diffusivity and density, cp = lambda / (rho * a), with a in mm2/s (the formula of the database).
    let specificHeat =
        match conductivity.FromCode && diffusivity.FromCode, pd with
        | true, Some p ->
            let a = diffusivity.Curve |> dict

            let curve =
                [ for t, l in conductivity.Curve do
                      match a.TryGetValue t with
                      | true, diff -> yield t, l / (p.Density * diff * 1e-6)
                      | _ -> () ]

            { Curve = curve; Source = "Formula lambda/(rho*a) on Code values"; FromCode = true; Column = None }
        | _ -> { Curve = d.SpecificHeat; Source = "Database (not verified)"; FromCode = false; Column = None }

    { Elastic = elastic
      AlphaInstantaneous = alphaInst
      AlphaMean = alphaMean
      Conductivity = conductivity
      Diffusivity = diffusivity
      SpecificHeat = specificHeat
      Density = density
      Poisson = poisson }
