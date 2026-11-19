namespace UkiyoFileEditor.UI.Tabs;

/// <summary>One texture in the tree of the Flash texture tab: the .srd it belongs to, the texture and its tree node.</summary>
internal sealed class FlashTextureReference
{
    public FlashTextureReference(FlashPackEntry entry, FlashTexture texture)
    {
        Entry = entry;
        Texture = texture;
    }

    public FlashPackEntry Entry { get; }
    public FlashTexture Texture { get; }
    public TreeNode? TreeNode { get; set; }
}
