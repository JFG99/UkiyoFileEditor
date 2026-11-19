using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace UkiyoFileEditor.UI.Tabs;

/// <summary>
/// The Messages tab: lists message packs (.pak), shows each row's Japanese original next to its translation, and lets you
/// translate by hand, search / replace, check, and save the translated packs back.
/// This file holds the state and the constructor; the other MessageTab.*.cs files are split by responsibility (layout, files, grid, detail, search, save, tools ...).
/// </summary>
public sealed partial class MessageTab : UserControl
{
    private const int MaxShownRows = 20000;

    // ---- Data -----------------------------------------------------------
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly TranslationStore _store = TranslationStore.Load();
    private readonly UndoStack _undo = new();
    private CheckOptions _check;
    private readonly ConcurrentDictionary<string, (DateTime Stamp, long Length, MessagePack Pack)> _packCache = new();

    private readonly List<PackFile> _allFiles = new();
    private PackFile? _openFile;
    private List<RowRef> _source = new();
    private List<RowRef> _shown = new();
    private bool _resultsMode;
    private string _resultsText = "";
    private string _containerText = "";

    private string _folder = "";
    private bool _recursive = true;
    private bool _busy;

    private RowRef? _detailRef;
    private bool _detailDirty;
    private bool _suppressBox;
    private bool _suppressSelection;
    private bool _suppressFileEvent;
    private bool _splittersSet;
    private bool _storeDirty;

    private readonly System.Windows.Forms.Timer _commitTimer = new() { Interval = 800 };
    private readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = 2000 };
    private readonly System.Windows.Forms.Timer _filterTimer = new() { Interval = 250 };

    /// <summary>Raised after packs were saved or restored on disk (the System Text tab rebuilds its game file from it).</summary>
    public event EventHandler? PacksChanged;

    /// <summary>Viewer settings (also used by the main window for the recent folders).</summary>
    public AppSettings Settings => _settings;

    // ---- Controls -------------------------------------------------------
    private readonly MenuStrip _menu = new();

    private readonly TextBox _filterBox = new() { Dock = DockStyle.Top, Text = "*.pak", PlaceholderText = "File filter, Enter to apply" };
    private readonly CheckBox _hideEmpty = new() { Dock = DockStyle.Top, Text = "Hide empty (0-byte) files", Checked = true, Padding = new Padding(4, 2, 0, 2) };
    private readonly CheckBox _onlyTranslated = new() { Dock = DockStyle.Top, Text = "Only files with translations", Padding = new Padding(4, 2, 0, 2) };
    private readonly Label _fileCountLabel = new() { Dock = DockStyle.Bottom, Height = 20, Padding = new Padding(4, 3, 0, 0) };
    // Owner-drawn so the "[count]" and "saved" marks in a file's text follow the store without rebuilding the list.
    private readonly ListBox _fileList = new()
    {
        Dock = DockStyle.Fill,
        IntegralHeight = false,
        SelectionMode = SelectionMode.MultiExtended,
        DrawMode = DrawMode.OwnerDrawFixed,
        ItemHeight = 18,
    };

    private readonly TextBox _searchBox = new() { Width = 230, PlaceholderText = "Filter rows: ID, original or translation…" };
    private readonly ComboBox _fieldBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 95 };
    private readonly CheckBox _caseBox = new() { Text = "Aa", AutoSize = true, Appearance = Appearance.Button };
    private readonly CheckBox _wordBox = new() { Text = "Word", AutoSize = true, Appearance = Appearance.Button };
    private readonly CheckBox _regexBox = new() { Text = ".*", AutoSize = true, Appearance = Appearance.Button };
    private readonly ComboBox _showBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly ComboBox _lenOfBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly TextBox _minBox = new() { Width = 42, PlaceholderText = "min" };
    private readonly TextBox _maxBox = new() { Width = 42, PlaceholderText = "max" };
    private readonly ComboBox _sortBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly Button _searchAllButton = new() { Text = "Search all files", AutoSize = true };
    private readonly Label _infoLabel = new() { Dock = DockStyle.Top, AutoSize = false, Height = 22, Padding = new Padding(4, 4, 0, 0) };

    private readonly DataGridView _grid = new();
    private readonly DataGridViewTextBoxColumn _colFile = new() { HeaderText = "File", Width = 200, Visible = false, ReadOnly = true };
    private readonly DataGridViewTextBoxColumn _colId = new() { HeaderText = "ID / Entry", Width = 190, ReadOnly = true };
    private readonly DataGridViewTextBoxColumn _colOriginal = new()
    {
        HeaderText = "Original (Japanese)",
        AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
        FillWeight = 100,
        ReadOnly = true,
        DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True },
        SortMode = DataGridViewColumnSortMode.NotSortable,
    };
    private readonly DataGridViewTextBoxColumn _colTranslation = new()
    {
        HeaderText = "Translation",
        AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
        FillWeight = 100,
        DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True },
        SortMode = DataGridViewColumnSortMode.NotSortable,
    };
    private readonly DataGridViewTextBoxColumn _colLength = new() { HeaderText = "Len", Width = 62, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable };
    private readonly DataGridViewTextBoxColumn _colNote = new()
    {
        HeaderText = "Notes",
        Width = 170,
        ReadOnly = true,
        DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True },
        SortMode = DataGridViewColumnSortMode.NotSortable,
    };

    private readonly TextBox _origBox = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = SystemColors.Window, HideSelection = false };
    private readonly TextBox _translationBox = new() { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, HideSelection = false };
    private readonly Label _origLabel = new() { AutoSize = true, Padding = new Padding(0, 2, 0, 2) };
    private readonly Label _trLabel = new() { AutoSize = true, Padding = new Padding(0, 2, 0, 2) };
    private readonly Label _issueLabel = new() { AutoSize = true, MaximumSize = new Size(1500, 0), ForeColor = Color.DarkRed };
    private readonly Button _copyOrigButton = new() { Text = "Copy original", AutoSize = true };
    private readonly Button _useOrigButton = new() { Text = "Copy original → translation", AutoSize = true };
    private readonly Button _codeButton = new() { Text = "Insert code ▾", AutoSize = true };
    private readonly Button _nextUntranslatedButton = new() { Text = "Next untranslated", AutoSize = true };
    private readonly Button _clearButton = new() { Text = "Clear translation", AutoSize = true };
    private readonly Button _formatButton = new() { Text = "Auto format (Ctrl+Shift+L)", AutoSize = true };
    private readonly ContextMenuStrip _codeMenu = new();
    private readonly ContextMenuStrip _gridMenu = new();

    private SplitContainer _mainSplit = null!;
    private SplitContainer _rightSplit = null!;

    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _statusLabel = new() { Text = "Open a folder containing message .pak files.", Spring = true, TextAlign = ContentAlignment.MiddleLeft };

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    public MessageTab()
    {
        Dock = DockStyle.Fill;
        _check = CheckOptions.From(_settings);

        BuildUi();
        BuildMenu();
        WireEvents();

        _commitTimer.Tick += (_, _) => CommitDetail();
        _saveTimer.Tick += (_, _) => FlushSave();
        _filterTimer.Tick += (_, _) => { _filterTimer.Stop(); ApplyView(); };
    }

    protected override void OnVisibleChanged(EventArgs eventArgs)
    {
        base.OnVisibleChanged(eventArgs);
        if (Visible && !_splittersSet && _mainSplit.Width > 700)
        {
            _splittersSet = true;
            try
            {
                _mainSplit.SplitterDistance = 300;
                _rightSplit.SplitterDistance = Math.Max(160, _rightSplit.Height - 280);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { }
        }
    }

    // ---- Busy state --------------------------------------------------------------
    private void SetBusy(bool busy)
    {
        _busy = busy;
        _menu.Enabled = !busy;
        _searchAllButton.Enabled = !busy;
        _fileList.Enabled = !busy;
        UseWaitCursor = busy;
    }
}
