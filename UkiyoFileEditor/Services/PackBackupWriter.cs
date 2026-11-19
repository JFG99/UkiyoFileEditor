namespace UkiyoFileEditor.Services;

/// <summary>Writes a pack file to disk while keeping the untouched original next to it.</summary>
public static class PackBackupWriter
{
    private const string BackupSuffix = ".old";
    private const string TemporarySuffix = ".tmp";

    /// <summary>The untouched original: "name.pak.old" when a translated pack was saved over it before, otherwise the file itself.</summary>
    public static string OriginalOf(string path) => File.Exists(path + BackupSuffix) ? path + BackupSuffix : path;

    /// <summary>Writes the pack under its original name and keeps the original as "name.pak.old" (an existing .old is never overwritten).</summary>
    public static void Write(string path, byte[] bytes)
    {
        string temporaryPath = path + TemporarySuffix;
        File.WriteAllBytes(temporaryPath, bytes);
        try
        {
            if (!File.Exists(path + BackupSuffix)) File.Move(path, path + BackupSuffix);
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            throw;
        }
    }

    /// <summary>Puts the original back (the .old replaces the written file). Returns false when there is no .old.</summary>
    public static bool Restore(string path)
    {
        if (!File.Exists(path + BackupSuffix)) return false;
        File.Move(path + BackupSuffix, path, overwrite: true);
        return true;
    }
}
