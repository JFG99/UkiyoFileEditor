using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace UkiyoFileEditor.UI.Tabs;

/// <summary>Loading the folder, the file list, and opening a pack.</summary>
public sealed partial class MessageTab
{
    // ---- Folder / file handling ------------------------------------------
    public void LoadFolder(string folder, bool recursive)
    {
        _folder = folder;
        _recursive = recursive;
        Reload();
    }

    private void Reload()
    {
        if (string.IsNullOrEmpty(_folder)) return;

        CommitDetail();
        int skippedEmpty = 0;
        _allFiles.Clear();

        try
        {
            string pattern = string.IsNullOrWhiteSpace(_filterBox.Text) ? "*.pak" : _filterBox.Text.Trim();
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = _recursive,
                IgnoreInaccessible = true,
            };

            foreach (string filePath in Directory.EnumerateFiles(_folder, pattern, options).OrderBy(filePath => filePath, StringComparer.OrdinalIgnoreCase))
            {
                if (filePath.EndsWith(".old", StringComparison.OrdinalIgnoreCase)) continue;
                if (_hideEmpty.Checked)
                {
                    try
                    {
                        if (new FileInfo(filePath).Length == 0)
                        {
                            skippedEmpty++;
                            continue;
                        }
                    }
                    catch (IOException) { }
                }

                _allFiles.Add(new PackFile
                {
                    FullPath = filePath,
                    Display = Path.GetRelativePath(_folder, filePath),
                });
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Cannot read folder", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        FillFileList();
        OfferRekey();
        _statusLabel.Text = $"{_allFiles.Count} file(s) found" + (skippedEmpty > 0 ? $" ({skippedEmpty} empty file(s) hidden)" : "") + $".  {_store.Count:N0} stored translation(s).";

        // Saved packs whose translations are not in the store yet (saved by another tool or build) are read back automatically.
        _ = AutoReadBackAllAsync();
    }

    /// <summary>
    /// Translations are filed under the pack's path relative to the opened folder. When a different folder level was opened before,
    /// the stored paths no longer match; stored sets whose file name matches exactly one listed file are offered to be re-filed.
    /// </summary>
    private void OfferRekey()
    {
        if (_store.Count == 0 || _allFiles.Count == 0) return;

        var listed = new HashSet<string>(_allFiles.Select(packFile => packFile.Display), StringComparer.OrdinalIgnoreCase);
        var byName = _allFiles.GroupBy(packFile => Path.GetFileName(packFile.Display), StringComparer.OrdinalIgnoreCase)
                              .Where(group => group.Count() == 1)
                              .ToDictionary(group => group.Key, group => group.First().Display, StringComparer.OrdinalIgnoreCase);

        var renames = new List<(string Old, string New)>();
        foreach (string stored in _store.FileKeys())
        {
            if (listed.Contains(stored)) continue;
            if (!byName.TryGetValue(Path.GetFileName(stored), out string? target)) continue;
            if (_store.HasAny(target)) continue;
            renames.Add((stored, target));
        }
        if (renames.Count == 0) return;

        string sample = string.Join("\n", renames.Take(3).Select(rename => $"   {rename.Old}  →  {rename.New}"));
        if (MessageBox.Show(this,
                $"{renames.Count:N0} set(s) of stored translations are filed under a different folder path than the one you opened, " +
                $"but their file names match files in this list:\n\n{sample}\n\nRe-file them under the paths of this folder?",
                "Stored translations found", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        foreach (var (oldKey, newKey) in renames) _store.RenameFile(oldKey, newKey);
        _undo.Clear();
        _storeDirty = true;
        FlushSave();
        FillFileList();
    }

    private void FillFileList()
    {
        string? keep = _openFile?.Display;
        RefreshFileCounts(backups: true);

        _suppressFileEvent = true;
        _fileList.BeginUpdate();
        _fileList.Items.Clear();
        foreach (PackFile packFile in _allFiles)
        {
            if (_onlyTranslated.Checked && packFile.TranslatedCount == 0) continue;
            _fileList.Items.Add(packFile);
        }
        _fileList.EndUpdate();
        _suppressFileEvent = false;
        _fileCountLabel.Text = $"{_fileList.Items.Count} of {_allFiles.Count} file(s)";

        int index = -1;
        if (keep != null)
        {
            for (int listIndex = 0; listIndex < _fileList.Items.Count; listIndex++)
            {
                if (((PackFile)_fileList.Items[listIndex]).Display == keep) { index = listIndex; break; }
            }
        }

        if (index < 0 && _fileList.Items.Count > 0) index = 0;
        if (index >= 0)
        {
            _fileList.SelectedIndex = index; // opens the file through SelectedIndexChanged
        }
        else
        {
            _openFile = null;
            _source = new List<RowRef>();
            _containerText = "";
            _resultsMode = false;
            _resultsText = "";
            ApplyView();
        }
    }

    private void RefreshFileCounts(bool backups = false)
    {
        foreach (PackFile packFile in _allFiles)
        {
            packFile.TranslatedCount = _store.CountIn(packFile.Display);
            if (backups) packFile.HasBackup = File.Exists(packFile.FullPath + ".old");
        }
        _fileList.Invalidate();
    }

    private void OnFileSelectionChanged()
    {
        if (_suppressFileEvent) return;
        int count = _fileList.SelectedItems.Count;
        if (count == 1 && _fileList.SelectedItem is PackFile packFile)
        {
            OpenFile(packFile);
        }
        else if (count > 1)
        {
            _statusLabel.Text = $"{count} files selected. Use the Translate and Packs menus to work on all of them.";
        }
    }

    /// <summary>Selects a file of the list by its relative path. Returns false when it is not in the list.</summary>
    private bool SelectFileInList(string display)
    {
        for (int index = 0; index < _fileList.Items.Count; index++)
        {
            if (((PackFile)_fileList.Items[index]).Display != display) continue;
            _fileList.ClearSelected();
            _fileList.SelectedIndex = index;
            return true;
        }
        return false;
    }

    /// <summary>Reads a pack (the .old original when there is one). Safe to call from background threads.</summary>
    private MessagePack LoadPackCached(PackFile packFile)
    {
        string path = packFile.OriginalPath;
        var info = new FileInfo(path);
        if (_packCache.TryGetValue(path, out var entry) && entry.Stamp == info.LastWriteTimeUtc && entry.Length == info.Length)
            return entry.Pack;

        MessagePack pack = MessagePack.Load(File.ReadAllBytes(path));
        _packCache[path] = (info.LastWriteTimeUtc, info.Length, pack);
        return pack;
    }

    private void OpenFile(PackFile packFile)
    {
        CommitDetail();
        _openFile = packFile;
        _resultsMode = false;
        _resultsText = "";

        try
        {
            MessagePack pack = LoadPackCached(packFile);
            _containerText = $"{pack.Container}, {pack.EntryCount} entries" + (packFile.HasBackup ? ", original read from .old" : "");
            _source = MakeRefs(packFile.Display, pack);
        }
        catch (Exception exception)
        {
            _source = new List<RowRef>();
            _containerText = "";
            _resultsText = exception.Message;
            _statusLabel.Text = "Error reading " + packFile.Display;
        }

        ApplyView();
    }

    private List<RowRef> MakeRefs(string file, MessagePack pack)
    {
        var list = new List<RowRef>(pack.Rows.Count);
        for (int index = 0; index < pack.Rows.Count; index++)
        {
            var rowRef = new RowRef(file, pack.Rows[index], index);
            RefreshRef(rowRef);
            list.Add(rowRef);
        }
        return list;
    }

    private void RefreshRef(RowRef rowRef)
    {
        string? storedText = _store.Get(rowRef.File, rowRef.Row);
        rowRef.Translation = storedText == null ? "" : TextTools.Norm(storedText);
        rowRef.Issues = rowRef.IsTranslated ? TranslationChecker.Check(rowRef.Row.Text, rowRef.Translation, _check) : new List<Issue>();
    }

    private void MoveFile(int delta)
    {
        if (_fileList.Items.Count == 0) return;
        int current = _openFile == null ? -1 : _fileList.Items.IndexOf(_openFile);
        int index = Math.Clamp(Math.Max(0, current) + delta, 0, _fileList.Items.Count - 1);
        _fileList.ClearSelected();
        _fileList.SelectedIndex = index;
    }

    private void OpenResultFile(int rowIndex)
    {
        string file = _shown[rowIndex].File;
        string key = _shown[rowIndex].Key;
        if (!SelectFileInList(file))
        {
            _statusLabel.Text = $"{file} is not in the file list (check the file filter).";
            return;
        }
        int index = _shown.FindIndex(rowRef => rowRef.Key == key);
        if (index >= 0) SelectRow(index);
    }

    /// <summary>Opens a file and selects a row of it (used by the check list). Filters that hide the row are cleared.</summary>
    private void JumpTo(string file, string key)
    {
        CommitDetail();
        if (_openFile?.Display != file || _resultsMode)
        {
            if (!SelectFileInList(file))
            {
                _statusLabel.Text = $"{file} is not in the file list.";
                return;
            }
        }

        int index = _shown.FindIndex(rowRef => rowRef.Key == key);
        if (index < 0)
        {
            _searchBox.Text = "";
            _showBox.SelectedIndex = 0;
            _minBox.Text = "";
            _maxBox.Text = "";
            ApplyView();
            index = _shown.FindIndex(rowRef => rowRef.Key == key);
        }
        if (index >= 0) SelectRow(index);
    }
}
