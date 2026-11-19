using System.Drawing.Imaging;

namespace UkiyoFileEditor.UI.Tabs;

/// <summary>
/// Tab for the Flash UI packs: lists "*_m.pak" files that have a "*_v.pak" next to them, shows every texture of every .srd
/// inside (pixels come from the matching .srdv in the _v pak), exports PNGs and replaces textures from PNGs.
/// A replacement PNG must have exactly the size of the texture; it is re-encoded in the texture's own format (DXT1 / DXT5),
/// so the data keeps its size and nothing else in the pack has to move. Saving rewrites the _v pack; the original is kept as
/// "name.pak.old" (an existing .old is never overwritten). "Import all packs" is the reverse of "Export all packs": it reads the
/// whole exported folder tree back and replaces every texture whose PNG was edited.
/// This file holds the state, the UI and the viewing; export, import and replacing live in the other partial files.
/// </summary>
public sealed partial class FlashTextureTab : UserControl
{
    private const string DefaultFilter = "*_m.pak";
    private const string MetadataPackSuffix = "_m.pak";
    private const string PixelDataPackSuffix = "_v.pak";
    private const float MaxFitZoom = 8f;

    private readonly TextBox _filterBox = new() { Dock = DockStyle.Top, Text = DefaultFilter, PlaceholderText = "File filter, Enter to apply" };
    private readonly ListBox _packList = new() { Dock = DockStyle.Top, Height = 150, IntegralHeight = false, HorizontalScrollbar = true };
    private readonly TreeView _textureTree = new() { Dock = DockStyle.Fill, HideSelection = false };

    private readonly ComboBox _zoomBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
    private readonly ComboBox _backgroundBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly Button _savePngButton = new() { Text = "Save PNG…", AutoSize = true };
    private readonly Button _exportAllButton = new() { Text = "Export all PNGs…", AutoSize = true };
    private readonly Button _exportAllPacksButton = new() { Text = "Export all packs…", AutoSize = true };
    private readonly Button _importAllPacksButton = new() { Text = "Import all packs…", AutoSize = true };
    private readonly Button _replaceButton = new() { Text = "Replace from PNG…", AutoSize = true };
    private readonly Button _replaceAllCopiesButton = new() { Text = "Replace in all copies…", AutoSize = true };
    private readonly Button _savePackButton = new() { Text = "Save _v pack", AutoSize = true };
    private readonly Button _discardButton = new() { Text = "Discard changes", AutoSize = true };
    private readonly Label _infoLabel = new() { Dock = DockStyle.Top, AutoSize = false, Height = 22, Padding = new Padding(4, 4, 0, 0) };

    private readonly Panel _viewHost = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(40, 40, 40) };
    private readonly PixelView _pixelView = new();

    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _statusLabel = new() { Text = "Open a folder containing *_m.pak / *_v.pak Flash packs." };

    private readonly List<FlashTextureReference> _textureReferences = new();

    private string _folder = "";
    private bool _includeSubfolders = true;
    private FlashPackPair? _currentPair;
    private FlashPack? _currentPack;
    private bool _hasUnsavedChanges;
    private bool _suppressSelectionEvents;
    private int _lastSelectedPackIndex = -1;
    private bool _batchRunning;

    public FlashTextureTab()
    {
        Dock = DockStyle.Fill;

        BuildUi();
        WireEvents();

        _zoomBox.Items.AddRange(new object[] { "Fit", "25%", "50%", "100%", "200%", "300%", "400%", "800%" });
        _zoomBox.SelectedIndex = 0;

        _backgroundBox.Items.AddRange(new object[] { "Black", "White", "Checkerboard" });
        _backgroundBox.SelectedIndex = 2;
        _pixelView.Background = ViewBackground.Checkerboard;

        UpdateButtons();
    }

    public bool HasUnsavedChanges => _hasUnsavedChanges;

    private FlashTextureReference? SelectedReference => _textureTree.SelectedNode?.Tag as FlashTextureReference;

    /// <summary>Asks before unsaved texture changes are thrown away. True means it is fine to continue.</summary>
    public bool ConfirmDiscard()
    {
        if (!_hasUnsavedChanges) return true;
        return MessageBox.Show(this, "There are texture changes that have not been saved to the _v pack.\n\nDiscard them?",
            "Unsaved changes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
    }

    // ---- UI construction ------------------------------------------------
    private void BuildUi()
    {
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(4),
            WrapContents = true
        };
        toolbar.Controls.Add(new Label { Text = "Zoom:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        toolbar.Controls.Add(_zoomBox);
        toolbar.Controls.Add(new Label { Text = "Background:", AutoSize = true, Padding = new Padding(8, 6, 0, 0) });
        toolbar.Controls.Add(_backgroundBox);
        toolbar.Controls.Add(_savePngButton);
        toolbar.Controls.Add(_exportAllButton);
        toolbar.Controls.Add(_exportAllPacksButton);
        toolbar.Controls.Add(_importAllPacksButton);
        toolbar.Controls.Add(_replaceButton);
        toolbar.Controls.Add(_replaceAllCopiesButton);
        toolbar.Controls.Add(_savePackButton);
        toolbar.Controls.Add(_discardButton);

        var rightPanel = new Panel { Dock = DockStyle.Fill };
        _viewHost.Controls.Add(_pixelView);
        rightPanel.Controls.Add(_viewHost);
        rightPanel.Controls.Add(_infoLabel);
        rightPanel.Controls.Add(toolbar);

        var leftPanel = new Panel { Dock = DockStyle.Fill };
        leftPanel.Controls.Add(_textureTree);
        leftPanel.Controls.Add(_packList);
        leftPanel.Controls.Add(_filterBox);

        var splitContainer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 330,
            FixedPanel = FixedPanel.Panel1
        };
        splitContainer.Panel1.Controls.Add(leftPanel);
        splitContainer.Panel2.Controls.Add(rightPanel);

        _statusStrip.Items.Add(_statusLabel);

        Controls.Add(splitContainer);
        Controls.Add(_statusStrip);
    }

    private void WireEvents()
    {
        _filterBox.KeyDown += (_, keyEventArgs) =>
        {
            if (keyEventArgs.KeyCode != Keys.Enter) return;
            keyEventArgs.SuppressKeyPress = true;
            if (ConfirmDiscard()) ReloadPackList();
        };

        _packList.SelectedIndexChanged += (_, _) => OnPackSelected();
        _textureTree.AfterSelect += (_, _) => OnTextureSelected();

        _zoomBox.SelectedIndexChanged += (_, _) => ApplyZoom();
        _backgroundBox.SelectedIndexChanged += (_, _) =>
        {
            _pixelView.Background = _backgroundBox.SelectedIndex switch
            {
                1 => ViewBackground.White,
                2 => ViewBackground.Checkerboard,
                _ => ViewBackground.Black
            };
        };
        _viewHost.Resize += (_, _) =>
        {
            if (_zoomBox.SelectedIndex == 0) ApplyZoom();
        };

        _savePngButton.Click += (_, _) => SaveSelectedPng();
        _exportAllButton.Click += (_, _) => ExportCurrentPack();
        _exportAllPacksButton.Click += (_, _) => ExportAllPacks();
        _importAllPacksButton.Click += (_, _) => ImportAllPacks();
        _replaceButton.Click += (_, _) => ReplaceSelected(replaceAllCopies: false);
        _replaceAllCopiesButton.Click += (_, _) => ReplaceSelected(replaceAllCopies: true);
        _savePackButton.Click += (_, _) => SavePixelDataPack();
        _discardButton.Click += (_, _) =>
        {
            if (_currentPair != null && ConfirmDiscard()) LoadPair(_currentPair);
        };
    }

    // ---- Folder / pack handling ------------------------------------------
    public void LoadFolder(string folder, bool includeSubfolders)
    {
        if (!ConfirmDiscard()) return;
        _folder = folder;
        _includeSubfolders = includeSubfolders;
        ReloadPackList();
    }

    private void ReloadPackList()
    {
        if (string.IsNullOrEmpty(_folder)) return;

        _suppressSelectionEvents = true;
        _packList.BeginUpdate();
        _packList.Items.Clear();
        try
        {
            string pattern = string.IsNullOrWhiteSpace(_filterBox.Text) ? DefaultFilter : _filterBox.Text.Trim();
            var options = new EnumerationOptions { RecurseSubdirectories = _includeSubfolders, IgnoreInaccessible = true };

            foreach (string metadataPath in Directory.EnumerateFiles(_folder, pattern, options).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (!metadataPath.EndsWith(MetadataPackSuffix, StringComparison.OrdinalIgnoreCase)) continue;

                string pixelDataPath = metadataPath[..^MetadataPackSuffix.Length] + PixelDataPackSuffix;
                if (!File.Exists(pixelDataPath)) continue;

                _packList.Items.Add(new FlashPackPair
                {
                    MetadataPath = metadataPath,
                    PixelDataPath = pixelDataPath,
                    Display = Path.GetRelativePath(_folder, metadataPath)
                });
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Cannot read folder", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _packList.EndUpdate();
            _suppressSelectionEvents = false;
        }

        ClearCurrentPack();
        _lastSelectedPackIndex = -1;
        _statusLabel.Text = $"{_packList.Items.Count} Flash pack pair(s) found (*_m.pak with a matching *_v.pak)";
        if (_packList.Items.Count > 0) _packList.SelectedIndex = 0;
    }

    private void OnPackSelected()
    {
        if (_suppressSelectionEvents || _packList.SelectedItem is not FlashPackPair pair) return;

        if (!ConfirmDiscard())
        {
            _suppressSelectionEvents = true;
            _packList.SelectedIndex = _lastSelectedPackIndex;
            _suppressSelectionEvents = false;
            return;
        }

        _lastSelectedPackIndex = _packList.SelectedIndex;
        LoadPair(pair);
    }

    private void ClearCurrentPack()
    {
        ClearView();
        _textureTree.Nodes.Clear();
        _textureReferences.Clear();
        _currentPair = null;
        _currentPack = null;
        _hasUnsavedChanges = false;
        UpdateButtons();
    }

    private void LoadPair(FlashPackPair pair)
    {
        ClearCurrentPack();

        try
        {
            _currentPack = FlashPack.Load(pair.MetadataPath, pair.PixelDataPath);
            _currentPair = pair;
        }
        catch (Exception exception)
        {
            _infoLabel.Text = $"{pair.Display}: {exception.Message}";
            _statusLabel.Text = "Error reading pack";
            return;
        }

        FillTree(_currentPack);
        _statusLabel.Text = $"{pair.Display}: {_currentPack.Entries.Count} .srd file(s), {_textureReferences.Count} texture(s)";
        UpdateButtons();

        if (_textureTree.Nodes.Count > 0 && _textureTree.Nodes[0].Nodes.Count > 0)
            _textureTree.SelectedNode = _textureTree.Nodes[0].Nodes[0];
    }

    private void FillTree(FlashPack pack)
    {
        _textureTree.BeginUpdate();
        _textureTree.Nodes.Clear();
        _textureReferences.Clear();

        foreach (FlashPackEntry entry in pack.Entries)
        {
            string noPixelDataNote = entry.HasPixelData ? "" : "  (no matching .srdv)";
            var entryNode = new TreeNode($"{entry.Name}  [{entry.Textures.Count}]{noPixelDataNote}") { Tag = entry };
            foreach (FlashTexture texture in entry.Textures)
            {
                var reference = new FlashTextureReference(entry, texture);
                var textureNode = new TreeNode(TextureLabel(texture)) { Tag = reference };
                reference.TreeNode = textureNode;
                entryNode.Nodes.Add(textureNode);
                _textureReferences.Add(reference);
            }
            _textureTree.Nodes.Add(entryNode);
        }

        _textureTree.EndUpdate();
        if (_textureTree.Nodes.Count > 0) _textureTree.Nodes[0].Expand();
    }

    private static string TextureLabel(FlashTexture texture) =>
        $"{(texture.Modified ? "* " : "")}{texture.Name}   {texture.Width}×{texture.Height}  {texture.FormatName}";

    // ---- Viewing --------------------------------------------------------
    private void OnTextureSelected()
    {
        if (SelectedReference is { } reference) ShowTexture(reference);
        else ClearView();
        UpdateButtons();
    }

    private byte[]? TryDecode(FlashTextureReference reference) =>
        _currentPack?.TryDecode(reference.Entry, reference.Texture);

    private void ShowTexture(FlashTextureReference reference)
    {
        FlashPackEntry entry = reference.Entry;
        FlashTexture texture = reference.Texture;

        byte[]? bgra = TryDecode(reference);
        if (bgra == null)
        {
            ClearView();
            _infoLabel.Text = $"{entry.Name} / {texture.Name}: cannot show this texture ({texture.FormatName}, {texture.Width}×{texture.Height}, {texture.Size:N0} bytes).";
            return;
        }

        Bitmap bitmap = BitmapConverter.ToBitmap(bgra, texture.Width, texture.Height);
        Bitmap? previousImage = _pixelView.Image;
        _pixelView.Image = bitmap;
        previousImage?.Dispose();
        ApplyZoom();

        _infoLabel.Text = $"{entry.Name} / {texture.Name}  |  {texture.Width}×{texture.Height}  |  {texture.FormatName}  |  " +
                          $"{texture.Size:N0} bytes at 0x{texture.Offset:X} in {entry.Name}v" + (texture.Modified ? "  |  REPLACED" : "");
    }

    private void ClearView()
    {
        Bitmap? previousImage = _pixelView.Image;
        _pixelView.Image = null;
        previousImage?.Dispose();
        _infoLabel.Text = "";
    }

    private void ApplyZoom()
    {
        Bitmap? image = _pixelView.Image;
        if (image == null) return;

        float zoom;
        string choice = _zoomBox.SelectedItem?.ToString() ?? "Fit";

        if (choice == "Fit")
        {
            Size available = _viewHost.ClientSize;
            float zoomToFitWidth = (available.Width - 2f) / image.Width;
            float zoomToFitHeight = (available.Height - 2f) / image.Height;
            zoom = Math.Min(Math.Min(zoomToFitWidth, zoomToFitHeight), MaxFitZoom);
            if (zoom <= 0f) zoom = 1f;
        }
        else
        {
            zoom = float.Parse(choice.TrimEnd('%')) / 100f;
        }

        _pixelView.Zoom = zoom;
    }

    private void UpdateButtons()
    {
        FlashTextureReference? selected = SelectedReference;
        bool canEditSelected = selected != null && _currentPack != null && selected.Entry.HasPixelData && selected.Texture.Supported;
        _savePngButton.Enabled = canEditSelected;
        _replaceButton.Enabled = canEditSelected;
        _replaceAllCopiesButton.Enabled = canEditSelected;
        _exportAllButton.Enabled = _currentPack != null && _textureReferences.Count > 0;
        _exportAllPacksButton.Enabled = !_batchRunning && _packList.Items.Count > 0;
        _importAllPacksButton.Enabled = !_batchRunning && _packList.Items.Count > 0;
        _savePackButton.Enabled = _hasUnsavedChanges;
        _discardButton.Enabled = _hasUnsavedChanges;
    }

    /// <summary>Disables the controls that must not be used while a background export / import runs.</summary>
    private void SetBatchRunning(bool running)
    {
        _batchRunning = running;
        _packList.Enabled = !running;
        if (running)
        {
            _exportAllButton.Enabled = false;
            _exportAllPacksButton.Enabled = false;
            _importAllPacksButton.Enabled = false;
            _replaceButton.Enabled = false;
            _replaceAllCopiesButton.Enabled = false;
        }
        else
        {
            UpdateButtons();
        }
    }

    private void ShowError(string title, Exception exception) =>
        MessageBox.Show(this, exception.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);

    private void SaveSelectedPng()
    {
        FlashTextureReference? selected = SelectedReference;
        if (selected == null || _pixelView.Image == null) return;

        using var dialog = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            FileName = FlashPackExporter.PngFileName(selected.Texture.Name)
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            _pixelView.Image.Save(dialog.FileName, ImageFormat.Png);
            _statusLabel.Text = "Saved " + dialog.FileName;
        }
        catch (Exception exception)
        {
            ShowError("Save failed", exception);
        }
    }
}
