namespace UkiyoFileEditor.UI.Tabs;

/// <summary>How the bytes of a raw .srdv file are interpreted when it is shown as an image.</summary>
internal enum PixelKind
{
    /// <summary>8-bit alpha mask, shown as white glyphs (fonts).</summary>
    Alpha8,

    /// <summary>8-bit grayscale, opaque.</summary>
    Gray8,

    /// <summary>32-bit B,G,R,A.</summary>
    Bgra32,

    /// <summary>32-bit R,G,B,A.</summary>
    Rgba32
}
