namespace MaterialLibrary.Domain

open System
open System.Text.Json
open System.Text.Json.Serialization
open ROP

// JSON data-transfer types for Material. Units are documented on each field.


// ─────────────────────────────────────────────────────────────────────────────
// MATERIAL JSON TYPES
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// JSON representation of <see cref="BasicProperties"/>.
/// Units: All stresses/strengths in MPa, elongation and reduction of area in %.
/// </summary>
[<JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)>]
type BasicPropertiesJson =
    { [<JsonPropertyName("elongationPercent")>]
      ElongationPercent: float
      [<JsonPropertyName("reductionOfAreaPercent")>]
      ReductionOfAreaPercent: float
      [<JsonPropertyName("specifiedMinimumYieldStrength")>]
      SpecifiedMinimumYieldStrength: float
      [<JsonPropertyName("specifiedMinimumUltimateStrength")>]
      SpecifiedMinimumUltimateStrength: float }

type CompressionPropertiesJson =
    { Temperature: float
      CompressiveStrength: float
      CompressiveYield: float }

type AsmeNoteReferenceJson =
    { Table: int
      Code: string }

type AllowableStressDatasetJson =
    { DatabaseRowId: int64
      Source: int
      Case: int
      Table: PropertyTableJson
      SizeMinimum: float option
      SizeMaximum: float option
      MaximumTemperature: float option
      CreepTemperature: float option
      AsmeNoteReferences: AsmeNoteReferenceJson list
      Notes: string option }

/// <summary>
/// JSON representation of <see cref="StrengthProperties"/> aggregating all curve/model lists.
/// </summary>
[<JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)>]
type StrengthPropertiesJson =
    { [<JsonPropertyName("stressStrainTables")>]
      StressStrainTables: StressStrainTableJson list
      [<JsonPropertyName("cyclicCurves")>]
      CyclicStrainTables: CyclicStrainTableJson list
      [<JsonPropertyName("externalPressureTables")>]
      ExternalPressureTables: ExternalPressureTableJson list
      [<JsonPropertyName("creepCurves")>]
      CreepTables: CreepTableJson list
      [<JsonPropertyName("stressRuptureCurves")>]
      StressRuptureCurves: StressRuptureTableJson list
      [<JsonPropertyName("fatigueCurves")>]
      FatigueCurves: FatigueTableJson list
      [<JsonPropertyName("allowableStressDatasets")>]
      AllowableStressDatasets: AllowableStressDatasetJson list
      [<JsonPropertyName("syTable")>]
      SyTable: PropertyTableJson option
      [<JsonPropertyName("suTable")>]
      SuTable: PropertyTableJson option
      [<JsonPropertyName("compressionProperties")>]
      CompressionProperties: CompressionPropertiesJson list option
      [<JsonPropertyName("nortonModels")>]
      NortonModels: NortonPowerLawCoefficients list option
      [<JsonPropertyName("garofaloModels")>]
      GarofaloModels: GarofaloCoefficients list option
      [<JsonPropertyName("kachanovOmegaModels")>]
      KachanovOmegaModels: KachanovOmegaModel list option
      [<JsonPropertyName("averageCreepStrainRateStress")>]
      AverageCreepStrainRateStress: CreepStrainRateTableJson list
      [<JsonPropertyName("minimumCreepStrainRateStress")>]
      MinimumCreepStrainRateStress: CreepStrainRateTableJson list
      [<JsonPropertyName("averageCreepRuptureStress")>]
      AverageCreepRuptureStress: CreepStressRuptureTableJson list
      [<JsonPropertyName("minimumCreepRuptureStress")>]
      MinimumCreepRuptureStress: CreepStressRuptureTableJson list
      [<JsonPropertyName("larsonMillerCurves")>]
      LarsonMillerCurves: LarsonMillerCurve list option }

type AppendixIIIFactorRuleJson =
    { MaterialFamily: int
      TemperatureLimitF: float
      M2Coefficient: float
      EpsPrimeP: float
      Notes: string option }

type SpecialPropertiesJson =
    { AppendixIIIConstants: CodeCase2964AppendixIIIConstants list
      AppendixIIIFactorRule: AppendixIIIFactorRuleJson option }

/// <summary>
/// JSON representation of <see cref="MaximumAllowableTemperature"/>.
/// Units: all temperatures in degC.
/// </summary>
[<JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)>]
type MaximumAllowableTemperatureJson =
    { [<JsonPropertyName("asmeViiiI")>]
      AsmeViiiI: float option
      [<JsonPropertyName("asmeViii1")>]
      AsmeViii1: float option
      [<JsonPropertyName("asmeViii2")>]
      AsmeViii2: float option }

/// <summary>
/// JSON representation of <see cref="WeldingInfo"/>.
/// </summary>
[<JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)>]
type WeldingInfoJson =
    { [<JsonPropertyName("pNumber")>]
      PNumber: string option
      [<JsonPropertyName("gNumber")>]
      GNumber: string option }

/// <summary>
/// JSON representation of <see cref="Material"/>.
/// Includes identification, basic/physical/strength properties, and metadata.
/// </summary>
[<JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)>]
type MaterialJson =
    { [<JsonPropertyName("schemaVersion")>]
      SchemaVersion: int
      [<JsonPropertyName("id")>]
      Id: string
      [<JsonPropertyName("name")>]
      Name: string
      [<JsonPropertyName("productForm")>]
      ProductForm: string option
      [<JsonPropertyName("nominalComposition")>]
      NominalComposition: string option
      [<JsonPropertyName("specification")>]
      Specification: string option
      [<JsonPropertyName("asmeSpecification")>]
      ASMESpecification: string
      [<JsonPropertyName("grade")>]
      Grade: string
      [<JsonPropertyName("classConditionTempering")>]
      Class_Condition_Tempering: string option
      [<JsonPropertyName("alloyIdentificationUns")>]
      AlloyIdentification_UNS: string option
      [<JsonPropertyName("family")>]
      Family: string option
      [<JsonPropertyName("asmeNoteReferences")>]
      AsmeNoteReferences: AsmeNoteReferenceJson list
      [<JsonPropertyName("basicProperties")>]
      BasicProperties: BasicPropertiesJson
      [<JsonPropertyName("physicalProperties")>]
      PhysicalProperties: PhysicalProperties option
      [<JsonPropertyName("strengthProperties")>]
      StrengthProperties: StrengthPropertiesJson
      [<JsonPropertyName("specialProperties")>]
      SpecialProperties: SpecialPropertiesJson option
      [<JsonPropertyName("maximumAllowableTemperature")>]
      MaximumAllowableTemperature: MaximumAllowableTemperatureJson option
      [<JsonPropertyName("timeDepenedingStartTemperature")>]
      TimeDepenedingStartTemperature: float option
      [<JsonPropertyName("weldingInfo")>]
      WeldingInfo: WeldingInfoJson option
      [<JsonPropertyName("createdDate")>]
      CreatedDate: DateTime
      [<JsonPropertyName("lastModified")>]
      LastModified: DateTime
      [<JsonPropertyName("notes")>]
      Notes: string option }

// ─────────────────────────────────────────────────────────────────────────────
// MATERIAL SERIALIZATION
// ─────────────────────────────────────────────────────────────────────────────
