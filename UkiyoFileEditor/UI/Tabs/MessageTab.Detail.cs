using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace UkiyoFileEditor.UI.Tabs;

/// <summary>The detail area below the grid (original / translation boxes), undo and redo.</summary>
public sealed partial class MessageTab
{
    // ---- Detail area ------------------------------------------------------------
    private RowRef? CurrentRef()
    {
        if (_grid.CurrentRow == null) return null;
        int index = _grid.CurrentRow.Index;
        return index >= 0 && index < _shown.Count ? _shown[index] : null;
    }

    private List<RowRef> SelectedRefs() =>
        _grid.SelectedRows.Cast<DataGridViewRow>()
             .Select(gridRow => gridRow.Index)
             .Where(rowIndex => rowIndex >= 0 && rowIndex < _shown.Count)
             .OrderBy(rowIndex => rowIndex)
             .Select(rowIndex => _shown[rowIndex])
             .ToList();

    private void ShowDetail()
    {
        RowRef? rowRef = CurrentRef();
        CommitDetail();
        _detailRef = rowRef;

        _suppressBox = true;
        if (rowRef == null)
        {
            _origBox.Text = "";
            _translationBox.Text = "";
            _translationBox.Enabled = false;
        }
        else
        {
            _origBox.Text = TextTools.ToBox(rowRef.Original);
            _translationBox.Text = TextTools.ToBox(rowRef.Translation);
            _translationBox.Enabled = true;
        }
        _suppressBox = false;
        _detailDirty = false;

        _origLabel.Text = rowRef == null ? "Original" : $"Original — {(rowRef.Row.Id.Length > 0 ? rowRef.Row.Id : rowRef.Row.Key)}  ({LengthOf(rowRef.Original)} chars)";
        UpdateIssueLabel(live: false);
    }

    private void UpdateIssueLabel(bool live)
    {
        RowRef? rowRef = _detailRef;
        if (rowRef == null)
        {
            _trLabel.Text = "Translation";
            _issueLabel.Text = "";
            return;
        }

        string text = live ? TextTools.FromBox(_translationBox.Text) : rowRef.Translation;
        List<Issue> issues = live ? TranslationChecker.Check(rowRef.Original, text, _check) : rowRef.Issues;
        string lines = text.Length == 0
            ? "not translated yet"
            : "line lengths " + string.Join(" / ", text.Split('\n').Select(line => TextTools.VisibleLength(line, _check.Icon))) +
              (_check.MaxLineChars > 0 ? $"  (max {_check.MaxLineChars})" : "");
        _trLabel.Text = $"Translation — {lines}";
        _issueLabel.Text = string.Join("   •   ", issues.Select(issue => issue.Message));
        _issueLabel.ForeColor = issues.Any(issue => issue.Severity == Severity.Warning) ? Color.DarkRed : Color.DarkGoldenrod;
    }

    /// <summary>Stores what is typed in the translation box (called after a pause, when leaving the box, and before anything that reads the store).</summary>
    private void CommitDetail()
    {
        _commitTimer.Stop();
        if (!_detailDirty || _detailRef == null) return;
        _detailDirty = false;
        SetTranslation(_detailRef, TextTools.FromBox(_translationBox.Text));
    }

    /// <summary>The one place a single translation is changed: store, undo step, check results, display.</summary>
    private void SetTranslation(RowRef rowRef, string text, string undoName = "Edit translation", bool coalesce = true)
    {
        TranslationChange? change = _store.Set(rowRef.File, rowRef.Key, rowRef.Row.Text, text);
        if (change == null) return;

        _undo.Push(undoName, new List<TranslationChange> { change }, coalesce ? rowRef.File + "|" + rowRef.Key : null);
        RefreshRef(rowRef);
        StoreChanged();

        if (ReferenceEquals(rowRef, _detailRef))
        {
            if (TextTools.FromBox(_translationBox.Text) != rowRef.Translation)
            {
                _suppressBox = true;
                _translationBox.Text = TextTools.ToBox(rowRef.Translation);
                _suppressBox = false;
                _detailDirty = false;
            }
            UpdateIssueLabel(live: false);
        }
    }

    /// <summary>Call after the store changed: refreshes counts and schedules the save to disk.</summary>
    private void StoreChanged()
    {
        _storeDirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
        RefreshFileCounts();
        _grid.Invalidate();
        UpdateInfo();
    }

    /// <summary>Writes the translations to disk now if anything changed. Also called when the window closes.</summary>
    public void FlushSave()
    {
        _saveTimer.Stop();
        CommitDetail();
        if (!_storeDirty) return;
        _storeDirty = false;
        _store.Save();
    }

    /// <summary>After changes made behind the grid's back (undo, replace, import): refresh the touched rows.</summary>
    private void AfterBulkChange(IEnumerable<TranslationChange> changes)
    {
        var touched = new HashSet<(string, string)>();
        foreach (TranslationChange change in changes) touched.Add((change.File, change.Key));
        foreach (RowRef rowRef in _source)
        {
            if (touched.Contains((rowRef.File, rowRef.Key))) RefreshRef(rowRef);
        }

        if (_detailRef != null)
        {
            _suppressBox = true;
            _translationBox.Text = TextTools.ToBox(_detailRef.Translation);
            _suppressBox = false;
            _detailDirty = false;
            UpdateIssueLabel(live: false);
        }
        StoreChanged();
    }

    private void DoUndo()
    {
        CommitDetail();
        string? name = _undo.UndoName;
        List<TranslationChange>? changes = _undo.Undo(_store);
        if (changes == null) { _statusLabel.Text = "Nothing to undo."; return; }
        AfterBulkChange(changes);
        _statusLabel.Text = $"Undid: {name}.";
    }

    private void DoRedo()
    {
        CommitDetail();
        string? name = _undo.RedoName;
        List<TranslationChange>? changes = _undo.Redo(_store);
        if (changes == null) { _statusLabel.Text = "Nothing to redo."; return; }
        AfterBulkChange(changes);
        _statusLabel.Text = $"Redid: {name}.";
    }

    // ---- Detail buttons -----------------------------------------------------------
    private void CopyOriginalOfDetail()
    {
        if (_detailRef == null) return;
        SetClipboard(TextTools.ToBox(_detailRef.Original));
    }

    private void StartFromOriginal()
    {
        if (_detailRef == null) return;
        _translationBox.Text = TextTools.ToBox(_detailRef.Original);
        _translationBox.Focus();
    }

    private void ClearDetailTranslation()
    {
        if (_detailRef == null) return;
        _commitTimer.Stop();
        _detailDirty = false;
        SetTranslation(_detailRef, "", "Clear translation", coalesce: false);
        _suppressBox = true;
        _translationBox.Text = "";
        _suppressBox = false;
    }

    /// <summary>Removes the stored translation of every selected row, as one undoable step.</summary>
    private void ClearSelectedTranslations()
    {
        CommitDetail();
        List<RowRef> translatedRows = SelectedRefs().Where(rowRef => rowRef.IsTranslated).ToList();
        if (translatedRows.Count == 0)
        {
            _statusLabel.Text = "The selected rows have no translation.";
            return;
        }

        DialogResult answer = MessageBox.Show(this,
            $"Remove the stored translation of {translatedRows.Count:N0} row(s)?\n\nCtrl+Z brings them back.",
            "Clear translations", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (answer != DialogResult.OK) return;

        var changes = new List<TranslationChange>();
        foreach (RowRef rowRef in translatedRows)
        {
            TranslationChange? change = _store.Remove(rowRef.File, rowRef.Key);
            if (change != null) changes.Add(change);
        }
        if (changes.Count == 0) return;

        _undo.Push("Clear translations", changes);
        AfterBulkChange(changes);
        _statusLabel.Text = $"Removed {changes.Count:N0} translation(s).";
    }

    /// <summary>The codes (tags) of the original, so one can be put into the translation at the cursor.</summary>
    private void ShowCodeMenu()
    {
        if (_detailRef == null) return;
        _codeMenu.Items.Clear();
        var seen = new HashSet<string>();
        foreach (Match match in TextTools.Tag.Matches(_detailRef.Original))
        {
            if (!seen.Add(match.Value)) continue;
            string code = match.Value;
            _codeMenu.Items.Add(code, null, (_, _) =>
            {
                _translationBox.Focus();
                _translationBox.SelectedText = code;
            });
        }
        if (_codeMenu.Items.Count == 0)
        {
            ToolStripItem none = _codeMenu.Items.Add("(the original has no codes)");
            none.Enabled = false;
        }
        _codeMenu.Show(_codeButton, 0, _codeButton.Height);
    }
}
