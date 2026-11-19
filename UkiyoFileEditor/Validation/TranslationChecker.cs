using System.Text.RegularExpressions;

namespace UkiyoFileEditor.Validation;

/// <summary>Checks a translation against its original: codes, placeholders, line length, spacing, leftover Japanese, odd characters.</summary>
public static class TranslationChecker
{
    private static readonly Regex Ruby = new(@"^<R=[^<>\r\n]*>$", RegexOptions.Compiled);
    private static readonly Regex Japanese = new(@"[぀-ヿ㐀-䶿一-鿿ｦ-ﾟ]", RegexOptions.Compiled);
    private static readonly Regex Placeholder = new(@"%[-+ #0-9.]*[a-zA-Z]|\{[^{}\r\n]*\}", RegexOptions.Compiled);

    public static bool HasJapanese(string text) => Japanese.IsMatch(text);

    private static List<string> Codes(string text)
    {
        var list = new List<string>();
        foreach (Match match in TextTools.Tag.Matches(text))
            if (!Ruby.IsMatch(match.Value)) list.Add(match.Value); // furigana tags are dropped on purpose before translating
        return list;
    }

    /// <summary>Items of <paramref name="items"/> that <paramref name="itemsToRemove"/> does not have (counting repeats).</summary>
    private static List<string> Minus(List<string> items, List<string> itemsToRemove)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string item in itemsToRemove) counts[item] = counts.GetValueOrDefault(item) + 1;
        var rest = new List<string>();
        foreach (string item in items)
        {
            if (counts.TryGetValue(item, out int remaining) && remaining > 0) counts[item] = remaining - 1;
            else rest.Add(item);
        }
        return rest;
    }

    public static List<Issue> Check(string original, string translation, CheckOptions options)
    {
        var issues = new List<Issue>();
        if (translation.Trim().Length == 0) return issues;

        string normalizedTranslation = TextTools.Norm(translation);
        string orig = TextTools.Norm(original);

        List<string> codesOriginal = Codes(orig), codesTranslation = Codes(normalizedTranslation);
        List<string> missing = Minus(codesOriginal, codesTranslation);
        List<string> extra = Minus(codesTranslation, codesOriginal);
        if (missing.Count > 0) issues.Add(new Issue(Severity.Warning, "Missing code(s): " + string.Join(" ", missing.Take(4))));
        if (extra.Count > 0) issues.Add(new Issue(Severity.Warning, "Extra code(s): " + string.Join(" ", extra.Take(4))));

        List<string> placeholdersOriginal = Placeholder.Matches(orig).Select(match => match.Value).ToList();
        List<string> placeholdersTranslation = Placeholder.Matches(normalizedTranslation).Select(match => match.Value).ToList();
        if (Minus(placeholdersOriginal, placeholdersTranslation).Count > 0 || Minus(placeholdersTranslation, placeholdersOriginal).Count > 0)
            issues.Add(new Issue(Severity.Warning, "Placeholders (%d, {…}) differ from the original."));

        string[] lines = normalizedTranslation.Split('\n');
        if (options.MaxLines > 0 && lines.Length > options.MaxLines)
            issues.Add(new Issue(Severity.Warning, $"{lines.Length} lines (max {options.MaxLines})."));
        if (options.MaxLineChars > 0)
        {
            for (int index = 0; index < lines.Length; index++)
            {
                int length = TextTools.VisibleLength(lines[index], options.Icon);
                if (length > options.MaxLineChars)
                    issues.Add(new Issue(Severity.Warning, $"Line {index + 1} is {length} characters (max {options.MaxLineChars})."));
            }
        }

        if (options.SpaceAroundIcons)
        {
            string icon = options.Icon.ToString();
            if (Regex.IsMatch(normalizedTranslation, "[A-Za-z0-9](?:" + icon + ")|(?:" + icon + ")[A-Za-z0-9]"))
                issues.Add(new Issue(Severity.Info, "No space next to a button icon."));
        }

        string body = TextTools.Tag.Replace(normalizedTranslation, "");
        if (options.WarnJapanese && Japanese.IsMatch(body))
            issues.Add(new Issue(Severity.Warning, "Japanese text left in the translation."));

        if (options.WarnNonAscii)
        {
            var badCharacters = new SortedSet<char>();
            foreach (char character in body)
            {
                if (character == '\n') continue;
                if ((character < 0x20 || character > 0x7E) && !options.AllowedChars.Contains(character) && !Japanese.IsMatch(character.ToString())) badCharacters.Add(character);
            }
            if (badCharacters.Count > 0) issues.Add(new Issue(Severity.Warning, "Non-ASCII characters: " + string.Join(" ", badCharacters)));
        }

        if (body.IndexOf('<') >= 0 || body.IndexOf('>') >= 0)
            issues.Add(new Issue(Severity.Warning, "Unmatched < or > (broken code?)."));

        if (normalizedTranslation.Contains("  ", StringComparison.Ordinal))
            issues.Add(new Issue(Severity.Info, "Double space."));
        if (lines.Any(line => line.Length > 0 && (char.IsWhiteSpace(line[0]) || char.IsWhiteSpace(line[^1]))))
            issues.Add(new Issue(Severity.Info, "Leading or trailing space on a line."));

        if (normalizedTranslation == orig && Japanese.IsMatch(body))
            issues.Add(new Issue(Severity.Info, "Same as the original."));

        return issues;
    }
}
