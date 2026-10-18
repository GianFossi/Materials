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
let private reference = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__,"../../src/MaterialLibrary/data/asme_materials_2025.db"))

/// <summary>Run a mutation on an isolated explicit working copy and clean up all journal files.</summary>
let private working action =
    let folder = Path.Combine(Path.GetTempPath(),"materials-native-"+Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory folder |> ignore
    let path = Path.Combine(folder,"asme_materials_2025.working.db")
    File.Copy(reference,path)
    try action path finally Directory.Delete(folder,true)

/// <summary>Create a new curve in MPa or the selected physical family's fixed unit.</summary>
let private curve materialId code =
    { Id=0L;MaterialId=materialId;Code=code;ProductForm="";SizeMinimum=Nullable();SizeMaximum=Nullable();MinimumIncluded=false;MaximumIncluded=true;
      SizeAxis="thickness-or-unspecified";SourceTable="User";SourceNotes="";UserNotes="User comment";Version="";
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
        Assert.Equal(Path.Combine(folder,"data","asme_materials_2025.db"),Configuration.resolveAsmeDatabasePath (Some folder))
        let config = {Configuration.createDefault () with Io={ (Configuration.createDefault ()).Io with MaterialDatabaseFolder="./data";AsmeMaterialDatabaseFile="asme_materials_2025.db"}}
        Configuration.save (Path.Combine(folder,Configuration.DefaultConfigFileName)) config |> ok
        Assert.Equal(Path.Combine(folder,"data","asme_materials_2025.db"),Configuration.resolveAsmeDatabasePath (Some folder))
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
    Assert.Equal("177",selected.Id)
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
