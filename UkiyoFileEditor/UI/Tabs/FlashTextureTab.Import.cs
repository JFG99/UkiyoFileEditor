namespace UkiyoFileEditor.UI.Tabs;

/// <summary>Importing edited PNGs back into the _v packs.</summary>
public sealed partial class FlashTextureTab
{
    private const int MaxReportedNotes = 15;
    private const int MaxDisplayedWarningLines = 25;

    /// <summary>Running totals of an "Import all packs" run.</summary>
    private sealed class ImportTotals
    {
        public int Changed, Unchanged, Missing, WrongSize, Unreadable, NoFolder, PacksWritten, Failed;
        public readonly List<string> Errors = new();
        public readonly List<string> Notes = new();

        public void AddError(string packName, Exception exception)
        {
            Failed++;
            if (Errors.Count < MaxReportedErrors) Errors.Add($"{packName}: {exception.Message}");
        }
    }

    /// <summary>
    /// Reverse of "Export all packs": pick the parent folder that export created (one sub-folder per *_m.pak, each with one
    /// sub-folder per .srd). For every pack listed on the left, every PNG whose pixels differ from the texture in the pack is
    /// re-encoded into the _v pack. PNGs that are identical to the pack's current pixels are skipped, so untouched exports never
    /// get re-compressed. A scan runs first and asks before anything is written; each rewritten _v pack keeps its original as
    /// "name.pak.old" (an existing .old is never overwritten).
    /// </summary>
    private async void ImportAllPacks()
    {
        if (_batchRunning || _packList.Items.Count == 0) return;
        if (!ConfirmDiscard()) return;

        using var dialog = new FolderBrowserDialog
        {
            Description = "Parent folder created by \"Export all packs\" (one subfolder per *_m.pak). Edited PNGs in it replace the matching textures.",
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        List<FlashPackPair> pairs = _packList.Items.Cast<FlashPackPair>().ToList();
        string rootFolder = dialog.SelectedPath;
        List<string> packFolders = FlashPackExporter.UniqueFolderNames(pairs).Select(name => Path.Combine(rootFolder, name)).ToList();
        var totals = new ImportTotals();

        SetBatchRunning(true);
        try
        {
            PackImportResult?[] scanResults = await ScanPacksAsync(pairs, packFolders, totals);

            int editedTextureCount = scanResults.Sum(result => result?.Changed ?? 0);
            int packsWithChanges = scanResults.Count(result => result?.Changed > 0);

            if (editedTextureCount == 0)
            {
                ReportNothingToImport(pairs.Count, rootFolder, totals);
                return;
            }

            if (!ConfirmImport(editedTextureCount, packsWithChanges))
            {
                _statusLabel.Text = "Import cancelled.";
                return;
            }

            await WritePacksAsync(pairs, packFolders, scanResults, totals);
        }
        finally
        {
            SetBatchRunning(false);
        }

        // Show the new pixels of the pack that is currently open.
        if (_currentPair != null) LoadPair(_currentPair);

        _statusLabel.Text = $"Imported {totals.Changed} texture(s) into {totals.PacksWritten} _v pack(s) from {rootFolder}"
                            + (totals.WrongSize > 0 ? $" ({totals.WrongSize} PNG(s) skipped: wrong size)" : "")
                            + (totals.Failed > 0 ? $" ({totals.Failed} pack(s) failed)" : "");

        if (totals.WrongSize > 0 || totals.Unreadable > 0 || totals.Errors.Count > 0)
        {
            IEnumerable<string> lines = totals.Notes.Concat(totals.Errors).Take(MaxDisplayedWarningLines);
            MessageBox.Show(this, string.Join("\n", lines), "Import finished with warnings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>Pass 1: compares the PNGs with the packs without writing anything.</summary>
    private async Task<PackImportResult?[]> ScanPacksAsync(List<FlashPackPair> pairs, List<string> packFolders, ImportTotals totals)
    {
        var results = new PackImportResult?[pairs.Count];
        for (int index = 0; index < pairs.Count; index++)
        {
            FlashPackPair pair = pairs[index];
            string packFolder = packFolders[index];
            _statusLabel.Text = $"Scanning pack {index + 1} of {pairs.Count}: {pair.Display}";

            try
            {
                PackImportResult result = await Task.Run(() => FlashPackImporter.ImportPack(pair, packFolder, write: false));
                results[index] = result;
                if (result.NoFolder) totals.NoFolder++;
            }
            catch (Exception exception)
            {
                totals.AddError(pair.Display, exception);
            }
        }
        return results;
    }

    /// <summary>Pass 2: writes the packs whose scan found edited PNGs.</summary>
    private async Task WritePacksAsync(List<FlashPackPair> pairs, List<string> packFolders, PackImportResult?[] scanResults, ImportTotals totals)
    {
        for (int index = 0; index < pairs.Count; index++)
        {
            PackImportResult? scan = scanResults[index];
            if (scan == null) continue;

            if (scan.Changed == 0)
            {
                totals.Unchanged += scan.Unchanged;
                totals.Missing += scan.Missing;
                totals.WrongSize += scan.WrongSize;
                totals.Unreadable += scan.Unreadable;
                continue;
            }

            FlashPackPair pair = pairs[index];
            string packFolder = packFolders[index];
            _statusLabel.Text = $"Importing pack {index + 1} of {pairs.Count}: {pair.Display}";

            try
            {
                PackImportResult result = await Task.Run(() => FlashPackImporter.ImportPack(pair, packFolder, write: true));
                totals.Changed += result.Changed;
                totals.Unchanged += result.Unchanged;
                totals.Missing += result.Missing;
                totals.WrongSize += result.WrongSize;
                totals.Unreadable += result.Unreadable;
                totals.PacksWritten++;
                foreach (string note in result.Notes)
                {
                    if (totals.Notes.Count < MaxReportedNotes) totals.Notes.Add($"{pair.Display}: {note}");
                }
            }
            catch (Exception exception)
            {
                totals.AddError(pair.Display, exception);
            }
        }
    }

    private void ReportNothingToImport(int packCount, string rootFolder, ImportTotals totals)
    {
        bool noPackHasFolder = totals.NoFolder == packCount;
        _statusLabel.Text = noPackHasFolder
            ? "None of the listed packs has a matching folder in " + rootFolder
            : "No edited PNGs found in " + rootFolder;

        string message = noPackHasFolder
            ? "No subfolder of the chosen folder matches the packs in the list.\n\n" +
              "Choose the same parent folder that \"Export all packs\" wrote to (it contains one folder per *_m.pak)."
            : "Every PNG in the folder is identical to the texture already in the pack (or missing), so there is nothing to import.";
        if (totals.Errors.Count > 0) message += "\n\n" + string.Join("\n", totals.Errors);
        MessageBox.Show(this, message, "Import", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private bool ConfirmImport(int editedTextureCount, int packsWithChanges) =>
        MessageBox.Show(this,
            $"{editedTextureCount} edited texture(s) in {packsWithChanges} pack(s) will be written into their _v packs.\n\n" +
            "Each _v pack that changes is rewritten and its original is kept as \"name.pak.old\" (an existing .old is never overwritten).\n\nContinue?",
            "Import all packs", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
}
