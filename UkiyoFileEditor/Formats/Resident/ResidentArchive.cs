using System.Text;
using System.Text.Json;

namespace UkiyoFileEditor.Formats.Resident;

/// <summary>
/// The "resident" archive (game_resident_JP_m.pak) holds the menu, shop, item, quest and glossary text as packs inside packs:
///   game_resident_JP_m.pak  =  STPZ + $CMP + STPK   (1 entry: game_font_resident_JP_m.pak)
///   game_font_resident_JP_m.pak  =  STPK            (81 entries, each one a message pack such as E_ShopKaziya.pak)
///   E_ShopKaziya.pak ...    =  STPK                 (entries are UTF-16LE text, named; there are no separate ID entries)
/// <see cref="Extract"/> writes the 81 packs as ordinary files into a work folder, so the normal message editor can work on them.
/// <see cref="Build"/> puts the (translated) packs back and rewrites both outer layers.
/// </summary>
public static class ResidentArchive
{
    public const string FileName = "game_resident_JP_m.pak";

    private const string ManifestFileName = "manifest.json";
    private const string BackupSuffix = ".old";
    private const int MinimumPacksInMiddleLayer = 2;

    /// <summary>Folder under %AppData% that holds the extracted packs, one sub folder named like the game file.</summary>
    public static string WorkRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SrdViewer", "system");

    public static string WorkFolder => Path.Combine(WorkRoot, FileName);

    private static string ManifestPath => Path.Combine(WorkFolder, ManifestFileName);

    /// <summary>Looks for the resident archive in a folder. Returns "" when it is not there.</summary>
    public static string Find(string folder, bool recursive)
    {
        try
        {
            string direct = Path.Combine(folder, FileName);
            if (File.Exists(direct)) return direct;

            var options = new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true };
            return Directory.EnumerateFiles(folder, FileName, options).FirstOrDefault() ?? "";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return "";
        }
    }

    /// <summary>
    /// Writes the packs of the archive into <see cref="WorkFolder"/>. Does nothing when that was already done for this very file
    /// (translated packs saved there are kept). Returns a short message for the status line.
    /// </summary>
    public static string Extract(string gamePath, bool force = false)
    {
        string originalPath = PackBackupWriter.OriginalOf(gamePath);
        var originalInfo = new FileInfo(originalPath);

        ResidentManifest? existing = ReadManifest();
        if (!force && IsAlreadyExtracted(existing, originalInfo, gamePath))
            return $"{existing!.Names.Length} packs ready (extracted earlier).";

        StpkLayer outer = StpkLayer.Parse(File.ReadAllBytes(originalPath));
        int middleIndex = FindMiddleLayer(outer, out StpkLayer middle);

        Directory.CreateDirectory(WorkFolder);
        if (existing != null) DeleteExtractedFiles(existing);

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packNames = new string[middle.EntryCount];
        for (int pack = 0; pack < middle.EntryCount; pack++)
        {
            packNames[pack] = UniquePackFileName(middle.NameOf(pack), usedNames);
            File.WriteAllBytes(Path.Combine(WorkFolder, packNames[pack]), middle.Bodies[pack]);
        }

        var manifest = new ResidentManifest
        {
            Source = gamePath,
            Length = originalInfo.Length,
            Ticks = originalInfo.LastWriteTimeUtc.Ticks,
            Names = packNames,
            MiddleIndex = middleIndex,
        };
        File.WriteAllText(ManifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        return $"{packNames.Length} packs extracted from {Path.GetFileName(originalPath)}.";
    }

    /// <summary>
    /// Builds the game file from the original and the packs in the work folder, checks it by reading it back, and writes it
    /// over <paramref name="gamePath"/> (the original is kept as ".old"). Returns how many packs differ from the original.
    /// </summary>
    public static int Build(string gamePath, out string message)
    {
        ResidentManifest manifest = ReadManifest() ?? throw new InvalidDataException("The packs were not extracted yet.");
        string originalPath = PackBackupWriter.OriginalOf(gamePath);
        StpkLayer outer = StpkLayer.Parse(File.ReadAllBytes(originalPath));
        int middleIndex = FindMiddleLayer(outer, out StpkLayer middle);
        if (middleIndex != manifest.MiddleIndex || middle.EntryCount != manifest.Names.Length)
            throw new InvalidDataException("The game file no longer matches the extracted packs. Use \"Extract again\".");

        int changedPackCount = 0;
        for (int pack = 0; pack < manifest.Names.Length; pack++)
        {
            string packPath = Path.Combine(WorkFolder, manifest.Names[pack]);
            if (!File.Exists(packPath)) throw new FileNotFoundException("Missing extracted pack " + manifest.Names[pack], packPath);

            byte[] packBytes = File.ReadAllBytes(packPath);
            if (packBytes.AsSpan().SequenceEqual(middle.Bodies[pack])) continue;

            StpkLayer.Parse(packBytes); // must still be a readable pack
            _ = MessagePack.Load(packBytes);
            middle.Bodies[pack] = packBytes;
            changedPackCount++;
        }

        byte[] middleBytes = middle.Build();
        outer.Bodies[middleIndex] = middleBytes;
        byte[] result = outer.Build();

        VerifyReadBack(result, outer, middle, middleIndex, middleBytes, manifest);

        PackBackupWriter.Write(gamePath, result);
        message = $"Wrote {Path.GetFileName(gamePath)} with {changedPackCount} changed pack(s) ({result.Length:N0} bytes); the original is kept as {BackupSuffix}.";
        return changedPackCount;
    }

    /// <summary>Puts the original game file back (the .old replaces the built file). Returns false when there is no .old.</summary>
    public static bool Restore(string gamePath) => PackBackupWriter.Restore(gamePath);

    /// <summary>Reads the result back: every layer parses and every pack is what was put in.</summary>
    private static void VerifyReadBack(byte[] result, StpkLayer outer, StpkLayer middle, int middleIndex, byte[] middleBytes, ResidentManifest manifest)
    {
        StpkLayer checkedOuter = StpkLayer.Parse(result);
        if (checkedOuter.EntryCount != outer.EntryCount || !checkedOuter.Bodies[middleIndex].AsSpan().SequenceEqual(middleBytes))
            throw new InvalidDataException("The rebuilt archive does not read back correctly; nothing was written.");

        StpkLayer checkedMiddle = StpkLayer.Parse(checkedOuter.Bodies[middleIndex]);
        if (checkedMiddle.EntryCount != middle.EntryCount)
            throw new InvalidDataException("The rebuilt archive has a different number of packs; nothing was written.");

        for (int pack = 0; pack < middle.EntryCount; pack++)
        {
            if (!checkedMiddle.Bodies[pack].AsSpan().SequenceEqual(middle.Bodies[pack]))
                throw new InvalidDataException($"Pack {manifest.Names[pack]} does not read back correctly; nothing was written.");
        }
    }

    private static bool IsAlreadyExtracted(ResidentManifest? manifest, FileInfo originalInfo, string gamePath) =>
        manifest != null
        && manifest.Length == originalInfo.Length
        && manifest.Ticks == originalInfo.LastWriteTimeUtc.Ticks
        && string.Equals(manifest.Source, gamePath, StringComparison.OrdinalIgnoreCase)
        && manifest.Names.All(name => File.Exists(Path.Combine(WorkFolder, name)));

    private static void DeleteExtractedFiles(ResidentManifest manifest)
    {
        foreach (string name in manifest.Names)
        {
            foreach (string suffix in new[] { "", BackupSuffix })
            {
                try { File.Delete(Path.Combine(WorkFolder, name + suffix)); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>The entry of the outer archive that itself holds the packs (game_font_resident_JP_m.pak).</summary>
    private static int FindMiddleLayer(StpkLayer outer, out StpkLayer middle)
    {
        for (int entry = 0; entry < outer.Bodies.Length; entry++)
        {
            try
            {
                StpkLayer candidate = StpkLayer.Parse(outer.Bodies[entry]);
                if (candidate.EntryCount >= MinimumPacksInMiddleLayer)
                {
                    middle = candidate;
                    return entry;
                }
            }
            catch (Exception exception) when (exception is InvalidDataException or OverflowException) { }
        }
        throw new InvalidDataException("The archive does not contain a pack of packs.");
    }

    private static string UniquePackFileName(string entryName, HashSet<string> usedNames)
    {
        var builder = new StringBuilder();
        foreach (char character in entryName.Trim())
            builder.Append(Path.GetInvalidFileNameChars().Contains(character) ? '_' : character);

        string baseName = builder.Length == 0 ? "pack" : builder.ToString();
        if (!baseName.EndsWith(".pak", StringComparison.OrdinalIgnoreCase)) baseName += ".pak";

        string candidate = baseName;
        for (int suffix = 2; !usedNames.Add(candidate); suffix++)
            candidate = Path.GetFileNameWithoutExtension(baseName) + "_" + suffix + ".pak";
        return candidate;
    }

    private static ResidentManifest? ReadManifest()
    {
        try
        {
            if (File.Exists(ManifestPath))
                return JsonSerializer.Deserialize<ResidentManifest>(File.ReadAllText(ManifestPath));
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException) { }
        return null;
    }
}
