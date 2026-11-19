using System.Text;
using System.Text.RegularExpressions;

namespace UkiyoFileEditor.UI.Tabs;

/// <summary>Exporting translations to CSV / TSV and the shown rows to a text file.</summary>
public sealed partial class MessageTab
{
    private string SuggestName(string what)
    {
        if (_resultsMode) return "search_results";
        string baseName = _openFile == null ? "messages" : Path.GetFileNameWithoutExtension(_openFile.Display);
        return baseName + (what == "file" || what == "rows" ? "_translation" : "");
    }

    private void ExportRows(List<RowRef> rows, string what)
    {
        CommitDetail();
        if (rows.Count == 0)
        {
            _statusLabel.Text = "Nothing to export.";
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "CSV file (*.csv)|*.csv|TSV file (*.tsv)|*.tsv",
            FileName = SuggestName(what),
            AddExtension = true,
            DefaultExt = "csv",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            TranslationSheet.Write(dialog.FileName, rows.Select(rowRef =>
                new[] { rowRef.File, rowRef.Row.Key, rowRef.Row.Id, TextTools.Norm(rowRef.Original), rowRef.Translation }));
            _statusLabel.Text = $"Saved {rows.Count:N0} row(s) to {dialog.FileName}.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private Task ExportAllAsync()
    {
        FlushSave();
        var items = _store.AllItems().OrderBy(entry => entry.File, StringComparer.OrdinalIgnoreCase).ToList();
        if (items.Count == 0)
        {
            _statusLabel.Text = "There are no stored translations to export.";
            return Task.CompletedTask;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "CSV file (*.csv)|*.csv|TSV file (*.tsv)|*.tsv",
            FileName = "all_translations",
            AddExtension = true,
            DefaultExt = "csv",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return Task.CompletedTask;

        try
        {
            TranslationSheet.Write(dialog.FileName, items.Select(entry =>
                new[] { entry.File, "", entry.Key, TextTools.Norm(entry.Item.Source), entry.Item.Text }));
            _statusLabel.Text = $"Saved {items.Count:N0} translation(s) to {dialog.FileName}.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        return Task.CompletedTask;
    }

    private void ExportShownText()
    {
        CommitDetail();
        if (_shown.Count == 0)
        {
            _statusLabel.Text = "Nothing to export.";
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "Text file (*.txt)|*.txt",
            FileName = SuggestName("text"),
            AddExtension = true,
            DefaultExt = "txt",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var builder = new StringBuilder();
            foreach (RowRef rowRef in _shown)
            {
                string header = _resultsMode ? $"{rowRef.File} :: {rowRef.Row.Key}" : rowRef.Row.Key;
                if (rowRef.Row.Id.Length > 0) header += $"  [{rowRef.Row.Id}]";
                builder.Append("# ").AppendLine(header);
                builder.AppendLine(TextTools.ToBox(rowRef.Original));
                if (rowRef.IsTranslated)
                {
                    builder.AppendLine("--");
                    builder.AppendLine(TextTools.ToBox(rowRef.Translation));
                }
                builder.AppendLine();
            }
            File.WriteAllText(dialog.FileName, builder.ToString(), new UTF8Encoding(true));
            _statusLabel.Text = $"Saved {_shown.Count:N0} row(s) to {dialog.FileName}.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
