namespace MaterialLibrary.Interpolation

open System
open MaterialLibrary.Domain

/// CULTURE RULE: Numeric parsing and formatting for XML/JSON persistence must always use CultureInfo.InvariantCulture.
/// <summary>
/// Specifies the algorithm used when evaluating a property at a temperature or time not directly tabulated.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><description><c>Linear</c> — piecewise linear (trapezoidal); exact at knot points, first order.</description></item>
///   <item><description><c>CubicSpline</c> — natural cubic spline with zero second derivatives at the ends.</description></item>
///   <item><description><c>LagrangePolynomial n</c> — local Lagrange polynomial of degree n (1..10); use with caution near dataset boundaries.</description></item>
///   <item><description><c>Constant</c> — nearest-neighbour (step) interpolation; returns the value at the closest knot.</description></item>
/// </list>
/// </remarks>
type InterpolationMode =
    | Linear
    | CubicSpline
    | LagrangePolynomial of int
    | Constant

// ========== LINEAR INTERPOLATION HELPER ==========

[<AutoOpen>]
module internal Helpers =
    /// <summary>Performs a one-dimensional linear (lerp) interpolation between two known points.</summary>
    /// <param name="x0">Abscissa of the left knot.</param>
    /// <param name="y0">Ordinate of the left knot.</param>
    /// <param name="x1">Abscissa of the right knot.</param>
    /// <param name="y1">Ordinate of the right knot.</param>
    /// <param name="x">Query abscissa. Must satisfy x0 ≤ x ≤ x1.</param>
    /// <returns>
    /// Interpolated ordinate y = y0 + (y1 - y0) · (x - x0) / (x1 - x0).
    /// Returns <paramref name="y0"/> when x0 = x1 to avoid division by zero.
    /// </returns>
    let linearInterpolate (x0: float) (y0: float) (x1: float) (y1: float) (x: float) : float =
        if x0 = x1 then
            y0
        else
            y0 + (y1 - y0) * (x - x0) / (x1 - x0)

    /// Evaluates a property at temperature t by linear interpolation on sorted (T, V) knots.
    /// A single-knot list returns that constant for any t (no interpolation, no range check).
    /// extrapolateFlat=true: extend first/last value outside range; false: return Double.NaN.
    let evalAtTemp (extrapolateFlat: bool) (points: (float * float) list) (t: float) : float =
        let sorted: (float * float) list = points |> List.sortBy fst

        match sorted with
        | [] -> Double.NaN
        | [ (_, v: float) ] -> v
        | _ ->
            let (tMin: float), (vMin: float) = List.head sorted
            let (tMax: float), (vMax: float) = List.last sorted

            if t < tMin then
                if extrapolateFlat then vMin else Double.NaN
            elif t > tMax then
                if extrapolateFlat then vMax else Double.NaN
            else
                match
                    sorted |> List.tryFindBack (fun (ti, _) -> ti <= t), sorted |> List.tryFind (fun (ti, _) -> ti >= t)
                with
                | Some(t0: float, v0: float), Some(t1: float, v1: float) -> linearInterpolate t0 v0 t1 v1 t
                | _ -> Double.NaN

    let private cubicSplineCoefficients (points: (float * float) list) : (float * float * float * float * float) list =
        let sorted = points |> List.sortBy fst
        let n = List.length sorted - 1

        if n < 1 then
            []
        else
            let xs = sorted |> List.map fst |> Array.ofList
            let ys = sorted |> List.map snd |> Array.ofList
            let h = Array.init n (fun i -> xs.[i + 1] - xs.[i])

            let lo = Array.zeroCreate (n + 1)
            let diag = Array.create (n + 1) 1.0
            let up = Array.zeroCreate (n + 1)
            let rhs = Array.zeroCreate (n + 1)

            for i in 1 .. n - 1 do
                lo.[i] <- h.[i - 1]
                diag.[i] <- 2.0 * (h.[i - 1] + h.[i])
                up.[i] <- h.[i]
                rhs.[i] <- 3.0 * ((ys.[i + 1] - ys.[i]) / h.[i] - (ys.[i] - ys.[i - 1]) / h.[i - 1])

            let diagW = Array.copy diag
            let rhsW = Array.copy rhs

            for i in 1..n do
                let m = lo.[i] / diagW.[i - 1]
                diagW.[i] <- diagW.[i] - m * up.[i - 1]
                rhsW.[i] <- rhsW.[i] - m * rhsW.[i - 1]

            let c = Array.zeroCreate (n + 1)
            c.[n] <- rhsW.[n] / diagW.[n]

            for i in n - 1 .. -1 .. 0 do
                c.[i] <- (rhsW.[i] - up.[i] * c.[i + 1]) / diagW.[i]

            [ for i in 0 .. n - 1 ->
                  let a = ys.[i]
                  let b = (ys.[i + 1] - ys.[i]) / h.[i] - h.[i] * (2.0 * c.[i] + c.[i + 1]) / 3.0
                  let dco = (c.[i + 1] - c.[i]) / (3.0 * h.[i])
                  (xs.[i], a, b, c.[i], dco) ]

    let private cubicSplineEvaluate (segments: (float * float * float * float * float) list) (x: float) : float =
        if List.isEmpty segments then
            Double.NaN
        else
            let sorted = segments |> List.sortBy (fun (x0, _, _, _, _) -> x0)

            let seg =
                sorted
                |> List.tryFindBack (fun (x0, _, _, _, _) -> x0 <= x)
                |> Option.defaultValue (List.head sorted)

            let (x0, a, b, c, d) = seg
            let dx = x - x0
            a + b * dx + c * dx * dx + d * dx * dx * dx

    let private lagrangeEvaluate (degree: int) (points: (float * float) list) (x: float) : float =
        if degree < 1 || degree > 10 then
            Double.NaN
        else
            let sorted = points |> List.sortBy fst |> Array.ofList
            let m = degree + 1

            if sorted.Length < m then
                Double.NaN
            else
                let pivot =
                    sorted
                    |> Array.tryFindIndexBack (fun (xi, _) -> xi <= x)
                    |> Option.defaultValue 0

                let start = max 0 (min (sorted.Length - m) (pivot - m / 2))
                let knots = sorted.[start .. start + m - 1]

                knots
                |> Array.sumBy (fun (xk, yk) ->
                    let basis =
                        knots
                        |> Array.fold (fun acc (xj, _) -> if xj = xk then acc else acc * (x - xj) / (xk - xj)) 1.0

                    yk * basis)

    /// Evaluates a sorted 1D dataset using the requested interpolation mode.
    let interpolate1D
        (mode: InterpolationMode)
        (points: (float * float) list)
        (x: float)
        : Result<float, InterpolationError> =
        let sorted = points |> List.sortBy fst

        if List.isEmpty sorted then
            Error InterpolationError.InsufficientData
        else
            let minX = fst (List.head sorted)
            let maxX = fst (List.last sorted)

            if sorted.Length = 1 then
                Ok(snd (List.head sorted))
            elif x < minX || x > maxX then
                Error(InterpolationError.OutOfRange(minX, maxX))
            else
                match mode with
                | Constant ->
                    let closest = sorted |> List.minBy (fun (xi, _) -> abs (xi - x))
                    Ok(snd closest)

                | Linear ->
                    match
                        sorted |> List.tryFindBack (fun (xi, _) -> xi <= x),
                        sorted |> List.tryFind (fun (xi, _) -> xi >= x)
                    with
                    | Some(x0, y0), Some(x1, y1) -> Ok(linearInterpolate x0 y0 x1 y1 x)
                    | _ -> Error InterpolationError.InsufficientData

                | CubicSpline ->
                    let value =
                        sorted
                        |> cubicSplineCoefficients
                        |> fun segments -> cubicSplineEvaluate segments x

                    if Double.IsNaN value then
                        Error InterpolationError.InsufficientData
                    else
                        Ok value

                | LagrangePolynomial degree ->
                    let value = lagrangeEvaluate degree sorted x

                    if Double.IsNaN value then
                        Error InterpolationError.InsufficientData
                    else
                        Ok value

// ========== SPECIFIC HEAT INTERPOLATION ==========
