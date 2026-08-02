namespace MaterialLibrary

open System
open System.IO
open System.Xml.Serialization
open MaterialLibrary.Interpolation

/// CULTURE RULE: Numeric parsing and formatting for XML/JSON persistence must always use CultureInfo.InvariantCulture.

[<CLIMutable>]
type GeneralOptions =
    { EnableDiagnostics: bool
      StrictValidation: bool
      DefaultMaterialId: string }

[<CLIMutable>]
type InterpolationSectionOptions =
    { Mode: string
      LagrangeDegree: int
      ExtrapolateFlat: bool }

[<CLIMutable>]
type InterpolationOptions =
    { SpecificHeat: InterpolationSectionOptions
      StressStrain: InterpolationSectionOptions
      CreepTable: InterpolationSectionOptions
      StressRupture: InterpolationSectionOptions
      Fatigue: InterpolationSectionOptions }

[<CLIMutable>]
type CreepDefaults =
    { NortonA: float
      NortonN: float
      NortonM: float
      GarofaloA: float
      GarofaloN: float
      GarofaloM: float
      GarofaloAlpha: float
      KachanovA1: float
      KachanovN1: float
      KachanovM1: float
      KachanovA2: float
      KachanovN2: float
      KachanovM2: float
      KachanovTimeSteps: int
      KachanovTotalHours: float }

[<CLIMutable>]
type IoOptions =
    { DataFolder: string
      ExportFolder: string
      AutoCreateFolders: bool
      MaterialDatabaseFolder: string
      AsmeMaterialDatabaseFile: string
      EnMaterialDatabaseFile: string }

[<CLIMutable>]
type LibraryConfiguration =
    { ConfigurationVersion: string
      General: GeneralOptions
      Interpolation: InterpolationOptions
      Creep: CreepDefaults
      Io: IoOptions }

module Configuration =

    let private isFinite value =
        not (Double.IsNaN value || Double.IsInfinity value)

    let private validateInterpolationSection name (options: InterpolationSectionOptions) =
        if isNull (box options) then
            Error $"{name} interpolation configuration is missing"
        elif String.IsNullOrWhiteSpace options.Mode then
            Error $"{name} interpolation mode is required"
        else
            match options.Mode.Trim().ToLowerInvariant() with
            | "linear"
            | "cubicspline"
            | "constant" -> Ok()
            | "lagrangepolynomial" when options.LagrangeDegree >= 1 -> Ok()
            | "lagrangepolynomial" -> Error $"{name} Lagrange degree must be >= 1"
            | invalid -> Error $"Unsupported {name} interpolation mode: {invalid}"

    /// Validates configuration loaded from XML before it reaches numerical or I/O code.
    let validate (config: LibraryConfiguration) : Result<LibraryConfiguration, string> =
        if isNull (box config) then
            Error "Configuration cannot be null"
        elif
            [ box config.General; box config.Interpolation; box config.Creep; box config.Io ]
            |> List.exists isNull
        then
            Error "Configuration contains a missing section"
        elif String.IsNullOrWhiteSpace config.ConfigurationVersion then
            Error "ConfigurationVersion is required"
        else
            let interpolationSections =
                [ "SpecificHeat", config.Interpolation.SpecificHeat
                  "StressStrain", config.Interpolation.StressStrain
                  "CreepTable", config.Interpolation.CreepTable
                  "StressRupture", config.Interpolation.StressRupture
                  "Fatigue", config.Interpolation.Fatigue ]

            let interpolationError =
                interpolationSections
                |> List.tryPick (fun (name, section) ->
                    match validateInterpolationSection name section with
                    | Ok() -> None
                    | Error error -> Some error)

            let creepValues =
                [ config.Creep.NortonA
                  config.Creep.NortonN
                  config.Creep.NortonM
                  config.Creep.GarofaloA
                  config.Creep.GarofaloN
                  config.Creep.GarofaloM
                  config.Creep.GarofaloAlpha
                  config.Creep.KachanovA1
                  config.Creep.KachanovN1
                  config.Creep.KachanovM1
                  config.Creep.KachanovA2
                  config.Creep.KachanovN2
                  config.Creep.KachanovM2
                  config.Creep.KachanovTotalHours ]

            match interpolationError with
            | Some error -> Error error
            | None when creepValues |> List.exists (fun value -> not (isFinite value) || value < 0.0) ->
                Error "Creep defaults must be finite and non-negative"
            | None when config.Creep.KachanovTimeSteps <= 0 ->
                Error "KachanovTimeSteps must be > 0"
            | None
                when [ config.Io.DataFolder
                       config.Io.ExportFolder
                       config.Io.MaterialDatabaseFolder
                       config.Io.AsmeMaterialDatabaseFile
                       config.Io.EnMaterialDatabaseFile ]
                     |> List.exists String.IsNullOrWhiteSpace ->
                Error "All configured data and database paths are required"
            | None -> Ok config

    let private defaultInterpolationSection mode =
        { Mode = mode
          LagrangeDegree = 3
          ExtrapolateFlat = false }

    let createDefault () : LibraryConfiguration =
        { ConfigurationVersion = "1.0.2"
          General =
            { EnableDiagnostics = false
              StrictValidation = true
              DefaultMaterialId = "" }
          Interpolation =
            { SpecificHeat = defaultInterpolationSection "Linear"
              StressStrain = defaultInterpolationSection "Linear"
              CreepTable = defaultInterpolationSection "Linear"
              StressRupture = defaultInterpolationSection "Linear"
              Fatigue = defaultInterpolationSection "Linear" }
          Creep =
            { NortonA = 1e-12
              NortonN = 3.0
              NortonM = 0.3
              GarofaloA = 1e-10
              GarofaloN = 2.0
              GarofaloM = 0.4
              GarofaloAlpha = 0.01
              KachanovA1 = 5e-11
              KachanovN1 = 2.0
              KachanovM1 = 1.5
              KachanovA2 = 1e-8
              KachanovN2 = 1.0
              KachanovM2 = 2.0
              KachanovTimeSteps = 100
              KachanovTotalHours = 1000.0 }
          Io =
            { DataFolder = "./data"
              ExportFolder = "./export"
              AutoCreateFolders = true
              MaterialDatabaseFolder = @"C:\Users\ganfossi\Documents\DataBase\data"
              AsmeMaterialDatabaseFile = "ASME_Materials.db"
              EnMaterialDatabaseFile = "en_materials.db" } }

    /// <summary>
    /// Serializes <see cref="LibraryConfiguration"/> to an XML file.
    /// </summary>
    /// <remarks>
    /// This function stores configuration values only. If Material entities are serialized,
    /// each Material property/table must include XML comments that state fixed units explicitly.
    /// Numeric formatting for persistence must be culture-invariant.
    /// </remarks>
    let save (path: string) (config: LibraryConfiguration) : Result<unit, string> =
        validate config
        |> Result.bind (fun validConfig ->
            let mutable temporaryPath = None

            try
                let fullPath = Path.GetFullPath(path)
                let dir = Path.GetDirectoryName(fullPath)

                use buffer = new MemoryStream()
                let serializer = XmlSerializer(typeof<LibraryConfiguration>)
                serializer.Serialize(buffer, validConfig)

                if not (String.IsNullOrWhiteSpace(dir)) && not (Directory.Exists(dir)) then
                    Directory.CreateDirectory(dir) |> ignore

                let tempPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp"
                temporaryPath <- Some tempPath
                File.WriteAllBytes(tempPath, buffer.ToArray())
                File.Move(tempPath, fullPath, true)
                temporaryPath <- None
                Ok()
            with ex ->
                temporaryPath
                |> Option.iter (fun tempPath ->
                    try
                        if File.Exists(tempPath) then
                            File.Delete(tempPath)
                    with _ ->
                        ())

                Error $"Could not save XML configuration to '{path}': {ex.Message}")

    /// <summary>
    /// Deserializes <see cref="LibraryConfiguration"/> from an XML file.
    /// </summary>
    /// <remarks>
    /// When reading data that references material properties, units are assumed fixed a priori
    /// as documented in source XML comments and project README.
    /// Numeric parsing for persistence must be culture-invariant.
    /// </remarks>
    let load (path: string) : Result<LibraryConfiguration, string> =
        try
            if not (File.Exists(path)) then
                Error $"Configuration file not found: {path}"
            else
                use stream = File.OpenRead(path)
                let serializer = XmlSerializer(typeof<LibraryConfiguration>)
                let config = serializer.Deserialize(stream) :?> LibraryConfiguration
                validate config
        with ex ->
            Error $"Could not read XML configuration from '{path}': {ex.Message}"

    let loadOrCreateDefault (path: string) : Result<LibraryConfiguration, string> =
        if File.Exists(path) then
            load path
        else
            let config = createDefault ()

            match save path config with
            | Ok() -> Ok config
            | Error e -> Error e

    let toInterpolationMode (options: InterpolationSectionOptions) : InterpolationMode =
        let mode =
            if isNull (box options) || String.IsNullOrWhiteSpace options.Mode then
                ""
            else
                options.Mode.Trim().ToLowerInvariant()

        match mode with
        | "linear" -> InterpolationMode.Linear
        | "cubicspline" -> InterpolationMode.CubicSpline
        | "constant" -> InterpolationMode.Constant
        | "lagrangepolynomial" -> InterpolationMode.LagrangePolynomial(options.LagrangeDegree)
        | _ -> InterpolationMode.Linear

    let getAsmeDatabasePath (config: LibraryConfiguration) : string =
        Path.Combine(config.Io.MaterialDatabaseFolder, config.Io.AsmeMaterialDatabaseFile)

    let getEnDatabasePath (config: LibraryConfiguration) : string =
        Path.Combine(config.Io.MaterialDatabaseFolder, config.Io.EnMaterialDatabaseFile)

    // ── Default path resolution ────────────────────────────────────────────

    /// <summary>File name of the configuration file this module looks for next to a running assembly.</summary>
    [<Literal>]
    let DefaultConfigFileName = "MaterialLibrary.config.xml"

    /// <summary>Resolves the configuration file path under <paramref name="baseDirectory"/> (defaults to <see cref="AppContext.BaseDirectory"/>).</summary>
    let resolveConfigPath (baseDirectory: string option) : string =
        Path.Combine(defaultArg baseDirectory AppContext.BaseDirectory, DefaultConfigFileName)

    /// <summary>
    /// Resolves a database path from the configuration file under <paramref name="baseDirectory"/> when
    /// present and valid, otherwise <paramref name="fallbackFileName"/> next to it. Never touches disk
    /// beyond reading the configuration file, and never fails: any error while resolving falls back to
    /// <paramref name="fallbackFileName"/>.
    /// </summary>
    let private resolveDatabasePath
        (baseDirectory: string option)
        (getPath: LibraryConfiguration -> string)
        (fallbackFileName: string)
        : string =
        let baseDir = defaultArg baseDirectory AppContext.BaseDirectory
        let configPath = resolveConfigPath (Some baseDir)

        if File.Exists configPath then
            match load configPath with
            | Ok cfg -> getPath cfg
            | Error _ -> Path.Combine(baseDir, fallbackFileName)
        else
            Path.Combine(baseDir, fallbackFileName)

    /// <summary>Resolves the default ASME database path (configuration file if present, else a sibling <c>ASME_Materials.db</c>).</summary>
    let resolveAsmeDatabasePath (baseDirectory: string option) : string =
        resolveDatabasePath baseDirectory getAsmeDatabasePath "ASME_Materials.db"

    /// <summary>Resolves the default EN database path (configuration file if present, else a sibling <c>en_materials.db</c>).</summary>
    let resolveEnDatabasePath (baseDirectory: string option) : string =
        resolveDatabasePath baseDirectory getEnDatabasePath "en_materials.db"

    // ── File accessibility ──────────────────────────────────────────────────

    /// <summary>Checks that <paramref name="path"/> exists and can actually be opened for reading (existence alone does not guarantee OS-level read permission).</summary>
    let checkFileAccessible (path: string) : Result<unit, string> =
        if String.IsNullOrWhiteSpace path then
            Error "Path cannot be empty"
        elif not (File.Exists path) then
            Error $"File not found: {path}"
        else
            try
                use _stream = File.OpenRead path
                Ok()
            with ex ->
                Error $"File exists but could not be opened: {path} ({ex.Message})"

    // ── Read/write individual configuration sections ────────────────────────
    // Immutable-update helpers mirroring the Material module's set*/add* convention:
    // each returns a new LibraryConfiguration with LastModified-equivalent freshness
    // left to the caller (this record has no timestamp field), ready to pass to `save`.

    /// <summary>Returns a copy of <paramref name="config"/> with <c>General</c> replaced.</summary>
    let setGeneralOptions (options: GeneralOptions) (config: LibraryConfiguration) : LibraryConfiguration =
        { config with General = options }

    /// <summary>Returns a copy of <paramref name="config"/> with <c>Interpolation</c> replaced.</summary>
    let setInterpolationOptions (options: InterpolationOptions) (config: LibraryConfiguration) : LibraryConfiguration =
        { config with Interpolation = options }

    /// <summary>Returns a copy of <paramref name="config"/> with <c>Creep</c> replaced.</summary>
    let setCreepDefaults (defaults: CreepDefaults) (config: LibraryConfiguration) : LibraryConfiguration =
        { config with Creep = defaults }

    /// <summary>Returns a copy of <paramref name="config"/> with <c>Io</c> replaced.</summary>
    let setIoOptions (options: IoOptions) (config: LibraryConfiguration) : LibraryConfiguration =
        { config with Io = options }

    /// <summary>
    /// Returns a copy of <paramref name="config"/> with one named interpolation section replaced.
    /// </summary>
    /// <param name="sectionName">One of (case-insensitive): SpecificHeat, StressStrain, CreepTable, StressRupture, Fatigue.</param>
    let setInterpolationSection
        (sectionName: string)
        (section: InterpolationSectionOptions)
        (config: LibraryConfiguration)
        : Result<LibraryConfiguration, string> =
        match sectionName.Trim().ToLowerInvariant() with
        | "specificheat" ->
            Ok
                { config with
                    Interpolation = { config.Interpolation with SpecificHeat = section } }
        | "stressstrain" ->
            Ok
                { config with
                    Interpolation = { config.Interpolation with StressStrain = section } }
        | "creeptable" ->
            Ok
                { config with
                    Interpolation = { config.Interpolation with CreepTable = section } }
        | "stressrupture" ->
            Ok
                { config with
                    Interpolation = { config.Interpolation with StressRupture = section } }
        | "fatigue" ->
            Ok
                { config with
                    Interpolation = { config.Interpolation with Fatigue = section } }
        | other -> Error $"Unknown interpolation section: {other}"

    /// <summary>Returns a copy of <paramref name="config"/> with the database folder replaced.</summary>
    let setDatabaseFolder (folder: string) (config: LibraryConfiguration) : LibraryConfiguration =
        { config with
            Io = { config.Io with MaterialDatabaseFolder = folder } }

    /// <summary>Returns a copy of <paramref name="config"/> with the ASME database file name replaced.</summary>
    let setAsmeDatabaseFileName (fileName: string) (config: LibraryConfiguration) : LibraryConfiguration =
        { config with
            Io = { config.Io with AsmeMaterialDatabaseFile = fileName } }

    /// <summary>Returns a copy of <paramref name="config"/> with the EN database file name replaced.</summary>
    let setEnDatabaseFileName (fileName: string) (config: LibraryConfiguration) : LibraryConfiguration =
        { config with
            Io = { config.Io with EnMaterialDatabaseFile = fileName } }

    /// <summary>
    /// Loads the configuration at <paramref name="path"/> (creating a default one if it does not yet
    /// exist), applies <paramref name="update"/>, validates the result, and saves it back to
    /// <paramref name="path"/>.
    /// </summary>
    /// <returns>The updated, validated, and saved configuration, or an error from load/validate/save.</returns>
    let updateAndSave
        (path: string)
        (update: LibraryConfiguration -> LibraryConfiguration)
        : Result<LibraryConfiguration, string> =
        loadOrCreateDefault path
        |> Result.map update
        |> Result.bind (fun updated -> save path updated |> Result.map (fun () -> updated))

