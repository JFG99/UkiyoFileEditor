using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace UkiyoFileEditor.UI.Tabs;

/// <summary>What the grid shows: columns, filters and the row list.</summary>
public sealed partial class MessageTab
{
    // ---- Grid contents -------------------------------------------------------
    private object CellText(RowRef rowRef, int column)
    {
        if (column == _colFile.Index) return rowRef.File;
        if (column == _colId.Index) return rowRef.Row.Id.Length > 0 ? rowRef.Row.Id : rowRef.Row.Key;
        if (column == _colOriginal.Index) return TextTools.ToBox(rowRef.Original);
        if (column == _colTranslation.Index) return TextTools.ToBox(rowRef.Translation);
        if (column == _colLength.Index)
            return rowRef.IsTranslated ? $"{LengthOf(rowRef.Original)}/{LengthOf(rowRef.Translation)}" : LengthOf(rowRef.Original).ToString();
        if (column == _colNote.Index) return string.Join("\n", rowRef.Issues.Select(issue => issue.Message));
        return "";
    }

    /// <summary>Characters of a text without codes and line breaks.</summary>
    private static int LengthOf(string text) =>
        text.Length == 0 ? 0 : TextTools.Tag.Replace(TextTools.Norm(text), "").Replace("\n", "").Length;

    private static int ParseInt(TextBox inputBox) => int.TryParse(inputBox.Text.Trim(), out int value) && value > 0 ? value : 0;

    private SearchSpec FilterSpec() => new()
    {
        Find = _searchBox.Text,
        MatchCase = _caseBox.Checked,
        WholeWord = _wordBox.Checked,
        UseRegex = _regexBox.Checked,
        Field = (SearchField)Math.Max(0, _fieldBox.SelectedIndex),
    };

    /// <summary>Puts a search into the filter bar (used by Find All of the Find and Replace window).</summary>
    private void SetFilterFromSpec(SearchSpec spec)
    {
        _searchBox.Text = spec.Find;
        _caseBox.Checked = spec.MatchCase;
        _wordBox.Checked = spec.WholeWord;
        _regexBox.Checked = spec.UseRegex;
        _fieldBox.SelectedIndex = (int)spec.Field;
        _filterTimer.Stop();
    }

    private void ApplyView()
    {
        _filterTimer.Stop();
        CommitDetail();
        RowRef? keep = _detailRef;
        IEnumerable<RowRef> rows = _source;

        _searchBox.BackColor = SystemColors.Window;
        if (_searchBox.Text.Length > 0)
        {
            SearchSpec spec = FilterSpec();
            if (spec.TryBuild(out Regex? built, out string error) && built != null)
            {
                Regex regex = built;
                rows = rows.Where(rowRef => spec.Matches(regex, rowRef.Row.Id, rowRef.Row.Key, rowRef.Original, rowRef.Translation));
            }
            else
            {
                _searchBox.BackColor = Color.MistyRose;
                _statusLabel.Text = "Filter: " + error;
            }
        }

        switch (_showBox.SelectedIndex)
        {
            case 1: rows = rows.Where(rowRef => !rowRef.IsTranslated); break;
            case 2: rows = rows.Where(rowRef => rowRef.IsTranslated); break;
            case 3: rows = rows.Where(rowRef => rowRef.Issues.Count > 0); break;
            case 4: rows = rows.Where(rowRef => rowRef.HasWarning); break;
        }

        int minLength = ParseInt(_minBox), maxLength = ParseInt(_maxBox);
        bool ofTranslation = _lenOfBox.SelectedIndex == 1;
        if (minLength > 0 || maxLength > 0)
        {
            rows = rows.Where(rowRef =>
            {
                int length = LengthOf(ofTranslation ? rowRef.Translation : rowRef.Original);
                return length >= minLength && (maxLength <= 0 || length <= maxLength);
            });
        }

        switch (_sortBox.SelectedIndex)
        {
            case 1: rows = rows.OrderByDescending(rowRef => LengthOf(ofTranslation ? rowRef.Translation : rowRef.Original)); break;
            case 2: rows = rows.OrderBy(rowRef => LengthOf(ofTranslation ? rowRef.Translation : rowRef.Original)); break;
        }

        _shown = rows.Take(MaxShownRows).ToList();

        _suppressSelection = true;
        _grid.RowCount = 0;
        _colFile.Visible = _resultsMode;
        _grid.RowCount = _shown.Count;
        _suppressSelection = false;

        int index = keep == null ? -1 : _shown.IndexOf(keep);
        if (index < 0 && _shown.Count > 0) index = 0;
        if (index >= 0)
        {
            _suppressSelection = true;
            _grid.ClearSelection();
            _grid.CurrentCell = _grid[_colOriginal.Index, index];
            _grid.Rows[index].Selected = true;
            _suppressSelection = false;
        }

        _detailRef = null;
        ShowDetail();
        UpdateInfo();
    }

    private void UpdateInfo()
    {
        if (_resultsMode || _openFile == null)
        {
            _infoLabel.Text = _resultsText;
            return;
        }

        int translated = _source.Count(rowRef => rowRef.IsTranslated);
        int notes = _source.Count(rowRef => rowRef.Issues.Count > 0);
        string filtered = _shown.Count == _source.Count ? "" : $"  |  showing {_shown.Count:N0}";
        _infoLabel.Text = $"{_openFile.Display}  |  {_containerText}  |  {_source.Count:N0} row(s), {translated:N0} translated" +
                          (notes > 0 ? $", {notes:N0} with notes" : "") + filtered +
                          (_resultsText.Length > 0 ? "  |  " + _resultsText : "");
    }
}
