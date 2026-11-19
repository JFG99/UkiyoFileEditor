namespace UkiyoFileEditor.UI.Tabs;

/// <summary>Exporting textures as PNG files: the open pack, or every pack in the list.</summary>
public sealed partial class FlashTextureTab
{
    private const int MaxReportedErrors = 10;

    /// <summary>Exports every texture of the open pack, one sub-folder per .srd.</summary>
    private void ExportCurrentPack()
    {
        if (_currentPack == null || _currentPair == null) return;

        using var dialog = new FolderBrowserDialog
        {
            Description = "Folder to export the textures into (one subfolder per .srd)",
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        int savedCount = 0, skippedCount = 0;
        try
        {
            foreach (FlashTextureReference reference in _textureReferences)
            {
                byte[]? bgra = TryDecode(reference);
                if (bgra == null) { skippedCount++; continue; }

                string folder = FlashPackExporter.SrdFolder(dialog.SelectedPath, reference.Entry.Name);
                FlashPackExporter.SavePng(bgra, reference.Texture, folder);
                savedCount++;
            }
        }
        catch (Exception exception)
        {
            ShowError("Export failed", exception);
            return;
        }

        _statusLabel.Text = $"Exported {savedCount} texture(s) to {dialog.SelectedPath}"
                            + (skippedCount > 0 ? $" ({skippedCount} skipped: unsupported format)" : "");
    }

    /// <summary>
    /// Exports the textures of every pack currently listed on the left (respecting the filter and the sub-folder option).
    /// Each *_m.pak gets a folder named like the pack (without ".pak"); inside it the layout is the same as "Export all PNGs":
    /// one sub-folder per .srd. The work runs on a background thread so the window stays responsive.
    /// </summary>
    private async void ExportAllPacks()
    {
        if (_batchRunning || _packList.Items.Count == 0) return;

        using var dialog = new FolderBrowserDialog
        {
            Description = "Folder to export into (one subfolder per *_m.pak, each with one subfolder per .srd)",
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        List<FlashPackPair> pairs = _packList.Items.Cast<FlashPackPair>().ToList();
        List<string> packFolders = FlashPackExporter.UniqueFolderNames(pairs);
        string rootFolder = dialog.SelectedPath;
        int savedCount = 0, skippedCount = 0, failedCount = 0;
        var errors = new List<string>();

        SetBatchRunning(true);
        try
        {
            for (int index = 0; index < pairs.Count; index++)
            {
                FlashPackPair pair = pairs[index];
                _statusLabel.Text = $"Exporting pack {index + 1} of {pairs.Count}: {pair.Display}";
                string packFolder = Path.Combine(rootFolder, packFolders[index]);

                try
                {
                    (int saved, int skipped) = await Task.Run(() => FlashPackExporter.ExportPack(pair, packFolder));
                    savedCount += saved;
                    skippedCount += skipped;
                }
                catch (Exception exception)
                {
                    failedCount++;
                    if (errors.Count < MaxReportedErrors) errors.Add($"{pair.Display}: {exception.Message}");
                }
            }
        }
        finally
        {
            SetBatchRunning(false);
        }

        _statusLabel.Text = $"Exported {savedCount} texture(s) from {pairs.Count - failedCount} pack(s) to {rootFolder}"
                            + (skippedCount > 0 ? $" ({skippedCount} skipped: unsupported format)" : "")
                            + (failedCount > 0 ? $" ({failedCount} pack(s) failed)" : "");
        if (errors.Count > 0)
            MessageBox.Show(this, string.Join("\n", errors), "Some packs could not be exported", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
