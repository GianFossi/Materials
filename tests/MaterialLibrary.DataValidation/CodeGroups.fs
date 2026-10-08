/// Which group of the physical-property tables a steel belongs to according to the Code. The notes of
/// Tables TM-1 (Material Groups A-J), TE-1 (Groups 1-4) and TCD (Material Groups A-L) list the nominal
/// compositions of every group; the database assigns steels to groups through MaterialGroupMap, which
/// is not trusted: the group comes from the composition and the Code notes.
module MaterialLibrary.DataValidation.CodeGroups

open System
open System.IO
open System.Text.RegularExpressions
open System.Xml.Linq

/// Nominal composition in a canonical form: fractions as ASCII, hyphens, no blanks, lower case.
let normalize (composition: string) : string =
    composition
        .Replace("¼", "1/4")
        .Replace("½", "1/2")
        .Replace("¾", "3/4")
        .Replace("⅓", "1/3")
        .Replace("⅔", "2/3")
        .Replace("⅕", "1/5")
        .Replace("–", "-")
        .Replace("‐", "-")
        .Replace(" ", "")
        .ToLowerInvariant()

/// The composition lists of the notes: group id (letter or number) -> normalized compositions.
type GroupLists =
    { /// Table TM-1 notes: "A".."J".
      Modulus: Map<string, Set<string>>
      /// Table TE-1 notes: "1".."4".
      Expansion: Map<string, Set<string>>
      /// Table TCD notes: "A".."L".
      Conductivity: Map<string, Set<string>> }

let private noise =
    [ @"^--[`,\-]*$"; @"^ASME BPVC"; @"^Copyright"; @"^Provided by"; @"^Licensee="; @"^Not for Resale"; @"^No reproduction"; @"^--- SourcePage"; @"^\d{4}$"; @"^(Copy|Provid|No re)$"; @"^ð25Þ$" ]
    |> List.map (fun p -> Regex(p, RegexOptions.Compiled))

let private linesOf (path: string) : string[] =
    let doc = XDocument.Load path
    let raw = doc.Root.Element(XName.Get "RawText").Value

    raw.Split('\n')
    |> Array.map (fun l -> l.Trim())
    |> Array.filter (fun l -> l <> "" && not (noise |> List.exists (fun r -> r.IsMatch l)))

/// Reads the lists introduced by a header line matching <c>header</c> (group 1 = group id). A list
/// ends at the next numbered note, a "NOTES" line, a sentence or a table title.
let private readLists (header: Regex) (lines: string[]) : Map<string, Set<string>> =
    let lists = Collections.Generic.Dictionary<string, Set<string>>()
    let mutable current: string option = None

    let isTerminator (l: string) =
        Regex.IsMatch(l, @"^\(\w{1,3}\)") && not (header.IsMatch l)
        || l.StartsWith("NOTES", StringComparison.Ordinal)
        || l.StartsWith("GENERAL", StringComparison.Ordinal)
        || l.StartsWith("Also known", StringComparison.Ordinal)
        || l.StartsWith("These ", StringComparison.Ordinal)
        || l.StartsWith("Table ", StringComparison.Ordinal)
        || l.StartsWith("Material Group", StringComparison.Ordinal) && not (header.IsMatch l)

    for l in lines do
        let m = header.Match l

        if m.Success then
            current <- Some m.Groups.[1].Value
            if not (lists.ContainsKey current.Value) then lists.[current.Value] <- Set.empty
        elif isTerminator l then
            current <- None
        else
            match current with
            | Some id when Regex.IsMatch(l, @"^[0-9A-Za-z/.\-–]+$|Cr|Ni|Mo|Mn|Cu|steel", RegexOptions.None) ->
                lists.[id] <- lists.[id].Add(normalize l)
            | _ -> ()

    lists |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq

let load (physicalPropertiesDir: string) : GroupLists =
    let file sub name = Path.Combine(physicalPropertiesDir, sub, name)

    let tm = linesOf (file "ElasticModulus" "TM-1.xml")
    let te = Array.append (linesOf (file "ThermalExpansion" "TE-1.xml")) (linesOf (file "ThermalExpansion" "TE-2.xml"))
    let tcd = linesOf (file "ThermalConductivity" "TCD-ThermalConductivity.xml")

    { Modulus = readLists (Regex(@"Material Group ([A-Z]) (?:consists|includes)", RegexOptions.Compiled)) tm
      Expansion = readLists (Regex(@"Group (\d) alloys", RegexOptions.Compiled)) te
      Conductivity = readLists (Regex(@"Material Group ([A-Z]) (?:consists|includes)", RegexOptions.Compiled)) tcd }

/// Looks the composition up in the lists; a nitrogen variant ("-N") of a listed grade belongs to
/// the same group (the Code says the values also apply to H, L, N and LN grades).
let private find (lists: Map<string, Set<string>>) (composition: string) : string option =
    let c = normalize composition
    let lookup (x: string) = lists |> Map.tryFindKey (fun _ members -> members.Contains x)

    match lookup c with
    | Some g -> Some g
    | None when c.EndsWith("-n", StringComparison.Ordinal) -> lookup (c.Substring(0, c.Length - 2))
    | None -> None

/// The group of the elastic-modulus table (TM-1 letter) a composition belongs to.
let modulusGroup (lists: GroupLists) (composition: string) : string option =
    let c = normalize composition

    match find lists.Modulus composition with
    | Some g -> Some g
    | None when c.StartsWith("carbonsteel", StringComparison.Ordinal) -> Some "Carbon steels with C"
    // Group E: "9Cr-Mo, including variations thereof".
    | None when c.StartsWith("9cr-", StringComparison.Ordinal) && c.Contains "mo" -> Some "E"
    | None -> None

/// The group of the thermal-expansion table (TE-1 group number or named column) of a composition.
let expansionGroup (lists: GroupLists) (composition: string) : string option =
    let c = normalize composition

    match find lists.Expansion composition with
    | Some g -> Some $"(Group {g})"
    | None when c.StartsWith("9cr-1mo", StringComparison.Ordinal) -> Some "9Cr–1Mo Steels"
    | None when c = "9ni" || c = "8ni" -> Some "8Ni and 9Ni Steels"
    | None when c = "13cr" || c.StartsWith("13cr-4ni", StringComparison.Ordinal) || c.StartsWith("12cr", StringComparison.Ordinal) -> Some "12Cr, 12Cr–1Al, 13Cr"
    | None -> None

/// The group of the conductivity/diffusivity table (TCD letter) of a composition.
let conductivityGroup (lists: GroupLists) (composition: string) : string option =
    let c = normalize composition

    match find lists.Conductivity composition with
    | Some g -> Some $"Group {g}"
    | None when c.StartsWith("carbonsteel", StringComparison.Ordinal) -> Some "Group A"
    // Group F: "9Cr-1Mo" and its variations.
    | None when c.StartsWith("9cr-1mo", StringComparison.Ordinal) -> Some "Group F"
    | None -> None
