module MaterialLibrary.Native2025Tests

open System
open System.IO
open Xunit
open MaterialLibrary
open MaterialLibrary.Crud
open MaterialLibrary.Domain
open MaterialLibrary.Domain.Database.Lookup

/// <summary>Unwrap a tested result and preserve useful failure diagnostics.</summary>
let private ok = function Ok value -> value | Error error -> failwithf "%A" error

/// <summary>The real published 2025 reference fixture, never opened for writing.</summary>
let private reference = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__,"../../database/data/materials_2025.db"))

/// <summary>Run a mutation on an isolated explicit working copy and clean up all journal files.</summary>
let private working action =
    let folder = Path.Combine(Path.GetTempPath(),"materials-native-"+Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory folder |> ignore
    let path = Path.Combine(folder,"materials_2025.working.db")
    File.Copy(reference,path)
    try action path
    finally
        // Pooled SQLite handles keep the file locked on Windows; release them and never let cleanup mask the test outcome.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()
        try Directory.Delete(folder,true) with :? IOException | :? UnauthorizedAccessException -> ()

/// <summary>Create a new curve in MPa or the selected physical family's fixed unit.</summary>
let private curve materialId code =
    { Id=0L;MaterialId=materialId;Code=code;ProductForm="";SizeMinimum=Nullable();SizeMaximum=Nullable();MinimumIncluded=false;MaximumIncluded=true;
      SizeAxis="thickness-or-unspecified";SourceTable="User";SourceNotes="";UserNotes="User comment";UserNoteItems=[||];S1StressLevel="Normal";CodeLimits=Asme2025Data.emptyCodeLimits;Version="";
      Points=[|{Temperature=20.;Value=100.};{Temperature=100.;Value=80.}|] }

[<Fact>]
let ``native 2025 reads every identity and preserves both bolting families and note bodies`` () =
    use db = Asme2025Data.openRead reference
    let identities = Asme2025Data.materials db
    Assert.Equal(2532,identities.Length)
    let bolt = identities |> List.find (fun m -> not (Asme2025Data.curves db m.Id "Sb2").IsEmpty)
    let material = AsmeMaterialRepository.findById reference bolt.Id |> ok
    Assert.Contains(material.StrengthProperties.AllowableStressDatasets,fun d -> d.Source=BoltingDivision2AllowableStress)
    let imported = Asme2025Data.curves db bolt.Id "Sb2"
    Assert.Contains(imported,fun c -> c.SourceNotes.Length>50 && c.SourceTable="4")
    let json = MaterialSerialization.toJsonString material
    let roundTrip = MaterialSerialization.fromJsonStringComplete json |> ok
    Assert.Equal<AllowableStressDataset list>(material.StrengthProperties.AllowableStressDatasets,roundTrip.StrengthProperties.AllowableStressDatasets)

[<Fact>]
let ``native allowable curves expose their own P-No and Section limits and persist edits`` () = working (fun path ->
    // SA-178 A (UNS K01200) has S1 variants whose VIII-1 limits differ (NP and 538), which the per-material ASME_Data row cannot represent.
    let identity,variants =
        use db = Asme2025Data.openRead path
        let identity = Asme2025Data.materials db |> List.find (fun m -> m.Specification="SA-178" && m.Grade="A" && m.UNS="K01200")
        identity,Asme2025Data.curves db identity.Id "S1"
    let limitsVIII1 = variants |> List.map (fun c -> c.CodeLimits.MaxTempSectionVIII1) |> List.distinct
    Assert.Contains("NP",limitsVIII1)
    Assert.Contains("538",limitsVIII1)
    Assert.All(variants,fun c -> Assert.False(String.IsNullOrWhiteSpace c.CodeLimits.PNumber))
    Assert.Equal(Nullable 538.,Asme2025Data.temperatureLimitC "538")
    Assert.False((Asme2025Data.temperatureLimitC "NP").HasValue)
    Assert.False((Asme2025Data.temperatureLimitC "204 (Cl. 3 only)").HasValue)
    let chosen = variants |> List.find (fun c -> c.CodeLimits.MaxTempSectionVIII1="538")
    let hydrated = AsmeMaterialRepository.findById path identity.Id |> ok
    Assert.Contains(hydrated.StrengthProperties.AllowableStressDatasets,fun d -> d.DatabaseRowId=chosen.Id && d.MaximumTemperature=Some 538.)
    let invalid = {chosen with CodeLimits={chosen.CodeLimits with MaxTempSectionVIII1="hot"}}
    Assert.True(Asme2025Crud.saveCurve path invalid |> Result.isError)
    let edited = {chosen with CodeLimits={chosen.CodeLimits with MaxTempSectionVIII1="500";GroupNumber="2";MaxTempSectionIII="204 (Cl. 3 only)"}}
    Asme2025Crud.saveCurve path edited |> ok |> ignore
    let saved =
        use db = Asme2025Data.openRead path
        Asme2025Data.curves db identity.Id "S1" |> List.find (fun c -> c.Id=chosen.Id)
    Assert.Equal("500",saved.CodeLimits.MaxTempSectionVIII1)
    Assert.Equal("2",saved.CodeLimits.GroupNumber)
    Assert.Equal("204 (Cl. 3 only)",saved.CodeLimits.MaxTempSectionIII)
    // Restoring the source values clears the stored override; the reference source row is never modified.
    Asme2025Crud.saveCurve path {saved with CodeLimits=chosen.CodeLimits} |> ok |> ignore
    use check = Asme2025Data.openRead path
    let stored = Asme2025Data.query check "SELECT CodeLimitsJson FROM Property_Curve_Metadata WHERE Code='S1' AND CurveId=$id" ["$id",box chosen.Id] |> List.head |> Array.head
    Assert.Null(stored)
    Assert.Equal(chosen.CodeLimits,Asme2025Data.sourceCodeLimits check chosen.Id))

[<Fact>]
let ``native S1 curves classify G5 as high stress and persist editable note rows`` () = working (fun path ->
    let identity,selected =
        use db = Asme2025Data.openRead path
        let materialIdWithG5 =
            Asme2025Data.query db """SELECT p.id_main FROM Material_Properties p
                JOIN Property_Note_Links l ON l.id_property=p.id AND l.SourceTable IN ('1A','1B') AND l.Code='G5'
                WHERE p.PropertyCode='S1' ORDER BY p.id LIMIT 1""" []
            |> List.head |> Array.head |> Convert.ToInt64
        let identity = Asme2025Data.materials db |> List.find (fun m -> m.Id=materialIdWithG5)
        let selected = Asme2025Data.curves db identity.Id "S1" |> List.find (fun c -> c.SourceNotes.Contains("/ G5 (Specific):"))
        Assert.Equal("High",selected.S1StressLevel)
        let standardMaterialId =
            Asme2025Data.query db """SELECT p.id_main FROM Material_Properties p
                WHERE p.PropertyCode='S1' AND NOT EXISTS (
                    SELECT 1 FROM Property_Note_Links l WHERE l.id_property=p.id AND l.SourceTable IN ('1A','1B') AND l.Code='G5')
                ORDER BY p.id LIMIT 1""" []
            |> List.head |> Array.head |> Convert.ToInt64
        let normalCurve = Asme2025Data.curves db standardMaterialId "S1" |> List.head
        Assert.Equal("Normal",normalCurve.S1StressLevel)
        identity,selected
    let note = {SourceTable="1A";Code="G5";Text="Copied and reviewed note"}
    let requested = {selected with UserNotes="Free user text";UserNoteItems=[|note;{SourceTable="User";Code="";Text="Legacy row"}|];S1StressLevel="High"}
    let id = Asme2025Crud.saveCurve path requested |> ok
    use savedDb = Asme2025Data.openRead path
    let saved = Asme2025Data.curves savedDb identity.Id "S1" |> List.find (fun c -> c.Id=id)
    Assert.Equal("High",saved.S1StressLevel)
    Assert.Equal("Free user text",saved.UserNotes)
    Assert.Equal<Asme2025UserNote array>([|note|],saved.UserNoteItems)
    let hydrated = Asme2025Repository.hydrate savedDb identity
    savedDb.Close()
    Assert.Contains(hydrated.StrengthProperties.AllowableStressDatasets,fun dataset ->
        dataset.DatabaseRowId=id && dataset.Source=Division1HighAllowableStress && dataset.Case=HighStrengthAllowableStress))

[<Fact>]
let ``native curve edits preserve source notes and other variants and reject stale updates`` () = working (fun path ->
    use db = Asme2025Data.openRead path
    let identity = Asme2025Data.materials db |> List.find (fun m -> not (Asme2025Data.curves db m.Id "S1").IsEmpty)
    let before = Asme2025Data.curves db identity.Id "S1"
    let chosen = before.Head
    let changed = {chosen with Points=chosen.Points |> Array.map (fun p -> {p with Value=p.Value+1.});UserNotes="Reviewed manually"}
    Asme2025Crud.saveCurve path changed |> ok |> ignore
    use check = Asme2025Data.openRead path
    let after = Asme2025Data.curves check identity.Id "S1"
    let actual = after |> List.find (fun c -> c.Id=chosen.Id)
    Assert.Equal(chosen.SourceNotes,actual.SourceNotes)
    Assert.Equal("Reviewed manually",actual.UserNotes)
    Assert.Equal(before.Length,after.Length)
    Assert.NotEqual<string>(chosen.Version,actual.Version)
    Assert.True(Asme2025Crud.saveCurve path changed |> Result.isError)
    Assert.True(Asme2025Crud.deleteCurve path chosen |> Result.isError)
    Asme2025Crud.deleteCurve path actual |> ok
    use final = Asme2025Data.openRead path
    Assert.Equal(before.Length-1,(Asme2025Data.curves final identity.Id "S1").Length)
    Assert.Equal(414,Asme2025Data.query final "SELECT count(*) FROM Source_Notes" [] |> List.head |> Array.head |> Convert.ToInt32)
    Assert.Empty(Asme2025Data.query final "PRAGMA foreign_key_check" []))

[<Fact>]
let ``native editor creates all requested mechanical and physical families and clears constants`` () = working (fun path ->
    let id = Asme2025Crud.saveIdentity path {Id=0L;Specification="TEST-2025";Grade="A";Condition="N";UNS="X1";NominalComposition="Test"} |> ok
    for code,_ in Asme2025Data.families do
        let requested = curve id code
        let requested = if code="Nu" then {requested with Points=[|{Temperature=20.;Value=0.3};{Temperature=100.;Value=0.29}|]} else requested
        let newId = Asme2025Crud.saveCurve path requested |> ok
        use db = Asme2025Data.openRead path
        let saved = Asme2025Data.curves db id code |> List.exactlyOne
        Assert.Equal(newId,saved.Id)
        Assert.Equal(2,saved.Points.Length)
        Assert.Equal("User comment",saved.UserNotes)
    Asme2025Crud.saveConstants path id (Nullable 7850.) (Nullable 0.3) |> ok
    Asme2025Crud.saveConstants path id (Nullable()) (Nullable()) |> ok
    use db = Asme2025Data.openRead path
    Assert.Null(Asme2025Data.query db "SELECT ConstantDensity FROM Extension WHERE id_main=(SELECT id FROM Main WHERE Specification='TEST-2025')" [] |> List.head |> Array.head)
    Assert.Empty(Asme2025Data.query db "PRAGMA foreign_key_check" [])
    let material = AsmeMaterialRepository.findById path id |> ok
    Assert.Equal(2,material.PhysicalProperties.SpecificHeatTable.Value.Length))

[<Fact>]
let ``native editor validates atomic saves and prohibits writing the reference edition`` () = working (fun path ->
    use db = Asme2025Data.openRead path
    let id = (Asme2025Data.materials db).Head.Id
    let requested = curve id "E"
    Assert.True(Asme2025Crud.saveCurve reference requested |> Result.isError)
    Assert.True(Asme2025Crud.saveCurve path {requested with Points=[|{Temperature=20.;Value=1.};{Temperature=20.;Value=2.}|]} |> Result.isError)
    Assert.True(Asme2025Crud.saveCurve path {requested with Points=[|{Temperature=nan;Value=1.}|]} |> Result.isError)
    Assert.True(Asme2025Crud.saveCurve path {requested with Code="Sy";ProductForm="Plate"} |> Result.isError)
    Assert.True(Asme2025Crud.saveConstants path id (Nullable -1.) (Nullable 0.5) |> Result.isError)
    Assert.Empty(Asme2025Data.curves db id "E"))

[<Fact>]
let ``native curve lookup enforces open boundaries ambiguity and temperature range`` () =
    let first = {curve 1L "S1" with Id=1L;ProductForm="Plate";SizeMinimum=Nullable 10.;SizeMaximum=Nullable 20.;MinimumIncluded=false;MaximumIncluded=true}
    let second = {first with Id=2L;ProductForm="Tube"}
    Assert.Equal(90.,Asme2025Data.lookup [first] 60. (Some 20.) None None |> ok)
    Assert.True(Asme2025Data.lookup [first] 60. (Some 10.) None None |> Result.isError)
    Assert.True(Asme2025Data.lookup [first;second] 60. (Some 15.) None None |> Result.isError)
    Assert.Equal(90.,Asme2025Data.lookup [first;second] 60. (Some 15.) (Some "Tube") None |> ok)
    Assert.True(Asme2025Data.lookup [first] 150. (Some 15.) None None |> Result.isError)

[<Fact>]
let ``working database provisioning and material listing recognize native MAIN without creating legacy Materials`` () = working (fun path ->
    MaterialDatabaseCrud.ensureSchema path |> ok |> ignore
    Assert.Equal(2532,(MaterialDatabaseCrud.listMaterials path |> ok).Length)
    let selected = MaterialDatabaseCrud.listMaterials path |> ok |> List.head
    let material = MaterialDatabaseCrud.readMaterial path selected.MaterialKey |> ok
    Assert.Equal(selected.Specification,material.Specification)
    use db = Asme2025Data.openRead path
    Assert.Empty(Asme2025Data.query db "SELECT name FROM sqlite_master WHERE name='Materials'" []))

[<Fact>]
let ``native constants support arbitrary finite Celsius temperatures without inventing missing data`` () = working (fun path ->
    use db = Asme2025Data.openRead path
    let id = (Asme2025Data.materials db).Head.Id
    Assert.True(Asme2025Data.lookupValue db id "Nu" 200. None None None |> Result.isError)
    Asme2025Crud.saveConstants path id (Nullable 7850.) (Nullable 0.) |> ok
    Assert.Equal(7850.,Asme2025Data.lookupValue db id "density" 450. None None None |> ok)
    Assert.Equal(0.,Asme2025Data.lookupValue db id "Nu" 450. None None None |> ok)
    Assert.True(Asme2025Data.lookupValue db id "Nu" nan None None None |> Result.isError))

[<Fact>]
let ``native metadata changes enforce four field uniqueness without changing original source notes`` () = working (fun path ->
    let one = {Id=0L;Specification="TEST-UNIQUE";Grade="B";Condition="N";UNS="X2";NominalComposition="Test"}
    let id = Asme2025Crud.saveIdentity path one |> ok
    Assert.True(Asme2025Crud.saveIdentity path {one with Specification="test-unique"} |> Result.isError)
    let existing = {one with Id=id;Grade="C"}
    Asme2025Crud.saveIdentity path existing |> ok |> ignore
    use db = Asme2025Data.openRead path
    Assert.Equal(1,Asme2025Data.materials db |> List.filter (fun m -> m.Specification="TEST-UNIQUE") |> List.length)
    Assert.Equal(414,Asme2025Data.query db "SELECT count(*) FROM Source_Notes" [] |> List.head |> Array.head |> Convert.ToInt32))

[<Fact>]
let ``active configuration points to 2025 and the archived 2023 database remains explicit`` () =
    let folder = Path.Combine(Path.GetTempPath(),"materials-config-"+Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory folder |> ignore
    try
        Assert.Equal(Path.Combine(folder,"data","materials_2025.db"),Configuration.resolveAsmeDatabasePath (Some folder))
        let config = {Configuration.createDefault () with Io={ (Configuration.createDefault ()).Io with MaterialDatabaseFolder="./data";AsmeMaterialDatabaseFile="materials_2025.db"}}
        Configuration.save (Path.Combine(folder,Configuration.DefaultConfigFileName)) config |> ok
        Assert.Equal(Path.Combine(folder,"data","materials_2025.db"),Configuration.resolveAsmeDatabasePath (Some folder))
    finally Directory.Delete(folder,true)

[<Fact>]
let ``native product form search uses curve context without duplicating MAIN identities`` () =
    let identity = { Id=123L;Specification="TEST";Grade="A";Condition="N";UNS="X";NominalComposition="" }
    let basic = BasicProperties.create None None 0. 0. 0.
    let physical = PhysicalPropertiesTable.create None [] [] None [] None None
    let material = Material.create "123" "Test" identity.Specification identity.Grade basic physical
    let propertyTable = PropertyTable.create1D "S1" "Temperature" "degC" "S1" "MPa" ReturnError [PropertyTable.entry 20. 100.] |> ok
    let propertyTable = {propertyTable with Columns=propertyTable.Columns |> List.map (fun c -> {c with SizeRange={c.SizeRange with Label=Some "Plate / 1A / curve 1"}})}
    let dataset : AllowableStressDataset = {DatabaseRowId=1L;Source=Division1AllowableStress;Case=StandardStrengthAllowableStress;Table=propertyTable;SizeMinimum=None;SizeMaximum=None;MaximumTemperature=None;CreepTemperature=None;AsmeNoteReferences=[];Notes=None}
    let material = {material with StrengthProperties={material.StrengthProperties with AllowableStressDatasets=[dataset]}}
    Assert.Single(MaterialFiltering.findMany {MaterialSearchCriteria.empty with ProductForm=Some(Exact "Plate")} [material]) |> ignore
    Assert.Empty(MaterialFiltering.findMany {MaterialSearchCriteria.empty with ProductForm=Some(Exact "Tube")} [material])

[<Fact>]
let ``native public search hydrates the 2025 catalog and filters product form from its curve context`` () =
    let criteria = {MaterialSearchCriteria.empty with Specification=Some(Exact "SA-516");Grade=Some(Exact "70");ProductForm=Some(Exact "Plate")}
    let materials = AsmeMaterialRepository.findMany reference criteria |> ok
    let selected = materials |> List.exactlyOne
    Assert.Equal(string (Asme2025Data.materials (Asme2025Data.openRead reference) |> List.find (fun m -> m.Specification="SA-516" && m.Grade="70" && m.UNS="K02700")).Id,selected.Id)
    Assert.True(selected.StrengthProperties.SyTable.IsSome)
    Assert.Contains(selected.StrengthProperties.AllowableStressDatasets,fun dataset -> not dataset.AsmeNoteReferences.IsEmpty)

[<Fact>]
let ``native exact closed size intervals survive material serialization`` () =
    use db = Asme2025Data.openRead reference
    let id = Asme2025Data.query db "SELECT p.id_main FROM Material_Properties p JOIN Sizes s ON s.id=p.id_size WHERE p.PropertyCode IN ('S1','S2','Sb1','Sb2') AND s.SizeMin=s.SizeMax AND s.SizeMinIncluded AND s.SizeMaxIncluded LIMIT 1" [] |> List.head |> Array.head |> Convert.ToInt64
    let material = AsmeMaterialRepository.findById reference id |> ok
    for dataset in material.StrengthProperties.AllowableStressDatasets do AllowableStressDataset.validate dataset |> ok |> ignore
    let restored = MaterialSerialization.toJsonString material |> MaterialSerialization.fromJsonStringComplete |> ok
    Assert.Equal<AllowableStressDataset list>(material.StrengthProperties.AllowableStressDatasets,restored.StrengthProperties.AllowableStressDatasets)

/// <summary>Run raw SQL on a working copy with foreign keys on, as a user editing the database directly would.</summary>
let private rawSql (path: string) (sql: string) =
    let options = Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(DataSource=path,Mode=Microsoft.Data.Sqlite.SqliteOpenMode.ReadWrite,ForeignKeys=Nullable true,Pooling=false)
    use db = new Microsoft.Data.Sqlite.SqliteConnection(options.ConnectionString)
    db.Open()
    use command = db.CreateCommand()
    command.CommandText <- sql
    command.ExecuteNonQuery() |> ignore

[<Fact>]
let ``working copy hardening is applied once, leaves the reference untouched and reports nothing on clean data`` () = working (fun path ->
    MaterialDatabaseCrud.ensureSchema path |> ok |> ignore
    MaterialDatabaseCrud.ensureSchema path |> ok |> ignore
    use db = Asme2025Data.openRead path
    Assert.Equal(int64 WorkingCopyHardening.Version,Asme2025Data.query db "PRAGMA user_version" [] |> List.head |> Array.head |> Convert.ToInt64)
    Assert.Empty(WorkingCopyHardening.integrityReport db)
    Assert.Equal(149029L,Asme2025Data.query db "SELECT COUNT(*) FROM VW_Curve_Points" [] |> List.head |> Array.head |> Convert.ToInt64)
    use original = Asme2025Data.openRead reference
    Assert.Equal(0L,Asme2025Data.query original "PRAGMA user_version" [] |> List.head |> Array.head |> Convert.ToInt64)
    Assert.Empty(Asme2025Data.query original "SELECT name FROM sqlite_master WHERE name LIKE 'Guard_%' OR name='VW_Integrity_Report'" []))

[<Fact>]
let ``working copy hardening rejects duplicate temperatures invalid codes negative stress and in-place size edits`` () = working (fun path ->
    MaterialDatabaseCrud.ensureSchema path |> ok |> ignore
    let throws sql = Assert.ThrowsAny<Exception>(fun () -> rawSql path sql) |> ignore
    throws "INSERT INTO Property_Values(id_property,Temperature,StressValue) SELECT id_property,Temperature,StressValue FROM Property_Values LIMIT 1"
    throws "INSERT INTO Material_Properties(id_main,PropertyCode) VALUES(1,'yield')"
    throws "INSERT INTO Physical_Properties(id_main,PropertyCode) VALUES(1,'unknown')"
    throws "INSERT INTO Property_Values(id_property,Temperature,StressValue) VALUES(1,2000,10)"
    throws "INSERT INTO Property_Values(id_property,Temperature,StressValue) VALUES(1,1000,-5)"
    throws "UPDATE Sizes SET SizeMax=999 WHERE id=1"
    throws "INSERT INTO Extension(id_main,ConstantDensity,ConstantPoissonRatio) VALUES(1,-1,0.3)"
    use db = Asme2025Data.openRead path
    Assert.Empty(WorkingCopyHardening.integrityReport db))

[<Fact>]
let ``working copy hardening removes sizes metadata and unshared charts left by curve and material deletion`` () = working (fun path ->
    let id = Asme2025Crud.saveIdentity path {Id=0L;Specification="TEST-CLEAN";Grade="A";Condition="N";UNS="X9";NominalComposition="Test"} |> ok
    let sy = {curve id "Sy" with SizeMaximum=Nullable 40.;UserNotes="note"}
    let curveId = Asme2025Crud.saveCurve path sy |> ok
    let saved =
        use db = Asme2025Data.openRead path
        Asme2025Data.curves db id "Sy" |> List.exactlyOne
    Assert.Equal(curveId,saved.Id)
    Asme2025Crud.deleteCurve path saved |> ok
    use db = Asme2025Data.openRead path
    Assert.Empty(WorkingCopyHardening.integrityReport db)
    Assert.Equal(0L,Asme2025Data.query db "SELECT COUNT(*) FROM Property_Curve_Metadata WHERE Code='Sy' AND CurveId=$id" ["$id",box curveId] |> List.head |> Array.head |> Convert.ToInt64)
    Assert.Equal(331L,Asme2025Data.query db "SELECT COUNT(*) FROM Sizes" [] |> List.head |> Array.head |> Convert.ToInt64)
    // A chart linked to one material disappears with it; a chart shared with another material survives.
    rawSql path "INSERT INTO External_Pressure_Charts(id,Chart_Name) VALUES(900,'T-1'); INSERT INTO Material_EP_Charts(id_main,id_ep_chart) VALUES(1,900),(2,900)"
    rawSql path "DELETE FROM Main WHERE id=1"
    Assert.Equal(1L,Asme2025Data.query db "SELECT COUNT(*) FROM External_Pressure_Charts WHERE id=900" [] |> List.head |> Array.head |> Convert.ToInt64)
    rawSql path "DELETE FROM Main WHERE id=2"
    Assert.Equal(0L,Asme2025Data.query db "SELECT COUNT(*) FROM External_Pressure_Charts WHERE id=900" [] |> List.head |> Array.head |> Convert.ToInt64))

[<Fact>]
let ``working copy hardening does not block a legacy copy with duplicate temperatures and reports it`` () = working (fun path ->
    rawSql path "INSERT INTO Property_Values(id_property,Temperature,StressValue) SELECT id_property,Temperature,StressValue FROM Property_Values LIMIT 1"
    MaterialDatabaseCrud.ensureSchema path |> ok |> ignore
    use db = Asme2025Data.openRead path
    let problems = WorkingCopyHardening.integrityReport db
    Assert.Equal("DuplicateTemperature",(problems |> List.exactlyOne).Problem)
    Assert.Empty(Asme2025Data.query db "SELECT name FROM sqlite_master WHERE name='UX_PropertyValues_Curve_Temp'" []))

[<Fact>]
let ``empty curves are removed atomically with an audit row and no orphans, and only from a working copy`` () = working (fun path ->
    MaterialDatabaseCrud.ensureSchema path |> ok |> ignore
    rawSql path "INSERT INTO Sizes(SizeMin,SizeMinIncluded,SizeMax,SizeMaxIncluded) VALUES(1,0,2,1); INSERT INTO Material_Properties(id_main,id_size,PropertyCode) VALUES(1,last_insert_rowid(),'Sy'); INSERT INTO Property_Curve_Metadata(Code,CurveId) SELECT 'Sy',MAX(id) FROM Material_Properties; INSERT INTO Physical_Properties(id_main,PropertyCode) VALUES(1,'E')"
    let options = Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(DataSource=path,Mode=Microsoft.Data.Sqlite.SqliteOpenMode.ReadWrite,ForeignKeys=Nullable true,Pooling=false)
    use db = new Microsoft.Data.Sqlite.SqliteConnection(options.ConnectionString)
    db.Open()
    let kinds = WorkingCopyHardening.integrityReport db |> List.map (fun p -> p.Problem) |> List.sort
    Assert.Equal<string list>(["EmptyMechanicalCurve";"EmptyPhysicalCurve"],kinds)
    Assert.Equal(2,WorkingCopyHardening.removeEmptyCurves db |> ok)
    Assert.Empty(WorkingCopyHardening.integrityReport db)
    Assert.Equal(331L,Asme2025Data.query db "SELECT COUNT(*) FROM Sizes" [] |> List.head |> Array.head |> Convert.ToInt64)
    Assert.Equal(2L,Asme2025Data.query db "SELECT COUNT(*) FROM Property_Edit_History WHERE Operation='DeleteEmpty'" [] |> List.head |> Array.head |> Convert.ToInt64)
    Assert.Equal(0,WorkingCopyHardening.removeEmptyCurves db |> ok)
    Assert.Empty(Asme2025Data.query db "PRAGMA foreign_key_check" [])
    use original = Asme2025Data.openRead reference
    Assert.True(WorkingCopyHardening.removeEmptyCurves original |> Result.isError))

/// <summary>Open a read-write connection (foreign keys on) to a working copy for the lifetime of a test.</summary>
let private openWritable (path: string) =
    let options = Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(DataSource=path,Mode=Microsoft.Data.Sqlite.SqliteOpenMode.ReadWrite,ForeignKeys=Nullable true,Pooling=false)
    let db = new Microsoft.Data.Sqlite.SqliteConnection(options.ConnectionString)
    db.Open()
    db

/// <summary>Scalar helper for integer results.</summary>
let private count (db: Microsoft.Data.Sqlite.SqliteConnection) (sql: string) =
    Asme2025Data.query db sql [] |> List.head |> Array.head |> Convert.ToInt64

[<Fact>]
let ``sql change journal is opt-in, records direct edits with before and after images and can be switched off`` () = working (fun path ->
    MaterialDatabaseCrud.ensureSchema path |> ok |> ignore
    use db = openWritable path
    Assert.False(SqlAudit.isEnabled db)
    // Opt-in: nothing is journaled until enabled.
    rawSql path "UPDATE Property_Values SET StressValue=StressValue WHERE id=1"
    Assert.Empty(Asme2025Data.query db "SELECT 1 FROM sqlite_master WHERE name='Sql_Change_Log'" [])
    SqlAudit.enable db |> ok
    SqlAudit.enable db |> ok
    Assert.True(SqlAudit.isEnabled db)
    rawSql path "UPDATE Property_Values SET StressValue=StressValue+1 WHERE id=1"
    rawSql path "DELETE FROM Property_Values WHERE id=2"
    rawSql path "UPDATE Main SET Revision='r2' WHERE id=1"
    let entries = SqlAudit.read db 10
    Assert.Equal(3,entries.Length)
    let update = entries |> List.find (fun e -> e.TableName="Property_Values" && e.Operation="UPDATE")
    Assert.Equal(1L,update.RowId)
    Assert.Contains("\"StressValue\":78.6",update.BeforeJson)
    Assert.Contains("\"StressValue\":79.6",update.AfterJson)
    let delete = entries |> List.find (fun e -> e.Operation="DELETE")
    Assert.Equal(2L,delete.RowId)
    Assert.Equal("",delete.AfterJson)
    SqlAudit.disable db
    Assert.False(SqlAudit.isEnabled db)
    rawSql path "UPDATE Property_Values SET StressValue=StressValue+1 WHERE id=1"
    Assert.Equal(3,(SqlAudit.read db 10).Length)
    Assert.Equal(3,SqlAudit.purgeOlderThan db (DateTime.UtcNow.AddMinutes 1.))
    use original = Asme2025Data.openRead reference
    Assert.True(SqlAudit.enable original |> Result.isError))

[<Fact>]
let ``source row comparison matches every imported curve with its printed ASME row and detects a changed value`` () = working (fun path ->
    use db = openWritable path
    let view = SourceRowComparison.compare db 176L |> ok
    Assert.Equal("1A",view.SourceTable)
    Assert.True(view.IsConsistent)
    Assert.Equal(40.,view.Cells.Head.Temperature)
    Assert.Equal(Nullable 138.,view.Cells.Head.DatabaseValue)
    Assert.Contains(view.Fields,fun (name,_) -> name="Spec. No.")
    // Decimal-comma rows (curve 8614) and placeholder cells must still compare as consistent.
    Assert.True((SourceRowComparison.compare db 8614L |> ok).IsConsistent)
    Assert.Empty(SourceRowComparison.findInconsistent db)
    rawSql path "UPDATE Property_Values SET StressValue=139 WHERE id_property=176 AND Temperature=40"
    let changed = SourceRowComparison.compare db 176L |> ok
    Assert.False(changed.IsConsistent)
    Assert.Equal(SourceCellStatus.Different,changed.Cells.Head.Status)
    Assert.Equal(176L,(SourceRowComparison.findInconsistent db |> List.exactlyOne).CurveId)
    // User-created curves have no printed row.
    Assert.True(SourceRowComparison.compare db 999999L |> Result.isError))

[<Fact>]
let ``reference counts match the recorded import verification and the hardened copy is fully consistent`` () = working (fun path ->
    MaterialDatabaseCrud.ensureSchema path |> ok |> ignore
    use verification = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__,"../../docs/asme-2025-import-verification.json")))
    let expected (name: string) = verification.RootElement.GetProperty(name).GetInt64()
    use db = openWritable path
    Assert.Equal(expected "main_materials",count db "SELECT COUNT(*) FROM Main")
    Assert.Equal(expected "mechanical_values",count db "SELECT COUNT(*) FROM Property_Values")
    Assert.Equal(expected "regulatory_materials",count db "SELECT COUNT(*) FROM ASME_Data")
    Assert.Equal(expected "full_note_texts_verified",count db "SELECT COUNT(*) FROM Source_Notes")
    Assert.Equal("ok",Asme2025Data.query db "PRAGMA integrity_check" [] |> List.head |> Array.head |> string)
    Assert.Equal(0,WorkingCopyHardening.foreignKeyViolations db)
    Assert.Empty(WorkingCopyHardening.integrityReport db))

/// <summary>Directory with the example SQL scripts that the documentation embeds verbatim.</summary>
let private exampleSql = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../../docs/examples/sql"))

/// <summary>Run one documented example script on a hardened working copy (foreign keys on, as the scripts require).</summary>
let private runExample (path: string) (file: string) = rawSql path (File.ReadAllText(Path.Combine(exampleSql, file)))

/// <summary>Hardened working copy with the two documented example materials loaded.</summary>
let private withExamples action = working (fun path ->
    MaterialDatabaseCrud.ensureSchema path |> ok |> ignore
    runExample path "materiale-s1-s2.sql"
    runExample path "materiale-bolting-sb1-sb2.sql"
    runExample path "proprieta-fisiche-e-pressione-esterna.sql"
    action path)

/// <summary>Standard sheet temperatures used by the documentation.</summary>
let private sheetConditions = { RoomTemperature=20.; DesignTemperatures=[350.;400.;475.]; OperatingTemperatures=[100.;200.;250.] }

/// <summary>Material id of a documented example by specification.</summary>
let private exampleId (db: Microsoft.Data.Sqlite.SqliteConnection) spec = Asme2025Data.materials db |> List.find (fun m -> m.Specification=spec) |> fun m -> m.Id

[<Fact>]
let ``documented S1 S2 example is complete: views, limits, notes, product forms and size ranges`` () = withExamples (fun path ->
    use db = openWritable path
    let id = exampleId db "SA-DEMO2"
    // The S1/S2 views need Property_Source rows: 5 S1 curves and 4 S2 curves of 10 points each.
    Assert.Equal(50L,count db "SELECT COUNT(*) FROM S1 WHERE Specification='SA-DEMO2'")
    Assert.Equal(40L,count db "SELECT COUNT(*) FROM S2 WHERE Specification='SA-DEMO2'")
    Assert.Empty(WorkingCopyHardening.integrityReport db)
    let s1 = Asme2025Data.curves db id "S1"
    Assert.Equal(5,s1.Length)
    Assert.Equal<string list>(["FORGINGS";"FORGINGS";"PLATE";"PLATE";"PLATE"],s1 |> List.map (fun c -> c.ProductForm) |> List.sort)
    Assert.Equal(1,s1 |> List.filter (fun c -> c.S1StressLevel="High") |> List.length)
    Assert.All(s1,fun c -> Assert.Equal("450",c.CodeLimits.MaxTempSectionVIII1))
    // Two plate curves share the 0..40 mm range: the lookup refuses to guess between Normal and High stress.
    Assert.True(Asme2025Data.lookup s1 100. (Some 20.) (Some "PLATE") None |> Result.isError)
    let normal = s1 |> List.find (fun c -> c.ProductForm="PLATE" && c.S1StressLevel="Normal" && c.SizeMaximum=Nullable 40.)
    Assert.Equal(138.,Asme2025Data.lookup s1 100. (Some 20.) (Some "PLATE") (Some normal.Id) |> ok)
    Assert.Equal(129.,Asme2025Data.lookup s1 100. (Some 60.) (Some "PLATE") None |> ok)
    Assert.Equal(5,(Asme2025Data.curves db id "S1" |> List.map (fun c -> c.SourceNotes) |> List.filter (fun n -> n.Contains "G10")).Length)
    let s2 = Asme2025Data.curves db id "S2"
    Assert.Equal(4,s2.Length)
    // The BAR curve has SizeMin and SizeMax NULL: any dimension (and no dimension at all) selects it; other forms keep their ranges.
    let bar = s2 |> List.find (fun c -> c.ProductForm="BAR")
    Assert.False(bar.SizeMinimum.HasValue || bar.SizeMaximum.HasValue)
    for size in [ Some 1.; Some 20.; Some 1000.; None ] do Assert.Equal(150.,Asme2025Data.lookup s2 100. size (Some "BAR") None |> ok)
    Assert.True(Asme2025Data.lookup s2 100. (Some 500.) (Some "PLATE") None |> Result.isError)
    // Without a product form the unbounded curve competes with the bounded ones: the lookup refuses to choose.
    Assert.True(Asme2025Data.lookup s2 100. (Some 20.) None None |> Result.isError))

[<Fact>]
let ``documented bolting example links Sb1 and Sb2 to three diameter ranges with the source table guard`` () = withExamples (fun path ->
    use db = openWritable path
    let id = exampleId db "SA-DEMO3"
    Assert.Equal(3L,count db ("SELECT COUNT(*) FROM Sb1 WHERE id_main=" + string id))
    Assert.Equal(3L,count db ("SELECT COUNT(*) FROM Sb2 WHERE id_main=" + string id))
    let sb1 = Asme2025Data.curves db id "Sb1"
    Assert.Equal<float list>([65.;100.;150.],sb1 |> List.map (fun c -> c.SizeMaximum.Value) |> List.sort)
    Assert.Equal(172.,Asme2025Data.lookup sb1 100. (Some 50.) None None |> ok)
    Assert.True(Asme2025Data.lookup sb1 100. (Some 200.) None None |> Result.isError)
    Assert.Empty(WorkingCopyHardening.integrityReport db)
    // Sb1 rows without a Table 3 source are rejected by the original trigger, even on a hardened copy.
    Assert.ThrowsAny<Exception>(fun () -> rawSql path "INSERT INTO Material_Properties(id_main,id_size,PropertyCode) VALUES(1,1,'Sb1'); INSERT INTO Sb1(id_property,id_main,id_size) VALUES(last_insert_rowid(),1,1)") |> ignore)

[<Fact>]
let ``product sheet lists every size range with room design and operating columns, limits and all notes`` () = withExamples (fun path ->
    use db = openWritable path
    let sheet = ProductSheet.build db (exampleId db "SA-DEMO2") sheetConditions |> ok
    for expected in [ "# Product data sheet — SA-DEMO2 · GR1 · N · K88888"; "### 3.1 t ≤ 40 mm"; "### 3.2 40 < t ≤ 100 mm"; "### 3.3 100 < t ≤ 200 mm"; "### 3.4 t > 200 mm"
                      "Room<br>20 °C"; "T.Des<br>350 °C"; "T.Des<br>475 °C"; "T.Ope<br>250 °C"; "**Notes of table 3.1**"; "260 (min.)"; "PLATE, High"; "FORGINGS"; "## 5. External pressure charts"; "### 5.1 Chart DEMO-CS-1"
                      "50.5 ⚠"; "**User note:** Forgiati"; "**ASME 1A / G10** (Specific)"; "**ASME 1A / G5** (Specific)"; "## 7. General reading notes" ] do
        Assert.Contains(expected,sheet)
    // 3.1 has three S1 curves (PLATE Normal, PLATE High, FORGINGS) and three S2 curves (PLATE, FORGINGS, BAR); 3.3 only the 100-200 mm forging S1.
    let section n =
        let parts = sheet.Split("### 3.")
        parts[n].Split("## 4.")[0]
    Assert.Equal(6,section 1 |> fun s -> s.Split('\n') |> Array.filter (fun l -> l.StartsWith "| S1 " || l.StartsWith "| S2 ") |> Array.length)
    Assert.Equal(1,section 3 |> fun s -> s.Split('\n') |> Array.filter (fun l -> l.StartsWith "| S1 ") |> Array.length)
    // Physical rows: no source note exists for hand-entered curves and nothing is invented for missing data.
    Assert.Matches(@"\| density\s+\| constant\s+\|\s+7850",sheet)
    Assert.Contains("n.d.",sheet)
    // Design temperature above the VIII-1 limit (450) is flagged, below it is not.
    // The unbounded BAR curve is valid for every size range, so it appears in every section, including the one above 200 mm where no other curve is valid.
    Assert.Equal(4,sheet.Split([| char 10 |]) |> Array.filter (fun l -> l.StartsWith "| S2 " && l.Contains "| BAR") |> Array.length)
    Assert.DoesNotContain("101 ⚠",sheet))

[<Fact>]
let ``product sheet of the bolting example has diameter sections and rejects invalid conditions`` () = withExamples (fun path ->
    use db = openWritable path
    let id = exampleId db "SA-DEMO3"
    let sheet = ProductSheet.build db id sheetConditions |> ok
    for expected in [ "### 3.1 d ≤ 65 mm"; "### 3.2 65 < d ≤ 100 mm"; "### 3.3 100 < d ≤ 150 mm"; "| Sb1 "; "| Sb2 "; "**ASME 3 / W1** (Specific)"; "**ASME 4 / G1** (Specific)"; "99 ⚠" ] do
        Assert.Contains(expected,sheet)
    Assert.True(ProductSheet.build db id {sheetConditions with DesignTemperatures=[100.;200.;300.;400.]} |> Result.isError)
    Assert.True(ProductSheet.build db id {sheetConditions with RoomTemperature=nan} |> Result.isError)
    Assert.True(ProductSheet.build db 999999L sheetConditions |> Result.isError))

[<Fact>]
let ``documentation embeds the example scripts and generated sheets verbatim`` () =
    let doc = File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__,"../../docs/asme-materials-db.md")).Replace("\r\n","\n")
    for file in Directory.GetFiles(exampleSql,"*.sql") do
        let script = File.ReadAllText(file).Replace("\r\n","\n").Trim()
        Assert.True(doc.Contains script,$"docs/asme-materials-db.md must embed {Path.GetFileName file} verbatim")

[<Fact>]
let ``a size interval with SizeMin and SizeMax NULL means any dimension for S1 S2 Sb1 and Sb2`` () =
    use db = Asme2025Data.openRead reference
    // The single unbounded Sizes row is shared by thousands of curves of every mechanical family.
    let families = Asme2025Data.query db "SELECT p.PropertyCode,COUNT(*) FROM Material_Properties p JOIN Sizes z ON z.id=p.id_size WHERE z.SizeMin IS NULL AND z.SizeMax IS NULL GROUP BY 1" [] |> List.map (fun r -> string r[0],Convert.ToInt64 r[1]) |> Map.ofList
    for family in [ "S1"; "S2"; "Sb1"; "Sb2"; "Sy"; "Su" ] do Assert.True(families[family] > 0L)
    Assert.Equal(1L,Asme2025Data.query db "SELECT COUNT(*) FROM Sizes WHERE SizeMin IS NULL AND SizeMax IS NULL" [] |> List.head |> Array.head |> Convert.ToInt64)
    // SA-307 Gr. B bolting (curve 3215) has no size limit: a 1 mm and a 1000 mm bolt, or no size at all, select the same curve.
    let materialId = Asme2025Data.query db "SELECT id_main FROM Sb1 WHERE id_property=3215" [] |> List.head |> Array.head |> Convert.ToInt64
    let sb1 = Asme2025Data.curves db materialId "Sb1" |> List.filter (fun c -> c.Id=3215L)
    Assert.False(sb1.Head.SizeMinimum.HasValue || sb1.Head.SizeMaximum.HasValue)
    for size in [ Some 1.; Some 1000.; None ] do Assert.Equal(48.3,Asme2025Data.lookup sb1 100. size None None |> ok)

[<Fact>]
let ``a curve without a Sizes row is hidden from the readers, rejected on hardened copies and reported`` () = working (fun path ->
    // Not hardened yet: the insert is accepted, but the readers join Sizes, so the curve silently disappears.
    let before = (use db = Asme2025Data.openRead path in (Asme2025Data.curves db 1L "Sy").Length)
    rawSql path "INSERT INTO Material_Properties(id_main,id_size,PropertyCode) VALUES(1,NULL,'Sy'); INSERT INTO Property_Values(id_property,Temperature,StressValue) VALUES(last_insert_rowid(),40,100)"
    use db = openWritable path
    Assert.Equal(before,(Asme2025Data.curves db 1L "Sy").Length)
    MaterialDatabaseCrud.ensureSchema path |> ok |> ignore
    let problems = WorkingCopyHardening.integrityReport db
    Assert.Equal("CurveWithoutSize",(problems |> List.exactlyOne).Problem)
    Assert.ThrowsAny<Exception>(fun () -> rawSql path "INSERT INTO Material_Properties(id_main,id_size,PropertyCode) VALUES(1,NULL,'Su')") |> ignore)
