using System.Drawing.Imaging;

namespace UkiyoFileEditor.Services;

/// <summary>
/// Writes the textures of Flash packs as PNG files. Layout: packFolder / srd name without extension / texture name + ".png"
/// (duplicate names get _2, _3 ...). <see cref="FlashPackImporter"/> reads the same layout back.
/// </summary>
public static class FlashPackExporter
{
    private const string PngExtension = ".png";

    public static string SafeFileName(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        return name;
    }

    /// <summary>Folder name of every pack, in list order: the _m pack name without extension, made unique with _2, _3 ...</summary>
    public static List<string> UniqueFolderNames(IEnumerable<FlashPackPair> pairs)
    {
        var names = new List<string>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (FlashPackPair pair in pairs)
        {
            string baseName = SafeFileName(Path.GetFileNameWithoutExtension(pair.MetadataPath));
            string unique = baseName;
            for (int suffix = 2; !usedNames.Add(unique); suffix++) unique = baseName + "_" + suffix;
            names.Add(unique);
        }
        return names;
    }

    /// <summary>Folder of one .srd inside a pack folder.</summary>
    public static string SrdFolder(string packFolder, string srdName) =>
        Path.Combine(packFolder, SafeFileName(Path.GetFileNameWithoutExtension(srdName)));

    /// <summary>PNG file name of a texture (before duplicates are numbered).</summary>
    public static string PngFileName(string textureName)
    {
        string fileName = SafeFileName(textureName);
        return fileName.EndsWith(PngExtension, StringComparison.OrdinalIgnoreCase) ? fileName : fileName + PngExtension;
    }

    /// <summary>First path for <paramref name="fileName"/> in <paramref name="folder"/> that <paramref name="isTaken"/> does not reject.</summary>
    public static string NumberedPath(string folder, string fileName, Func<string, bool> isTaken)
    {
        string path = Path.Combine(folder, fileName);
        for (int suffix = 2; isTaken(path); suffix++)
            path = Path.Combine(folder, Path.GetFileNameWithoutExtension(fileName) + "_" + suffix + PngExtension);
        return path;
    }

    /// <summary>Writes one texture to a new PNG file in <paramref name="folder"/> (an existing file is never overwritten).</summary>
    public static void SavePng(byte[] bgra, FlashTexture texture, string folder)
    {
        Directory.CreateDirectory(folder);
        string path = NumberedPath(folder, PngFileName(texture.Name), File.Exists);
        using Bitmap bitmap = BitmapConverter.ToBitmap(bgra, texture.Width, texture.Height);
        bitmap.Save(path, ImageFormat.Png);
    }

    /// <summary>Reads one pack pair from disk and writes its textures under <paramref name="packFolder"/>. Safe to call off the UI thread.</summary>
    public static (int Saved, int Skipped) ExportPack(FlashPackPair pair, string packFolder)
    {
        FlashPack pack = FlashPack.Load(pair.MetadataPath, pair.PixelDataPath);

        int saved = 0, skipped = 0;
        foreach (FlashPackEntry entry in pack.Entries)
        {
            foreach (FlashTexture texture in entry.Textures)
            {
                byte[]? bgra = pack.TryDecode(entry, texture);
                if (bgra == null) { skipped++; continue; }

                SavePng(bgra, texture, SrdFolder(packFolder, entry.Name));
                saved++;
            }
        }
        return (saved, skipped);
    }
}
