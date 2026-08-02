using System.Text.RegularExpressions;

namespace MaterialLibraryCrudApp.Services;

/// <summary>Recognizes schema statements for which a deterministic inverse exists.</summary>
public static class SchemaUndo
{
    /// <summary>Derives the statement that undoes a schema change.</summary>
    /// <param name="sql">Schema statement that was executed.</param>
    /// <param name="inverse">Receives the undo statement, or an empty string when none exists.</param>
    /// <returns><c>true</c> when the statement has a deterministic inverse.</returns>
    /// <remarks>
    /// Only three forms are recognised - <c>CREATE TABLE</c>, <c>CREATE INDEX</c>, and
    /// <c>ALTER TABLE ... RENAME TO</c> - because those are the only ones whose undo is unambiguous
    /// and lossless. Anything else (a drop, a column addition, arbitrary DDL) returns <c>false</c>
    /// rather than guessing, so undo is never offered for an operation it cannot actually reverse.
    /// </remarks>
    public static bool TryGetInverse(string sql, out string inverse)
    {
        inverse = string.Empty;
        var normalized = sql.Trim().TrimEnd(';').Trim();
        var table = Regex.Match(normalized, "^CREATE\\s+TABLE\\s+(?:IF\\s+NOT\\s+EXISTS\\s+)?(?<name>[^\\s(]+)", RegexOptions.IgnoreCase);
        if (table.Success) { inverse = $"DROP TABLE {table.Groups["name"].Value}"; return true; }
        var index = Regex.Match(normalized, "^CREATE\\s+(?:UNIQUE\\s+)?INDEX\\s+(?:IF\\s+NOT\\s+EXISTS\\s+)?(?<name>[^\\s(]+)", RegexOptions.IgnoreCase);
        if (index.Success) { inverse = $"DROP INDEX {index.Groups["name"].Value}"; return true; }
        var rename = Regex.Match(normalized, "^ALTER\\s+TABLE\\s+(?<old>[^\\s]+)\\s+RENAME\\s+TO\\s+(?<new>[^\\s]+)$", RegexOptions.IgnoreCase);
        if (rename.Success) { inverse = $"ALTER TABLE {rename.Groups["new"].Value} RENAME TO {rename.Groups["old"].Value}"; return true; }
        return false;
    }
}
