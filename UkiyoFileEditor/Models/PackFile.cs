namespace UkiyoFileEditor.Models;

/// <summary>One pack file in the list on the left.</summary>
public sealed class PackFile
{
    public string FullPath = "";

    /// <summary>Path relative to the opened folder; also the key of its translations.</summary>
    public string Display = "";

    /// <summary>Number of stored translations (shown in the list).</summary>
    public int TranslatedCount;

    /// <summary>True when "name.pak.old" exists, i.e. a translated pack was saved over this file.</summary>
    public bool HasBackup;

    /// <summary>The untouched original: "name.pak.old" when a translated pack was saved before, otherwise the file itself.</summary>
    public string OriginalPath => PackBackupWriter.OriginalOf(FullPath);

    public override string ToString() =>
        Display + (TranslatedCount > 0 ? $"   [{TranslatedCount}]" : "") + (HasBackup ? "  ✔saved" : "");
}
