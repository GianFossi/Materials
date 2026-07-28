namespace MaterialLibrary.Domain

open System

/// ASME Section II-D table containing a referenced note.
type AsmePartDTable =
    | Table1A
    | Table1B
    | Table5A
    | Table5B
    | TableSy
    | TableSu
    | TableSBolting

/// A note identifier as printed in one specific ASME Section II-D table.
type AsmeNoteReference =
    {
        Table: AsmePartDTable
        Code: string
    }

module AsmeNoteReference =
    let parse table (value: string option) =
        value
        |> Option.toList
        |> List.collect (fun text ->
            text.Split([| ','; ';' |], StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
            |> Array.toList)
        |> List.filter (String.IsNullOrWhiteSpace >> not)
        |> List.map (fun code ->
            { Table = table
              Code = code.ToUpperInvariant() })
        |> List.distinct

/// ASME database table from which an allowable-stress dataset was read.
type AllowableStressSource =
    | Division1AllowableStress
    | Division1HighAllowableStress
    | Division2AllowableStress
    | BoltingAllowableStress

/// Retains the selected material variant; the source distinguishes S1 from S1H.
type AllowableStressCase =
    | StandardStrengthAllowableStress
    | HighStrengthAllowableStress

/// One independently selectable allowable-stress curve from the database.
type AllowableStressDataset =
    {
        DatabaseRowId: int64
        Source: AllowableStressSource
        Case: AllowableStressCase
        Table: PropertyTable
        SizeMinimum: float option
        SizeMaximum: float option
        MaximumTemperature: float option
        CreepTemperature: float option
        /// Structured ASME Section II-D references imported from the source row.
        AsmeNoteReferences: AsmeNoteReference list
        /// Optional user-defined free text; never populated from ASME note-code columns.
        Notes: string option
    }

module AllowableStressDataset =
    let private isFinite value =
        not (Double.IsNaN value || Double.IsInfinity value)

    let validate dataset =
        if dataset.DatabaseRowId <= 0L then
            Error(MaterialError.InvalidOperation "Allowable-stress database row ID must be positive")
        elif dataset.SizeMinimum |> Option.exists (isFinite >> not) then
            Error(MaterialError.InvalidOperation "Allowable-stress minimum size must be finite")
        elif dataset.SizeMaximum |> Option.exists (isFinite >> not) then
            Error(MaterialError.InvalidOperation "Allowable-stress maximum size must be finite")
        elif
            match dataset.SizeMinimum, dataset.SizeMaximum with
            | Some lower, Some upper -> lower >= upper
            | _ -> false
        then
            Error(MaterialError.InvalidOperation "Allowable-stress size range must be ascending")
        else
            PropertyTable.validate dataset.Table
            |> Result.map (fun _ -> dataset)
