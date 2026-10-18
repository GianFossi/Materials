module MaterialLibrary.XmlPhysicalImportTests

open System
open System.IO
open Xunit
open MaterialLibrary.Crud
open MaterialLibrary.Domain.Database.Lookup

/// <summary>Fail with a readable diagnostic rather than silently accepting an unsuccessful import.</summary>
let private ok = function Ok v -> v | Error e -> failwithf "%A" e
/// <summary>Run against an isolated native working copy and a temporary XML directory.</summary>
let private workspace action =
    let folder=Path.Combine(Path.GetTempPath(),"xml-properties-"+Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory folder |> ignore
    let path=Path.Combine(folder,"materials.working.db")
    File.Copy(Path.Combine(__SOURCE_DIRECTORY__,"../../src/MaterialLibrary/data/asme_materials_2025.db"),path)
    try action folder path finally Directory.Delete(folder,true)
/// <summary>Write an explicit physical record in GPa; the reader converts it to MPa.</summary>
let private xml folder =
    let file=Path.Combine(folder,"modulus.xml")
    File.WriteAllText(file,"""<Properties><Record id="r1" property="E" unit="GPa" description="Test record" composition="8Ni-18Cr" uns="S30400"><notes>Full source note</notes><Point temperature="20" value="200"/><Point temperature="100" value="190"/></Record></Properties>""")
    XmlPropertyReader.readFile file |> List.exactlyOne
/// <summary>Capture the expected target state before selecting an import.</summary>
let private assignment db id record reviewed replace =
    {MaterialId=id;Record=record;Reviewed=reviewed;ReplaceExisting=replace;ExpectedVersion=XmlPropertyImport.version db id record.PropertyCode}

[<Fact>]
let ``structured XML converts units and preserves full source content`` () = workspace (fun folder path ->
    let record=xml folder
    Assert.Equal("MPa",record.Unit)
    Assert.Equal(200000.,record.Points[0].Y)
    Assert.False(record.RequiresReview)
    use db=Asme2025Data.openRead path
    let id=(Asme2025Data.materials db).Head.Id
    XmlPropertyImport.apply path [|assignment db id record true false|] |> ok |> ignore
    let saved=Asme2025Data.curves db id "E" |> List.exactlyOne
    Assert.Equal(200000.,saved.Points[0].Value)
    Assert.Contains("Full source note",saved.SourceNotes)
    Assert.Equal(1,Asme2025Data.query db "SELECT count(*) FROM Xml_Import_Documents" [] |> List.head |> Array.head |> Convert.ToInt32)
    Assert.Empty(Asme2025Data.query db "PRAGMA foreign_key_check" []))

[<Fact>]
let ``repeated XML updates linked curves without duplication and stale preview is rejected`` () = workspace (fun folder path ->
    let record=xml folder
    use db=Asme2025Data.openRead path
    let id=(Asme2025Data.materials db).Head.Id
    let before=assignment db id record true false
    XmlPropertyImport.apply path [|before|] |> ok |> ignore
    Assert.True(XmlPropertyImport.apply path [|before|] |> Result.isError)
    let updated={record with Points=record.Points |> Array.map (fun p -> {p with Y=p.Y+100.})}
    XmlPropertyImport.apply path [|assignment db id updated true false|] |> ok |> ignore
    Assert.Single(Asme2025Data.curves db id "E") |> ignore
    Assert.Equal(200100.,(Asme2025Data.curves db id "E").Head.Points[0].Value)
    Assert.Equal(2,Asme2025Data.query db "SELECT count(*) FROM Xml_Import_History" [] |> List.head |> Array.head |> Convert.ToInt32))

[<Fact>]
let ``XML requires explicit replacement and a failing batch leaves all values intact`` () = workspace (fun folder path ->
    let record=xml folder
    use db=Asme2025Data.openRead path
    let ids=Asme2025Data.materials db |> List.take 2 |> List.map (fun m -> m.Id)
    XmlPropertyImport.apply path [|assignment db ids[0] record true false|] |> ok |> ignore
    let other={record with Key="other-record"}
    let batch=[|assignment db ids[1] other true false;assignment db ids[0] other true false|]
    Assert.True(XmlPropertyImport.apply path batch |> Result.isError)
    Assert.Empty(Asme2025Data.curves db ids[1] "E")
    XmlPropertyImport.apply path [|assignment db ids[0] other true true|] |> ok |> ignore
    Assert.Single(Asme2025Data.curves db ids[0] "E") |> ignore)

[<Fact>]
let ``composition UNS and explicit identity matching expose ambiguities and respect qualifiers`` () = workspace (fun folder path ->
    let source=xml folder
    let id=Asme2025Crud.saveIdentity path {Id=0L;Specification="TEST-XML";Grade="A";Condition="N";UNS="S30400";NominalComposition="18Cr-8Ni"} |> ok
    use db=Asme2025Data.openRead path
    let proposals=XmlPropertyMatching.propose db [|source;{source with Key="second"}|] |> Array.filter (fun p -> p.MaterialId=id)
    Assert.Equal(2,proposals.Length)
    Assert.All(proposals,fun p -> Assert.Contains("più record",p.Reason))
    Assert.Empty(XmlPropertyMatching.propose db [|{source with Grade="B"}|] |> Array.filter (fun p -> p.MaterialId=id))
    let chemistryOnly={source with UNS="";Description=""}
    Assert.Equal(85,(XmlPropertyMatching.propose db [|chemistryOnly|] |> Array.find (fun p -> p.MaterialId=id)).Score))

[<Fact>]
let ``pressure charts are versioned shared safely and linked through native chart tables`` () = workspace (fun folder path ->
    let source=xml folder
    let record={source with Key="CS-test";PropertyCode="EP";Unit="MPa";ChartName="CS-test";Points=[|{Temperature=100.;X=1e-5;Y=1.};{Temperature=100.;X=1e-4;Y=10.}|]}
    use db=Asme2025Data.openRead path
    let ids=Asme2025Data.materials db |> List.take 2 |> List.map (fun m -> m.Id)
    XmlPropertyImport.apply path (ids |> List.map (fun id -> assignment db id record true false) |> List.toArray) |> ok |> ignore
    let hydrated=AsmeMaterialRepository.findById path ids[0] |> ok
    Assert.Single(hydrated.StrengthProperties.ExternalPressureTables) |> ignore
    let before=Asme2025Data.pressureCurves db ids[1]
    let changed={record with Points=record.Points |> Array.map (fun p -> {p with Y=p.Y+1.})}
    XmlPropertyImport.apply path [|assignment db ids[0] changed true false|] |> ok |> ignore
    Assert.Equal<(int64*string*float*(float*float)list) list>(before,Asme2025Data.pressureCurves db ids[1])
    let chartName id = Asme2025Data.pressureCurves db id |> List.head |> fun (_,name,_,_) -> name
    Assert.NotEqual<string>(chartName ids[0],chartName ids[1]))

[<Fact>]
let ``existing staged XML extracts mean instantaneous modulus and density as reviewable independent records`` () =
    let folder=Path.Combine(__SOURCE_DIRECTORY__,"../../src/MaterialLibrary/data/physical-properties-xml")
    let records,errors=XmlPropertyReader.readFolder folder
    Assert.Empty(errors)
    Assert.Contains(records,fun r -> r.PropertyCode="Alpha" && r.Points.Length>10)
    Assert.Contains(records,fun r -> r.PropertyCode="AlphaInstantaneous" && r.Points.Length>10)
    Assert.Contains(records,fun r -> r.PropertyCode="E" && r.Points |> Array.exists (fun p -> p.Y>100000.))
    Assert.Contains(records,fun r -> r.PropertyCode="density" && r.IsConstant && r.Points[0].Y=7750.)
    Assert.All(records |> Array.filter (fun r -> r.Points.Length>0),fun r -> Assert.True(r.RequiresReview))
    Assert.True(XmlPropertyImport.validate (records |> Array.find (fun r -> r.PropertyCode="Alpha")) false |> Result.isError)

[<Fact>]
let ``XML DTD malformed units and duplicate record IDs are rejected`` () = workspace (fun folder _ ->
    let file=Path.Combine(folder,"bad.xml")
    File.WriteAllText(file,"""<!DOCTYPE x [<!ENTITY read SYSTEM "file:///etc/passwd">]><x>&read;</x>""")
    Assert.ThrowsAny<Exception>(fun () -> XmlPropertyReader.readFile file |> ignore) |> ignore
    File.WriteAllText(file,"""<Records><Record id="same" property="E" unit="MPa"><Point temperature="20" value="10"/></Record><Record id="same" property="E" unit="MPa"><Point temperature="20" value="10"/></Record></Records>""")
    Assert.ThrowsAny<Exception>(fun () -> XmlPropertyReader.readFile file |> ignore) |> ignore
    File.WriteAllText(file,"""<Record property="E" unit="unknown"><Point temperature="20" value="10"/></Record>""")
    let invalid=XmlPropertyReader.readFile file |> List.exactlyOne
    Assert.Empty(invalid.Points)
    Assert.True(XmlPropertyImport.validate invalid true |> Result.isError))

[<Fact>]
let ``density constants populate Extension and switch to tabulated only through replacement`` () = workspace (fun folder path ->
    let source=xml folder
    let record={source with PropertyCode="density";Unit="kg/m3";IsConstant=true;Points=[|{Temperature=20.;X=20.;Y=7900.}|]}
    use db=Asme2025Data.openRead path
    let id=(Asme2025Data.materials db).Head.Id
    XmlPropertyImport.apply path [|assignment db id record true false|] |> ok |> ignore
    Assert.Equal(7900.,Asme2025Data.lookupValue db id "density" 100. None None None |> ok)
    let tabulated={record with IsConstant=false;Key="tabulated";Points=[|{Temperature=20.;X=20.;Y=7890.};{Temperature=100.;X=100.;Y=7850.}|]}
    Assert.True(XmlPropertyImport.apply path [|assignment db id tabulated true false|] |> Result.isError)
    XmlPropertyImport.apply path [|assignment db id tabulated true true|] |> ok |> ignore
    Assert.Equal(7850.,Asme2025Data.lookupValue db id "density" 100. None None None |> ok)
    Assert.Empty(Asme2025Data.query db "PRAGMA foreign_key_check" []))

[<Fact>]
let ``raw pressure XML never silently guesses carried PDF exponents`` () =
    let folder=Path.Combine(__SOURCE_DIRECTORY__,"../../src/MaterialLibrary/data/external-pressure-xml")
    let records,errors=XmlPropertyReader.readFolder folder
    Assert.Empty(errors)
    let charts=records |> Array.filter (fun r -> r.PropertyCode="EP")
    Assert.NotEmpty(charts)
    Assert.All(charts,fun r -> Assert.Empty(r.Points); Assert.True(XmlPropertyImport.validate r true |> Result.isError))
