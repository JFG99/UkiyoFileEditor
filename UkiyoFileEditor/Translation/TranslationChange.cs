using System.Text.Json;

namespace UkiyoFileEditor.Translation;

/// <summary>One change of the store, kept so it can be undone and redone.</summary>
public sealed record TranslationChange(string File, string Key, TranslationItem? Before, TranslationItem? After);
