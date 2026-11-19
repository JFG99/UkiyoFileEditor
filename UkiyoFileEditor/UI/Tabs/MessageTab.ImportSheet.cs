using System.Text;
using System.Text.RegularExpressions;

namespace UkiyoFileEditor.UI.Tabs;

/// <summary>Importing translations from a CSV / TSV sheet.</summary>
public sealed partial class MessageTab
{
    private async Task ImportSheetAsync()
    {
        if (_busy) return;
        CommitDetail();

        using var dialog = new OpenFileDialog { Filter = "CSV / TSV files (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        List<SheetRow> sheet;
        int emptyTranslations;
        try
        {
            sheet = TranslationSheet.Read(dialog.FileName, _openFile?.Display ?? "", out emptyTranslations);
        }
        catch (InvalidDataException exception)
        {
            MessageBox.Show(this, exception.Message, "Import", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, "Cannot read the file", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (sheet.Count == 0)
        {
            _statusLabel.Text = "The sheet has no translations to import.";
            return;
        }

        DialogResult mode = MessageBox.Show(this,
            $"Import {sheet.Count:N0} translation(s)?\n\nYes = replace translations that already exist\nNo = only fill rows without a translation\nCancel = stop",
            "Import translations", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (mode == DialogResult.Cancel) return;
        bool overwrite = mode == DialogResult.Yes;

        var byDisplay = new Dictionary<string, PackFile>(StringComparer.OrdinalIgnoreCase);
        foreach (PackFile packFile in _allFiles) byDisplay.TryAdd(packFile.Display, packFile);

        // A sheet may name a file by its bare name or by a path relative to a different root than the folder that is
        // loaded now. Fall back to a unique match on the file name (or on the trailing path segments).
        var byName = _allFiles.GroupBy(packFile => Path.GetFileName(packFile.Display), StringComparer.OrdinalIgnoreCase)
                              .Where(group => group.Count() == 1)
                              .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        PackFile? Resolve(string name)
        {
            if (byDisplay.TryGetValue(name, out PackFile? exact)) return exact;
            string norm = name.Replace('/', '\\');
            PackFile? suffix = null;
            foreach (PackFile packFile in _allFiles)
            {
                if (packFile.Display.EndsWith("\\" + norm, StringComparison.OrdinalIgnoreCase) ||
                    norm.EndsWith("\\" + packFile.Display, StringComparison.OrdinalIgnoreCase))
                {
                    if (suffix != null) return byName.GetValueOrDefault(Path.GetFileName(norm));
                    suffix = packFile;
                }
            }
            return suffix ?? byName.GetValueOrDefault(Path.GetFileName(norm));
        }

        var groups = sheet.GroupBy(sheetRow => sheetRow.File, StringComparer.OrdinalIgnoreCase).ToList();
        var resolved = new Dictionary<string, PackFile>(StringComparer.OrdinalIgnoreCase);
        var needed = new List<PackFile>();
        int unknownFile = 0;
        foreach (var fileGroup in groups)
        {
            PackFile? known = Resolve(fileGroup.Key);
            if (known != null)
            {
                resolved[fileGroup.Key] = known;
                if (!needed.Contains(known)) needed.Add(known);
            }
            else unknownFile += fileGroup.Count();
        }

        var changes = new List<TranslationChange>();
        int missing = 0, kept = 0;
        SetBusy(true);
        try
        {
            List<LoadedPack> loaded = await LoadPacksAsync(needed, "Reading");
            var packs = new Dictionary<string, MessagePack>(StringComparer.OrdinalIgnoreCase);
            foreach (LoadedPack loadedPack in loaded)
            {
                if (loadedPack.Pack != null) packs[loadedPack.File.Display] = loadedPack.Pack;
            }

            foreach (var fileGroup in groups)
            {
                if (!resolved.TryGetValue(fileGroup.Key, out PackFile? packFile) || !packs.TryGetValue(packFile.Display, out MessagePack? pack)) continue;

                var rowsByKey = new Dictionary<string, MessageRow>();
                foreach (MessageRow packRow in pack.Rows) rowsByKey.TryAdd(TranslationStore.KeyOf(packRow), packRow);

                foreach (SheetRow sheetRow in fileGroup)
                {
                    if (!rowsByKey.TryGetValue(sheetRow.Key, out MessageRow? target))
                    {
                        missing++;
                        continue;
                    }
                    if (!overwrite && _store.Get(packFile.Display, target) != null)
                    {
                        kept++;
                        continue;
                    }
                    TranslationChange? change = _store.Set(packFile.Display, sheetRow.Key, target.Text, sheetRow.Translation);
                    if (change != null) changes.Add(change);
                }
            }
        }
        finally
        {
            SetBusy(false);
        }

        if (changes.Count > 0)
        {
            _undo.Push("Import from sheet", changes);
            AfterBulkChange(changes);
        }
        _statusLabel.Text = $"Imported {changes.Count:N0} translation(s)" +
                       (kept > 0 ? $"; {kept:N0} existing kept" : "") +
                       (missing > 0 ? $"; {missing:N0} ID(s) not found in their file" : "") +
                       (unknownFile > 0 ? $"; {unknownFile:N0} row(s) of files not in the list" : "") +
                       (emptyTranslations > 0 ? $"; {emptyTranslations:N0} empty translation(s) skipped" : "") + ".";
    }
}
