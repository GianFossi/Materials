using System.Globalization;
using MaterialLibrary.Domain;

namespace MaterialLibraryCrudApp.Interop;

/// <summary>Formats F# discriminated-union errors as user-facing messages for the CRUD UI.</summary>
/// <remarks>
/// C# has no <c>match</c> over discriminated unions, but F# compiles each case to a nested
/// subclass of the union type, so a C# type-pattern <c>switch</c> is the faithful equivalent.
/// The payload of a single-field case is exposed as <c>Item</c>; multi-field cases expose
/// <c>Item1</c>, <c>Item2</c>, and so on.
/// Note that C# cannot verify exhaustiveness the way F# does, hence the explicit default arms.
/// </remarks>
internal static class MaterialErrorFormat
{
    /// <summary>Converts a <see cref="MaterialError"/> into a single display string.</summary>
    /// <param name="error">Error returned by a domain or CRUD operation.</param>
    /// <returns>A message safe to show in a dialog or status bar.</returns>
    internal static string Format(MaterialError? error) => error switch
    {
        null => "Unknown error.",
        MaterialError.NotFound e => e.Item,
        MaterialError.InvalidOperation e => e.Item,
        MaterialError.CreepModelError e => e.Item,
        MaterialError.InterpolationError e => Format(e.Item),
        // Reached only if a new case is added to the F# union without updating this switch.
        _ => error.ToString() ?? "Unknown error.",
    };

    /// <summary>Converts an <see cref="InterpolationError"/> into a display string.</summary>
    /// <param name="error">Interpolation failure reported by the domain.</param>
    /// <returns>A message safe to show in a dialog or status bar.</returns>
    private static string Format(InterpolationError error) => error switch
    {
        InterpolationError.OutOfRange e => string.Format(
            CultureInfo.CurrentCulture,
            "Value out of range [{0}, {1}].",
            e.Item1,
            e.Item2),
        InterpolationError.InvalidInput e => e.Item,
        // InsufficientData is a nullary case: it has no payload subclass to pattern-match,
        // so it is identified by its tag instead.
        _ when error.IsInsufficientData => "Insufficient data for interpolation.",
        _ => error.ToString() ?? "Interpolation error.",
    };
}
