using System.Text.Json;
using System.IO;

namespace MaterialLibraryCrudApp.Services;

/// <summary>Durable journal entry for one committed raw-table transaction.</summary>
public sealed record TransactionJournalEntry(
    DateTimeOffset Timestamp,
    string DatabasePath,
    string TableName,
    IReadOnlyList<TransactionRowChange> Changes);

/// <summary>Serializable before/after values for one row operation.</summary>
public sealed record TransactionRowChange(
    string State,
    IReadOnlyDictionary<string, JsonElement> Before,
    IReadOnlyDictionary<string, JsonElement> After,
    long? RowId);

/// <summary>Small, failure-tolerant JSON journal stored per user profile.</summary>
public sealed class TransactionJournal
{
    private readonly string _path;
    private readonly string _redoPath;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public TransactionJournal(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MaterialLibrary", "crud-transactions.json");
        _redoPath = Path.Combine(Path.GetDirectoryName(_path)!, "crud-redo.json");
    }

    public IReadOnlyList<TransactionJournalEntry> Load()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            return JsonSerializer.Deserialize<List<TransactionJournalEntry>>(File.ReadAllText(_path), _options) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void Append(TransactionJournalEntry entry, int maximumEntries = 100)
    {
        try
        {
            var entries = Load().ToList();
            entries.Add(entry);
            if (entries.Count > maximumEntries)
                entries = entries.Skip(entries.Count - maximumEntries).ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(entries, _options));
        }
        catch
        {
            // Journaling must never turn a successful database commit into a failed operation.
        }
    }

    public void Remove(TransactionJournalEntry entry)
    {
        try
        {
            var entries = Load().Where(item => item.Timestamp != entry.Timestamp || !string.Equals(item.DatabasePath, entry.DatabasePath, StringComparison.OrdinalIgnoreCase) || !string.Equals(item.TableName, entry.TableName, StringComparison.Ordinal)).ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(entries, _options));
        }
        catch { }
    }

    public IReadOnlyList<TransactionJournalEntry> LoadRedo()
    {
        try
        {
            if (!File.Exists(_redoPath)) return [];
            return JsonSerializer.Deserialize<List<TransactionJournalEntry>>(File.ReadAllText(_redoPath), _options) ?? [];
        }
        catch { return []; }
    }

    public void PushRedo(TransactionJournalEntry entry)
    {
        try
        {
            var entries = LoadRedo().ToList();
            entries.Add(entry);
            Directory.CreateDirectory(Path.GetDirectoryName(_redoPath)!);
            File.WriteAllText(_redoPath, JsonSerializer.Serialize(entries.TakeLast(100), _options));
        }
        catch { }
    }

    public void ClearRedo()
    {
        try { if (File.Exists(_redoPath)) File.Delete(_redoPath); } catch { }
    }

    public void RemoveRedo(TransactionJournalEntry entry)
    {
        try
        {
            var entries = LoadRedo().Where(item => item.Timestamp != entry.Timestamp || !string.Equals(item.DatabasePath, entry.DatabasePath, StringComparison.OrdinalIgnoreCase) || !string.Equals(item.TableName, entry.TableName, StringComparison.Ordinal)).ToList();
            File.WriteAllText(_redoPath, JsonSerializer.Serialize(entries, _options));
        }
        catch { }
    }
}
