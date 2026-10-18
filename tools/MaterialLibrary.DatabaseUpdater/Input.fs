namespace MaterialLibrary.DatabaseUpdater

open System
open System.IO
open System.IO.Compression
open System.Collections.Generic
open System.Text
open System.Text.RegularExpressions
open Domain

/// <summary>Strict CSV and ZIP import; documents are data and are never evaluated as instructions.</summary>
module Input =
    /// <summary>Decode UTF-8 strictly, then Windows-1252 exports without replacing undecodable characters.</summary>
    let decode (bytes: byte array) =
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)
        try UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF')
        with :? DecoderFallbackException -> Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(bytes)

    /// <summary>Read CSV including quoted commas, CRLF, multiline fields and Excel ="text" wrappers without evaluation.</summary>
    let parseCsv (text: string) =
        let records = ResizeArray<int * string list>()
        let fields = ResizeArray<string>()
        let cell = StringBuilder()
        let mutable quoted = false
        let mutable closed = false
        let mutable index = 0
        let mutable line = 1
        let mutable startLine = 1
        let finishCell () = fields.Add(cell.ToString()); cell.Clear() |> ignore; closed <- false
        let finishRow () = finishCell(); records.Add(startLine, List.ofSeq fields); fields.Clear(); startLine <- line + 1
        while index < text.Length do
            let ch = text[index]
            if quoted then
                if ch = '"' then
                    if index + 1 < text.Length && text[index+1] = '"' then cell.Append('"') |> ignore; index <- index + 1
                    else quoted <- false; closed <- true
                else
                    cell.Append(ch) |> ignore
                    if ch = '\n' then line <- line + 1
            else
                match ch with
                | '"' when cell.Length = 0 || cell.ToString() = "=" -> cell.Clear() |> ignore; quoted <- true
                | ',' -> finishCell()
                | '\r' when index + 1 < text.Length && text[index+1] = '\n' -> ()
                | '\n' | '\r' -> finishRow(); line <- line + 1
                | _ when closed && not (Char.IsWhiteSpace ch) -> failwithf "CSV line %d: unexpected text after closing quote" line
                | _ when closed -> ()
                | _ -> cell.Append(ch) |> ignore
            index <- index + 1
        if quoted then failwithf "CSV line %d: unterminated quoted field" startLine
        if cell.Length > 0 || fields.Count > 0 || closed then finishRow()
        List.ofSeq records

    /// <summary>Read table-specific general and row note definitions, preserving multiline note bodies.</summary>
    let parseNotes table (text: string) =
        let notes = ResizeArray<Note>()
        let mutable kind = "General"
        let mutable current: Note option = None
        let flush () = current |> Option.iter notes.Add; current <- None
        for line in text.Replace("\r\n", "\n").Split('\n') do
            let trimmed = line.Trim()
            if trimmed.StartsWith("GENERAL NOTES", StringComparison.Ordinal) || trimmed.StartsWith("NOTES", StringComparison.Ordinal) then
                flush()
                kind <- if trimmed.StartsWith("GENERAL NOTES", StringComparison.Ordinal) then "General" else "Specific"
            else
                let m = Regex.Match(line, @"^(?:\(\d{2}\)\t)?(?<code>\([a-z]\)|\(\d+\)|[A-Z]\d+)\t(?<text>.*)$")
                if m.Success then
                    flush()
                    current <- Some { Table=table; Code=m.Groups["code"].Value; Kind=kind; Text=m.Groups["text"].Value }
                else
                    match current with
                    | Some note when trimmed <> "" -> current <- Some { note with Text=note.Text + "\n" + line }
                    | _ -> ()
        flush()
        let duplicates = notes |> Seq.countBy (fun n -> n.Code) |> Seq.filter (fun (_, n) -> n > 1) |> Seq.toList
        if not duplicates.IsEmpty then failwithf "Duplicate note codes in %s: %A" table duplicates
        List.ofSeq notes

    /// <summary>Detached source file read from a ZIP or a table-folder tree.</summary>
    type private SourceFile = { FullName: string; Name: string; Bytes: byte array }

    /// <summary>Accept both the standard CSV extension and the CVS spelling used by some exports.</summary>
    let private isCsv (name: string) =
        name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".cvs", StringComparison.OrdinalIgnoreCase)

    /// <summary>Union material identities from every table folder and import the eight mechanical exports.</summary>
    let private readFiles path sourceKind sourceHash (entries: SourceFile list) edition =
        let selected = entries |> List.filter (fun entry -> isCsv entry.Name && entry.FullName.Contains('/') && not (isIgnoredTable (entry.FullName.Split('/')[0]))) |> List.map (fun entry -> entry.FullName.Split('/')[0]) |> Set.ofList
        if selected.IsEmpty then failwith "No table folders containing CSV/CVS exports found"
        let issues = ResizeArray<Issue>()
        let rows = ResizeArray<SourceRow>()
        let omitted = ResizeArray<OmittedRow>()
        let notes = ResizeArray<Note>()
        let documents = ResizeArray<string * string * string>()
        let files = ResizeArray<string * string * int>()
        let ignored = ResizeArray<string>()
        let issue severity code key source detail = issues.Add { Severity=severity; Code=code; MaterialKey=key; Source=source; Detail=detail }
        if entries.Length > 4096 || (entries |> List.sumBy (fun e -> int64 e.Bytes.Length)) > 200_000_000L then failwith "Source exceeds supported import limits"
        let duplicates = entries |> List.countBy (fun e -> e.FullName.ToUpperInvariant()) |> List.filter (fun (_, n) -> n > 1)
        if not duplicates.IsEmpty then failwith "Duplicate or case-colliding source file names"
        let bytes (entry: SourceFile) = entry.Bytes
        for required in ["Sy";"Su";"1A";"1B";"5A";"5B";"3";"4"] do
            if not (selected.Contains required) then issue "Warning" "MissingSourceTable" "" required "No source export: per-material availability will be checked and reported as missing"
        for table in selected do
            let tableEntries = entries |> List.filter (fun e -> e.FullName.StartsWith(table + "/", StringComparison.Ordinal))
            let csvs = tableEntries |> List.filter (fun e -> isCsv e.Name) |> List.sortBy (fun e ->
                let m = Regex.Match(e.Name, @"\((\d+)\)")
                (if m.Success then int m.Groups[1].Value else 0), e.FullName)
            if csvs.IsEmpty then failwithf "Required table %s has no CSV files" table
            let parts = csvs |> List.choose (fun entry ->
                let m = Regex.Match(entry.Name,@"^Exported_data \((\d+)\)\.(?:csv|cvs)$",RegexOptions.IgnoreCase)
                if m.Success then Some (int m.Groups[1].Value) else None)
            if parts <> [1..parts.Length] then failwithf "Missing or duplicate CSV part in table %s: %A" table parts
            let noteFiles = tableEntries |> List.filter (fun e -> e.Name.Equals("notes.txt", StringComparison.OrdinalIgnoreCase))
            if noteFiles.Length > 1 || (isMechanicalTable table && noteFiles.Length <> 1) then failwithf "Required table %s must contain exactly one notes.txt" table
            let definitions =
                match noteFiles with
                | [noteEntry] ->
                    let noteBytes = bytes noteEntry
                    let noteText = decode noteBytes
                    documents.Add(table, noteText, hash noteBytes)
                    parseNotes table noteText
                | _ ->
                    issue "Warning" "MissingNoteDocument" "" table "Identity-only table has no notes.txt; raw fields remain preserved"
                    []
            notes.AddRange definitions
            let defined = definitions |> List.map (fun n -> n.Code) |> Set.ofList
            let mutable referenceHeader = None
            for entry in csvs do
                let raw = bytes entry
                let records = decode raw |> parseCsv
                let headerIndex = records |> List.tryFindIndex (fun (_, cells) -> List.contains "Spec. No." cells)
                let hi = headerIndex |> Option.defaultWith (fun () -> failwith ("Missing CSV header: " + entry.FullName))
                let preamble = records |> List.take hi |> List.collect snd |> String.concat "\n"
                if not (preamble.Contains("Subscription: " + edition + " Metric", StringComparison.Ordinal)) then failwith ("Edition/units mismatch: " + entry.FullName)
                let expectedTable = match table with "Sy" -> "Y-1" | "Su" -> "U" | _ -> table
                if not (Regex.IsMatch(preamble, @"(?m)^Table: Table " + Regex.Escape(expectedTable) + @"\s*$")) then failwith ("Source table mismatch: " + entry.FullName)
                let _, header = records[hi]
                if (header |> List.distinct |> List.length) <> header.Length then failwith ("Duplicate CSV columns: " + entry.FullName)
                for required in ["Spec. No."; "Type/Grade"; "Alloy Desig./UNS No."] do
                    if not (List.contains required header) then failwithf "%s: missing column %s" entry.FullName required
                if isMechanicalTable table then
                    for required in ["Size/Thickness, mm";"Min. Tensile Strength, MPa";"Page No.";"Line No."] do
                        if not (List.contains required header) then failwithf "%s: missing column %s" entry.FullName required
                    if table <> "Su" && not (List.contains "Min. Yield Strength, MPa" header) then failwith ("Missing yield minimum column: " + entry.FullName)
                    if table <> "Su" && not (List.contains "Notes" header) then failwith ("Missing row notes column: " + entry.FullName)
                match referenceHeader with
                | Some prior when prior <> header -> failwith ("Header changed across parts: " + entry.FullName)
                | _ -> referenceHeader <- Some header
                let temperatures = header |> List.choose (fun h ->
                    let m = Regex.Match(h, @"^<=(-?\d+(?:\.\d+)?) °C, MPa$")
                    if m.Success then Some (h, (number m.Groups[1].Value).Value) else None)
                if isMechanicalTable table && temperatures.IsEmpty then failwith ("Missing metric stress columns: " + entry.FullName)
                for column in header do
                    if column.Contains("°C, MPa",StringComparison.Ordinal) && not (temperatures |> List.exists (fun (name,_) -> name=column)) then
                        failwith ("Unrecognized temperature column: " + column)
                let mutable count = 0
                for line, cells in records |> List.skip (hi + 1) do
                    if cells |> List.exists (fun s -> not (String.IsNullOrWhiteSpace s)) then
                        if cells.Length <> header.Length then failwithf "%s:%d has %d columns, expected %d" entry.FullName line cells.Length header.Length
                        let fields = List.zip header cells |> Map.ofList
                        let identity = identity fields
                        let materialKey = key identity
                        let location = entry.FullName + ":" + string line
                        if identity.Specification = "" then
                            let meaningful = fields |> Map.exists (fun k v -> not (List.contains k [""; "Revision"; "Page No."; "Line No."]) && not (missing v))
                            if meaningful then failwith ("Missing material specification in a populated row at " + location)
                            omitted.Add { Table=table; File=entry.FullName; Line=line; Fields=fields; Reason="Blank export row: no material identity or engineering values; possibly a deletion marker" }
                            issue "Warning" "BlankRevisionRow" "" location ("Revision=" + field "Revision" fields + "; page=" + field "Page No." fields + "; line=" + field "Line No." fields + "; identity cannot be inferred")
                        else
                            let parseNumeric column =
                                let text = field column fields
                                if missing text then None
                                else
                                    match number text with
                                    | Some value when value >= 0. ->
                                        if text.Contains(',') then issue "Info" "DecimalComma" materialKey location (column + "=" + text)
                                        Some value
                                    | _ -> failwithf "%s: invalid nonnegative MPa value %s=%s" location column text
                            let sy = parseNumeric "Min. Yield Strength, MPa"
                            let su = parseNumeric "Min. Tensile Strength, MPa"
                            let values = if isMechanicalTable table then temperatures |> List.choose (fun (column, temp) -> parseNumeric column |> Option.map (fun v -> temp, v)) else []
                            let rawSize = field "Size/Thickness, mm" fields
                            let size = parseSize rawSize |> Result.defaultWith (fun message -> failwith (location + ": " + message))
                            let noteText = field "Notes" fields
                            let codes = if missing noteText then [] else noteText.Split(',') |> Array.map (fun s -> s.Trim()) |> Array.toList
                            for code in codes do
                                if not (defined.Contains code) then issue "Warning" "UnresolvedNote" materialKey location (table + ":" + code)
                            if isMechanicalTable table && values.IsEmpty then issue "Warning" "EmptyCurve" materialKey location (propertyCode table)
                            let canonical = fields |> Map.filter (fun k _ -> not (List.contains k [""; "Page No."; "Line No."])) |> Map.toArray |> Array.map (fun (k,v) -> [|k; normalize v|])
                            let fingerprint = json [|table; json canonical|] |> Encoding.UTF8.GetBytes |> hash
                            rows.Add { Table=table; File=entry.FullName; Line=line; Fields=fields; Identity=identity; ProductForm=(if List.contains (propertyCode table) ["S1";"S2"] then field "Product Form" fields |> normalize else ""); Size=size; Values=values; Sy=sy; Su=su; NoteCodes=codes; Fingerprint=fingerprint }
                        count <- count + 1
                if count = 0 then failwith ("Empty CSV part: " + entry.FullName)
                files.Add(entry.FullName, hash raw, count)
        for entry in entries do
            let folder = entry.FullName.Split('/')[0]
            let recognized = selected.Contains(folder) && (isCsv entry.Name || entry.Name.Equals("notes.txt", StringComparison.OrdinalIgnoreCase))
            if not recognized then ignored.Add entry.FullName
        { Rows=List.ofSeq rows; Omitted=List.ofSeq omitted; Notes=List.ofSeq notes; Documents=List.ofSeq documents; Issues=List.ofSeq issues; Files=List.ofSeq files; Ignored=List.ofSeq ignored; ArchiveHash=sourceHash; SourceKind=sourceKind; SourcePath=Path.GetFullPath path }

    /// <summary>Load archive files without extracting paths or executing document contents.</summary>
    let private archiveFiles path =
        use archive = ZipFile.OpenRead path
        if archive.Entries.Count > 4096 || (archive.Entries |> Seq.sumBy (fun entry -> entry.Length)) > 200_000_000L then failwith "Archive exceeds supported import limits"
        archive.Entries
        |> Seq.filter (fun entry -> not (entry.FullName.EndsWith('/')))
        |> Seq.map (fun entry ->
            use stream = entry.Open()
            use data = new MemoryStream()
            stream.CopyTo(data)
            { FullName=entry.FullName; Name=entry.Name; Bytes=data.ToArray() })
        |> Seq.toList

    /// <summary>Load a table-folder tree without following symbolic links.</summary>
    let private directoryFiles path =
        let root = Path.GetFullPath path
        if not (Directory.Exists root) then failwith ("Source directory not found: " + root)
        let options = EnumerationOptions(RecurseSubdirectories=true, IgnoreInaccessible=false, AttributesToSkip=FileAttributes.ReparsePoint)
        let paths = Directory.EnumerateFiles(root,"*",options) |> Seq.sort |> Seq.toList
        let isSource file = isCsv file || Path.GetFileName(file).Equals("notes.txt",StringComparison.OrdinalIgnoreCase)
        if paths.Length > 4096 || (paths |> List.filter isSource |> List.sumBy (fun file -> FileInfo(file).Length)) > 200_000_000L then failwith "Source directory exceeds supported import limits"
        paths |> List.map (fun file -> { FullName=Path.GetRelativePath(root,file).Replace('\\','/'); Name=Path.GetFileName file; Bytes=if isSource file then File.ReadAllBytes file else [||] })

    /// <summary>Detect the edition from a table export, never from the current calendar year.</summary>
    let detectEdition path =
        let entries = if Directory.Exists path then directoryFiles path else archiveFiles path
        let first = entries |> List.tryFind (fun entry -> isCsv entry.Name && entry.FullName.Contains('/') && not (isIgnoredTable (entry.FullName.Split('/')[0])))
        let file = first |> Option.defaultWith (fun () -> failwith "No supported CSV table found to determine edition")
        let preamble = decode file.Bytes |> parseCsv |> List.takeWhile (fun (_,fields) -> not (List.contains "Spec. No." fields)) |> List.collect snd |> String.concat "\n"
        let m = Regex.Match(preamble,@"(?m)^Subscription: (\d{4}) Metric\s*$")
        if not m.Success then failwith ("Cannot determine metric edition from " + file.FullName)
        m.Groups[1].Value

    /// <summary>Read selected exports from a ZIP and retain its byte-level SHA-256.</summary>
    let readArchive path edition =
        readFiles path "ZIP" (File.ReadAllBytes path |> hash) (archiveFiles path) edition

    /// <summary>Read selected exports from table-named folders and hash the sorted path/content manifest.</summary>
    let readDirectory path edition =
        let files = directoryFiles path
        let manifest = files |> List.filter (fun entry -> not (isIgnoredTable (entry.FullName.Split('/')[0])) && (isCsv entry.Name || entry.Name.Equals("notes.txt",StringComparison.OrdinalIgnoreCase))) |> List.sortBy (fun entry -> entry.FullName) |> List.map (fun entry -> [|entry.FullName; hash entry.Bytes|])
        let digest = json manifest |> Encoding.UTF8.GetBytes |> hash
        readFiles path "Directory" digest files edition
