namespace UkiyoFileEditor.Formats.Texture;

/// <summary>
/// A Flash UI pack pair: the "*_m.pak" (holding the .srd texture lists) and the "*_v.pak" (holding the matching .srdv pixels).
/// </summary>
public sealed class FlashPack
{
    private const string SrdExtension = ".srd";
    private const string PixelDataSuffix = "v";

    public FlashPack(StpkArchive metadataPack, StpkArchive pixelDataPack, List<FlashPackEntry> entries)
    {
        MetadataPack = metadataPack;
        PixelDataPack = pixelDataPack;
        Entries = entries;
    }

    public StpkArchive MetadataPack { get; }

    /// <summary>The _v pack; its bodies are edited in place when textures are replaced.</summary>
    public StpkArchive PixelDataPack { get; }

    public List<FlashPackEntry> Entries { get; }

    public static FlashPack Load(string metadataPath, string pixelDataPath)
    {
        StpkArchive metadataPack = StpkArchive.Load(File.ReadAllBytes(metadataPath));
        StpkArchive pixelDataPack = StpkArchive.Load(File.ReadAllBytes(pixelDataPath));

        var pixelDataIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < pixelDataPack.Count; index++)
            pixelDataIndexByName[pixelDataPack.NameOf(index)] = index;

        var entries = new List<FlashPackEntry>();
        for (int index = 0; index < metadataPack.Count; index++)
        {
            string name = metadataPack.NameOf(index);
            if (!name.EndsWith(SrdExtension, StringComparison.OrdinalIgnoreCase)) continue;

            var entry = new FlashPackEntry { Name = name, Textures = SrdTextureParser.Parse(metadataPack.Bodies[index]) };
            if (pixelDataIndexByName.TryGetValue(name + PixelDataSuffix, out int pixelDataIndex))
                entry.PixelDataIndex = pixelDataIndex;
            entries.Add(entry);
        }

        return new FlashPack(metadataPack, pixelDataPack, entries);
    }

    /// <summary>
    /// Decodes the pixels of a texture to BGRA. Returns null when the texture cannot be shown: no .srdv, an unsupported
    /// format, or a data range outside the .srdv.
    /// </summary>
    public byte[]? TryDecode(FlashPackEntry entry, FlashTexture texture)
    {
        if (!CanDecode(entry, texture)) return null;
        byte[] pixelData = PixelDataPack.Bodies[entry.PixelDataIndex];
        return DxtCodec.Decode(pixelData, texture.Offset, texture.Width, texture.Height, texture.IsDxt1);
    }

    /// <summary>The single test that decides whether a texture is exported / imported at all.</summary>
    public bool CanDecode(FlashPackEntry entry, FlashTexture texture)
    {
        if (!entry.HasPixelData || !texture.Supported) return false;
        byte[] pixelData = PixelDataPack.Bodies[entry.PixelDataIndex];
        return texture.Offset >= 0 && texture.Offset + texture.Size <= pixelData.Length;
    }

    /// <summary>Encodes <paramref name="bgra"/> in the texture's own format and writes it over the old pixels in the _v pack.</summary>
    public void ReplacePixels(FlashPackEntry entry, FlashTexture texture, byte[] bgra, string label)
    {
        byte[] encoded = DxtCodec.EncodeChain(bgra, texture.Width, texture.Height, texture.IsDxt1, texture.MipCount);
        byte[] pixelData = PixelDataPack.Bodies[entry.PixelDataIndex];
        if (encoded.Length != texture.Size || texture.Offset + encoded.Length > pixelData.Length)
            throw new InvalidDataException($"{label}: encoded size {encoded.Length} does not fit the {texture.Size}-byte slot.");

        Buffer.BlockCopy(encoded, 0, pixelData, texture.Offset, encoded.Length);
    }
}
