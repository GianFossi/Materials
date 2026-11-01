// Author: Copilot
// Last revision: 2026-11-01
// Purpose: Validate XML physical-property parsing, association and transactional imports.
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
    File.Copy(Path.Combine(__SOURCE_DIRECTORY__,"../../database/data/asme_materials_2025.db"),path)
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
let ``Code Case shared curves support density Nu constants and temperature bounds`` () =
    let file = Path.Combine(Path.GetTempPath(), "cc-curves-" + Guid.NewGuid().ToString("N") + ".xml")
    try
        File.WriteAllText(file, """<PhysicalPropertyTableImport schema="material-curves-v1" codeCase="TEST">
          <Notes>Code Case applicability.</Notes><Curves>
            <Curve id="rho" property="density" unit="kg/m3" temperatureUnit="degC">
              <Materials><Material id="one" name="Test" uns="N08935" minTemperature="0" maxTemperature="100" requiresReview="true" /></Materials>
              <Points><Point temperature="-20" value="8100" /><Point temperature="20" value="8050" /><Point temperature="100" value="8000" /><Point temperature="150" value="7950" /></Points>
            </Curve>
            <Curve id="nu" property="Nu" unit="dimensionless" temperatureUnit="degC">
              <Materials><Material id="one" name="Test" uns="N08935" /></Materials>
              <Points><Point temperature="20" value="0.291" /><Point temperature="100" value="0.294" /></Points>
            </Curve>
            <Curve id="constant" property="density" unit="kg/m3" temperatureUnit="degC" constant="true">
              <Materials><Material id="one" name="Test" uns="N08120" minTemperature="899" maxTemperature="982" requiresReview="true" /></Materials>
              <Points><Point value="8070" /></Points>
            </Curve>
          </Curves><SourceCurves>
            <Curve id="dynamic" property="DynamicE" unit="MPa"><Values><Value temperature="25" value="197000" /></Values></Curve>
            <Curve id="rt" property="Alpha" unit="1/degC"><Values><Value temperatureLabel="RT-100" value="0.0000136" /></Values></Curve>
          </SourceCurves></PhysicalPropertyTableImport>""")
        let records = XmlPropertyReader.readFile file |> List.toArray
        Assert.Equal(3, records.Length)
        Assert.Equal<float array>([|20.; 100.|], records[0].Points |> Array.map (fun p -> p.Temperature))
        Assert.Equal<float array>([|8050.; 8000.|], records[0].Points |> Array.map (fun p -> p.Y))
        Assert.False(records[0].IsConstant)
        Assert.Equal("Nu", records[1].PropertyCode)
        Assert.Equal(0.294, records[1].Points[1].Y)
        Assert.True(records[2].IsConstant)
        Assert.Single(records[2].Points) |> ignore
        Assert.Equal(8070., records[2].Points[0].Y)
        Assert.Contains("reader convention", records[2].Notes)
        Assert.Contains("minTemperature (degC): 899", records[2].Notes)
        Assert.All(records, fun r ->
            Assert.True(r.RequiresReview)
            Assert.True(XmlPropertyImport.validate r false |> Result.isError)
            Assert.True(XmlPropertyImport.validate r true |> Result.isOk))
        Assert.DoesNotContain(records, fun r -> r.PropertyCode="DynamicE" || r.PropertyCode="Alpha")
    finally
        File.Delete file

[<Fact>]
let ``Code Case shared curves reject invalid Poisson constants and ranges`` () =
    let file = Path.Combine(Path.GetTempPath(), "cc-invalid-" + Guid.NewGuid().ToString("N") + ".xml")
    let write property unit attributes material points =
        File.WriteAllText(file, $"<PhysicalPropertyTableImport schema=\"material-curves-v1\"><Curves><Curve id=\"one\" property=\"{property}\" unit=\"{unit}\" temperatureUnit=\"degC\" {attributes}><Materials><Material id=\"one\" name=\"Test\" uns=\"N08935\" {material}/></Materials><Points>{points}</Points></Curve></Curves></PhysicalPropertyTableImport>")
    try
        for property,unit,attributes,material,points in [
            "Nu","dimensionless","","","""<Point temperature="20" value="0.5"/>"""
            "Nu","dimensionless","","","""<Point temperature="20" value="-0.1"/>"""
            "density","kg/m3","","","""<Point temperature="20" value="0"/>"""
            "density","kg/m3","constant=\"true\"","","""<Point temperature="20" value="8050"/>"""
            "density","kg/m3","constant=\"true\"","","""<Point value="8050"/><Point value="8000"/>"""
            "density","kg/m3","constant=\"yes\"","","""<Point value="8050"/>"""
            "E","MPa","constant=\"true\"","","""<Point value="200000"/>"""
            "DynamicE","MPa","","","""<Point temperature="20" value="200000"/>"""
            "Nu","dimensionless","","minTemperature=\"200\" maxTemperature=\"100\"","""<Point temperature="20" value="0.3"/>"""
            "Nu","dimensionless","","minTemperature=\"50\"","""<Point temperature="20" value="0.3"/>"""
            "Nu","dimensionless","","minTemperature=\"NaN\"","""<Point temperature="20" value="0.3"/>"""
        ] do
            write property unit attributes material points
            Assert.Throws<FormatException>(fun () -> XmlPropertyReader.readFile file |> ignore) |> ignore
    finally
        File.Delete file

[<Fact>]
let ``Code Case datasets retain source-only data provenance and independently reviewable records`` () =
    let folder = Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "database", "data", "physical-properties-xml", "CodeCases")
    let records, errors = XmlPropertyReader.readFolder folder
    Assert.Empty(errors)
    Assert.Equal(177, records.Length)
    Assert.Equal(records.Length, records |> Array.map (fun r -> r.Key) |> Array.distinct |> Array.length)
    Assert.All(records, fun r ->
        Assert.True(r.RequiresReview)
        Assert.NotEmpty(r.Points)
        Assert.True(XmlPropertyImport.validate r true |> Result.isOk)
        Assert.Contains("Code Case", r.Notes))
    let index = System.Xml.Linq.XDocument.Load(Path.Combine(folder, "index.xml"))
    let datasets = index.Root.Elements(System.Xml.Linq.XName.Get "Dataset") |> Seq.toArray
    Assert.Equal(34, datasets.Length)
    Assert.Empty(XmlPropertyReader.readFile(Path.Combine(folder, "index.xml")))
    let mutable sourceOnly = 0
    let mutable rawPoints = 0
    for dataset in datasets do
        let file = Path.Combine(folder, XmlPropertyReader.value "file" dataset)
        let document = System.Xml.Linq.XDocument.Load file
        let root = document.Root
        let curves = root.Element(System.Xml.Linq.XName.Get "Curves").Elements() |> Seq.toArray
        let original = root.Element(System.Xml.Linq.XName.Get "SourceCurves").Elements() |> Seq.toArray
        sourceOnly <- sourceOnly + original.Length
        Assert.Equal(int (XmlPropertyReader.value "curveCount" root), curves.Length)
        Assert.Equal(64, (XmlPropertyReader.value "sourcePdfSha256" root).Length)
        Assert.All(original, fun c -> Assert.Empty(c.Descendants(System.Xml.Linq.XName.Get "Point")))
        for c in curves do
            let points = c.Element(System.Xml.Linq.XName.Get "Points").Elements() |> Seq.toArray
            rawPoints <- rawPoints + points.Length
            Assert.Equal(int (XmlPropertyReader.value "pointCount" c), points.Length)
            Assert.NotEqual<string>("DynamicE", XmlPropertyReader.value "property" c)
            Assert.DoesNotContain(points, fun p -> XmlPropertyReader.value "temperatureLabel" p<>"")
            if XmlPropertyReader.value "constant" c="true" then
                Assert.All(points, fun p -> Assert.Equal("", XmlPropertyReader.value "temperature" p))
            let sourceFile = XmlPropertyReader.value "mappedSourceFile" c
            let sourceCurve = XmlPropertyReader.value "mappedSourceCurve" c
            if sourceCurve<>"" then
                let sourcePath = Path.Combine(folder, "..", sourceFile.Replace('/', Path.DirectorySeparatorChar))
                let source = System.Xml.Linq.XDocument.Load sourcePath
                let expected = source.Root.Element(System.Xml.Linq.XName.Get "Curves").Elements()
                               |> Seq.find (fun n -> XmlPropertyReader.value "id" n=sourceCurve)
                let coordinates (node: System.Xml.Linq.XElement) =
                    node.Element(System.Xml.Linq.XName.Get "Points").Elements()
                    |> Seq.map (fun p -> XmlPropertyReader.number (XmlPropertyReader.value "temperature" p) |> Option.get,
                                         XmlPropertyReader.number (XmlPropertyReader.value "value" p) |> Option.get)
                    |> Seq.toArray
                Assert.Equal<(float*float) array>(coordinates expected, coordinates c)
                Assert.Equal(XmlPropertyReader.value "referenceTemperature" expected, XmlPropertyReader.value "referenceTemperature" c)
    Assert.Equal(2147, rawPoints)
    Assert.True(sourceOnly>0)
    let read number = XmlPropertyReader.readFile(Path.Combine(folder, "CC-" + number + ".xml")) |> List.toArray
    let ni = read "2983"
    Assert.Equal(0.33, (ni |> Array.find (fun r -> r.PropertyCode="Nu")).Points[0].Y)
    Assert.DoesNotContain(ni, fun r -> r.PropertyCode="E")
    Assert.Contains(ni, fun r -> r.PropertyCode="Alpha" && r.Notes.Contains("referenceTemperature (degC): 25"))
    let duplex = read "2914"
    Assert.All(duplex |> Array.filter (fun r -> not r.IsConstant), fun r -> Assert.All(r.Points, fun p -> Assert.True(p.Temperature<=316.)))
    Assert.Contains(duplex, fun r -> r.PropertyCode="Alpha" && r.Notes.Contains("referenceTemperature (degC): 30"))
    let nickel = read "2684-5"
    Assert.DoesNotContain(nickel, fun r -> r.PropertyCode="Alpha")
    Assert.All(nickel |> Array.filter (fun r -> not r.IsConstant), fun r -> Assert.All(r.Points, fun p -> Assert.True(p.Temperature<=800.)))
    let sectionI, sectionVIII = read "3089", read "2982-2"
    Assert.Equal(550., (sectionI |> Array.find (fun r -> r.PropertyCode="E")).Points |> Array.last |> fun p -> p.Temperature)
    Assert.Equal(450., (sectionVIII |> Array.find (fun r -> r.PropertyCode="E")).Points |> Array.last |> fun p -> p.Temperature)
    let density = read "3007" |> Array.find (fun r -> r.PropertyCode="density")
    Assert.False(density.IsConstant)
    Assert.Equal(7670., density.Points[0].Y)
    Assert.Equal(7470., density.Points |> Array.last |> fun p -> p.Y)

[<Fact>]
let ``Code Case reviewed source records import into multiple explicitly matched materials`` () = workspace (fun _ path ->
    let folder = Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "database", "data", "physical-properties-xml", "CodeCases")
    let records = XmlPropertyReader.readFile(Path.Combine(folder, "CC-3007.xml")) |> List.toArray
    let create grade = Asme2025Crud.saveIdentity path {Id=0L;Specification="TEST-CC";Grade=grade;Condition="";UNS="";NominalComposition="9Cr-2Si-1Cu"} |> ok
    let ids = [|create "A"; create "B"|]
    let proposals, assignments =
        use db = Asme2025Data.openRead path
        XmlPropertyMatching.propose db records |> Array.filter (fun p -> ids |> Array.contains p.MaterialId),
        ids |> Array.collect (fun id -> records |> Array.map (fun record -> assignment db id record true false))
    Assert.Equal(12, proposals.Length)
    Assert.All(proposals, fun p -> Assert.Equal(85,p.Score))
    XmlPropertyImport.apply path assignments |> ok |> ignore
    use db = Asme2025Data.openRead path
    for id in ids do
        for record in records do
            let actual = Asme2025Data.curves db id record.PropertyCode |> List.exactlyOne
            Assert.Equal(record.Points.Length, actual.Points.Length)
            Assert.Equal(record.Points[0].Y, actual.Points[0].Value))

[<Fact>]
let ``compact PRD rejects incomplete invalid and duplicate material constants`` () =
    let file = Path.Combine(Path.GetTempPath(), "compact-prd-" + Guid.NewGuid().ToString("N") + ".xml")
    let write units rows =
        File.WriteAllText(file, $"<PhysicalPropertyTableImport schema=\"material-constants-v1\" {units}><Materials>{rows}</Materials></PhysicalPropertyTableImport>")
    let units = """densityUnit="kg/m3" poissonRatioUnit="dimensionless" """
    let row = """<Material id="test" name="Test" uns="S30400" density="8030" poissonRatio="0.31" />"""
    try
        write units row
        let records = XmlPropertyReader.readFile file
        Assert.Equal(2, records.Length)
        Assert.Equal<string list>([ "density"; "Nu" ], records |> List.map (fun r -> r.PropertyCode))
        Assert.Equal(file + "/prd-density-test", records.Head.Key)
        Assert.All(records, fun r -> Assert.Equal("S30400", r.UNS); Assert.False(r.RequiresReview))
        for invalid in [
            row.Replace(""" density="8030" """, " ")
            row.Replace("8030", "NaN")
            row.Replace("8030", "-1")
            row.Replace("0.31", "0.5")
            row.Replace("0.31", "0")
            row.Replace("""id="test" """, "")
            row + row
            ""
        ] do
            write units invalid
            Assert.ThrowsAny<Exception>(fun () -> XmlPropertyReader.readFile file |> ignore) |> ignore
        write """densityUnit="unknown" poissonRatioUnit="dimensionless" """ row
        Assert.Throws<FormatException>(fun () -> XmlPropertyReader.readFile file |> ignore) |> ignore
    finally
        File.Delete file

[<Fact>]
let ``compact PRD associates explicit UNS or nominal chemistry and leaves generic families manual`` () = workspace (fun folder path ->
    let file = Path.Combine(folder, "prd.xml")
    File.WriteAllText(file, """<PhysicalPropertyTableImport schema="material-constants-v1" densityUnit="kg/m3" poissonRatioUnit="dimensionless">
      <Notes>Review applicability.</Notes><Materials>
        <Material id="uns" name="UNS material" uns="S30400" sourceMaterial="Source label" sourcePdfPage="1244" density="8030" poissonRatio="0.31" />
        <Material id="chemistry" name="Chemistry material" nominalChemicalComposition="8Ni-18Cr" density="8030" poissonRatio="0.31" />
        <Material id="family" name="Carbon steels" association="manual" density="7750" poissonRatio="0.30" />
      </Materials></PhysicalPropertyTableImport>""")
    let records = XmlPropertyReader.readFile file |> List.toArray
    let id = Asme2025Crud.saveIdentity path {Id=0L;Specification="TEST-PRD";Grade="A";Condition="";UNS="S30400";NominalComposition="18Cr-8Ni"} |> ok
    use db = Asme2025Data.openRead path
    let proposals = XmlPropertyMatching.propose db records |> Array.filter (fun p -> p.MaterialId=id)
    Assert.Equal(4, proposals.Length)
    Assert.Equal(2, proposals |> Array.filter (fun p -> p.Score=95) |> Array.length)
    Assert.Equal(2, proposals |> Array.filter (fun p -> p.Score=85) |> Array.length)
    Assert.DoesNotContain(proposals, fun p -> p.RecordKey.Contains("family"))
    Assert.All(records |> Array.filter (fun r -> r.Key.Contains("family")), fun r ->
        Assert.True(r.RequiresReview)
        Assert.Contains("manuale", r.Diagnostic)
        Assert.True(XmlPropertyImport.validate r false |> Result.isError))
    Assert.All(records |> Array.filter (fun r -> r.UNS<>""), fun r ->
        Assert.Contains("Review applicability.", r.Notes)
        Assert.Contains("Source label", r.Notes)
        Assert.Contains("1244", r.Notes)))

[<Fact>]
let ``shared modulus curves preserve independent UNS composition and review qualifiers`` () = workspace (fun folder path ->
    let file = Path.Combine(folder, "modulus-curves.xml")
    File.WriteAllText(file, """<PhysicalPropertyTableImport schema="material-curves-v1"><Notes>Common notes</Notes><SourceNotes>Source applicability</SourceNotes>
      <Curves><Curve id="shared" property="E" unit="MPa" temperatureUnit="degC">
        <Materials>
          <Material id="uns" name="Explicit UNS" uns="S30400" sourceMaterial="UNS row" sourcePdfPage="1238" />
          <Material id="composition" name="Nominal composition" nominalChemicalComposition="8Ni-18Cr" sourceMaterial="Group G" sourcePdfPage="1238" />
          <Material id="family" name="Carbon steels" association="manual" />
          <Material id="aged" name="Aged material" uns="S45000" nominalChemicalComposition="15Cr-6Ni-Cu-Mo" specification="TEST-AGED" grade="D" condition="TEST-CONDITION" requiresReview="true" applicability="Aged at 480 degC" />
        </Materials>
        <Points><Point temperature="-200" value="216000" /><Point temperature="25" value="202000" /><Point temperature="100" value="198000" /></Points>
      </Curve></Curves></PhysicalPropertyTableImport>""")
    let records = XmlPropertyReader.readFile file |> List.toArray
    Assert.Equal(4, records.Length)
    Assert.All(records, fun r ->
        Assert.Equal("E",r.PropertyCode)
        Assert.Equal("MPa",r.Unit)
        Assert.Equal(3,r.Points.Length)
        Assert.Equal(-200.,r.Points[0].Temperature)
        Assert.Equal(202000.,r.Points[1].Y)
        Assert.False(r.IsConstant)
        Assert.Contains("Common notes",r.Notes)
        Assert.Contains("Source applicability",r.Notes)
        Assert.True(XmlPropertyImport.validate r true |> Result.isOk))
    let aged = records |> Array.find (fun r -> r.UNS="S45000")
    Assert.True(aged.RequiresReview)
    Assert.Equal("15Cr-6Ni-Cu-Mo",aged.Composition)
    Assert.Equal("TEST-AGED",aged.Specification)
    Assert.Equal("D",aged.Grade)
    Assert.Equal("TEST-CONDITION",aged.Condition)
    Assert.Contains("Aged at 480 degC",aged.Notes)
    Assert.True(XmlPropertyImport.validate aged false |> Result.isError)
    let id = Asme2025Crud.saveIdentity path {Id=0L;Specification="TEST-SHARED-E";Grade="A";Condition="";UNS="S30400";NominalComposition="18Cr-8Ni"} |> ok
    use db = Asme2025Data.openRead path
    let proposals = XmlPropertyMatching.propose db records |> Array.filter (fun p -> p.MaterialId=id)
    Assert.Equal(2,proposals.Length)
    Assert.All(proposals,fun p -> Assert.Contains("più record",p.Reason))
    Assert.DoesNotContain(proposals,fun p -> p.RecordKey.EndsWith("/family"))
    Assert.Contains("Source material: UNS row;",records[0].Notes))

[<Fact>]
let ``shared modulus curves reject malformed coordinates and duplicate identities`` () =
    let file = Path.Combine(Path.GetTempPath(),"shared-e-"+Guid.NewGuid().ToString("N")+".xml")
    let row = """<Material id="one" name="Test" uns="S30400" />"""
    let points = """<Point temperature="-200" value="216000" /><Point temperature="25" value="202000" />"""
    let write rows coordinates attributes =
        File.WriteAllText(file,$"<PhysicalPropertyTableImport schema=\"material-curves-v1\"><Curves><Curve id=\"test\" {attributes}><Materials>{rows}</Materials><Points>{coordinates}</Points></Curve></Curves></PhysicalPropertyTableImport>")
    let attributes = """property="E" unit="MPa" temperatureUnit="degC" """
    try
        for invalid in [
            points.Replace("-200","NaN")
            points.Replace("-200","-274")
            points.Replace("202000","0")
            points.Replace("202000","-1")
            points.Replace("202000","Infinity")
            points.Replace("""temperature="25" ""","""temperature="-200" """)
            points.Replace("""temperature="25" ""","""temperature="-201" """)
            """<Point temperature="25" />"""
            ""
        ] do
            write row invalid attributes
            Assert.Throws<FormatException>(fun () -> XmlPropertyReader.readFile file |> ignore) |> ignore
        for invalidRow in [" "; row+row; row.Replace("""id="one" ""","")] do
            write invalidRow points attributes
            Assert.ThrowsAny<Exception>(fun () -> XmlPropertyReader.readFile file |> ignore) |> ignore
        write row points (attributes.Replace("MPa","GPa"))
        Assert.Throws<FormatException>(fun () -> XmlPropertyReader.readFile file |> ignore) |> ignore
    finally
        File.Delete file

[<Fact>]
let ``modulus nominal composition normalizes exact fractions without inferring missing amounts`` () =
    Assert.Equal(XmlPropertyMatching.compositionKey "C-½Mo", XmlPropertyMatching.compositionKey "0.50Mo-C")
    Assert.Equal(XmlPropertyMatching.compositionKey "1¼Cr-½Mo", XmlPropertyMatching.compositionKey "0.5Mo-1.25Cr")
    Assert.Equal(XmlPropertyMatching.compositionKey "1¾Ni-¾Cr-¼Mo", XmlPropertyMatching.compositionKey "0.25Mo-0.75Cr-1.75Ni")
    Assert.NotEqual<string>(XmlPropertyMatching.compositionKey "9Cr-Mo", XmlPropertyMatching.compositionKey "9Cr-1Mo")
    Assert.NotEqual<string>(XmlPropertyMatching.compositionKey "½Cr-⅕Mo", XmlPropertyMatching.compositionKey "½Cr-?Mo")

[<Fact>]
let ``ASME 2025 modulus XML contains all shared curves and source material points`` () =
    let folder = Path.Combine(__SOURCE_DIRECTORY__,"..","..","database","data","physical-properties-xml","ElasticModulus")
    let counts = [ "TM-1",17,104; "TM-2",4,18; "TM-3",11,47; "TM-4",23,36; "TM-5",5,15 ]
    let mutable totalPoints = 0
    for table,curveCount,materialCount in counts do
        let file = Path.Combine(folder,table+".xml")
        let document = System.Xml.Linq.XDocument.Load file
        let curves = document.Descendants(System.Xml.Linq.XName.Get "Curve") |> Seq.toArray
        let records = XmlPropertyReader.readFile file |> List.toArray
        Assert.Equal(curveCount,curves.Length)
        Assert.Equal(materialCount,records.Length)
        Assert.Equal(materialCount,records |> Array.map (fun r -> r.Key) |> Array.distinct |> Array.length)
        Assert.Empty(document.Descendants(System.Xml.Linq.XName.Get "RawText"))
        for curve in curves do
            let curveId = XmlPropertyReader.value "id" curve
            let points = curve.Descendants(System.Xml.Linq.XName.Get "Point") |> Seq.toArray
            totalPoints <- totalPoints+points.Length
            for material in curve.Descendants(System.Xml.Linq.XName.Get "Material") do
                let id = XmlPropertyReader.value "id" material
                let record = records |> Array.find (fun r -> r.Key.EndsWith("/"+curveId+"/"+id))
                Assert.Equal(XmlPropertyReader.value "uns" material,record.UNS)
                Assert.Equal(XmlPropertyReader.value "nominalChemicalComposition" material,record.Composition)
                Assert.Equal(points.Length,record.Points.Length)
                Assert.True(XmlPropertyImport.validate record true |> Result.isOk)
                for i in 0..points.Length-1 do
                    Assert.Equal(XmlPropertyReader.number (XmlPropertyReader.value "temperature" points[i]) |> Option.get,record.Points[i].Temperature)
                    Assert.Equal(XmlPropertyReader.number (XmlPropertyReader.value "value" points[i]) |> Option.get,record.Points[i].Y)
        if table="TM-1" then
            Assert.Equal(3,records |> Array.filter (fun r -> r.UNS="" && r.Composition="") |> Array.length)
            Assert.True((records |> Array.find (fun r -> r.UNS="S45000")).RequiresReview)
            Assert.Contains(records,fun r -> r.Composition="18Cr-8Ni")
        if table="TM-5" then
            Assert.Equal(6,(records |> Array.find (fun r -> r.UNS="R56320")).Points.Length)
            Assert.Contains(records,fun r -> r.UNS="R60705")
    Assert.Equal(801,totalPoints)

[<Fact>]
let ``one shared modulus record can import into multiple target materials`` () = workspace (fun folder path ->
    let file = Path.Combine(folder,"shared.xml")
    File.WriteAllText(file,"""<PhysicalPropertyTableImport schema="material-curves-v1"><Curves><Curve id="one" property="E" unit="MPa" temperatureUnit="degC">
      <Materials><Material id="composition" name="Group G member" nominalChemicalComposition="18Cr-8Ni" /></Materials>
      <Points><Point temperature="25" value="195000" /><Point temperature="100" value="191000" /></Points>
      </Curve></Curves></PhysicalPropertyTableImport>""")
    let record = XmlPropertyReader.readFile file |> List.exactlyOne
    let ids = [
        Asme2025Crud.saveIdentity path {Id=0L;Specification="TEST-SHARED-ONE";Grade="A";Condition="";UNS="S30400";NominalComposition="18Cr-8Ni"} |> ok
        Asme2025Crud.saveIdentity path {Id=0L;Specification="TEST-SHARED-TWO";Grade="B";Condition="";UNS="S30409";NominalComposition="18Cr-8Ni"} |> ok
    ]
    use db = Asme2025Data.openRead path
    let proposals = XmlPropertyMatching.propose db [|record|] |> Array.filter (fun p -> List.contains p.MaterialId ids)
    Assert.Equal(2,proposals.Length)
    XmlPropertyImport.apply path (ids |> List.map (fun id -> assignment db id record true false) |> List.toArray) |> ok |> ignore
    for id in ids do
        let curve = Asme2025Data.curves db id "E" |> List.exactlyOne
        Assert.Equal(195000.,curve.Points[0].Value)
        Assert.Equal(191000.,curve.Points[1].Value))

[<Fact>]
let ``conductivity shared curves associate multiple materials and enforce per-material limits`` () = workspace (fun folder path ->
    let file = Path.Combine(folder,"conductivity.xml")
    File.WriteAllText(file,"""<PhysicalPropertyTableImport schema="material-curves-v1"><Notes>Source uncertainty +/-10%.</Notes><Curves>
      <Curve id="shared" property="k" unit="W/(m*K)" temperatureUnit="degC"><Materials>
        <Material id="composition" name="Group J" nominalChemicalComposition="18Cr-8Ni" />
        <Material id="limited" name="Limited member" uns="S17400" maxTemperature="425" applicability="Only to 425 degC" />
      </Materials><Points>
        <Point temperature="20" value="14.8" /><Point temperature="425" value="22.0" /><Point temperature="450" value="23.0" />
      </Points></Curve></Curves></PhysicalPropertyTableImport>""")
    let records = XmlPropertyReader.readFile file |> List.toArray
    let record = records |> Array.find (fun r -> r.Composition<>"")
    let limited = records |> Array.find (fun r -> r.UNS="S17400")
    Assert.Equal(3,record.Points.Length)
    Assert.Equal(2,limited.Points.Length)
    Assert.Equal(425.,limited.Points[1].Temperature)
    Assert.Contains("Only to 425 degC",limited.Notes)
    Assert.Contains("maxTemperature (degC): 425",limited.Notes)
    Assert.Contains("450",limited.Xml)
    Assert.Equal("k",record.PropertyCode)
    Assert.Equal("W/(m*K)",record.Unit)
    let ids = [
        Asme2025Crud.saveIdentity path {Id=0L;Specification="TEST-K-ONE";Grade="A";Condition="";UNS="S30400";NominalComposition="18Cr-8Ni"} |> ok
        Asme2025Crud.saveIdentity path {Id=0L;Specification="TEST-K-TWO";Grade="B";Condition="";UNS="S30409";NominalComposition="18Cr-8Ni"} |> ok
    ]
    use db = Asme2025Data.openRead path
    let proposals = XmlPropertyMatching.propose db [|record|] |> Array.filter (fun p -> List.contains p.MaterialId ids)
    Assert.Equal(2,proposals.Length)
    XmlPropertyImport.apply path (ids |> List.map (fun id -> assignment db id record true false) |> List.toArray) |> ok |> ignore
    for id in ids do
        let curve = Asme2025Data.curves db id "k" |> List.exactlyOne
        Assert.Equal(3,curve.Points.Length)
        Assert.Equal(14.8,curve.Points[0].Value))

[<Fact>]
let ``conductivity rejects invalid units coordinates and applicability limits`` () =
    let file = Path.Combine(Path.GetTempPath(),"invalid-k-"+Guid.NewGuid().ToString("N")+".xml")
    let original = """<PhysicalPropertyTableImport schema="material-curves-v1"><Curves><Curve id="k" property="k" unit="W/(m*K)" temperatureUnit="degC">
      <Materials><Material id="member" name="Material" uns="N02200" maxTemperature="425" /></Materials>
      <Points><Point temperature="20" value="90.0" /><Point temperature="450" value="65.0" /></Points>
      </Curve></Curves></PhysicalPropertyTableImport>"""
    try
        for invalid in [
            original.Replace("425","NaN")
            original.Replace("425","-274")
            original.Replace("425","0")
            original.Replace("W/(m*K)","MPa")
            original.Replace("90.0","0")
            original.Replace("90.0","-1")
            original.Replace("90.0","NaN")
            original.Replace("""temperature="450" ""","""temperature="20" """)
        ] do
            File.WriteAllText(file,invalid)
            Assert.Throws<FormatException>(fun () -> XmlPropertyReader.readFile file |> ignore) |> ignore
    finally
        File.Delete file

[<Fact>]
let ``ASME conductivity XML preserves every shared point and explicit material identity`` () =
    let file = Path.Combine(__SOURCE_DIRECTORY__,"..","..","database","data","physical-properties-xml","ThermalConductivity","TCD-ThermalConductivity.xml")
    let root = System.Xml.Linq.XDocument.Load(file).Root
    let curves = root.Descendants(System.Xml.Linq.XName.Get "Curve") |> Seq.toArray
    let records = XmlPropertyReader.readFile file |> List.toArray
    Assert.Equal("material-curves-v1",XmlPropertyReader.value "schema" root)
    Assert.Equal("65",XmlPropertyReader.value "sourceColumnCount" root)
    Assert.Equal("1390",XmlPropertyReader.value "sourcePointCount" root)
    Assert.Equal(63,curves.Length)
    Assert.Equal(178,records.Length)
    Assert.Equal(records.Length,records |> Array.map (fun r -> r.Key) |> Array.distinct |> Array.length)
    Assert.Empty(root.Descendants(System.Xml.Linq.XName.Get "RawText"))
    Assert.Empty(root.Descendants(System.Xml.Linq.XName.Get "RawTokens"))
    Assert.Equal(1361,root.Descendants(System.Xml.Linq.XName.Get "Point") |> Seq.length)
    let mutable limitedCount = 0
    for curve in curves do
        let curveId = XmlPropertyReader.value "id" curve
        let points = curve.Descendants(System.Xml.Linq.XName.Get "Point") |> Seq.toArray
        for material in curve.Descendants(System.Xml.Linq.XName.Get "Material") do
            let key = "/"+curveId+"/"+XmlPropertyReader.value "id" material
            let record = records |> Array.find (fun r -> r.Key.EndsWith key)
            let maximum = XmlPropertyReader.number (XmlPropertyReader.value "maxTemperature" material)
            let applicable = points |> Array.filter (fun p -> maximum |> Option.forall (fun limit ->
                (XmlPropertyReader.number (XmlPropertyReader.value "temperature" p) |> Option.get)<=limit))
            if maximum.IsSome then
                limitedCount <- limitedCount+1
                Assert.All(record.Points,fun p -> Assert.True(p.Temperature<=425.))
                Assert.Contains("425",record.Notes)
            Assert.Equal("k",record.PropertyCode)
            Assert.Equal("W/(m*K)",record.Unit)
            Assert.Equal(XmlPropertyReader.value "uns" material,record.UNS)
            Assert.Equal(XmlPropertyReader.value "nominalChemicalComposition" material,record.Composition)
            Assert.Equal(applicable.Length,record.Points.Length)
            Assert.True(XmlPropertyImport.validate record true |> Result.isOk)
            for i in 0..applicable.Length-1 do
                Assert.Equal(XmlPropertyReader.number (XmlPropertyReader.value "temperature" applicable[i]) |> Option.get,record.Points[i].Temperature)
                Assert.Equal(XmlPropertyReader.number (XmlPropertyReader.value "value" applicable[i]) |> Option.get,record.Points[i].Y)
    Assert.Equal(4,limitedCount)
    Assert.Single(records |> Array.filter (fun r -> r.RequiresReview)) |> ignore
    Assert.Contains(records,fun r -> r.Composition="Carbon steel" && not r.RequiresReview)
    Assert.Contains(records,fun r -> r.Composition="18Cr-8Ni-Se")
    let titanium = records |> Array.find (fun r -> r.UNS="R54250")
    Assert.Contains("1244",titanium.Xml)
    Assert.Contains("TM-5",titanium.Xml)
    Assert.Equal(7.4,titanium.Points[0].Y)

[<Fact>]
let ``ASME expansion XML preserves separate instantaneous mean and linear quantities`` () =
    let folder = Path.Combine(__SOURCE_DIRECTORY__,"..","..","database","data","physical-properties-xml","ThermalExpansion")
    let counts = [1,30,210;2,2,36;3,10,28;4,60,74;5,6,26]
    for table,curveCount,recordCount in counts do
        let file = Path.Combine(folder,$"TE-{table}.xml")
        let root = System.Xml.Linq.XDocument.Load(file).Root
        let curves = root.Descendants(System.Xml.Linq.XName.Get "Curve") |> Seq.toArray
        let records = XmlPropertyReader.readFile file |> List.toArray
        Assert.Equal(curveCount,curves.Length)
        Assert.Equal(recordCount,records.Length)
        Assert.Equal("20",XmlPropertyReader.value "referenceTemperature" root)
        Assert.Empty(root.Descendants(System.Xml.Linq.XName.Get "RawText"))
        Assert.Equal(recordCount/2,records |> Array.filter (fun r -> r.PropertyCode="Alpha") |> Array.length)
        Assert.Equal(recordCount/2,records |> Array.filter (fun r -> r.PropertyCode="AlphaInstantaneous") |> Array.length)
        for curve in curves do
            let id = XmlPropertyReader.value "id" curve
            let linearRef = XmlPropertyReader.value "linearExpansionRef" curve
            Assert.Contains(root.Descendants(System.Xml.Linq.XName.Get "SourceLinearExpansion"),fun n -> XmlPropertyReader.value "id" n=linearRef)
            let points = curve.Descendants(System.Xml.Linq.XName.Get "Point") |> Seq.toArray
            for material in curve.Descendants(System.Xml.Linq.XName.Get "Material") do
                let materialId = XmlPropertyReader.value "id" material
                let record = records |> Array.find (fun r -> r.Key.EndsWith("/"+id+"/"+materialId))
                Assert.Equal(XmlPropertyReader.value "uns" material,record.UNS)
                Assert.Equal(XmlPropertyReader.value "nominalChemicalComposition" material,record.Composition)
                Assert.Equal("1/degC",record.Unit)
                Assert.Contains("referenceTemperature (degC): 20",record.Notes)
                Assert.True(XmlPropertyImport.validate record true |> Result.isOk)
                Assert.Equal(points.Length,record.Points.Length)
                Assert.All(record.Points,fun p -> Assert.True(p.Y>0. && p.Y<0.001))
                for i in 0..points.Length-1 do
                    Assert.Equal(XmlPropertyReader.number (XmlPropertyReader.value "temperature" points[i]) |> Option.get,record.Points[i].Temperature)
                    Assert.Equal(XmlPropertyReader.number (XmlPropertyReader.value "value" points[i]) |> Option.get,record.Points[i].Y)
        if table=1 then
            Assert.Contains(records,fun r -> r.Composition="Carbon steel")
            Assert.Equal(6,records |> Array.filter (fun r -> r.RequiresReview) |> Array.length)
        if table=3 then
            Assert.Contains(records,fun r -> r.UNS="C97600" && not r.RequiresReview)
            Assert.Contains(records,fun r -> r.Description="C1XXXX family" && r.RequiresReview)
        if table=5 then Assert.Contains(records,fun r -> r.UNS="R54250")

[<Fact>]
let ``expansion shared curves require reference temperature and import distinct families into multiple materials`` () = workspace (fun folder path ->
    let file = Path.Combine(folder,"expansion.xml")
    let original = """<PhysicalPropertyTableImport schema="material-curves-v1"><Notes>Mean from 20 degC.</Notes><Curves>
      <Curve id="mean" property="Alpha" unit="1/degC" temperatureUnit="degC" referenceTemperature="20"><Materials>
        <Material id="composition" name="Group member" nominalChemicalComposition="18Cr-8Ni" /></Materials>
        <Points><Point temperature="20" value="0.0000160" /><Point temperature="100" value="0.0000165" /></Points>
      </Curve><Curve id="instant" property="AlphaInstantaneous" unit="1/degC" temperatureUnit="degC" referenceTemperature="20"><Materials>
        <Material id="composition" name="Group member" nominalChemicalComposition="18Cr-8Ni" /></Materials>
        <Points><Point temperature="20" value="0.0000160" /><Point temperature="100" value="0.0000170" /></Points>
      </Curve></Curves></PhysicalPropertyTableImport>"""
    for invalid in [original.Replace(" referenceTemperature=\"20\"","");original.Replace("referenceTemperature=\"20\"","referenceTemperature=\"NaN\"")] do
        File.WriteAllText(file,invalid)
        Assert.Throws<FormatException>(fun () -> XmlPropertyReader.readFile file |> ignore) |> ignore
    File.WriteAllText(file,original)
    let records = XmlPropertyReader.readFile file |> List.toArray
    Assert.Equal(2,records.Length)
    let ids = [
        Asme2025Crud.saveIdentity path {Id=0L;Specification="TEST-ALPHA-ONE";Grade="A";Condition="";UNS="S30400";NominalComposition="18Cr-8Ni"} |> ok
        Asme2025Crud.saveIdentity path {Id=0L;Specification="TEST-ALPHA-TWO";Grade="B";Condition="";UNS="S30409";NominalComposition="18Cr-8Ni"} |> ok
    ]
    use db = Asme2025Data.openRead path
    let proposals = XmlPropertyMatching.propose db records |> Array.filter (fun p -> List.contains p.MaterialId ids)
    Assert.Equal(4,proposals.Length)
    let assignments = [|for id in ids do for record in records do yield assignment db id record true false|]
    XmlPropertyImport.apply path assignments |> ok |> ignore
    for id in ids do
        let mean = Asme2025Data.curves db id "Alpha" |> List.exactlyOne
        let instant = Asme2025Data.curves db id "AlphaInstantaneous" |> List.exactlyOne
        Assert.Equal(0.0000165,mean.Points[1].Value)
        Assert.Equal(0.0000170,instant.Points[1].Value))

[<Fact>]
let ``ASME diffusivity preserves SI points identities limits and explicitly missing datasets`` () =
    let file = Path.Combine(__SOURCE_DIRECTORY__,"..","..","database","data","physical-properties-xml","ThermalDiffusivity","TCD-ThermalDiffusivity.xml")
    let root = System.Xml.Linq.XDocument.Load(file).Root
    let curves = root.Descendants(System.Xml.Linq.XName.Get "Curve") |> Seq.toArray
    let records = XmlPropertyReader.readFile file |> List.toArray
    Assert.Equal(58,curves.Length)
    Assert.Equal(172,records.Length)
    Assert.Equal("1154",XmlPropertyReader.value "sourcePointCount" root)
    Assert.Equal(1146,root.Descendants(System.Xml.Linq.XName.Get "Point") |> Seq.length)
    Assert.Empty(root.Descendants(System.Xml.Linq.XName.Get "RawText"))
    Assert.Equal(records.Length,records |> Array.map (fun r -> r.Key) |> Array.distinct |> Array.length)
    for curve in curves do
        let curveId = XmlPropertyReader.value "id" curve
        let points = curve.Descendants(System.Xml.Linq.XName.Get "Point") |> Seq.toArray
        for material in curve.Descendants(System.Xml.Linq.XName.Get "Material") do
            let key = "/"+curveId+"/"+XmlPropertyReader.value "id" material
            let record = records |> Array.find (fun r -> r.Key.EndsWith key)
            let maximum = XmlPropertyReader.number (XmlPropertyReader.value "maxTemperature" material)
            let applicable = points |> Array.filter (fun p -> maximum |> Option.forall (fun limit ->
                (XmlPropertyReader.number (XmlPropertyReader.value "temperature" p) |> Option.get)<=limit))
            Assert.Equal("a",record.PropertyCode)
            Assert.Equal("m2/s",record.Unit)
            Assert.Equal(XmlPropertyReader.value "uns" material,record.UNS)
            Assert.Equal(XmlPropertyReader.value "nominalChemicalComposition" material,record.Composition)
            Assert.True(XmlPropertyImport.validate record true |> Result.isOk)
            Assert.Equal(applicable.Length,record.Points.Length)
            for i in 0..applicable.Length-1 do
                Assert.Equal(XmlPropertyReader.number (XmlPropertyReader.value "temperature" applicable[i]) |> Option.get,record.Points[i].Temperature)
                Assert.Equal(XmlPropertyReader.number (XmlPropertyReader.value "value" applicable[i]) |> Option.get,record.Points[i].Y)
    let unavailable = root.Element(System.Xml.Linq.XName.Get "UnavailableMaterials").Elements() |> Seq.toArray
    Assert.Equal(6,unavailable.Length)
    for uns in ["N02200";"N02201";"N04405";"N06022";"N06030";"N10276"] do
        Assert.Contains(unavailable,fun m -> XmlPropertyReader.value "uns" m=uns)
        Assert.DoesNotContain(records,fun r -> r.UNS=uns)
    let carbon = records |> Array.find (fun r -> r.Composition="Carbon steel")
    Assert.Equal(0.00001810,carbon.Points[0].Y)
    Assert.Single(records |> Array.filter (fun r -> r.RequiresReview)) |> ignore
    Assert.Equal(4,records |> Array.filter (fun r -> r.Notes.Contains("maxTemperature (degC): 425")) |> Array.length)

[<Fact>]
let ``diffusivity imports shared SI curves to multiple materials and rejects wrong units`` () = workspace (fun folder path ->
    let file = Path.Combine(folder,"diffusivity.xml")
    let original = """<PhysicalPropertyTableImport schema="material-curves-v1"><Curves><Curve id="a" property="a" unit="m2/s" temperatureUnit="degC">
      <Materials><Material id="member" name="Group" nominalChemicalComposition="18Cr-8Ni" maxTemperature="425" /></Materials>
      <Points><Point temperature="20" value="0.00000390" /><Point temperature="425" value="0.000004" /><Point temperature="450" value="0.0000041" /></Points>
      </Curve></Curves></PhysicalPropertyTableImport>"""
    for invalid in [original.Replace("m2/s","mm2/s");original.Replace("0.00000390","0");original.Replace("0.00000390","NaN")] do
        File.WriteAllText(file,invalid)
        Assert.Throws<FormatException>(fun () -> XmlPropertyReader.readFile file |> ignore) |> ignore
    File.WriteAllText(file,original)
    let record = XmlPropertyReader.readFile file |> List.exactlyOne
    Assert.Equal(2,record.Points.Length)
    let ids = [
        Asme2025Crud.saveIdentity path {Id=0L;Specification="TEST-TD-ONE";Grade="A";Condition="";UNS="S30400";NominalComposition="18Cr-8Ni"} |> ok
        Asme2025Crud.saveIdentity path {Id=0L;Specification="TEST-TD-TWO";Grade="B";Condition="";UNS="S30409";NominalComposition="18Cr-8Ni"} |> ok
    ]
    use db = Asme2025Data.openRead path
    Assert.Equal(2,XmlPropertyMatching.propose db [|record|] |> Array.filter (fun p -> List.contains p.MaterialId ids) |> Array.length)
    XmlPropertyImport.apply path (ids |> List.map (fun id -> assignment db id record true false) |> List.toArray) |> ok |> ignore
    for id in ids do
        let curve = Asme2025Data.curves db id "a" |> List.exactlyOne
        Assert.Equal(2,curve.Points.Length)
        Assert.Equal(0.00000390,curve.Points[0].Value))

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
let ``existing staged XML extracts raw curves and structured PRD as independent records`` () =
    let folder=Path.Combine(__SOURCE_DIRECTORY__,"../../database/data/physical-properties-xml")
    let records,errors=XmlPropertyReader.readFolder folder
    Assert.Empty(errors)
    Assert.Contains(records,fun r -> r.PropertyCode="Alpha" && r.Points.Length>10)
    Assert.Contains(records,fun r -> r.PropertyCode="AlphaInstantaneous" && r.Points.Length>10)
    Assert.Contains(records,fun r -> r.PropertyCode="E" && r.Points |> Array.exists (fun p -> p.Y>100000.))
    Assert.Contains(records,fun r -> r.PropertyCode="density" && r.IsConstant && r.Points[0].Y=7750.)
    Assert.Contains(records,fun r -> r.PropertyCode="Nu" && r.IsConstant && r.Points[0].Y=0.30)
    Assert.All(records |> Array.filter (fun r -> r.Points.Length>0 && r.RawText<>""),fun r -> Assert.True(r.RequiresReview))
    Assert.All(records |> Array.filter (fun r -> r.PropertyCode="density" || r.PropertyCode="Nu"),fun r ->
        let codeCase = XmlPropertyReader.value "codeCase" (System.Xml.Linq.XDocument.Parse(r.Xml).Root)
        Assert.Equal(codeCase<>"" || (String.IsNullOrWhiteSpace r.UNS && String.IsNullOrWhiteSpace r.Composition), r.RequiresReview)
        Assert.True(XmlPropertyImport.validate r true |> Result.isOk))
    Assert.True(XmlPropertyImport.validate (records |> Array.find (fun r -> r.PropertyCode="Alpha" && r.RequiresReview)) false |> Result.isError)

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
    let folder=Path.Combine(__SOURCE_DIRECTORY__,"../../database/data/external-pressure-xml")
    let records,errors=XmlPropertyReader.readFolder folder
    Assert.Empty(errors)
    let charts=records |> Array.filter (fun r -> r.PropertyCode="EP")
    Assert.NotEmpty(charts)
    Assert.All(charts,fun r -> Assert.Empty(r.Points); Assert.True(XmlPropertyImport.validate r true |> Result.isError))
