using System.Text;

namespace UkiyoFileEditor.Services;

/// <summary>Reading and writing translation sheets (CSV / TSV) with the columns File, Entry, ID, Original, Translation.</summary>
public static class TranslationSheet
{
    private static readonly string[] HeaderRow = { "File", "Entry", "ID", "Original", "Translation" };

    private const int DefaultFileColumn = 0;
    private const int DefaultEntryColumn = 1;
    private const int DefaultIdColumn = 2;
    private const int DefaultTranslationColumn = 4;

    /// <summary>Tab for ".tsv" files, comma for everything else.</summary>
    public static char DelimiterFor(string path) =>
        path.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase) ? '\t' : ',';

    /// <summary>Guesses the delimiter of a sheet from its first line.</summary>
    public static char DetectDelimiter(string text)
    {
        int firstLineEnd = text.IndexOf('\n');
        string firstLine = firstLineEnd < 0 ? text : text.Substring(0, firstLineEnd);
        return firstLine.Count(character => character == '\t') > firstLine.Count(character => character == ',') ? '\t' : ',';
    }

    /// <summary>Writes the header row followed by <paramref name="dataRows"/> (each: file, entry, id, original, translation).</summary>
    public static void Write(string path, IEnumerable<string[]> dataRows)
    {
        var lines = new List<string[]> { HeaderRow };
        lines.AddRange(dataRows);
        Csv.Write(path, lines, DelimiterFor(path));
    }

    /// <summary>
    /// Reads the translations of a sheet. Columns are found by their header names when there is a header row, otherwise
    /// the default order File, Entry, ID, Original, Translation is assumed. Rows without a translation are counted, not returned.
    /// </summary>
    /// <param name="defaultFile">File used for rows of a sheet that has no File column ("" when there is none).</param>
    /// <exception cref="InvalidDataException">The sheet has no ID / Entry column or no Translation column.</exception>
    public static List<SheetRow> Read(string path, string defaultFile, out int emptyTranslationCount)
    {
        string text = File.ReadAllText(path, Encoding.UTF8);
        char delimiter = path.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase) ? '\t' : DetectDelimiter(text);
        List<List<string>> table = Csv.Read(text, delimiter);

        int fileColumn = DefaultFileColumn, entryColumn = DefaultEntryColumn, idColumn = DefaultIdColumn;
        int translationColumn = DefaultTranslationColumn, firstDataRow = 0;
        if (table.Count > 0)
        {
            List<string> header = table[0].Select(headerCell => headerCell.Trim().ToLowerInvariant()).ToList();
            if (header.Contains("translation"))
            {
                fileColumn = header.IndexOf("file");
                entryColumn = header.IndexOf("entry");
                idColumn = header.IndexOf("id");
                translationColumn = header.IndexOf("translation");
                firstDataRow = 1;
            }
        }
        if (idColumn < 0 && entryColumn < 0)
            throw new InvalidDataException("The sheet needs an ID (or Entry) column and a Translation column.");

        var sheetRows = new List<SheetRow>();
        emptyTranslationCount = 0;
        for (int rowIndex = firstDataRow; rowIndex < table.Count; rowIndex++)
        {
            List<string> line = table[rowIndex];
            string translation = TextTools.Norm(Cell(line, translationColumn));
            if (translation.Trim().Length == 0)
            {
                emptyTranslationCount++;
                continue;
            }

            string file = Cell(line, fileColumn).Trim();
            if (file.Length == 0 && fileColumn < 0) file = defaultFile;
            string key = TranslationStore.KeyOf(Cell(line, idColumn).Trim(), Cell(line, entryColumn).Trim());
            if (file.Length == 0 || key.Length == 0) continue;

            sheetRows.Add(new SheetRow(file, key, translation));
        }
        return sheetRows;
    }

    private static string Cell(List<string> row, int index) => index >= 0 && index < row.Count ? row[index] : "";
}
