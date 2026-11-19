namespace UkiyoFileEditor.Formats.Texture;

/// <summary>One texture listed in an .srd file; its pixels live in the matching .srdv at <see cref="Offset"/>.</summary>
public sealed class FlashTexture
{
    public const int Dxt1Format = 0x0F;
    public const int Dxt5Format = 0x11;

    public string Name = "";
    public int Width;
    public int Height;

    /// <summary>Pixel format code stored in the $TXR block (<see cref="Dxt1Format"/> or <see cref="Dxt5Format"/>).</summary>
    public int Format;

    /// <summary>Offset of the pixel data inside the .srdv file.</summary>
    public int Offset;

    /// <summary>Size of the pixel data in bytes.</summary>
    public int Size;

    /// <summary>Number of mip levels stored (u8 at body offset 13 of the $TXR block): 1 = base image only, 9 = 256x256 down to 1x1.</summary>
    public int MipCount = 1;

    /// <summary>Set when the pixels were replaced in this session.</summary>
    public bool Modified;

    public bool IsDxt1 => Format == Dxt1Format;
    public bool IsDxt5 => Format == Dxt5Format;
    public bool Supported => (IsDxt1 || IsDxt5) && Size == DxtCodec.ChainSize(Width, Height, IsDxt1, MipCount);

    public string FormatName => (IsDxt1 ? "DXT1" : IsDxt5 ? "DXT5" : $"format 0x{Format:X2}") + (MipCount > 1 ? $", {MipCount} mips" : "");
}
