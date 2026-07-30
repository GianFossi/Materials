namespace MaterialLibrary.Excel

open ExcelDna.Integration
open MaterialLibrary
open MaterialLibrary.Domain.Database.Lookup

/// <summary>
/// Excel worksheet functions for resolving default database paths, checking file/database
/// accessibility, and reading/writing the <see cref="LibraryConfiguration"/> file.
/// </summary>
module ConfigurationFunctions =

    let private describeConfig (path: string) (cfg: LibraryConfiguration) : string =
        sprintf
            "%s | ASME: %s | EN: %s"
            path
            (Configuration.getAsmeDatabasePath cfg)
            (Configuration.getEnDatabasePath cfg)

    // ── Read: default paths and accessibility ─────────────────────────────

    [<ExcelFunction(Category = "MaterialLibrary.Config", Description = "Resolves the configuration file path this add-in uses by default (next to the add-in's runtime folder).")>]
    let MatConfigPath () : string = Configuration.resolveConfigPath None

    [<ExcelFunction(Category = "MaterialLibrary.Config", Description = "Resolves the default ASME database path: from the configuration file if present, otherwise a sibling ASME_Material_DB.sqlite.")>]
    let MatDefaultAsmeDatabasePath () : string = Configuration.resolveAsmeDatabasePath None

    [<ExcelFunction(Category = "MaterialLibrary.Config", Description = "Resolves the default EN database path: from the configuration file if present, otherwise a sibling en_materials.db.")>]
    let MatDefaultEnDatabasePath () : string = Configuration.resolveEnDatabasePath None

    [<ExcelFunction(Category = "MaterialLibrary.Config", Description = "Checks that a file exists and can actually be opened for reading. Returns \"OK\" or an explanatory error.")>]
    let MatCheckFileAccessible
        ([<ExcelArgument(Description = "File path to check.")>] path: string)
        : string =
        match Configuration.checkFileAccessible path with
        | Ok() -> "OK"
        | Error message -> sprintf "#VALUE! %s" message

    [<ExcelFunction(Category = "MaterialLibrary.Config", Description = "Checks that the ASME SQLite database at the given path exists and can actually be opened as a SQLite database. Returns \"OK\" or an explanatory error.")>]
    let MatCheckDatabaseAccessible
        ([<ExcelArgument(Description = "ASME database file path.")>] path: string)
        : string =
        match AsmeMaterialRepository.checkAccessible path with
        | Ok() -> "OK"
        | Error err -> ExcelHelpers.materialErrorToText err

    // ── Read: full configuration dump ─────────────────────────────────────

    [<ExcelFunction(Category = "MaterialLibrary.Config", Description = "Loads (or creates a default) configuration file and returns every value as a flattened key/value table.")>]
    let MatConfigTable
        ([<ExcelArgument(Description = "Configuration file path; blank uses the default path next to the add-in.")>] path: obj)
        : obj[,] =
        let resolvedPath = Args.optionalText (Configuration.resolveConfigPath None) path

        match Configuration.loadOrCreateDefault resolvedPath with
        | Error message -> ExcelHelpers.errorGrid (sprintf "#VALUE! %s" message)
        | Ok cfg ->
            let interpolationRows (name: string) (section: InterpolationSectionOptions) =
                [ [ box (sprintf "Interpolation.%s.Mode" name); box section.Mode ]
                  [ box (sprintf "Interpolation.%s.LagrangeDegree" name); box section.LagrangeDegree ]
                  [ box (sprintf "Interpolation.%s.ExtrapolateFlat" name); box section.ExtrapolateFlat ] ]

            let rows =
                [ [ box "ConfigurationVersion"; box cfg.ConfigurationVersion ]
                  [ box "General.EnableDiagnostics"; box cfg.General.EnableDiagnostics ]
                  [ box "General.StrictValidation"; box cfg.General.StrictValidation ]
                  [ box "General.DefaultMaterialId"; box cfg.General.DefaultMaterialId ] ]
                @ interpolationRows "SpecificHeat" cfg.Interpolation.SpecificHeat
                @ interpolationRows "StressStrain" cfg.Interpolation.StressStrain
                @ interpolationRows "CreepTable" cfg.Interpolation.CreepTable
                @ interpolationRows "StressRupture" cfg.Interpolation.StressRupture
                @ interpolationRows "Fatigue" cfg.Interpolation.Fatigue
                @ [ [ box "Creep.NortonA"; box cfg.Creep.NortonA ]
                    [ box "Creep.NortonN"; box cfg.Creep.NortonN ]
                    [ box "Creep.NortonM"; box cfg.Creep.NortonM ]
                    [ box "Creep.GarofaloA"; box cfg.Creep.GarofaloA ]
                    [ box "Creep.GarofaloN"; box cfg.Creep.GarofaloN ]
                    [ box "Creep.GarofaloM"; box cfg.Creep.GarofaloM ]
                    [ box "Creep.GarofaloAlpha"; box cfg.Creep.GarofaloAlpha ]
                    [ box "Creep.KachanovA1"; box cfg.Creep.KachanovA1 ]
                    [ box "Creep.KachanovN1"; box cfg.Creep.KachanovN1 ]
                    [ box "Creep.KachanovM1"; box cfg.Creep.KachanovM1 ]
                    [ box "Creep.KachanovA2"; box cfg.Creep.KachanovA2 ]
                    [ box "Creep.KachanovN2"; box cfg.Creep.KachanovN2 ]
                    [ box "Creep.KachanovM2"; box cfg.Creep.KachanovM2 ]
                    [ box "Creep.KachanovTimeSteps"; box cfg.Creep.KachanovTimeSteps ]
                    [ box "Creep.KachanovTotalHours"; box cfg.Creep.KachanovTotalHours ]
                    [ box "Io.DataFolder"; box cfg.Io.DataFolder ]
                    [ box "Io.ExportFolder"; box cfg.Io.ExportFolder ]
                    [ box "Io.AutoCreateFolders"; box cfg.Io.AutoCreateFolders ]
                    [ box "Io.MaterialDatabaseFolder"; box cfg.Io.MaterialDatabaseFolder ]
                    [ box "Io.AsmeMaterialDatabaseFile"; box cfg.Io.AsmeMaterialDatabaseFile ]
                    [ box "Io.EnMaterialDatabaseFile"; box cfg.Io.EnMaterialDatabaseFile ] ]

            ExcelHelpers.gridOfRows [ "Key"; "Value" ] rows

    // ── Write: targeted configuration updates ─────────────────────────────

    [<ExcelFunction(Category = "MaterialLibrary.Config", Description = "Loads (or creates) the configuration file, replaces the database folder and/or file names (blank arguments keep the current value), and saves it.")>]
    let MatConfigSetDatabasePaths
        ([<ExcelArgument(Description = "Configuration file path; blank uses the default path next to the add-in.")>] path: obj)
        ([<ExcelArgument(Description = "Folder containing the ASME/EN database files; blank leaves it unchanged.")>] folder: obj)
        ([<ExcelArgument(Description = "ASME database file name; blank leaves it unchanged.")>] asmeFileName: obj)
        ([<ExcelArgument(Description = "EN database file name; blank leaves it unchanged.")>] enFileName: obj)
        : string =
        let resolvedPath = Args.optionalText (Configuration.resolveConfigPath None) path

        let update (cfg: LibraryConfiguration) =
            let withFolder =
                match Args.optionalTextOption folder with
                | Some value -> Configuration.setDatabaseFolder value cfg
                | None -> cfg

            let withAsmeFile =
                match Args.optionalTextOption asmeFileName with
                | Some value -> Configuration.setAsmeDatabaseFileName value withFolder
                | None -> withFolder

            match Args.optionalTextOption enFileName with
            | Some value -> Configuration.setEnDatabaseFileName value withAsmeFile
            | None -> withAsmeFile

        match Configuration.updateAndSave resolvedPath update with
        | Ok cfg -> describeConfig resolvedPath cfg
        | Error message -> sprintf "#VALUE! %s" message

    [<ExcelFunction(Category = "MaterialLibrary.Config", Description = "Loads (or creates) the configuration file, replaces general options (blank arguments keep the current value), and saves it.")>]
    let MatConfigSetGeneralOptions
        ([<ExcelArgument(Description = "Configuration file path; blank uses the default path next to the add-in.")>] path: obj)
        ([<ExcelArgument(Description = "Enable diagnostics (TRUE/FALSE); blank leaves it unchanged.")>] enableDiagnostics: obj)
        ([<ExcelArgument(Description = "Strict validation (TRUE/FALSE); blank leaves it unchanged.")>] strictValidation: obj)
        ([<ExcelArgument(Description = "Default material ID; blank leaves it unchanged.")>] defaultMaterialId: obj)
        : string =
        let resolvedPath = Args.optionalText (Configuration.resolveConfigPath None) path

        let update (cfg: LibraryConfiguration) =
            let current = cfg.General

            Configuration.setGeneralOptions
                { EnableDiagnostics = Args.optionalBool current.EnableDiagnostics enableDiagnostics
                  StrictValidation = Args.optionalBool current.StrictValidation strictValidation
                  DefaultMaterialId = Args.optionalText current.DefaultMaterialId defaultMaterialId }
                cfg

        match Configuration.updateAndSave resolvedPath update with
        | Ok cfg ->
            sprintf
                "%s | Diagnostics=%b, StrictValidation=%b, DefaultMaterialId=%s"
                resolvedPath
                cfg.General.EnableDiagnostics
                cfg.General.StrictValidation
                cfg.General.DefaultMaterialId
        | Error message -> sprintf "#VALUE! %s" message

    [<ExcelFunction(Category = "MaterialLibrary.Config", Description = "Loads (or creates) the configuration file, replaces one interpolation section's mode/degree/extrapolation (blank arguments keep the current value), and saves it. section: SpecificHeat, StressStrain, CreepTable, StressRupture, Fatigue.")>]
    let MatConfigSetInterpolationMode
        ([<ExcelArgument(Description = "Configuration file path; blank uses the default path next to the add-in.")>] path: obj)
        ([<ExcelArgument(Description = "Section name: SpecificHeat, StressStrain, CreepTable, StressRupture, Fatigue.")>] section: string)
        ([<ExcelArgument(Description = "Interpolation mode: Linear, CubicSpline, Constant, LagrangePolynomial; blank leaves it unchanged.")>] mode: obj)
        ([<ExcelArgument(Description = "Lagrange polynomial degree, used only when mode is LagrangePolynomial; blank leaves it unchanged.")>] lagrangeDegree: obj)
        ([<ExcelArgument(Description = "Flat-extrapolate outside the tabulated range (TRUE/FALSE); blank leaves it unchanged.")>] extrapolateFlat: obj)
        : string =
        let resolvedPath = Args.optionalText (Configuration.resolveConfigPath None) path

        let currentSection (cfg: LibraryConfiguration) : InterpolationSectionOptions option =
            match section.Trim().ToLowerInvariant() with
            | "specificheat" -> Some cfg.Interpolation.SpecificHeat
            | "stressstrain" -> Some cfg.Interpolation.StressStrain
            | "creeptable" -> Some cfg.Interpolation.CreepTable
            | "stressrupture" -> Some cfg.Interpolation.StressRupture
            | "fatigue" -> Some cfg.Interpolation.Fatigue
            | _ -> None

        match Configuration.loadOrCreateDefault resolvedPath with
        | Error message -> sprintf "#VALUE! %s" message
        | Ok cfg ->
            match currentSection cfg with
            | None -> sprintf "#VALUE! Unknown interpolation section: %s" section
            | Some current ->
                let updatedSection =
                    { Mode = Args.optionalText current.Mode mode
                      LagrangeDegree = Args.optionalNumber (float current.LagrangeDegree) lagrangeDegree |> int
                      ExtrapolateFlat = Args.optionalBool current.ExtrapolateFlat extrapolateFlat }

                let update cfg =
                    Configuration.setInterpolationSection section updatedSection cfg
                    |> function
                        | Ok updated -> updated
                        | Error _ -> cfg

                match Configuration.updateAndSave resolvedPath update with
                | Ok _ -> sprintf "%s | %s.Mode=%s, LagrangeDegree=%d, ExtrapolateFlat=%b" resolvedPath section updatedSection.Mode updatedSection.LagrangeDegree updatedSection.ExtrapolateFlat
                | Error message -> sprintf "#VALUE! %s" message

    [<ExcelFunction(Category = "MaterialLibrary.Config", Description = "Loads (or creates) the configuration file, replaces creep-model default coefficients (blank arguments keep the current value), and saves it.")>]
    let MatConfigSetCreepDefaults
        ([<ExcelArgument(Description = "Configuration file path; blank uses the default path next to the add-in.")>] path: obj)
        ([<ExcelArgument(Description = "Norton A coefficient; blank keeps the current value.")>] nortonA: obj)
        ([<ExcelArgument(Description = "Norton n exponent; blank keeps the current value.")>] nortonN: obj)
        ([<ExcelArgument(Description = "Norton m exponent; blank keeps the current value.")>] nortonM: obj)
        ([<ExcelArgument(Description = "Garofalo A coefficient; blank keeps the current value.")>] garofaloA: obj)
        ([<ExcelArgument(Description = "Garofalo n exponent; blank keeps the current value.")>] garofaloN: obj)
        ([<ExcelArgument(Description = "Garofalo m exponent; blank keeps the current value.")>] garofaloM: obj)
        ([<ExcelArgument(Description = "Garofalo alpha, 1/MPa; blank keeps the current value.")>] garofaloAlpha: obj)
        ([<ExcelArgument(Description = "Kachanov-Omega A1 coefficient; blank keeps the current value.")>] kachanovA1: obj)
        ([<ExcelArgument(Description = "Kachanov-Omega N1 exponent; blank keeps the current value.")>] kachanovN1: obj)
        ([<ExcelArgument(Description = "Kachanov-Omega M1 exponent; blank keeps the current value.")>] kachanovM1: obj)
        ([<ExcelArgument(Description = "Kachanov-Omega A2 coefficient; blank keeps the current value.")>] kachanovA2: obj)
        ([<ExcelArgument(Description = "Kachanov-Omega N2 exponent; blank keeps the current value.")>] kachanovN2: obj)
        ([<ExcelArgument(Description = "Kachanov-Omega M2 exponent; blank keeps the current value.")>] kachanovM2: obj)
        ([<ExcelArgument(Description = "Default Kachanov-Omega integration time steps; blank keeps the current value.")>] kachanovTimeSteps: obj)
        ([<ExcelArgument(Description = "Default Kachanov-Omega total simulation time, hours; blank keeps the current value.")>] kachanovTotalHours: obj)
        : string =
        let resolvedPath = Args.optionalText (Configuration.resolveConfigPath None) path

        let update (cfg: LibraryConfiguration) =
            let current = cfg.Creep

            Configuration.setCreepDefaults
                { NortonA = Args.optionalNumber current.NortonA nortonA
                  NortonN = Args.optionalNumber current.NortonN nortonN
                  NortonM = Args.optionalNumber current.NortonM nortonM
                  GarofaloA = Args.optionalNumber current.GarofaloA garofaloA
                  GarofaloN = Args.optionalNumber current.GarofaloN garofaloN
                  GarofaloM = Args.optionalNumber current.GarofaloM garofaloM
                  GarofaloAlpha = Args.optionalNumber current.GarofaloAlpha garofaloAlpha
                  KachanovA1 = Args.optionalNumber current.KachanovA1 kachanovA1
                  KachanovN1 = Args.optionalNumber current.KachanovN1 kachanovN1
                  KachanovM1 = Args.optionalNumber current.KachanovM1 kachanovM1
                  KachanovA2 = Args.optionalNumber current.KachanovA2 kachanovA2
                  KachanovN2 = Args.optionalNumber current.KachanovN2 kachanovN2
                  KachanovM2 = Args.optionalNumber current.KachanovM2 kachanovM2
                  KachanovTimeSteps = Args.optionalNumber (float current.KachanovTimeSteps) kachanovTimeSteps |> int
                  KachanovTotalHours = Args.optionalNumber current.KachanovTotalHours kachanovTotalHours }
                cfg

        match Configuration.updateAndSave resolvedPath update with
        | Ok _ -> sprintf "Saved creep defaults to %s" resolvedPath
        | Error message -> sprintf "#VALUE! %s" message
