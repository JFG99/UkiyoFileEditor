using System.Text.RegularExpressions;

namespace UkiyoFileEditor.TextProcessing;

/// <summary>Small text helpers shared by the Messages tab.</summary>
public static class TextTools
{
    public static readonly Regex Tag = new(@"<[^<>\r\n]*>", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    public const string DefaultIconPattern = @"<PAD=[^<>\r\n]*>";

    /// <summary>All line breaks as a plain "\n". Translations are stored this way; the pack writer restores CR LF where the original used it.</summary>
    public static string Norm(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    /// <summary>Text for a multi-line TextBox (it needs CR LF).</summary>
    public static string ToBox(string text) => Norm(text).Replace("\n", "\r\n");

    public static string FromBox(string text) => Norm(text);

    public static Regex MakeIconRegex(string pattern)
    {
        try
        {
            if (pattern.Length > 0) return new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
        }
        catch (ArgumentException) { }
        return new Regex(DefaultIconPattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }

    /// <summary>Characters a line takes on screen: codes count 0, except button icons, which count 1.</summary>
    public static int VisibleLength(string line, Regex icon) => Tag.Replace(icon.Replace(line, "■"), "").Length;

    /// <summary>Splits off the codes at the very start and the very end of a text.</summary>
    public static void SplitCodes(string text, Regex regex, out string prefix, out string body, out string suffix)
    {
        int start = 0;
        while (true)
        {
            Match match = regex.Match(text, start);
            if (match.Success && match.Index == start && match.Length > 0) start += match.Length;
            else break;
        }

        int endIndex = text.Length;
        while (endIndex > start)
        {
            Match? last = null;
            foreach (Match match in regex.Matches(text, start))
            {
                if (match.Length > 0 && match.Index + match.Length == endIndex) { last = match; break; }
            }
            if (last == null) break;
            endIndex = last.Index;
        }

        prefix = text.Substring(0, start);
        body = text.Substring(start, endIndex - start);
        suffix = text.Substring(endIndex);
    }

    public static string OneLine(string text, int maxLength = 120)
    {
        text = Norm(text).Replace("\n", " ⏎ ");
        return text.Length > maxLength ? text.Substring(0, maxLength) + "…" : text;
    }

    /// <summary>
    /// Breaks a translation into lines of at most <paramref name="maxLineLength"/> visible characters, only at spaces (words are never split).
    /// Codes at the very start and end of the text stay where they are. With <paramref name="rejoin"/> the existing line breaks are
    /// first turned into spaces; without it every line is wrapped on its own and the existing breaks are kept.
    /// </summary>
    public static string Wrap(string text, int maxLineLength, bool rejoin, Regex icon)
    {
        text = Norm(text);
        if (maxLineLength <= 0) return text;

        SplitCodes(text, Tag, out string prefix, out string body, out string suffix);
        IEnumerable<string> paragraphs = rejoin
            ? new[] { string.Join(" ", body.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0)) }
            : body.Split('\n');

        var lines = new List<string>();
        foreach (string paragraph in paragraphs) lines.AddRange(WrapParagraph(paragraph, maxLineLength, icon));
        return prefix + string.Join("\n", lines) + suffix;
    }

    private static List<string> WrapParagraph(string paragraph, int maxLineLength, Regex icon)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        int length = 0;
        foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int wordLength = VisibleLength(word, icon);
            if (current.Length == 0) { current.Append(word); length = wordLength; }
            else if (length + 1 + wordLength <= maxLineLength) { current.Append(' ').Append(word); length += 1 + wordLength; }
            else
            {
                result.Add(current.ToString());
                current.Clear();
                current.Append(word);
                length = wordLength;
            }
        }
        result.Add(current.ToString());
        return result;
    }
}
