namespace UkiyoFileEditor.Formats.Texture;

/// <summary>One .srd file of a Flash pack together with the index of its pixel data (.srdv) in the _v pack.</summary>
public sealed class FlashPackEntry
{
    public const int NoPixelData = -1;

    public string Name = "";

    /// <summary>Index of the matching ".srdv" entry in the _v pack, or <see cref="NoPixelData"/>.</summary>
    public int PixelDataIndex = NoPixelData;

    public List<FlashTexture> Textures = new();

    public bool HasPixelData => PixelDataIndex >= 0;
}
