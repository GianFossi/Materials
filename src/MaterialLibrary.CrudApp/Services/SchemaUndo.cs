using System.Text.RegularExpressions;

namespace MaterialLibraryCrudApp.Services;

/// <summary>Recognizes schema statements for which a deterministic inverse exists.</summary>
public static class SchemaUndo
{
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
