using System.Text.Json;

namespace UkiyoFileEditor.Translation;

/// <summary>Undo / redo of translation changes. Each step is a batch, so a Replace All or a translation run undoes in one go.</summary>
public sealed class UndoStack
{
    private sealed class Batch
    {
        public string Name = "";
        public List<TranslationChange> Changes = new();
        public string? CoalesceKey;
        public DateTime When;
    }

    private const int Limit = 500;
    private readonly List<Batch> _undo = new();
    private readonly List<Batch> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoName => _undo.Count > 0 ? _undo[^1].Name : null;
    public string? RedoName => _redo.Count > 0 ? _redo[^1].Name : null;

    /// <summary>
    /// Adds a step. Typing in the translation box passes a coalesce key so that a burst of edits of one entry is one step.
    /// </summary>
    public void Push(string name, List<TranslationChange> changes, string? coalesceKey = null)
    {
        if (changes.Count == 0) return;
        _redo.Clear();

        if (coalesceKey != null && _undo.Count > 0)
        {
            Batch topPanel = _undo[^1];
            if (topPanel.CoalesceKey == coalesceKey && topPanel.Changes.Count == 1 && changes.Count == 1 &&
                (DateTime.UtcNow - topPanel.When).TotalSeconds < 10)
            {
                topPanel.Changes[0] = topPanel.Changes[0] with { After = changes[0].After };
                topPanel.When = DateTime.UtcNow;
                return;
            }
        }

        _undo.Add(new Batch { Name = name, Changes = changes, CoalesceKey = coalesceKey, When = DateTime.UtcNow });
        if (_undo.Count > Limit) _undo.RemoveAt(0);
    }

    public List<TranslationChange>? Undo(TranslationStore store)
    {
        if (_undo.Count == 0) return null;
        Batch batch = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        for (int index = batch.Changes.Count - 1; index >= 0; index--) store.Apply(batch.Changes[index], undo: true);
        _redo.Add(batch);
        return batch.Changes;
    }

    public List<TranslationChange>? Redo(TranslationStore store)
    {
        if (_redo.Count == 0) return null;
        Batch batch = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        foreach (TranslationChange change in batch.Changes) store.Apply(change, undo: false);
        _undo.Add(batch);
        return batch.Changes;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
