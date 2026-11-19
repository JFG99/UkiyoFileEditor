namespace UkiyoFileEditor.Services;

/// <summary>Builds a message pack with the stored translations applied.</summary>
public static class TranslatedPackBuilder
{
    /// <summary>
    /// Builds the translated pack in memory and checks it by reading it back. Returns null when nothing in the pack has a
    /// translation. <paramref name="translationsByKey"/> is a snapshot of the stored translations of this file, so this can run on a worker thread.
    /// </summary>
    /// <param name="originalPath">The untouched original pack (see <see cref="PackBackupWriter.OriginalOf"/>).</param>
    public static byte[]? Build(string originalPath, IReadOnlyDictionary<string, TranslationItem> translationsByKey, out int replacedCount)
    {
        replacedCount = 0;
        byte[] originalBytes = File.ReadAllBytes(originalPath);
        MessagePack originalPack = MessagePack.Load(originalBytes);

        var translationByEntry = new Dictionary<int, string>();
        foreach (MessageRow row in originalPack.Rows)
        {
            if (row.EntryIndex < 0) continue;

            bool hasMatchingTranslation = translationsByKey.TryGetValue(TranslationStore.KeyOf(row), out TranslationItem? item)
                                          && item.Source == row.Text
                                          && item.Text.Length > 0;
            if (hasMatchingTranslation) translationByEntry[row.EntryIndex] = item!.Text;
        }
        if (translationByEntry.Count == 0) return null;

        byte[] rebuiltBytes = MessagePackWriter.Rebuild(originalBytes, translationByEntry, out replacedCount);
        VerifyReadBack(originalPack, rebuiltBytes, translationByEntry);
        return rebuiltBytes;
    }

    /// <summary>Reads the result back: same rows, and every translated row carries its translation.</summary>
    private static void VerifyReadBack(MessagePack originalPack, byte[] rebuiltBytes, Dictionary<int, string> translationByEntry)
    {
        MessagePack rebuiltPack = MessagePack.Load(rebuiltBytes);
        if (rebuiltPack.Rows.Count != originalPack.Rows.Count)
            throw new InvalidDataException("The rebuilt pack has a different number of entries; nothing was written.");

        for (int rowIndex = 0; rowIndex < originalPack.Rows.Count; rowIndex++)
        {
            MessageRow before = originalPack.Rows[rowIndex];
            MessageRow after = rebuiltPack.Rows[rowIndex];
            string expectedText = translationByEntry.TryGetValue(before.EntryIndex, out string? translation) ? translation : before.Text;
            if (before.Id != after.Id || NormalizeBreaks(after.Text) != NormalizeBreaks(expectedText))
                throw new InvalidDataException($"The rebuilt pack does not match at entry {before.Key}; nothing was written.");
        }
    }

    private static string NormalizeBreaks(string text) => text.Replace("\r\n", "\n");
}
