/// Reference columns read from the staged text of ASME BPVC II-D (Metric 2025), Subpart 2:
/// src/MaterialLibrary/data/physical-properties-xml (TM = elastic modulus, TE = thermal expansion,
/// TCD = conductivity/diffusivity, PRD = Poisson ratio and density). The XML files are raw PDF
/// extractions: the parsers below accept only rows whose shape is exactly the expected one and report
/// what they skipped, so a parsing doubt never turns into a silent wrong reference value.
module MaterialLibrary.DataValidation.CodeText

open System
open System.Globalization
open System.IO
open System.Text.RegularExpressions
open System.Xml.Linq

let private inv = CultureInfo.InvariantCulture

/// One column of a Code table: values by temperature (degC); "..." cells are absent.
type CodeColumn =
    { /// Table name, for example "TE-1".
      Table: string
      /// Quantity: "E" (GPa), "AlphaInstantaneous", "AlphaMean", "LinearExpansion" (mm/m),
      /// "Conductivity" (W/m/K), "Diffusivity" (mm2/s).
      Quantity: string
      /// Heading text of the column group (best effort, informational).
      Heading: string
      Values: (float * float) list }

/// Poisson ratio and density of one row of Table PRD.
type PoissonDensity =
    { Material: string
      Poisson: float
      Density: float }

let private noise =
    [ @"^--[`,\-]*$"
      @"^ASME BPVC"
      @"^Copyright"
      @"^Provided by"
      @"^Licensee="
      @"^Not for Resale"
      @"^No reproduction"
      @"^--- SourcePage"
      @"^ð25Þ$" ]
    |> List.map (fun p -> Regex(p, RegexOptions.Compiled))

/// Non-empty text lines of the RawText element, noise removed, unicode minus normalised.
let private tokensOf (path: string) : string[] =
    let doc = XDocument.Load path
    let raw = doc.Root.Element(XName.Get "RawText").Value

    raw.Split('\n')
    |> Array.map (fun l -> l.Trim().Replace('−', '-').Replace("…", "..."))
    |> Array.filter (fun l -> l <> "" && not (noise |> List.exists (fun r -> r.IsMatch l)))

let private numberRegex = Regex(@"^-?\d+(\.\d+)?$", RegexOptions.Compiled)
let private integerRegex = Regex(@"^-?\d{1,4}$", RegexOptions.Compiled)

let private isNumber (t: string) = numberRegex.IsMatch t
let private isCell (t: string) = t = "..." || isNumber t

let private parseNumber (t: string) = Double.Parse(t, inv)

let private cellValue (t: string) = if t = "..." then None else Some(parseNumber t)

/// Reads the rows "temperature, then width cells" that follow position <c>start</c>; stops at the
/// first row that does not have the expected shape or whose temperature does not increase.
let private readTemperatureRows (tokens: string[]) (start: int) (width: int) : (float * string[]) list * int =
    let rows = ResizeArray<float * string[]>()
    let mutable i = start
    let mutable last = Double.NegativeInfinity
    let mutable go = true

    while go && i + width < tokens.Length do
        let t = tokens.[i]

        if integerRegex.IsMatch t && (parseNumber t) > last then
            let cells = tokens.[i + 1 .. i + width]

            if cells |> Array.forall isCell then
                rows.Add((parseNumber t, cells))
                last <- parseNumber t
                i <- i + width + 1
            else
                go <- false
        else
            go <- false

    List.ofSeq rows, i

/// Tables made of a header "A B C A B C ..." (TE) or "TC TD TC TD ..." (TCD) followed by
/// temperature rows. A table can continue on later pages with a new header.
let private readGroupedTables (table: string) (columnNames: string list) (tokens: string[]) (quantities: string list) : CodeColumn list =
    let width = columnNames.Length
    let columns = ResizeArray<CodeColumn>()
    let mutable i = 0

    let isHeaderAt (index: int) =
        index + width <= tokens.Length
        && (columnNames |> List.mapi (fun k name -> tokens.[index + k] = name) |> List.forall id)

    while i < tokens.Length do
        if isHeaderAt i then
            // Heading text: lines before the header back to the previous "Temperature" line.
            let mutable back = i - 1

            while back >= 0 && not (tokens.[back].StartsWith("Temp", StringComparison.Ordinal)) && i - back < 30 do
                back <- back - 1

            let heading =
                if back >= 0 then
                    String.Join(" ", tokens.[back + 1 .. i - 1])
                else
                    ""

            // Count the repeated header blocks (one per material group on this page).
            let mutable groups = 0

            while isHeaderAt i do
                groups <- groups + 1
                i <- i + width

            let rows, next = readTemperatureRows tokens i (groups * width)

            for g in 0 .. groups - 1 do
                for k in 0 .. width - 1 do
                    let values =
                        rows
                        |> List.choose (fun (t, cells) -> cellValue cells.[g * width + k] |> Option.map (fun v -> t, v))

                    columns.Add
                        { Table = table
                          Quantity = quantities.[k]
                          Heading = $"{heading} [group {g + 1}]"
                          Values = values }

            i <- max next (i + 1)
        else
            i <- i + 1

    List.ofSeq columns

/// Table TM: header of ascending temperatures, then "label, N cells" rows.
let private readModulusTable (table: string) (tokens: string[]) : CodeColumn list =
    let columns = ResizeArray<CodeColumn>()
    let mutable i = 0

    // A header is a run of at least five ascending integers.
    let headerAt (index: int) =
        let mutable j = index
        let temps = ResizeArray<float>()

        while j < tokens.Length && integerRegex.IsMatch tokens.[j] && (temps.Count = 0 || parseNumber tokens.[j] > temps.[temps.Count - 1]) do
            temps.Add(parseNumber tokens.[j])
            j <- j + 1

        if temps.Count >= 5 then Some(List.ofSeq temps, j) else None

    let mutable temps: float list = []
    let mutable label = ResizeArray<string>()

    while i < tokens.Length do
        match headerAt i with
        | Some(found, next) when temps.IsEmpty || found <> temps ->
            temps <- found
            label.Clear()
            i <- next
        | _ ->
            if temps.IsEmpty then
                i <- i + 1
            elif isCell tokens.[i] then
                // A run of cells: exactly one row of N values expected.
                let mutable j = i

                while j < tokens.Length && isCell tokens.[j] do
                    j <- j + 1

                let count = j - i

                if count = temps.Length then
                    let values =
                        List.zip temps (List.ofArray tokens.[i .. j - 1])
                        |> List.choose (fun (t, cell) -> cellValue cell |> Option.map (fun v -> t, v))

                    columns.Add
                        { Table = table
                          Quantity = "E"
                          Heading = String.Join(" ", label)
                          Values = values }

                label.Clear()
                i <- j
            elif tokens.[i].StartsWith("NOTES", StringComparison.Ordinal) || tokens.[i].StartsWith("GENERAL NOTE", StringComparison.Ordinal) then
                temps <- []
                i <- i + 1
            else
                label.Add tokens.[i]
                i <- i + 1

    List.ofSeq columns

/// Poisson ratio and density pairs of Table PRD (density is printed with a space, "7 750").
let private readPoissonDensity (tokens: string[]) : PoissonDensity list =
    let poisson = Regex(@"^0\.\d\d$", RegexOptions.Compiled)
    let density = Regex(@"^\d( \d{3}|\d{3})$", RegexOptions.Compiled)
    let rows = ResizeArray<PoissonDensity>()
    let mutable started = false
    let label = ResizeArray<string>()
    let mutable i = 0

    while i < tokens.Length do
        let t = tokens.[i]

        if t.StartsWith("Table PRD", StringComparison.Ordinal) then
            started <- true
            label.Clear()
            i <- i + 1
        elif started && poisson.IsMatch t && i + 1 < tokens.Length && density.IsMatch tokens.[i + 1] then
            rows.Add
                { Material = String.Join(" ", label)
                  Poisson = parseNumber t
                  Density = parseNumber (tokens.[i + 1].Replace(" ", "")) }

            label.Clear()
            i <- i + 2
        else
            if started then label.Add t
            i <- i + 1

    List.ofSeq rows

/// Result of reading the staged Code text.
type CodeReference =
    { Columns: CodeColumn list
      PoissonDensity: PoissonDensity list
      /// Files that were read, for reports.
      Files: string list }

let load (physicalPropertiesDir: string) : CodeReference =
    let file sub name = Path.Combine(physicalPropertiesDir, sub, name)

    let tm =
        [ for n in 1..5 -> $"TM-{n}", file "ElasticModulus" $"TM-{n}.xml" ]
        |> List.collect (fun (table, path) -> readModulusTable table (tokensOf path))

    let te =
        [ for n in 1..5 -> $"TE-{n}", file "ThermalExpansion" $"TE-{n}.xml" ]
        |> List.collect (fun (table, path) ->
            readGroupedTables table [ "A"; "B"; "C" ] (tokensOf path) [ "AlphaInstantaneous"; "AlphaMean"; "LinearExpansion" ])

    let tcd =
        readGroupedTables
            "TCD"
            [ "TC"; "TD" ]
            (tokensOf (file "ThermalConductivity" "TCD-ThermalConductivity.xml"))
            [ "Conductivity"; "Diffusivity" ]

    let prd = readPoissonDensity (tokensOf (file "Density" "PRD-Density.xml"))

    { Columns = tm @ te @ tcd
      PoissonDensity = prd
      Files = [ "TM-1..5"; "TE-1..5"; "TCD"; "PRD" ] }

/// Columns of a quantity whose values contain every given (temperature, value) pair, ignoring
/// pairs whose temperature the column does not list. Returns the columns together with the number
/// of pairs found in the column and the number of pairs that disagree.
type Comparison =
    { Column: CodeColumn
      Matched: int
      /// Pairs of the database whose temperature the Code column does not list.
      NotInCode: int
      Mismatched: (float * float * float) list }

let compare (tolerance: float) (quantity: string) (reference: CodeReference) (database: (float * float) list) : Comparison list =
    reference.Columns
    |> List.filter (fun c -> c.Quantity = quantity)
    |> List.map (fun column ->
        let lookup = column.Values |> dict

        let mutable matched = 0
        let mutable notInCode = 0
        let mismatched = ResizeArray<float * float * float>()

        for t, v in database do
            match lookup.TryGetValue t with
            | true, c when abs (c - v) <= tolerance -> matched <- matched + 1
            | true, c -> mismatched.Add((t, v, c))
            | _ -> notInCode <- notInCode + 1

        { Column = column
          Matched = matched
          NotInCode = notInCode
          Mismatched = List.ofSeq mismatched })

/// Columns of the quantity that reproduce every database value (no mismatch, at least one match).
let exactMatches (tolerance: float) (quantity: string) (reference: CodeReference) (database: (float * float) list) =
    compare tolerance quantity reference database
    |> List.filter (fun c -> c.Mismatched.IsEmpty && c.Matched > 0 && c.NotInCode = 0)

/// Outcome of looking for a database curve in the Code columns of a quantity.
type Diagnosis =
    /// A Code column reproduces every database value at the same temperature.
    | Exact of CodeColumn list
    /// The database values equal a run of a Code column but at other temperatures: a data row that
    /// was shifted when the table was imported (typically leading "..." cells dropped).
    | Shifted of CodeColumn * databaseFirstT: float * codeFirstT: float
    /// No Code column reproduces the values.
    | NoMatch of closest: Comparison option

/// Finds the Code column(s) a database curve was taken from, or explains why none matches.
let diagnose (tolerance: float) (quantity: string) (reference: CodeReference) (database: (float * float) list) : Diagnosis =
    match exactMatches tolerance quantity reference database with
    | [] ->
        let values = database |> List.map snd |> Array.ofList
        let n = values.Length

        let shifted =
            reference.Columns
            |> List.filter (fun c -> c.Quantity = quantity)
            |> List.tryPick (fun c ->
                let cv = c.Values |> Array.ofList

                [ 0 .. cv.Length - n ]
                |> List.tryFind (fun s -> Array.forall2 (fun (a: float) (_, b) -> abs (a - b) <= tolerance) values cv.[s .. s + n - 1])
                |> Option.map (fun s -> c, fst cv.[s]))

        match shifted, database with
        | Some(column, codeT), (dbT, _) :: _ when codeT <> dbT -> Shifted(column, dbT, codeT)
        | _ ->
            compare tolerance quantity reference database
            |> List.sortByDescending (fun c -> c.Matched)
            |> List.tryHead
            |> NoMatch
    | matches -> Exact(matches |> List.map (fun m -> m.Column))

/// The mean-coefficient column (B) that belongs to an instantaneous column (A) of the same table
/// group, if the Code lists one.
let meanColumnOf (reference: CodeReference) (instantaneous: CodeColumn) : CodeColumn option =
    reference.Columns
    |> List.tryFind (fun c -> c.Quantity = "AlphaMean" && c.Table = instantaneous.Table && c.Heading = instantaneous.Heading)

/// Describes a diagnosis in one line for test messages.
let describe (quantity: string) (diagnosis: Diagnosis) : string =
    match diagnosis with
    | Exact columns ->
        let c = columns.Head
        $"{quantity}: equals {c.Table} column '{c.Heading}'"
    | Shifted(c, dbT, codeT) ->
        $"{quantity}: values of {c.Table} '{c.Heading}' stored shifted (database starts at {dbT} degC with the Code value of {codeT} degC)"
    | NoMatch(Some c) ->
        let sample = c.Mismatched |> List.truncate 2 |> List.map (fun (t, v, code) -> $"{t} degC: database {v}, Code {code}")
        let text = String.Join("; ", sample)
        $"{quantity}: no Code column reproduces the values (closest {c.Column.Table} '{c.Column.Heading}': {text})"
    | NoMatch None -> $"{quantity}: no Code column of this quantity"
