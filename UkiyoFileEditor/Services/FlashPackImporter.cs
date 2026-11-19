namespace UkiyoFileEditor.Services;

/// <summary>Reads edited PNGs (laid out by <see cref="FlashPackExporter"/>) back into the _v pack of a Flash pack pair.</summary>
public static class FlashPackImporter
{
    private const string BackupSuffix = ".old";

    /// <summary>
    /// Compares the PNGs under <paramref name="packFolder"/> with the textures of one pack pair. With <paramref name="write"/>, every
    /// PNG that differs is re-encoded into the _v pack and the pack is saved (original kept as .old). Safe to call off the UI thread.
    /// File names follow <see cref="FlashPackExporter.ExportPack"/> exactly.
    /// </summary>
    public static PackImportResult ImportPack(FlashPackPair pair, string packFolder, bool write)
    {
        var result = new PackImportResult();
        if (!Directory.Exists(packFolder))
        {
            result.NoFolder = true;
            return result;
        }

        FlashPack pack = FlashPack.Load(pair.MetadataPath, pair.PixelDataPath);
        bool anyTextureWritten = false;

        foreach (FlashPackEntry entry in pack.Entries)
        {
            string srdFolder = FlashPackExporter.SrdFolder(packFolder, entry.Name);
            bool folderExists = Directory.Exists(srdFolder);
            var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (FlashTexture texture in entry.Textures)
            {
                // Same test the exporter uses to decide whether a texture was written at all.
                if (!pack.CanDecode(entry, texture))
                {
                    result.Unsupported++;
                    continue;
                }

                string pngPath = FlashPackExporter.NumberedPath(srdFolder, FlashPackExporter.PngFileName(texture.Name), usedPaths.Contains);
                usedPaths.Add(pngPath);

                if (!folderExists || !File.Exists(pngPath))
                {
                    result.Missing++;
                    continue;
                }

                string label = Path.GetRelativePath(packFolder, pngPath);
                bool wasChanged = CompareOrReplace(pack, entry, texture, pngPath, label, write, result);
                anyTextureWritten |= wasChanged && write;
            }
        }

        if (write && anyTextureWritten) SavePixelDataPack(pair, pack);
        return result;
    }

    /// <summary>Updates the counters of <paramref name="result"/> for one PNG; returns true when the PNG differs from the pack.</summary>
    private static bool CompareOrReplace(FlashPack pack, FlashPackEntry entry, FlashTexture texture, string pngPath, string label, bool write, PackImportResult result)
    {
        byte[] pngPixels;
        int pngWidth, pngHeight;
        try
        {
            pngPixels = BitmapConverter.LoadBgra(pngPath, out pngWidth, out pngHeight);
        }
        catch (Exception exception)
        {
            result.Unreadable++;
            result.Notes.Add($"{label}: cannot read ({exception.Message})");
            return false;
        }

        if (pngWidth != texture.Width || pngHeight != texture.Height)
        {
            result.WrongSize++;
            result.Notes.Add($"{label}: image is {pngWidth}×{pngHeight}, texture is {texture.Width}×{texture.Height}");
            return false;
        }

        byte[] currentPixels = pack.TryDecode(entry, texture)!;
        if (BitmapConverter.SamePicture(currentPixels, pngPixels))
        {
            result.Unchanged++;
            return false;
        }

        result.Changed++;
        if (write) pack.ReplacePixels(entry, texture, pngPixels, label);
        return true;
    }

    private static void SavePixelDataPack(FlashPackPair pair, FlashPack pack)
    {
        byte[] rebuilt = pack.PixelDataPack.Build();
        string backupPath = pair.PixelDataPath + BackupSuffix;
        if (!File.Exists(backupPath)) File.Copy(pair.PixelDataPath, backupPath);
        File.WriteAllBytes(pair.PixelDataPath, rebuilt);
    }
}
