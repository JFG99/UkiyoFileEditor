namespace UkiyoFileEditor.UI.Dialogs;

/// <summary>Asks for the line length and which translations to wrap.</summary>
public sealed class WrapDialog : Form
{
    private readonly NumericUpDown _max = new() { Minimum = 5, Maximum = 400, Width = 70 };
    private readonly CheckBox _rejoin = new() { Text = "Join the existing lines first (wrap from scratch)", AutoSize = true, Checked = true };
    private readonly ComboBox _scope = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };

    public int MaxChars => (int)_max.Value;
    public bool Rejoin => _rejoin.Checked;

    /// <summary>0 = selected rows, 1 = rows shown, 2 = the open list (whole file or search results), 3 = every stored translation.</summary>
    public int Scope => Math.Max(0, _scope.SelectedIndex);

    public WrapDialog(int initialMaxChars)
    {
        Text = "Wrap translations";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _max.Value = Math.Clamp(initialMaxChars, 5, 400);
        _scope.Items.AddRange(new object[]
        {
            "Selected rows",
            "Rows shown",
            "Open list (whole file or search results)",
            "ALL stored translations of every file",
        });
        _scope.SelectedIndex = 2;

        var toolTip = new ToolTip { AutoPopDelay = 20000 };
        toolTip.SetToolTip(_max, "Lines are broken at spaces so that no line is longer than this many visible characters. Codes count 0, button icons count 1.");
        toolTip.SetToolTip(_rejoin, "On: the line breaks of the translation are removed and the text is wrapped again. Off: every existing line is only split where it is too long.");

        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(12) };
        grid.Controls.Add(new Label { Text = "Characters per line:", AutoSize = true, Padding = new Padding(0, 6, 8, 0) }, 0, 0);
        grid.Controls.Add(_max, 1, 0);
        grid.Controls.Add(new Label { Text = "Wrap:", AutoSize = true, Padding = new Padding(0, 6, 8, 0) }, 0, 1);
        grid.Controls.Add(_scope, 1, 1);
        grid.Controls.Add(_rejoin, 1, 2);

        var button = new Button { Text = "Wrap", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(button);
        grid.Controls.Add(buttons, 1, 3);

        Controls.Add(grid);
        AcceptButton = button;
        CancelButton = cancel;
    }
}
