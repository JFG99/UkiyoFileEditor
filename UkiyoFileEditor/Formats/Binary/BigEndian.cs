namespace UkiyoFileEditor.Formats.Binary;

/// <summary>Big-endian integer helpers, magic-number checks and alignment used by every container format of the game.</summary>
internal static class BigEndian
{
    public static uint ReadUInt32(byte[] data, int position) =>
        ((uint)data[position] << 24) | ((uint)data[position + 1] << 16) | ((uint)data[position + 2] << 8) | data[position + 3];

    public static void WriteUInt32(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    /// <summary>True when the ASCII <paramref name="magic"/> is found at <paramref name="position"/>.</summary>
    public static bool HasMagic(byte[] data, string magic, int position = 0)
    {
        if (position + magic.Length > data.Length) return false;
        for (int index = 0; index < magic.Length; index++)
        {
            if (data[position + index] != (byte)magic[index]) return false;
        }
        return true;
    }

    public static int AlignUp(long value, int alignment) => (int)((value + alignment - 1) / alignment * alignment);

    /// <summary>Appends zero bytes until the stream length is a multiple of <paramref name="alignment"/>.</summary>
    public static void PadToAlignment(Stream stream, int alignment)
    {
        while (stream.Length % alignment != 0) stream.WriteByte(0);
    }
}
