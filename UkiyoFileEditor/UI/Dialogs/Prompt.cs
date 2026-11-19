namespace UkiyoFileEditor.UI.Dialogs;

/// <summary>A tiny "type a line of text" dialog.</summary>
public static class Prompt
{
    public static string? Ask(IWin32Window? owner, string title, string label, string initial)
    {
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(420, 112),
        };
        var caption = new Label { Text = label, AutoSize = true, Left = 12, Top = 12 };
        var inputBox = new TextBox { Left = 12, Top = 36, Width = 396, Text = initial };
        var button = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 252, Top = 74, Width = 75 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 333, Top = 74, Width = 75 };
        form.Controls.Add(caption);
        form.Controls.Add(inputBox);
        form.Controls.Add(button);
        form.Controls.Add(cancel);
        form.AcceptButton = button;
        form.CancelButton = cancel;
        form.Shown += (_, _) =>
        {
            inputBox.Focus();
            inputBox.SelectAll();
        };
        return form.ShowDialog(owner) == DialogResult.OK ? inputBox.Text : null;
    }
}
