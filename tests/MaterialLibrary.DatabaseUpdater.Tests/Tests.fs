module MaterialLibrary.DatabaseUpdater.Tests

open System
open System.IO
open System.IO.Compression
open System.Text
open System.Text.Json
open Xunit
open Microsoft.Data.Sqlite
open MaterialLibrary.DatabaseUpdater
open MaterialLibrary.DatabaseUpdater.Domain

/// <summary>Remove each isolated fixture workspace after its test.</summary>
type Workspace() =
    let root = Path.Combine(Path.GetTempPath(), "materials-updater-tests-" + Guid.NewGuid().ToString("N"))
    do Directory.CreateDirectory(root) |> ignore
    member _.Root = root
    member _.Path name = Path.Combine(root,name)
    interface IDisposable with member _.Dispose() = Directory.Delete(root,true)

/// <summary>Generate all six required table exports, with independent per-table note definitions.</summary>
let archive path changed =
    use zip = ZipFile.Open(path,ZipArchiveMode.Create)
    let add name text =
        let entry = zip.CreateEntry name
        use stream = entry.Open()
        use writer = new StreamWriter(stream,UTF8Encoding(false))
        writer.Write(text: string)
    let quote (value: string) = "\"" + value.Replace("\"","\"\"") + "\""
    let header = ["";"Revision";"Nominal Composition";"Product Form";"Spec. No.";"Type/Grade";"Alloy Desig./UNS No.";"Class/Condition/Temper";"Size/Thickness, mm";"Min. Tensile Strength, MPa";"Min. Yield Strength, MPa";"Notes";"<=40 °C, MPa";"<=100 °C, MPa";"Page No.";"Line No."]
    for table in ["Sy";"Su";"1A";"1B";"5A";"5B"] do
        let source = match table with "Sy" -> "Y-1" | "Su" -> "U" | _ -> table
        let materials =
            match table with
            | "Sy" -> ["A";"C";"E"]
            | "Su" -> ["A";"D";"E"]
            | "1A" -> if changed then ["A"] else ["A";"B"]
            | "1B" -> if changed then ["C";"Z"] else ["C"]
            | "5A" -> ["D"]
            | _ -> ["E"]
        let data = materials |> List.mapi (fun i material ->
            let stress = if changed && table="1A" && material="A" then "81.5" else "80"
            [string (i+1);"(25)";"Carbon steel";"Plate";"SA-" + material;"70";"…";"N";"t <= 50";"400";"200";"G1";stress;"75";"1";string (i+1)] |> List.map quote |> String.concat ",")
        let blank = ["99";"(25)";"…";"…";"…";"…";"…";"…";"…";"…";"…";"…";"…";"…";"1";"99"] |> List.map quote |> String.concat ","
        let rows = if table="1A" then data @ [blank] else data
        add (table+"/Exported_data (1).csv") ("\"Subscription: 2025 Metric\"\n\"Table: Table " + source + "\"\n\n" + (header |> List.map quote |> String.concat ",") + "\n" + String.concat "\n" rows + "\n")
        add (table+"/notes.txt") ("GENERAL NOTES\n(a)\tGeneral " + table + "\nNOTES - GENERAL REQUIREMENTS\nG1\tSpecific " + table + "\ncontinued line\n")

/// <summary>Build a fixture edition using the public CLI workflow.</summary>
let import zip output reports baseline =
    let args = ["--input";zip;"--output";output;"--edition";"2025";"--report-dir";reports] @ (baseline |> Option.map (fun p -> ["--baseline";p]) |> Option.defaultValue [])
    Program.run (List.toArray args) |> ignore

[<Fact>]
let ``CSV handles Excel wrappers commas escaped quotes and multiline fields`` () =
    let rows = Input.parseCsv "a,b,c\r\n=\"SA–516\",\"55,2\",\"a \"\"quote\"\"\nnext\"\r\n"
    Assert.Equal<string list>(["a";"b";"c"],snd rows[0])
    Assert.Equal<string list>(["SA–516";"55,2";"a \"quote\"\nnext"],snd rows[1])
    Assert.Equal(2,fst rows[1])
    Assert.ThrowsAny<Exception>(fun () -> Input.parseCsv "\"unterminated" |> ignore) |> ignore
    Assert.ThrowsAny<Exception>(fun () -> Input.parseCsv "\"closed\"garbage" |> ignore) |> ignore

[<Theory>]
[<InlineData("1.25",1.25)>]
[<InlineData("55,2",55.2)>]
[<InlineData("0",0.)>]
let ``numbers preserve zero and accept unambiguous decimal comma`` text expected =
    Assert.Equal(Some expected,number text)
    Assert.Equal(None,number "NaN")
    Assert.Equal(None,number "1,234.5")

[<Fact>]
let ``Windows export encoding and table scoped multiline notes are retained`` () =
    Assert.Equal("°C –", Input.decode [|0xB0uy;0x43uy;0x20uy;0x96uy|])
    let notes = Input.parseNotes "1A" "GENERAL NOTES\n(25)\t(a)\tGeneral\nNOTES – GENERAL REQUIREMENTS\n(25)\tG5\tBody\ncontinuation\n"
    Assert.Equal(2,notes.Length)
    Assert.Equal("General",notes[0].Kind)
    Assert.Equal("Body\ncontinuation",notes[1].Text)
    Assert.Equal("G5",notes[1].Code)

[<Theory>]
[<InlineData("6.30 <= t < 12.50",6.3,true,12.5,false)>]
[<InlineData("40.00 < t <= 80.00",40.,false,80.,true)>]
[<InlineData("0.15–12.67",0.15,true,12.67,true)>]
[<InlineData("t = 5",5.,true,5.,true)>]
let ``size parsing preserves boundaries`` text lo li hi ui =
    let size = parseSize text |> Result.defaultWith failwith
    Assert.Equal(Some lo,size.Minimum)
    Assert.Equal(Some hi,size.Maximum)
    Assert.Equal(li,size.MinimumIncluded)
    Assert.Equal(ui,size.MaximumIncluded)

[<Fact>]
let ``coverage unions adjacent bands but preserves open gaps and dimension axes`` () =
    let band text = parseSize text |> Result.defaultWith failwith
    Assert.True(Reports.covers (band "t <= 100") [band "t <= 50";band "50 < t <= 100"])
    Assert.False(Reports.covers (band "t <= 100") [band "t < 50";band "50 < t <= 100"])
    Assert.False(Reports.covers (band "t <= 100") [band "O.D. <= 100"])
    Assert.True(Reports.covers (band "DN <= 100") [band "All"])
    Assert.True(Result.isError (parseSize "50 < t < 50"))
    Assert.True(Result.isError (parseSize "not a restriction"))

[<Fact>]
let ``complete import preserves notes provenance and all missing strength categories`` () =
    use work = new Workspace()
    let zip,output,reports = work.Path "source.zip",work.Path "2025.db",work.Path "reports"
    archive zip false
    let input = Input.readArchive zip "2025"
    Assert.Single(input.Omitted) |> ignore
    Assert.Equal(11,input.Rows.Length)
    import zip output reports None
    use db = Database.connect output SqliteOpenMode.ReadWrite
    let count sql = Database.query db sql |> List.head |> Array.head |> Convert.ToInt32
    Assert.Equal(5,count "SELECT count(*) FROM Main")
    Assert.Equal(6,count "SELECT count(*) FROM S1")
    Assert.Equal(4,count "SELECT count(*) FROM S2")
    Assert.Equal(12,count "SELECT count(*) FROM Source_Notes")
    Assert.Equal(1,count "SELECT count(*) FROM Import_Omitted_Rows")
    Assert.Equal(3,count "SELECT count(*) FROM VW_Missing_Strength")
    Assert.Equal(1,count "SELECT count(*) FROM VW_Missing_Strength WHERE NOT HasSy AND HasSu")
    Assert.Equal(1,count "SELECT count(*) FROM VW_Missing_Strength WHERE HasSy AND NOT HasSu")
    Assert.Equal(1,count "SELECT count(*) FROM VW_Missing_Strength WHERE NOT HasSy AND NOT HasSu")
    Assert.Equal(1,count "SELECT count(*) FROM Main WHERE Specification='SA-B' AND SMYS=200")
    Assert.Equal(2,count "SELECT count(*) FROM VW_Property_Notes_All n JOIN Property_Source s ON s.id_property=n.id_property WHERE s.SourceTable='5A'")
    let text = Database.query db "SELECT NoteText FROM Source_Notes WHERE SourceTable='1A' AND Code='G1'" |> List.head |> Array.head |> string
    Assert.Equal("Specific 1A\ncontinued line",text)
    Assert.Empty(Database.query db "PRAGMA foreign_key_check")
    use tx = db.BeginTransaction()
    Database.execute db (Some tx) "DELETE FROM Main WHERE Specification='SA-A'" []
    Assert.Empty(Database.query db "PRAGMA foreign_key_check")
    tx.Rollback()
    Assert.Equal(5,count "SELECT count(*) FROM Main")

[<Fact>]
let ``updates preserve IDs report modified rows additions removals and repeatable imports`` () =
    use work = new Workspace()
    let zip = work.Path "source.zip"
    archive zip false
    let first = work.Path "first.db"
    import zip first (work.Path "first-report") None
    let before = File.ReadAllBytes first |> hash
    let repeated = work.Path "repeated.db"
    import zip repeated (work.Path "repeat-report") (Some first)
    use repeatSummary = JsonDocument.Parse(File.ReadAllText(work.Path "repeat-report/summary.json"))
    Assert.Equal(5,repeatSummary.RootElement.GetProperty("MaterialChanges").GetProperty("Unchanged").GetInt32())
    let changedZip = work.Path "changed.zip"
    archive changedZip true
    let updated = work.Path "updated.db"
    import changedZip updated (work.Path "updated-report") (Some first)
    use result = JsonDocument.Parse(File.ReadAllText(work.Path "updated-report/summary.json"))
    let counts = result.RootElement.GetProperty("MaterialChanges")
    Assert.Equal(1,counts.GetProperty("Added").GetInt32())
    Assert.Equal(1,counts.GetProperty("RemovedFromSelectedScope").GetInt32())
    Assert.Equal(1,counts.GetProperty("Modified").GetInt32())
    Assert.Equal(3,counts.GetProperty("Unchanged").GetInt32())
    use oldDb = Database.connect first SqliteOpenMode.ReadOnly
    use newDb = Database.connect updated SqliteOpenMode.ReadOnly
    Assert.True(Database.query oldDb "SELECT id FROM Main WHERE Specification='SA-A'" = Database.query newDb "SELECT id FROM Main WHERE Specification='SA-A'")
    Assert.Equal(before,File.ReadAllBytes first |> hash)
    Assert.ThrowsAny<Exception>(fun () -> import changedZip updated (work.Path "overwrite-report") (Some first)) |> ignore
    Assert.Equal(before,File.ReadAllBytes first |> hash)

[<Fact>]
let ``mismatched editions and missing tables cannot publish a partial database`` () =
    use work = new Workspace()
    let zip = work.Path "source.zip"
    archive zip false
    Assert.ThrowsAny<Exception>(fun () -> Input.readArchive zip "2023" |> ignore) |> ignore
    use incomplete = ZipFile.Open(zip,ZipArchiveMode.Update)
    incomplete.GetEntry("5B/notes.txt").Delete()
    incomplete.Dispose()
    let output = work.Path "bad.db"
    Assert.ThrowsAny<Exception>(fun () -> import zip output (work.Path "report") None) |> ignore
    Assert.False(File.Exists output)
    Assert.False(Directory.Exists(work.Path "report"))

[<Fact>]
let ``changed note bodies are reported even when all curves stay unchanged`` () =
    use work = new Workspace()
    let zip = work.Path "source.zip"
    archive zip false
    let first = work.Path "first.db"
    import zip first (work.Path "first-reports") None
    do
        use source = ZipFile.Open(zip,ZipArchiveMode.Update)
        let entry = source.GetEntry("5B/notes.txt")
        entry.Delete()
        use stream = source.CreateEntry("5B/notes.txt").Open()
        use writer = new StreamWriter(stream)
        writer.Write("GENERAL NOTES\n(a)\tGeneral 5B\nNOTES - GENERAL REQUIREMENTS\nG1\tRevised restriction 5B\n")
    import zip (work.Path "updated.db") (work.Path "updated-reports") (Some first)
    use summary = JsonDocument.Parse(File.ReadAllText(work.Path "updated-reports/summary.json"))
    Assert.Equal(1,summary.RootElement.GetProperty("NoteChanges").GetProperty("Modified").GetInt32())
    Assert.Equal(5,summary.RootElement.GetProperty("MaterialChanges").GetProperty("Unchanged").GetInt32())

[<Fact>]
let ``populated anonymous rows and invalid numbers fail instead of silently disappearing`` () =
    for replacement in ["\"SA-A\"","\"…\""; "\"80\"","\"bad\""] do
        use work = new Workspace()
        let zip = work.Path "source.zip"
        archive zip false
        do
            use source = ZipFile.Open(zip,ZipArchiveMode.Update)
            let entry = source.GetEntry("1A/Exported_data (1).csv")
            let content =
                use stream = entry.Open()
                use reader = new StreamReader(stream)
                reader.ReadToEnd()
            entry.Delete()
            use stream = source.CreateEntry("1A/Exported_data (1).csv").Open()
            use writer = new StreamWriter(stream)
            writer.Write(content.Replace(fst replacement,snd replacement))
        Assert.ThrowsAny<Exception>(fun () -> import zip (work.Path "bad.db") (work.Path "reports") None) |> ignore
        Assert.False(File.Exists(work.Path "bad.db"))

/// <summary>Capture the console destination and always restore the process writer.</summary>
let captureConsole action =
    use writer = new StringWriter()
    let previous = Console.Out
    Console.SetOut writer
    try
        let result = action()
        result,writer.ToString()
    finally Console.SetOut previous

/// <summary>Run default-filename checks in an isolated current working directory.</summary>
let inDirectory directory action =
    let previous = Directory.GetCurrentDirectory()
    Directory.SetCurrentDirectory directory
    try action()
    finally Directory.SetCurrentDirectory previous

/// <summary>Prepare table-named source folders from a controlled test archive.</summary>
let sourceDirectory (work: Workspace) changed =
    let zip = work.Path (if changed then "changed.zip" else "source.zip")
    let directory = work.Path (if changed then "changed tables" else "source tables")
    archive zip changed
    ZipFile.ExtractToDirectory(zip,directory)
    directory

[<Fact>]
let ``help documents defaults and exits without input or output files`` () =
    let code,text = captureConsole (fun () -> Program.run [|"--help"|])
    Assert.Equal(0,code)
    for argument in ["--sourcedir";"--newdatabasefile";"--compare";"--report";"asme_materials.db";"<anno>"] do Assert.Contains(argument,text)
    Assert.ThrowsAny<Exception>(fun () -> Program.arguments [|"--report";"--compare"|] |> ignore) |> ignore
    Assert.ThrowsAny<Exception>(fun () -> Program.arguments [|"--input";"one.zip";"--sourcedir";"data"|] |> ignore) |> ignore
    Assert.ThrowsAny<Exception>(fun () -> Program.arguments [|"--updatedatabasefile"|] |> ignore) |> ignore

[<Fact>]
let ``directory imports match ZIP including notes and accept CVS or custom filenames`` () =
    use work = new Workspace()
    let directory = sourceDirectory work false
    File.Move(Path.Combine(directory,"Su/Exported_data (1).csv"),Path.Combine(directory,"Su/tensile.cvs"))
    Directory.CreateDirectory(Path.Combine(directory,"misc")) |> ignore
    File.WriteAllText(Path.Combine(directory,"misc/not-imported.txt"),"not a source export")
    let zipped = Input.readArchive (work.Path "source.zip") "2025"
    let tree = Input.readDirectory directory "2025"
    Assert.Equal("2025",Input.detectEdition directory)
    Assert.Equal("Directory",tree.SourceKind)
    Assert.Equal<string list>(zipped.Rows |> List.map (fun row -> row.Fingerprint) |> List.sort,tree.Rows |> List.map (fun row -> row.Fingerprint) |> List.sort)
    Assert.True(zipped.Notes=tree.Notes)
    Assert.Contains("misc/not-imported.txt",tree.Ignored)
    let digest = tree.ArchiveHash
    File.WriteAllText(Path.Combine(directory,"previous-report.txt"),"not input data")
    Assert.Equal(digest,(Input.readDirectory directory "2025").ArchiveHash)

[<Fact>]
let ``newdatabasefile infers edition and report saves full text with no console output`` () =
    use work = new Workspace()
    let directory = sourceDirectory work false
    inDirectory work.Root (fun () ->
        let code,text = captureConsole (fun () -> Program.run [|"--sourcedir";directory;"--newdatabasefile";"--report";"report.txt"|])
        Assert.Equal(0,code)
        Assert.Equal("",text)
        Assert.True(File.Exists(work.Path "asme_materials_2025.db"))
        let report = File.ReadAllText(work.Path "report.txt")
        Assert.Contains("ASME - edizione 2025",report)
        Assert.Contains("MissingBoth",report)
        Assert.Contains("SA-B",report)
        Assert.DoesNotContain("INCONGRUENZE E DIAGNOSTICA",report)
        Assert.DoesNotContain("COPERTURA DIMENSIONALE",report)
        Assert.DoesNotContain("MissingData",report)
        Assert.False(Directory.Exists(work.Path "reports")))

[<Fact>]
let ``compare defaults to asme_materials db and file report equals terminal without creating a database`` () =
    use work = new Workspace()
    let directory = sourceDirectory work false
    let baseline = work.Path "asme_materials.db"
    captureConsole (fun () -> Program.run [|"--sourcedir";directory;"--newdatabasefile";baseline|]) |> ignore
    let before = File.ReadAllBytes baseline |> hash
    let changed = sourceDirectory work true
    inDirectory work.Root (fun () ->
        let code,terminal = captureConsole (fun () -> Program.run [|"--sourcedir";changed;"--compare"|])
        Assert.Equal(0,code)
        Assert.Contains("solo confronto",terminal)
        Assert.Contains("Modified",terminal)
        Assert.Contains("RemovedFromSelectedScope",terminal)
        Assert.Contains("SA-Z",terminal)
        let code,stdout = captureConsole (fun () -> Program.run [|"--sourcedir";changed;"--compare";"--report";"diff.txt"|])
        Assert.Equal(0,code)
        Assert.Equal("",stdout)
        Assert.Equal(terminal,File.ReadAllText(work.Path "diff.txt"))
        Assert.Equal(before,File.ReadAllBytes baseline |> hash)
        Assert.Single(Directory.GetFiles(work.Root,"*.db",SearchOption.AllDirectories)) |> ignore
        Assert.Empty(Directory.GetFiles(work.Root,"*.building-*",SearchOption.AllDirectories)))

[<Fact>]
let ``explicit compare and creation can be combined without altering existing reports or baseline`` () =
    use work = new Workspace()
    let directory = sourceDirectory work false
    let baseline = work.Path "previous.db"
    captureConsole (fun () -> Program.run [|"--sourcedir";directory;"--newdatabasefile";baseline|]) |> ignore
    let before = File.ReadAllBytes baseline |> hash
    let changed = sourceDirectory work true
    let output = work.Path "new edition.db"
    let report = work.Path "differences.txt"
    let detailed = work.Path "details"
    let code,stdout = captureConsole (fun () -> Program.run [|"--sourcedir";changed;"--compare";baseline;"--newdatabasefile";output;"--report";report;"--report-dir";detailed|])
    Assert.Equal(0,code)
    Assert.Equal("",stdout)
    Assert.True(File.Exists output)
    Assert.True(File.Exists(Path.Combine(detailed,"summary.json")))
    Assert.Equal(before,File.ReadAllBytes baseline |> hash)
    let previousReport = File.ReadAllText report
    Assert.ThrowsAny<Exception>(fun () -> Program.run [|"--sourcedir";changed;"--newdatabasefile";work.Path "another.db";"--report";report|] |> ignore) |> ignore
    Assert.False(File.Exists(work.Path "another.db"))
    Assert.Equal(previousReport,File.ReadAllText report)

/// <summary>Build one additional ASME source row with temperature values in MPa and size in mm.</summary>
let extraFields specification product condition =
    ["","1";"Revision","(25)";"Product Form",product;"Spec. No.",specification;"Type/Grade","70";
     "Alloy Desig./UNS No.","K00001";"Class/Condition/Temper",condition;"Size/Thickness, mm","t <= 50";
     "Min. Tensile Strength, MPa","400";"Min. Yield Strength, MPa","200";"Notes","G1";
     "<=40 °C, MPa","80";"<=100 °C, MPa","75";"Page No.","1";"Line No.","1"]

/// <summary>Append an independently named table and its table-scoped note to an existing test ZIP.</summary>
let appendTable path table fields =
    use zip = ZipFile.Open(path,ZipArchiveMode.Update)
    let quote (value: string) = "\"" + value.Replace("\"","\"\"") + "\""
    let add name text =
        use stream = zip.CreateEntry(name).Open()
        use writer = new StreamWriter(stream,UTF8Encoding(false))
        writer.Write(text: string)
    let header = fields |> List.map (fst >> quote) |> String.concat ","
    let row = fields |> List.map (snd >> quote) |> String.concat ","
    add (table+"/Exported_data (1).csv") ("\"Subscription: 2025 Metric\"\n\"Table: Table " + table + "\"\n" + header + "\n" + row + "\n")
    add (table+"/notes.txt") ("GENERAL NOTES\n(a)\tGeneral " + table + "\nNOTES - GENERAL REQUIREMENTS\nG1\tSpecific " + table + "\n")

[<Fact>]
let ``MAIN unions admitted tables and bolting remains isolated while 6A to 6D are ignored`` () =
    use work = new Workspace()
    let zip,output,reports = work.Path "all.zip",work.Path "all.db",work.Path "reports"
    archive zip false
    let bolt = extraFields "SA-BOLT" "Bolting" "N"
    appendTable zip "3" bolt
    appendTable zip "4" (bolt |> List.filter (fun (name,_) -> name<>"Product Form"))
    appendTable zip "2A" (extraFields "SA-ONLY" "Plate" "N")
    appendTable zip "6B" (extraFields "SA-ONLY" "Plate" "N" |> List.map (fun (name,value) -> (if name="Class/Condition/Temper" then "Class/Cond./Temper" else name),value))
    appendTable zip "6A" (extraFields "SA-ALIAS" "Plate" "QT" |> List.map (fun (name,value) -> (if name="Class/Condition/Temper" then "Class/Cond./Temper" else name),value))
    appendTable zip "6D" (extraFields "SA-NOCOND" "Plate" "" |> List.filter (fun (name,_) -> name<>"Class/Condition/Temper"))
    let input = Input.readArchive zip "2025"
    Assert.Equal(14,input.Rows.Length)
    Assert.Equal(7,input.Rows |> List.map (fun row -> key row.Identity) |> List.distinct |> List.length)
    captureConsole (fun () -> Program.run [|"--input";zip;"--newdatabasefile";output;"--report-dir";reports;"--report";work.Path "report.txt"|]) |> ignore
    use db = Database.connect output SqliteOpenMode.ReadWrite
    let count sql = Database.query db sql |> List.head |> Array.head |> Convert.ToInt32
    Assert.Equal(7,count "SELECT count(*) FROM Main")
    Assert.Equal(14,count "SELECT count(*) FROM Material_Source")
    Assert.Equal(13,count "SELECT count(*) FROM Material_Properties")
    Assert.Equal(26,count "SELECT count(*) FROM Property_Values")
    Assert.Equal(2,count "SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ('Sb1','Sb2')")
    Assert.Equal(1,count "SELECT count(*) FROM Sb1")
    Assert.Equal(1,count "SELECT count(*) FROM Sb2")
    Assert.Equal(1,count "SELECT count(*) FROM Main WHERE Specification='SA-BOLT'")
    Assert.Equal(1,count "SELECT count(*) FROM Main WHERE Specification='SA-ONLY'")
    Assert.Equal(0,count "SELECT count(*) FROM Main WHERE Specification='SA-ALIAS'")
    Assert.Equal(0,count "SELECT count(*) FROM Main WHERE Specification='SA-NOCOND'")
    let counts = Database.query db "SELECT Sy,Su,S1,S2,Sb1,Sb2 FROM VW_Main_Mechanical_Counts WHERE Specification='SA-A'" |> List.head |> Array.map Convert.ToInt32 |> Array.toList
    Assert.Equal<int list>([1;1;1;0;0;0],counts)
    Assert.Equal(1,count "SELECT count(*) FROM VW_Missing_Strength WHERE Specification='SA-BOLT' AND HasSb1 AND HasSb2 AND NOT HasSy AND NOT HasSu")
    let notes = Database.query db "SELECT s.SourceTable,n.note FROM Property_Notes n JOIN Property_Source s ON s.id_property=n.id_property WHERE n.note_type IN ('3:G1','4:G1') ORDER BY s.SourceTable" |> List.map (fun row -> string row[0],string row[1])
    Assert.Equal<(string*string) list>([("3","Specific 3");("4","Specific 4")],notes)
    Assert.Throws<SqliteException>(fun () -> Database.execute db None "INSERT INTO Sb1(id_property,id_main,id_size) SELECT id,id_main,id_size FROM Material_Properties WHERE PropertyCode='S1' LIMIT 1" []) |> ignore
    let mechanical = File.ReadAllBytes(Path.Combine(reports,"main-mechanical.csv")) |> Input.decode |> Input.parseCsv
    Assert.Equal(8,mechanical.Length)
    let only = mechanical |> List.tail |> List.map (snd >> List.zip (snd mechanical[0]) >> Map.ofList) |> List.find (fun row -> row["Specification"]="SA-ONLY")
    Assert.Equal("Sy,Su,S1,S2",only["MissingData"])
    let text = File.ReadAllText(work.Path "report.txt")
    Assert.Contains("MAIN - BOLTING: CONTEGGI Sy, Su, Sb1, Sb2 (1)",text)
    Assert.Contains("MAIN - ALTRI PRODUCT FORM: CONTEGGI Sy, Su, S1, S2 (6)",text)
    Assert.Contains("MAIN - PRESENZA E, Alpha, c, k, a, density, Nu (7)",text)

[<Fact>]
let ``empty bolting curves are counted as rows but remain missing data`` () =
    use work = new Workspace()
    let zip,output,reports = work.Path "empty.zip",work.Path "empty.db",work.Path "reports"
    archive zip false
    appendTable zip "3" (extraFields "SA-EMPTY-BOLT" "Bar, rod" "N" |> List.map (fun (name,value) -> name,(if name.Contains("°C, MPa") then "…" else value)))
    captureConsole (fun () -> Program.run [|"--input";zip;"--newdatabasefile";output;"--report-dir";reports;"--report";work.Path "report.txt"|]) |> ignore
    use db = Database.connect output SqliteOpenMode.ReadOnly
    let flags = Database.query db "SELECT Sb1,HasSb1 FROM VW_Main_Mechanical_Counts WHERE Specification='SA-EMPTY-BOLT'" |> List.head |> Array.map Convert.ToInt32 |> Array.toList
    Assert.Equal<int list>([1;0],flags)
    let matrix = File.ReadAllBytes(Path.Combine(reports,"main-mechanical.csv")) |> Input.decode |> Input.parseCsv
    let row = matrix |> List.tail |> List.map (snd >> List.zip (snd matrix[0]) >> Map.ofList) |> List.find (fun row -> row["Specification"]="SA-EMPTY-BOLT")
    Assert.Contains("Sb1",row["MissingData"])
    Assert.DoesNotContain("S1",row["MissingData"])
    Assert.DoesNotContain("S2",row["MissingData"])
    Assert.Equal("NO",row["Sb1HasData"])

[<Fact>]
let ``physical availability checks values and constant density Nu including zero`` () =
    use work = new Workspace()
    let zip,output,reports = work.Path "source.zip",work.Path "physical.db",work.Path "reports"
    archive zip false
    captureConsole (fun () -> Program.run [|"--input";zip;"--newdatabasefile";output;"--report";work.Path "report.txt"|]) |> ignore
    do
        use db = Database.connect output SqliteOpenMode.ReadWrite
        let run sql = Database.execute db None sql []
        run "INSERT INTO Physical_Properties(id_main,PropertyCode) SELECT id,'E' FROM Main WHERE Specification='SA-A'"
        let hasE () = Database.query db "SELECT E FROM VW_Main_Physical_Availability WHERE Specification='SA-A'" |> List.head |> Array.head |> Convert.ToInt32
        Assert.Equal(0,hasE())
        run "INSERT INTO Physical_Property_Values(id_physical_property,Temperature,PropertyValue) SELECT id,20,210000 FROM Physical_Properties WHERE PropertyCode='E'"
        Assert.Equal(1,hasE())
        for code,value in ["Alpha",0.000012;"Cp",500.;"TC",44.;"TD",0.00001] do
            Database.execute db None "INSERT INTO Physical_Properties(id_main,PropertyCode) SELECT id,$code FROM Main WHERE Specification='SA-A'" ["$code",box code]
            Database.execute db None "INSERT INTO Physical_Property_Values(id_physical_property,Temperature,PropertyValue) SELECT id,20,$value FROM Physical_Properties WHERE PropertyCode=$code" ["$code",box code;"$value",box value]
        run "INSERT INTO Extension(id_main,ConstantDensity,ConstantPoissonRatio) SELECT id,7850,0 FROM Main WHERE Specification='SA-A'"
        for code in ["Density";"Nu"] do
            Database.execute db None "INSERT INTO Physical_Properties(id_main,PropertyCode) SELECT id,$code FROM Main WHERE Specification='SA-B'" ["$code",box code]
            Database.execute db None "INSERT INTO Physical_Property_Values(id_physical_property,Temperature,PropertyValue) SELECT p.id,20,1 FROM Physical_Properties p JOIN Main m ON m.id=p.id_main WHERE m.Specification='SA-B' AND p.PropertyCode=$code" ["$code",box code]
        let flags = Database.query db "SELECT E,Alpha,c,k,a,density,Nu FROM VW_Main_Physical_Availability WHERE Specification='SA-A'" |> List.head |> Array.map Convert.ToInt32 |> Array.toList
        Assert.Equal<int list>([1;1;1;1;1;1;1],flags)
    Directory.CreateDirectory reports |> ignore
    let coverage = Coverage.write Reports.csv reports output
    Assert.Equal(5,coverage.MainRowsChecked)
    Assert.Equal(2,coverage.PhysicalMaterialsWithData["density"])
    Assert.Equal(2,coverage.PhysicalMaterialsWithData["Nu"])
    let rows = File.ReadAllBytes(Path.Combine(reports,"main-physical.csv")) |> Input.decode |> Input.parseCsv
    let row = rows |> List.tail |> List.map (snd >> List.zip (snd rows[0]) >> Map.ofList) |> List.find (fun row -> row["Specification"]="SA-A")
    Assert.False(row.ContainsKey "MissingData")
    Assert.Equal("Nu",snd rows[0] |> List.last)
    Assert.Equal("YES",row["Nu"])

[<Fact>]
let ``missing whole source families checks only applicable mechanical properties`` () =
    use work = new Workspace()
    let zip,output,reports = work.Path "minimal.zip",work.Path "minimal.db",work.Path "reports"
    do
        use empty = ZipFile.Open(zip,ZipArchiveMode.Create)
        ()
    appendTable zip "2A" (extraFields "SA-ONLY" "Plate" "N")
    captureConsole (fun () -> Program.run [|"--input";zip;"--newdatabasefile";output;"--report-dir";reports;"--report";work.Path "report.txt"|]) |> ignore
    use summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(reports,"summary.json")))
    Assert.Equal(1,summary.RootElement.GetProperty("MaterialCount").GetInt32())
    Assert.Equal(0,summary.RootElement.GetProperty("SourceRowCount").GetInt32())
    for code in mechanicalCodes do
        Assert.Equal((if code="Sb1" || code="Sb2" then 0 else 1),summary.RootElement.GetProperty("MainCoverage").GetProperty("MissingMechanicalByProperty").GetProperty(code).GetInt32())

/// <summary>Append a source row to an existing table without changing the table's note document.</summary>
let appendVariant path table (transform: Map<string,string> -> Map<string,string>) =
    use zip = ZipFile.Open(path,ZipArchiveMode.Update)
    let name = table+"/Exported_data (1).csv"
    let entry = zip.GetEntry name
    let contents =
        use stream = entry.Open()
        use reader = new StreamReader(stream)
        reader.ReadToEnd()
    let records = Input.parseCsv contents |> List.map snd
    let index = records |> List.findIndex (List.contains "Spec. No.")
    let header = records[index]
    let row = records[index+1] |> List.zip header |> Map.ofList |> transform
    let quote (value: string) = "\"" + value.Replace("\"","\"\"") + "\""
    let rendered = header |> List.map (fun column -> quote row[column]) |> String.concat ","
    entry.Delete()
    use stream = zip.CreateEntry(name).Open()
    use writer = new StreamWriter(stream)
    writer.Write(contents.TrimEnd() + "\n" + rendered + "\n")

[<Fact>]
let ``four field identity merges product forms and SQL forbids duplicates even with NULLs`` () =
    use work = new Workspace()
    let zip,output,reports = work.Path "forms.zip",work.Path "forms.db",work.Path "reports"
    archive zip false
    appendVariant zip "1A" (Map.add "Product Form" "Bar")
    appendVariant zip "Sy" (Map.add "Product Form" "Sheet")
    appendVariant zip "5B" (Map.add "Product Form" "Tube")
    captureConsole (fun () -> Program.run [|"--input";zip;"--newdatabasefile";output;"--report-dir";reports;"--report";work.Path "report.txt"|]) |> ignore
    use db = Database.connect output SqliteOpenMode.ReadWrite
    let count sql = Database.query db sql |> List.head |> Array.head |> Convert.ToInt32
    Assert.Equal(5,count "SELECT count(*) FROM Main")
    Assert.Equal(0,count "SELECT count(*) FROM pragma_table_info('Main') WHERE name='ProductForm'")
    Assert.Equal(0,count "SELECT count(*) FROM Material_Properties WHERE ProductForm IS NOT NULL AND PropertyCode NOT IN ('S1','S2')")
    let forms = Database.query db "SELECT DISTINCT ProductForm FROM S1 WHERE Specification='SA-A' ORDER BY ProductForm" |> List.map (fun row -> string row[0])
    Assert.Equal<string list>(["BAR";"PLATE"],forms)
    Assert.Equal(1,count "SELECT count(*) FROM Main WHERE Specification='SA-A' AND NominalComposition='Carbon steel'")
    Assert.Throws<SqliteException>(fun () -> Database.execute db None "INSERT INTO Main(Specification,Grade,ClassConditionTemper,AlloyDesignation) SELECT lower(Specification),Grade,ClassConditionTemper,AlloyDesignation FROM Main WHERE Specification='SA-A'" []) |> ignore
    Assert.Throws<SqliteException>(fun () -> Database.execute db None "UPDATE Material_Properties SET ProductForm='BAR' WHERE PropertyCode='Sy'" []) |> ignore
    let baseFields = extraFields "SA-IDENTITY" "Plate" "N" |> Map.ofList
    let baseKey = identity baseFields |> key
    Assert.Equal(baseKey,baseFields |> Map.add "Product Form" "Forgings" |> identity |> key)
    for fieldName in ["Spec. No.";"Type/Grade";"Class/Condition/Temper";"Alloy Desig./UNS No."] do
        Assert.NotEqual<string>(baseKey,baseFields |> Map.add fieldName "different" |> identity |> key)

[<Fact>]
let ``nominal composition is shared and real conflicts do not split MAIN`` () =
    use work = new Workspace()
    let zip,output,reports = work.Path "composition.zip",work.Path "composition.db",work.Path "reports"
    archive zip false
    appendVariant zip "1A" (Map.add "Nominal Composition" "Different composition")
    appendTable zip "3" (extraFields "SA-COMPOSITION" "Bolting" "N" @ ["Nominal Composition","33Ni–42Fe–21Cr"])
    appendTable zip "4" (extraFields "SA-COMPOSITION" "Bolting" "N" @ ["Nominal Composition","42Fe–33Ni–21Cr"])
    captureConsole (fun () -> Program.run [|"--input";zip;"--newdatabasefile";output;"--report-dir";reports;"--report";work.Path "report.txt"|]) |> ignore
    use db = Database.connect output SqliteOpenMode.ReadOnly
    Assert.Equal(1L,Database.query db "SELECT count(*) FROM Main WHERE Specification='SA-A' AND NominalComposition IS NULL" |> List.head |> Array.head |> Convert.ToInt64)
    Assert.Equal(1L,Database.query db "SELECT count(*) FROM Main WHERE Specification='SA-COMPOSITION' AND NominalComposition IS NOT NULL" |> List.head |> Array.head |> Convert.ToInt64)
    Assert.Equal(compositionKey "33Ni–42Fe–21Cr",compositionKey "42Fe–33Ni–21Cr")
    let issues = File.ReadAllText(Path.Combine(reports,"issues.csv"))
    Assert.Contains("ConflictingNominalComposition",issues)
    Assert.Contains("EquivalentCompositionNotation",issues)
    use summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(reports,"summary.json")))
    Assert.Equal(1,summary.RootElement.GetProperty("CompositionConflictCount").GetInt32())

[<Fact>]
let ``ignored 6A to 6D do not affect MAIN notes edition or effective directory digest`` () =
    use work = new Workspace()
    let directory = sourceDirectory work false
    let before = Input.readDirectory directory "2025"
    for table in ["6A";"6B";"6C";"6D"] do
        Directory.CreateDirectory(Path.Combine(directory,table)) |> ignore
        File.WriteAllBytes(Path.Combine(directory,table,"broken.csv"),[|0xFFuy;0x00uy|])
        File.WriteAllText(Path.Combine(directory,table,"notes.txt"),"invalid ignored notes")
    let after = Input.readDirectory directory "2025"
    Assert.Equal("2025",Input.detectEdition directory)
    Assert.Equal(before.ArchiveHash,after.ArchiveHash)
    Assert.Equal(before.Rows.Length,after.Rows.Length)
    Assert.Equal(before.Notes.Length,after.Notes.Length)
    Assert.Equal(8,after.Ignored.Length)
    Assert.DoesNotContain(after.Files,fun (name,_,_) -> name.StartsWith("6"))

[<Fact>]
let ``old five field baselines merge deterministically and ignored-only records are excluded`` () =
    use work = new Workspace()
    let zip,baseline,output = work.Path "source.zip",work.Path "old.db",work.Path "new.db"
    archive zip false
    do
        use db = Database.connect baseline SqliteOpenMode.ReadWriteCreate
        Database.execute db None "CREATE TABLE Main(id INTEGER PRIMARY KEY,Specification TEXT,Grade TEXT,AlloyDesignation TEXT,ClassConditionTemper TEXT,ProductForm TEXT); CREATE TABLE Material_Identity(id_main INTEGER,NaturalKey TEXT); CREATE TABLE Material_Source(id_main INTEGER,SourceTable TEXT,Fingerprint TEXT)" []
        for id,spec,form,table in [10,"SA-A","PLATE","1A";20,"SA-A","BAR","1A";30,"SA-IGNORED","PLATE","6A"] do
            Database.execute db None "INSERT INTO Main VALUES($id,$spec,'70',NULL,'N',$form)" ["$id",box id;"$spec",box spec;"$form",box form]
            Database.execute db None "INSERT INTO Material_Identity VALUES($id,$key)" ["$id",box id;"$key",box (json [|spec;"70";"";"N";form|])]
            Database.execute db None "INSERT INTO Material_Source VALUES($id,$table,$fingerprint)" ["$id",box id;"$table",box table;"$fingerprint",box (string id)]
    let original = File.ReadAllBytes baseline |> hash
    let loaded,_ = Database.loadBaseline baseline
    Assert.Equal(2,loaded.Length)
    Assert.Equal(key loaded[0].Identity,key loaded[1].Identity)
    captureConsole (fun () -> Program.run [|"--input";zip;"--newdatabasefile";output;"--compare";baseline;"--report";work.Path "report.txt"|]) |> ignore
    use db = Database.connect output SqliteOpenMode.ReadOnly
    Assert.Equal(10L,Database.query db "SELECT id FROM Main WHERE Specification='SA-A'" |> List.head |> Array.head |> Convert.ToInt64)
    Assert.Equal(original,File.ReadAllBytes baseline |> hash)

[<Fact>]
let ``shared bolting identity remains unique with both applicability groups`` () =
    use work = new Workspace()
    let zip,output,reports = work.Path "shared.zip",work.Path "shared.db",work.Path "reports"
    archive zip false
    appendVariant zip "1A" (Map.add "Product Form" "BOLTING")
    captureConsole (fun () -> Program.run [|"--input";zip;"--newdatabasefile";output;"--report-dir";reports;"--report";work.Path "report.txt"|]) |> ignore
    use db = Database.connect output SqliteOpenMode.ReadOnly
    Assert.Equal(5,Database.query db "SELECT count(*) FROM Main" |> List.head |> Array.head |> Convert.ToInt32)
    let read name =
        let rows = File.ReadAllBytes(Path.Combine(reports,name)) |> Input.decode |> Input.parseCsv |> List.map snd
        List.tail rows |> List.map (List.zip rows.Head >> Map.ofList)
    Assert.Single(read "main-bolting.csv") |> ignore
    Assert.Equal(5,(read "main-nonbolting.csv").Length)
    let shared = read "main-mechanical.csv" |> List.find (fun row -> row["Specification"]="SA-A")
    Assert.Equal("S2,Sb1,Sb2",shared["MissingData"])
    let text = File.ReadAllText(work.Path "report.txt")
    let finalSection = text.Substring(text.IndexOf("MAIN - PRESENZA E, Alpha, c, k, a, density, Nu"))
    Assert.Equal(7,finalSection.TrimEnd().Split('\n').Length)
    Assert.EndsWith("Nu",(finalSection.Split('\n')[1]).TrimEnd())
    Assert.DoesNotContain("MissingData",text)

[<Fact>]
let ``ASME data imports identifiers and Celsius limits with exact regulatory provenance`` () =
    use work = new Workspace()
    let zip,output,reports = work.Path "regulatory.zip",work.Path "regulatory.db",work.Path "reports"
    do
        use empty = ZipFile.Open(zip,ZipArchiveMode.Create)
        ()
    appendTable zip "1A" (extraFields "SA-REG" "Plate" "N" @ [
        "P-No.","5A"; "Group No.","1"; "External Pressure Chart No.","CS-1";
        "I—Applic. and Max. Temp. Limit (°C)","649";
        "III—Applic. and Max. Temp. Limit (°C)","427";
        "VIII-1—Applic. and Max. Temp. Limit (°C)","538";
        "XII—Applic. and Max. Temp. Limit (°C)","343"])
    appendTable zip "2A" (extraFields "SA-REG" "Plate" "N" @ [
        "P-No.","5A"; "Group No.","1";
        "VIII-2-Applic. and Max. Temp. Limit (°C)","427"])
    captureConsole (fun () -> Program.run [|"--input";zip;"--newdatabasefile";output;"--report-dir";reports;"--report";work.Path "report.txt"|]) |> ignore
    use db = Database.connect output SqliteOpenMode.ReadWrite
    let row = Database.query db "SELECT P_Number,Group_Number,External_Pressure_Chart_No,Max_Temp_Sect_I,Max_Temp_Sect_III,Max_Temp_Sect_VIII_1,Max_Temp_Sect_VIII_2,Max_Temp_Sect_XII FROM ASME_Data" |> List.exactlyOne
    Assert.Equal<string list>(["5A";"1";"CS-1";"649";"427";"538";"427";"343"],row |> Array.map string |> Array.toList)
    Assert.Equal(10,Database.query db "SELECT count(*) FROM ASME_Data_Source" |> List.head |> Array.head |> Convert.ToInt32)
    Assert.True(Database.query db "SELECT Time_Dependent_Start_Temp FROM ASME_Data" |> List.head |> Array.head |> isNull)
    let note = Database.query db "SELECT NoteText FROM VW_Property_Notes_All WHERE SourceTable='1A' AND Code='G1'" |> List.exactlyOne |> Array.head |> string
    Assert.Equal("Specific 1A",note)
    use transaction = db.BeginTransaction()
    Database.execute db (Some transaction) "DELETE FROM Main" []
    Assert.Empty(Database.query db "PRAGMA foreign_key_check")
    transaction.Rollback()

[<Fact>]
let ``ASME data never promotes NP qualified or conflicting source limits to unconditional scalars`` () =
    use work = new Workspace()
    let zip,output,reports = work.Path "limits.zip",work.Path "limits.db",work.Path "reports"
    do
        use empty = ZipFile.Open(zip,ZipArchiveMode.Create)
        ()
    appendTable zip "1A" (extraFields "SA-LIMIT" "Plate" "N" @ [
        "P-No.","5A"; "I—Applic. and Max. Temp. Limit (°C)","NP";
        "III—Applic. and Max. Temp. Limit (°C)","204 (Cl. 3 only)";
        "VIII-1—Applic. and Max. Temp. Limit (°C)","538"])
    appendVariant zip "1A" (Map.add "Product Form" "Tube" >> Map.add "P-No." "5B" >> Map.add "VIII-1—Applic. and Max. Temp. Limit (°C)" "649")
    captureConsole (fun () -> Program.run [|"--input";zip;"--newdatabasefile";output;"--report-dir";reports;"--report";work.Path "report.txt"|]) |> ignore
    use db = Database.connect output SqliteOpenMode.ReadOnly
    let row = Database.query db "SELECT P_Number,Max_Temp_Sect_I,Max_Temp_Sect_III,Max_Temp_Sect_VIII_1 FROM ASME_Data" |> List.exactlyOne
    Assert.All(row,fun value -> Assert.Null(value))
    let raw = Database.query db "SELECT DISTINCT RawValue FROM ASME_Data_Source WHERE NumericValue IS NULL" |> List.map (Array.head >> string)
    Assert.Contains("NP",raw)
    Assert.Contains("204 (Cl. 3 only)",raw)
    Assert.Contains("5A",raw)
    Assert.Contains("5B",raw)
    Assert.Contains("ConflictingAsmeData",File.ReadAllText(Path.Combine(reports,"issues.csv")))
    Assert.Contains("QualifiedAsmeLimit",File.ReadAllText(Path.Combine(reports,"issues.csv")))
    Assert.Contains("Max_Temp_Sect_VIII_1",File.ReadAllText(Path.Combine(reports,"asme-data-conflicts.csv")))
    Assert.Equal(1,Database.query db "SELECT count(*) FROM Main" |> List.head |> Array.head |> Convert.ToInt32)

[<Fact>]
let ``every imported note text and complete source document survives database round trip`` () =
    use work = new Workspace()
    let zip,output = work.Path "notes.zip",work.Path "notes.db"
    archive zip false
    let input = Input.readArchive zip "2025"
    Database.create output "2025" input [] |> ignore
    use db = Database.connect output SqliteOpenMode.ReadOnly
    let notes = Database.query db "SELECT SourceTable,Code,Kind,NoteText FROM Source_Notes" |> List.map (fun row -> (string row[0],string row[1]),(string row[2],string row[3])) |> Map.ofList
    for note in input.Notes do Assert.Equal((note.Kind,note.Text),notes[(note.Table,note.Code)])
    let documents = Database.query db "SELECT SourceTable,Contents,SHA256 FROM Source_Documents" |> List.map (fun row -> string row[0],(string row[1],string row[2])) |> Map.ofList
    for table,contents,sha in input.Documents do Assert.Equal((contents,sha),documents[table])
    for row in input.Rows do
        for code in row.NoteCodes do
            let expected = input.Notes |> List.find (fun note -> note.Table=row.Table && note.Code=code)
            let actual = Database.query db $"SELECT n.NoteText FROM VW_Property_Notes_All n JOIN Property_Source s ON s.id_property=n.id_property WHERE s.SourceTable='{row.Table}' AND s.CsvLine={row.Line} AND n.Code='{code}'" |> List.exactlyOne |> Array.head |> string
            Assert.Equal(expected.Text,actual)
