namespace UkiyoFileEditor.Models;

/// <summary>Outcome of comparing (or importing) the edited PNGs of one Flash pack.</summary>
public sealed class PackImportResult
{
    public bool NoFolder;

    /// <summary>PNG differs from the pixels in the pack, so the texture is replaced.</summary>
    public int Changed;

    /// <summary>PNG is identical to what the pack already holds, so it is left alone.</summary>
    public int Unchanged;

    /// <summary>No PNG with the expected name.</summary>
    public int Missing;

    /// <summary>PNG size differs from the texture size.</summary>
    public int WrongSize;

    /// <summary>Texture cannot be handled (unsupported format or no .srdv).</summary>
    public int Unsupported;

    /// <summary>PNG could not be opened.</summary>
    public int Unreadable;

    public List<string> Notes { get; } = new();
}
