using System.Text.Json;

namespace UkiyoFileEditor.Translation;

/// <summary>
/// Remembers the translations between sessions in %AppData%\SrdViewer\translations.json, per pack file (relative path) and
/// message ID (or entry name when a pack has no IDs). Texts are stored with plain "\n" line breaks. If one pack repeats an ID,
/// the last translation stored for it wins.
/// </summary>
public sealed class TranslationStore
{
    private readonly Dictionary<string, Dictionary<string, TranslationItem>> _files;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SrdViewer", "translations.json");

    private TranslationStore(Dictionary<string, Dictionary<string, TranslationItem>> files) => _files = files;

    private static TranslationStore? _shared;

    /// <summary>The one store of the program: the Messages tab and the System Text tab use the same instance.</summary>
    public static TranslationStore Load() => _shared ??= ReadFromDisk();

    private static TranslationStore ReadFromDisk()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, TranslationItem>>>(File.ReadAllText(FilePath));
                if (data != null) return new TranslationStore(data);
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException) { }
        return new TranslationStore(new());
    }

    public static string KeyOf(string id, string key) => id.Length > 0 ? id : key;
    public static string KeyOf(MessageRow row) => KeyOf(row.Id, row.Key);

    public TranslationItem? Find(string file, string key) =>
        _files.TryGetValue(file, out var items) && items.TryGetValue(key, out TranslationItem? item) ? item : null;

    /// <summary>The translation of a row, or null when there is none or the original text has changed since.</summary>
    public string? Get(string file, MessageRow row)
    {
        TranslationItem? item = Find(file, KeyOf(row));
        return item != null && item.Source == row.Text ? item.Text : null;
    }

    /// <summary>Stores a translation (empty text removes it). Returns the change, or null when nothing changed.</summary>
    public TranslationChange? Set(string file, string key, string source, string text)
    {
        text = TextTools.Norm(text);
        if (text.Length == 0) return Remove(file, key);

        TranslationItem? before = Find(file, key);
        var after = new TranslationItem(source, text);
        if (before != null && before.Source == after.Source && before.Text == after.Text) return null;

        if (!_files.TryGetValue(file, out var items)) _files[file] = items = new();
        items[key] = after;
        return new TranslationChange(file, key, before, after);
    }

    public TranslationChange? Remove(string file, string key)
    {
        TranslationItem? before = Find(file, key);
        if (before == null) return null;
        _files[file].Remove(key);
        if (_files[file].Count == 0) _files.Remove(file);
        return new TranslationChange(file, key, before, null);
    }

    /// <summary>Puts the store into the state before (undo) or after (redo) a change.</summary>
    public void Apply(TranslationChange change, bool undo)
    {
        TranslationItem? target = undo ? change.Before : change.After;
        if (target == null)
        {
            if (_files.TryGetValue(change.File, out var items))
            {
                items.Remove(change.Key);
                if (items.Count == 0) _files.Remove(change.File);
            }
            return;
        }
        if (!_files.TryGetValue(change.File, out var list)) _files[change.File] = list = new();
        list[change.Key] = target;
    }

    /// <summary>The file keys (relative pack paths) that have stored translations.</summary>
    public List<string> FileKeys() => _files.Keys.ToList();

    /// <summary>Files the translations of one pack under another path (used when the folder that was opened has changed).</summary>
    public void RenameFile(string oldKey, string newKey)
    {
        if (oldKey == newKey || !_files.TryGetValue(oldKey, out var items)) return;
        _files.Remove(oldKey);
        if (_files.TryGetValue(newKey, out var existing))
        {
            foreach (var (key, item) in items) existing.TryAdd(key, item);
        }
        else
        {
            _files[newKey] = items;
        }
    }

    public bool HasAny(string file) => _files.TryGetValue(file, out var items) && items.Count > 0;

    public int CountIn(string file) => _files.TryGetValue(file, out var items) ? items.Count : 0;

    public int Count => _files.Values.Sum(items => items.Count);

    /// <summary>Every stored item (a snapshot, so the store may be changed while the list is used).</summary>
    public List<(string File, string Key, TranslationItem Item)> AllItems()
    {
        var list = new List<(string, string, TranslationItem)>();
        foreach (var (file, items) in _files)
            foreach (var (key, item) in items)
                list.Add((file, key, item));
        return list;
    }

    /// <summary>Original text to translation, for filling identical lines. The first translation seen for a text wins.</summary>
    public Dictionary<string, string> Memory()
    {
        var memory = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var items in _files.Values)
            foreach (TranslationItem item in items.Values)
                memory.TryAdd(item.Source, item.Text);
        return memory;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_files, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }));
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
