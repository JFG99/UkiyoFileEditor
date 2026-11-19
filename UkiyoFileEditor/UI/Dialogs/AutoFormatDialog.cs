namespace UkiyoFileEditor.UI.Dialogs;

/// <summary>Asks how the auto format should work and which translations it should re-break.</summary>
public sealed class AutoFormatDialog : Form
{
    private readonly NumericUpDown _extra = new() { Minimum = 0, Maximum = 10, Width = 70 };
    private readonly CheckBox _sentences = new() { Text = "Start each sentence on a new line (when the original does so)", AutoSize = true };
    private readonly ComboBox _scope = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };

    public bool SentencesOwnLine => _sentences.Checked;
    public int ExtraLines => (int)_extra.Value;

    /// <summary>0 = selected rows, 1 = rows shown, 2 = the open list (whole file or search results).</summary>
    public int Scope => Math.Max(0, _scope.SelectedIndex);

    public AutoFormatDialog(bool sentences, int extraLines)
    {
        Text = "Auto format translations";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _extra.Value = Math.Clamp(extraLines, 0, 10);
        _sentences.Checked = sentences;
        _scope.Items.AddRange(new object[]
        {
            "Selected rows",
            "Rows shown",
            "Open list (whole file or search results)",
        });
        _scope.SelectedIndex = 2;

        var toolTip = new ToolTip { AutoPopDelay = 20000 };
        toolTip.SetToolTip(_extra, "0 = the translation gets exactly as many lines as the Japanese original. Raise it to allow a few more lines.");
        toolTip.SetToolTip(_sentences, "Only used when the original ends its sentences at the end of a line, as in the Japanese text of most system messages. The lines are then shared out between the sentences so that all lines come out about equally long.");

        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(12) };
        grid.Controls.Add(new Label { Text = "Extra lines allowed beyond the original:", AutoSize = true, Padding = new Padding(0, 6, 8, 0) }, 0, 0);
        grid.Controls.Add(_extra, 1, 0);
        grid.Controls.Add(new Label { Text = "Format:", AutoSize = true, Padding = new Padding(0, 6, 8, 0) }, 0, 1);
        grid.Controls.Add(_scope, 1, 1);
        grid.Controls.Add(_sentences, 1, 2);

        var button = new Button { Text = "Format", DialogResult = DialogResult.OK, AutoSize = true };
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
