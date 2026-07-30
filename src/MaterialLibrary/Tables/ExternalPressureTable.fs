namespace MaterialLibrary.Domain

open System

/// <summary>Origin of an external-pressure material table.</summary>
type ExternalPressureTableSource =
    /// Table read from a material database.
    | MaterialDatabase
    /// Table generated using ASME Code Case 2964.
    | CodeCase2964

/// <summary>
/// External-pressure material table: X = Factor A (dimensionless), Y = Factor B (MPa), at a
/// reference temperature.
/// </summary>
/// <remarks>
/// Factor A and Factor B are the ASME Section II Part D external-pressure chart quantities: Factor A
/// (from the geometry charts, a function of L/Do and Do/t) selects Factor B from this
/// material-specific chart; Factor B is dimensioned as a stress (MPa) and is used directly in the
/// UG-28 allowable-external-pressure formulas. This table stores that Factor-B value in
/// <c>ExternalPressureTablePoint.CompressiveStress</c> — the field name reflects what Factor B
/// physically represents (an allowable compressive stress), not a separate, further-derived quantity.
/// </remarks>
type ExternalPressureTable =
    {
        /// The underlying property table (X=Factor A, Y=Factor B / allowable compressive stress MPa).
        Table: PropertyTable
        /// Reference temperature or assessment condition (degC).
        ReferenceTemperature: float
        /// Reference duration in hours. None is time-independent; Some hours is isochronous.
        ReferenceDurationHours: float option
        /// Origin of the table data.
        Source: ExternalPressureTableSource
        /// Optional reduction factor used while generating a Code Case 2964 minimum curve.
        ReductionFactor: float option
    }

/// <summary>Factory and accessor functions for <see cref="ExternalPressureTable"/>.</summary>
module ExternalPressureTable =
    let private isFinite value =
        not (Double.IsNaN value || Double.IsInfinity value)

    /// <summary>Creates an ExternalPressureTable with required metadata.</summary>
    let create
        (table: PropertyTable)
        (referenceTemperature: float)
        (referenceDurationHours: float option)
        (source: ExternalPressureTableSource)
        (reductionFactor: float option)
        : ExternalPressureTable =
        { Table = table
          ReferenceTemperature = referenceTemperature
          ReferenceDurationHours = referenceDurationHours
          Source = source
          ReductionFactor = reductionFactor }

    /// <summary>Validates external-pressure-specific metadata and A-vs-stress values.</summary>
    let validate (externalPressureTable: ExternalPressureTable) : Result<ExternalPressureTable, MaterialError> =
        if isNull (box externalPressureTable) then
            Error(MaterialError.InvalidOperation "External-pressure table cannot be null")
        elif not (isFinite externalPressureTable.ReferenceTemperature) then
            Error(MaterialError.InvalidOperation "External-pressure table temperature must be finite")
        elif
            externalPressureTable.ReferenceDurationHours
            |> Option.exists (fun duration -> not (isFinite duration) || duration <= 0.0)
        then
            Error(MaterialError.InvalidOperation "External-pressure table duration must be finite and > 0")
        elif
            externalPressureTable.ReductionFactor
            |> Option.exists (fun factor -> not (isFinite factor) || factor <= 0.0 || factor > 1.0)
        then
            Error(MaterialError.InvalidOperation "External-pressure reduction factor must be in (0, 1]")
        else
            PropertyTable.validate externalPressureTable.Table
            |> Result.bind (fun _ ->
                let invalidPoint =
                    externalPressureTable.Table.Columns
                    |> List.collect (fun column -> column.Entries)
                    |> List.exists (fun entry -> entry.X <= 0.0 || entry.Value <= 0.0)

                if invalidPoint then
                    Error(
                        MaterialError.InvalidOperation
                            "External-pressure Factor A and allowable compressive stress must be > 0"
                    )
                else
                    Ok externalPressureTable)

    /// <summary>Gets the underlying PropertyTable.</summary>
    let table (t: ExternalPressureTable) : PropertyTable = t.Table

    /// <summary>Gets the reference temperature (degC).</summary>
    let referenceTemperature (t: ExternalPressureTable) : float = t.ReferenceTemperature

    /// <summary>Gets the duration: None for time-independent data, Some hours for isochronous data.</summary>
    let referenceDurationHours (t: ExternalPressureTable) : float option = t.ReferenceDurationHours

    /// <summary>Returns true when the table represents time-independent data.</summary>
    let isTimeIndependent (t: ExternalPressureTable) : bool = t.ReferenceDurationHours.IsNone

    /// <summary>Returns true when the table represents isochronous data.</summary>
    let isIsochronous (t: ExternalPressureTable) : bool = t.ReferenceDurationHours.IsSome

    /// <summary>Gets the table origin.</summary>
    let source (t: ExternalPressureTable) : ExternalPressureTableSource = t.Source

    /// <summary>Unwraps the ExternalPressureTable to access the underlying PropertyTable.</summary>
    let unwrap (t: ExternalPressureTable) : PropertyTable = t.Table
