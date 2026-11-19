namespace UkiyoFileEditor.UI.Dialogs;

/// <summary>
/// Modeless Find and Replace window. It only collects what to look for; the Messages tab does the work through the events.
/// Replace only ever changes translations.
/// </summary>
public sealed class FindReplaceForm : Form
{
    private readonly TextBox _find = new() { Width = 320 };
    private readonly TextBox _replace = new() { Width = 320 };
    private readonly CheckBox _case = new() { Text = "Match case", AutoSize = true };
    private readonly CheckBox _word = new() { Text = "Whole word", AutoSize = true };
    private readonly CheckBox _regex = new() { Text = "Regular expression", AutoSize = true };
    private readonly ComboBox _field = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly ComboBox _scope = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly Label _message = new() { AutoSize = true, MaximumSize = new Size(480, 0), ForeColor = Color.DarkSlateGray };

    private readonly Button _findNext = new() { Text = "Find next", AutoSize = true };
    private readonly Button _findPrevious = new() { Text = "Find previous", AutoSize = true };
    private readonly Button _findAll = new() { Text = "Find all (filter)", AutoSize = true };
    private readonly Button _replaceOne = new() { Text = "Replace", AutoSize = true };
    private readonly Button _replaceAll = new() { Text = "Replace all", AutoSize = true };
    private readonly Button _close = new() { Text = "Close", AutoSize = true };

    public event Action<SearchSpec, bool>? FindRequested;        // spec, backwards
    public event Action<SearchSpec, bool>? FindAllRequested;     // spec, all files
    public event Action<SearchSpec>? ReplaceRequested;
    public event Action<SearchSpec, bool>? ReplaceAllRequested;  // spec, all files

    public FindReplaceForm(SearchSpec initial)
    {
        Text = "Find and Replace";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _field.Items.AddRange(new object[] { "All fields", "ID / Entry", "Original", "Translation" });
        _scope.Items.AddRange(new object[] { "Current list (open file / results)", "All files (every stored translation)" });
        _scope.SelectedIndex = 0;

        _find.Text = initial.Find;
        _replace.Text = initial.Replace;
        _case.Checked = initial.MatchCase;
        _word.Checked = initial.WholeWord;
        _regex.Checked = initial.UseRegex;
        _field.SelectedIndex = (int)initial.Field;

        var tips = new ToolTip { AutoPopDelay = 20000 };
        tips.SetToolTip(_replace, "In the Replace box \\n is a line break, \\t a tab and \\\\ a backslash. In regular expression mode $1, $2… work too.");
        tips.SetToolTip(_findAll, "Filters the list to the matching rows. With \"All files\" every file is searched.");
        tips.SetToolTip(_replaceOne, "Replaces in the translation of the selected row and moves to the next match.");
        tips.SetToolTip(_replaceAll, "Replaces in all translations of the chosen scope. Ctrl+Z in the Messages tab undoes it.");

        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(10), Dock = DockStyle.Fill };
        grid.Controls.Add(new Label { Text = "Find:", AutoSize = true, Padding = new Padding(0, 6, 8, 0) }, 0, 0);
        grid.Controls.Add(_find, 1, 0);
        grid.Controls.Add(new Label { Text = "Replace with:", AutoSize = true, Padding = new Padding(0, 6, 8, 0) }, 0, 1);
        grid.Controls.Add(_replace, 1, 1);

        var options = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(420, 0) };
        options.Controls.Add(_case);
        options.Controls.Add(_word);
        options.Controls.Add(_regex);
        grid.Controls.Add(options, 1, 2);

        grid.Controls.Add(new Label { Text = "Look in:", AutoSize = true, Padding = new Padding(0, 6, 8, 0) }, 0, 3);
        grid.Controls.Add(_field, 1, 3);
        grid.Controls.Add(new Label { Text = "Scope:", AutoSize = true, Padding = new Padding(0, 6, 8, 0) }, 0, 4);
        grid.Controls.Add(_scope, 1, 4);

        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(420, 0) };
        buttons.Controls.Add(_findNext);
        buttons.Controls.Add(_findPrevious);
        buttons.Controls.Add(_findAll);
        buttons.Controls.Add(_replaceOne);
        buttons.Controls.Add(_replaceAll);
        buttons.Controls.Add(_close);
        grid.Controls.Add(buttons, 1, 5);
        grid.Controls.Add(_message, 1, 6);

        Controls.Add(grid);
        AcceptButton = _findNext;

        _findNext.Click += (_, _) => FindRequested?.Invoke(Spec, false);
        _findPrevious.Click += (_, _) => FindRequested?.Invoke(Spec, true);
        _findAll.Click += (_, _) => FindAllRequested?.Invoke(Spec, AllFiles);
        _replaceOne.Click += (_, _) => ReplaceRequested?.Invoke(Spec);
        _replaceAll.Click += (_, _) => ReplaceAllRequested?.Invoke(Spec, AllFiles);
        _close.Click += (_, _) => Hide();
    }

    public SearchSpec Spec => new()
    {
        Find = _find.Text,
        Replace = _replace.Text,
        MatchCase = _case.Checked,
        WholeWord = _word.Checked,
        UseRegex = _regex.Checked,
        Field = (SearchField)Math.Max(0, _field.SelectedIndex),
    };

    public bool AllFiles => _scope.SelectedIndex == 1;

    public void SetMessage(string text) => _message.Text = text;

    /// <summary>Called when the window is (re)opened: a one-line selection from the main window becomes the search text.</summary>
    public void Prime(string text)
    {
        if (text.Length > 0 && text.IndexOfAny(new[] { '\r', '\n' }) < 0) _find.Text = text;
        _find.Focus();
        _find.SelectAll();
    }

    protected override void OnFormClosing(FormClosingEventArgs eventArgs)
    {
        if (eventArgs.CloseReason == CloseReason.UserClosing)
        {
            eventArgs.Cancel = true;
            Hide();
        }
        base.OnFormClosing(eventArgs);
    }
}
