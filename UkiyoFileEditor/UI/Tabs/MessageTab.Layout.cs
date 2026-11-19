using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace UkiyoFileEditor.UI.Tabs;

/// <summary>Building the controls, wiring their events and the menu bar.</summary>
public sealed partial class MessageTab
{
    // ---- Layout ---------------------------------------------------------
    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };

    private void BuildUi()
    {
        Font japaneseFont = new("Yu Gothic UI", 10f);

        // Grid (virtual mode: rows are read from _shown on demand, so 20,000 rows are no problem).
        _grid.Dock = DockStyle.Fill;
        _grid.VirtualMode = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = true;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells;
        _grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.Disable;
        _grid.BackgroundColor = SystemColors.Window;
        _grid.DefaultCellStyle.Font = japaneseFont;
        _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _grid.Columns.AddRange(_colFile, _colId, _colOriginal, _colTranslation, _colLength, _colNote);
        _grid.ContextMenuStrip = _gridMenu;

        _origBox.Font = new Font("Yu Gothic UI", 11f);
        _translationBox.Font = new Font("Yu Gothic UI", 11f);
        _origLabel.Font = new Font(Font, FontStyle.Bold);
        _trLabel.Font = new Font(Font, FontStyle.Bold);

        // Filter bar
        _fieldBox.Items.AddRange(new object[] { "All fields", "ID / Entry", "Original", "Translation" });
        _fieldBox.SelectedIndex = 0;
        _showBox.Items.AddRange(new object[] { "Show: all rows", "Show: untranslated", "Show: translated", "Show: with notes", "Show: with warnings" });
        _showBox.SelectedIndex = 0;
        _lenOfBox.Items.AddRange(new object[] { "Length of original", "Length of translation" });
        _lenOfBox.SelectedIndex = 0;
        _sortBox.Items.AddRange(new object[] { "Sort: file order", "Sort: longest first", "Sort: shortest first" });
        _sortBox.SelectedIndex = 0;

        var tips = new ToolTip { AutoPopDelay = 15000 };
        tips.SetToolTip(_caseBox, "Match case");
        tips.SetToolTip(_wordBox, "Whole word");
        tips.SetToolTip(_regexBox, "Regular expression");
        tips.SetToolTip(_minBox, "Show only rows with at least this many characters (codes not counted)");
        tips.SetToolTip(_maxBox, "Show only rows with at most this many characters (codes not counted)");
        tips.SetToolTip(_searchAllButton, "Look for the filter text in every file of the list (Ctrl+Shift+F)");

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(4),
            WrapContents = true,
        };
        toolbar.Controls.Add(Caption("Filter:"));
        toolbar.Controls.Add(_searchBox);
        toolbar.Controls.Add(_fieldBox);
        toolbar.Controls.Add(_caseBox);
        toolbar.Controls.Add(_wordBox);
        toolbar.Controls.Add(_regexBox);
        toolbar.Controls.Add(_showBox);
        toolbar.Controls.Add(_lenOfBox);
        toolbar.Controls.Add(_minBox);
        toolbar.Controls.Add(_maxBox);
        toolbar.Controls.Add(_sortBox);
        toolbar.Controls.Add(_searchAllButton);

        // Detail area under the grid: the original (read-only, selectable) and the translation (editable).
        var detail = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(4) };
        detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        detail.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detail.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        detail.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detail.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var origButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        origButtons.Controls.Add(_copyOrigButton);
        origButtons.Controls.Add(_useOrigButton);
        var trButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        trButtons.Controls.Add(_codeButton);
        trButtons.Controls.Add(_nextUntranslatedButton);
        trButtons.Controls.Add(_formatButton);
        trButtons.Controls.Add(_clearButton);

        detail.Controls.Add(_origLabel, 0, 0);
        detail.Controls.Add(_trLabel, 1, 0);
        detail.Controls.Add(_origBox, 0, 1);
        detail.Controls.Add(_translationBox, 1, 1);
        detail.Controls.Add(origButtons, 0, 2);
        detail.Controls.Add(trButtons, 1, 2);
        detail.Controls.Add(_issueLabel, 0, 3);
        detail.SetColumnSpan(_issueLabel, 2);

        _rightSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel2 };
        _rightSplit.Panel1.Controls.Add(_grid);
        _rightSplit.Panel2.Controls.Add(detail);

        // Right side: the Fill control first, then the docked-top ones (later-added controls dock first).
        var right = new Panel { Dock = DockStyle.Fill };
        right.Controls.Add(_rightSplit);
        right.Controls.Add(_infoLabel);
        right.Controls.Add(toolbar);

        var left = new Panel { Dock = DockStyle.Fill };
        left.Controls.Add(_fileList);
        left.Controls.Add(_fileCountLabel);
        left.Controls.Add(_onlyTranslated);
        left.Controls.Add(_hideEmpty);
        left.Controls.Add(_filterBox);

        _mainSplit = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
        _mainSplit.Panel1.Controls.Add(left);
        _mainSplit.Panel2.Controls.Add(right);

        _statusStrip.Items.Add(_statusLabel);

        Controls.Add(_mainSplit);
        Controls.Add(_statusStrip);
        Controls.Add(_menu);
    }

    private void WireEvents()
    {
        // ---- file list
        _filterBox.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.Enter)
            {
                eventArgs.SuppressKeyPress = true;
                Reload();
            }
        };
        _hideEmpty.CheckedChanged += (_, _) => Reload();
        _onlyTranslated.CheckedChanged += (_, _) => FillFileList();
        _fileList.SelectedIndexChanged += (_, _) => OnFileSelectionChanged();
        _fileList.DrawItem += (_, eventArgs) =>
        {
            eventArgs.DrawBackground();
            if (eventArgs.Index >= 0 && eventArgs.Index < _fileList.Items.Count && _fileList.Items[eventArgs.Index] is PackFile pack)
            {
                bool selected = (eventArgs.State & DrawItemState.Selected) != 0;
                Color color = selected ? SystemColors.HighlightText : pack.TranslatedCount > 0 ? Color.DarkGreen : SystemColors.WindowText;
                TextRenderer.DrawText(eventArgs.Graphics, pack.ToString(), eventArgs.Font, eventArgs.Bounds, color,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
            eventArgs.DrawFocusRectangle();
        };
        // After a search over all files, clicking the file that is already selected shows that file again.
        _fileList.MouseUp += (_, _) =>
        {
            if (_resultsMode && !_busy && _fileList.SelectedItems.Count == 1 && _fileList.SelectedItem is PackFile selected)
                OpenFile(selected);
        };

        // ---- filter bar
        _searchBox.TextChanged += (_, _) => RestartFilterTimer();
        _fieldBox.SelectedIndexChanged += (_, _) => RestartFilterTimer();
        _caseBox.CheckedChanged += (_, _) => RestartFilterTimer();
        _wordBox.CheckedChanged += (_, _) => RestartFilterTimer();
        _regexBox.CheckedChanged += (_, _) => RestartFilterTimer();
        _showBox.SelectedIndexChanged += (_, _) => RestartFilterTimer();
        _lenOfBox.SelectedIndexChanged += (_, _) => RestartFilterTimer();
        _minBox.TextChanged += (_, _) => RestartFilterTimer();
        _maxBox.TextChanged += (_, _) => RestartFilterTimer();
        _sortBox.SelectedIndexChanged += (_, _) => RestartFilterTimer();
        _searchBox.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.Enter)
            {
                eventArgs.SuppressKeyPress = true;
                _filterTimer.Stop();
                ApplyView();
                _grid.Focus();
            }
        };
        _searchAllButton.Click += async (_, _) => await SearchAllFilesAsync(FilterSpec());

        // ---- grid (virtual mode)
        _grid.CellValueNeeded += (_, eventArgs) =>
        {
            if (eventArgs.RowIndex < 0 || eventArgs.RowIndex >= _shown.Count) return;
            eventArgs.Value = CellText(_shown[eventArgs.RowIndex], eventArgs.ColumnIndex);
        };
        _grid.CellValuePushed += (_, eventArgs) =>
        {
            if (eventArgs.RowIndex < 0 || eventArgs.RowIndex >= _shown.Count || eventArgs.ColumnIndex != _colTranslation.Index) return;
            SetTranslation(_shown[eventArgs.RowIndex], TextTools.FromBox(Convert.ToString(eventArgs.Value) ?? ""));
        };
        _grid.CellFormatting += (_, eventArgs) =>
        {
            if (eventArgs.RowIndex < 0 || eventArgs.RowIndex >= _shown.Count || eventArgs.CellStyle == null) return;
            RowRef rowRef = _shown[eventArgs.RowIndex];
            if (rowRef.HasWarning) eventArgs.CellStyle.BackColor = Color.FromArgb(255, 235, 200);
            else if (rowRef.Issues.Count > 0) eventArgs.CellStyle.BackColor = Color.FromArgb(255, 250, 215);
            else if (rowRef.IsTranslated) eventArgs.CellStyle.BackColor = Color.FromArgb(232, 247, 232);
        };
        _grid.SelectionChanged += (_, _) =>
        {
            if (!_suppressSelection) ShowDetail();
        };
        _grid.CellDoubleClick += (_, eventArgs) =>
        {
            if (eventArgs.RowIndex < 0) return;
            if (_resultsMode && eventArgs.ColumnIndex != _colTranslation.Index) OpenResultFile(eventArgs.RowIndex);
        };
        _grid.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.Enter && !eventArgs.Control && !_grid.IsCurrentCellInEditMode)
            {
                eventArgs.SuppressKeyPress = true;
                _translationBox.Focus();
            }
        };

        // ---- detail
        _translationBox.TextChanged += (_, _) =>
        {
            if (_suppressBox || _detailRef == null) return;
            _detailDirty = true;
            _commitTimer.Stop();
            _commitTimer.Start();
            UpdateIssueLabel(live: true);
        };
        _translationBox.Leave += (_, _) => CommitDetail();
        _copyOrigButton.Click += (_, _) => CopyOriginalOfDetail();
        _useOrigButton.Click += (_, _) => StartFromOriginal();
        _codeButton.Click += (_, _) => ShowCodeMenu();
        _nextUntranslatedButton.Click += (_, _) => GoToNextUntranslated();
        _clearButton.Click += (_, _) => ClearDetailTranslation();
        _formatButton.Click += (_, _) => AutoFormatCurrent();

        _gridMenu.Items.Add("Copy original", null, (_, _) => CopyOriginals());
        _gridMenu.Items.Add("Copy translation", null, (_, _) => CopyTranslations());
        _gridMenu.Items.Add("Copy ID", null, (_, _) => CopyIds());
        _gridMenu.Items.Add("Copy original + translation", null, (_, _) => CopyBoth());
        _gridMenu.Items.Add(new ToolStripSeparator());
        _gridMenu.Items.Add("Clear translation of selected rows", null, (_, _) => ClearSelectedTranslations());
    }

    private void RestartFilterTimer()
    {
        _filterTimer.Stop();
        _filterTimer.Start();
    }

    // ---- Menu -------------------------------------------------------------
    private static ToolStripMenuItem Item(string text, Action action, string shortcut = "")
    {
        var item = new ToolStripMenuItem(text);
        if (shortcut.Length > 0) item.ShortcutKeyDisplayString = shortcut;
        item.Click += (_, _) => action();
        return item;
    }

    private static ToolStripMenuItem AsyncItem(string text, Func<Task> action, string shortcut = "")
    {
        var item = new ToolStripMenuItem(text);
        if (shortcut.Length > 0) item.ShortcutKeyDisplayString = shortcut;
        item.Click += async (_, _) => await action();
        return item;
    }

    private void BuildMenu()
    {
        var packs = new ToolStripMenuItem("&Packs");
        var export = new ToolStripMenuItem("Export translations");
        export.DropDownItems.Add(Item("Rows shown…", () => ExportRows(_shown.ToList(), "rows")));
        export.DropDownItems.Add(Item("Open file (every row)…", () => ExportRows(_source.ToList(), "file")));
        export.DropDownItems.Add(AsyncItem("ALL stored translations…", ExportAllAsync));
        packs.DropDownItems.AddRange(new ToolStripItem[]
        {
            AsyncItem("Save translated .pak (open file)", SaveCurrentPackAsync, "Ctrl+S"),
            AsyncItem("Save selected files", SaveSelectedPacksAsync),
            AsyncItem("Save ALL files that have translations…", SaveAllPacksAsync),
            AsyncItem("Restore originals (.old) of selected files…", RestoreSelectedAsync),
            AsyncItem("Read translations back from saved packs (.old pairs)…", ReadBackSavedPacksAsync),
            new ToolStripSeparator(),
            export,
            AsyncItem("Import translations from CSV / TSV…", ImportSheetAsync),
            AsyncItem("Import translations from a reference folder…", ImportReferenceAsync),
            new ToolStripSeparator(),
            Item("Export the rows shown as a text file…", ExportShownText),
        });

        var edit = new ToolStripMenuItem("&Edit");
        edit.DropDownItems.AddRange(new ToolStripItem[]
        {
            Item("Undo", DoUndo, "Ctrl+Z"),
            Item("Redo", DoRedo, "Ctrl+Y"),
            new ToolStripSeparator(),
            Item("Copy original", CopyOriginals, "Ctrl+C (in the grid)"),
            Item("Copy translation", CopyTranslations, "Ctrl+Shift+C"),
            new ToolStripSeparator(),
            Item("Filter box", () => { _searchBox.Focus(); _searchBox.SelectAll(); }, "Ctrl+F"),
            AsyncItem("Search all files", () => SearchAllFilesAsync(FilterSpec()), "Ctrl+Shift+F"),
            Item("Find and Replace…", () => ShowFind(), "Ctrl+H"),
            Item("Find next", () => FindStep(false), "F3"),
            Item("Find previous", () => FindStep(true), "Shift+F3"),
            Item("Go to ID…", GoToId, "Ctrl+G"),
            new ToolStripSeparator(),
            Item("Next untranslated", GoToNextUntranslated, "Ctrl+U"),
            Item("Next row with notes", () => GoToIssue(false), "F8"),
            Item("Previous row with notes", () => GoToIssue(true), "Shift+F8"),
            Item("Next row", () => MoveRow(1), "Alt+Down"),
            Item("Previous row", () => MoveRow(-1), "Alt+Up"),
        });

        var tools = new ToolStripMenuItem("T&ools");
        tools.DropDownItems.AddRange(new ToolStripItem[]
        {
            Item("Check the open file and show rows with notes", CheckOpenFile, "F7"),
            AsyncItem("Check all listed files…", CheckAllFilesAsync),
            new ToolStripSeparator(),
            Item("Auto format current row (match the original's lines)", AutoFormatCurrent, "Ctrl+Shift+L"),
            Item("Auto format rows / whole file…", AutoFormatRows),
            Item("Wrap translations at N characters…", WrapTool),
            AsyncItem("Fill rows whose Japanese text is identical…", FillIdenticalAsync),
            AsyncItem("Progress / statistics…", StatisticsAsync),
            new ToolStripSeparator(),
            Item("Settings (line limits, checks)…", ShowViewerSettings),
        });

        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add(Item("Shortcuts", ShowHelp, "F1"));

        _menu.Items.AddRange(new ToolStripItem[] { packs, edit, tools, help });
    }
}
