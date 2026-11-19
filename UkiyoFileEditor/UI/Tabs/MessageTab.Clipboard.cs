using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace UkiyoFileEditor.UI.Tabs;

/// <summary>Copying rows to the clipboard.</summary>
public sealed partial class MessageTab
{
    // ---- Clipboard ------------------------------------------------------------
    private void SetClipboard(string text)
    {
        if (text.Length == 0) { _statusLabel.Text = "Nothing to copy."; return; }
        try
        {
            Clipboard.SetText(text);
            _statusLabel.Text = $"Copied {text.Length:N0} character(s).";
        }
        catch (Exception exception) when (exception is ExternalException or ThreadStateException)
        {
            _statusLabel.Text = "The clipboard is busy; try again.";
        }
    }

    private void CopyOriginals() =>
        SetClipboard(string.Join("\r\n\r\n", SelectedRefs().Select(rowRef => TextTools.ToBox(rowRef.Original))));

    private void CopyTranslations() =>
        SetClipboard(string.Join("\r\n\r\n", SelectedRefs().Select(rowRef => TextTools.ToBox(rowRef.Translation))));

    private void CopyIds() =>
        SetClipboard(string.Join("\r\n", SelectedRefs().Select(rowRef => rowRef.Row.Id.Length > 0 ? rowRef.Row.Id : rowRef.Row.Key)));

    private void CopyBoth()
    {
        var builder = new StringBuilder();
        foreach (RowRef rowRef in SelectedRefs())
        {
            builder.Append("# ").AppendLine(rowRef.Row.Id.Length > 0 ? rowRef.Row.Id : rowRef.Row.Key);
            builder.AppendLine(TextTools.ToBox(rowRef.Original));
            if (rowRef.IsTranslated) builder.AppendLine(TextTools.ToBox(rowRef.Translation));
            builder.AppendLine();
        }
        SetClipboard(builder.ToString());
    }
}
