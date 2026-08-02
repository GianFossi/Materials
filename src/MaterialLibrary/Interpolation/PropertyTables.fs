namespace MaterialLibrary.Interpolation

open System
open MaterialLibrary.Domain

// One-, two-, and three-dimensional property-table shapes and their lookups.

/// A (temperature, property-value) knot used as input to all property table types.
type TvPoint = float * float


/// Type 1 — Property as a function of temperature only.
/// Linear interpolation on T; a single input point is extended as a constant for all temperatures.
type PropertyTableType1 =
    {
        /// Input (T, V) knots, sorted ascending by T.
        Points: TvPoint list
        /// true = extend first/last value outside the tabulated range; false = return Double.NaN.
        ExtrapolateFlat: bool
    }

/// Construction and evaluation functions for <see cref="PropertyTableType1"/>.
module PropertyTableType1 =

    /// Creates a Type-1 table, sorting the input knots by temperature.
    let create (points: TvPoint list) (extrapolateFlat: bool) : PropertyTableType1 =
        { Points = points |> List.sortBy fst
          ExtrapolateFlat = extrapolateFlat }

    /// Evaluates the property at temperature t using linear interpolation.
    let evaluate (t: float) (table: PropertyTableType1) : float =
        evalAtTemp table.ExtrapolateFlat table.Points t


/// Type 2 — Property as a function of temperature and a size range (no interpolation on size).
/// Each curve is keyed by the maximum (inclusive) size it applies to.
/// The selected curve is the one with the smallest max-size that is >= the query size.
/// When the query size exceeds all defined max-sizes the last (largest) curve is used.
type PropertyTableType2 =
    {
        /// (maxSize, (T,V) knots) entries, sorted ascending by maxSize.
        Curves: (float * TvPoint list) list
        /// true = extend first/last value outside the tabulated T range; false = return Double.NaN.
        ExtrapolateFlat: bool
    }

/// Construction and evaluation functions for <see cref="PropertyTableType2"/>.
module PropertyTableType2 =

    /// Creates a Type-2 table, sorting the curves by their max-size key.
    let create (curves: (float * TvPoint list) list) (extrapolateFlat: bool) : PropertyTableType2 =
        { Curves = curves |> List.sortBy fst
          ExtrapolateFlat = extrapolateFlat }

    /// Selects the correct size bucket and evaluates the property at temperature t.
    /// No interpolation is performed across size boundaries.
    let evaluate (size: float) (t: float) (table: PropertyTableType2) : float =
        let sorted = table.Curves |> List.sortBy fst

        let pts =
            match sorted |> List.tryFind (fun (maxS, _) -> size <= maxS) with
            | Some(_, pts) -> pts
            | None -> if List.isEmpty sorted then [] else snd (List.last sorted)

        if List.isEmpty pts then
            Double.NaN
        else
            evalAtTemp table.ExtrapolateFlat pts t


/// Type 3 — Property as a function of temperature and a second continuous parameter (double interpolation).
/// Step 1: linear interpolation along T on the two nearest parameter curves.
/// Step 2: linear interpolation between those two T-evaluated values across the second parameter.
type PropertyTableType3 =
    {
        /// (paramValue, (T,V) knots) entries, sorted ascending by parameter value.
        Curves: (float * TvPoint list) list
        /// true = extend first/last value outside the tabulated T range; false = return Double.NaN.
        ExtrapolateFlat: bool
    }

/// Construction and evaluation functions for <see cref="PropertyTableType3"/>.
module PropertyTableType3 =

    /// Creates a Type-3 table, sorting the curves by parameter value.
    let create (curves: (float * TvPoint list) list) (extrapolateFlat: bool) : PropertyTableType3 =
        { Curves = curves |> List.sortBy fst
          ExtrapolateFlat = extrapolateFlat }

    /// Evaluates the property at (param, t) using bilinear (T then param) linear interpolation.
    /// For param outside the defined range the nearest boundary curve is used (flat extension on param).
    let evaluate (param: float) (t: float) (table: PropertyTableType3) : float =
        let sorted = table.Curves |> List.sortBy fst

        match sorted with
        | [] -> Double.NaN
        | [ (_, pts) ] -> evalAtTemp table.ExtrapolateFlat pts t
        | _ ->
            let pMin, ptsMin = List.head sorted
            let pMax, ptsMax = List.last sorted

            if param <= pMin then
                evalAtTemp table.ExtrapolateFlat ptsMin t
            elif param >= pMax then
                evalAtTemp table.ExtrapolateFlat ptsMax t
            else
                let lower = sorted |> List.tryFindBack (fun (p, _) -> p <= param)
                let upper = sorted |> List.tryFind (fun (p, _) -> p >= param)

                match lower, upper with
                | Some(p0, pts0), Some(p1, pts1) ->
                    let v0 = evalAtTemp table.ExtrapolateFlat pts0 t
                    let v1 = evalAtTemp table.ExtrapolateFlat pts1 t

                    if Double.IsNaN v0 || Double.IsNaN v1 then
                        Double.NaN
                    else
                        linearInterpolate p0 v0 p1 v1 param
                | _ -> Double.NaN


// ========== SPLINE AND POLYNOMIAL HELPERS ==========
