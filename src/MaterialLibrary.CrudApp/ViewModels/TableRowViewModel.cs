using System.Globalization;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>One editable row of a material table, held as text cells.</summary>
/// <remarks>
/// <para>
/// Cells are strings rather than <c>double?</c> because a partially typed value (<c>"-"</c>,
/// <c>"1.2e"</c>, <c>""</c>) is not a valid number, and binding a grid cell straight to a numeric
/// property would either swallow the keystroke or throw. Parsing happens once, on commit.
/// </para>
/// <para>
/// The indexer is what the grid binds to (<c>{Binding [0]}</c>), because the column count is
/// decided at runtime by the <c>MaterialTableSpec</c> and cannot be expressed as fixed properties.
/// </para>
/// </remarks>
public sealed class TableRowViewModel : ObservableObject
{
    private readonly string[] _cells;

    /// <summary>Creates a row from cell text.</summary>
    /// <param name="values">Cell values; <c>null</c> renders as an empty cell.</param>
    public TableRowViewModel(IReadOnlyList<string?> values)
    {
        _cells = values.Select(value => value ?? string.Empty).ToArray();
    }

    /// <summary>Creates an empty row with the given number of cells.</summary>
    /// <param name="columnCount">Number of cells to allocate.</param>
    public TableRowViewModel(int columnCount)
    {
        _cells = Enumerable.Repeat(string.Empty, columnCount).ToArray();
    }

    /// <summary>Gets or sets the text of one cell.</summary>
    /// <param name="index">Zero-based column index.</param>
    /// <returns>The cell text; empty when the cell has no value.</returns>
    public string this[int index]
    {
        get => index >= 0 && index < _cells.Length ? _cells[index] : string.Empty;
        set
        {
            if (index < 0 || index >= _cells.Length || _cells[index] == value)
            {
                return;
            }

            _cells[index] = value ?? string.Empty;

            // "Item[]" is the name WPF listens for when a binding targets an indexer.
            RaisePropertyChanged("Item[]");
        }
    }

    /// <summary>Number of cells in the row.</summary>
    public int CellCount => _cells.Length;

    /// <summary>Whether every cell in the row is blank.</summary>
    /// <returns><c>true</c> when the row carries no data and can be skipped on commit.</returns>
    public bool IsBlank() => _cells.All(string.IsNullOrWhiteSpace);

    /// <summary>
    /// Parses the row into values, enforcing which columns may be blank.
    /// </summary>
    /// <param name="columns">Column definitions supplying the optionality rules and header names.</param>
    /// <param name="values">Receives the validated cells; <c>null</c> entries mark blank optional cells.</param>
    /// <param name="error">Receives a user-facing message naming the offending column, or <c>null</c>.</param>
    /// <returns><c>true</c> when every cell parsed successfully.</returns>
    public bool TryParse(
        IReadOnlyList<Interop.MaterialTableColumn> columns,
        out string?[] values,
        out string? error)
    {
        values = new string?[columns.Count];
        error = null;

        for (var i = 0; i < columns.Count; i++)
        {
            var text = this[i];
            var column = columns[i];

            if (string.IsNullOrWhiteSpace(text))
            {
                if (!column.IsOptional)
                {
                    error = $"'{column.DisplayHeader}' is required.";
                    return false;
                }

                // Blank optional cell maps to the F# None.
                values[i] = null;
                continue;
            }

            // Text columns pass through unparsed; numeric columns must be valid before a spec
            // is allowed to read them, because the spec's parse helpers assume success.
            if (!column.IsText
                && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                error = $"'{column.DisplayHeader}' must be numeric (got '{text}').";
                return false;
            }

            values[i] = column.IsText ? text : text.Trim();
        }

        return true;
    }

}
