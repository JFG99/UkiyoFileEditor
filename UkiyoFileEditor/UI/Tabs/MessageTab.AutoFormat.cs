namespace UkiyoFileEditor.UI.Tabs;

/// <summary>
/// Auto format: re-breaks translations into lines that follow the layout of the Japanese original
/// (see <see cref="AutoFormatter"/>). One row (button / Ctrl+Shift+L) or many rows (Tools menu).
/// </summary>
public sealed partial class MessageTab
{
    private AutoFormatOptions FormatOptions() =>
        new(_settings.AutoFormatSentences, _settings.AutoFormatExtraLines, _check.MaxLineChars, _check.Icon);

    /// <summary>Formats the translation of the current row. Uses the settings last chosen in the "Auto format rows…" window.</summary>
    private void AutoFormatCurrent()
    {
        CommitDetail();
        RowRef? rowRef = _detailRef ?? CurrentRef();
        if (rowRef == null)
        {
            _statusLabel.Text = "Select a row first.";
            return;
        }
        if (!rowRef.IsTranslated)
        {
            _statusLabel.Text = "This row has no translation to format.";
            return;
        }

        AutoFormatOptions options = FormatOptions();
        string formatted = AutoFormatter.Format(rowRef.Row.Text, rowRef.Translation, options);
        if (formatted == rowRef.Translation)
        {
            _statusLabel.Text = "The line breaks already follow the original.";
            return;
        }

        SetTranslation(rowRef, formatted, "Auto format", coalesce: false);
        int lines = formatted.Split('\n').Length;
        int originalLines = AutoFormatter.ParagraphLines(rowRef.Original).Sum();
        _statusLabel.Text = $"Formatted: {lines} line(s); the original has {originalLines}." +
                       (lines > originalLines + _settings.AutoFormatExtraLines ? " More lines were needed to keep every line within the line limit." : "") + " Ctrl+Z undoes it.";
    }

    /// <summary>Asks for the settings and formats the selected rows, the rows shown, or the whole open file.</summary>
    private void AutoFormatRows()
    {
        CommitDetail();
        using var dialog = new AutoFormatDialog(_settings.AutoFormatSentences, _settings.AutoFormatExtraLines);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _settings.AutoFormatSentences = dialog.SentencesOwnLine;
        _settings.AutoFormatExtraLines = dialog.ExtraLines;
        _settings.Save();
        AutoFormatOptions options = FormatOptions();

        List<RowRef> targets = dialog.Scope switch
        {
            0 => SelectedRefs(),
            1 => _shown.ToList(),
            _ => _source.ToList(),
        };

        var changes = new List<TranslationChange>();
        int translated = 0, tooLong = 0;
        foreach (RowRef rowRef in targets)
        {
            if (!rowRef.IsTranslated) continue;
            translated++;
            string formatted = AutoFormatter.Format(rowRef.Row.Text, rowRef.Translation, options);
            if (formatted == rowRef.Translation) continue;

            TranslationChange? change = _store.Set(rowRef.File, rowRef.Key, rowRef.Row.Text, formatted);
            if (change == null) continue;
            changes.Add(change);
            if (formatted.Split('\n').Length > AutoFormatter.ParagraphLines(rowRef.Original).Sum() + _settings.AutoFormatExtraLines) tooLong++;
        }

        if (changes.Count == 0)
        {
            _statusLabel.Text = translated == 0
                ? "There are no translated rows to format."
                : "Nothing changed: every translation already follows its original.";
            return;
        }

        _undo.Push("Auto format", changes);
        AfterBulkChange(changes);
        _statusLabel.Text = $"Formatted {changes.Count:N0} of {translated:N0} translated row(s). Ctrl+Z undoes it." +
                       (tooLong > 0 ? $"  {tooLong:N0} needed more lines than the original to stay within the line limit." : "");
    }
}
