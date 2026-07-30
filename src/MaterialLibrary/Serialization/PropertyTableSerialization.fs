namespace MaterialLibrary.Domain

open System
open System.Text.Json
open System.Text.Json.Serialization
open ROP

// ─────────────────────────────────────────────────────────────────────────────
// JSON DOMAIN TYPES — represent PropertyTable and specialized table metadata
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> for all Material/PropertyTable JSON (de)serialization.
/// </summary>
/// <remarks>
/// The <c>JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)</c> attributes on the JSON
/// DTOs below only take effect through a source-generated <c>JsonSerializerContext</c>, which this
/// library does not define. Every call to <c>JsonSerializer.Serialize</c>/<c>Deserialize</c> must
/// therefore pass this options instance explicitly, or case-insensitive matching silently does not
/// happen and differently-cased input JSON deserializes with missing properties defaulted to zero
/// instead of a reported error.
/// </remarks>
module internal JsonOptions =
    let value = JsonSerializerOptions(PropertyNameCaseInsensitive = true)



/// <summary>JSON representation of <see cref="BoundInclusion"/>.</summary>
[<JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)>]
type BoundInclusionJson = string

/// <summary>JSON representation of <see cref="SizeRangeBound"/>.</summary>
[<JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)>]
type SizeRangeBoundJson =
    { [<JsonPropertyName("value")>]
      Value: float
      [<JsonPropertyName("inclusion")>]
      Inclusion: BoundInclusionJson }

/// <summary>JSON representation of <see cref="SizeColumnRange"/>.</summary>
[<JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)>]
type SizeColumnRangeJson =
    { [<JsonPropertyName("lower")>]
      Lower: SizeRangeBoundJson option
      [<JsonPropertyName("upper")>]
      Upper: SizeRangeBoundJson option
      [<JsonPropertyName("label")>]
      Label: string option }

/// <summary>JSON representation of <see cref="TableColumnEntry"/>.</summary>
[<JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)>]
type TableColumnEntryJson =
    { [<JsonPropertyName("x")>]
      X: float
      [<JsonPropertyName("value")>]
      Value: float }

/// <summary>JSON representation of <see cref="TableColumn"/>.</summary>
[<JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)>]
type TableColumnJson =
    { [<JsonPropertyName("sizeRange")>]
      SizeRange: SizeColumnRangeJson
      [<JsonPropertyName("entries")>]
      Entries: TableColumnEntryJson list }

/// <summary>JSON representation of <see cref="TableDimension"/>.</summary>
[<JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)>]
type TableDimensionJson = string

/// <summary>JSON representation of <see cref="XBoundaryPolicy"/>.</summary>
[<JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)>]
type XBoundaryPolicyJson = string

/// <summary>JSON representation of <see cref="PropertyTable"/>.</summary>
[<JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)>]
type PropertyTableJson =
    { [<JsonPropertyName("name")>]
      Name: string
      [<JsonPropertyName("xAxisName")>]
      XAxisName: string
      [<JsonPropertyName("xAxisUnit")>]
      XAxisUnit: string
      [<JsonPropertyName("yAxisName")>]
      YAxisName: string
      [<JsonPropertyName("valueUnit")>]
      ValueUnit: string
      [<JsonPropertyName("dimensionType")>]
      DimensionType: TableDimensionJson
      [<JsonPropertyName("dimensionUnit")>]
      DimensionUnit: string
      [<JsonPropertyName("xBoundaryPolicy")>]
      XBoundaryPolicy: XBoundaryPolicyJson
      [<JsonPropertyName("columns")>]
      Columns: TableColumnJson list }

// ─────────────────────────────────────────────────────────────────────────────
// SPECIALIZED TABLE JSON TYPES
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>JSON representation of <see cref="StressStrainTable"/> including embedded metadata.</summary>
[<JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)>]
type StressStrainTableJson =
    { [<JsonPropertyName("table")>]
      Table: PropertyTableJson
      [<JsonPropertyName("referenceTemperature")>]
      ReferenceTemperature: float
      [<JsonPropertyName("referenceDurationHours")>]
      ReferenceDurationHours: float option
      [<JsonPropertyName("source")>]
      Source: int
      [<JsonPropertyName("strainBasis")>]
      StrainBasis: int
      [<JsonPropertyName("stressBasis")>]
      StressBasis: int
      [<JsonPropertyName("yieldStress")>]
      YieldStress: float option
      [<JsonPropertyName("ultimateStress")>]
      UltimateStress: float option }

/// <summary>JSON representation of <see cref="CreepTable"/> including embedded metadata.</summary>
[<JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)>]
type CreepTableJson =
    { [<JsonPropertyName("table")>]
      Table: PropertyTableJson
      [<JsonPropertyName("referenceTemperature")>]
      ReferenceTemperature: float
      [<JsonPropertyName("appliedStress")>]
      AppliedStress: float option
      [<JsonPropertyName("source")>]
      Source: int
      [<JsonPropertyName("notes")>]
      Notes: string option }

/// <summary>JSON representation of <see cref="StressRuptureTable"/> including embedded metadata.</summary>
[<JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)>]
type StressRuptureTableJson =
    { [<JsonPropertyName("table")>]
      Table: PropertyTableJson
      [<JsonPropertyName("referenceTemperature")>]
      ReferenceTemperature: float }

/// <summary>JSON representation of <see cref="FatigueTable"/> including embedded metadata.</summary>
[<JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)>]
type FatigueTableJson =
    { [<JsonPropertyName("table")>]
      Table: PropertyTableJson
      [<JsonPropertyName("referenceTemperature")>]
      ReferenceTemperature: float
      [<JsonPropertyName("referenceDurationHours")>]
      ReferenceDurationHours: float option }

/// <summary>JSON representation of <see cref="CyclicStrainTable"/> including embedded metadata.</summary>
[<JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)>]
type CyclicStrainTableJson =
    { [<JsonPropertyName("table")>]
      Table: PropertyTableJson
      [<JsonPropertyName("hysteresisRangeTable")>]
      HysteresisRangeTable: PropertyTableJson
      [<JsonPropertyName("referenceTemperature")>]
      ReferenceTemperature: float
      [<JsonPropertyName("kcss")>]
      Kcss: float
      [<JsonPropertyName("ncss")>]
      Ncss: float
      [<JsonPropertyName("materialDescription")>]
      MaterialDescription: string
      [<JsonPropertyName("description")>]
      Description: string }

/// <summary>JSON representation of <see cref="CreepStressRuptureTable"/> including embedded metadata.</summary>
[<JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)>]
type CreepStressRuptureTableJson =
    { [<JsonPropertyName("table")>]
      Table: PropertyTableJson
      [<JsonPropertyName("referenceDurationHours")>]
      ReferenceDurationHours: float }

/// <summary>JSON representation of <see cref="CreepStrainRateTable"/> including embedded metadata.</summary>
[<JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)>]
type CreepStrainRateTableJson =
    { [<JsonPropertyName("table")>]
      Table: PropertyTableJson
      [<JsonPropertyName("referenceCreepRatePercentPer1000Hours")>]
      ReferenceCreepRatePercentPer1000Hours: float }

/// <summary>JSON representation of <see cref="ExternalPressureTable"/> including embedded metadata.</summary>
[<JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)>]
type ExternalPressureTableJson =
    { [<JsonPropertyName("table")>]
      Table: PropertyTableJson
      [<JsonPropertyName("referenceTemperature")>]
      ReferenceTemperature: float
      [<JsonPropertyName("referenceDurationHours")>]
      ReferenceDurationHours: float option
      [<JsonPropertyName("source")>]
      Source: int
      [<JsonPropertyName("reductionFactor")>]
      ReductionFactor: float option }

// ─────────────────────────────────────────────────────────────────────────────
// SERIALIZATION: PropertyTable ↔ JSON
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Serialization and deserialization for <see cref="PropertyTable"/> and specialized table types.</summary>
module PropertyTableSerialization =

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

    // ── Enum conversions ─────────────────────────────────────────────────────

    let private boundInclusionToJson (bi: BoundInclusion) : BoundInclusionJson =
        match bi with
        | Inclusive -> "Inclusive"
        | Exclusive -> "Exclusive"

    let private boundInclusionFromJson (s: BoundInclusionJson) : Result<BoundInclusion, MaterialError> =
        match s with
        | "Inclusive" -> Ok Inclusive
        | "Exclusive" -> Ok Exclusive
        | _ -> Error(MaterialError.InvalidOperation(sprintf "Unknown BoundInclusion: %s" s))

    let private tableDimensionToJson (td: TableDimension) : TableDimensionJson =
        match td with
        | Thickness -> "Thickness"
        | Diameter -> "Diameter"
        | Length -> "Length"
        | NoDimension -> "NoDimension"

    let private tableDimensionFromJson (s: TableDimensionJson) : Result<TableDimension, MaterialError> =
        match s with
        | "Thickness" -> Ok Thickness
        | "Diameter" -> Ok Diameter
        | "Length" -> Ok Length
        | "NoDimension" -> Ok NoDimension
        | _ -> Error(MaterialError.InvalidOperation(sprintf "Unknown TableDimension: %s" s))

    let private xBoundaryPolicyToJson (policy: XBoundaryPolicy) : XBoundaryPolicyJson =
        match policy with
        | ReturnError -> "ReturnError"
        | FlatExtrapolate -> "FlatExtrapolate"

    let private xBoundaryPolicyFromJson (s: XBoundaryPolicyJson) : Result<XBoundaryPolicy, MaterialError> =
        match s with
        | "ReturnError" -> Ok ReturnError
        | "FlatExtrapolate" -> Ok FlatExtrapolate
        | _ -> Error(MaterialError.InvalidOperation(sprintf "Unknown XBoundaryPolicy: %s" s))

    // ── Core serialization ───────────────────────────────────────────────────

    /// <summary>Converts a <see cref="PropertyTable"/> to its JSON representation.</summary>
    let toJson (table: PropertyTable) : PropertyTableJson =
        { Name = table.Name
          XAxisName = table.XAxisName
          XAxisUnit = table.XAxisUnit
          YAxisName = table.YAxisName
          ValueUnit = table.ValueUnit
          DimensionType = tableDimensionToJson table.DimensionType
          DimensionUnit = table.DimensionUnit
          XBoundaryPolicy = xBoundaryPolicyToJson table.XBoundaryPolicy
          Columns =
            table.Columns
            |> List.map (fun col ->
                { SizeRange =
                    { Lower =
                        col.SizeRange.Lower
                        |> Option.map (fun lb ->
                            { Value = lb.Value
                              Inclusion = boundInclusionToJson lb.Inclusion })
                      Upper =
                        col.SizeRange.Upper
                        |> Option.map (fun ub ->
                            { Value = ub.Value
                              Inclusion = boundInclusionToJson ub.Inclusion })
                      Label = col.SizeRange.Label }
                  Entries = col.Entries |> List.map (fun entry -> { X = entry.X; Value = entry.Value }) }) }

    /// <summary>Converts a JSON column to a domain TableColumn.</summary>
    let private columnFromJson (colJson: TableColumnJson) : Result<TableColumn, MaterialError> =
        result {
            // Process lower bound
            let! lower_bound_opt =
                match colJson.SizeRange.Lower with
                | None -> Ok None
                | Some lb ->
                    result {
                        let! incl = boundInclusionFromJson lb.Inclusion
                        let bound: SizeRangeBound = { Value = lb.Value; Inclusion = incl }
                        return Some bound
                    }

            // Process upper bound
            let! upper_bound_opt =
                match colJson.SizeRange.Upper with
                | None -> Ok None
                | Some ub ->
                    result {
                        let! incl = boundInclusionFromJson ub.Inclusion
                        let bound: SizeRangeBound = { Value = ub.Value; Inclusion = incl }
                        return Some bound
                    }

            // Build entries
            let entries =
                colJson.Entries
                |> List.map (fun entry -> { X = entry.X; Value = entry.Value }: TableColumnEntry)

            // Build SizeColumnRange explicitly
            let range: SizeColumnRange =
                { Lower = lower_bound_opt
                  Upper = upper_bound_opt
                  Label = colJson.SizeRange.Label }

            // Build TableColumn
            let col: TableColumn = { SizeRange = range; Entries = entries }

            return col
        }

    /// <summary>Converts JSON representation back to a <see cref="PropertyTable"/>.</summary>
    let fromJson (json: PropertyTableJson) : Result<PropertyTable, MaterialError> =
        result {
            let! dimensionType = tableDimensionFromJson json.DimensionType
            let! xBoundaryPolicy = xBoundaryPolicyFromJson json.XBoundaryPolicy

            let! columns = json.Columns |> List.map columnFromJson |> sequenceResultList

            let table =
                ({ Name = json.Name
                   XAxisName = json.XAxisName
                   XAxisUnit = json.XAxisUnit
                   YAxisName = json.YAxisName
                   ValueUnit = json.ValueUnit
                   DimensionType = dimensionType
                   DimensionUnit = json.DimensionUnit
                   XBoundaryPolicy = xBoundaryPolicy
                   Columns = columns }
                : PropertyTable)

            return! PropertyTable.validate table
        }

    /// <summary>Serializes a <see cref="PropertyTable"/> to a JSON string.</summary>
    let toJsonString (table: PropertyTable) : string =
        JsonSerializer.Serialize(table |> toJson, JsonOptions.value)

    /// <summary>Deserializes a JSON string to a <see cref="PropertyTable"/>.</summary>
    let fromJsonString (json: string) : Result<PropertyTable, MaterialError> =
        try
            let parsed = JsonSerializer.Deserialize<PropertyTableJson>(json, JsonOptions.value)

            if obj.ReferenceEquals(box parsed, null) then
                Error(MaterialError.InvalidOperation "Deserialized JSON was null")
            else
                fromJson parsed
        with ex ->
            Error(MaterialError.InvalidOperation(sprintf "JSON deserialization failed: %s" ex.Message))

    /// <summary>Saves a <see cref="PropertyTable"/> to a JSON file.</summary>
    let saveToFile (filePath: string) (table: PropertyTable) : Result<unit, MaterialError> =
        try
            System.IO.File.WriteAllText(filePath, toJsonString table)
            Ok()
        with ex ->
            Error(MaterialError.InvalidOperation(sprintf "File write failed: %s" ex.Message))

    /// <summary>Loads a <see cref="PropertyTable"/> from a JSON file.</summary>
    let loadFromFile (filePath: string) : Result<PropertyTable, MaterialError> =
        try
            let json = System.IO.File.ReadAllText(filePath)
            fromJsonString json
        with ex ->
            Error(MaterialError.InvalidOperation(sprintf "File read failed: %s" ex.Message))

// ─────────────────────────────────────────────────────────────────────────────
// SPECIALIZED TABLE SERIALIZATION
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Serialization for specialized table types with full metadata preservation.</summary>
module SpecializedTableSerialization =

    // ── StressStrainTable ────────────────────────────────────────────────────

    /// <summary>Converts a <see cref="StressStrainTable"/> to its JSON representation.</summary>
    let stressStrainTableToJson (t: StressStrainTable) : StressStrainTableJson =
        { Table = PropertyTableSerialization.toJson t.Table
          ReferenceTemperature = t.ReferenceTemperature
          ReferenceDurationHours = t.ReferenceDurationHours
          Source =
            match t.Source with
            | StressStrainDatabase -> 0
            | GeneratedAsmeVIII2Annex3D -> 1
            | GeneratedApi579Annex10B5 -> 2
          StrainBasis = t.StrainBasis
          StressBasis = t.StressBasis
          YieldStress = t.YieldStress
          UltimateStress = t.UltimateStress }

    /// <summary>Converts JSON representation back to a <see cref="StressStrainTable"/>.</summary>
    let stressStrainTableFromJson (json: StressStrainTableJson) : Result<StressStrainTable, MaterialError> =
        result {
            let! baseTable = PropertyTableSerialization.fromJson json.Table

            let! source =
                match json.Source with
                | 0 -> Ok StressStrainDatabase
                | 1 -> Ok GeneratedAsmeVIII2Annex3D
                | 2 -> Ok GeneratedApi579Annex10B5
                | value -> Error(MaterialError.InvalidOperation $"Unknown stress-strain table source: {value}")

            return!
                StressStrainTable.createWithMetadata
                    baseTable
                    json.ReferenceTemperature
                    json.ReferenceDurationHours
                    source
                    json.StrainBasis
                    json.StressBasis
                    json.YieldStress
                    json.UltimateStress
                |> StressStrainTable.validate
        }

    /// <summary>Serializes a <see cref="StressStrainTable"/> to a JSON string.</summary>
    let stressStrainTableToJsonString (t: StressStrainTable) : string =
        JsonSerializer.Serialize(t |> stressStrainTableToJson, JsonOptions.value)

    /// <summary>Deserializes a JSON string to a <see cref="StressStrainTable"/>.</summary>
    let stressStrainTableFromJsonString (json: string) : Result<StressStrainTable, MaterialError> =
        try
            let parsed = JsonSerializer.Deserialize<StressStrainTableJson>(json, JsonOptions.value)

            if obj.ReferenceEquals(box parsed, null) then
                Error(MaterialError.InvalidOperation "Deserialized JSON was null")
            else
                stressStrainTableFromJson parsed
        with ex ->
            Error(MaterialError.InvalidOperation(sprintf "JSON deserialization failed: %s" ex.Message))

    // ── CreepTable ───────────────────────────────────────────────────────────

    /// <summary>Converts a <see cref="CreepTable"/> to its JSON representation.</summary>
    let creepTableToJson (t: CreepTable) : CreepTableJson =
        { Table = PropertyTableSerialization.toJson t.Table
          ReferenceTemperature = t.ReferenceTemperature
          AppliedStress = t.AppliedStress
          Source =
            match t.Source with
            | CreepDatabase -> 0
            | GeneratedNortonPowerLaw -> 1
            | GeneratedGarofalo -> 2
            | GeneratedKachanovOmega -> 3
          Notes = t.Notes }

    /// <summary>Converts JSON representation back to a <see cref="CreepTable"/>.</summary>
    let creepTableFromJson (json: CreepTableJson) : Result<CreepTable, MaterialError> =
        result {
            let! baseTable = PropertyTableSerialization.fromJson json.Table
            let! source =
                match json.Source with
                | 0 -> Ok CreepDatabase
                | 1 -> Ok GeneratedNortonPowerLaw
                | 2 -> Ok GeneratedGarofalo
                | 3 -> Ok GeneratedKachanovOmega
                | value -> Error(MaterialError.InvalidOperation $"Unknown creep-table source: {value}")

            return!
                CreepTable.createWithAppliedStress
                    baseTable
                    json.ReferenceTemperature
                    json.AppliedStress
                    source
                    json.Notes
                |> CreepTable.validate
        }

    /// <summary>Serializes a <see cref="CreepTable"/> to a JSON string.</summary>
    let creepTableToJsonString (t: CreepTable) : string =
        JsonSerializer.Serialize(t |> creepTableToJson, JsonOptions.value)

    /// <summary>Deserializes a JSON string to a <see cref="CreepTable"/>.</summary>
    let creepTableFromJsonString (json: string) : Result<CreepTable, MaterialError> =
        try
            let parsed = JsonSerializer.Deserialize<CreepTableJson>(json, JsonOptions.value)

            if obj.ReferenceEquals(box parsed, null) then
                Error(MaterialError.InvalidOperation "Deserialized JSON was null")
            else
                creepTableFromJson parsed
        with ex ->
            Error(MaterialError.InvalidOperation(sprintf "JSON deserialization failed: %s" ex.Message))

    // ── StressRuptureTable ───────────────────────────────────────────────────

    /// <summary>Converts a <see cref="StressRuptureTable"/> to its JSON representation.</summary>
    let stressRuptureTableToJson (t: StressRuptureTable) : StressRuptureTableJson =
        { Table = PropertyTableSerialization.toJson t.Table
          ReferenceTemperature = t.ReferenceTemperature }

    /// <summary>Converts JSON representation back to a <see cref="StressRuptureTable"/>.</summary>
    let stressRuptureTableFromJson (json: StressRuptureTableJson) : Result<StressRuptureTable, MaterialError> =
        result {
            let! baseTable = PropertyTableSerialization.fromJson json.Table
            return StressRuptureTable.create baseTable json.ReferenceTemperature
        }

    /// <summary>Serializes a <see cref="StressRuptureTable"/> to a JSON string.</summary>
    let stressRuptureTableToJsonString (t: StressRuptureTable) : string =
        JsonSerializer.Serialize(t |> stressRuptureTableToJson, JsonOptions.value)

    /// <summary>Deserializes a JSON string to a <see cref="StressRuptureTable"/>.</summary>
    let stressRuptureTableFromJsonString (json: string) : Result<StressRuptureTable, MaterialError> =
        try
            let parsed = JsonSerializer.Deserialize<StressRuptureTableJson>(json, JsonOptions.value)

            if obj.ReferenceEquals(box parsed, null) then
                Error(MaterialError.InvalidOperation "Deserialized JSON was null")
            else
                stressRuptureTableFromJson parsed
        with ex ->
            Error(MaterialError.InvalidOperation(sprintf "JSON deserialization failed: %s" ex.Message))

    // ── FatigueTable ─────────────────────────────────────────────────────────

    /// <summary>Converts a <see cref="FatigueTable"/> to its JSON representation.</summary>
    let fatigueTableToJson (t: FatigueTable) : FatigueTableJson =
        { Table = PropertyTableSerialization.toJson t.Table
          ReferenceTemperature = t.ReferenceTemperature
          ReferenceDurationHours = t.ReferenceDurationHours }

    /// <summary>Converts JSON representation back to a <see cref="FatigueTable"/>.</summary>
    let fatigueTableFromJson (json: FatigueTableJson) : Result<FatigueTable, MaterialError> =
        result {
            let! baseTable = PropertyTableSerialization.fromJson json.Table
            return FatigueTable.create baseTable json.ReferenceTemperature json.ReferenceDurationHours
        }

    /// <summary>Serializes a <see cref="FatigueTable"/> to a JSON string.</summary>
    let fatigueTableToJsonString (t: FatigueTable) : string =
        JsonSerializer.Serialize(t |> fatigueTableToJson, JsonOptions.value)

    /// <summary>Deserializes a JSON string to a <see cref="FatigueTable"/>.</summary>
    let fatigueTableFromJsonString (json: string) : Result<FatigueTable, MaterialError> =
        try
            let parsed = JsonSerializer.Deserialize<FatigueTableJson>(json, JsonOptions.value)

            if obj.ReferenceEquals(box parsed, null) then
                Error(MaterialError.InvalidOperation "Deserialized JSON was null")
            else
                fatigueTableFromJson parsed
        with ex ->
            Error(MaterialError.InvalidOperation(sprintf "JSON deserialization failed: %s" ex.Message))

    // ── CyclicStrainTable ────────────────────────────────────────────────────

    /// <summary>Converts a <see cref="CyclicStrainTable"/> to its JSON representation.</summary>
    let cyclicStrainTableToJson (t: CyclicStrainTable) : CyclicStrainTableJson =
        { Table = PropertyTableSerialization.toJson t.Table
          HysteresisRangeTable = PropertyTableSerialization.toJson t.HysteresisRangeTable
          ReferenceTemperature = t.ReferenceTemperature
          Kcss = t.Kcss
          Ncss = t.Ncss
          MaterialDescription = t.MaterialDescription
          Description = t.Description }

    /// <summary>Converts JSON representation back to a <see cref="CyclicStrainTable"/>.</summary>
    let cyclicStrainTableFromJson (json: CyclicStrainTableJson) : Result<CyclicStrainTable, MaterialError> =
        result {
            let! baseTable = PropertyTableSerialization.fromJson json.Table
            let! hysteresisRangeTable = PropertyTableSerialization.fromJson json.HysteresisRangeTable

            return!
                CyclicStrainTable.create
                    baseTable
                    hysteresisRangeTable
                    json.ReferenceTemperature
                    json.Kcss
                    json.Ncss
                    json.MaterialDescription
                    json.Description
                |> CyclicStrainTable.validate
        }

    /// <summary>Serializes a <see cref="CyclicStrainTable"/> to a JSON string.</summary>
    let cyclicStrainTableToJsonString (t: CyclicStrainTable) : string =
        JsonSerializer.Serialize(t |> cyclicStrainTableToJson, JsonOptions.value)

    /// <summary>Deserializes a JSON string to a <see cref="CyclicStrainTable"/>.</summary>
    let cyclicStrainTableFromJsonString (json: string) : Result<CyclicStrainTable, MaterialError> =
        try
            let parsed = JsonSerializer.Deserialize<CyclicStrainTableJson>(json, JsonOptions.value)

            if obj.ReferenceEquals(box parsed, null) then
                Error(MaterialError.InvalidOperation "Deserialized JSON was null")
            else
                cyclicStrainTableFromJson parsed
        with ex ->
            Error(MaterialError.InvalidOperation(sprintf "JSON deserialization failed: %s" ex.Message))

    // ── ExternalPressureTable ────────────────────────────────────────────────

    /// <summary>Converts a <see cref="ExternalPressureTable"/> to its JSON representation.</summary>
    let externalPressureTableToJson (t: ExternalPressureTable) : ExternalPressureTableJson =
        { Table = PropertyTableSerialization.toJson t.Table
          ReferenceTemperature = t.ReferenceTemperature
          ReferenceDurationHours = t.ReferenceDurationHours
          Source =
            match t.Source with
            | MaterialDatabase -> 0
            | CodeCase2964 -> 1
          ReductionFactor = t.ReductionFactor }

    /// <summary>Converts JSON representation back to a <see cref="ExternalPressureTable"/>.</summary>
    let externalPressureTableFromJson (json: ExternalPressureTableJson) : Result<ExternalPressureTable, MaterialError> =
        result {
            let! baseTable = PropertyTableSerialization.fromJson json.Table
            let! source =
                match json.Source with
                | 0 -> Ok MaterialDatabase
                | 1 -> Ok CodeCase2964
                | value -> Error(MaterialError.InvalidOperation $"Unknown external-pressure table source: {value}")

            return!
                ExternalPressureTable.create
                    baseTable
                    json.ReferenceTemperature
                    json.ReferenceDurationHours
                    source
                    json.ReductionFactor
                |> ExternalPressureTable.validate
        }

    /// <summary>Serializes a <see cref="ExternalPressureTable"/> to a JSON string.</summary>
    let externalPressureTableToJsonString (t: ExternalPressureTable) : string =
        JsonSerializer.Serialize(t |> externalPressureTableToJson, JsonOptions.value)

    /// <summary>Deserializes a JSON string to a <see cref="ExternalPressureTable"/>.</summary>
    let externalPressureTableFromJsonString (json: string) : Result<ExternalPressureTable, MaterialError> =
        try
            let parsed = JsonSerializer.Deserialize<ExternalPressureTableJson>(json, JsonOptions.value)

            if obj.ReferenceEquals(box parsed, null) then
                Error(MaterialError.InvalidOperation "Deserialized JSON was null")
            else
                externalPressureTableFromJson parsed
        with ex ->
            Error(MaterialError.InvalidOperation(sprintf "JSON deserialization failed: %s" ex.Message))

    // ── CreepStressRuptureTable ──────────────────────────────────────────────

    /// <summary>Converts a <see cref="CreepStressRuptureTable"/> to its JSON representation.</summary>
    let creepStressRuptureTableToJson (t: CreepStressRuptureTable) : CreepStressRuptureTableJson =
        { Table = PropertyTableSerialization.toJson t.Table
          ReferenceDurationHours = t.ReferenceDurationHours }

    /// <summary>Converts JSON representation back to a <see cref="CreepStressRuptureTable"/>.</summary>
    let creepStressRuptureTableFromJson
        (json: CreepStressRuptureTableJson)
        : Result<CreepStressRuptureTable, MaterialError> =
        result {
            let! baseTable = PropertyTableSerialization.fromJson json.Table

            return!
                CreepStressRuptureTable.create baseTable json.ReferenceDurationHours
                |> CreepStressRuptureTable.validate
        }

    // ── CreepStrainRateTable ─────────────────────────────────────────────────

    /// <summary>Converts a <see cref="CreepStrainRateTable"/> to its JSON representation.</summary>
    let creepStrainRateTableToJson (t: CreepStrainRateTable) : CreepStrainRateTableJson =
        { Table = PropertyTableSerialization.toJson t.Table
          ReferenceCreepRatePercentPer1000Hours = t.ReferenceCreepRatePercentPer1000Hours }

    /// <summary>Converts JSON representation back to a <see cref="CreepStrainRateTable"/>.</summary>
    let creepStrainRateTableFromJson
        (json: CreepStrainRateTableJson)
        : Result<CreepStrainRateTable, MaterialError> =
        result {
            let! baseTable = PropertyTableSerialization.fromJson json.Table

            return!
                CreepStrainRateTable.create baseTable json.ReferenceCreepRatePercentPer1000Hours
                |> CreepStrainRateTable.validate
        }
