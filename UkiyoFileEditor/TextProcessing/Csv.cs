using System.Text;

namespace UkiyoFileEditor.TextProcessing;

/// <summary>Small reader / writer for CSV and TSV sheets (quotes, doubled quotes and line breaks inside cells).</summary>
public static class Csv
{
    public static void Write(string path, IEnumerable<string[]> rows, char delimiter)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        foreach (string[] row in rows)
        {
            for (int index = 0; index < row.Length; index++)
            {
                if (index > 0) writer.Write(delimiter);
                writer.Write(Quote(row[index], delimiter));
            }
            writer.Write("\r\n");
        }
    }

    private static string Quote(string value, char delimiter)
    {
        if (value.IndexOfAny(new[] { delimiter, '"', '\r', '\n' }) < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    public static List<List<string>> Read(string text, char delimiter)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false, foundAny = false;

        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            if (quoted)
            {
                if (character == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"') { cell.Append('"'); index++; }
                    else quoted = false;
                }
                else cell.Append(character);
                continue;
            }

            if (character == '"' && cell.Length == 0) { quoted = true; foundAny = true; }
            else if (character == delimiter) { row.Add(cell.ToString()); cell.Clear(); foundAny = true; }
            else if (character == '\r' || character == '\n')
            {
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                if (foundAny || cell.Length > 0)
                {
                    row.Add(cell.ToString());
                    rows.Add(row);
                }
                row = new List<string>();
                cell.Clear();
                foundAny = false;
            }
            else { cell.Append(character); foundAny = true; }
        }

        if (foundAny || cell.Length > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }
        return rows;
    }
}
