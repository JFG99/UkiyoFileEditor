namespace UkiyoFileEditor.UI.Tabs;

/// <summary>
/// Reads translations back out of packs that were saved earlier. A saved pack "x.pak" has its Japanese original in "x.pak.old";
/// every row whose text differs between the two is a translation.
/// </summary>
public sealed partial class MessageTab
{
    private async Task ReadBackSavedPacksAsync()
    {
        if (_busy) return;
        CommitDetail();

        List<PackFile> chosen = _fileList.SelectedItems.Count > 1
            ? _fileList.SelectedItems.Cast<PackFile>().ToList()
            : _fileList.Items.Cast<PackFile>().ToList();
        List<PackFile> files = chosen.Where(packFile => File.Exists(packFile.FullPath + ".old")).ToList();
        if (files.Count == 0)
        {
            _statusLabel.Text = "None of the files has a .old original, so there is no saved translation to read back.";
            return;
        }

        DialogResult mode = MessageBox.Show(this,
            $"Compare {files.Count:N0} saved pack(s) with their .old originals and take every changed row as a translation.\n\n" +
            "Yes = only fill rows that have no translation stored yet\nNo = replace stored translations too\nCancel = stop",
            "Read translations back from saved packs", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (mode == DialogResult.Cancel) return;
        bool onlyEmpty = mode == DialogResult.Yes;

        var progress = new Progress<int>(completedCount => _statusLabel.Text = $"Reading saved packs… {completedCount:N0} / {files.Count:N0}");
        List<ReferenceCandidate> candidates = new();
        var errors = new List<string>();

        SetBusy(true);
        try
        {
            (candidates, errors) = await Task.Run(() => CollectSavedTranslations(files, progress));
        }
        finally
        {
            SetBusy(false);
        }

        if (candidates.Count == 0)
        {
            var stored = _store.FileKeys();
            MessageBox.Show(this,
                $"Compared {files.Count - errors.Count:N0} saved pack(s) with their .old files and found no row whose text differs.\n\n" +
                "That means the saved .pak files still contain the Japanese text (nothing was written into them), " +
                "or the .old file is itself a translated copy.\n\n" +
                $"The translation store holds {_store.Count:N0} translation(s) in {stored.Count:N0} file(s)" +
                (stored.Count > 0 ? ", for example:\n   " + string.Join("\n   ", stored.Take(3)) : ".") +
                (files.Count > 0 ? $"\n\nThe first pack compared was:\n   {files[0].Display}" : ""),
                "Nothing to read back", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _statusLabel.Text = "Nothing differs between the saved packs and their .old originals.";
            return;
        }

        var changes = new List<TranslationChange>();
        int kept = 0;
        foreach (ReferenceCandidate candidate in candidates)
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
            _undo.Push("Read back saved packs", changes);
            AfterBulkChange(changes);
        }

        _statusLabel.Text = $"Read {changes.Count:N0} translation(s) from {files.Count - errors.Count:N0} saved pack(s)" +
                       (kept > 0 ? $"; {kept:N0} existing kept" : "") +
                       (errors.Count > 0 ? $"; {errors.Count:N0} could not be read" : "") + ". Ctrl+Z undoes it.";
        if (errors.Count > 0)
            MessageBox.Show(this, string.Join("\n", errors.Take(15)), "Some packs could not be read", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    /// <summary>
    /// Runs after a folder is opened: every file with a .old original gets the rows that have no stored translation filled from
    /// the saved pack, so saved work shows up without any menu command.
    /// </summary>
    private async Task AutoReadBackAllAsync()
    {
        if (_busy) return;
        // Every saved pack is checked, but only rows without a stored translation are filled, so nothing you typed is touched.
        List<PackFile> files = _allFiles.Where(packFile => packFile.HasBackup).ToList();
        if (files.Count == 0) return;

        var progress = new Progress<int>(completedCount => _statusLabel.Text = $"Reading saved translations… {completedCount:N0} / {files.Count:N0} pack(s)");
        List<ReferenceCandidate> candidates;
        List<string> errors;

        SetBusy(true);
        try
        {
            (candidates, errors) = await Task.Run(() => CollectSavedTranslations(files, progress));
        }
        finally
        {
            SetBusy(false);
        }

        var changes = new List<TranslationChange>();
        foreach (ReferenceCandidate candidate in candidates)
        {
            // A translation typed in the meantime wins.
            if (_store.Find(candidate.File, candidate.Key) != null) continue;
            TranslationChange? change = _store.Set(candidate.File, candidate.Key, candidate.Source, candidate.Text);
            if (change != null) changes.Add(change);
        }
        if (changes.Count > 0) AfterBulkChange(changes);

        _statusLabel.Text = changes.Count > 0
            ? $"Read {changes.Count:N0} translation(s) from {files.Count - errors.Count:N0} saved pack(s). {_store.Count:N0} stored translation(s) in all."
            : _statusLabel.Text;
        if (changes.Count > 0) FillFileList();
    }

    /// <summary>Worker-thread part: no access to the store or to controls.</summary>
    private (List<ReferenceCandidate>, List<string>) CollectSavedTranslations(List<PackFile> files, IProgress<int> progress)
    {
        var candidates = new List<ReferenceCandidate>();
        var errors = new List<string>();

        for (int index = 0; index < files.Count; index++)
        {
            PackFile packFile = files[index];
            if (index % 25 == 0) progress.Report(index + 1);
            try
            {
                MessagePack original = LoadPackCached(packFile); // reads the .old
                MessagePack saved = MessagePack.Load(File.ReadAllBytes(packFile.FullPath));

                var savedByKey = new Dictionary<string, string>();
                foreach (MessageRow savedRow in saved.Rows) savedByKey.TryAdd(TranslationStore.KeyOf(savedRow), savedRow.Text);
                bool sameCount = original.Rows.Count == saved.Rows.Count;

                for (int rowIndex = 0; rowIndex < original.Rows.Count; rowIndex++)
                {
                    MessageRow messageRow = original.Rows[rowIndex];
                    if (messageRow.Text.Trim().Length == 0) continue;

                    string key = TranslationStore.KeyOf(messageRow);
                    string? text = null;
                    if (savedByKey.TryGetValue(key, out string? byKey)) text = byKey;
                    else if (sameCount) text = saved.Rows[rowIndex].Text;

                    if (text == null || text.Trim().Length == 0) continue;
                    text = TextTools.Norm(text);
                    // Unchanged text that still contains Japanese was never translated. Unchanged text without Japanese (dots,
                    // numbers, English) is a finished line and is shown as the translation too.
                    if (text == TextTools.Norm(messageRow.Text) && TranslationChecker.HasJapanese(messageRow.Text)) continue;

                    candidates.Add(new ReferenceCandidate(packFile.Display, key, messageRow.Text, text));
                }
            }
            catch (Exception exception)
            {
                errors.Add($"{packFile.Display}: {exception.Message}");
            }
        }
        return (candidates, errors);
    }
}
