using System.Text;
using UkiyoFileEditor.Formats.Binary;

namespace UkiyoFileEditor.Formats.Stpk;

/// <summary>
/// General reader / writer for STPZ + $CMP + STPK packs, including the Flash UI packs ("*_m.pak" holding .sfl / .srd files,
/// "*_v.pak" holding the matching .srdv pixel data). Layout (verified on game_flash_pause_opt_m/v.pak):
///   STPZ wrapper : "STPZ", u32 1, u32 entry count, u32 alignment; the payload starts at byte 16 ("$CMP" there).
///   STPK archive : "STPK", u32 1, u32 entry count, u32 ALIGNMENT (0x10 in message packs, 0x80 in the _v pack), then a
///                  table that always starts at 0x10. Row = u32 offset, u32 size, 8 bytes padding, 32-byte name.
///                  The row stride is 0x30 (derived from the first entry offset in the 0x10-aligned message packs).
///                  The first body starts at the table end rounded up to the alignment; every body is padded to it.
///   Rebuilding an untouched pack reproduces the decompressed archive exactly, except for zero bytes after the last body
///   (their count is remembered and written again).
/// Unlike <see cref="StpkLayer"/> this keeps the archive's own alignment, which the _v packs need.
/// </summary>
public sealed class StpkArchive
{
    private const int FileAlignment = 16;
    private const int MaxAlignment = 0x10000;
    private const int MaxEntryCount = 100_000;
    private const int MinimumArchiveSize = 0x20;
    private const int MinimumWrapperSize = 20;

    /// <summary>The last dword of the 32-byte $CMP header carries a value the game files have (0x000F0000); it is kept.</summary>
    private const int CmpHeaderTrailerOffset = 28;

    private byte[] _wrapper = Array.Empty<byte>();
    private bool _hasCmp;
    private byte[] _header = new byte[StpkFormat.HeaderSize];
    private int _alignment = StpkFormat.DefaultAlignment;
    private int _stride = StpkFormat.DefaultStride;
    private int _trailingZeroBytes;
    private byte[]? _cmpHeaderTrailer;
    private readonly List<byte[]> _rows = new();

    /// <summary>Entry bodies, in table order. Replace an element to change an entry.</summary>
    public List<byte[]> Bodies { get; } = new();

    public int Count => _rows.Count;

    public string NameOf(int index)
    {
        byte[] row = _rows[index];
        int nameEnd = StpkFormat.RowNameOffset;
        while (nameEnd < row.Length && row[nameEnd] != 0) nameEnd++;
        return Encoding.Latin1.GetString(row, StpkFormat.RowNameOffset, nameEnd - StpkFormat.RowNameOffset);
    }

    public static StpkArchive Load(byte[] raw)
    {
        if (raw.Length == 0) throw new InvalidDataException("The file is empty.");

        var archive = new StpkArchive();

        int payloadStart = 0;
        if (BigEndian.HasMagic(raw, StpkFormat.WrapperMagic))
        {
            if (raw.Length < MinimumWrapperSize) throw new InvalidDataException("STPZ header is truncated.");

            bool payloadFollowsHeader = BigEndian.HasMagic(raw, CmpFormat.ContainerMagic, StpkFormat.HeaderSize)
                                        || BigEndian.HasMagic(raw, StpkFormat.ArchiveMagic, StpkFormat.HeaderSize);
            payloadStart = payloadFollowsHeader
                ? StpkFormat.HeaderSize
                : checked((int)BigEndian.ReadUInt32(raw, StpkFormat.HeaderSizeOrAlignmentOffset));
            if (payloadStart < StpkFormat.HeaderSize || payloadStart >= raw.Length) payloadStart = StpkFormat.HeaderSize;
            archive._wrapper = raw[..payloadStart];
        }

        byte[] payload = payloadStart == 0 ? raw : raw[payloadStart..];
        archive._hasCmp = BigEndian.HasMagic(payload, CmpFormat.ContainerMagic);
        byte[] data = archive._hasCmp ? CmpDecoder.Decompress(payload, out _) : payload;

        if (archive._hasCmp && raw.Length >= payloadStart + CmpFormat.ContainerHeaderSize)
        {
            int trailerStart = payloadStart + CmpHeaderTrailerOffset;
            archive._cmpHeaderTrailer = raw[trailerStart..(trailerStart + 4)];
        }

        if (!BigEndian.HasMagic(data, StpkFormat.ArchiveMagic) || data.Length < MinimumArchiveSize)
            throw new InvalidDataException("No STPK archive found.");

        long entryCount = BigEndian.ReadUInt32(data, StpkFormat.EntryCountOffset);
        int alignment = checked((int)BigEndian.ReadUInt32(data, StpkFormat.HeaderSizeOrAlignmentOffset));
        bool alignmentIsPowerOfTwo = (alignment & (alignment - 1)) == 0;
        if (alignment < FileAlignment || alignment > MaxAlignment || !alignmentIsPowerOfTwo) alignment = FileAlignment;
        if (entryCount <= 0 || entryCount > MaxEntryCount)
            throw new InvalidDataException("STPK header has an unexpected entry count.");

        int stride = StpkFormat.DefaultStride;
        if (alignment == FileAlignment)
        {
            long tableBytes = (long)BigEndian.ReadUInt32(data, StpkFormat.HeaderSize) - StpkFormat.HeaderSize;
            long derivedStride = tableBytes / entryCount;
            if (tableBytes > 0 && tableBytes % entryCount == 0 &&
                derivedStride >= StpkFormat.MinStride && derivedStride <= StpkFormat.MaxStride)
                stride = (int)derivedStride;
        }

        if (StpkFormat.HeaderSize + entryCount * stride > data.Length)
            throw new InvalidDataException("STPK entry table is truncated.");

        archive._alignment = alignment;
        archive._stride = stride;
        archive._header = data[..StpkFormat.HeaderSize];

        for (int entry = 0; entry < entryCount; entry++)
        {
            int rowPosition = StpkFormat.HeaderSize + entry * stride;
            long bodyOffset = BigEndian.ReadUInt32(data, rowPosition);
            long bodySize = BigEndian.ReadUInt32(data, rowPosition + 4);
            if (bodyOffset + bodySize > data.Length)
                throw new InvalidDataException($"STPK entry {entry} points outside the archive.");

            archive._rows.Add(data[rowPosition..(rowPosition + stride)]);
            archive.Bodies.Add(data[(int)bodyOffset..(int)(bodyOffset + bodySize)]);
        }

        // Zero padding that follows the last body in the original.
        long expectedEnd = BigEndian.AlignUp(StpkFormat.HeaderSize + entryCount * stride, alignment);
        foreach (byte[] body in archive.Bodies) expectedEnd += BigEndian.AlignUp(body.Length, alignment);
        archive._trailingZeroBytes = (int)Math.Max(0, data.Length - expectedEnd);

        return archive;
    }

    /// <summary>Writes the pack again: the original wrapper, a $CL0-compressed archive (when the original was compressed) and 16-byte padding.</summary>
    public byte[] Build()
    {
        int entryCount = Count;
        int tableEnd = BigEndian.AlignUp(StpkFormat.HeaderSize + entryCount * _stride, _alignment);

        var bodyOffsets = new int[entryCount];
        int nextBodyOffset = tableEnd;
        for (int entry = 0; entry < entryCount; entry++)
        {
            bodyOffsets[entry] = nextBodyOffset;
            nextBodyOffset += BigEndian.AlignUp(Bodies[entry].Length, _alignment);
        }

        var archiveStream = new MemoryStream();
        archiveStream.Write(_header, 0, _header.Length);
        for (int entry = 0; entry < entryCount; entry++)
        {
            BigEndian.WriteUInt32(archiveStream, (uint)bodyOffsets[entry]);
            BigEndian.WriteUInt32(archiveStream, (uint)Bodies[entry].Length);
            archiveStream.Write(_rows[entry], StpkFormat.RowOffsetAndSizeBytes, _stride - StpkFormat.RowOffsetAndSizeBytes);
        }

        while (archiveStream.Length < tableEnd) archiveStream.WriteByte(0);
        for (int entry = 0; entry < entryCount; entry++)
        {
            archiveStream.Write(Bodies[entry], 0, Bodies[entry].Length);
            BigEndian.PadToAlignment(archiveStream, _alignment);
        }
        for (int zeroByte = 0; zeroByte < _trailingZeroBytes; zeroByte++) archiveStream.WriteByte(0);

        byte[] archiveBytes = archiveStream.ToArray();
        byte[] payload = _hasCmp ? CmpEncoder.Compress(archiveBytes) : archiveBytes;

        var file = new MemoryStream();
        file.Write(_wrapper, 0, _wrapper.Length);
        file.Write(payload, 0, payload.Length);
        BigEndian.PadToAlignment(file, FileAlignment);

        byte[] result = file.ToArray();
        if (_cmpHeaderTrailer != null && result.Length >= _wrapper.Length + CmpFormat.ContainerHeaderSize)
            Buffer.BlockCopy(_cmpHeaderTrailer, 0, result, _wrapper.Length + CmpHeaderTrailerOffset, 4);
        return result;
    }
}
