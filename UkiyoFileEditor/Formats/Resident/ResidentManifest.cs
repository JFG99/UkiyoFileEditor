namespace UkiyoFileEditor.Formats.Resident;

/// <summary>Remembers which game file the extracted packs came from, so a second extraction can be skipped.</summary>
internal sealed class ResidentManifest
{
    /// <summary>Path of the game file that was extracted.</summary>
    public string Source { get; set; } = "";

    /// <summary>Size of the original file in bytes.</summary>
    public long Length { get; set; }

    /// <summary>Last write time (UTC ticks) of the original file.</summary>
    public long Ticks { get; set; }

    /// <summary>File names of the extracted packs, in archive order.</summary>
    public string[] Names { get; set; } = Array.Empty<string>();

    /// <summary>Index of the outer entry that holds the packs.</summary>
    public int MiddleIndex { get; set; }
}
