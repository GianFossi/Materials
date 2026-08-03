namespace MaterialLibrary.Domain

/// <summary>Which minimum-strength table a curve was read from.</summary>
/// <remarks>
/// ASME Section II Part D publishes the two temperature-dependent minimum strengths in separate
/// tables, and they are not interchangeable: Table Y-1 gives the yield strength Sy used for
/// deformation-controlled limits, Table U the ultimate tensile strength Su used for the
/// load-controlled ones.
/// </remarks>
type TensileStrengthKind =
    /// Minimum yield strength Sy(T), ASME Section II Part D Table Y-1.
    | YieldStrengthSy
    /// Minimum ultimate tensile strength Su(T), ASME Section II Part D Table U.
    | UltimateTensileStrengthSu

/// <summary>
/// One size/thickness group's minimum-strength curve against temperature.
/// </summary>
/// <remarks>
/// <para>
/// A specification and grade can carry several of these per strength kind, because ASME derates
/// heavier sections. Keeping one dataset per published row preserves that grouping instead of
/// collapsing it to a single governing curve, which is what
/// <see cref="StrengthProperties.TensileProperties"/> holds.
/// </para>
/// <para>
/// Units: <c>Table</c> maps temperature in degC to strength in MPa; <c>SizeRange</c> is in mm.
/// </para>
/// </remarks>
type TensileStrengthDataset =
    {
        /// Primary key of the source row, so a curve stays traceable to the reference database.
        DatabaseRowId: int64
        /// Whether this curve is Sy(T) or Su(T).
        Kind: TensileStrengthKind
        /// Strength curve: X = temperature (degC), Y = strength (MPa).
        Table: PropertyTable
        /// Size, diameter, or thickness band the curve applies to (mm).
        SizeRange: SizeThicknessRange
        /// Structured ASME Section II-D note references imported from the source row.
        AsmeNoteReferences: AsmeNoteReference list
        /// Optional user-defined free text; never populated from ASME note-code columns.
        Notes: string option
    }

/// <summary>Validation and lookup helpers for <see cref="TensileStrengthDataset"/>.</summary>
module TensileStrengthDataset =
    /// <summary>Name of the strength kind as printed in ASME Section II Part D.</summary>
    /// <param name="kind">Strength kind.</param>
    /// <returns>Short label such as <c>"Sy"</c>.</returns>
    let kindSymbol kind =
        match kind with
        | YieldStrengthSy -> "Sy"
        | UltimateTensileStrengthSu -> "Su"

    /// <summary>Checks the row identity, the size band, and the underlying curve.</summary>
    /// <param name="dataset">Dataset to validate.</param>
    /// <returns><c>Ok dataset</c> when usable, otherwise a describing error.</returns>
    let validate (dataset: TensileStrengthDataset) : Result<TensileStrengthDataset, MaterialError> =
        if dataset.DatabaseRowId <= 0L then
            Error(MaterialError.InvalidOperation "Tensile-strength database row ID must be positive")
        else
            SizeThicknessRange.validate "Tensile-strength" dataset.SizeRange
            |> Result.bind (fun _ -> PropertyTable.validate dataset.Table)
            |> Result.map (fun _ -> dataset)

    /// <summary>Selects the datasets of one strength kind that cover a given section size.</summary>
    /// <param name="kind">Strength kind wanted.</param>
    /// <param name="size">Governing size, diameter, or thickness (mm).</param>
    /// <param name="datasets">Datasets to search.</param>
    /// <returns>The matching datasets, lightest band first.</returns>
    /// <remarks>
    /// Returns every match rather than one, because a material may legitimately publish several
    /// rows for the same band; the caller decides which applies.
    /// </remarks>
    let forSize kind (size: float) (datasets: TensileStrengthDataset list) =
        datasets
        |> List.filter (fun dataset -> dataset.Kind = kind && SizeThicknessRange.contains size dataset.SizeRange)
        |> List.sortBy (fun dataset -> SizeThicknessRange.sortKey dataset.SizeRange, dataset.DatabaseRowId)

    /// <summary>Sort key grouping datasets by kind, then from the lightest band to the heaviest.</summary>
    /// <param name="dataset">Dataset to rank.</param>
    /// <returns>A tuple usable directly with <c>List.sortBy</c>.</returns>
    let sortKey (dataset: TensileStrengthDataset) =
        let kindOrder =
            match dataset.Kind with
            | YieldStrengthSy -> 0
            | UltimateTensileStrengthSu -> 1

        let lower, upper = SizeThicknessRange.sortKey dataset.SizeRange
        kindOrder, lower, upper, dataset.DatabaseRowId
