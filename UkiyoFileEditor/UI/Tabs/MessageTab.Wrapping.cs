using System.Text;
using System.Text.RegularExpressions;

namespace UkiyoFileEditor.UI.Tabs;

/// <summary>Wrapping long lines, filling identical rows and the progress statistics.</summary>
public sealed partial class MessageTab
{
    private void WrapTool()
    {
        CommitDetail();
        using var dialog = new WrapDialog(_settings.MaxLineChars > 0 ? _settings.MaxLineChars : _settings.WrapMax);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        int maxLineChars = dialog.MaxChars;
        bool rejoin = dialog.Rejoin;
        _settings.WrapMax = maxLineChars;
        _settings.Save();
        Regex icon = _check.Icon;

        var changes = new List<TranslationChange>();
        if (dialog.Scope == 3)
        {
            foreach (var (file, key, item) in _store.AllItems())
            {
                string wrapped = TextTools.Wrap(item.Text, maxLineChars, rejoin, icon);
                if (wrapped == item.Text) continue;
                TranslationChange? change = _store.Set(file, key, item.Source, wrapped);
                if (change != null) changes.Add(change);
            }
        }
        else
        {
            List<RowRef> targets = dialog.Scope switch
            {
                0 => SelectedRefs(),
                1 => _shown.ToList(),
                _ => _source.ToList(),
            };
            foreach (RowRef rowRef in targets)
            {
                if (!rowRef.IsTranslated) continue;
                string wrapped = TextTools.Wrap(rowRef.Translation, maxLineChars, rejoin, icon);
                if (wrapped == rowRef.Translation) continue;
                TranslationChange? change = _store.Set(rowRef.File, rowRef.Key, rowRef.Row.Text, wrapped);
                if (change != null) changes.Add(change);
            }
        }

        if (changes.Count == 0)
        {
            _statusLabel.Text = "Nothing to wrap: every line already fits.";
            return;
        }
        _undo.Push("Wrap translations", changes);
        AfterBulkChange(changes);
        _statusLabel.Text = $"Wrapped {changes.Count:N0} translation(s) at {maxLineChars} characters. Ctrl+Z undoes it.";
    }

    private async Task FillIdenticalAsync()
    {
        if (_busy) return;
        CommitDetail();

        DialogResult answer = MessageBox.Show(this,
            "Fill untranslated rows whose Japanese text is exactly the same as a row you already translated.\n\n" +
            "Yes = all listed files\nNo = only the list on screen (open file / search results)\nCancel = stop",
            "Fill identical lines", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel) return;

        Dictionary<string, string> memory = _store.Memory();
        if (memory.Count == 0)
        {
            _statusLabel.Text = "There are no translations yet to copy from.";
            return;
        }

        var changes = new List<TranslationChange>();
        void Fill(string file, MessageRow row)
        {
            if (row.Text.Trim().Length == 0) return;
            if (_store.Get(file, row) != null) return;
            if (!memory.TryGetValue(row.Text, out string? text)) return;
            TranslationChange? change = _store.Set(file, TranslationStore.KeyOf(row), row.Text, text);
            if (change != null) changes.Add(change);
        }

        if (answer == DialogResult.Yes)
        {
            List<PackFile> files = _fileList.Items.Cast<PackFile>().ToList();
            SetBusy(true);
            try
            {
                List<LoadedPack> loaded = await LoadPacksAsync(files, "Reading");
                foreach (LoadedPack loadedPack in loaded)
                {
                    if (loadedPack.Pack == null) continue;
                    foreach (MessageRow row in loadedPack.Pack.Rows) Fill(loadedPack.File.Display, row);
                }
            }
            finally
            {
                SetBusy(false);
            }
        }
        else
        {
            foreach (RowRef rowRef in _source.ToList()) Fill(rowRef.File, rowRef.Row);
        }

        if (changes.Count == 0)
        {
            _statusLabel.Text = "No untranslated row has identical Japanese text to a translated one.";
            return;
        }
        _undo.Push("Fill identical lines", changes);
        AfterBulkChange(changes);
        _statusLabel.Text = $"Filled {changes.Count:N0} row(s) from identical text. Ctrl+Z undoes it.";
    }

    private async Task StatisticsAsync()
    {
        if (_busy) return;
        CommitDetail();

        List<PackFile> files = _fileList.Items.Cast<PackFile>().ToList();
        if (files.Count == 0)
        {
            _statusLabel.Text = "There are no files in the list.";
            return;
        }

        int total = 0, translated = 0, warnings = 0, unreadable = 0;
        long charsLeft = 0;
        var perFile = new List<(string File, int Left)>();

        SetBusy(true);
        try
        {
            List<LoadedPack> loaded = await LoadPacksAsync(files, "Counting");
            foreach (LoadedPack loadedPack in loaded)
            {
                if (loadedPack.Pack == null)
                {
                    unreadable++;
                    continue;
                }
                string file = loadedPack.File.Display;
                int left = 0;
                foreach (MessageRow row in loadedPack.Pack.Rows)
                {
                    if (!TranslationChecker.HasJapanese(row.Text)) continue;
                    total++;
                    string? stored = _store.Get(file, row);
                    if (stored == null)
                    {
                        left++;
                        charsLeft += row.Text.Length;
                        continue;
                    }
                    translated++;
                    if (TranslationChecker.Check(row.Text, TextTools.Norm(stored), _check).Any(issue => issue.Severity == Severity.Warning)) warnings++;
                }
                if (left > 0) perFile.Add((file, left));
            }
        }
        finally
        {
            SetBusy(false);
        }

        double percent = total == 0 ? 0 : translated * 100.0 / total;
        var builder = new StringBuilder();
        builder.AppendLine($"Files in the list: {files.Count:N0}" + (unreadable > 0 ? $"  ({unreadable:N0} could not be read)" : ""));
        builder.AppendLine($"Rows with Japanese text: {total:N0}");
        builder.AppendLine($"Translated: {translated:N0}  ({percent:0.0}%)");
        builder.AppendLine($"Still to translate: {total - translated:N0} rows, about {charsLeft:N0} characters");
        builder.AppendLine($"Translated rows with warnings: {warnings:N0}");
        if (perFile.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Files with the most untranslated rows:");
            foreach (var (file, left) in perFile.OrderByDescending(fileStat => fileStat.Left).Take(10))
                builder.AppendLine($"   {left,6:N0}   {file}");
        }
        MessageBox.Show(this, builder.ToString(), "Progress", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
