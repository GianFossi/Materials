using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace MaterialLibraryCrudApp.Interop;

/// <summary>
/// Conversions between the F# core types exposed on the MaterialLibrary public API
/// (<c>FSharpOption</c>, <c>FSharpResult</c>, <c>FSharpList</c>) and their idiomatic
/// C# counterparts (nullable references, try-patterns, <c>IReadOnlyList</c>).
/// </summary>
/// <remarks>
/// <para>
/// This type is the single place where F#-specific representations are allowed to leak
/// into the application. ViewModels and views must consume only the C# shapes produced
/// here, so that the WPF layer never has to reason about F# encodings.
/// </para>
/// <para>
/// Critical runtime detail: F# compiles <c>None</c> to a <b>null reference</b>, not to a
/// sentinel instance. A <c>FSharpOption&lt;T&gt;</c> returned from F# is therefore legitimately
/// <c>null</c>, and reading <c>.Value</c> on it throws <see cref="NullReferenceException"/>.
/// Every helper below treats <c>null</c> and <c>None</c> as the same thing.
/// </para>
/// </remarks>
internal static class FSharpInterop
{
    // ---------- Option: F# -> C# ----------

    /// <summary>Converts an F# <c>string option</c> to a nullable string.</summary>
    /// <param name="option">Option produced by F#; <c>null</c> is accepted and means <c>None</c>.</param>
    /// <returns>The wrapped string, or <c>null</c> when the option is <c>None</c>.</returns>
    internal static string? AsNullable(this FSharpOption<string>? option) =>
        // FSharpOption.None is a null reference, so the null test covers both cases.
        option is null ? null : option.Value;

    /// <summary>Converts an F# <c>float option</c> to a nullable double.</summary>
    /// <param name="option">Option produced by F#; <c>null</c> is accepted and means <c>None</c>.</param>
    /// <returns>The wrapped value (dimensionless here; units depend on the caller), or <c>null</c> when <c>None</c>.</returns>
    internal static double? AsNullable(this FSharpOption<double>? option) =>
        option is null ? null : option.Value;

    /// <summary>Converts an arbitrary reference-typed F# option to a nullable reference.</summary>
    /// <typeparam name="T">Reference type carried by the option.</typeparam>
    /// <param name="option">Option produced by F#; <c>null</c> is accepted and means <c>None</c>.</param>
    /// <returns>The wrapped value, or <c>null</c> when the option is <c>None</c>.</returns>
    internal static T? AsNullableRef<T>(this FSharpOption<T>? option)
        where T : class =>
        option is null ? null : option.Value;

    // ---------- Option: C# -> F# ----------

    /// <summary>
    /// Wraps a nullable string as an F# <c>string option</c>, mapping <c>null</c> and
    /// whitespace-only text to <c>None</c>.
    /// </summary>
    /// <param name="value">Text entered by the user, possibly <c>null</c> or blank.</param>
    /// <returns><c>Some trimmed-text</c>, or <c>None</c> for blank input.</returns>
    /// <remarks>
    /// <para>
    /// Blank-to-<c>None</c> is deliberate: WPF <c>TextBox.Text</c> is never <c>null</c>, it is
    /// the empty string, so a naive conversion would persist <c>Some ""</c> for every field the
    /// user left untouched.
    /// </para>
    /// <para>
    /// The return type is annotated nullable because <c>FSharpOption&lt;T&gt;.None</c> <i>is</i> a
    /// null reference. Annotating it non-nullable would be a lie that silences exactly the warnings
    /// that catch <c>None</c>-handling mistakes.
    /// </para>
    /// </remarks>
    internal static FSharpOption<string>? ToOption(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? FSharpOption<string>.None
            : FSharpOption<string>.Some(value.Trim());

    /// <summary>Wraps a nullable double as an F# <c>float option</c>.</summary>
    /// <param name="value">Value to wrap, or <c>null</c> for <c>None</c>.</param>
    /// <returns><c>Some value</c>, or <c>None</c> when the input is <c>null</c>.</returns>
    /// <remarks>Nullable-annotated for the same reason as <see cref="ToOption(string?)"/>.</remarks>
    internal static FSharpOption<double>? ToOption(double? value) =>
        value.HasValue ? FSharpOption<double>.Some(value.Value) : FSharpOption<double>.None;

    // ---------- Result ----------

    /// <summary>
    /// Try-pattern projection of an F# <c>Result&lt;'T, 'TError&gt;</c>.
    /// </summary>
    /// <typeparam name="T">Success payload type.</typeparam>
    /// <typeparam name="TError">Failure payload type.</typeparam>
    /// <param name="result">Result returned by an F# API.</param>
    /// <param name="value">Receives the success payload, or <c>default</c> on failure.</param>
    /// <param name="error">Receives the failure payload, or <c>default</c> on success.</param>
    /// <returns><c>true</c> when the result is <c>Ok</c>.</returns>
    /// <remarks>
    /// Reading <c>ResultValue</c> on an <c>Error</c> (or <c>ErrorValue</c> on an <c>Ok</c>) throws,
    /// so callers must branch on the return value before touching the outputs. This helper makes
    /// that impossible to get wrong.
    /// </remarks>
    internal static bool TryUnwrap<T, TError>(
        this FSharpResult<T, TError> result,
        out T value,
        out TError error)
    {
        if (result.IsOk)
        {
            value = result.ResultValue;
            error = default!;
            return true;
        }

        value = default!;
        error = result.ErrorValue;
        return false;
    }

    // ---------- List ----------

    /// <summary>Copies an F# immutable list into a C# list the WPF layer can index and sort.</summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="list">F# list produced by the domain; <c>null</c> is treated as empty.</param>
    /// <returns>A materialised, independently owned list.</returns>
    /// <remarks>
    /// <c>FSharpList</c> implements <see cref="IEnumerable{T}"/> so WPF <i>can</i> bind to it
    /// directly, but it is a singly linked list: indexed access is O(n) and it raises no change
    /// notifications. Always project it into a C# collection before binding.
    /// </remarks>
    internal static IReadOnlyList<T> ToReadOnlyList<T>(this FSharpList<T>? list) =>
        list is null ? Array.Empty<T>() : list.ToList();

    /// <summary>Builds an F# immutable list from any C# sequence.</summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="source">Sequence to convert; <c>null</c> yields the empty list.</param>
    /// <returns>An F# list suitable for passing back into the domain API.</returns>
    internal static FSharpList<T> ToFSharpList<T>(this IEnumerable<T>? source) =>
        source is null ? FSharpList<T>.Empty : ListModule.OfSeq(source);
}
