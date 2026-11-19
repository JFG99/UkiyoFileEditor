namespace UkiyoFileEditor.Models;

/// <summary>One line of a translation sheet (CSV / TSV): which row of which file gets which translation.</summary>
/// <param name="File">Pack the row belongs to (path relative to the opened folder).</param>
/// <param name="Key">The row's ID, or its entry name when it has no ID.</param>
/// <param name="Translation">The translated text.</param>
public sealed record SheetRow(string File, string Key, string Translation);
