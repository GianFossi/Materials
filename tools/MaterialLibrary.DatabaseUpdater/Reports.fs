namespace MaterialLibrary.DatabaseUpdater

open System
open System.IO
open System.Text
open System.Text.Json
open System.Collections.Generic
open Microsoft.Data.Sqlite
open Domain

/// <summary>Auditable differences, missing strengths and inconsistency reports.</summary>
module Reports =
    /// <summary>Check complete coverage of a target size interval by a union of compatible source bands.</summary>
    let covers (target: SizeBand) (bands: SizeBand list) =
        let lo band = Option.defaultValue Double.NegativeInfinity band.Minimum
        let hi band = Option.defaultValue Double.PositiveInfinity band.Maximum
        let compatible band = band.Axis = target.Axis || (band.Minimum.IsNone && band.Maximum.IsNone)
        let ordered = bands |> List.filter compatible |> List.sortBy (fun band -> lo band, not band.MinimumIncluded)
        let merged = ResizeArray<SizeBand>()
        for band in ordered do
            if merged.Count = 0 then merged.Add band
            else
                let previous = merged[merged.Count-1]
                if lo band < hi previous || (lo band = hi previous && (band.MinimumIncluded || previous.MaximumIncluded)) then
                    if hi band > hi previous then merged[merged.Count-1] <- { previous with Maximum=band.Maximum; MaximumIncluded=band.MaximumIncluded }
                    elif hi band = hi previous then merged[merged.Count-1] <- { previous with MaximumIncluded=band.MaximumIncluded || previous.MaximumIncluded }
                else merged.Add band
        merged |> Seq.exists (fun band ->
            (lo band < lo target || (lo band = lo target && (not target.MinimumIncluded || band.MinimumIncluded || target.Minimum.IsNone)))
            && (hi band > hi target || (hi band = hi target && (not target.MaximumIncluded || band.MaximumIncluded || target.Maximum.IsNone))))

    /// <summary>Write portable UTF-8 CSV with a header even when no findings exist.</summary>
    let csv path headers (rows: string list seq) =
        let quote (s: string) = "\"" + s.Replace("\"", "\"\"") + "\""
        use writer = new StreamWriter(path, false, UTF8Encoding(true))
        let write row = row |> List.map quote |> String.concat "," |> writer.WriteLine
        write headers
        rows |> Seq.iter write

    /// <summary>Write explicit JSON records without relying on F# option/union serialization.</summary>
    let writeJson path value =
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonSerializerOptions(WriteIndented=true)) + "\n")

    /// <summary>Generate all reports from source rows and the exact created database.</summary>
    let write directory edition baselinePath baselineEdition (input: InputData) (baseline: Snapshot list) (ids: Map<string,int64>) databasePath =
        Directory.CreateDirectory directory |> ignore
        let groups = input.Rows |> List.groupBy (fun row -> key row.Identity) |> Map.ofList
        let oldGroups = baseline |> List.groupBy (fun row -> key row.Identity) |> Map.ofList
        let issues = ResizeArray<Issue>(input.Issues)
        let add severity code materialKey source detail = issues.Add { Severity=severity; Code=code; MaterialKey=materialKey; Source=source; Detail=detail }
        let location (row: SourceRow) = row.File + ":" + string row.Line
        for materialKey,old in Map.toSeq oldGroups do
            if old.Length > 1 then add "Info" "MergedBaselineIdentities" materialKey baselinePath ("Four-field identity merges old IDs; smallest ID retained: " + (old |> List.map (fun row -> string row.Id) |> String.concat ","))
            for item in old do
                if System.Text.RegularExpressions.Regex.IsMatch(item.Identity.Grade,@"^\d{4}-\d{2}-\d{2}(?: |$)") then
                    add "Warning" "DateLikeLegacyGrade" materialKey baselinePath ("Old grade resembles an Excel date conversion; do not interpret its unmatched identity as a confirmed deletion: " + item.Identity.Grade)
        for materialKey,rows in Map.toSeq groups do
            let compositions = rows |> List.map (fun row -> field "Nominal Composition" row.Fields) |> List.filter (missing >> not) |> List.distinct
            let normalizedCompositions = compositions |> List.map compositionKey |> List.distinct
            if normalizedCompositions.Length > 1 then
                let evidence = rows |> List.choose (fun row -> let value=field "Nominal Composition" row.Fields in if missing value then None else Some (location row + "=" + value)) |> String.concat "; "
                add "Warning" "ConflictingNominalComposition" materialKey "Main.NominalComposition" ("Single MAIN identity retained; composition left NULL; " + evidence)
            elif compositions.Length > 1 then
                add "Info" "EquivalentCompositionNotation" materialKey "Main.NominalComposition" ("Equivalent chemical tokens in different order/case: " + json compositions)
            for label,select in ["SMYS", (fun (row: SourceRow) -> row.Sy); "SMUS", (fun row -> row.Su)] do
                let values = rows |> List.choose select |> List.distinct |> List.sort
                if values.Length > 1 then
                    add "Info" "NonScalarMinimum" materialKey "Main" (label + " varies by source/size; Main is NULL; source minima retained: " + json values)
                elif values.IsEmpty then add "Warning" "MissingMinimum" materialKey "Main" (label + " is absent from all imported rows")
                for size,rs in rows |> List.groupBy (fun row -> row.Size,row.ProductForm) do
                    let values = rs |> List.choose select |> List.distinct |> List.sort
                    if values.Length > 1 then
                        let evidence = rs |> List.choose (fun row -> select row |> Option.map (fun value -> location row + "=" + string value)) |> String.concat "; "
                        add "Warning" "ConflictingMinimumSameSize" materialKey (field "Size/Thickness, mm" (List.head rs).Fields) (label + ": " + evidence)
            for row in rows do
                match row.Sy,row.Su with
                | Some sy,Some su when sy > su -> add "Warning" "MinimumYieldExceedsTensile" materialKey (location row) (sprintf "%g > %g MPa" sy su)
                | _ -> ()
            for _,duplicates in rows |> List.groupBy (fun row -> row.Fingerprint) do
                if duplicates.Length > 1 then add "Warning" "DuplicateSourceRow" materialKey "input" (duplicates |> List.map location |> String.concat "; ")
            for _,rs in rows |> List.groupBy (fun row -> row.Size) do
                let yieldRows = rs |> List.filter (fun row -> row.Table="Sy")
                let tensileRows = rs |> List.filter (fun row -> row.Table="Su")
                for sy in yieldRows do
                    for su in tensileRows do
                        let tensile = su.Values |> Map.ofList
                        for temp,value in sy.Values do
                            match Map.tryFind temp tensile with
                            | Some ultimate when value > ultimate -> add "Warning" "YieldExceedsTensile" materialKey (location sy + "; " + location su) (sprintf "%g C: Sy=%g MPa > Su=%g MPa" temp value ultimate)
                            | _ -> ()
                for (table,notes,product),variants in rs |> List.groupBy (fun row -> row.Table,row.NoteCodes,row.ProductForm) do
                    if variants.Length > 1 && (variants |> List.map (fun row -> row.Values) |> List.distinct |> List.length) > 1 then
                        add "Warning" "MultipleCurvesSameSizeAndNotes" materialKey table ("Separate variants retained for product form " + product + ": " + json notes + "; " + (variants |> List.map location |> String.concat "; "))
        let identityColumns identity = [identity.Specification;identity.Grade;identity.Condition;identity.Alloy]
        let headers = ["MaterialKey";"Specification";"Grade";"ClassConditionTemper";"UNS"]
        let candidateIndex = groups |> Map.toSeq |> Seq.groupBy (fun (_,rows) -> (List.head rows).Identity.Specification) |> Seq.map (fun (spec,items) -> spec,List.ofSeq items) |> Map.ofSeq
        let candidates (identity: Identity) =
            Map.tryFind identity.Specification candidateIndex |> Option.defaultValue [] |> List.choose (fun (k,rows) ->
                let other = (List.head rows).Identity
                if key identity <> k && identity.Grade=other.Grade && (identity.Alloy=other.Alloy || identity.Alloy="" || other.Alloy="") then Some k else None)
        let missingRows = ResizeArray<string list>()
        let mutable missingSyOnly = 0
        let mutable missingSuOnly = 0
        let mutable missingBoth = 0
        let mutable missingWithStrengthCandidates = 0
        let mutable missingWithoutStrengthCandidates = 0
        let coverageRows = ResizeArray<string list>()
        for materialKey,rows in Map.toSeq groups do
            let available = rows |> List.filter (fun row -> not row.Values.IsEmpty) |> List.map (fun row -> propertyCode row.Table) |> Set.ofList
            if available.Contains "S1" || available.Contains "S2" || available.Contains "Sb1" || available.Contains "Sb2" then
                let hasSy,hasSu = available.Contains "Sy",available.Contains "Su"
                if not hasSy || not hasSu then
                    let category =
                        if not hasSy && not hasSu then missingBoth <- missingBoth + 1; "MissingBoth"
                        elif not hasSy then missingSyOnly <- missingSyOnly + 1; "MissingSy"
                        else missingSuOnly <- missingSuOnly + 1; "MissingSu"
                    let first = List.head rows
                    let alternatives = candidates first.Identity
                    let strengthCandidates code =
                        alternatives |> List.filter (fun k -> groups[k] |> List.exists (fun row -> row.Table=code && not row.Values.IsEmpty))
                    let syCandidates = if hasSy then [] else strengthCandidates "Sy"
                    let suCandidates = if hasSu then [] else strengthCandidates "Su"
                    if not syCandidates.IsEmpty || not suCandidates.IsEmpty then missingWithStrengthCandidates <- missingWithStrengthCandidates + 1
                    else missingWithoutStrengthCandidates <- missingWithoutStrengthCandidates + 1
                    missingRows.Add([materialKey] @ identityColumns first.Identity @ [string ids[materialKey];category;string hasSy;string hasSu;string (available.Contains "S1");string (available.Contains "S2");string (available.Contains "Sb1");string (available.Contains "Sb2");json alternatives;json syCandidates;json suCandidates;"Four-field identity (Specification,Grade,ClassConditionTemper,UNS); candidates are not automatically merged"])
                    add "Warning" category materialKey "Sy/Su" (if syCandidates.IsEmpty && suCandidates.IsEmpty then "No populated strength curve for this exact identity or plausible identity candidates" else "Possible strength curves under identity variants; inspect candidate keys in missing-strength.csv")
                for row in rows |> List.filter (fun row -> (List.contains (propertyCode row.Table) ["S1";"S2";"Sb1";"Sb2"]) && not row.Values.IsEmpty) do
                    for code in ["Sy";"Su"] do
                        let strength = rows |> List.filter (fun row -> row.Table=code && not row.Values.IsEmpty)
                        if not (covers row.Size (strength |> List.map (fun row -> row.Size))) then
                            coverageRows.Add([materialKey] @ identityColumns row.Identity @ [code;row.Table;row.File;string row.Line;field "Size/Thickness, mm" row.Fields;row.Size.Axis;"Not fully covered by known " + code + " size intervals; note-defined restrictions require separate review"])
        csv (Path.Combine(directory,"missing-strength.csv")) (headers @ ["MaterialId";"Category";"HasSy";"HasSu";"HasS1";"HasS2";"HasSb1";"HasSb2";"CandidateMaterialKeys";"CandidateSyMaterialKeys";"CandidateSuMaterialKeys";"MatchingPolicy"]) missingRows
        csv (Path.Combine(directory,"size-coverage.csv")) (headers @ ["MissingCoverage";"SourceTable";"File";"CsvLine";"SizeText";"SizeAxis";"Detail"]) coverageRows
        let allKeys = Set.union (groups |> Map.keys |> Set.ofSeq) (oldGroups |> Map.keys |> Set.ofSeq)
        let changes = ResizeArray<string list>()
        let rowChanges = ResizeArray<string list>()
        let counts = Dictionary<string,int>()
        let count status = counts[status] <- (match counts.TryGetValue status with true,v -> v | _ -> 0) + 1
        let subtract current previous =
            let remaining = Dictionary<string,int>()
            for value in previous do remaining[value] <- (match remaining.TryGetValue value with true,v -> v | _ -> 0) + 1
            current |> List.filter (fun value ->
                match remaining.TryGetValue value with
                | true,n when n>0 -> remaining[value] <- n-1; false
                | _ -> true)
        for materialKey in allKeys do
            let current = Map.tryFind materialKey groups
            let previous = Map.tryFind materialKey oldGroups
            let identity = match current,previous with Some rows,_ -> (List.head rows).Identity | _,Some rows -> (List.head rows).Identity | _ -> failwith "Impossible empty comparison key"
            let newPrints = current |> Option.map (List.map (fun row -> row.Fingerprint)) |> Option.defaultValue []
            let oldPrints = previous |> Option.bind (fun rows -> if rows |> List.forall (fun row -> row.Fingerprints.IsSome) then Some (rows |> List.collect (fun row -> row.Fingerprints.Value)) else None)
            let status =
                match current,previous,oldPrints with
                | Some _,None,_ -> "Added"
                | None,Some _,_ -> "RemovedFromSelectedScope"
                | Some _,Some _,None -> "RetainedLegacyComparisonUnavailable"
                | _,_,Some old -> if List.sort newPrints=List.sort old then "Unchanged" else "Modified"
                | _ -> failwith "Unsupported comparison state"
            count status
            let oldIds = previous |> Option.map (List.map (fun row -> row.Id) >> json) |> Option.defaultValue "[]"
            let newId = Map.tryFind materialKey ids |> Option.map string |> Option.defaultValue ""
            changes.Add([materialKey] @ identityColumns identity @ [status;oldIds;newId;json (candidates identity)])
            match oldPrints with
            | Some old ->
                for fingerprint in subtract newPrints old do rowChanges.Add [materialKey;"AddedSourceRow";fingerprint]
                for fingerprint in subtract old newPrints do rowChanges.Add [materialKey;"RemovedSourceRow";fingerprint]
            | None when previous.IsNone -> for fingerprint in newPrints do rowChanges.Add [materialKey;"AddedSourceRow";fingerprint]
            | _ -> ()
        csv (Path.Combine(directory,"material-changes.csv")) (headers @ ["Status";"PreviousIds";"NewId";"CandidateMaterialKeys"]) changes
        csv (Path.Combine(directory,"source-row-changes.csv")) ["MaterialKey";"Change";"Fingerprint"] rowChanges
        csv (Path.Combine(directory,"issues.csv")) ["Severity";"Code";"MaterialKey";"Source";"Detail"] (issues |> Seq.map (fun issue -> [issue.Severity;issue.Code;issue.MaterialKey;issue.Source;issue.Detail]))
        let noteRows = input.Notes |> List.map (fun note -> [note.Table;note.Code;note.Kind;note.Text])
        csv (Path.Combine(directory,"notes.csv")) ["SourceTable";"Code";"Kind";"Text"] noteRows
        // Note-body changes matter even when every stress value and row note code is unchanged.
        let oldNotes =
            if baselinePath = "none" then Map.empty
            else
                use previous = Database.connect baselinePath SqliteOpenMode.ReadOnly
                let tables = Database.query previous "SELECT name FROM sqlite_master WHERE type='table'" |> List.map (fun row -> string row[0]) |> Set.ofList
                if tables.Contains "Source_Notes" then
                    Database.query previous "SELECT SourceTable,Code,Kind,NoteText FROM Source_Notes WHERE upper(SourceTable) NOT IN ('6A','6B','6C','6D')"
                    |> List.map (fun row -> (string row[0],string row[1]),(string row[2],string row[3])) |> Map.ofList
                elif tables.Contains "NotesDatabase" then
                    Database.query previous "SELECT SourceTable,NoteCode,NoteText FROM NotesDatabase"
                    |> List.filter (fun row -> List.contains (string row[0]) ["Sy";"Su";"1A";"1B";"5A";"5B";"3";"4"])
                    |> List.map (fun row -> (string row[0],string row[1]),("Legacy",string row[2])) |> Map.ofList
                else Map.empty
        let currentNotes = input.Notes |> List.map (fun note -> (note.Table,note.Code),(note.Kind,note.Text)) |> Map.ofList
        let noteCounts = Dictionary<string,int>()
        let noteChanges =
            Set.union (oldNotes |> Map.keys |> Set.ofSeq) (currentNotes |> Map.keys |> Set.ofSeq)
            |> Seq.map (fun (table,code) ->
                let before = Map.tryFind (table,code) oldNotes
                let after = Map.tryFind (table,code) currentNotes
                let status =
                    match before,after with
                    | None,Some _ -> "Added"
                    | Some _,None -> "Removed"
                    | Some (_,oldText),Some (_,newText) when oldText=newText -> "Unchanged"
                    | _ -> "Modified"
                noteCounts[status] <- (match noteCounts.TryGetValue status with true,n -> n | _ -> 0) + 1
                [table;code;status;before |> Option.map snd |> Option.defaultValue "";after |> Option.map snd |> Option.defaultValue ""])
            |> Seq.toList
        csv (Path.Combine(directory,"note-changes.csv")) ["SourceTable";"Code";"Status";"PreviousText";"NewText"] noteChanges
        csv (Path.Combine(directory,"blank-revision-rows.csv")) ["SourceTable";"File";"CsvLine";"Revision";"Page";"SourceLine";"Reason"]
            (input.Omitted |> List.map (fun row -> [row.Table;row.File;string row.Line;field "Revision" row.Fields;field "Page No." row.Fields;field "Line No." row.Fields;row.Reason]))
        let coverage = Coverage.write csv directory databasePath
        let mechanicalRows = input.Rows |> List.filter (fun row -> isMechanicalTable row.Table)
        let tableCounts = input.Rows |> List.groupBy (fun row -> row.Table) |> List.map (fun (table,rows) -> {| SourceTable=table; PropertyCode=propertyCode table; Rows=rows.Length; Values=rows |> List.sumBy (fun row -> row.Values.Length) |})
        csv (Path.Combine(directory,"source-counts.csv")) ["SourceTable";"PropertyCode";"Rows";"Values"] (tableCounts |> List.map (fun row -> [row.SourceTable;row.PropertyCode;string row.Rows;string row.Values]))
        use db = Database.connect databasePath SqliteOpenMode.ReadOnly
        let dbMissing = Database.query db "SELECT count(*) FROM VW_Missing_Strength" |> List.head |> Array.head |> Convert.ToInt32
        if dbMissing <> missingRows.Count then failwith "Database/report missing-strength count mismatch"
        let compositionConflicts = issues |> Seq.filter (fun issue -> issue.Code="ConflictingNominalComposition") |> Seq.toList
        csv (Path.Combine(directory,"composition-conflicts.csv")) ["MaterialKey";"Source";"Detail"] (compositionConflicts |> List.map (fun issue -> [issue.MaterialKey;issue.Source;issue.Detail]))
        let summary = {|
            Edition=edition; Baseline=baselinePath; BaselineEdition=baselineEdition; ArchiveSHA256=(if input.SourceKind="ZIP" then input.ArchiveHash else null)
            SourceKind=input.SourceKind; SourcePath=input.SourcePath; SourceSHA256=input.ArchiveHash
            MaterialCount=groups.Count; IdentityFields=[|"Specification";"Grade";"ClassConditionTemper";"UNS"|]; ExcludedTables=[|"6A";"6B";"6C";"6D"|]; CompositionConflictCount=compositionConflicts.Length; SourceRowCount=mechanicalRows.Length; IdentitySourceRowCount=input.Rows.Length; MainCoverage=coverage; BlankRevisionRows=input.Omitted.Length; StressValueCount=input.Rows |> List.sumBy (fun row -> row.Values.Length)
            NoteDefinitionCount=input.Notes.Length; Tables=tableCounts; MaterialChanges=counts; NoteChanges=noteCounts
            MissingSyOnly=missingSyOnly; MissingSuOnly=missingSuOnly; MissingBoth=missingBoth; MissingStrengthTotal=missingRows.Count
            MissingWithStrengthCandidates=missingWithStrengthCandidates; MissingWithoutStrengthCandidates=missingWithoutStrengthCandidates
            SizeCoverageFindings=coverageRows.Count; Diagnostics=issues |> Seq.countBy (fun issue -> issue.Code) |> dict
            IgnoredFiles=input.Ignored
            Checks=[|"All source rows contribute to MAIN; mechanical row and value counts agree";"All table-scoped note definitions retained";"PRAGMA integrity_check: ok";"PRAGMA foreign_key_check: no violations";"Missing-strength view agrees with report";"Both MAIN matrices include all four-field unique materials; 6A-6D excluded"|]
            Limitations=[|"Material differences use exact normalized identities; removals mean absent from the supplied source tables, not confirmed ASME deletions.";"Legacy baselines support identities and available notes only; source-row differences require a database produced by this tool.";"Main.SMYS/SMUS are NULL when absent or not scalar; size-specific minima are retained in Property_Source.";"CSV export does not preserve alternative typeface; time-dependent styling is not inferred.";"Size coverage checks numeric intervals only; additional restrictions in notes remain applicable.";"Every table except 6A-6D contributes MAIN identities; only Sy/Su/S1/S2/Sb1/Sb2 curves are imported. Physical availability reports actual stored values, not absence from the ASME standard."|]
        |}
        writeJson (Path.Combine(directory,"summary.json")) summary
        let markdown = $"""# Database ASME {edition}

- Materiali: {groups.Count}
- Righe identità da tabelle ammesse (6A–6D escluse): {input.Rows.Length}; curve meccaniche: {mechanicalRows.Length}; valori di tensione: {summary.StressValueCount}
- Note definite: {input.Notes.Length}, distinte per tabella di origine.
- Con valori S1/S2/Sb1/Sb2 ma senza Sy: {missingSyOnly}; senza Su: {missingSuOnly}; senza entrambi: {missingBoth}.
- Fra questi, {missingWithStrengthCandidates} hanno possibili curve sotto identità diverse; {missingWithoutStrengthCandidates} non hanno candidati secondo i criteri conservativi del tool.
- Segnalazioni di copertura dimensionale Sy/Su: {coverageRows.Count}.

S1 = 1A + 1B; S2 = 5A + 5B; Sb1 = Table 3; Sb2 = Table 4. Temperature in °C, tensioni in MPa, dimensioni in mm.
MAIN è univoca per Specification, Grade, ClassConditionTemper e UNS; 6A–6D sono ignorate. ProductForm appartiene solo alle curve S1/S2. Le varianti di curva e le righe ammesse sono conservate. Note generali e specifiche restano collegate alla propria tabella, senza confondere codici uguali di tabelle differenti.

`material-changes.csv` confronta le identità con il database precedente. Una rimozione indica assenza dalle tabelle sorgenti, non una cancellazione normativa confermata. Le possibili varianti di identità sono segnalate e non fuse automaticamente. Con il database legacy il confronto dei valori precedenti non è disponibile; con un database prodotto dal tool, `source-row-changes.csv` identifica anche righe aggiunte/rimosse/modificate tramite impronte. `note-changes.csv` confronta separatamente i testi delle note, anche quando i valori delle curve non cambiano. `blank-revision-rows.csv` conserva le righe vuote con revisione senza inventarne l'identità.

`missing-strength.csv` distingue Sy, Su ed entrambi mancanti per identità esatta; `size-coverage.csv` aggiunge il controllo degli intervalli dimensionali. `issues.csv` riporta dati incoerenti, note non risolte e minimi non scalari. Le restrizioni scritte nelle note richiedono comunque lettura tecnica.

La nominal composition è unica in MAIN quando coerente. Notazioni con gli stessi elementi in diverso ordine sono equivalenti; composizioni effettivamente discordanti restano NULL e sono documentate in `composition-conflicts.csv`, senza duplicare il materiale. I minimi sono conservati in `Property_Source.MinYieldMPa/MinTensileMPa`; in `Main` sono NULL quando mancanti o differenti fra righe. Nessun valore viene inventato. Le altre proprietà restano non importate. `main-mechanical.csv` conserva ogni riga MAIN con i conteggi delle sei famiglie e le sole assenze applicabili. `main-bolting.csv` controlla Sy/Su/Sb1/Sb2, `main-nonbolting.csv` Sy/Su/S1/S2; le identità condivise compaiono in entrambi. Il report testuale termina al prospetto fisico, senza MissingData; `main-physical.csv` indica E, Alpha, c, k, a, density e Nu presenti nel database, comprese le costanti density/Nu. Un NO non prova una mancanza nella norma ASME.

Verificati conteggi sorgente/database, integrità SQLite, chiavi esterne e coerenza del report di assenza Sy/Su. Dettagli completi in `summary.json`.
"""
        File.WriteAllText(Path.Combine(directory,"README.md"), markdown)
        summary
