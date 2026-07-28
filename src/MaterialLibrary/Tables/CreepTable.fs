namespace MaterialLibrary.Domain

open System

/// <summary>
/// Creep table: X = time (hours), Y = creep strain (%).
/// Metadata: ReferenceTemperature (degC), optional Notes for applied stress.
/// </summary>
type CreepTable =
    {
        /// The underlying property table (X=time h, Y=strain%).
        Table: PropertyTable
        /// Reference temperature or assessment condition (degC).
        ReferenceTemperature: float
        /// Applied stress for the creep test (MPa), when known.
        AppliedStress: float option
        /// Database origin or explicitly selected generation model.
        Source: CreepTableSource
        /// Mandatory model/data applicability warning.
        ApplicabilityWarning: string
        /// Optional applied stress or other notes (MPa or description).
        Notes: string option
    }

/// <summary>Factory and accessor functions for <see cref="CreepTable"/>.</summary>
module CreepTable =
    let private isFinite value =
        not (Double.IsNaN value || Double.IsInfinity value)

    /// <summary>Creates a CreepTable with structured applied-stress metadata.</summary>
    let createWithAppliedStress
        (table: PropertyTable)
        (referenceTemperature: float)
        (appliedStress: float option)
        (source: CreepTableSource)
        (notes: string option)
        : CreepTable =
        { Table = table
          ReferenceTemperature = referenceTemperature
          AppliedStress = appliedStress
          Source = source
          ApplicabilityWarning = CreepModelApplicability.warning source
          Notes = notes }

    /// <summary>Validates creep-specific metadata and time-strain values.</summary>
    let validate (creepTable: CreepTable) : Result<CreepTable, MaterialError> =
        if isNull (box creepTable) then
            Error(MaterialError.InvalidOperation "Creep table cannot be null")
        elif not (isFinite creepTable.ReferenceTemperature) then
            Error(MaterialError.InvalidOperation "Creep table temperature must be finite")
        elif
            match creepTable.AppliedStress with
            | Some stress -> not (isFinite stress) || stress <= 0.0
            | None -> true
        then
            Error(MaterialError.InvalidOperation "Creep table applied stress must be finite and > 0")
        else
            PropertyTable.validate creepTable.Table
            |> Result.bind (fun _ ->
                let invalidTime =
                    creepTable.Table.Columns
                    |> List.collect (fun column -> column.Entries)
                    |> List.exists (fun entry -> entry.X < 0.0)

                if invalidTime then
                    Error(MaterialError.InvalidOperation "Creep table time values must be >= 0")
                else
                    Ok creepTable)

    /// <summary>Gets the underlying PropertyTable.</summary>
    let table (t: CreepTable) : PropertyTable = t.Table

    /// <summary>Gets the reference temperature (degC).</summary>
    let referenceTemperature (t: CreepTable) : float = t.ReferenceTemperature

    /// <summary>Gets the optional notes/description.</summary>
    let notes (t: CreepTable) : string option = t.Notes

    /// <summary>Unwraps the CreepTable to access the underlying PropertyTable.</summary>
    let unwrap (t: CreepTable) : PropertyTable = t.Table
