using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace UkiyoFileEditor.UI.Tabs;

/// <summary>Keyboard shortcuts of the tab.</summary>
public sealed partial class MessageTab
{
    // ---- Key handling -------------------------------------------------------
    private static Control? FocusedControl()
    {
        IntPtr handle = GetFocus();
        return handle == IntPtr.Zero ? null : Control.FromHandle(handle);
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (!Visible) return base.ProcessCmdKey(ref message, keyData);

        Control? focused = FocusedControl();
        bool inText = focused is TextBoxBase;
        bool editingCell = _grid.IsCurrentCellInEditMode;

        switch (keyData)
        {
            case Keys.Control | Keys.Z:
                if (inText || editingCell) return base.ProcessCmdKey(ref message, keyData);
                DoUndo();
                return true;
            case Keys.Control | Keys.Y:
                if (inText || editingCell) return base.ProcessCmdKey(ref message, keyData);
                DoRedo();
                return true;
            case Keys.Control | Keys.C:
                if (inText || editingCell || !ReferenceEquals(focused, _grid)) return base.ProcessCmdKey(ref message, keyData);
                CopyOriginals();
                return true;
            case Keys.Control | Keys.Shift | Keys.C:
                if (editingCell || inText) return base.ProcessCmdKey(ref message, keyData);
                CopyTranslations();
                return true;
            case Keys.Control | Keys.F:
                _searchBox.Focus();
                _searchBox.SelectAll();
                return true;
            case Keys.Control | Keys.Shift | Keys.F:
                _ = SearchAllFilesAsync(FilterSpec());
                return true;
            case Keys.Control | Keys.H:
                ShowFind();
                return true;
            case Keys.F3:
                FindStep(false);
                return true;
            case Keys.Shift | Keys.F3:
                FindStep(true);
                return true;
            case Keys.Control | Keys.G:
                GoToId();
                return true;
            case Keys.Control | Keys.S:
                _ = SaveCurrentPackAsync();
                return true;
            case Keys.Control | Keys.Shift | Keys.L:
                AutoFormatCurrent();
                return true;
            case Keys.F7:
                CheckOpenFile();
                return true;
            case Keys.F8:
                GoToIssue(false);
                return true;
            case Keys.Shift | Keys.F8:
                GoToIssue(true);
                return true;
            case Keys.Control | Keys.U:
                GoToNextUntranslated();
                return true;
            case Keys.Control | Keys.Return:
                MoveRow(1, focusBox: true);
                return true;
            case Keys.Alt | Keys.Down:
                MoveRow(1);
                return true;
            case Keys.Alt | Keys.Up:
                MoveRow(-1);
                return true;
            case Keys.Control | Keys.PageDown:
                MoveFile(1);
                return true;
            case Keys.Control | Keys.PageUp:
                MoveFile(-1);
                return true;
            case Keys.F1:
                ShowHelp();
                return true;
        }
        return base.ProcessCmdKey(ref message, keyData);
    }
}
