// Author: Claude
// Last revision: 2026-11-01
// Purpose: Show an allowable-stress curve next to the original ASME table row it was imported from and flag any difference.
namespace MaterialLibrary.Crud

open System
open System.Globalization
open System.Text.Json
open System.Text.RegularExpressions
open Microsoft.Data.Sqlite
open MaterialLibrary.Domain.Database.Lookup

/// <summary>Outcome of comparing one temperature column of the source row with the stored curve.</summary>
type SourceCellStatus =
    /// Source and database hold the same number.
    | Match = 0
    /// The source cell is empty (placeholder) and the database has no point either.
    | Empty = 1
    /// The source has a number but the database has no point at that temperature.
    | MissingInDatabase = 2
    /// Source and database both have a number but they differ.
    | Different = 3
    /// The database has a point that the source row does not show as a number.
    | OnlyInDatabase = 4

/// <summary>One temperature column: source text as printed, database value and comparison status.</summary>
[<CLIMutable>]
type SourceCell = {
    /// Column temperature in degC.
    Temperature: float
    /// Cell text exactly as stored from the ASME table (decimal comma or placeholder possible).
    SourceText: string
    /// Stored value in MPa, or null when the database has no point at this temperature.
    DatabaseValue: Nullable<float>
    /// Comparison result.
    Status: SourceCellStatus
}

/// <summary>An ASME table row as imported, aligned with the stored curve.</summary>
[<CLIMutable>]
type SourceRowView = {
    /// Curve identifier (Material_Properties.id).
    CurveId: int64
    /// ASME source table (1A, 1B, 5A, 5B, Sy, Su, 3, 4).
    SourceTable: string
    /// Page and line of the printed row, as imported.
    Page: string
    /// Line number on the page.
    SourceLine: string
    /// Non-numeric fields of the printed row (specification, grade, product form, size, notes, ...) in a stable order.
    Fields: (string * string) list
    /// Temperature columns in increasing temperature order.
    Cells: SourceCell list
    /// True when every column is a Match or Empty and no extra database point exists.
    IsConsistent: bool
}

/// <summary>Read-only comparison of the stored mechanical curves with the original table rows kept in <c>Property_Source.RawFieldsJson</c>.</summary>
module SourceRowComparison =
    /// <summary>Header of a temperature column, e.g. <c>&lt;=100 °C, MPa</c>; tolerant to encoding variants of the degree sign.</summary>
    let private temperatureColumn = Regex(@"^<=\s*(-?\d+(?:\.\d+)?)\s*\S{0,3}C,\s*MPa$", RegexOptions.Compiled)

    /// <summary>Parse a printed cell: decimal point or comma; any other text (ellipsis, dash, blank) means "no value".</summary>
    let private parseCell (text: string) =
        let t = if isNull text then "" else text.Trim().Replace(',', '.')
        match Double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture) with
        | true, v when Double.IsFinite v -> Some v
        | _ -> None

    /// <summary>Two numbers agree within printing precision (relative 1e-9).</summary>
    let private sameNumber (a: float) (b: float) = abs (a - b) <= 1e-9 * max 1. (max (abs a) (abs b))

    /// <summary>Compare one curve with its source row.</summary>
    /// <param name="db">Open connection to a native 2025 database (reference or working copy).</param>
    /// <param name="curveId">Material_Properties.id of a curve imported from an ASME table.</param>
    let compare (db: SqliteConnection) (curveId: int64) : Result<SourceRowView, string> =
        let source = Asme2025Data.query db "SELECT SourceTable,Page,SourceLine,RawFieldsJson FROM Property_Source WHERE id_property=$id" [ "$id", box curveId ]
        match source with
        | [] -> Error $"Curve {curveId} has no imported source row (user-created curves have none)."
        | row :: _ ->
            let text (v: obj) = if isNull v || Convert.IsDBNull v then "" else string v
            use document = JsonDocument.Parse(text row[3])
            let all = [ for p in document.RootElement.EnumerateObject() -> p.Name, (match p.Value.ValueKind with JsonValueKind.String -> p.Value.GetString() | _ -> p.Value.ToString()) ]
            let columns = all |> List.choose (fun (name, value) -> let m = temperatureColumn.Match name in if m.Success then Some(Double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), value) else None)
            let fields = all |> List.filter (fun (name, _) -> not (temperatureColumn.IsMatch name) && name <> "") |> List.sortBy fst
            let stored =
                Asme2025Data.query db "SELECT Temperature,StressValue FROM Property_Values WHERE id_property=$id" [ "$id", box curveId ]
                |> List.map (fun r -> Convert.ToDouble r[0], Convert.ToDouble r[1]) |> Map.ofList
            let cells =
                columns |> List.sortBy fst |> List.map (fun (t, printed) ->
                    let storedValue = Map.tryFind t stored
                    let status =
                        match parseCell printed, storedValue with
                        | Some s, Some d -> if sameNumber s d then SourceCellStatus.Match else SourceCellStatus.Different
                        | Some _, None -> SourceCellStatus.MissingInDatabase
                        | None, Some _ -> SourceCellStatus.OnlyInDatabase
                        | None, None -> SourceCellStatus.Empty
                    { Temperature = t; SourceText = printed; DatabaseValue = (match storedValue with Some d -> Nullable d | None -> Nullable()); Status = status })
            // Stored points at temperatures with no source column cannot be traced to the printed row.
            let orphanPoints =
                stored |> Map.toList |> List.filter (fun (t, _) -> not (columns |> List.exists (fun (c, _) -> c = t)))
                |> List.map (fun (t, d) -> { Temperature = t; SourceText = ""; DatabaseValue = Nullable d; Status = SourceCellStatus.OnlyInDatabase })
            let cells = (cells @ orphanPoints) |> List.sortBy (fun c -> c.Temperature)
            Ok { CurveId = curveId; SourceTable = text row[0]; Page = text row[1]; SourceLine = text row[2]; Fields = fields; Cells = cells
                 IsConsistent = cells |> List.forall (fun c -> c.Status = SourceCellStatus.Match || c.Status = SourceCellStatus.Empty) }

    /// <summary>Compare every imported curve; return those whose stored points differ from the printed row (empty = fully consistent).</summary>
    /// <param name="db">Open connection to a native 2025 database.</param>
    let findInconsistent (db: SqliteConnection) : SourceRowView list =
        Asme2025Data.query db "SELECT id_property FROM Property_Source WHERE Fingerprint <> 'User' ORDER BY id_property" []
        |> List.choose (fun r -> match compare db (Convert.ToInt64 r[0]) with Ok view when not view.IsConsistent -> Some view | _ -> None)
