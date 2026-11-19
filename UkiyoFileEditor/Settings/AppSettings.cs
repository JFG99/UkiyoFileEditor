using System.Text.Json;

namespace UkiyoFileEditor.Settings;

/// <summary>Viewer settings, stored as JSON in %AppData%\SrdViewer\settings.json.</summary>
public sealed class AppSettings
{
    // Checker (F7) and wrap tool
    public int MaxLineChars { get; set; }
    public int MaxLines { get; set; }
    public bool WarnNonAscii { get; set; } = true;
    public string AllowedChars { get; set; } = "";
    public bool WarnJapanese { get; set; } = true;
    public bool SpaceAroundIcons { get; set; } = true;
    public string IconTagPattern { get; set; } = TextTools.DefaultIconPattern;
    public int WrapMax { get; set; } = 40;

    // Folders
    public List<string> RecentFolders { get; set; } = new();

    public bool AutoFormatSentences { get; set; } = true;
    public int AutoFormatExtraLines { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SrdViewer", "settings.json");

    private static AppSettings? _shared;

    /// <summary>One instance for the whole program (both text tabs share it).</summary>
    public static AppSettings Load() => _shared ??= ReadFromDisk();

    private static AppSettings ReadFromDisk()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException) { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
