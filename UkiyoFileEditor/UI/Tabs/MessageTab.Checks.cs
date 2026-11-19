using System.Text;
using System.Text.RegularExpressions;

namespace UkiyoFileEditor.UI.Tabs;

/// <summary>Checking translations against the quality rules.</summary>
public sealed partial class MessageTab
{
    private void ShowViewerSettings()
    {
        CommitDetail();
        using var form = new ViewerSettingsForm(_settings);
        if (form.ShowDialog(this) != DialogResult.OK) return;

        _check = CheckOptions.From(_settings);
        foreach (RowRef rowRef in _source) RefreshRef(rowRef);
        ApplyView();
        _statusLabel.Text = "Checker settings saved; the notes were updated.";
    }

    /// <summary>F7: re-checks the open list and shows the rows that have notes.</summary>
    private void CheckOpenFile()
    {
        CommitDetail();
        foreach (RowRef rowRef in _source) RefreshRef(rowRef);

        int warnings = _source.Count(rowRef => rowRef.HasWarning);
        int notes = _source.Count(rowRef => rowRef.Issues.Count > 0);
        _showBox.SelectedIndex = notes > 0 ? 3 : 0;
        ApplyView();

        _statusLabel.Text = notes == 0
            ? "No problems found in the translated rows of this list."
            : $"{warnings:N0} row(s) with warnings, {notes - warnings:N0} with notes only. F8 / Shift+F8 jump between them.";
    }

    private async Task CheckAllFilesAsync()
    {
        if (_busy) return;
        CommitDetail();

        List<PackFile> files = _fileList.Items.Cast<PackFile>().Where(packFile => _store.HasAny(packFile.Display)).ToList();
        if (files.Count == 0)
        {
            _statusLabel.Text = "None of the listed files has translations to check.";
            return;
        }

        var items = new List<IssueItem>();
        int rowsChecked = 0;
        SetBusy(true);
        try
        {
            List<LoadedPack> loaded = await LoadPacksAsync(files, "Checking");
            foreach (LoadedPack loadedPack in loaded)
            {
                if (loadedPack.Pack == null) continue;
                string file = loadedPack.File.Display;
                foreach (MessageRow row in loadedPack.Pack.Rows)
                {
                    string? stored = _store.Get(file, row);
                    if (stored == null) continue;
                    rowsChecked++;
                    string translation = TextTools.Norm(stored);
                    foreach (Issue issue in TranslationChecker.Check(row.Text, translation, _check))
                    {
                        items.Add(new IssueItem(file, TranslationStore.KeyOf(row), row.Id.Length > 0 ? row.Id : row.Key,
                            issue.Severity, issue.Message, row.Text, translation));
                    }
                }
            }
        }
        finally
        {
            SetBusy(false);
        }

        if (items.Count == 0)
        {
            MessageBox.Show(this, $"No problems found in {rowsChecked:N0} translated row(s) of {files.Count:N0} file(s).", "Check all files",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var form = new IssuesForm(items, files.Count, rowsChecked);
        form.JumpRequested += JumpTo;
        form.Owner = FindForm();
        form.Show();
    }
}
