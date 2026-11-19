using System.Text.Json;

namespace UkiyoFileEditor.Translation;

/// <summary>One stored translation. Source is the original text it was made from; it is only shown while the original is unchanged.</summary>
public sealed record TranslationItem(string Source, string Text);
