namespace UkiyoFileEditor.UI.Tabs;

/// <summary>Writing the translated packs back to disk (original kept as "name.pak.old").</summary>
public sealed partial class MessageTab
{
    /// <summary>Snapshot of the stored translations, per file, for the background thread.</summary>
    private Dictionary<string, Dictionary<string, TranslationItem>> SnapshotStore()
    {
        var result = new Dictionary<string, Dictionary<string, TranslationItem>>();
        foreach (var (file, key, item) in _store.AllItems())
        {
            if (!result.TryGetValue(file, out Dictionary<string, TranslationItem>? items))
                result[file] = items = new Dictionary<string, TranslationItem>();
            items[key] = item;
        }
        return result;
    }

    private async Task SavePacksAsync(List<PackFile> candidates, bool confirm)
    {
        if (_busy) return;
        CommitDetail();
        FlushSave();

        List<PackFile> files = candidates.Where(packFile => _store.HasAny(packFile.Display)).ToList();
        if (files.Count == 0)
        {
            _statusLabel.Text = "None of those files has translations to save.";
            return;
        }

        if (confirm && MessageBox.Show(this,
                $"Rebuild {files.Count:N0} pack(s) that have translations?\n\nEach one is written under its own name, and the original is kept as \"name.pak.old\" " +
                "(an existing .old is never overwritten).",
                "Save translated packs", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

        var snapshot = SnapshotStore();
        var firstSave = files.Where(packFile => !File.Exists(packFile.FullPath + ".old")).Select(packFile => packFile.Display).ToHashSet();
        var progress = new Progress<int>(completedCount => _statusLabel.Text = $"Saving… {completedCount:N0} / {files.Count:N0} pack(s)");

        SetBusy(true);
        try
        {
            (int saved, int replacedTotal, int nothing, List<string> errors) = await Task.Run(() =>
            {
                int savedCount = 0, entries = 0, none = 0;
                var failed = new List<string>();
                for (int index = 0; index < files.Count; index++)
                {
                    PackFile packFile = files[index];
                    try
                    {
                        byte[]? bytes = TranslatedPackBuilder.Build(packFile.OriginalPath, snapshot[packFile.Display], out int replaced);
                        if (bytes == null) { none++; }
                        else
                        {
                            PackBackupWriter.Write(packFile.FullPath, bytes);
                            savedCount++;
                            entries += replaced;
                        }
                    }
                    catch (Exception exception)
                    {
                        failed.Add($"{packFile.Display}: {exception.Message}");
                    }
                    ((IProgress<int>)progress).Report(index + 1);
                }
                return (savedCount, entries, none, failed);
            });

            foreach (PackFile packFile in files) _packCache.TryRemove(packFile.FullPath, out _);
            RefreshFileCounts(backups: true);
            if (_openFile != null) UpdateInfo();
            if (saved > 0) PacksChanged?.Invoke(this, EventArgs.Empty);

            string note = firstSave.Count > 0 ? "; the originals are kept as .old" : "; the existing .old originals were kept";
            _statusLabel.Text = saved == 1 && files.Count == 1
                ? $"Saved {files[0].Display} with {replacedTotal:N0} translated entr{(replacedTotal == 1 ? "y" : "ies")}{note}."
                : $"Saved {saved:N0} pack(s), {replacedTotal:N0} translated entries{note}" +
                  (nothing > 0 ? $"; {nothing:N0} had nothing to save" : "") + (errors.Count > 0 ? $"; {errors.Count:N0} failed" : "") + ".";

            if (errors.Count > 0)
            {
                MessageBox.Show(this, string.Join("\n", errors.Take(15)) + (errors.Count > 15 ? "\n…" : ""), "Some packs were not saved",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task SaveCurrentPackAsync()
    {
        if (_openFile == null)
        {
            _statusLabel.Text = "Select a pack in the list first.";
            return;
        }
        await SavePacksAsync(new List<PackFile> { _openFile }, confirm: false);
    }

    private async Task SaveSelectedPacksAsync()
    {
        List<PackFile> files = _fileList.SelectedItems.Cast<PackFile>().ToList();
        if (files.Count == 0)
        {
            _statusLabel.Text = "Select one or more files in the list first.";
            return;
        }
        await SavePacksAsync(files, confirm: files.Count > 1);
    }

    private async Task SaveAllPacksAsync()
    {
        List<PackFile> files = _fileList.Items.Cast<PackFile>().ToList();
        await SavePacksAsync(files, confirm: true);
    }

    /// <summary>Puts the original (.old) back over the translated pack for the selected files. The translations stay in the store.</summary>
    private Task RestoreSelectedAsync()
    {
        if (_busy) return Task.CompletedTask;
        List<PackFile> files = _fileList.SelectedItems.Cast<PackFile>().Where(packFile => File.Exists(packFile.FullPath + ".old")).ToList();
        if (files.Count == 0)
        {
            _statusLabel.Text = "None of the selected files has a .old original.";
            return Task.CompletedTask;
        }
        if (MessageBox.Show(this,
                $"Put the original back for {files.Count:N0} file(s)?\n\nThe translated .pak is replaced by the .old file (which then disappears). " +
                "Your translations stay stored here and can be saved again.",
                "Restore originals", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return Task.CompletedTask;

        int restored = 0;
        var errors = new List<string>();
        foreach (PackFile packFile in files)
        {
            try
            {
                File.Move(packFile.FullPath + ".old", packFile.FullPath, overwrite: true);
                _packCache.TryRemove(packFile.FullPath, out _);
                _packCache.TryRemove(packFile.FullPath + ".old", out _);
                restored++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{packFile.Display}: {exception.Message}");
            }
        }

        RefreshFileCounts(backups: true);
        if (_openFile != null) OpenFile(_openFile);
        if (restored > 0) PacksChanged?.Invoke(this, EventArgs.Empty);
        _statusLabel.Text = $"Restored {restored:N0} original file(s)" + (errors.Count > 0 ? $"; {errors.Count:N0} failed" : "") + ".";
        if (errors.Count > 0)
            MessageBox.Show(this, string.Join("\n", errors.Take(15)), "Some files were not restored", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return Task.CompletedTask;
    }
}
