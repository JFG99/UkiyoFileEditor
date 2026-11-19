using System.Text;

namespace UkiyoFileEditor.Formats.Stpk;

/// <summary>
/// Rebuilds a message pack with some of its text entries replaced. Everything else (entry names, the ID entries, the
/// padding bytes of every table row) is copied from the original. Layout written, verified by a round trip of all 1,270
/// non-empty packs of the data set:
///   STPK : 16-byte header, entry table (offset/size rewritten), entry data, every entry padded to 16 bytes.
///   $CMP : 32-byte header, then $CL0 chunks (see <see cref="Cmp.CmpEncoder"/>).
///   STPZ : the original 16-byte wrapper, and the whole file padded to a multiple of 16.
/// The original files carry some zero bytes after the last entry; they are not reproduced.
/// </summary>
public static class MessagePackWriter
{
    /// <param name="raw">The original pack file.</param>
    /// <param name="newTextByEntry">Entry index (see <see cref="MessageRow.EntryIndex"/>) to the new text of that entry.</param>
    /// <param name="replacedCount">How many entries were actually replaced.</param>
    public static byte[] Rebuild(byte[] raw, IReadOnlyDictionary<int, string> newTextByEntry, out int replacedCount)
    {
        if (raw.Length == 0) throw new InvalidDataException("The file is empty.");

        StpkLayer layer = StpkLayer.Parse(raw);

        replacedCount = 0;
        foreach (var (entryIndex, newText) in newTextByEntry)
        {
            if (entryIndex < 0 || entryIndex >= layer.EntryCount || newText.Length == 0) continue;

            byte[] oldBody = layer.Bodies[entryIndex];
            if (!StartsWithUtf16LittleEndianBom(oldBody)) continue; // only UTF-16LE text entries

            layer.Bodies[entryIndex] = EncodeText(oldBody, newText);
            replacedCount++;
        }

        return layer.Build();
    }

    private static bool StartsWithUtf16LittleEndianBom(byte[] body) =>
        body.Length >= 2 && body[0] == 0xFF && body[1] == 0xFE;

    /// <summary>UTF-16LE with BOM and a two-byte NUL terminator; the line-break style of the old text is kept.</summary>
    private static byte[] EncodeText(byte[] oldBody, string newText)
    {
        bool usesCrLf = Encoding.Unicode.GetString(oldBody, 2, oldBody.Length - 2).Contains("\r\n");
        string normalized = newText.Replace("\r\n", "\n");
        if (usesCrLf) normalized = normalized.Replace("\n", "\r\n");

        byte[] encoded = Encoding.Unicode.GetBytes(normalized);
        var body = new byte[2 + encoded.Length + 2];
        body[0] = 0xFF;
        body[1] = 0xFE;
        Buffer.BlockCopy(encoded, 0, body, 2, encoded.Length);
        return body; // the last two bytes stay 0: the terminator
    }
}
