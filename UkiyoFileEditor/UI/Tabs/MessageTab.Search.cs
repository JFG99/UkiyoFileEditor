using System.Text.RegularExpressions;

namespace UkiyoFileEditor.UI.Tabs;

/// <summary>Searching across files, Find / Replace, and background loading of packs.</summary>
public sealed partial class MessageTab
{
    private FindReplaceForm? _findForm;
    private SearchSpec _lastSpec = new();

    // ---- Loading many packs ---------------------------------------------------
    /// <summary>Reads the given packs on a background thread (results are cached by path and timestamp).</summary>
    private async Task<List<LoadedPack>> LoadPacksAsync(IReadOnlyList<PackFile> files, string verb)
    {
        var progress = new Progress<int>(completedCount => _statusLabel.Text = $"{verb}… {completedCount:N0} / {files.Count:N0} file(s)");
        return await Task.Run(() =>
        {
            var list = new List<LoadedPack>(files.Count);
            for (int index = 0; index < files.Count; index++)
            {
                try
                {
                    list.Add(new LoadedPack(files[index], LoadPackCached(files[index]), null));
                }
                catch (Exception exception)
                {
                    list.Add(new LoadedPack(files[index], null, exception.Message));
                }
                if (index % 25 == 0) ((IProgress<int>)progress).Report(index + 1);
            }
            return list;
        });
    }

    // ---- Search all files ------------------------------------------------------
    private async Task SearchAllFilesAsync(SearchSpec spec)
    {
        if (_busy) return;
        if (spec.IsEmpty)
        {
            _statusLabel.Text = "Type something to search for first.";
            return;
        }
        if (!spec.TryBuild(out Regex? built, out string error) || built == null)
        {
            _statusLabel.Text = "Search: " + error;
            return;
        }
        Regex regex = built;

        List<PackFile> files = _fileList.Items.Cast<PackFile>().ToList();
        if (files.Count == 0)
        {
            _statusLabel.Text = "No files to search.";
            return;
        }

        CommitDetail();
        SetBusy(true);
        try
        {
            List<LoadedPack> loaded = await LoadPacksAsync(files, "Searching");

            // Matching happens here, on the UI thread, because it reads the translation store.
            var hits = new List<RowRef>();
            int failed = 0;
            bool truncated = false;
            foreach (LoadedPack loadedPack in loaded)
            {
                if (loadedPack.Pack == null) { failed++; continue; }
                string file = loadedPack.File.Display;
                for (int index = 0; index < loadedPack.Pack.Rows.Count; index++)
                {
                    MessageRow row = loadedPack.Pack.Rows[index];
                    string? stored = _store.Get(file, row);
                    string translation = stored == null ? "" : TextTools.Norm(stored);
                    if (!spec.Matches(regex, row.Id, row.Key, row.Text, translation)) continue;

                    var rowRef = new RowRef(file, row, index);
                    RefreshRef(rowRef);
                    hits.Add(rowRef);
                    if (hits.Count >= MaxShownRows) { truncated = true; break; }
                }
                if (truncated) break;
            }

            _resultsMode = true;
            _source = hits;
            _containerText = "";
            _resultsText = $"Search results for \"{spec.Find}\"  |  {hits.Count:N0} match(es)" + (truncated ? " (list cut off)" : "") +
                           "  |  double-click a row to open its file";
            SetFilterFromSpec(spec);
            _showBox.SelectedIndex = 0;
            ApplyView();

            _statusLabel.Text = $"{hits.Count:N0} match(es) in {files.Count - failed:N0} file(s)" +
                           (failed > 0 ? $"; {failed:N0} file(s) could not be read as message packs" : "") + ".";
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ---- Find and Replace window ------------------------------------------------
    private void ShowFind()
    {
        if (_findForm == null || _findForm.IsDisposed)
        {
            _findForm = new FindReplaceForm(_lastSpec.IsEmpty ? FilterSpec() : _lastSpec);
            _findForm.FindRequested += (spec, backwards) => { _lastSpec = spec; FindStep(backwards); };
            _findForm.FindAllRequested += (spec, allFiles) => FindAll(spec, allFiles);
            _findForm.ReplaceRequested += spec => ReplaceCurrent(spec);
            _findForm.ReplaceAllRequested += (spec, allFiles) => ReplaceAll(spec, allFiles);
            _findForm.Owner = FindForm();
        }

        string seed = _origBox.SelectionLength > 0 ? _origBox.SelectedText
                    : _translationBox.SelectionLength > 0 ? _translationBox.SelectedText
                    : "";
        _findForm.Show();
        _findForm.Activate();
        _findForm.Prime(seed);
    }

    private bool TryRegex(SearchSpec spec, out Regex regex)
    {
        regex = null!;
        if (spec.IsEmpty)
        {
            _statusLabel.Text = "Type something to find first.";
            return false;
        }
        if (!spec.TryBuild(out Regex? built, out string error) || built == null)
        {
            _statusLabel.Text = "Search: " + error;
            _findForm?.SetMessage(error);
            return false;
        }
        regex = built;
        return true;
    }

    private void FindAll(SearchSpec spec, bool allFiles)
    {
        _lastSpec = spec;
        if (!TryRegex(spec, out _)) return;
        if (allFiles)
        {
            _ = SearchAllFilesAsync(spec);
            return;
        }
        SetFilterFromSpec(spec);
        ApplyView();
        _findForm?.SetMessage($"{_shown.Count:N0} row(s) match in the list.");
    }

    /// <summary>Selects the next (or previous) row of the list that matches. Wraps around.</summary>
    private void FindStep(bool backwards)
    {
        SearchSpec spec = _lastSpec.IsEmpty ? FilterSpec() : _lastSpec;
        if (spec.IsEmpty)
        {
            ShowFind();
            return;
        }
        if (!TryRegex(spec, out Regex regex)) return;
        if (_shown.Count == 0)
        {
            _statusLabel.Text = "No rows to search.";
            return;
        }

        CommitDetail();
        int start = CurrentIndex();
        for (int step = 1; step <= _shown.Count; step++)
        {
            int index = ((start + (backwards ? -step : step)) % _shown.Count + _shown.Count) % _shown.Count;
            RowRef rowRef = _shown[index];
            if (!spec.Matches(regex, rowRef.Row.Id, rowRef.Row.Key, rowRef.Original, rowRef.Translation)) continue;

            SelectRow(index);
            HighlightMatch(spec, regex);
            string found = $"Match in row {index + 1:N0} of {_shown.Count:N0}.";
            _statusLabel.Text = found;
            _findForm?.SetMessage(found);
            return;
        }

        _statusLabel.Text = $"\"{spec.Find}\" was not found in the rows shown.";
        _findForm?.SetMessage("Not found in the rows shown.");
    }

    private void HighlightMatch(SearchSpec spec, Regex regex)
    {
        if (spec.InTranslation && _translationBox.Enabled)
        {
            Match match = regex.Match(_translationBox.Text);
            if (match.Success && match.Length > 0)
            {
                _translationBox.Select(match.Index, match.Length);
                return;
            }
        }
        if (spec.InOriginal)
        {
            Match match = regex.Match(_origBox.Text);
            if (match.Success && match.Length > 0) _origBox.Select(match.Index, match.Length);
        }
    }

    /// <summary>Replaces in the translation of the current row, then moves to the next match.</summary>
    private void ReplaceCurrent(SearchSpec spec)
    {
        _lastSpec = spec;
        if (!spec.InTranslation)
        {
            _findForm?.SetMessage("Replace only changes translations: set the field to All or Translation.");
            return;
        }
        if (!TryRegex(spec, out Regex regex)) return;

        CommitDetail();
        RowRef? rowRef = CurrentRef();
        if (rowRef != null && rowRef.IsTranslated && regex.IsMatch(rowRef.Translation))
        {
            string updated = regex.Replace(rowRef.Translation, spec.ReplacementPattern);
            SetTranslation(rowRef, updated, "Replace", coalesce: false);
            _grid.InvalidateRow(CurrentIndex());
        }
        FindStep(false);
    }

    /// <summary>Replace All. "Current file" means the rows of the list on screen (the open file, or the search results); "All files" works on every stored translation.</summary>
    private void ReplaceAll(SearchSpec spec, bool allFiles)
    {
        _lastSpec = spec;
        if (!spec.InTranslation)
        {
            _findForm?.SetMessage("Replace only changes translations: set the field to All or Translation.");
            return;
        }
        if (!TryRegex(spec, out Regex regex)) return;

        CommitDetail();
        string pattern = spec.ReplacementPattern;
        var changes = new List<TranslationChange>();
        int replacements = 0;

        try
        {
            if (allFiles)
            {
                var items = _store.AllItems();
                int rowsHit = items.Count(hit => regex.IsMatch(hit.Item.Text));
                if (rowsHit == 0)
                {
                    _findForm?.SetMessage("Nothing to replace.");
                    return;
                }
                if (MessageBox.Show(this,
                        $"Replace in {rowsHit:N0} stored translation(s) of ALL files?\n\nThis can be undone with Ctrl+Z.",
                        "Replace All", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

                foreach (var (file, key, item) in items)
                {
                    MatchCollection matches = regex.Matches(item.Text);
                    if (matches.Count == 0) continue;
                    TranslationChange? change = _store.Set(file, key, item.Source, regex.Replace(item.Text, pattern));
                    if (change == null) continue;
                    changes.Add(change);
                    replacements += matches.Count;
                }
            }
            else
            {
                foreach (RowRef rowRef in _source)
                {
                    if (!rowRef.IsTranslated) continue;
                    MatchCollection matches = regex.Matches(rowRef.Translation);
                    if (matches.Count == 0) continue;
                    TranslationChange? change = _store.Set(rowRef.File, rowRef.Key, rowRef.Row.Text, regex.Replace(rowRef.Translation, pattern));
                    if (change == null) continue;
                    changes.Add(change);
                    replacements += matches.Count;
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            _findForm?.SetMessage("The pattern took too long.");
        }

        if (changes.Count == 0)
        {
            _findForm?.SetMessage("Nothing to replace.");
            return;
        }

        _undo.Push("Replace All", changes);
        AfterBulkChange(changes);
        string done = $"Replaced {replacements:N0} occurrence(s) in {changes.Count:N0} translation(s). Ctrl+Z undoes it.";
        _statusLabel.Text = done;
        _findForm?.SetMessage(done);
    }
}
