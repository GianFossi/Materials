using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using MaterialLibrary.Crud;
using MaterialLibrary.Domain;
using MaterialLibraryCrudApp.Interop;
using MaterialLibraryCrudApp.Services;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace MaterialLibraryCrudApp.ViewModels;


/// <summary>One rendered series of the plot.</summary>
/// <param name="name">Series name, taken from the source column.</param>
/// <param name="points">Points in canvas coordinates.</param>
/// <param name="index">Series index, used to pick a distinct stroke colour.</param>
public sealed class PlotSeriesViewModel(string name, PointCollection points, int index)
{
    /// <summary>Object name as stored in the database.</summary>
    public string Name { get; } = name;
    /// <summary>Points in canvas coordinates.</summary>
    public PointCollection Points { get; } = points;
    /// <summary>Stroke used to draw this series; colour is picked from a fixed palette by series index.</summary>
    public System.Windows.Media.Brush Stroke { get; } = new System.Windows.Media.SolidColorBrush(new[] { Colors.DodgerBlue, Colors.IndianRed, Colors.ForestGreen, Colors.DarkOrange, Colors.Purple }[index % 5]);
}

/// <summary>Read-only projection of a SQLite table or view.</summary>
internal sealed record SqlExecutionResult(DataTable? Rows, int Changes);
internal sealed record RawRowChange(DataRowState State, Dictionary<string, object?> Current, Dictionary<string, object?> Original, long? RowId);
internal sealed record RawCommitResult(int Changed, IReadOnlyList<RawRowChange> Changes);

/// <summary>One table or view listed in the raw-table tab.</summary>
/// <param name="name">Object name as stored in the database.</param>
/// <param name="kind">Either <c>table</c> or <c>view</c>.</param>
public sealed class DatabaseTableViewModel(string name, string kind)
{
    /// <summary>Object name as stored in the database.</summary>
    public string Name { get; } = name;

    /// <summary>Either <c>table</c> or <c>view</c>.</summary>
    public string Kind { get; } = kind;
    /// <summary>Whether the object rejects edits; true for views.</summary>
    public bool IsReadOnly => Kind != "table";
    /// <summary>Name plus kind, as shown in the picker.</summary>
    public string DisplayName => $"{Name} ({Kind})";
}

/// <summary>One column of the selected table, shown in the schema pane.</summary>
/// <param name="Name">Column name.</param>
/// <param name="DataType">Declared SQLite type.</param>
/// <param name="IsNullable">Whether the column accepts NULL.</param>
/// <param name="IsPrimaryKey">Whether the column participates in the primary key.</param>
/// <param name="DefaultValue">Declared default, or an empty string.</param>
public sealed record DatabaseColumnViewModel(string Name, string DataType, bool IsNullable, bool IsPrimaryKey, string DefaultValue)
{
    /// <summary>Tick shown in the primary-key column of the schema grid.</summary>
    public string PrimaryKeyDisplay => IsPrimaryKey ? "Yes" : "";
    /// <summary>Tick shown in the nullable column of the schema grid.</summary>
    public string NullableDisplay => IsNullable ? "Yes" : "No";
}

/// <summary>One foreign key declared on the selected table.</summary>
/// <param name="ReferencedTable">Table the key points at.</param>
/// <param name="FromColumn">Column on this table.</param>
/// <param name="ToColumn">Column on the referenced table.</param>
public sealed record DatabaseForeignKeyViewModel(string ReferencedTable, string FromColumn, string ToColumn);

/// <summary>Read-only projection of one database material summary for the grid.</summary>
public sealed class DatabaseRowViewModel
{
    /// <summary>Creates a row projection.</summary>
    /// <param name="summary">Summary returned by the database layer.</param>
    public DatabaseRowViewModel(DatabaseMaterialSummary summary)
    {
        DatabaseId = summary.DatabaseId;
        MaterialKey = summary.MaterialKey;
        Name = summary.Name;
        Specification = summary.Specification;
        Grade = summary.Grade;
        ClassConditionTemper = summary.ClassConditionTemper;
        Uns = summary.Uns;
        HasDocument = summary.HasDocument;
    }

    /// <summary>Integer primary key in the ASME <c>Materials</c> table.</summary>
    public long DatabaseId { get; }

    /// <summary>Domain material identifier, or the integer key for reference-only rows.</summary>
    public string MaterialKey { get; }

    /// <summary>Composed material name, when the extension row supplies one.</summary>
    public string Name { get; }

    /// <summary>Specification text.</summary>
    public string Specification { get; }

    /// <summary>Grade text.</summary>
    public string Grade { get; }

    /// <summary>Class, condition, or tempering designation.</summary>
    public string ClassConditionTemper { get; }

    /// <summary>UNS alloy identifier.</summary>
    public string Uns { get; }

    /// <summary>Whether this material was written by the application and can be read back in full.</summary>
    public bool HasDocument { get; }

    /// <summary>Human-readable origin of the row.</summary>
    /// <remarks>
    /// Reference rows come from the shipped ASME data and carry only the columns that schema has;
    /// application rows have a stored document and round-trip losslessly.
    /// </remarks>
    public string Origin => HasDocument ? "Application" : "ASME reference";

    /// <summary>Reports whether one search term appears anywhere in this row's identity.</summary>
    /// <param name="term">Term to look for; matched case-insensitively as a substring.</param>
    /// <returns><c>true</c> when any identity field contains the term.</returns>
    /// <remarks>
    /// Covers the database ID, the material key, specification, grade, class/condition/tempering,
    /// UNS, and the composed full name, so a user can find a row by whichever identifier they happen
    /// to have to hand.
    /// </remarks>
    public bool Matches(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return true;
        }

        return Contains(DatabaseId.ToString(System.Globalization.CultureInfo.InvariantCulture), term)
            || Contains(MaterialKey, term)
            || Contains(Specification, term)
            || Contains(Grade, term)
            || Contains(ClassConditionTemper, term)
            || Contains(Uns, term)
            || Contains(Name, term);
    }

    /// <summary>Case-insensitive substring test tolerant of null fields.</summary>
    /// <param name="value">Field being searched.</param>
    /// <param name="term">Term to look for.</param>
    /// <returns><c>true</c> when the field contains the term.</returns>
    private static bool Contains(string? value, string term) =>
        !string.IsNullOrEmpty(value) && value.Contains(term, StringComparison.OrdinalIgnoreCase);
}
