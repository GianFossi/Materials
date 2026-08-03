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
        /// Size, diameter, or thickness band this curve applies to (mm).
        SizeRange: SizeThicknessRange
        MaximumTemperature: float option
        CreepTemperature: float option
        /// Structured ASME Section II-D references imported from the source row.
        AsmeNoteReferences: AsmeNoteReference list
        /// Optional user-defined free text; never populated from ASME note-code columns.
        Notes: string option
    }

/// <summary>Validation, lookup, and display helpers for <see cref="AllowableStressDataset"/>.</summary>
module AllowableStressDataset =
    /// <summary>Checks the row identity, the size band, and the underlying curve.</summary>
    /// <param name="dataset">Dataset to validate.</param>
    /// <returns><c>Ok dataset</c> when usable, otherwise a describing error.</returns>
    let validate (dataset: AllowableStressDataset) : Result<AllowableStressDataset, MaterialError> =
        if dataset.DatabaseRowId <= 0L then
            Error(MaterialError.InvalidOperation "Allowable-stress database row ID must be positive")
        else
            SizeThicknessRange.validate "Allowable-stress" dataset.SizeRange
            |> Result.bind (fun _ -> PropertyTable.validate dataset.Table)
            |> Result.map (fun _ -> dataset)

    /// <summary>Name of the Code section a source belongs to, for display.</summary>
    /// <param name="source">Source table the dataset was read from.</param>
    /// <returns>Short label such as <c>"VIII-1"</c>.</returns>
    let divisionLabel source =
        match source with
        | Division1AllowableStress
        | Division1HighAllowableStress -> "VIII-1"
        | Division2AllowableStress -> "VIII-2"
        | BoltingAllowableStress -> "Bolting"

    /// <summary>Name of the allowable-stress case, for display.</summary>
    /// <param name="case">Case carried by the dataset.</param>
    /// <returns><c>"Normal"</c> or <c>"High"</c>.</returns>
    let caseLabel case =
        match case with
        | StandardStrengthAllowableStress -> "Normal"
        | HighStrengthAllowableStress -> "High"

    /// <summary>Selects the datasets of one source that cover a given section size.</summary>
    /// <param name="source">Source table wanted.</param>
    /// <param name="size">Governing size, diameter, or thickness (mm).</param>
    /// <param name="datasets">Datasets to search.</param>
    /// <returns>The matching datasets, lightest band first.</returns>
    let forSize source (size: float) (datasets: AllowableStressDataset list) =
        datasets
        |> List.filter (fun dataset ->
            dataset.Source = source && SizeThicknessRange.contains size dataset.SizeRange)
        |> List.sortBy (fun dataset -> SizeThicknessRange.sortKey dataset.SizeRange, dataset.DatabaseRowId)

    /// <summary>Sort key grouping datasets by source, then from the lightest band to the heaviest.</summary>
    /// <param name="dataset">Dataset to rank.</param>
    /// <returns>A tuple usable directly with <c>List.sortBy</c>.</returns>
    let sortKey (dataset: AllowableStressDataset) =
        let sourceOrder =
            match dataset.Source with
            | Division1AllowableStress -> 0
            | Division1HighAllowableStress -> 1
            | Division2AllowableStress -> 2
            | BoltingAllowableStress -> 3

        let lower, upper = SizeThicknessRange.sortKey dataset.SizeRange
        sourceOrder, lower, upper, dataset.DatabaseRowId
