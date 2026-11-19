namespace UkiyoFileEditor.UI.Tabs;

/// <summary>Replacing textures from an image file and saving the changed _v pack.</summary>
public sealed partial class FlashTextureTab
{
    private const string BackupSuffix = ".old";

    private void ReplaceSelected(bool replaceAllCopies)
    {
        FlashTextureReference? selected = SelectedReference;
        if (selected == null || _currentPack == null) return;

        FlashTexture selectedTexture = selected.Texture;
        using var dialog = new OpenFileDialog
        {
            Filter = "Images (*.png;*.bmp;*.jpg)|*.png;*.bmp;*.jpg|All files (*.*)|*.*",
            Title = $"New image for {selectedTexture.Name} ({selectedTexture.Width}×{selectedTexture.Height})"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        byte[] bgra;
        int imageWidth, imageHeight;
        try
        {
            bgra = BitmapConverter.LoadBgra(dialog.FileName, out imageWidth, out imageHeight);
        }
        catch (Exception exception)
        {
            ShowError("Cannot read image", exception);
            return;
        }

        if (imageWidth != selectedTexture.Width || imageHeight != selectedTexture.Height)
        {
            MessageBox.Show(this,
                $"The image is {imageWidth}×{imageHeight} but the texture is {selectedTexture.Width}×{selectedTexture.Height}.\n\n" +
                "The size must match exactly, because the pixel data keeps its place in the pack.",
                "Wrong size", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        List<FlashTextureReference> targets = replaceAllCopies
            ? FindCopiesOf(selectedTexture)
            : new List<FlashTextureReference> { selected };

        if (replaceAllCopies && !ConfirmReplaceAllCopies(selectedTexture, targets)) return;

        int replacedCount = ReplaceTextures(targets, bgra);
        if (replacedCount > 0) _hasUnsavedChanges = true;

        _statusLabel.Text = $"Replaced {replacedCount} texture(s). Use \"Save _v pack\" to write them.";
        if (SelectedReference != null) ShowTexture(SelectedReference);
        UpdateButtons();
    }

    /// <summary>Every texture of the open pack with the same name, size, format and mip count as <paramref name="original"/>.</summary>
    private List<FlashTextureReference> FindCopiesOf(FlashTexture original) =>
        _textureReferences.Where(reference =>
            reference.Entry.HasPixelData
            && reference.Texture.Supported
            && string.Equals(reference.Texture.Name, original.Name, StringComparison.OrdinalIgnoreCase)
            && reference.Texture.Width == original.Width
            && reference.Texture.Height == original.Height
            && reference.Texture.Format == original.Format
            && reference.Texture.MipCount == original.MipCount).ToList();

    private bool ConfirmReplaceAllCopies(FlashTexture texture, List<FlashTextureReference> targets)
    {
        string srdNames = string.Join(", ", targets.Select(reference => reference.Entry.Name));
        return MessageBox.Show(this, $"Replace \"{texture.Name}\" in {targets.Count} .srd file(s)?\n\n{srdNames}",
            "Replace in all copies", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    }

    /// <summary>Re-encodes <paramref name="bgra"/> into each target. Returns how many were replaced before an error (if any) stopped the run.</summary>
    private int ReplaceTextures(List<FlashTextureReference> targets, byte[] bgra)
    {
        int replacedCount = 0;
        try
        {
            foreach (FlashTextureReference target in targets)
            {
                string label = $"{target.Entry.Name} / {target.Texture.Name}";
                _currentPack!.ReplacePixels(target.Entry, target.Texture, bgra, label);

                target.Texture.Modified = true;
                if (target.TreeNode != null) target.TreeNode.Text = TextureLabel(target.Texture);
                replacedCount++;
            }
        }
        catch (Exception exception)
        {
            ShowError("Replace failed", exception);
        }
        return replacedCount;
    }

    // ---- Saving ---------------------------------------------------------
    private void SavePixelDataPack()
    {
        if (_currentPack == null || _currentPair == null || !_hasUnsavedChanges) return;

        string path = _currentPair.PixelDataPath;
        try
        {
            byte[] rebuilt = _currentPack.PixelDataPack.Build();

            string backupPath = path + BackupSuffix;
            if (!File.Exists(backupPath)) File.Copy(path, backupPath);

            File.WriteAllBytes(path, rebuilt);
            _hasUnsavedChanges = false;
            foreach (FlashTextureReference reference in _textureReferences)
            {
                if (!reference.Texture.Modified) continue;
                reference.Texture.Modified = false;
                if (reference.TreeNode != null) reference.TreeNode.Text = TextureLabel(reference.Texture);
            }
            _statusLabel.Text = $"Saved {path} ({rebuilt.Length:N0} bytes); original kept as {Path.GetFileName(backupPath)}";
        }
        catch (Exception exception)
        {
            ShowError("Save failed", exception);
        }

        UpdateButtons();
    }
}
