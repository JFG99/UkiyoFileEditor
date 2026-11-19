namespace UkiyoFileEditor.Models;

/// <summary>Everything found while reading a reference folder.</summary>
/// <param name="Candidates">The rows that can be imported.</param>
/// <param name="Matched">Rows matched by ID or entry name.</param>
/// <param name="NoCounterpart">Rows of the open folder that have no counterpart in the reference.</param>
/// <param name="Unreadable">Reference packs that could not be read.</param>
/// <param name="ByIndex">Rows matched by their position because the IDs differ.</param>
public sealed record ReferenceResult(List<ReferenceCandidate> Candidates, int Matched, int NoCounterpart, int Unreadable, int ByIndex);
