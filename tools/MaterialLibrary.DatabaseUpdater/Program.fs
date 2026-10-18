namespace MaterialLibrary.DatabaseUpdater

open System
open System.IO
open System.Text

/// <summary>Console interface for edition creation and read-only comparisons.</summary>
module Program =
    /// <summary>Display modes, defaults and examples without accessing source files.</summary>
    let help = """ASME DatabaseUpdater - console F# (.NET 8)

Uso:
  MaterialLibrary.DatabaseUpdater --sourcedir <directory> [opzioni]

  --help                         Mostra questa guida.
  --sourcedir <directory>         Cartella con sottocartelle ASME: 1A, 1B, 5A, 5B, Sy, Su, ...
                                 Legge CSV (anche .cvs) e notes.txt. MAIN univoca su 4 campi (escluse 6A-6D); importa Sy, Su, S1, S2, Sb1 (3), Sb2 (4).
  --newdatabasefile [file.db]     Crea un nuovo database. Default: asme_materials_<anno>.db.
  --compare [file.db]             Confronta soltanto. Default: asme_materials.db.
                                 Non modifica il file; il report va sul terminale o in --report.
  --report <Nomefile.txt>         Salva il report completo UTF-8 anziché mostrarlo a video.
  --edition <anno>               Anno a quattro cifre; di default rilevato dagli export.
  --report-dir <directory>       Salva anche report CSV/JSON dettagliati (facoltativo).

Chiave MAIN: Specification, Grade, ClassConditionTemper, UNS. ProductForm solo S1/S2.
Ogni creazione verifica e stampa MAIN: conteggi Sy/Su/S1/S2/Sb1/Sb2,
assenze per materiale e presenza E/Alpha/c/k/a/density/Nu.

Se non si specifica --compare viene creato il nuovo database con il nome predefinito.
--compare e --newdatabasefile possono essere combinati per confrontare e creare.
File e cartelle di output esistenti non vengono sovrascritti.

Esempi:
  dotnet run --project tools/MaterialLibrary.DatabaseUpdater -- --sourcedir "./ASME Database" --newdatabasefile
  dotnet run --project tools/MaterialLibrary.DatabaseUpdater -- --sourcedir "./ASME Database" --compare --report differenze.txt
  dotnet run --project tools/MaterialLibrary.DatabaseUpdater -- --sourcedir "./ASME Database" --compare precedente.db --newdatabasefile nuovo.db

Compatibilità: --input <archivio.zip> al posto di --sourcedir; --output <file.db>
al posto di --newdatabasefile; --baseline <file.db> come alias di --compare.
"""

    /// <summary>Parse required values and optional filenames, rejecting duplicate and unknown switches.</summary>
    let arguments (argv: string array) =
        let required = Set.ofList ["--input";"--output";"--edition";"--baseline";"--report-dir";"--sourcedir";"--report"]
        let optional = Set.ofList ["--newdatabasefile";"--compare"]
        let mutable index = 0
        let mutable result = Map.empty
        while index < argv.Length do
            let name = argv[index]
            if not (required.Contains name || optional.Contains name || name="--help") then
                failwith (if name="--updatedatabasefile" then "Use --compare instead of --updatedatabasefile" else "Unknown option: " + name)
            if Map.containsKey name result then failwith ("Duplicate option: " + name)
            let hasValue = index+1 < argv.Length && not (argv[index+1].StartsWith("--",StringComparison.Ordinal))
            if required.Contains name && not hasValue then failwith ("Required value: " + name)
            if name="--help" then result <- Map.add name "" result; index <- index+1
            elif hasValue then
                if String.IsNullOrWhiteSpace argv[index+1] then failwith ("Empty value: " + name)
                result <- Map.add name argv[index+1] result
                index <- index+2
            else result <- Map.add name "" result; index <- index+1
        for left,right in ["--sourcedir","--input";"--newdatabasefile","--output";"--compare","--baseline"] do
            if Map.containsKey left result && Map.containsKey right result then failwith ("Choose either " + left + " or " + right)
        result

    /// <summary>Execute creation or a temporary, read-only comparison; publish output files only after validation.</summary>
    let private execute (options: Map<string,string>) =
        let get name = Map.tryFind name options
        let full = Option.map Path.GetFullPath
        let sourcePath,isDirectory =
            match get "--sourcedir",get "--input" with
            | Some path,None -> Path.GetFullPath path,true
            | None,Some path -> Path.GetFullPath path,false
            | _ -> failwith "Specify --sourcedir <directory> or --input <archive.zip>"
        if isDirectory && not (Directory.Exists sourcePath) then failwith ("Source directory not found: " + sourcePath)
        if not isDirectory && not (File.Exists sourcePath) then failwith ("Input ZIP not found: " + sourcePath)
        let edition = get "--edition" |> Option.defaultWith (fun () -> Input.detectEdition sourcePath)
        if not (System.Text.RegularExpressions.Regex.IsMatch(edition,@"^\d{4}$")) then failwith "Edition must be a four-digit year"
        let baselinePath =
            get "--compare" |> Option.map (fun value -> if value="" then "asme_materials.db" else value)
            |> Option.orElseWith (fun () -> get "--baseline") |> full
        let create = baselinePath.IsNone || Map.containsKey "--newdatabasefile" options || Map.containsKey "--output" options
        let output =
            if create then
                get "--newdatabasefile" |> Option.orElseWith (fun () -> get "--output")
                |> Option.filter ((<>) "") |> Option.defaultValue ("asme_materials_" + edition + ".db") |> Path.GetFullPath |> Some
            else None
        let reports = get "--report-dir" |> full
        let reportFile = get "--report" |> full
        let destinations = [output;reports;reportFile] |> List.choose id
        if (destinations |> List.distinct).Length <> destinations.Length then failwith "Database, report directory and text report must have different paths"
        for destination in destinations do
            if File.Exists destination || Directory.Exists destination then failwith ("Output already exists; choose a new path: " + destination)
        // Prevent an output directory from being a parent of another final artifact.
        reports |> Option.iter (fun folder ->
            for destination in [output;reportFile] |> List.choose id do
                let relative = Path.GetRelativePath(folder,destination)
                if not (Path.IsPathRooted relative) && relative <> ".." && not (relative.StartsWith(".." + string Path.DirectorySeparatorChar,StringComparison.Ordinal)) then
                    failwith "Place the database/text report outside --report-dir")
        let baseline,baselineEdition = match baselinePath with Some path -> Database.loadBaseline path | None -> [],"none"
        let input = if isDirectory then Input.readDirectory sourcePath edition else Input.readArchive sourcePath edition
        for destination in destinations do Directory.CreateDirectory(Path.GetDirectoryName destination) |> ignore
        let workspace = Directory.CreateTempSubdirectory("materials-updater-")
        let suffix = ".building-" + Guid.NewGuid().ToString("N")
        let tempOutput = output |> Option.map (fun path -> path+suffix) |> Option.defaultValue (Path.Combine(workspace.FullName,"comparison.db"))
        let tempReports = reports |> Option.map (fun path -> path+suffix) |> Option.defaultValue (Path.Combine(workspace.FullName,"reports"))
        let tempReport = reportFile |> Option.map (fun path -> path+suffix)
        let published = ResizeArray<unit -> unit>()
        let mutable committed = false
        try
            let ids = Database.create tempOutput edition input baseline
            Reports.write tempReports edition (Option.defaultValue "none" baselinePath) baselineEdition input baseline ids tempOutput |> ignore
            let mode = match create,baselinePath with true,Some _ -> "creazione e confronto" | true,None -> "creazione" | _ -> "solo confronto (database invariato)"
            let rendered = TextReport.render tempReports mode output reports
            tempReport |> Option.iter (fun path -> File.WriteAllText(path,rendered,UTF8Encoding(false)))
            output |> Option.iter (fun path -> File.Move(tempOutput,path,false); published.Add(fun () -> File.Delete path))
            reports |> Option.iter (fun path -> Directory.Move(tempReports,path); published.Add(fun () -> Directory.Delete(path,true)))
            match tempReport,reportFile with
            | Some temporary,Some final -> File.Move(temporary,final,false); published.Add(fun () -> File.Delete final)
            | _ -> ()
            committed <- true
            if reportFile.IsNone then Console.Out.Write rendered
            0
        finally
            if not committed then for undo in Seq.rev published do undo()
            if File.Exists tempOutput then File.Delete tempOutput
            if Directory.Exists tempReports then Directory.Delete(tempReports,true)
            tempReport |> Option.iter (fun path -> if File.Exists path then File.Delete path)
            workspace.Delete(true)

    /// <summary>Run a console command, including side-effect-free help.</summary>
    let run argv =
        let options = arguments argv
        if Map.containsKey "--help" options then Console.Out.Write help; 0
        else execute options

    /// <summary>Return nonzero on failure, preserving diagnostics on stderr even with --report.</summary>
    [<EntryPoint>]
    let main argv =
        try run argv
        with ex -> eprintfn "Import failed: %s" ex.Message; 1
