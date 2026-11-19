namespace UkiyoFileEditor.Models;

/// <summary>One row of the grid: a message row plus its translation and check results.</summary>
public sealed class RowRef
{
    public RowRef(string file, MessageRow row, int index)
    {
        File = file;
        Row = row;
        Index = index;
    }

    public string File { get; }
    public MessageRow Row { get; }

    /// <summary>Position in the pack, used for "file order".</summary>
    public int Index { get; }

    /// <summary>The translation with plain "\n" line breaks, or "" when there is none.</summary>
    public string Translation { get; set; } = "";

    public List<Issue> Issues { get; set; } = new();

    public bool IsTranslated => Translation.Length > 0;
    public string Key => TranslationStore.KeyOf(Row);
    public string Original => Row.Text;
    public bool HasWarning => Issues.Any(issue => issue.Severity == Severity.Warning);
}
