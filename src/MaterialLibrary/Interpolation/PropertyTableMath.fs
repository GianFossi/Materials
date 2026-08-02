namespace MaterialLibrary.Interpolation

open System
open MaterialLibrary.Domain

// Cubic-spline segments and the CLR-friendly maths facade over the table types.

/// Coefficients for one segment [X0, X_next) of a natural cubic spline.
/// The interpolant on this segment is: S(x) = A + B·(x−X0) + C·(x−X0)² + D·(x−X0)³
type CubicSplineSegment =
    {
        /// Left-knot abscissa of this interval.
        X0: float
        /// Constant coefficient — equals y at the left knot.
        A: float
        /// Linear coefficient.
        B: float
        /// Quadratic coefficient (half the second derivative at X0).
        C: float
        /// Cubic coefficient.
        D: float
    }

/// <summary>
/// Static helpers for spline and polynomial computations on raw (x, y) tabular data.
/// </summary>
[<AbstractClass; Sealed>]
type PropertyTableMath private () =

    /// <summary>
    /// Computes natural cubic spline coefficients for a set of (x, y) knots.
    /// Natural boundary conditions enforce zero second derivative at both ends (c₀ = cₙ = 0).
    /// </summary>
    /// <param name="points">Unsorted list of (x, y) knots; duplicate x-values produce undefined results.</param>
    /// <returns>
    /// One <see cref="CubicSplineSegment"/> per interval between adjacent knots,
    /// or an empty list when fewer than two knots are supplied.
    /// </returns>
    static member CubicSplineCoefficients(points: (float * float) list) : CubicSplineSegment list =
        let sorted = points |> List.sortBy fst
        let n = List.length sorted - 1

        if n < 1 then
            []
        else
            let xs = sorted |> List.map fst |> Array.ofList
            let ys = sorted |> List.map snd |> Array.ofList
            let h = Array.init n (fun i -> xs.[i + 1] - xs.[i])

            // (n+1)-sized tridiagonal system; rows 0 and n are identity rows → c[0]=c[n]=0.
            let lo = Array.zeroCreate (n + 1)
            let diag = Array.create (n + 1) 1.0
            let up = Array.zeroCreate (n + 1)
            let rhs = Array.zeroCreate (n + 1)

            for i in 1 .. n - 1 do
                lo.[i] <- h.[i - 1]
                diag.[i] <- 2.0 * (h.[i - 1] + h.[i])
                up.[i] <- h.[i]
                rhs.[i] <- 3.0 * ((ys.[i + 1] - ys.[i]) / h.[i] - (ys.[i] - ys.[i - 1]) / h.[i - 1])

            // Thomas algorithm — forward elimination.
            let diagW = Array.copy diag
            let rhsW = Array.copy rhs

            for i in 1..n do
                let m = lo.[i] / diagW.[i - 1]
                diagW.[i] <- diagW.[i] - m * up.[i - 1]
                rhsW.[i] <- rhsW.[i] - m * rhsW.[i - 1]

            // Back substitution — second-derivative coefficients c.
            let c = Array.zeroCreate (n + 1)
            c.[n] <- rhsW.[n] / diagW.[n]

            for i in n - 1 .. -1 .. 0 do
                c.[i] <- (rhsW.[i] - up.[i] * c.[i + 1]) / diagW.[i]

            [ for i in 0 .. n - 1 ->
                  let a = ys.[i]
                  let b = (ys.[i + 1] - ys.[i]) / h.[i] - h.[i] * (2.0 * c.[i] + c.[i + 1]) / 3.0
                  let dco = (c.[i + 1] - c.[i]) / (3.0 * h.[i])

                  { X0 = xs.[i]
                    A = a
                    B = b
                    C = c.[i]
                    D = dco } ]

    /// <summary>
    /// Evaluates a cubic spline at the given abscissa using pre-computed segment coefficients.
    /// For x below the first knot the first segment is used; for x above the last knot the last segment is used.
    /// </summary>
    /// <param name="segments">Segment list returned by <see cref="CubicSplineCoefficients"/>.</param>
    /// <param name="x">Query abscissa.</param>
    /// <returns>Interpolated value, or <see cref="Double.NaN"/> when the segment list is empty.</returns>
    static member CubicSplineEvaluate (segments: CubicSplineSegment list) (x: float) : float =
        if List.isEmpty segments then
            Double.NaN
        else
            let sorted = segments |> List.sortBy (fun s -> s.X0)

            let seg =
                sorted
                |> List.tryFindBack (fun s -> s.X0 <= x)
                |> Option.defaultValue (List.head sorted)

            let dx = x - seg.X0
            seg.A + seg.B * dx + seg.C * dx * dx + seg.D * dx * dx * dx

    /// <summary>
    /// Evaluates a Lagrange interpolating polynomial of the specified degree at <paramref name="x"/>.
    /// Selects the <c>degree+1</c> knots whose x-values bracket <paramref name="x"/> most closely,
    /// reducing Runge-phenomenon amplification near dataset boundaries.
    /// </summary>
    /// <param name="degree">Polynomial degree n; requires at least n+1 data points.</param>
    /// <param name="points">Tabulated (x, y) data; need not be sorted.</param>
    /// <param name="x">Query abscissa.</param>
    /// <returns>
    /// Lagrange-interpolated value, or <see cref="Double.NaN"/> when fewer than <c>degree+1</c>
    /// data points are available.
    /// </returns>
    static member LagrangeEvaluate (degree: int) (points: (float * float) list) (x: float) : float =
        let sorted = points |> List.sortBy fst |> Array.ofList
        let m = degree + 1

        if sorted.Length < m then
            Double.NaN
        else
            // Centre a window of m knots around the last knot with xi <= x.
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
                    |> Array.fold
                        (fun acc (xj, _) ->
                            // Skip the self-term; float equality is safe here (same array element).
                            if xj = xk then acc else acc * (x - xj) / (xk - xj))
                        1.0

                yk * basis)
