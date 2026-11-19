namespace UkiyoFileEditor.Models;

/// <summary>A "*_m.pak" with the "*_v.pak" next to it, as listed in the Flash texture tab.</summary>
public sealed class FlashPackPair
{
    /// <summary>The _m pack (texture lists).</summary>
    public string MetadataPath { get; init; } = "";

    /// <summary>The _v pack (pixel data).</summary>
    public string PixelDataPath { get; init; } = "";

    /// <summary>Path relative to the opened folder.</summary>
    public string Display { get; init; } = "";

    public override string ToString() => Display;
}
