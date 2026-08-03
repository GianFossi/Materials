namespace MaterialLibrary.Domain

open System
open System.Text.Json
open System.Text.Json.Serialization
open ROP

// Serialization of a single Material to and from its JSON representation.

/// <summary>
/// Serialization and deserialization for <see cref="Material"/> records.
/// Handles all core fields and specialized table metadata preservation.
/// </summary>
module MaterialSerialization =

    [<Literal>]
    let CurrentSchemaVersion = 16

    /// <summary>Oldest schema version this build can still read.</summary>
    /// <remarks>
    /// <para>
    /// Version 15 replaced the flat tensile-property list with the 2D <c>syTable</c> and
    /// <c>suTable</c>, and version 16 splits the room-temperature elongation by rolling direction
    /// and adds the optional thermal-diffusivity table. Both additions read back compatibly: an
    /// absent split field falls back to the legacy <c>elongationPercent</c>, and an absent optional
    /// table deserializes to <c>None</c>, so a version 15 document still loads in full.
    /// </para>
    /// <para>
    /// Version 14 is refused rather than accepted silently. Its tensile data lived in a
    /// <c>tensileProperties</c> array that no longer has any field to deserialize into, so reading
    /// one would appear to succeed while dropping the strength curves - a worse outcome than a
    /// clear error naming the version.
    /// </para>
    /// </remarks>
    [<Literal>]
    let MinimumReadableSchemaVersion = 15

    let private validateSchemaVersion version =
        if version >= MinimumReadableSchemaVersion && version <= CurrentSchemaVersion then
            Ok()
        else
            Error(MaterialError.InvalidOperation $"Unsupported material JSON schema version: {version}")

    let private normalizeOptionalString (value: string option) : string =
        match value with
        | Some s when not (String.IsNullOrWhiteSpace s) -> s.Trim()
        | _ -> ""

    let private familyFromCode = function
        | None -> Ok None
        | Some "CS" -> Ok(Some CS)
        | Some "QT" -> Ok(Some QT)
        | Some "LTCS" -> Ok(Some LTCS)
        | Some "LAS1.00" -> Ok(Some LAS1_00)
        | Some "LAS1.25" -> Ok(Some LAS1_25)
        | Some "LAS2.25" -> Ok(Some LAS2_25)
        | Some "LAS5.00" -> Ok(Some LAS5_00)
        | Some "LAS9.00" -> Ok(Some LAS9_00)
        | Some "SSA" -> Ok(Some SSA)
        | Some "SSF" -> Ok(Some SSF)
        | Some "SSM" -> Ok(Some SSM)
        | Some "SSD" -> Ok(Some SSD)
        | Some "SSD+" -> Ok(Some SSDPlus)
        | Some unsupported ->
            Error(MaterialError.InvalidOperation $"Unsupported ASME material family: {unsupported}")

    let private asmeNoteReferenceToJson (reference: AsmeNoteReference) : AsmeNoteReferenceJson =
        { Table =
            match reference.Table with
            | Table1A -> 0
            | Table1B -> 1
            | Table5A -> 2
            | Table5B -> 3
            | TableSy -> 4
            | TableSu -> 5
            | TableSBolting -> 6
          Code = reference.Code }

    let private asmeNoteReferenceFromJson
        (reference: AsmeNoteReferenceJson)
        : Result<AsmeNoteReference, MaterialError> =
        result {
            let! table =
                match reference.Table with
                | 0 -> Ok Table1A
                | 1 -> Ok Table1B
                | 2 -> Ok Table5A
                | 3 -> Ok Table5B
                | 4 -> Ok TableSy
                | 5 -> Ok TableSu
                | 6 -> Ok TableSBolting
                | value -> Error(MaterialError.InvalidOperation $"Unknown ASME note table: {value}")

            if String.IsNullOrWhiteSpace reference.Code then
                return! Error(MaterialError.InvalidOperation "ASME note-reference code cannot be blank")
            else
                return
                    { Table = table
                      Code = reference.Code.Trim().ToUpperInvariant() }
        }

    // Helper to convert list of Results to Result of list
    let private sequenceResultList (xs: Result<'a, 'e> list) : Result<'a list, 'e> =
        xs
        |> List.fold
            (fun acc item ->
                match acc, item with
                | Ok ys, Ok x -> Ok(x :: ys)
                | Error e, _ -> Error e
                | _, Error e -> Error e)
            (Ok [])
        |> Result.map List.rev

    let private basicPropertiesToJson (bp: BasicProperties) : BasicPropertiesJson =
        { ElongationPercent = None
          ElongationLongitudinalPercent = bp.ElongationLongitudinalPercent
          ElongationTransversePercent = bp.ElongationTransversePercent
          ReductionOfAreaPercent = bp.ReductionOfAreaPercent
          SpecifiedMinimumYieldStrength = bp.SpecifiedMinimumYieldStrength
          SpecifiedMinimumUltimateStrength = bp.SpecifiedMinimumUltimateStrength }

    let private basicPropertiesFromJson (json: BasicPropertiesJson) : BasicProperties =
        // Documents written before elongation was split by rolling direction carry a single
        // elongationPercent, which the reference importer filled from the longitudinal column;
        // it seeds the longitudinal value so those files keep their data.
        { ElongationLongitudinalPercent =
            match json.ElongationLongitudinalPercent with
            | Some value -> Some value
            | None -> json.ElongationPercent
          ElongationTransversePercent = json.ElongationTransversePercent
          ReductionOfAreaPercent = json.ReductionOfAreaPercent
          SpecifiedMinimumYieldStrength = json.SpecifiedMinimumYieldStrength
          SpecifiedMinimumUltimateStrength = json.SpecifiedMinimumUltimateStrength }

    let private compressionPropertiesToJson (properties: CompressionProperties) : CompressionPropertiesJson =
        { Temperature = properties.Temperature
          CompressiveStrength = properties.CompressiveStrength
          CompressiveYield = properties.CompressiveYield }

    let private compressionPropertiesFromJson
        (json: CompressionPropertiesJson)
        : Result<CompressionProperties, MaterialError> =
        Ok
            ({ Temperature = json.Temperature
               CompressiveStrength = json.CompressiveStrength
               CompressiveYield = json.CompressiveYield }: CompressionProperties)

    let private specialPropertiesToJson (properties: SpecialProperties) : SpecialPropertiesJson =
        { AppendixIIIConstants = properties.AppendixIIIConstants
          AppendixIIIFactorRule =
            properties.AppendixIIIFactorRule
            |> Option.map (fun rule ->
                { MaterialFamily =
                    match rule.MaterialFamily with
                    | FerrousSteel -> 0
                    | StainlessSteelOrNickelBasedAlloy -> 1
                    | DuplexStainlessSteel -> 2
                  TemperatureLimitF = rule.TemperatureLimitF
                  M2Coefficient = rule.M2Coefficient
                  EpsPrimeP = rule.EpsPrimeP
                  Notes = rule.Notes }) }

    let private specialPropertiesFromJson
        (json: SpecialPropertiesJson option)
        : Result<SpecialProperties, MaterialError> =
        match json with
        | None ->
            Ok
                { AppendixIIIConstants = []
                  AppendixIIIFactorRule = None }
        | Some properties ->
            let factorRule =
                match properties.AppendixIIIFactorRule with
                | None -> Ok None
                | Some rule ->
                    let family =
                        match rule.MaterialFamily with
                        | 0 -> Ok FerrousSteel
                        | 1 -> Ok StainlessSteelOrNickelBasedAlloy
                        | 2 -> Ok DuplexStainlessSteel
                        | invalid ->
                            Error(MaterialError.InvalidOperation(sprintf "Unknown Code Case material family: %d" invalid))

                    family
                    |> Result.map (fun materialFamily ->
                        Some
                            ({ MaterialFamily = materialFamily
                               TemperatureLimitF = rule.TemperatureLimitF
                               M2Coefficient = rule.M2Coefficient
                               EpsPrimeP = rule.EpsPrimeP
                               Notes = rule.Notes }: CodeCase2964AppendixIIIFactorRule))

            factorRule
            |> Result.map (fun rule ->
                { AppendixIIIConstants = properties.AppendixIIIConstants
                  AppendixIIIFactorRule = rule })

    let private strengthPropertiesToJson (sp: StrengthProperties) : StrengthPropertiesJson =
        { StressStrainTables =
            sp.StressStrainTables
            |> List.map SpecializedTableSerialization.stressStrainTableToJson
          CyclicStrainTables =
            sp.CyclicStrainTables
            |> List.map SpecializedTableSerialization.cyclicStrainTableToJson
          ExternalPressureTables =
            sp.ExternalPressureTables
            |> List.map SpecializedTableSerialization.externalPressureTableToJson
          CreepTables = sp.CreepTables |> List.map SpecializedTableSerialization.creepTableToJson
          StressRuptureCurves =
            sp.StressRuptureCurves
            |> List.map SpecializedTableSerialization.stressRuptureTableToJson
          FatigueCurves = sp.FatigueCurves |> List.map SpecializedTableSerialization.fatigueTableToJson
          AllowableStressDatasets =
            sp.AllowableStressDatasets
            |> List.map (fun dataset ->
                { DatabaseRowId = dataset.DatabaseRowId
                  Source =
                    match dataset.Source with
                    | Division1AllowableStress -> 0
                    | Division2AllowableStress -> 1
                    | BoltingAllowableStress -> 2
                    | Division1HighAllowableStress -> 3
                  Case =
                    match dataset.Case with
                    | StandardStrengthAllowableStress -> 0
                    | HighStrengthAllowableStress -> 1
                  Table = PropertyTableSerialization.toJson dataset.Table
                  SizeMinimum = dataset.SizeMinimum
                  SizeMaximum = dataset.SizeMaximum
                  MaximumTemperature = dataset.MaximumTemperature
                  CreepTemperature = dataset.CreepTemperature
                  AsmeNoteReferences = dataset.AsmeNoteReferences |> List.map asmeNoteReferenceToJson
                  Notes = dataset.Notes })
          SyTable = sp.SyTable |> Option.map PropertyTableSerialization.toJson
          SuTable = sp.SuTable |> Option.map PropertyTableSerialization.toJson
          CompressionProperties =
            sp.CompressionProperties
            |> Option.map (List.map compressionPropertiesToJson)
          NortonModels = Some sp.NortonModels
          GarofaloModels = Some sp.GarofaloModels
          KachanovOmegaModels = Some sp.KachanovOmegaModels
          AverageCreepStrainRateStress =
            sp.AverageCreepStrainRateStress
            |> List.map SpecializedTableSerialization.creepStrainRateTableToJson
          MinimumCreepStrainRateStress =
            sp.MinimumCreepStrainRateStress
            |> List.map SpecializedTableSerialization.creepStrainRateTableToJson
          AverageCreepRuptureStress =
            sp.AverageCreepRuptureStress
            |> List.map SpecializedTableSerialization.creepStressRuptureTableToJson
          MinimumCreepRuptureStress =
            sp.MinimumCreepRuptureStress
            |> List.map SpecializedTableSerialization.creepStressRuptureTableToJson
          LarsonMillerCurves = Some sp.LarsonMillerCurves }

    let private strengthPropertiesFromJson (json: StrengthPropertiesJson) : Result<StrengthProperties, MaterialError> =
        result {
            let! stressStrainTables =
                json.StressStrainTables
                |> List.map SpecializedTableSerialization.stressStrainTableFromJson
                |> sequenceResultList

            let! cyclicCurves =
                json.CyclicStrainTables
                |> List.map SpecializedTableSerialization.cyclicStrainTableFromJson
                |> sequenceResultList

            let! externalPressureTables =
                json.ExternalPressureTables
                |> List.map SpecializedTableSerialization.externalPressureTableFromJson
                |> sequenceResultList

            let! creepCurves =
                json.CreepTables
                |> List.map SpecializedTableSerialization.creepTableFromJson
                |> sequenceResultList

            let! stressRuptureCurves =
                json.StressRuptureCurves
                |> List.map SpecializedTableSerialization.stressRuptureTableFromJson
                |> sequenceResultList

            let! fatigueCurves =
                json.FatigueCurves
                |> List.map SpecializedTableSerialization.fatigueTableFromJson
                |> sequenceResultList

            let! averageCreepStrainRateStress =
                json.AverageCreepStrainRateStress
                |> List.map SpecializedTableSerialization.creepStrainRateTableFromJson
                |> sequenceResultList

            let! minimumCreepStrainRateStress =
                json.MinimumCreepStrainRateStress
                |> List.map SpecializedTableSerialization.creepStrainRateTableFromJson
                |> sequenceResultList

            let! averageCreepRuptureStress =
                json.AverageCreepRuptureStress
                |> List.map SpecializedTableSerialization.creepStressRuptureTableFromJson
                |> sequenceResultList

            let! minimumCreepRuptureStress =
                json.MinimumCreepRuptureStress
                |> List.map SpecializedTableSerialization.creepStressRuptureTableFromJson
                |> sequenceResultList

            let! compressionProperties =
                match json.CompressionProperties with
                | None -> Ok None
                | Some values ->
                    values
                    |> List.map compressionPropertiesFromJson
                    |> sequenceResultList
                    |> Result.map Some

            let! allowableStressDatasets =
                json.AllowableStressDatasets
                |> List.map (fun dataset ->
                    result {
                        let! source =
                            match dataset.Source with
                            | 0 -> Ok Division1AllowableStress
                            | 1 -> Ok Division2AllowableStress
                            | 2 -> Ok BoltingAllowableStress
                            | 3 -> Ok Division1HighAllowableStress
                            | value ->
                                Error(MaterialError.InvalidOperation $"Unknown allowable-stress source: {value}")

                        let! allowableCase =
                            match dataset.Case with
                            | 0 -> Ok StandardStrengthAllowableStress
                            | 1 -> Ok HighStrengthAllowableStress
                            | value ->
                                Error(MaterialError.InvalidOperation $"Unknown allowable-stress case: {value}")

                        let! table = PropertyTableSerialization.fromJson dataset.Table
                        let! noteReferences =
                            dataset.AsmeNoteReferences
                            |> List.map asmeNoteReferenceFromJson
                            |> sequenceResultList

                        return!
                            ({ DatabaseRowId = dataset.DatabaseRowId
                               Source = source
                               Case = allowableCase
                               Table = table
                               SizeMinimum = dataset.SizeMinimum
                               SizeMaximum = dataset.SizeMaximum
                               MaximumTemperature = dataset.MaximumTemperature
                               CreepTemperature = dataset.CreepTemperature
                               AsmeNoteReferences = noteReferences
                               Notes = dataset.Notes }: AllowableStressDataset)
                            |> AllowableStressDataset.validate
                    })
                |> sequenceResultList

            let! syTable =
                match json.SyTable with
                | None -> Ok None
                | Some t -> PropertyTableSerialization.fromJson t |> Result.map Some

            let! suTable =
                match json.SuTable with
                | None -> Ok None
                | Some t -> PropertyTableSerialization.fromJson t |> Result.map Some

            return
                { SyTable = syTable
                  SuTable = suTable
                  AllowableStressDatasets = allowableStressDatasets
                  CompressionProperties = compressionProperties
                  StressStrainTables = stressStrainTables
                  CyclicStrainTables = cyclicCurves
                  ExternalPressureTables = externalPressureTables
                  NortonModels = defaultArg json.NortonModels []
                  GarofaloModels = defaultArg json.GarofaloModels []
                  KachanovOmegaModels = defaultArg json.KachanovOmegaModels []
                  CreepTables = creepCurves
                  AverageCreepStrainRateStress = averageCreepStrainRateStress
                  MinimumCreepStrainRateStress = minimumCreepStrainRateStress
                  StressRuptureCurves = stressRuptureCurves
                  AverageCreepRuptureStress = averageCreepRuptureStress
                  MinimumCreepRuptureStress = minimumCreepRuptureStress
                  LarsonMillerCurves = defaultArg json.LarsonMillerCurves []
                  FatigueCurves = fatigueCurves }
        }

    /// <summary>Converts a <see cref="Material"/> to its JSON representation.</summary>
    /// <remarks>
    /// Note: PhysicalProperties, AllowableStresses, TensileProperties, and other advanced
    /// properties are stored separately and are not included in this basic serialization.
    /// Full material round-trip would require extending this with those types.
    /// </remarks>
    let toJson (material: Material) : MaterialJson =
        let composedName =
            Material.composeMaterialName
                material.Specification
                material.Grade
                material.Class_Condition_Tempering
                material.AlloyIdentification_UNS

        let resolvedName =
            if String.IsNullOrWhiteSpace composedName then
                material.Name
            else
                composedName

        { SchemaVersion = CurrentSchemaVersion
          Id = material.Id
          Name = resolvedName
          ProductForm = Some material.ProductForm
          NominalComposition = Some material.NominalComposition
          Specification = Some material.Specification
          ASMESpecification = material.ASMESpecification
          Grade = material.Grade
          Class_Condition_Tempering = Some material.Class_Condition_Tempering
          AlloyIdentification_UNS = Some material.AlloyIdentification_UNS
          Family = material.Family |> Option.map AsmeMaterialFamily.code
          AsmeNoteReferences = material.AsmeNoteReferences |> List.map asmeNoteReferenceToJson
          BasicProperties = basicPropertiesToJson material.BasicProperties
          PhysicalProperties = Some material.PhysicalProperties
          StrengthProperties = strengthPropertiesToJson material.StrengthProperties
          SpecialProperties = Some(specialPropertiesToJson material.SpecialProperties)
          MaximumAllowableTemperature =
            Some
                { AsmeViiiI = material.MaximumAllowableTemperature.AsmeViiiI
                  AsmeViii1 = material.MaximumAllowableTemperature.AsmeViii1
                  AsmeViii2 = material.MaximumAllowableTemperature.AsmeViii2 }
          TimeDepenedingStartTemperature = material.TimeDepenedingStartTemperature
          WeldingInfo =
            material.WeldingInfo
            |> Option.map (fun w ->
                { PNumber = Some w.PNumber
                  GNumber = Some w.GNumber })
          CreatedDate = material.CreatedDate
          LastModified = material.LastModified
          Notes = material.Notes }

    /// <summary>Converts JSON representation back to a <see cref="Material"/>.</summary>
    let fromJson (json: MaterialJson) (physicalProperties: PhysicalProperties) : Result<Material, MaterialError> =
        result {
            do! validateSchemaVersion json.SchemaVersion
            let! strengthProperties = strengthPropertiesFromJson json.StrengthProperties
            let! specialProperties = specialPropertiesFromJson json.SpecialProperties
            let! family = familyFromCode json.Family
            let! noteReferences =
                json.AsmeNoteReferences
                |> List.map asmeNoteReferenceFromJson
                |> sequenceResultList

            let specification =
                match json.Specification with
                | Some s when not (String.IsNullOrWhiteSpace s) -> s.Trim()
                | _ -> json.ASMESpecification

            let classConditionTempering = normalizeOptionalString json.Class_Condition_Tempering
            let alloyIdentificationUns = normalizeOptionalString json.AlloyIdentification_UNS

            let composedName =
                Material.composeMaterialName specification json.Grade classConditionTempering alloyIdentificationUns

            let resolvedName =
                if String.IsNullOrWhiteSpace composedName then
                    json.Name
                else
                    composedName

            return
                { Id = json.Id
                  Name = resolvedName
                  ProductForm = normalizeOptionalString json.ProductForm
                  NominalComposition = normalizeOptionalString json.NominalComposition
                  Specification = specification
                  ASMESpecification = json.ASMESpecification
                  Grade = json.Grade
                  Class_Condition_Tempering = classConditionTempering
                  AlloyIdentification_UNS = alloyIdentificationUns
                  Family = family
                  AsmeNoteReferences = noteReferences
                  BasicProperties = basicPropertiesFromJson json.BasicProperties
                  PhysicalProperties = defaultArg json.PhysicalProperties physicalProperties
                  StrengthProperties = strengthProperties
                  SpecialProperties = specialProperties
                  MaximumAllowableTemperature =
                    match json.MaximumAllowableTemperature with
                    | Some m ->
                        { AsmeViiiI = m.AsmeViiiI
                          AsmeViii1 = m.AsmeViii1
                          AsmeViii2 = m.AsmeViii2 }
                    | None ->
                        { AsmeViiiI = None
                          AsmeViii1 = None
                          AsmeViii2 = None }
                  TimeDepenedingStartTemperature = json.TimeDepenedingStartTemperature
                  WeldingInfo =
                    json.WeldingInfo
                    |> Option.map (fun w ->
                        { PNumber = normalizeOptionalString w.PNumber
                          GNumber = normalizeOptionalString w.GNumber })
                  CreatedDate = json.CreatedDate
                  LastModified = json.LastModified
                  Notes = json.Notes
                  AllowableStressLevel =
                    if
                        strengthProperties.AllowableStressDatasets
                        |> List.exists (fun dataset ->
                            dataset.Source = Division1HighAllowableStress
                            && dataset.Case = HighStrengthAllowableStress)
                    then
                        HighAllowableStress
                    else
                        StandardAllowableStress
                  ApplicableAsmeCodes =
                    strengthProperties.AllowableStressDatasets
                    |> List.collect (fun dataset ->
                        match dataset.Source with
                        | Division1AllowableStress -> [ AsmeSectionI; AsmeSectionVIII1 ]
                        | Division1HighAllowableStress -> [ AsmeSectionI; AsmeSectionVIII1 ]
                        | Division2AllowableStress -> [ AsmeSectionVIII2 ]
                        | BoltingAllowableStress -> [ AsmeSectionI; AsmeSectionVIII1; AsmeSectionVIII2 ])
                    |> List.distinct }
        }

    /// <summary>Converts complete JSON representation back to a material using embedded physical properties.</summary>
    let fromJsonComplete (json: MaterialJson) : Result<Material, MaterialError> =
        match json.PhysicalProperties with
        | Some physicalProperties -> fromJson json physicalProperties
        | None ->
            Error(
                MaterialError.InvalidOperation
                    "Material JSON does not contain physicalProperties; use fromJson with a legacy fallback"
            )

    /// <summary>Serializes a <see cref="Material"/> to a JSON string.</summary>
    let toJsonString (material: Material) : string =
        JsonSerializer.Serialize(material |> toJson, JsonOptions.value)

    /// <summary>Deserializes a JSON string to a <see cref="Material"/>.</summary>
    let fromJsonString (json: string) (physicalProperties: PhysicalProperties) : Result<Material, MaterialError> =
        try
            let parsed = JsonSerializer.Deserialize<MaterialJson>(json, JsonOptions.value)

            if obj.ReferenceEquals(box parsed, null) then
                Error(MaterialError.InvalidOperation "Deserialized JSON was null")
            else
                fromJson parsed physicalProperties
        with ex ->
            Error(MaterialError.InvalidOperation(sprintf "JSON deserialization failed: %s" ex.Message))

    /// <summary>Deserializes complete material JSON using its embedded physical properties.</summary>
    let fromJsonStringComplete (json: string) : Result<Material, MaterialError> =
        try
            let parsed = JsonSerializer.Deserialize<MaterialJson>(json, JsonOptions.value)

            if obj.ReferenceEquals(box parsed, null) then
                Error(MaterialError.InvalidOperation "Deserialized JSON was null")
            else
                fromJsonComplete parsed
        with ex ->
            Error(MaterialError.InvalidOperation(sprintf "JSON deserialization failed: %s" ex.Message))

    /// <summary>Saves a <see cref="Material"/> to a JSON file.</summary>
    let saveToFile (filePath: string) (material: Material) : Result<unit, MaterialError> =
        try
            System.IO.File.WriteAllText(filePath, toJsonString material)
            Ok()
        with ex ->
            Error(MaterialError.InvalidOperation(sprintf "File write failed: %s" ex.Message))

    /// <summary>Loads a <see cref="Material"/> from a JSON file.</summary>
    let loadFromFile (filePath: string) (physicalProperties: PhysicalProperties) : Result<Material, MaterialError> =
        try
            let json = System.IO.File.ReadAllText(filePath)
            fromJsonString json physicalProperties
        with ex ->
            Error(MaterialError.InvalidOperation(sprintf "File read failed: %s" ex.Message))

    /// <summary>Loads complete material JSON using its embedded physical properties.</summary>
    let loadFromFileComplete (filePath: string) : Result<Material, MaterialError> =
        try
            let json = System.IO.File.ReadAllText(filePath)
            fromJsonStringComplete json
        with ex ->
            Error(MaterialError.InvalidOperation(sprintf "File read failed: %s" ex.Message))

// ─────────────────────────────────────────────────────────────────────────────
// MATERIAL LIBRARY SERIALIZATION
// ─────────────────────────────────────────────────────────────────────────────
