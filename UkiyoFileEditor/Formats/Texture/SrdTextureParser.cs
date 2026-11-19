using System.Text;
using UkiyoFileEditor.Formats.Binary;

namespace UkiyoFileEditor.Formats.Texture;

/// <summary>
/// Reads the texture list out of an .srd file (verified on the 8 .srd files of game_flash_pause_opt_m.pak, 122 textures):
///   Blocks: magic[4], u32 BE bodyLength, u32 BE childLength, u32 unused, then the body and the child data, each padded to 16.
///   $TXR body : u32 1, u16 swizzle, u16 width, u16 height, u16 pitch, u8 format (0x0F = DXT1, 0x11 = DXT5), u8 mip level count (1 = no mips; model textures use a full chain, 9 for 256x256), ...
///               its child is a $RSI block: 16 bytes of header fields, then u32 (0x40000000 | offset into the .srdv),
///               u32 size, u32 0x80, u32 0, and the texture's name as a NUL-terminated string.
/// </summary>
public static class SrdTextureParser
{
    private const string TextureBlockMagic = "$TXR";
    private const string ResourceBlockMagic = "$RSI";
    private const int BlockHeaderSize = 16;
    private const int MinimumTextureBodyLength = 16;
    private const int ResourceBlockMinimumSize = 48;
    private const int ResourceLengthFieldOffset = 4;
    private const int ResourceLocatorOffset = 32;
    private const int ResourceSizeOffset = 36;
    private const int ResourceNameOffset = 48;
    private const uint LocatorInSrdvFlag = 0x40000000;
    private const uint LocatorOffsetMask = 0x3FFFFFFF;
    private const int WidthFieldOffset = 6;
    private const int HeightFieldOffset = 8;
    private const int FormatFieldOffset = 12;
    private const int MipCountFieldOffset = 13;

    public static List<FlashTexture> Parse(byte[] srd)
    {
        var textures = new List<FlashTexture>();
        int blockStart = 0;

        while (blockStart + BlockHeaderSize <= srd.Length)
        {
            string magic = Encoding.ASCII.GetString(srd, blockStart, 4);
            long bodyLength = BigEndian.ReadUInt32(srd, blockStart + 4);
            long childLength = BigEndian.ReadUInt32(srd, blockStart + 8);
            long bodyStart = blockStart + BlockHeaderSize;
            long nextBlockStart = bodyStart + AlignUp16(bodyLength) + AlignUp16(childLength);
            if (nextBlockStart <= blockStart || nextBlockStart > srd.Length + 15) break;

            if (magic == TextureBlockMagic && bodyLength >= MinimumTextureBodyLength)
            {
                FlashTexture? texture = ReadTexture(srd, (int)bodyStart, bodyLength);
                if (texture != null) textures.Add(texture);
            }

            blockStart = (int)nextBlockStart;
        }

        return textures;
    }

    /// <summary>Reads one $TXR block together with its $RSI child; returns null when the child is missing or points elsewhere than the .srdv.</summary>
    private static FlashTexture? ReadTexture(byte[] srd, int bodyStart, long bodyLength)
    {
        long resourceStart = bodyStart + AlignUp16(bodyLength);
        bool hasResourceBlock = resourceStart + ResourceBlockMinimumSize <= srd.Length
                                && Encoding.ASCII.GetString(srd, (int)resourceStart, 4) == ResourceBlockMagic;
        if (!hasResourceBlock) return null;

        int resource = (int)resourceStart;
        long resourceLength = BigEndian.ReadUInt32(srd, resource + ResourceLengthFieldOffset);
        uint locator = BigEndian.ReadUInt32(srd, resource + ResourceLocatorOffset);
        if ((locator & LocatorInSrdvFlag) == 0) return null;

        int nameStart = resource + ResourceNameOffset;
        int nameLimit = (int)Math.Min(srd.Length, resourceStart + BlockHeaderSize + resourceLength);
        int nameEnd = nameStart;
        while (nameEnd < nameLimit && srd[nameEnd] != 0) nameEnd++;

        var texture = new FlashTexture
        {
            Name = Encoding.Latin1.GetString(srd, nameStart, Math.Max(0, nameEnd - nameStart)),
            Width = (srd[bodyStart + WidthFieldOffset] << 8) | srd[bodyStart + WidthFieldOffset + 1],
            Height = (srd[bodyStart + HeightFieldOffset] << 8) | srd[bodyStart + HeightFieldOffset + 1],
            Format = srd[bodyStart + FormatFieldOffset],
            MipCount = Math.Max(1, (int)srd[bodyStart + MipCountFieldOffset]),
            Offset = (int)(locator & LocatorOffsetMask),
            Size = (int)BigEndian.ReadUInt32(srd, resource + ResourceSizeOffset),
        };

        // Older Flash packs carry only the base image even when the byte says otherwise: trust the stored size.
        bool sizeMatchesMipChain = texture.Size == DxtCodec.ChainSize(texture.Width, texture.Height, texture.IsDxt1, texture.MipCount);
        if (texture.MipCount > 1 && !sizeMatchesMipChain) texture.MipCount = 1;

        return texture;
    }

    private static long AlignUp16(long value) => (value + 15) / 16 * 16;
}
