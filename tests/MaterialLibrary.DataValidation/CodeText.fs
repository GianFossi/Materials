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
      /// UNS numbers the Code lists for this column (empty for steel groups named by letter).
      Uns: string list
      /// The Code name of the column: "Material Group D", "Group 3", the alloy family, or the UNS list.
      Label: string
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

    // A header line such as "Material -200 -125" carries the first temperatures: split it.
    let headerWithTemperatures = Regex(@"^Material\s+(-?\d+(?:\s+-?\d+)*)$", RegexOptions.Compiled)

    raw.Split('\n')
    |> Array.map (fun l -> l.Trim().Replace('−', '-').Replace("…", "..."))
    // Thousands are printed with a space ("1 000", "7 750"): make them plain integers.
    |> Array.map (fun l -> Regex.Replace(l, @"^(\d) (\d{3})$", "$1$2"))
    |> Array.collect (fun l ->
        let m = headerWithTemperatures.Match l

        if m.Success then
            Array.append [| "Material" |] (m.Groups.[1].Value.Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries))
        else
            [| l |])
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

let private unsRegex = Regex(@"[A-Z]\d{5}", RegexOptions.Compiled)

/// Splits the heading lines of a page into the UNS numbers of each of its <c>groups</c> columns.
/// A UNS number starts a new group unless it is joined to the previous one by a comma or "and";
/// when the headings start every group with "Coefficients for" that keyword delimits the groups.
/// Returns empty lists when the split does not give exactly <c>groups</c> groups.
let private groupUns (lines: string[]) (groups: int) : string list[] =
    let empty = Array.create groups []

    let byKeyword =
        let starts = lines |> Array.indexed |> Array.filter (fun (_, l) -> l.StartsWith("Coefficients for", StringComparison.Ordinal)) |> Array.map fst

        if starts.Length = groups then
            Some(
                [| for k in 0 .. groups - 1 ->
                       let last = if k + 1 < groups then starts.[k + 1] - 1 else lines.Length - 1
                       lines.[starts.[k] .. last]
                       |> Array.collect (fun l -> unsRegex.Matches(l) |> Seq.map (fun m -> m.Value) |> Array.ofSeq)
                       |> List.ofArray |]
            )
        else
            None

    match byKeyword with
    | Some g -> g
    | None ->
        // Flatten to (uns, textBefore) where textBefore is what separates it from the previous number.
        let joined = String.Join(" ", lines)
        let matches = unsRegex.Matches(joined) |> Seq.toList

        let grouped = ResizeArray<ResizeArray<string>>()
        let mutable previousEnd = -1

        for m in matches do
            let between = if previousEnd < 0 then "" else joined.Substring(previousEnd, m.Index - previousEnd).Trim()
            let sameGroup = previousEnd >= 0 && (between = "," || between = "and" || between = ", and" || between = "&")

            if sameGroup then
                grouped.[grouped.Count - 1].Add m.Value
            else
                grouped.Add(ResizeArray [ m.Value ])

            previousEnd <- m.Index + m.Length

        let named = [| for g in grouped -> List.ofSeq g |]

        if named.Length = groups then
            named
        else
            // Groups named by letter ("Material Group K") come first and groups named by alloy family
            // ("Titanium Gr. 1, 2 ...") come last on the pages of the Code: they carry no UNS number.
            let front = lines |> Array.filter (fun l -> l = "Material") |> Array.length
            let back = Regex.Matches(joined, @"Titanium (Gr\.|Grades)").Count

            if front + named.Length + back = groups then
                Array.concat [ Array.create front []; named; Array.create back [] ]
            else
                empty

/// Names of the <c>groups</c> columns of a page: the "Coefficients for ..." texts of Table TE, or
/// "Group X" for the letter groups of Table TCD followed by the alloys named by UNS number.
let private groupLabels (lines: string[]) (groups: int) (uns: string list[]) : string[] =
    let keyword =
        lines |> Array.indexed |> Array.filter (fun (_, l) -> l.StartsWith("Coefficients for", StringComparison.Ordinal)) |> Array.map fst

    if keyword.Length = groups then
        [| for k in 0 .. groups - 1 ->
               let last = if k + 1 < groups then keyword.[k + 1] - 1 else lines.Length - 1
               String.Join(" ", lines.[keyword.[k] .. last]).Replace("Coefficients for ", "").Trim() |]
    else
        let text = String.Join(" ", lines)
        let letters = Regex.Matches(text, @"Material Group ([A-Z])\b") |> Seq.map (fun m -> "Group " + m.Groups.[1].Value) |> List.ofSeq
        let front = if text.Contains("Ductile Cast Iron") && letters.Length + 1 = groups then [ "Ductile Cast Iron" ] else []
        let named = uns |> Array.filter (fun u -> not u.IsEmpty) |> Array.map (String.concat ",") |> List.ofArray
        let all = front @ letters @ named

        if all.Length = groups then Array.ofList all else Array.create groups ""

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

            let headingLines = if back >= 0 then tokens.[back + 1 .. i - 1] else [||]
            let heading = String.Join(" ", headingLines)

            // Count the repeated header blocks (one per material group on this page).
            let mutable groups = 0

            while isHeaderAt i do
                groups <- groups + 1
                i <- i + width

            let rows, next = readTemperatureRows tokens i (groups * width)
            let unsByGroup = groupUns headingLines groups
            let labels = groupLabels headingLines groups unsByGroup

            // The PDF text lists "Ductile Cast Iron" in the heading before the letter groups, but its
            // column sits between them. It is the column with the fewest values (cast iron stops at 325 degC).
            let labels =
                if labels |> Array.contains "Ductile Cast Iron" then
                    let counts = [| for g in 0 .. groups - 1 -> rows |> List.filter (fun (_, cells) -> cells.[g * width] <> "...") |> List.length |]
                    let ductile = counts |> Array.findIndex (fun c -> c = Array.min counts)
                    let others = labels |> Array.filter (fun l -> l <> "Ductile Cast Iron") |> List.ofArray
                    let mutable remaining = others
                    [| for g in 0 .. groups - 1 ->
                           if g = ductile then
                               "Ductile Cast Iron"
                           else
                               let next = List.head remaining
                               remaining <- List.tail remaining
                               next |]
                else
                    labels

            for g in 0 .. groups - 1 do
                for k in 0 .. width - 1 do
                    let values =
                        rows
                        |> List.choose (fun (t, cells) -> cellValue cells.[g * width + k] |> Option.map (fun v -> t, v))

                    columns.Add
                        { Table = table
                          Quantity = quantities.[k]
                          Heading = $"{heading} [group {g + 1}]"
                          Uns = unsByGroup.[g]
                          Label = labels.[g]
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
                          Uns = unsRegex.Matches(String.Join(" ", label)) |> Seq.map (fun m -> m.Value) |> List.ofSeq
                          Label = String.Join(" ", label)
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
            // The first row carries the table title and the "Ferrous Materials" heading in its label.
            let text = String.Join(" ", label)
            let marker = "Ferrous Materials "
            let cut = text.LastIndexOf(marker, StringComparison.Ordinal)
            let text = if cut >= 0 then text.Substring(cut + marker.Length) else text

            rows.Add
                { Material = text
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
    /// The database reproduces every value of a Code column and adds values at temperatures where the
    /// Code prints "..." (typically the first listed value repeated at lower temperatures).
    | Padded of CodeColumn * extraTemperatures: float list
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

        // Extra database temperatures with all the Code values reproduced.
        let padded =
            compare tolerance quantity reference database
            |> List.tryFind (fun c -> c.Mismatched.IsEmpty && c.Matched = c.Column.Values.Length && c.Matched > 0 && c.NotInCode > 0)

        match shifted, database with
        | Some(column, codeT), (dbT, _) :: _ when codeT <> dbT -> Shifted(column, dbT, codeT)
        | _ when padded.IsSome ->
            let c = padded.Value
            let known = c.Column.Values |> List.map fst |> set
            Padded(c.Column, database |> List.map fst |> List.filter (fun t -> not (known.Contains t)))
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

/// Short name of a Code column for messages: its UNS numbers, its label or the page heading.
let columnName (c: CodeColumn) : string =
    let what =
        if not c.Uns.IsEmpty then String.Join(",", c.Uns)
        elif c.Label <> "" then c.Label
        else c.Heading

    let what = if what.Length > 60 then what.Substring(0, 60) + "..." else what
    $"{c.Table} {what}"

/// Describes a diagnosis in one line for test messages.
let describe (quantity: string) (diagnosis: Diagnosis) : string =
    match diagnosis with
    | Exact columns -> $"{quantity}: equals {columnName columns.Head}"
    | Shifted(c, dbT, codeT) ->
        $"{quantity}: values of {columnName c} stored shifted (database starts at {dbT} degC with the Code value of {codeT} degC)"
    | Padded(c, extra) ->
        let temps = String.Join(", ", extra |> List.truncate 6)
        $"{quantity}: matches {columnName c} but also lists values where the Code has none ({temps} degC)"
    | NoMatch(Some c) ->
        let sample = c.Mismatched |> List.truncate 2 |> List.map (fun (t, v, code) -> $"{t} degC: database {v}, Code {code}")
        let text = String.Join("; ", sample)
        $"{quantity}: values differ from {columnName c.Column} ({text})"
    | NoMatch None -> $"{quantity}: no Code column of this quantity"

/// Compares a database curve with one specific Code column.
let diagnoseAgainst (tolerance: float) (column: CodeColumn) (database: (float * float) list) : Diagnosis =
    let values = database |> List.map snd |> Array.ofList
    let n = values.Length
    let cv = column.Values |> Array.ofList
    let lookup = column.Values |> dict

    let mismatched = database |> List.choose (fun (t, v) -> match lookup.TryGetValue t with | true, c when abs (c - v) > tolerance -> Some(t, v, c) | _ -> None)
    let notInCode = database |> List.filter (fun (t, _) -> not (lookup.ContainsKey t))
    let matched = database.Length - mismatched.Length - notInCode.Length

    let shifted =
        [ 0 .. cv.Length - n ]
        |> List.tryFind (fun s -> n > 0 && Array.forall2 (fun (a: float) (_, b) -> abs (a - b) <= tolerance) values cv.[s .. s + n - 1])

    if mismatched.IsEmpty && notInCode.IsEmpty && matched > 0 then
        Exact [ column ]
    elif mismatched.IsEmpty && matched = column.Values.Length && matched > 0 then
        Padded(column, notInCode |> List.map fst)
    else
        match shifted, database with
        | Some s, (dbT, _) :: _ when fst cv.[s] <> dbT -> Shifted(column, dbT, fst cv.[s])
        | _ ->
            NoMatch(Some { Column = column; Matched = matched; NotInCode = notInCode.Length; Mismatched = mismatched })

/// Code columns of a quantity that list the given UNS number.
let columnsForUns (reference: CodeReference) (quantity: string) (uns: string) : CodeColumn list =
    reference.Columns |> List.filter (fun c -> c.Quantity = quantity && c.Uns |> List.contains uns)
