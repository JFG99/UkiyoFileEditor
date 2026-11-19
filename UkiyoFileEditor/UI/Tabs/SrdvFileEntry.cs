namespace UkiyoFileEditor.UI.Tabs;

/// <summary>One .srdv file in the list of the texture tab.</summary>
internal sealed class SrdvFileEntry
{
    public string FullPath = "";

    /// <summary>Path relative to the opened folder.</summary>
    public string Display = "";

    public override string ToString() => Display;
}
