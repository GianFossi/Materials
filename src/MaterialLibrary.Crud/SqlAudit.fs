// Author: Claude
// Last revision: 2026-11-01
// Purpose: Opt-in row-level change journal (triggers) so that direct SQL edits of a native 2025 working copy leave a trace.
namespace MaterialLibrary.Crud

open System
open Microsoft.Data.Sqlite
open MaterialLibrary.Domain.Database.Lookup

/// <summary>One journaled row change.</summary>
[<CLIMutable>]
type SqlChangeEntry = {
    /// Journal sequence number (monotonic).
    Id: int64
    /// UTC timestamp, ISO 8601.
    ChangedUtc: string
    /// Table that changed.
    TableName: string
    /// INSERT, UPDATE or DELETE.
    Operation: string
    /// Primary-key value of the changed row.
    RowId: int64
    /// Row image before the change as JSON (empty for INSERT).
    BeforeJson: string
    /// Row image after the change as JSON (empty for DELETE).
    AfterJson: string
}

/// <summary>
/// Journal for edits made outside the editor. It is opt-in because it stores one row per changed table row:
/// a cascaded curve deletion or an XML import of many points journals every point.
/// It records every writer (editor and import included), so it complements, not replaces, <c>Property_Edit_History</c>.
/// </summary>
module SqlAudit =
    /// <summary>Tables whose rows are journaled: identity, curves, points, constants, regulatory data and external-pressure charts.</summary>
    let auditedTables =
        [ "Main"; "Material_Identity"; "Material_Properties"; "Property_Values"; "Physical_Properties"; "Physical_Property_Values"
          "Extension"; "ASME_Data"; "Sizes"; "Sb1"; "Sb2"; "External_Pressure_Charts"; "Material_EP_Charts"; "EP_Chart_Curves"; "EP_Chart_Points" ]

    /// <summary>Execute a (multi-statement) command.</summary>
    let private exec (db: SqliteConnection) (sql: string) =
        use command = db.CreateCommand()
        command.CommandText <- sql
        command.ExecuteNonQuery() |> ignore

    /// <summary>Trigger names for one table (fixed list, never user input).</summary>
    let private triggerNames table = [ $"Audit_{table}_Insert"; $"Audit_{table}_Update"; $"Audit_{table}_Delete" ]

    /// <summary>Table names that exist in this database, so older or partial schemas do not break the activation.</summary>
    let private existingTables (db: SqliteConnection) =
        Asme2025Data.query db "SELECT name FROM sqlite_master WHERE type='table'" [] |> List.map (fun r -> string r[0]) |> Set.ofList

    /// <summary>True when the journal triggers are installed.</summary>
    let isEnabled (db: SqliteConnection) =
        Asme2025Data.query db "SELECT COUNT(*) FROM sqlite_master WHERE type='trigger' AND name GLOB 'Audit_*'" []
        |> List.head |> Array.head |> Convert.ToInt64 > 0L

    /// <summary>
    /// Install the journal table and one INSERT/UPDATE/DELETE trigger per audited table (idempotent).
    /// Requires a read-write connection to a <c>.working.db</c> copy.
    /// </summary>
    let enable (db: SqliteConnection) : Result<unit, string> =
        if not (db.DataSource.EndsWith(".working.db", StringComparison.OrdinalIgnoreCase)) then
            Error "The change journal can be enabled on a .working.db copy only."
        else
            exec db """CREATE TABLE IF NOT EXISTS Sql_Change_Log (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, ChangedUtc TEXT NOT NULL, TableName TEXT NOT NULL, Operation TEXT NOT NULL,
                RowId INTEGER, BeforeJson TEXT, AfterJson TEXT);
                CREATE INDEX IF NOT EXISTS IDX_SqlChangeLog_Table_Row ON Sql_Change_Log (TableName, RowId);"""
            let present = existingTables db
            for table in auditedTables |> List.filter present.Contains do
                let columns = Asme2025Data.query db $"PRAGMA table_info(\"{table}\")" [] |> List.map (fun r -> string r[1], Convert.ToInt32 r[5])
                // First primary-key column identifies the row (id, id_main or id_property depending on the table).
                let key = columns |> List.find (fun (_, pk) -> pk = 1) |> fst
                let image prefix = "json_object(" + String.Join(",", columns |> List.map (fun (c, _) -> $"'{c}',{prefix}.\"{c}\"")) + ")"
                let stamp = "strftime('%Y-%m-%dT%H:%M:%fZ','now')"
                exec db $"""
CREATE TRIGGER IF NOT EXISTS Audit_{table}_Insert AFTER INSERT ON "{table}" BEGIN
    INSERT INTO Sql_Change_Log(ChangedUtc,TableName,Operation,RowId,AfterJson) VALUES({stamp},'{table}','INSERT',NEW."{key}",{image "NEW"});
END;
CREATE TRIGGER IF NOT EXISTS Audit_{table}_Update AFTER UPDATE ON "{table}" BEGIN
    INSERT INTO Sql_Change_Log(ChangedUtc,TableName,Operation,RowId,BeforeJson,AfterJson) VALUES({stamp},'{table}','UPDATE',NEW."{key}",{image "OLD"},{image "NEW"});
END;
CREATE TRIGGER IF NOT EXISTS Audit_{table}_Delete AFTER DELETE ON "{table}" BEGIN
    INSERT INTO Sql_Change_Log(ChangedUtc,TableName,Operation,RowId,BeforeJson) VALUES({stamp},'{table}','DELETE',OLD."{key}",{image "OLD"});
END;"""
            Ok ()

    /// <summary>Remove the triggers; the journal table and its rows are kept.</summary>
    let disable (db: SqliteConnection) =
        for table in auditedTables do
            for name in triggerNames table do exec db $"DROP TRIGGER IF EXISTS {name}"

    /// <summary>Most recent journal rows first.</summary>
    /// <param name="db">Open connection.</param>
    /// <param name="limit">Maximum number of rows (at least 1).</param>
    let read (db: SqliteConnection) (limit: int) : SqlChangeEntry list =
        let text (v: obj) = if isNull v || Convert.IsDBNull v then "" else string v
        Asme2025Data.query db "SELECT Id,ChangedUtc,TableName,Operation,RowId,BeforeJson,AfterJson FROM Sql_Change_Log ORDER BY Id DESC LIMIT $n" [ "$n", box (max 1 limit) ]
        |> List.map (fun r ->
            { Id = Convert.ToInt64 r[0]; ChangedUtc = text r[1]; TableName = text r[2]; Operation = text r[3]
              RowId = (if Convert.IsDBNull r[4] then 0L else Convert.ToInt64 r[4]); BeforeJson = text r[5]; AfterJson = text r[6] })

    /// <summary>Delete journal rows older than the given UTC instant (ISO 8601 comparison) and return how many were removed.</summary>
    let purgeOlderThan (db: SqliteConnection) (cutoffUtc: DateTime) : int =
        use command = db.CreateCommand()
        command.CommandText <- "DELETE FROM Sql_Change_Log WHERE ChangedUtc < $cutoff"
        command.Parameters.AddWithValue("$cutoff", cutoffUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")) |> ignore
        command.ExecuteNonQuery()
