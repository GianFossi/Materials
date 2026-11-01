// Author: Claude
// Last revision: 2026-11-01
// Purpose: Idempotent integrity hardening (indexes, guard triggers, orphan cleanup, integrity report) and explicit empty-curve removal for native 2025 working copies only.
namespace MaterialLibrary.Crud

open System
open Microsoft.Data.Sqlite
open MaterialLibrary.Domain.Database.Lookup

/// <summary>One finding of the integrity report; an empty list means no known inconsistency.</summary>
[<CLIMutable>]
type IntegrityProblem = {
    /// Problem kind, e.g. EmptyMechanicalCurve, UnusedSize, SbSizeMismatch.
    Problem: string
    /// Identifier of the affected row (curve, size, chart or material id depending on the kind).
    RowId: int64
    /// Optional extra detail such as the property code; empty when not applicable.
    Detail: string
}

/// <summary>
/// Constraints that the imported ASME schema cannot express (SQLite cannot add CHECK to existing tables) and
/// cleanup of rows that no foreign key owns. It is applied only to explicit working copies, never to the
/// reference database, so the reference bytes and its published hash stay unchanged.
/// Units: temperature in degC, stress in MPa, sizes in mm.
/// </summary>
module WorkingCopyHardening =
    /// <summary>Hardening revision stored in <c>PRAGMA user_version</c>; raise it whenever <see cref="script"/> changes.</summary>
    [<Literal>]
    let Version = 2

    /// <summary>Indexes, guard triggers, orphan-cleanup triggers and the integrity report view. Every statement is idempotent.</summary>
    let script = """
-- Indexes missing on foreign-key columns: every cascade delete would otherwise scan these tables.
CREATE INDEX IF NOT EXISTS IDX_PropertyNotes_Property ON Property_Notes (id_property);
CREATE INDEX IF NOT EXISTS IDX_Sb1_Main ON Sb1 (id_main);
CREATE INDEX IF NOT EXISTS IDX_Sb2_Main ON Sb2 (id_main);
CREATE INDEX IF NOT EXISTS IDX_MaterialProperties_Size ON Material_Properties (id_size);
CREATE INDEX IF NOT EXISTS IDX_MaterialEPCharts_Chart ON Material_EP_Charts (id_ep_chart);

-- Allowed values (triggers: SQLite cannot add CHECK constraints to an existing table).
CREATE TRIGGER IF NOT EXISTS Guard_MaterialProperties_Code_Insert BEFORE INSERT ON Material_Properties
WHEN NEW.PropertyCode NOT IN ('Sy','Su','S1','S2','Sb1','Sb2')
BEGIN SELECT RAISE(ABORT, 'Unknown mechanical PropertyCode'); END;

-- A mechanical curve needs a Sizes row: readers join Sizes, so a NULL id_size would hide the curve instead of meaning "any dimension".
-- "Any dimension" is the Sizes row with SizeMin and SizeMax both NULL.
CREATE TRIGGER IF NOT EXISTS Guard_MaterialProperties_Size_Insert BEFORE INSERT ON Material_Properties
WHEN NEW.id_size IS NULL
BEGIN SELECT RAISE(ABORT, 'id_size is required: for any dimension use the Sizes row with SizeMin and SizeMax NULL'); END;

CREATE TRIGGER IF NOT EXISTS Guard_PhysicalProperties_Code_Insert BEFORE INSERT ON Physical_Properties
WHEN NEW.PropertyCode NOT IN ('E','Alpha','AlphaInstantaneous','Cp','TC','TD','Density','Nu')
BEGIN SELECT RAISE(ABORT, 'Unknown physical PropertyCode'); END;

CREATE TRIGGER IF NOT EXISTS Guard_PropertyValues_Insert BEFORE INSERT ON Property_Values
WHEN NEW.StressValue < 0 OR NEW.Temperature < -273.15 OR NEW.Temperature > 1200
BEGIN SELECT RAISE(ABORT, 'Stress must be >= 0 MPa and temperature within -273.15..1200 degC'); END;

CREATE TRIGGER IF NOT EXISTS Guard_PropertyValues_Update BEFORE UPDATE ON Property_Values
WHEN NEW.StressValue < 0 OR NEW.Temperature < -273.15 OR NEW.Temperature > 1200
BEGIN SELECT RAISE(ABORT, 'Stress must be >= 0 MPa and temperature within -273.15..1200 degC'); END;

CREATE TRIGGER IF NOT EXISTS Guard_PhysicalValues_Insert BEFORE INSERT ON Physical_Property_Values
WHEN NEW.Temperature < -273.15 OR NEW.Temperature > 1200
BEGIN SELECT RAISE(ABORT, 'Temperature must be within -273.15..1200 degC'); END;

CREATE TRIGGER IF NOT EXISTS Guard_Extension_Insert BEFORE INSERT ON Extension
WHEN NEW.ConstantDensity <= 0 OR NEW.ConstantPoissonRatio <= -1 OR NEW.ConstantPoissonRatio >= 0.5
BEGIN SELECT RAISE(ABORT, 'Density > 0 and -1 < Nu < 0.5'); END;

CREATE TRIGGER IF NOT EXISTS Guard_Extension_Update BEFORE UPDATE ON Extension
WHEN NEW.ConstantDensity <= 0 OR NEW.ConstantPoissonRatio <= -1 OR NEW.ConstantPoissonRatio >= 0.5
BEGIN SELECT RAISE(ABORT, 'Density > 0 and -1 < Nu < 0.5'); END;

-- Sizes rows are shared by several curves: they are immutable (copy-on-write), never edited in place.
CREATE TRIGGER IF NOT EXISTS Guard_Sizes_Immutable BEFORE UPDATE ON Sizes
BEGIN SELECT RAISE(ABORT, 'Sizes rows are shared and immutable: insert a new row and repoint the curve'); END;

-- Sb1/Sb2 stay consistent with their curve (the original trigger only guards inserts).
CREATE TRIGGER IF NOT EXISTS Sync_Sb_Size_From_Curve AFTER UPDATE OF id_size ON Material_Properties
WHEN NEW.PropertyCode IN ('Sb1','Sb2')
BEGIN
    UPDATE Sb1 SET id_size = NEW.id_size WHERE id_property = NEW.id AND NEW.PropertyCode = 'Sb1';
    UPDATE Sb2 SET id_size = NEW.id_size WHERE id_property = NEW.id AND NEW.PropertyCode = 'Sb2';
END;

CREATE TRIGGER IF NOT EXISTS Guard_Sb1_Update BEFORE UPDATE OF id_size ON Sb1
WHEN NEW.id_size <> (SELECT id_size FROM Material_Properties WHERE id = NEW.id_property)
BEGIN SELECT RAISE(ABORT, 'Sb1.id_size must equal Material_Properties.id_size'); END;

CREATE TRIGGER IF NOT EXISTS Guard_Sb2_Update BEFORE UPDATE OF id_size ON Sb2
WHEN NEW.id_size <> (SELECT id_size FROM Material_Properties WHERE id = NEW.id_property)
BEGIN SELECT RAISE(ABORT, 'Sb2.id_size must equal Material_Properties.id_size'); END;

-- Orphan cleanup: rows no foreign key owns (none of the touched tables is an audit table).
CREATE TRIGGER IF NOT EXISTS Clean_Sizes_After_Curve_Delete AFTER DELETE ON Material_Properties
WHEN OLD.id_size IS NOT NULL
BEGIN
    DELETE FROM Sizes WHERE id = OLD.id_size
      AND NOT EXISTS (SELECT 1 FROM Material_Properties WHERE id_size = OLD.id_size)
      AND NOT EXISTS (SELECT 1 FROM Sb1 WHERE id_size = OLD.id_size)
      AND NOT EXISTS (SELECT 1 FROM Sb2 WHERE id_size = OLD.id_size);
END;

CREATE TRIGGER IF NOT EXISTS Clean_Sizes_After_Curve_Repoint AFTER UPDATE OF id_size ON Material_Properties
WHEN OLD.id_size IS NOT NULL AND OLD.id_size IS NOT NEW.id_size
BEGIN
    DELETE FROM Sizes WHERE id = OLD.id_size
      AND NOT EXISTS (SELECT 1 FROM Material_Properties WHERE id_size = OLD.id_size)
      AND NOT EXISTS (SELECT 1 FROM Sb1 WHERE id_size = OLD.id_size)
      AND NOT EXISTS (SELECT 1 FROM Sb2 WHERE id_size = OLD.id_size);
END;

CREATE TRIGGER IF NOT EXISTS Clean_EPChart_After_Link_Delete AFTER DELETE ON Material_EP_Charts
BEGIN
    DELETE FROM External_Pressure_Charts WHERE id = OLD.id_ep_chart
      AND NOT EXISTS (SELECT 1 FROM Material_EP_Charts WHERE id_ep_chart = OLD.id_ep_chart);
END;

CREATE TRIGGER IF NOT EXISTS Clean_EPChart_After_Link_Repoint AFTER UPDATE OF id_ep_chart ON Material_EP_Charts
WHEN OLD.id_ep_chart <> NEW.id_ep_chart
BEGIN
    DELETE FROM External_Pressure_Charts WHERE id = OLD.id_ep_chart
      AND NOT EXISTS (SELECT 1 FROM Material_EP_Charts WHERE id_ep_chart = OLD.id_ep_chart);
END;

CREATE TRIGGER IF NOT EXISTS Clean_Metadata_After_Mechanical_Delete AFTER DELETE ON Material_Properties
BEGIN DELETE FROM Property_Curve_Metadata WHERE Code = OLD.PropertyCode AND CurveId = OLD.id; END;

CREATE TRIGGER IF NOT EXISTS Clean_Metadata_After_Physical_Delete AFTER DELETE ON Physical_Properties
BEGIN DELETE FROM Property_Curve_Metadata WHERE CurveId = OLD.id AND Code IN ('E','Alpha','AlphaInstantaneous','c','k','a','density','Nu'); END;

-- Flat read view: material + curve + size interval + temperature + value (units: degC, MPa, mm).
DROP VIEW IF EXISTS VW_Curve_Points;
CREATE VIEW VW_Curve_Points AS
SELECT m.id AS MaterialId, i.NaturalKey, m.Specification, m.Grade, m.ClassConditionTemper,
       m.AlloyDesignation AS UNS, p.id AS CurveId, p.PropertyCode, p.ProductForm,
       z.SizeMin, z.SizeMinIncluded, z.SizeMax, z.SizeMaxIncluded,
       v.Temperature, v.StressValue AS Value
FROM Main m
JOIN Material_Identity i ON i.id_main = m.id
JOIN Material_Properties p ON p.id_main = m.id
LEFT JOIN Sizes z ON z.id = p.id_size
JOIN Property_Values v ON v.id_property = p.id;

-- Integrity report: one row per finding; an empty result means no known inconsistency.
DROP VIEW IF EXISTS VW_Integrity_Report;
CREATE VIEW VW_Integrity_Report AS
SELECT 'EmptyMechanicalCurve' AS Problem, p.id AS RowId, p.PropertyCode AS Detail
FROM Material_Properties p WHERE NOT EXISTS (SELECT 1 FROM Property_Values v WHERE v.id_property = p.id)
UNION ALL
SELECT 'EmptyPhysicalCurve', pp.id, pp.PropertyCode
FROM Physical_Properties pp WHERE NOT EXISTS (SELECT 1 FROM Physical_Property_Values v WHERE v.id_physical_property = pp.id)
UNION ALL
SELECT 'DuplicateTemperature', id_property, CAST(Temperature AS TEXT)
FROM Property_Values GROUP BY id_property, Temperature HAVING COUNT(*) > 1
UNION ALL
SELECT 'DuplicateTemperature', id_physical_property, CAST(Temperature AS TEXT)
FROM Physical_Property_Values GROUP BY id_physical_property, Temperature HAVING COUNT(*) > 1
UNION ALL
SELECT 'CurveWithoutSize', p.id, p.PropertyCode
FROM Material_Properties p WHERE p.id_size IS NULL
UNION ALL
SELECT 'UnusedSize', z.id, NULL
FROM Sizes z WHERE NOT EXISTS (SELECT 1 FROM Material_Properties p WHERE p.id_size = z.id)
  AND NOT EXISTS (SELECT 1 FROM Sb1 b WHERE b.id_size = z.id) AND NOT EXISTS (SELECT 1 FROM Sb2 b WHERE b.id_size = z.id)
UNION ALL
SELECT 'UnlinkedEPChart', ch.id, ch.Chart_Name
FROM External_Pressure_Charts ch WHERE NOT EXISTS (SELECT 1 FROM Material_EP_Charts l WHERE l.id_ep_chart = ch.id)
UNION ALL
SELECT 'SbSizeMismatch', b.id_property, 'Sb1'
FROM Sb1 b JOIN Material_Properties p ON p.id = b.id_property WHERE p.id_size IS NOT b.id_size
UNION ALL
SELECT 'SbSizeMismatch', b.id_property, 'Sb2'
FROM Sb2 b JOIN Material_Properties p ON p.id = b.id_property WHERE p.id_size IS NOT b.id_size
UNION ALL
SELECT 'IdentityKeyMismatch', m.id, i.NaturalKey
FROM Main m JOIN Material_Identity i ON i.id_main = m.id
WHERE json_extract(i.NaturalKey, '$[0]') IS NOT upper(m.Specification)
   OR json_extract(i.NaturalKey, '$[1]') IS NOT upper(COALESCE(m.Grade, ''))
   OR json_extract(i.NaturalKey, '$[2]') IS NOT upper(COALESCE(m.ClassConditionTemper, ''))
   OR json_extract(i.NaturalKey, '$[3]') IS NOT upper(COALESCE(m.AlloyDesignation, ''))
UNION ALL
SELECT 'MaterialWithoutIdentity', m.id, m.Specification
FROM Main m WHERE NOT EXISTS (SELECT 1 FROM Material_Identity i WHERE i.id_main = m.id);
"""

    /// <summary>Make (curve, temperature) unique; created separately so legacy duplicates never block the editor.</summary>
    let private uniqueIndexScript = """
DROP INDEX IF EXISTS IDX_PropertyValues_Temp;
CREATE UNIQUE INDEX IF NOT EXISTS UX_PropertyValues_Curve_Temp ON Property_Values (id_property, Temperature);
DROP INDEX IF EXISTS IDX_PhysicalValues_Temp;
CREATE UNIQUE INDEX IF NOT EXISTS UX_PhysicalValues_Curve_Temp ON Physical_Property_Values (id_physical_property, Temperature);
"""

    /// <summary>Execute a multi-statement script on the connection.</summary>
    let private exec (db: SqliteConnection) (sql: string) =
        use command = db.CreateCommand()
        command.CommandText <- sql
        command.ExecuteNonQuery() |> ignore

    /// <summary>Read one scalar as int64.</summary>
    let private scalar (db: SqliteConnection) (sql: string) =
        Asme2025Data.query db sql [] |> List.head |> Array.head |> Convert.ToInt64

    /// <summary>
    /// Create the unique temperature indexes unless a curve already contains a duplicated temperature.
    /// In that case the index is skipped and <c>VW_Integrity_Report</c> lists the duplicates; no exception is raised.
    /// </summary>
    let private ensureUniqueIndexes (db: SqliteConnection) =
        let present = scalar db "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name IN ('UX_PropertyValues_Curve_Temp','UX_PhysicalValues_Curve_Temp')"
        if present < 2L then
            let duplicates =
                scalar db "SELECT (SELECT COUNT(*) FROM (SELECT 1 FROM Property_Values GROUP BY id_property,Temperature HAVING COUNT(*)>1)) + (SELECT COUNT(*) FROM (SELECT 1 FROM Physical_Property_Values GROUP BY id_physical_property,Temperature HAVING COUNT(*)>1))"
            if duplicates = 0L then exec db uniqueIndexScript

    /// <summary>
    /// Apply the hardening to a native working copy; it is a no-op when the revision in <c>PRAGMA user_version</c> is current.
    /// Requires <c>Property_Curve_Metadata</c> (created by <c>Asme2025Crud.ensureSchema</c>) and never touches the reference database.
    /// </summary>
    /// <param name="db">Open read-write connection to a working copy.</param>
    let apply (db: SqliteConnection) =
        if scalar db "PRAGMA user_version" < int64 Version then
            exec db script
            exec db $"PRAGMA user_version = {Version}"
        ensureUniqueIndexes db

    /// <summary>List every known inconsistency of a hardened working copy (empty = none).</summary>
    /// <param name="db">Open connection; the report view must exist (see <see cref="apply"/>).</param>
    let integrityReport (db: SqliteConnection) : IntegrityProblem list =
        Asme2025Data.query db "SELECT Problem, RowId, Detail FROM VW_Integrity_Report ORDER BY Problem, RowId" []
        |> List.map (fun row ->
            { Problem = string row[0]
              RowId = Convert.ToInt64 row[1]
              Detail = if isNull row[2] || Convert.IsDBNull row[2] then "" else string row[2] })

    /// <summary>Findings present now that were not in a previous report (used to fail an operation only for what it introduced).</summary>
    /// <param name="baseline">Report taken before the operation.</param>
    /// <param name="db">Open connection after the operation.</param>
    let newProblems (baseline: IntegrityProblem list) (db: SqliteConnection) : IntegrityProblem list =
        let known = Set.ofList baseline
        integrityReport db |> List.filter (fun p -> not (known.Contains p))

    /// <summary>Number of rows that violate a foreign key (0 expected).</summary>
    let foreignKeyViolations (db: SqliteConnection) : int =
        Asme2025Data.query db "PRAGMA foreign_key_check" [] |> List.length

    /// <summary>
    /// Delete every mechanical and physical curve that has no points and record each deletion in <c>Property_Edit_History</c>
    /// (operation <c>DeleteEmpty</c>). Cascades remove the curve's source row, note links and bolting entry; the cleanup triggers
    /// then remove the size interval and editor metadata left without an owner. The batch is atomic.
    /// </summary>
    /// <param name="db">Open read-write connection to a <c>.working.db</c> copy with <c>PRAGMA foreign_keys = ON</c>.</param>
    /// <returns><c>Ok n</c> with the number of curves removed (0 when there is nothing to do), or <c>Error</c> with the reason it refused to run.</returns>
    let removeEmptyCurves (db: SqliteConnection) : Result<int, string> =
        if not (db.DataSource.EndsWith(".working.db", StringComparison.OrdinalIgnoreCase)) then
            Error "Empty curves can be removed from a .working.db copy only."
        elif scalar db "PRAGMA foreign_keys" <> 1L then
            Error "Enable PRAGMA foreign_keys before removing curves, otherwise dependent rows would be left behind."
        else
            use tx = db.BeginTransaction()
            let empty = scalar db "SELECT (SELECT COUNT(*) FROM VW_Integrity_Report WHERE Problem IN ('EmptyMechanicalCurve','EmptyPhysicalCurve'))"
            exec db """
INSERT INTO Property_Edit_History (MaterialId, Code, CurveId, EditedUtc, Operation, BeforeJson)
SELECT p.id_main, p.PropertyCode, p.id, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'), 'DeleteEmpty', '{"Points":0}'
FROM Material_Properties p WHERE NOT EXISTS (SELECT 1 FROM Property_Values v WHERE v.id_property = p.id);
INSERT INTO Property_Edit_History (MaterialId, Code, CurveId, EditedUtc, Operation, BeforeJson)
SELECT pp.id_main, pp.PropertyCode, pp.id, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'), 'DeleteEmpty', '{"Points":0}'
FROM Physical_Properties pp WHERE NOT EXISTS (SELECT 1 FROM Physical_Property_Values v WHERE v.id_physical_property = pp.id);
DELETE FROM Material_Properties WHERE NOT EXISTS (SELECT 1 FROM Property_Values v WHERE v.id_property = Material_Properties.id);
DELETE FROM Physical_Properties WHERE NOT EXISTS (SELECT 1 FROM Physical_Property_Values v WHERE v.id_physical_property = Physical_Properties.id);
"""
            tx.Commit()
            Ok(int empty)
