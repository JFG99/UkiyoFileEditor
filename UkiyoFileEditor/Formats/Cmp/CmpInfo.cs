namespace UkiyoFileEditor.Formats.Cmp;

/// <summary>Information gathered while decompressing a file.</summary>
public sealed class CmpInfo
{
    /// <summary>True when the input was a "$CMP" container (otherwise it was returned unchanged).</summary>
    public bool IsCmp;

    public int ChunkCount;
    public int CompressedSize;
    public int DecompressedSize;

    /// <summary>Distinct chunk types found, e.g. "$CL0, $CR0".</summary>
    public string Modes = "";

    /// <summary>Non-empty when the decompressed size differs from the size announced in the header.</summary>
    public string Warning = "";
}
