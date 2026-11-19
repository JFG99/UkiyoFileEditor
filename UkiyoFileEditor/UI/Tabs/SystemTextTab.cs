namespace UkiyoFileEditor.UI.Tabs;

/// <summary>
/// The System Text tab: the menu, shop, item, quest and glossary text that lives inside game_resident_JP_m.pak.
/// The packs nested in that file are extracted into a private work folder and edited with the same editor as the Messages tab
/// (translation store, undo, search / replace, translate, checks, CSV import / export). They never show up in the Messages list.
/// Saving a pack rebuilds game_resident_JP_m.pak (original kept as ".old") when "Update the game file when I save" is ticked.
/// </summary>
public sealed class SystemTextTab : UserControl
{
    private readonly MessageTab _editor = new();
    private readonly Label _pathLabel = new() { AutoSize = true, Padding = new Padding(0, 6, 0, 0), Text = "(no resident archive loaded)" };
    private readonly Button _browseButton = new() { Text = "Choose game_resident_JP_m.pak…", AutoSize = true };
    private readonly Button _buildButton = new() { Text = "Build game file now", AutoSize = true };
    private readonly Button _restoreButton = new() { Text = "Restore original game file", AutoSize = true };
    private readonly Button _extractButton = new() { Text = "Extract again", AutoSize = true };
    private readonly CheckBox _autoBuild = new() { Text = "Update the game file when I save", AutoSize = true, Checked = true, Padding = new Padding(8, 3, 0, 0) };
    private readonly Label _resultLabel = new() { Dock = DockStyle.Bottom, Height = 22, Padding = new Padding(6, 4, 0, 0) };

    private string _gamePath = "";
    private bool _recursive = true;
    private bool _building;

    public SystemTextTab()
    {
        Dock = DockStyle.Fill;
        _editor.Dock = DockStyle.Fill;

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(4),
            WrapContents = true,
        };
        toolbar.Controls.Add(_browseButton);
        toolbar.Controls.Add(_buildButton);
        toolbar.Controls.Add(_restoreButton);
        toolbar.Controls.Add(_extractButton);
        toolbar.Controls.Add(_autoBuild);
        toolbar.Controls.Add(_pathLabel);

        var tips = new ToolTip { AutoPopDelay = 15000 };
        tips.SetToolTip(_buildButton, "Put all packs of the work folder (translated or not) back into game_resident_JP_m.pak.");
        tips.SetToolTip(_restoreButton, "Put the original game_resident_JP_m.pak back (the .old file). Your translations stay stored.");
        tips.SetToolTip(_extractButton, "Read the packs out of the original archive again. Packs you saved in the work folder are replaced by the originals.");

        // Fill control first, then the docked ones (later-added controls dock first).
        Controls.Add(_editor);
        Controls.Add(_resultLabel);
        Controls.Add(toolbar);

        _browseButton.Click += (_, _) => Browse();
        _buildButton.Click += async (_, _) => await BuildAsync(manual: true);
        _restoreButton.Click += (_, _) => RestoreGameFile();
        _extractButton.Click += (_, _) => ExtractAgain();
        _editor.PacksChanged += async (_, _) =>
        {
            if (_autoBuild.Checked) await BuildAsync(manual: false);
        };
        UpdateButtons();
    }

    /// <summary>Writes pending translations to disk (called when the window closes).</summary>
    public void FlushSave() => _editor.FlushSave();

    /// <summary>Called by the main window when a folder is opened: looks for game_resident_JP_m.pak in it.</summary>
    public void LoadFolder(string folder, bool recursive)
    {
        _recursive = recursive;
        string found = ResidentArchive.Find(folder, recursive);
        if (found.Length > 0)
        {
            SetGameFile(found);
            return;
        }

        if (_gamePath.Length == 0)
            _resultLabel.Text = $"{ResidentArchive.FileName} was not found in this folder (use the button above to pick it).";
        else
            _resultLabel.Text = $"{ResidentArchive.FileName} was not found in this folder; still working on {_gamePath}.";
    }

    private void Browse()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select " + ResidentArchive.FileName,
            Filter = "Resident archive|" + ResidentArchive.FileName + "|Pack files (*.pak)|*.pak",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) SetGameFile(dialog.FileName);
    }

    private void SetGameFile(string path, bool force = false)
    {
        UseWaitCursor = true;
        try
        {
            string message = ResidentArchive.Extract(path, force);
            _gamePath = path;
            _pathLabel.Text = path + (File.Exists(path + ".old") ? "   (original kept as .old)" : "");
            _resultLabel.Text = message;
            _editor.LoadFolder(ResidentArchive.WorkRoot, true);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or OverflowException)
        {
            _resultLabel.Text = "Cannot read " + path + ": " + exception.Message;
            MessageBox.Show(this, exception.Message, "Cannot read the resident archive", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            UpdateButtons();
        }
    }

    private void ExtractAgain()
    {
        if (_gamePath.Length == 0) return;
        if (MessageBox.Show(this,
                "Read the packs out of the original archive again?\n\nPacks you saved in the work folder are replaced by the originals " +
                "(your stored translations are not touched, you can save them again).",
                "Extract again", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        SetGameFile(_gamePath, force: true);
    }

    private void UpdateButtons()
    {
        bool hasTranslations = _gamePath.Length > 0;
        _buildButton.Enabled = hasTranslations && !_building;
        _extractButton.Enabled = hasTranslations && !_building;
        _restoreButton.Enabled = hasTranslations && !_building && File.Exists(_gamePath + ".old");
    }

    private async Task BuildAsync(bool manual)
    {
        if (_gamePath.Length == 0 || _building) return;
        _building = true;
        UpdateButtons();
        UseWaitCursor = true;
        string path = _gamePath;
        try
        {
            string message = "";
            int changed = await Task.Run(() => ResidentArchive.Build(path, out message));
            _resultLabel.Text = message;
            _pathLabel.Text = path + "   (original kept as .old)";
            if (manual && changed == 0)
                _resultLabel.Text = "No pack differs from the original; the game file was written unchanged.";
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or OverflowException)
        {
            _resultLabel.Text = "Game file NOT updated: " + exception.Message;
            MessageBox.Show(this, exception.Message, "Game file not updated", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _building = false;
            UseWaitCursor = false;
            UpdateButtons();
        }
    }

    private void RestoreGameFile()
    {
        if (_gamePath.Length == 0 || !File.Exists(_gamePath + ".old")) return;
        if (MessageBox.Show(this,
                "Put the original " + ResidentArchive.FileName + " back?\n\nThe built file is replaced by the .old file (which then disappears). " +
                "Your translations stay stored and can be built again.",
                "Restore original", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        try
        {
            ResidentArchive.Restore(_gamePath);
            _resultLabel.Text = "Original game file restored.";
            _pathLabel.Text = _gamePath;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, "Cannot restore", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        UpdateButtons();
    }
}
