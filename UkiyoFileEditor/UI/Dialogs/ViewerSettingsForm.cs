using System.Text.RegularExpressions;

namespace UkiyoFileEditor.UI.Dialogs;

/// <summary>Limits and switches used by the checker (notes column, F7 and "Check all files").</summary>
public sealed class ViewerSettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly NumericUpDown _maxLineChars = new() { Minimum = 0, Maximum = 400, Width = 70 };
    private readonly NumericUpDown _maxLines = new() { Minimum = 0, Maximum = 50, Width = 70 };
    private readonly CheckBox _nonAscii = new() { Text = "Warn about characters outside plain ASCII", AutoSize = true };
    private readonly TextBox _allowed = new() { Width = 300 };
    private readonly CheckBox _japanese = new() { Text = "Warn when Japanese text is left in a translation", AutoSize = true };
    private readonly CheckBox _spaceIcons = new() { Text = "Note a missing space next to button icons", AutoSize = true };
    private readonly TextBox _iconPattern = new() { Width = 300 };

    public ViewerSettingsForm(AppSettings settings)
    {
        _settings = settings;

        Text = "Checker settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _maxLineChars.Value = Math.Clamp(settings.MaxLineChars, 0, 400);
        _maxLines.Value = Math.Clamp(settings.MaxLines, 0, 50);
        _nonAscii.Checked = settings.WarnNonAscii;
        _allowed.Text = settings.AllowedChars;
        _japanese.Checked = settings.WarnJapanese;
        _spaceIcons.Checked = settings.SpaceAroundIcons;
        _iconPattern.Text = settings.IconTagPattern;

        var toolTip = new ToolTip { AutoPopDelay = 20000 };
        toolTip.SetToolTip(_maxLineChars, "0 = no limit. Visible characters per line; codes count 0 and button icons count 1.");
        toolTip.SetToolTip(_maxLines, "0 = no limit.");
        toolTip.SetToolTip(_allowed, "Characters that are fine even though they are not plain ASCII (for example é or …).");
        toolTip.SetToolTip(_iconPattern, "Regular expression for the codes that show a button icon. They count as one character and should have spaces around them.");

        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(12) };
        grid.Controls.Add(new Label { Text = "Longest line (characters):", AutoSize = true, Padding = new Padding(0, 6, 8, 0) }, 0, 0);
        grid.Controls.Add(_maxLineChars, 1, 0);
        grid.Controls.Add(new Label { Text = "Most lines per message:", AutoSize = true, Padding = new Padding(0, 6, 8, 0) }, 0, 1);
        grid.Controls.Add(_maxLines, 1, 1);
        grid.Controls.Add(_nonAscii, 1, 2);
        grid.Controls.Add(new Label { Text = "Allowed extra characters:", AutoSize = true, Padding = new Padding(0, 6, 8, 0) }, 0, 3);
        grid.Controls.Add(_allowed, 1, 3);
        grid.Controls.Add(_japanese, 1, 4);
        grid.Controls.Add(_spaceIcons, 1, 5);
        grid.Controls.Add(new Label { Text = "Button icon code (regex):", AutoSize = true, Padding = new Padding(0, 6, 8, 0) }, 0, 6);
        grid.Controls.Add(_iconPattern, 1, 6);

        var button = new Button { Text = "OK", AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(button);
        grid.Controls.Add(buttons, 1, 7);

        Controls.Add(grid);
        AcceptButton = button;
        CancelButton = cancel;
        button.Click += (_, _) => Accept();
    }

    private void Accept()
    {
        string pattern = _iconPattern.Text.Trim();
        if (pattern.Length == 0) pattern = TextTools.DefaultIconPattern;
        try
        {
            _ = new Regex(pattern);
        }
        catch (ArgumentException exception)
        {
            MessageBox.Show(this, "The button icon pattern is not a valid regular expression:\n\n" + exception.Message, Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _settings.MaxLineChars = (int)_maxLineChars.Value;
        _settings.MaxLines = (int)_maxLines.Value;
        _settings.WarnNonAscii = _nonAscii.Checked;
        _settings.AllowedChars = _allowed.Text;
        _settings.WarnJapanese = _japanese.Checked;
        _settings.SpaceAroundIcons = _spaceIcons.Checked;
        _settings.IconTagPattern = pattern;
        _settings.Save();
        DialogResult = DialogResult.OK;
        Close();
    }
}
