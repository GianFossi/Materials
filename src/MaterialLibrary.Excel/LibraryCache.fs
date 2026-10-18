namespace MaterialLibrary.Excel

open System.IO
open MaterialLibrary
open MaterialLibrary.Domain
open MaterialLibrary.Domain.Database.Lookup

/// <summary>Resolve content beside the XLL, including Excel-DNA's generated packed subdirectory.</summary>
module AddInPaths =
    /// <summary>Directory containing the 2025 reference and configuration; never Excel.exe's installation directory.</summary>
    let runtimeDirectory () =
        let directory =
            try Path.GetDirectoryName(ExcelDna.Integration.ExcelDnaUtil.XllPath)
            with _ -> System.AppContext.BaseDirectory
        let directory = if System.String.IsNullOrWhiteSpace directory then System.AppContext.BaseDirectory else directory
        let parent = Directory.GetParent directory
        if Path.GetFileName(directory).Equals("packed",System.StringComparison.OrdinalIgnoreCase) && not (isNull parent)
           && File.Exists(Path.Combine(parent.FullName,"data","asme_materials_2025.db")) then parent.FullName
        else directory

/// <summary>
/// Process-wide, thread-safe cache holding the materials currently available to Excel worksheet
/// functions. This is the only place in the Excel add-in allowed to hold mutable state; every
/// query function in this project reads a snapshot via <see cref="LibraryCache.current"/> and
/// otherwise stays as pure as the underlying MaterialLibrary API.
/// </summary>
/// <remarks>
/// Two sources can be loaded side by side:
/// <list type="bullet">
///   <item><description>
///   The ASME SQLite database (<see cref="AsmeMaterialRepository"/>) supplies identity, basic
///   properties, and allowable-stress datasets for every material it contains.
///   </description></item>
///   <item><description>
///   An optional complete JSON material library (<see cref="MaterialLibrarySerialization"/>)
///   additionally supplies stress-strain, creep, fatigue, stress-rupture, cyclic, external-pressure,
///   and Code Case 2964 data that the SQLite database does not store.
///   </description></item>
/// </list>
/// When both are loaded and a material ID appears in both, the JSON-sourced record wins, since it
/// carries the richer data set.
/// </remarks>
module LibraryCache =

    type private State =
        { DatabasePath: string option
          DatabaseMaterials: Material list
          JsonLibraryPath: string option
          JsonMaterials: Material list
          Combined: MaterialLibrary }

    let private gate = obj ()

    let mutable private state =
        { DatabasePath = None
          DatabaseMaterials = []
          JsonLibraryPath = None
          JsonMaterials = []
          Combined = MaterialLibrary.empty () }

    // Database-sourced materials are listed first so that json-sourced materials, added after them,
    // win ties: the MaterialLibrary constructor keeps the *last* occurrence of a duplicate ID.
    let private rebuildCombined (s: State) : MaterialLibrary =
        MaterialLibrary(s.DatabaseMaterials @ s.JsonMaterials)

    /// <summary>Resolves the default ASME database path via <see cref="Configuration.resolveAsmeDatabasePath"/>.</summary>
    let defaultDatabasePath () : string = Configuration.resolveAsmeDatabasePath (Some(AddInPaths.runtimeDirectory ()))

    /// <summary>Loads (or reloads) all materials from the ASME SQLite database at <paramref name="path"/>.</summary>
    /// <param name="path">Database file path; when <c>None</c>, resolves via <see cref="defaultDatabasePath"/>.</param>
    /// <returns><c>Ok materialCount</c>, or <c>Error message</c> describing why the load failed.</returns>
    let loadDatabase (path: string option) : Result<int, string> =
        lock gate (fun () ->
            let resolvedPath = path |> Option.defaultWith defaultDatabasePath

            match AsmeMaterialRepository.findMany resolvedPath MaterialSearchCriteria.empty with
            | Error err -> Error(sprintf "%A" err)
            | Ok materials ->
                let updated =
                    { state with
                        DatabasePath = Some resolvedPath
                        DatabaseMaterials = materials }

                state <-
                    { updated with
                        Combined = rebuildCombined updated }

                Ok(List.length materials))

    /// <summary>Loads (or reloads) a complete material-library JSON file (see <see cref="MaterialLibrarySerialization"/>).</summary>
    /// <param name="path">Path to a JSON file produced by <c>MaterialLibrarySerialization.saveToFile</c>.</param>
    /// <returns><c>Ok materialCount</c>, or <c>Error message</c> describing why the load failed.</returns>
    let loadJsonLibrary (path: string) : Result<int, string> =
        lock gate (fun () ->
            try
                let json = File.ReadAllText path

                match MaterialLibrarySerialization.fromJsonStringComplete json with
                | Error err -> Error(sprintf "%A" err)
                | Ok materials ->
                    let updated =
                        { state with
                            JsonLibraryPath = Some path
                            JsonMaterials = materials }

                    state <-
                        { updated with
                            Combined = rebuildCombined updated }

                    Ok(List.length materials)
            with ex ->
                Error ex.Message)

    /// <summary>
    /// Adds or replaces one material in the JSON-sourced side of the cache (e.g. after loading a
    /// single material file with <c>MatLoadMaterial</c>), then rebuilds the combined library.
    /// </summary>
    let addOrReplaceJsonMaterial (material: Material) : unit =
        lock gate (fun () ->
            let updated =
                { state with
                    JsonMaterials =
                        (state.JsonMaterials |> List.filter (fun m -> m.Id <> material.Id))
                        @ [ material ] }

            state <-
                { updated with
                    Combined = rebuildCombined updated })

    /// <summary>Loads the ASME database from its default path unless a source has already been loaded.</summary>
    let ensureLoaded () : unit =
        lock gate (fun () ->
            if state.DatabasePath.IsNone && state.JsonLibraryPath.IsNone then
                loadDatabase None |> ignore)

    /// <summary>Returns the currently cached, combined <see cref="MaterialLibrary"/>, loading the default database on first use.</summary>
    let current () : MaterialLibrary =
        ensureLoaded ()
        lock gate (fun () -> state.Combined)

    /// <summary>Human-readable status line describing what is currently loaded, for diagnostics.</summary>
    let status () : string =
        lock gate (fun () ->
            let dbPart =
                match state.DatabasePath with
                | Some p -> sprintf "Database: %s (%d materials)" p (List.length state.DatabaseMaterials)
                | None -> "Database: not loaded"

            let jsonPart =
                match state.JsonLibraryPath with
                | Some p -> sprintf "JSON library: %s (%d materials)" p (List.length state.JsonMaterials)
                | None -> "JSON library: not loaded"

            sprintf "%s | %s | Combined: %d materials" dbPart jsonPart state.Combined.Count)

    /// <summary>Active database for native property queries; defaults to the packaged 2025 edition.</summary>
    let databasePath () = lock gate (fun () -> state.DatabasePath |> Option.defaultWith defaultDatabasePath)

    /// <summary>Whether a worksheet material comes from native 2025 data rather than a JSON override.</summary>
    let useNativeFor materialId =
        ensureLoaded ()
        let overridden = lock gate (fun () -> state.JsonMaterials |> List.exists (fun m -> m.Id=materialId))
        if overridden then false
        else
            try
                use db = Asme2025Data.openRead (databasePath ())
                Asme2025Data.isNormalized db
            with _ -> false
