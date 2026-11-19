namespace UkiyoFileEditor.UI;

/// <summary>Hosts the shared folder bar and the viewer tabs (textures, messages, system text).</summary>
public sealed class MainForm : Form
{
    private readonly Button _openButton = new() { Text = "Open Folder…", AutoSize = true };
    private readonly CheckBox _recursive = new() { Text = "Include subfolders", AutoSize = true, Checked = true };
    private readonly Button _recentButton = new() { Text = "Recent ▾", AutoSize = true };
    private readonly ContextMenuStrip _recentMenu = new();
    private readonly Label _pathLabel = new() { AutoSize = true, Text = "(no folder)", Padding = new Padding(0, 6, 0, 0) };

    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly TextureTab _textures = new();
    private readonly MessageTab _messages = new();
    private readonly SystemTextTab _system = new();
    private readonly FlashTextureTab _flash = new();

    private string _folder = "";

    public MainForm()
    {
        Text = "Ukiyo File Editor";
        Width = 1200;
        Height = 780;
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;

        var topBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(4),
            WrapContents = false
        };
        topBar.Controls.Add(_openButton);
        topBar.Controls.Add(_recentButton);
        topBar.Controls.Add(_recursive);
        topBar.Controls.Add(_pathLabel);

        var texturePage = new TabPage("Textures (.srdv)");
        texturePage.Controls.Add(_textures);

        var messagePage = new TabPage("Messages (.pak)");
        messagePage.Controls.Add(_messages);

        var systemPage = new TabPage("System text (resident)");
        systemPage.Controls.Add(_system);

        _tabs.TabPages.Add(texturePage);
        _tabs.TabPages.Add(messagePage);
        var flashPage = new TabPage("Flash textures (_m/_v.pak)");
        flashPage.Controls.Add(_flash);

        _tabs.TabPages.Add(systemPage);
        _tabs.TabPages.Add(flashPage);

        // Fill control first, then the top bar (later-added controls dock first).
        Controls.Add(_tabs);
        Controls.Add(topBar);

        _openButton.Click += (_, _) => PickFolder();
        _recentButton.Click += (_, _) => ShowRecent();
        FormClosing += (_, eventArgs) =>
        {
            if (!_flash.ConfirmDiscard())
            {
                eventArgs.Cancel = true;
                return;
            }
            _messages.FlushSave();
            _system.FlushSave();
        };
        _recursive.CheckedChanged += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_folder)) LoadFolder(_folder);
        };

        DragEnter += (_, eventArgs) =>
        {
            if (eventArgs.Data?.GetDataPresent(DataFormats.FileDrop) == true)
                eventArgs.Effect = DragDropEffects.Copy;
        };
        DragDrop += (_, eventArgs) =>
        {
            if (eventArgs.Data?.GetData(DataFormats.FileDrop) is string[] items && items.Length > 0)
            {
                string first = items[0];
                if (Directory.Exists(first)) LoadFolder(first);
                else if (File.Exists(first)) LoadFolder(Path.GetDirectoryName(first) ?? first);
            }
        };
    }

    private void PickFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select a folder containing .srdv or message .pak files",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
            LoadFolder(dialog.SelectedPath);
    }

    private void ShowRecent()
    {
        _recentMenu.Items.Clear();
        foreach (string folder in _messages.Settings.RecentFolders.ToList())
        {
            string path = folder;
            _recentMenu.Items.Add(path, null, (_, _) =>
            {
                if (Directory.Exists(path)) LoadFolder(path);
                else MessageBox.Show(this, "That folder no longer exists:\n" + path, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            });
        }
        if (_recentMenu.Items.Count == 0)
        {
            ToolStripItem none = _recentMenu.Items.Add("(no recent folders yet)");
            none.Enabled = false;
        }
        _recentMenu.Show(_recentButton, 0, _recentButton.Height);
    }

    private void RememberFolder(string folder)
    {
        List<string> recent = _messages.Settings.RecentFolders;
        recent.RemoveAll(packFile => string.Equals(packFile, folder, StringComparison.OrdinalIgnoreCase));
        recent.Insert(0, folder);
        if (recent.Count > 10) recent.RemoveRange(10, recent.Count - 10);
        _messages.Settings.Save();
    }

    private void LoadFolder(string folder)
    {
        _folder = folder;
        _pathLabel.Text = folder;
        RememberFolder(folder);

        UseWaitCursor = true;
        try
        {
            _textures.LoadFolder(folder, _recursive.Checked);
            _messages.LoadFolder(folder, _recursive.Checked);
            _system.LoadFolder(folder, _recursive.Checked);
            _flash.LoadFolder(folder, _recursive.Checked);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }
}
