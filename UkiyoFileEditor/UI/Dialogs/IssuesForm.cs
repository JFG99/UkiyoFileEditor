namespace UkiyoFileEditor.UI.Dialogs;

/// <summary>
/// Modeless list of the problems found by "Check all files". Double-click a line to open that file and row in the Messages tab.
/// </summary>
public sealed class IssuesForm : Form
{
    private const int MaxLines = 10000;

    private readonly List<IssueItem> _items;
    private readonly DataGridView _grid = new();
    private readonly CheckBox _warningsOnly = new() { Text = "Warnings only", AutoSize = true, Checked = true };
    private readonly Label _summary = new() { AutoSize = true, Padding = new Padding(0, 5, 0, 0) };
    private List<IssueItem> _shown = new();

    /// <summary>Raised with (file, key) when a line is double-clicked.</summary>
    public event Action<string, string>? JumpRequested;

    public IssuesForm(List<IssueItem> items, int filesChecked, int rowsChecked)
    {
        _items = items;
        Text = $"Check results - {rowsChecked:N0} translated row(s) in {filesChecked:N0} file(s)";
        Width = 1100;
        Height = 600;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells;
        _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _grid.DefaultCellStyle.Font = new Font("Yu Gothic UI", 10f);
        _grid.VirtualMode = true;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Level", Width = 70 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "File", Width = 220 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID", Width = 190 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Problem", Width = 280 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Translation", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });

        _grid.CellValueNeeded += (_, eventArgs) =>
        {
            if (eventArgs.RowIndex < 0 || eventArgs.RowIndex >= _shown.Count) return;
            IssueItem item = _shown[eventArgs.RowIndex];
            eventArgs.Value = eventArgs.ColumnIndex switch
            {
                0 => item.Severity == Severity.Warning ? "Warning" : "Note",
                1 => item.File,
                2 => item.Id,
                3 => item.Message,
                _ => TextTools.OneLine(item.Translation, 300),
            };
        };
        _grid.CellDoubleClick += (_, eventArgs) => Jump(eventArgs.RowIndex);
        _grid.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.Enter)
            {
                eventArgs.SuppressKeyPress = true;
                if (_grid.CurrentRow != null) Jump(_grid.CurrentRow.Index);
            }
        };
        _warningsOnly.CheckedChanged += (_, _) => Fill();

        var copy = new Button { Text = "Copy list", AutoSize = true };
        copy.Click += (_, _) => CopyList();

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        toolbar.Controls.Add(_warningsOnly);
        toolbar.Controls.Add(copy);
        toolbar.Controls.Add(_summary);

        // Fill control first, then the docked one.
        Controls.Add(_grid);
        Controls.Add(toolbar);

        Fill();
    }

    private void Fill()
    {
        _shown = (_warningsOnly.Checked ? _items.Where(issueItem => issueItem.Severity == Severity.Warning) : _items).Take(MaxLines).ToList();
        _grid.RowCount = 0;
        _grid.RowCount = _shown.Count;
        int warnings = _items.Count(issueItem => issueItem.Severity == Severity.Warning);
        _summary.Text = $"{warnings:N0} warning(s), {_items.Count - warnings:N0} note(s). Double-click a line to open it." +
                        (_shown.Count >= MaxLines ? $" (first {MaxLines:N0} shown)" : "");
    }

    private void Jump(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _shown.Count) return;
        IssueItem item = _shown[rowIndex];
        JumpRequested?.Invoke(item.File, item.Key);
    }

    private void CopyList()
    {
        string text = string.Join("\r\n", _shown.Select(issueItem => $"{issueItem.File}\t{issueItem.Id}\t{issueItem.Message}"));
        if (text.Length == 0) return;
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.ExternalException) { }
    }
}
