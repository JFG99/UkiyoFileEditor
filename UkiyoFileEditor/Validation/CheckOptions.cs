using System.Text.RegularExpressions;

namespace UkiyoFileEditor.Validation;

/// <summary>The limits and switches the checker uses (taken from the viewer settings).</summary>
public sealed class CheckOptions
{
    public int MaxLineChars;
    public int MaxLines;
    public bool WarnNonAscii = true;
    public string AllowedChars = "";
    public bool WarnJapanese = true;
    public bool SpaceAroundIcons = true;
    public Regex Icon = TextTools.MakeIconRegex("");

    public static CheckOptions From(AppSettings settings) => new()
    {
        MaxLineChars = settings.MaxLineChars,
        MaxLines = settings.MaxLines,
        WarnNonAscii = settings.WarnNonAscii,
        AllowedChars = settings.AllowedChars,
        WarnJapanese = settings.WarnJapanese,
        SpaceAroundIcons = settings.SpaceAroundIcons,
        Icon = TextTools.MakeIconRegex(settings.IconTagPattern),
    };
}
