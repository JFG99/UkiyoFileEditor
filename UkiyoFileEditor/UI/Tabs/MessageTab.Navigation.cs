using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace UkiyoFileEditor.UI.Tabs;

/// <summary>Moving between rows and the help text.</summary>
public sealed partial class MessageTab
{
    // ---- Navigation ---------------------------------------------------------------
    private void SelectRow(int index, bool focusBox = false)
    {
        if (index < 0 || index >= _shown.Count) return;
        CommitDetail();
        _grid.ClearSelection();
        _grid.CurrentCell = _grid[_colOriginal.Index, index];
        _grid.Rows[index].Selected = true;
        if (focusBox) _translationBox.Focus();
    }

    private int CurrentIndex() => _grid.CurrentRow?.Index ?? -1;

    private void MoveRow(int delta, bool focusBox = false)
    {
        if (_shown.Count == 0) return;
        SelectRow(Math.Clamp(CurrentIndex() + delta, 0, _shown.Count - 1), focusBox);
    }

    private void GoToNextUntranslated()
    {
        if (_shown.Count == 0) return;
        CommitDetail();
        int start = CurrentIndex();
        for (int step = 1; step <= _shown.Count; step++)
        {
            int index = (start + step) % _shown.Count;
            RowRef rowRef = _shown[index];
            if (!rowRef.IsTranslated && TranslationChecker.HasJapanese(rowRef.Original))
            {
                SelectRow(index, focusBox: true);
                return;
            }
        }
        _statusLabel.Text = "No untranslated rows in the list.";
    }

    private void GoToIssue(bool backwards)
    {
        if (_shown.Count == 0) return;
        CommitDetail();
        int start = CurrentIndex();
        for (int step = 1; step <= _shown.Count; step++)
        {
            int index = ((start + (backwards ? -step : step)) % _shown.Count + _shown.Count) % _shown.Count;
            if (_shown[index].Issues.Count > 0)
            {
                SelectRow(index);
                return;
            }
        }
        _statusLabel.Text = "No rows with notes in the list.";
    }

    private void GoToId()
    {
        string? text = Prompt.Ask(this, "Go to ID", "ID or entry name (or part of it):", "");
        if (string.IsNullOrWhiteSpace(text)) return;
        text = text.Trim();
        CommitDetail();

        int start = CurrentIndex();
        for (int step = 1; step <= _shown.Count; step++)
        {
            int index = ((start + step) % _shown.Count + _shown.Count) % _shown.Count;
            MessageRow row = _shown[index].Row;
            if (row.Id.Contains(text, StringComparison.OrdinalIgnoreCase) || row.Key.Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                SelectRow(index);
                return;
            }
        }
        _statusLabel.Text = $"\"{text}\" is not in the rows shown. Use Search all files to look in every pack.";
    }

    private void ShowHelp()
    {
        MessageBox.Show(this,
            "Ctrl+C (in the grid)    copy the original text of the selected rows\n" +
            "Ctrl+Shift+C            copy the translations\n" +
            "Ctrl+Z / Ctrl+Y         undo / redo translation changes (outside text boxes)\n" +
            "Ctrl+F                  filter box        Ctrl+Shift+F   search all files\n" +
            "Ctrl+H                  Find and Replace  F3 / Shift+F3  next / previous match\n" +
            "Ctrl+G                  go to an ID\n" +
            "Ctrl+S                  save the open file as a translated .pak\n" +
            "Ctrl+Shift+L            auto format: break the translation into lines like the original\n" +
            "Ctrl+Enter              next row, cursor in the translation box\n" +
            "Alt+Up / Alt+Down       previous / next row\n" +
            "Ctrl+PageUp / PageDown  previous / next file\n" +
            "Ctrl+U                  next untranslated row\n" +
            "F7                      show rows with notes     F8 / Shift+F8   next / previous such row\n" +
            "Enter (in the grid)     edit the translation in the box below\n\n" +
            "Translations are stored per file name (relative to the opened folder) and ID, so open the same folder each time.",
            "Messages tab shortcuts", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
