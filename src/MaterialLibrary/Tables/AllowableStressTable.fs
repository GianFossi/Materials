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

/// <summary>Which ASME allowable-stress table a dataset was read from.</summary>
/// <remarks>
/// The four are not alternatives to choose between freely: each is tied to the Code section the
/// design is being carried out under, and mixing them overstates what the material is allowed to
/// carry.
/// </remarks>
type AllowableStressSource =
    /// Section VIII Division 1 (also Section I and Section XII), Tables 1A and 1B - the normal
    /// maximum allowable stress S.
    | Division1AllowableStress
    /// Section VIII Division 1 higher alternative allowable stress: values that exceed two thirds
    /// of the yield strength but stay within 90 % of it. Permitted where slightly greater permanent
    /// deformation is acceptable, and not for flanges of gasketed joints or anywhere small
    /// distortion causes leakage. See Section II Part D note G5.
    | Division1HighAllowableStress
    /// Section VIII Division 2, Tables 5A and 5B - the maximum allowable stress S.
    | Division2AllowableStress
    /// Bolting materials, Table 3.
    | BoltingAllowableStress

/// <summary>Distinguishes the normal allowable stress from the higher alternative one.</summary>
/// <remarks>
/// Mirrors <see cref="AllowableStressSource"/> for Division 1, where both cases are published in
/// the same table and are told apart only by the note attached to the row.
/// </remarks>
type AllowableStressCase =
    /// The normal allowable stress.
    | StandardStrengthAllowableStress
    /// The higher alternative allowable stress; see <c>Division1HighAllowableStress</c>.
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

    /// <summary>Name of the Code section a source belongs to, for display and for SQL projections.</summary>
    /// <param name="source">Source table the dataset was read from.</param>
    /// <returns>Short label such as <c>"VIII-1"</c>.</returns>
    let divisionLabel source =
        match source with
        | Division1AllowableStress
        | Division1HighAllowableStress -> "VIII-1"
        | Division2AllowableStress -> "VIII-2"
        | BoltingAllowableStress -> "Bolting"

    /// <summary>Name of the allowable-stress case, for display and for SQL projections.</summary>
    /// <param name="case">Case carried by the dataset.</param>
    /// <returns><c>"Normal"</c> or <c>"High"</c>.</returns>
    let caseLabel case =
        match case with
        | StandardStrengthAllowableStress -> "Normal"
        | HighStrengthAllowableStress -> "High"
