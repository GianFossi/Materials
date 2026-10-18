namespace MaterialLibrary.DatabaseUpdater

open System
open System.Globalization
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.RegularExpressions

/// <summary>One diagnostic tied to an input row or material; no guessed corrections are applied.</summary>
type Issue = { Severity: string; Code: string; MaterialKey: string; Source: string; Detail: string }

/// <summary>Four-field material identity: specification, grade, condition and UNS; product form is never part of identity.</summary>
type Identity = {
    Specification: string
    Grade: string
    Alloy: string
    Condition: string
}

/// <summary>Size interval in millimetres; Axis distinguishes thickness, diameter and DN restrictions.</summary>
type SizeBand = { Minimum: float option; MinimumIncluded: bool; Maximum: float option; MaximumIncluded: bool; Axis: string }

/// <summary>One source curve: Celsius temperatures, MPa stresses, and exact decoded source fields.</summary>
type SourceRow = {
    Table: string
    File: string
    Line: int
    Fields: Map<string,string>
    Identity: Identity
    ProductForm: string
    Size: SizeBand
    Values: (float * float) list
    Sy: float option
    Su: float option
    NoteCodes: string list
    Fingerprint: string
}

/// <summary>Table-scoped note definition; identical codes in different tables are unrelated.</summary>
type Note = { Table: string; Code: string; Kind: string; Text: string }

/// <summary>Blank revision-marked export row, retained as evidence rather than assigned a fabricated material.</summary>
type OmittedRow = { Table: string; File: string; Line: int; Fields: Map<string,string>; Reason: string }

/// <summary>Decoded input plus note documents retained verbatim for audit.</summary>
type InputData = { Rows: SourceRow list; Omitted: OmittedRow list; Notes: Note list; Documents: (string * string * string) list; Issues: Issue list; Files: (string * string * int) list; Ignored: string list; ArchiveHash: string; SourceKind: string; SourcePath: string }

/// <summary>Shared deterministic normalization and serialization functions.</summary>
module Domain =
    /// <summary>Return true for exported missing-value markers only; zero remains a real value.</summary>
    let missing (s: string) = List.contains (s.Trim()) [""; "…"; "..."; "—"; "–"]

    /// <summary>Normalize typographic dashes and repeated whitespace without fuzzy material matching.</summary>
    let normalize (s: string) =
        if missing s then ""
        else Regex.Replace(s.Trim().Replace('–','-').Replace('—','-').Replace('−','-').Replace('\u00a0',' '), @"\s+", " ").ToUpperInvariant()

    /// <summary>Serialize public report records to portable JSON.</summary>
    let json value = JsonSerializer.Serialize(value)

    /// <summary>Compute a stable SHA-256 digest.</summary>
    let hash (bytes: byte array) = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()

    /// <summary>Encode all identity fields with unambiguous JSON separators.</summary>
    let key identity = json [| identity.Specification; identity.Grade; identity.Condition; identity.Alloy |]

    /// <summary>Retrieve a field without treating an absent column as a populated value.</summary>
    let field name fields = Map.tryFind name fields |> Option.defaultValue ""

    /// <summary>Build only the four requested identity fields.</summary>
    let identity fields = {
        Specification = field "Spec. No." fields |> normalize
        Grade = field "Type/Grade" fields |> normalize
        Alloy = field "Alloy Desig./UNS No." fields |> normalize
        Condition = (Map.tryFind "Class/Condition/Temper" fields |> Option.defaultWith (fun () -> field "Class/Cond./Temper" fields)) |> normalize
    }

    /// <summary>Exclude Section IV tables from identities, values, notes and comparisons.</summary>
    let isIgnoredTable table = List.contains (normalize table) ["6A";"6B";"6C";"6D"]

    /// <summary>Compare composition strings while allowing identical chemical tokens in a different order.</summary>
    let compositionKey value =
        let normalized = normalize value
        let tokens = normalized.Split('-')
        if tokens.Length > 1 && (tokens |> Array.forall (fun token -> Regex.IsMatch(token,@"^(?:[0-9¼½¾.]+)?[A-Z]{1,2}$"))) then
            tokens |> Array.sort |> String.concat "-"
        else normalized

    /// <summary>Return one nominal composition only when all supplied, nonmissing descriptions agree.</summary>
    let nominalComposition (rows: SourceRow list) =
        let values = rows |> List.map (fun row -> field "Nominal Composition" row.Fields) |> List.filter (missing >> not) |> List.distinct |> List.sortBy normalize
        match values |> List.map compositionKey |> List.distinct with
        | [_] -> values |> List.tryHead
        | _ -> None

    /// <summary>Parse an invariant finite number, accepting decimal commas only when unambiguous.</summary>
    let number (value: string) =
        let text = value.Trim()
        let canonical = if Regex.IsMatch(text, @"^[+-]?\d+,\d+$") then text.Replace(',', '.') else text
        match Double.TryParse(canonical, NumberStyles.AllowLeadingSign ||| NumberStyles.AllowDecimalPoint ||| NumberStyles.AllowExponent, CultureInfo.InvariantCulture) with
        | true, v when Double.IsFinite(v) -> Some v
        | _ -> None

    /// <summary>List the six mechanical families checked for every MAIN material.</summary>
    let mechanicalCodes = ["Sy";"Su";"S1";"S2";"Sb1";"Sb2"]

    /// <summary>Identify source tables whose mechanical curves are imported.</summary>
    let isMechanicalTable table = List.contains table ["Sy";"Su";"1A";"1B";"5A";"5B";"3";"4"]

    /// <summary>Map the eight mechanical source tables; other tables contribute identities and audit rows only.</summary>
    let propertyCode table =
        match table with
        | "Sy" | "Su" -> table
        | "1A" | "1B" -> "S1"
        | "5A" | "5B" -> "S2"
        | "3" -> "Sb1"
        | "4" -> "Sb2"
        | _ -> "IdentityOnly"

    /// <summary>Parse exported dimensional inequalities in mm, retaining inclusivity and dimension axis.</summary>
    let parseSize (value: string) =
        let text = normalize value |> fun s -> s.Replace("≤", "<=").Replace("≥", ">=")
        let axis = if text.Contains("DN") then "DN" elif text.Contains("O.D.") then "OD" elif Regex.IsMatch(text, @"\bD\b") then "diameter" else "thickness-or-unspecified"
        let empty = { Minimum=None; MinimumIncluded=false; Maximum=None; MaximumIncluded=false; Axis=axis }
        let n = @"(\d+(?:\.\d+)?)"
        let between = Regex.Match(text, "^" + n + @"\s*(<=|<)\s*(?:T|D|DN|O\.D\.)\s*(<=|<)\s*" + n + "$")
        let range = Regex.Match(text, "^" + n + @"\s*-\s*" + n + "$")
        let bound = Regex.Match(text, @"^(?:(?:T|D|DN|O\.D\.)\s*)?(<=|>=|<|>|=)\s*" + n + @"(?:\s*O\.D\.)?$")
        let exact = Regex.Match(text, "^" + n + "$")
        let valueAt (m: Match) (i: int) = number m.Groups[i].Value
        let result =
            if text = "" || text = "ALL" then Ok empty
            elif between.Success then Ok { empty with Minimum=valueAt between 1; MinimumIncluded=between.Groups[2].Value="<="; Maximum=valueAt between 4; MaximumIncluded=between.Groups[3].Value="<=" }
            elif range.Success then Ok { empty with Minimum=valueAt range 1; Maximum=valueAt range 2; MinimumIncluded=true; MaximumIncluded=true }
            elif bound.Success then
                let v = valueAt bound 2
                match bound.Groups[1].Value with
                | "<" -> Ok { empty with Maximum=v }
                | "<=" -> Ok { empty with Maximum=v; MaximumIncluded=true }
                | ">" -> Ok { empty with Minimum=v }
                | ">=" -> Ok { empty with Minimum=v; MinimumIncluded=true }
                | _ -> Ok { empty with Minimum=v; Maximum=v; MinimumIncluded=true; MaximumIncluded=true }
            elif exact.Success then Ok { empty with Minimum=valueAt exact 1; Maximum=valueAt exact 1; MinimumIncluded=true; MaximumIncluded=true }
            else Error ("Unrecognized size restriction: " + value)
        result |> Result.bind (fun band ->
            match band.Minimum, band.Maximum with
            | Some lo, Some hi when lo > hi || (lo = hi && not (band.MinimumIncluded && band.MaximumIncluded)) -> Error ("Empty or reversed size interval: " + value)
            | _ -> Ok band)
