namespace MaterialLibrary.Domain

open System

/// <summary>
/// Stress-Strain table: X = strain (%), Y = stress (MPa).
/// Metadata: ReferenceTemperature (degC), StrainBasis, StressBasis, YieldStress, UltimateStress.
/// </summary>
type StressStrainTable =
    {
        /// The underlying property table (X=strain%, Y=stress MPa).
        Table: PropertyTable
        /// Reference temperature or assessment condition (degC).
        ReferenceTemperature: float
        /// Reference duration for time-dependent data (hours).
        ReferenceDurationHours: float option
        /// Database origin or formula used to generate the table.
        Source: StressStrainTableSource
        /// Strain representation: 1=Engineering, 2=True.
        StrainBasis: int
        /// Stress representation: 1=Engineering, 2=True.
        StressBasis: int
        /// Yield stress extracted from curve (MPa).
        YieldStress: float option
        /// Ultimate/maximum stress (MPa).
        UltimateStress: float option
    }

/// <summary>Factory and accessor functions for <see cref="StressStrainTable"/>.</summary>
module StressStrainTable =
    let private isFinite value =
        not (Double.IsNaN value || Double.IsInfinity value)

    /// <summary>Creates a StressStrainTable with required metadata.</summary>
    let create
        (table: PropertyTable)
        (referenceTemperature: float)
        (strainBasis: int)
        (stressBasis: int)
        (yieldStress: float option)
        (ultimateStress: float option)
        : StressStrainTable =
        { Table = table
          ReferenceTemperature = referenceTemperature
          ReferenceDurationHours = None
          Source = StressStrainDatabase
          StrainBasis = strainBasis
          StressBasis = stressBasis
          YieldStress = yieldStress
          UltimateStress = ultimateStress }

    /// <summary>Creates a stress-strain table with optional isochronous duration and provenance.</summary>
    let createWithMetadata
        (table: PropertyTable)
        (referenceTemperature: float)
        (referenceDurationHours: float option)
        (source: StressStrainTableSource)
        (strainBasis: int)
        (stressBasis: int)
        (yieldStress: float option)
        (ultimateStress: float option)
        : StressStrainTable =
        { Table = table
          ReferenceTemperature = referenceTemperature
          ReferenceDurationHours = referenceDurationHours
          Source = source
          StrainBasis = strainBasis
          StressBasis = stressBasis
          YieldStress = yieldStress
          UltimateStress = ultimateStress }

    /// <summary>Validates stress-strain-specific metadata and table values.</summary>
    let validate (stressStrainTable: StressStrainTable) : Result<StressStrainTable, MaterialError> =
        if isNull (box stressStrainTable) then
            Error(MaterialError.InvalidOperation "Stress-strain table cannot be null")
        elif not (isFinite stressStrainTable.ReferenceTemperature) then
            Error(MaterialError.InvalidOperation "Stress-strain table temperature must be finite")
        elif
            stressStrainTable.ReferenceDurationHours
            |> Option.exists (fun duration -> not (isFinite duration) || duration <= 0.0)
        then
            Error(MaterialError.InvalidOperation "Isochronous reference duration must be finite and > 0")
        elif stressStrainTable.Source = GeneratedApi579Annex10B5 then
            Error(
                MaterialError.InvalidOperation
                    "API 579-1/ASME FFS-1 Annex 10B.5 generation is not implemented"
            )
        else
            PropertyTable.validate stressStrainTable.Table
            |> Result.map (fun _ -> stressStrainTable)

    /// <summary>Gets the underlying PropertyTable.</summary>
    let table (t: StressStrainTable) : PropertyTable = t.Table

    /// <summary>Gets the reference temperature (degC).</summary>
    let referenceTemperature (t: StressStrainTable) : float = t.ReferenceTemperature

    /// <summary>Gets the strain basis (1=Engineering, 2=True).</summary>
    let strainBasis (t: StressStrainTable) : int = t.StrainBasis

    /// <summary>Gets the stress basis (1=Engineering, 2=True).</summary>
    let stressBasis (t: StressStrainTable) : int = t.StressBasis

    /// <summary>Gets the yield stress, if available (MPa).</summary>
    let yieldStress (t: StressStrainTable) : float option = t.YieldStress

    /// <summary>Gets the ultimate stress, if available (MPa).</summary>
    let ultimateStress (t: StressStrainTable) : float option = t.UltimateStress

    /// <summary>Unwraps the StressStrainTable to access the underlying PropertyTable.</summary>
    let unwrap (t: StressStrainTable) : PropertyTable = t.Table
