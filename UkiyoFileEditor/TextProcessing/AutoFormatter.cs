using System.Text.RegularExpressions;

namespace UkiyoFileEditor.TextProcessing;

/// <summary>
/// Re-breaks a translation so that it has the same number of lines as the Japanese original:
///  - the number of lines is the number of lines of the original (per paragraph, when the paragraphs match);
///  - when the original puts each sentence on its own line(s), each English sentence starts a new line, and the lines are shared
///    out between the sentences so that all lines come out about equally long;
///  - codes (tags) count 0 wide, button icons count 1, and the codes at the very start and end of the text stay where they are.
/// </summary>
public static class AutoFormatter
{
    private static readonly Regex JpEnd = new(@"[。！？!?…]+[」』）)”""’']*", RegexOptions.Compiled);
    private static readonly Regex SentenceEnd = new(@"[.!?…]+[""'”’)\]]*(\s+)(?=[""'“‘(\[]*[A-Z0-9])", RegexOptions.Compiled);
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase) { "mr.", "mrs.", "ms.", "dr.", "st.", "mt.", "vs.", "jr.", "sr." };

    /// <summary>Number of lines of an original, per paragraph (paragraphs are separated by a blank line).</summary>
    public static List<int> ParagraphLines(string original)
    {
        var result = new List<int>();
        int current = 0;
        foreach (string line in TextTools.Norm(original).Split('\n'))
        {
            if (line.Trim().Length > 0) current++;
            else if (current > 0) { result.Add(current); current = 0; }
        }
        if (current > 0) result.Add(current);
        if (result.Count == 0) result.Add(1);
        return result;
    }

    /// <summary>True when the original ends most of its sentences at the end of a line.</summary>
    private static bool OriginalHasSentenceLines(string original)
    {
        int total = 0, atEnd = 0;
        foreach (string rawBytes in TextTools.Norm(original).Split('\n'))
        {
            string line = TextTools.Tag.Replace(rawBytes, "").TrimEnd();
            foreach (Match match in JpEnd.Matches(line))
            {
                total++;
                if (match.Index + match.Length == line.Length) atEnd++;
            }
        }
        return total > 0 && atEnd * 2 > total;
    }

    public static string Format(string original, string translation, AutoFormatOptions options)
    {
        string text = TextTools.Norm(translation);
        if (text.Trim().Length == 0) return translation;

        TextTools.SplitCodes(text, TextTools.Tag, out string prefix, out string body, out string suffix);

        List<int> targets = ParagraphLines(original);
        var paragraphs = new List<string>();
        foreach (string paragraph in Regex.Split(body.Trim('\n', ' ', '\t'), @"\n[ \t]*\n"))
        {
            string flat = Flatten(paragraph);
            if (flat.Length > 0) paragraphs.Add(flat);
        }
        if (paragraphs.Count == 0) return translation;

        // Paragraphs that do not match the original's: treat the whole text as one paragraph with all the lines.
        if (paragraphs.Count != targets.Count)
        {
            paragraphs = new List<string> { string.Join(" ", paragraphs) };
            targets = new List<int> { targets.Sum() };
        }

        bool sentenceLines = options.SentencesOwnLine && OriginalHasSentenceLines(original);
        var result = new List<string>();
        for (int index = 0; index < paragraphs.Count; index++)
        {
            List<string> lines = LayOut(paragraphs[index], Math.Max(1, targets[index] + Math.Max(0, options.ExtraLines)), sentenceLines, options);
            result.Add(string.Join("\n", lines));
        }
        return prefix + string.Join("\n\n", result) + suffix;
    }

    private static string Flatten(string paragraph)
    {
        string flat = string.Join(" ", paragraph.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0));
        return Regex.Replace(flat, @" {2,}", " ");
    }

    /// <summary>Lines of the text, exactly <paramref name="lineCount"/> of them when possible (more only when the width limit forces it).</summary>
    private static List<string> LayOut(string flat, int lineCount, bool sentenceLines, AutoFormatOptions options)
    {
        List<string> units = sentenceLines ? SplitSentences(flat) : new List<string> { flat };
        if (units.Count > lineCount) units = new List<string> { flat };

        for (int lines = lineCount; lines <= lineCount + 30; lines++)
        {
            List<string>? result = Share(units, lines, options);
            if (result != null) return result;
        }
        return new List<string> { flat };
    }

    private static List<string> SplitSentences(string text)
    {
        var parts = new List<string>();
        int start = 0;
        foreach (Match match in SentenceEnd.Matches(text))
        {
            string before = text.Substring(start, match.Groups[1].Index - start);
            int space = before.LastIndexOf(' ');
            string last = space >= 0 ? before.Substring(space + 1) : before;
            if (Abbreviations.Contains(last)) continue;
            parts.Add(before);
            start = match.Groups[1].Index + match.Groups[1].Length;
        }
        parts.Add(text.Substring(start));
        return parts.Where(part => part.Length > 0).ToList();
    }

    /// <summary>
    /// Shares <paramref name="lineCount"/> lines out between the sentences (each gets at least one) so that every line comes as
    /// close as possible to the same length. Returns null when it cannot be done (too few words, or a line would be too wide).
    /// </summary>
    private static List<string>? Share(List<string> sentences, int lineCount, AutoFormatOptions options)
    {
        int sentenceCount = sentences.Count;
        if (sentenceCount > lineCount) return null;

        var wordsPerSentence = sentences.Select(sentence => sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToList();
        int totalLength = 0;
        foreach (string[] sentenceWords in wordsPerSentence)
            totalLength += sentenceWords.Sum(word => TextTools.VisibleLength(word, options.Icon)) + sentenceWords.Length - 1;
        double targetLineLength = (double)totalLength / lineCount;

        // bestSplits[sentence][lines] = the best way to put that sentence on exactly that many lines
        var bestSplits = new List<Dictionary<int, (double Cost, List<string> Lines)>>();
        foreach (string[] sentenceWords in wordsPerSentence)
        {
            var splitsByLineCount = new Dictionary<int, (double, List<string>)>();
            for (int lines = 1; lines <= Math.Min(lineCount, sentenceWords.Length); lines++)
            {
                var split = SplitInto(sentenceWords, lines, targetLineLength, options);
                if (split != null) splitsByLineCount[lines] = split.Value;
            }
            bestSplits.Add(splitsByLineCount);
        }

        // totalCost[s, l] = cheapest way to lay out the first s sentences on exactly l lines
        var totalCost = new double[sentenceCount + 1, lineCount + 1];
        var chosenLines = new int[sentenceCount + 1, lineCount + 1];
        for (int sentence = 0; sentence <= sentenceCount; sentence++)
            for (int lines = 0; lines <= lineCount; lines++) totalCost[sentence, lines] = double.PositiveInfinity;
        totalCost[0, 0] = 0;

        for (int sentence = 1; sentence <= sentenceCount; sentence++)
        {
            for (int usedLines = sentence; usedLines <= lineCount; usedLines++)
            {
                foreach (var (linesForSentence, option) in bestSplits[sentence - 1])
                {
                    if (usedLines - linesForSentence < sentence - 1) continue;
                    double previousCost = totalCost[sentence - 1, usedLines - linesForSentence];
                    if (double.IsPositiveInfinity(previousCost)) continue;
                    double cost = previousCost + option.Cost;
                    if (cost < totalCost[sentence, usedLines])
                    {
                        totalCost[sentence, usedLines] = cost;
                        chosenLines[sentence, usedLines] = linesForSentence;
                    }
                }
            }
        }

        if (double.IsPositiveInfinity(totalCost[sentenceCount, lineCount])) return null;

        var result = new List<string>();
        int remainingLines = lineCount;
        for (int sentence = sentenceCount; sentence >= 1; sentence--)
        {
            int linesForSentence = chosenLines[sentence, remainingLines];
            result.InsertRange(0, bestSplits[sentence - 1][linesForSentence].Lines);
            remainingLines -= linesForSentence;
        }
        return result;
    }

    /// <summary>The best split of the words into exactly <paramref name="lineCount"/> lines (lines closest to <paramref name="targetLineLength"/>), or null.</summary>
    private static (double Cost, List<string> Lines)? SplitInto(string[] words, int lineCount, double targetLineLength, AutoFormatOptions options)
    {
        int wordCount = words.Length;
        if (lineCount < 1 || lineCount > wordCount) return null;

        var prefixLength = new int[wordCount + 1];
        for (int word = 0; word < wordCount; word++)
            prefixLength[word + 1] = prefixLength[word] + TextTools.VisibleLength(words[word], options.Icon);
        int LineLength(int firstWord, int endWord) => prefixLength[endWord] - prefixLength[firstWord] + (endWord - firstWord - 1);

        // cost[l, w] = cheapest way to put the first w words on l lines; breakBefore[l, w] = where line l starts
        var cost = new double[lineCount + 1, wordCount + 1];
        var breakBefore = new int[lineCount + 1, wordCount + 1];
        for (int line = 0; line <= lineCount; line++)
            for (int word = 0; word <= wordCount; word++) cost[line, word] = double.PositiveInfinity;
        cost[0, 0] = 0;

        for (int line = 1; line <= lineCount; line++)
        {
            for (int endWord = line; endWord <= wordCount; endWord++)
            {
                for (int startWord = line - 1; startWord < endWord; startWord++)
                {
                    if (double.IsPositiveInfinity(cost[line - 1, startWord])) continue;
                    int size = LineLength(startWord, endWord);
                    if (options.MaxWidthCap > 0 && size > options.MaxWidthCap) continue;
                    double candidateCost = cost[line - 1, startWord] + (size - targetLineLength) * (size - targetLineLength);
                    if (candidateCost < cost[line, endWord])
                    {
                        cost[line, endWord] = candidateCost;
                        breakBefore[line, endWord] = startWord;
                    }
                }
            }
        }

        if (double.IsPositiveInfinity(cost[lineCount, wordCount])) return null;

        var lines = new List<string>();
        int endOfLine = wordCount;
        for (int line = lineCount; line >= 1; line--)
        {
            int start = breakBefore[line, endOfLine];
            lines.Add(string.Join(" ", words.Skip(start).Take(endOfLine - start)));
            endOfLine = start;
        }
        lines.Reverse();
        return (cost[lineCount, wordCount], lines);
    }
}
