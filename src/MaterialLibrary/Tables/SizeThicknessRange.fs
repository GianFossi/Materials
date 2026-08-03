namespace MaterialLibrary.Domain

open System

/// <summary>
/// The size, diameter, or thickness band that a strength dataset applies to.
/// </summary>
/// <remarks>
/// <para>
/// ASME Section II Part D tabulates Sy, Su, and the allowable stresses per "Size/Thickness" group:
/// the same specification and grade can carry several curves, one per band, because the guaranteed
/// minimum strength falls as the section gets heavier. Selecting the wrong band silently overstates
/// the allowable stress, so the band travels with the curve rather than being applied by the caller.
/// </para>
/// <para>
/// Which dimension the limit refers to depends on the product form, per the note printed under
/// Table 1A: wall thickness for tubing, pipe, pipe fittings and hollow forgings; thickness for
/// plate, flat bar, polygonal bar and forgings; diameter for solid bar and bolting; and the thickest
/// cross-section for other pressure parts such as castings.
/// </para>
/// <para>
/// Units: millimetres. Both ends are optional and independently inclusive or exclusive, which is
/// what makes adjacent bands such as "up to and including 5" and "over 5" partition the range
/// instead of overlapping at the boundary.
/// </para>
/// </remarks>
type SizeThicknessRange =
    {
        /// Lower bound of the band (mm); <c>None</c> when the band is open below.
        Minimum: float option
        /// <c>true</c> when a section exactly equal to <c>Minimum</c> belongs to this band.
        MinimumIncluded: bool
        /// Upper bound of the band (mm); <c>None</c> when the band is open above.
        Maximum: float option
        /// <c>true</c> when a section exactly equal to <c>Maximum</c> belongs to this band.
        MaximumIncluded: bool
    }

/// <summary>Construction, testing, and display helpers for <see cref="SizeThicknessRange"/>.</summary>
module SizeThicknessRange =
    /// <summary>The unbounded band, used when a curve applies to every size.</summary>
    let all: SizeThicknessRange =
        { Minimum = None
          MinimumIncluded = true
          Maximum = None
          MaximumIncluded = true }

    /// <summary>Creates a band from its two optional bounds and their inclusivity.</summary>
    /// <param name="minimum">Lower bound (mm), or <c>None</c> when open below.</param>
    /// <param name="minimumIncluded">Whether the lower bound itself belongs to the band.</param>
    /// <param name="maximum">Upper bound (mm), or <c>None</c> when open above.</param>
    /// <param name="maximumIncluded">Whether the upper bound itself belongs to the band.</param>
    /// <returns>The corresponding band.</returns>
    let create minimum minimumIncluded maximum maximumIncluded : SizeThicknessRange =
        { Minimum = minimum
          MinimumIncluded = minimumIncluded
          Maximum = maximum
          MaximumIncluded = maximumIncluded }

    /// <summary><c>true</c> when the band places no restriction on section size.</summary>
    /// <param name="range">Band to test.</param>
    /// <returns><c>true</c> when both bounds are absent.</returns>
    let isUnbounded (range: SizeThicknessRange) =
        range.Minimum.IsNone && range.Maximum.IsNone

    /// <summary>Tests whether a section size falls inside the band.</summary>
    /// <param name="size">Governing size, diameter, or thickness (mm).</param>
    /// <param name="range">Band to test against.</param>
    /// <returns><c>true</c> when the size belongs to this band.</returns>
    /// <remarks>
    /// Honouring the inclusive flags is what keeps adjacent ASME bands disjoint: with both bounds
    /// treated as inclusive, a section exactly on a boundary would match two bands and the caller
    /// would silently get whichever came first.
    /// </remarks>
    let contains (size: float) (range: SizeThicknessRange) =
        let aboveLower =
            match range.Minimum with
            | None -> true
            | Some lower when range.MinimumIncluded -> size >= lower
            | Some lower -> size > lower

        let belowUpper =
            match range.Maximum with
            | None -> true
            | Some upper when range.MaximumIncluded -> size <= upper
            | Some upper -> size < upper

        aboveLower && belowUpper

    /// <summary>Sort key ordering bands from the lightest section to the heaviest.</summary>
    /// <param name="range">Band to rank.</param>
    /// <returns>A tuple of the lower and upper bounds, with open ends pushed to the extremes.</returns>
    let sortKey (range: SizeThicknessRange) =
        range.Minimum |> Option.defaultValue Double.NegativeInfinity,
        range.Maximum |> Option.defaultValue Double.PositiveInfinity

    /// <summary>Renders the band the way ASME prints it in the Size/Thickness column.</summary>
    /// <param name="range">Band to describe.</param>
    /// <returns>Human-readable text such as <c>"over 5 to 130 mm incl."</c>, never empty.</returns>
    let describe (range: SizeThicknessRange) =
        let number (value: float) = value.ToString("R", Globalization.CultureInfo.InvariantCulture)

        match range.Minimum, range.Maximum with
        | None, None -> "All sizes"
        | None, Some upper ->
            if range.MaximumIncluded then
                $"up to {number upper} mm incl."
            else
                $"under {number upper} mm"
        | Some lower, None ->
            if range.MinimumIncluded then
                $"{number lower} mm and over"
            else
                $"over {number lower} mm"
        | Some lower, Some upper ->
            let opening =
                if range.MinimumIncluded then
                    $"{number lower}"
                else
                    $"over {number lower}"

            let closing =
                if range.MaximumIncluded then
                    $"{number upper} mm incl."
                else
                    $"under {number upper} mm"

            $"{opening} to {closing}"

    /// <summary>Validates the bounds, rejecting non-finite or descending bands.</summary>
    /// <param name="context">Name of the owning dataset, used in the error message.</param>
    /// <param name="range">Band to validate.</param>
    /// <returns><c>Ok range</c> when usable, otherwise a describing error.</returns>
    let validate (context: string) (range: SizeThicknessRange) : Result<SizeThicknessRange, MaterialError> =
        let isFinite value =
            not (Double.IsNaN value || Double.IsInfinity value)

        if range.Minimum |> Option.exists (isFinite >> not) then
            Error(MaterialError.InvalidOperation $"{context} minimum size must be finite")
        elif range.Maximum |> Option.exists (isFinite >> not) then
            Error(MaterialError.InvalidOperation $"{context} maximum size must be finite")
        else
            match range.Minimum, range.Maximum with
            | Some lower, Some upper when lower >= upper ->
                Error(MaterialError.InvalidOperation $"{context} size range must be ascending")
            | _ -> Ok range
