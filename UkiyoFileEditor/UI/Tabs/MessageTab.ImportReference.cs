using System.Text;
using System.Text.RegularExpressions;

namespace UkiyoFileEditor.UI.Tabs;

/// <summary>Importing translations from a reference folder (for example the English message packs of another release).</summary>
public sealed partial class MessageTab
{
    private async Task ImportReferenceAsync()
    {
        if (_busy) return;
        CommitDetail();

        List<PackFile> files = _fileList.Items.Cast<PackFile>().ToList();
        if (files.Count == 0)
        {
            _statusLabel.Text = "There are no files in the list.";
            return;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = "Folder with the reference message packs (for example the English packs of another release)",
            UseDescriptionForTitle = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string reference = dialog.SelectedPath;

        DialogResult mode = MessageBox.Show(this,
            "Each pack of the list is paired with the pack of the same path (or the same file name) in that folder. Rows are matched by ID; " +
            "when an ID is missing and both packs have the same number of rows, by position.\n\n" +
            "Yes = only fill rows that have no translation yet\nNo = replace existing translations too\nCancel = stop",
            "Import from a reference folder", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (mode == DialogResult.Cancel) return;
        bool onlyEmpty = mode == DialogResult.Yes;

        ReferenceResult result;
        SetBusy(true);
        try
        {
            var progress = new Progress<int>(completedCount => _statusLabel.Text = $"Reading reference packs… {completedCount:N0} / {files.Count:N0}");
            result = await Task.Run(() => ReadReferencePacks(files, reference, progress));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, "Cannot read the reference folder", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            SetBusy(false);
        }

        var changes = new List<TranslationChange>();
        int kept = 0;
        foreach (ReferenceCandidate candidate in result.Candidates)
        {
            TranslationItem? existing = _store.Find(candidate.File, candidate.Key);
            if (onlyEmpty && existing != null && existing.Source == candidate.Source)
            {
                kept++;
                continue;
            }
            TranslationChange? change = _store.Set(candidate.File, candidate.Key, candidate.Source, candidate.Text);
            if (change != null) changes.Add(change);
        }

        if (changes.Count > 0)
        {
            _undo.Push("Import from reference folder", changes);
            AfterBulkChange(changes);
        }
        _statusLabel.Text = $"Imported {changes.Count:N0} translation(s) from {result.Matched:N0} file(s)" +
                       (result.ByIndex > 0 ? $" ({result.ByIndex:N0} matched by position)" : "") +
                       (kept > 0 ? $"; {kept:N0} existing kept" : "") +
                       (result.NoCounterpart > 0 ? $"; {result.NoCounterpart:N0} file(s) have no counterpart" : "") +
                       (result.Unreadable > 0 ? $"; {result.Unreadable:N0} could not be read" : "") + ". Ctrl+Z undoes it.";
    }

    /// <summary>Worker-thread part of the reference import. Does not touch the store or any control.</summary>
    private ReferenceResult ReadReferencePacks(List<PackFile> files, string reference, IProgress<int> progress)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        var byRelative = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFiles(reference, "*.pak", options))
        {
            if (path.EndsWith(".old", StringComparison.OrdinalIgnoreCase)) continue;
            byRelative.TryAdd(Path.GetRelativePath(reference, path), path);
            byName.TryAdd(Path.GetFileName(path), path);
        }

        var candidates = new List<ReferenceCandidate>();
        int matched = 0, noCounterpart = 0, unreadable = 0, byIndex = 0;

        for (int index = 0; index < files.Count; index++)
        {
            PackFile packFile = files[index];
            if (index % 25 == 0) progress.Report(index + 1);

            string? referencePath = null;
            if (byRelative.TryGetValue(packFile.Display, out string? exact)) referencePath = exact;
            else if (byName.TryGetValue(Path.GetFileName(packFile.Display), out string? sameName)) referencePath = sameName;
            if (referencePath == null)
            {
                noCounterpart++;
                continue;
            }

            try
            {
                MessagePack mine = LoadPackCached(packFile);
                MessagePack theirs = MessagePack.Load(File.ReadAllBytes(referencePath));

                var theirsByKey = new Dictionary<string, string>();
                foreach (MessageRow theirRow in theirs.Rows) theirsByKey.TryAdd(TranslationStore.KeyOf(theirRow), theirRow.Text);
                bool sameCount = mine.Rows.Count == theirs.Rows.Count;

                bool foundAny = false;
                for (int rowIndex = 0; rowIndex < mine.Rows.Count; rowIndex++)
                {
                    MessageRow messageRow = mine.Rows[rowIndex];
                    if (messageRow.Text.Trim().Length == 0) continue;

                    string key = TranslationStore.KeyOf(messageRow);
                    string? text = null;
                    bool positional = false;
                    if (theirsByKey.TryGetValue(key, out string? byKey)) text = byKey;
                    else if (sameCount)
                    {
                        text = theirs.Rows[rowIndex].Text;
                        positional = true;
                    }

                    if (text == null || text.Trim().Length == 0) continue;
                    text = TextTools.Norm(text);
                    if (text == TextTools.Norm(messageRow.Text)) continue; // the reference is not translated either

                    candidates.Add(new ReferenceCandidate(packFile.Display, key, messageRow.Text, text));
                    if (positional) byIndex++;
                    foundAny = true;
                }
                if (foundAny) matched++;
            }
            catch (Exception)
            {
                unreadable++;
            }
        }
        return new ReferenceResult(candidates, matched, noCounterpart, unreadable, byIndex);
    }
}
