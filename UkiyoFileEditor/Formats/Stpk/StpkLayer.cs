using UkiyoFileEditor.Formats.Binary;

namespace UkiyoFileEditor.Formats.Stpk;

/// <summary>
/// One STPZ / $CMP / STPK layer of a pack, split into its table rows and entry bodies so the bodies can be replaced and the
/// layer written again in the same wrapping it was read in.
///   STPZ wrapper (optional) : "STPZ", u32 version, u32 entry count, u32 payload offset.
///   $CMP container (optional): see <see cref="CmpDecoder"/>.
///   STPK archive            : "STPK", u32 version, u32 entry count, u32 header size, entry table, entry data (each padded to 16).
/// </summary>
public sealed class StpkLayer
{
    private byte[] _original = Array.Empty<byte>();
    private int _wrapperLength;
    private bool _hasWrapper;
    private bool _hasCmp;
    private byte[] _archiveData = Array.Empty<byte>();
    private int _headerSize;
    private int _stride;

    /// <summary>The raw table row of every entry (offset and size are rewritten by <see cref="Build"/>).</summary>
    public byte[][] Rows { get; private set; } = Array.Empty<byte[]>();

    /// <summary>The body of every entry; replace an element to change an entry.</summary>
    public byte[][] Bodies { get; private set; } = Array.Empty<byte[]>();

    /// <summary>Which layers wrapped the STPK archive, e.g. "STPZ + $CMP + STPK".</summary>
    public string Description
    {
        get
        {
            var parts = new List<string>();
            if (_hasWrapper) parts.Add(StpkFormat.WrapperMagic);
            if (_hasCmp) parts.Add(CmpFormat.ContainerMagic);
            parts.Add(StpkFormat.ArchiveMagic);
            return string.Join(" + ", parts);
        }
    }

    public int EntryCount => Rows.Length;

    public string NameOf(int index)
    {
        byte[] row = Rows[index];
        return StpkFormat.ReadEntryName(row, StpkFormat.RowNameOffset, row.Length - StpkFormat.RowNameOffset);
    }

    /// <param name="raw">The pack file (or a pack nested inside another one).</param>
    /// <param name="allowEmpty">When true an archive with no entries yields an empty layer instead of an error.</param>
    public static StpkLayer Parse(byte[] raw, bool allowEmpty = false)
    {
        var layer = new StpkLayer { _original = raw };
        byte[] payload = raw;

        if (BigEndian.HasMagic(raw, StpkFormat.WrapperMagic))
        {
            if (raw.Length < StpkFormat.HeaderSize) throw new InvalidDataException("STPZ header is truncated.");

            int wrapperLength = checked((int)BigEndian.ReadUInt32(raw, StpkFormat.HeaderSizeOrAlignmentOffset));
            if (wrapperLength < StpkFormat.HeaderSize || wrapperLength >= raw.Length) wrapperLength = StpkFormat.HeaderSize;

            layer._hasWrapper = true;
            layer._wrapperLength = wrapperLength;
            payload = new byte[raw.Length - wrapperLength];
            Buffer.BlockCopy(raw, wrapperLength, payload, 0, payload.Length);
        }

        layer._hasCmp = BigEndian.HasMagic(payload, CmpFormat.ContainerMagic);
        byte[] data = layer._hasCmp ? CmpDecoder.Decompress(payload, out _) : payload;
        layer._archiveData = data;

        if (!BigEndian.HasMagic(data, StpkFormat.ArchiveMagic))
            throw new InvalidDataException("Not a message pack (no STPK archive found).");
        if (data.Length < StpkFormat.HeaderSize + 4)
            throw new InvalidDataException("STPK header is truncated.");

        long entryCount = BigEndian.ReadUInt32(data, StpkFormat.EntryCountOffset);
        int headerSize = checked((int)BigEndian.ReadUInt32(data, StpkFormat.HeaderSizeOrAlignmentOffset));
        layer._headerSize = headerSize;

        if (entryCount == 0)
        {
            if (allowEmpty) return layer;
            throw new InvalidDataException("The pack has no entries.");
        }

        if (entryCount > StpkFormat.MaxEntryCount || headerSize < StpkFormat.HeaderSize || headerSize + 8 > data.Length)
            throw new InvalidDataException("STPK header has unexpected values.");

        // The entry table ends where the first entry's data begins.
        uint firstEntryOffset = BigEndian.ReadUInt32(data, headerSize);
        if (firstEntryOffset <= headerSize || firstEntryOffset > data.Length)
            throw new InvalidDataException("STPK first entry offset is invalid.");

        long tableBytes = firstEntryOffset - headerSize;
        if (tableBytes % entryCount != 0)
            throw new InvalidDataException("STPK entry table size does not divide evenly by the entry count.");

        int stride = (int)(tableBytes / entryCount);
        if (stride < StpkFormat.MinStride || stride > StpkFormat.MaxStride)
            throw new InvalidDataException($"STPK entry stride 0x{stride:X} is unexpected.");

        layer._stride = stride;
        layer.Rows = new byte[(int)entryCount][];
        layer.Bodies = new byte[(int)entryCount][];

        for (int entry = 0; entry < entryCount; entry++)
        {
            int rowPosition = headerSize + entry * stride;
            if (rowPosition + stride > data.Length)
                throw new InvalidDataException("STPK entry table is truncated.");

            long bodyOffset = BigEndian.ReadUInt32(data, rowPosition);
            long bodySize = BigEndian.ReadUInt32(data, rowPosition + 4);
            if (bodyOffset + bodySize > data.Length)
                throw new InvalidDataException($"STPK entry {entry} points outside the archive.");

            layer.Rows[entry] = new byte[stride];
            Buffer.BlockCopy(data, rowPosition, layer.Rows[entry], 0, stride);
            layer.Bodies[entry] = new byte[bodySize];
            Buffer.BlockCopy(data, (int)bodyOffset, layer.Bodies[entry], 0, (int)bodySize);
        }

        return layer;
    }

    /// <summary>Writes the layer with its current bodies, in the same wrapping as it was read.</summary>
    public byte[] Build()
    {
        int entryCount = Rows.Length;
        const int alignment = StpkFormat.DefaultAlignment;

        var archive = new MemoryStream();
        archive.Write(_archiveData, 0, _headerSize);

        var bodyOffsets = new int[entryCount];
        int nextBodyOffset = BigEndian.AlignUp(_headerSize + entryCount * _stride, alignment);
        for (int entry = 0; entry < entryCount; entry++)
        {
            bodyOffsets[entry] = nextBodyOffset;
            nextBodyOffset += BigEndian.AlignUp(Bodies[entry].Length, alignment);
        }

        for (int entry = 0; entry < entryCount; entry++)
        {
            BigEndian.WriteUInt32(archive, (uint)bodyOffsets[entry]);
            BigEndian.WriteUInt32(archive, (uint)Bodies[entry].Length);
            archive.Write(Rows[entry], StpkFormat.RowOffsetAndSizeBytes, _stride - StpkFormat.RowOffsetAndSizeBytes);
        }

        BigEndian.PadToAlignment(archive, alignment);
        for (int entry = 0; entry < entryCount; entry++)
        {
            archive.Write(Bodies[entry], 0, Bodies[entry].Length);
            BigEndian.PadToAlignment(archive, alignment);
        }

        byte[] archiveBytes = archive.ToArray();
        byte[] payload = _hasCmp ? CmpEncoder.Compress(archiveBytes) : archiveBytes;
        if (!_hasWrapper) return payload;

        var file = new MemoryStream();
        file.Write(_original, 0, _wrapperLength);
        file.Write(payload, 0, payload.Length);
        BigEndian.PadToAlignment(file, alignment);
        return file.ToArray();
    }
}
