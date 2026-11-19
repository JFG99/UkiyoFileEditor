namespace UkiyoFileEditor.Formats.Stpk;

/// <summary>One displayable row of a message pack.</summary>
/// <param name="File">Name of the pack the row belongs to (filled in by the caller).</param>
/// <param name="Key">Name of the text entry.</param>
/// <param name="Id">Content of the ID entry that precedes the text.</param>
/// <param name="Text">The message text.</param>
/// <param name="EntryIndex">Index of the text entry inside the STPK archive (-1 when the row has no text entry); used to write translations back.</param>
public sealed record MessageRow(string File, string Key, string Id, string Text, int EntryIndex = -1);
