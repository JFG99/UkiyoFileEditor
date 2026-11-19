using System.Text;

namespace UkiyoFileEditor.Formats.Stpk;

/// <summary>
/// Reader for message packs (verified against 1,270 non-empty files):
///   STPZ wrapper : "STPZ", u32 version (1), u32 entry count, u32 dataOffset (0x10), then a $CMP block.
///   STPK archive : "STPK", u32 version (1), u32 entry count, u32 headerSize (0x10), then the entry table.
///   Entry        : u32 offset, u32 size (big-endian), 8 bytes padding, 32-byte name (NOT NUL-terminated
///                  when full, so long names are truncated - "_JP_tag" suffixes are often cut off).
///                  Stride is (firstEntryOffset - headerSize) / count, always 0x30 in practice.
/// Entries strictly alternate: an ID entry (ASCII / Shift-JIS, NUL terminated) followed by a text entry
/// (UTF-16 with BOM, NUL terminated). Entries are therefore classified by CONTENT, not by name.
/// Zero-byte files exist in the data set and are reported as empty.
/// </summary>
public sealed class MessagePack
{
    private const double MaxControlCharacterRatio = 0.1;

    private static readonly Encoding ShiftJis = CreateShiftJis();

    public List<MessageRow> Rows { get; } = new();

    /// <summary>The layers the file consists of, e.g. "STPZ + $CMP + STPK".</summary>
    public string Container { get; private set; } = "";

    public int EntryCount { get; private set; }

    public static MessagePack Load(byte[] raw)
    {
        var pack = new MessagePack();

        if (raw.Length == 0)
        {
            pack.Container = "empty file (0 bytes)";
            return pack;
        }

        StpkLayer layer = StpkLayer.Parse(raw, allowEmpty: true);
        pack.Container = layer.Description;
        if (layer.EntryCount == 0) return pack;

        pack.EntryCount = layer.EntryCount;
        pack.AddRows(layer);
        return pack;
    }

    /// <summary>Entries alternate ID, text, ID, text ... Classify by content and pair each ID with the next text.</summary>
    private void AddRows(StpkLayer layer)
    {
        string? pendingId = null;

        for (int entry = 0; entry < layer.EntryCount; entry++)
        {
            byte[] body = layer.Bodies[entry];

            if (IsUtf16Text(body))
            {
                Rows.Add(new MessageRow("", layer.NameOf(entry), pendingId ?? "", DecodeText(body), entry));
                pendingId = null;
                continue;
            }

            // An ID that was never followed by text is kept so nothing is silently lost.
            if (pendingId != null)
                Rows.Add(new MessageRow("", "", pendingId, ""));

            pendingId = DecodeText(body);
        }

        if (pendingId != null)
            Rows.Add(new MessageRow("", "", pendingId, ""));
    }

    private static bool IsUtf16Text(byte[] body) =>
        body.Length >= 2 && ((body[0] == 0xFF && body[1] == 0xFE) || (body[0] == 0xFE && body[1] == 0xFF));

    private static string DecodeText(byte[] body)
    {
        string text;

        if (body.Length >= 2 && body[0] == 0xFF && body[1] == 0xFE)
        {
            text = Encoding.Unicode.GetString(body, 2, body.Length - 2);
        }
        else if (body.Length >= 2 && body[0] == 0xFE && body[1] == 0xFF)
        {
            text = Encoding.BigEndianUnicode.GetString(body, 2, body.Length - 2);
        }
        else
        {
            try
            {
                text = new UTF8Encoding(false, true).GetString(body);
            }
            catch (DecoderFallbackException)
            {
                // IDs occasionally contain Shift-JIS bytes (e.g. a "※" marker).
                try
                {
                    text = ShiftJis.GetString(body);
                }
                catch (Exception)
                {
                    return BinaryPlaceholder(body);
                }
            }
        }

        text = text.TrimEnd('\0');

        // Reject "text" that is mostly control characters (binary that happened to decode).
        int controlCharacters = 0;
        foreach (char character in text)
        {
            if (character < 0x20 && character != '\n' && character != '\r' && character != '\t') controlCharacters++;
        }
        if (text.Length > 0 && controlCharacters > text.Length * MaxControlCharacterRatio)
            return BinaryPlaceholder(body);

        return text;
    }

    private static string BinaryPlaceholder(byte[] body) => $"(binary data, {body.Length} bytes)";

    private static Encoding CreateShiftJis()
    {
        try
        {
            return Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }
        catch (Exception)
        {
            return Encoding.Latin1;
        }
    }
}
