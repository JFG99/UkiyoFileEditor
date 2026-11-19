using System.Text;
using System.Text.RegularExpressions;

namespace UkiyoFileEditor.TextProcessing;

/// <summary>What to look for (and what to replace it with). Replace only ever changes translations.</summary>
public sealed class SearchSpec
{
    public string Find = "";
    public string Replace = "";
    public bool MatchCase;
    public bool WholeWord;
    public bool UseRegex;
    public SearchField Field = SearchField.All;

    public bool InId => Field is SearchField.All or SearchField.Id;
    public bool InOriginal => Field is SearchField.All or SearchField.Original;
    public bool InTranslation => Field is SearchField.All or SearchField.Translation;

    public bool IsEmpty => Find.Length == 0;

    public Regex BuildRegex()
    {
        string pattern = UseRegex ? Find : Regex.Escape(Find);
        if (WholeWord) pattern = @"(?<!\w)(?:" + pattern + @")(?!\w)";
        var options = RegexOptions.CultureInvariant;
        if (!MatchCase) options |= RegexOptions.IgnoreCase;
        return new Regex(pattern, options);
    }

    /// <summary>Builds the regex; false (with a message) when the pattern is not valid.</summary>
    public bool TryBuild(out Regex? regex, out string error)
    {
        try
        {
            regex = BuildRegex();
            error = "";
            return true;
        }
        catch (ArgumentException exception)
        {
            regex = null;
            error = exception.Message;
            return false;
        }
    }

    /// <summary>
    /// The replacement as the regex engine needs it. In the Replace box \n is a line feed, \r a carriage return, \t a tab and
    /// \\ one backslash. In regular-expression mode $1 and the like still work.
    /// </summary>
    public string ReplacementPattern
    {
        get
        {
            string text = ExpandEscapes(Replace);
            return UseRegex ? text : text.Replace("$", "$$");
        }
    }

    public static string ExpandEscapes(string text)
    {
        if (text.IndexOf('\\') < 0) return text;
        var builder = new StringBuilder(text.Length);
        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            if (character == '\\' && index + 1 < text.Length)
            {
                char next = text[index + 1];
                if (next == 'n') { builder.Append('\n'); index++; continue; }
                if (next == 'r') { builder.Append('\r'); index++; continue; }
                if (next == 't') { builder.Append('\t'); index++; continue; }
                if (next == '\\') { builder.Append('\\'); index++; continue; }
            }
            builder.Append(character);
        }
        return builder.ToString();
    }

    public bool Matches(Regex regex, string id, string entry, string original, string translation) =>
        (InId && (regex.IsMatch(id) || regex.IsMatch(entry))) ||
        (InOriginal && regex.IsMatch(TextTools.Norm(original))) ||
        (InTranslation && regex.IsMatch(TextTools.Norm(translation)));
}
