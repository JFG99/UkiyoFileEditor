using System.Text;

namespace UkiyoFileEditor.Formats.Stpk;

/// <summary>Constants and small helpers of the STPZ / STPK container layout.</summary>
internal static class StpkFormat
{
    public const string WrapperMagic = "STPZ";
    public const string ArchiveMagic = "STPK";

    /// <summary>The STPZ wrapper and the STPK header are both 16 bytes.</summary>
    public const int HeaderSize = 0x10;

    public const int EntryCountOffset = 8;
    public const int HeaderSizeOrAlignmentOffset = 12;

    /// <summary>Default row size of the entry table: u32 offset, u32 size, 8 bytes padding, 32-byte name.</summary>
    public const int DefaultStride = 0x30;

    /// <summary>The name starts after the offset, size and padding fields of a table row.</summary>
    public const int RowNameOffset = 16;

    /// <summary>The offset and size fields; the rest of a row is copied unchanged when an archive is rebuilt.</summary>
    public const int RowOffsetAndSizeBytes = 8;

    public const int MinStride = 0x18;
    public const int MaxStride = 0x400;
    public const int MaxEntryCount = 500_000;
    public const int DefaultAlignment = 16;

    /// <summary>Decodes a NUL-terminated entry name (Shift-JIS, falling back to Latin-1).</summary>
    public static string ReadEntryName(byte[] data, int position, int maxLength)
    {
        int end = position;
        int limit = Math.Min(data.Length, position + maxLength);
        while (end < limit && data[end] != 0) end++;

        try
        {
            return Encoding.GetEncoding(932).GetString(data, position, end - position);
        }
        catch (Exception)
        {
            return Encoding.Latin1.GetString(data, position, end - position);
        }
    }
}
