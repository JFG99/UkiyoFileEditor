namespace UkiyoFileEditor.Formats.Cmp;

/// <summary>Layout constants of the "$CMP" container shared by the decoder and the encoder.</summary>
internal static class CmpFormat
{
    public const string ContainerMagic = "$CMP";
    public const string RawChunkMagic = "$CR0";
    public const string LzChunkPrefix = "$CL";
    public const string LzChunkMagic = "$CL0";

    /// <summary>The container header is followed immediately by the first chunk.</summary>
    public const int ContainerHeaderSize = 32;

    /// <summary>Offset of the total decompressed size inside the container header.</summary>
    public const int ContainerDecompressedSizeOffset = 16;

    public const int ChunkHeaderSize = 16;

    /// <summary>Largest amount of source data stored in one chunk by the encoder.</summary>
    public const int MaxChunkSourceSize = 15360;
}
