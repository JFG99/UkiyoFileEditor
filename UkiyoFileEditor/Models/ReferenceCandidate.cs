namespace UkiyoFileEditor.Models;

/// <summary>A translation found in a reference pack (for example the English release) that can be copied to the open folder.</summary>
/// <param name="File">Pack the row belongs to (path relative to the opened folder).</param>
/// <param name="Key">The row's ID, or its entry name when it has no ID.</param>
/// <param name="Source">The original (Japanese) text of the row in the open folder.</param>
/// <param name="Text">The text taken from the reference pack.</param>
public sealed record ReferenceCandidate(string File, string Key, string Source, string Text);
