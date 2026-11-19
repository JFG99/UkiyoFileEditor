using System.Text;
using UkiyoFileEditor.Formats.Binary;

namespace UkiyoFileEditor.Formats.Cmp;

/// <summary>
/// Decompressor for the Spike Chunsoft "$CMP" container ($CL0/$CL1/$CL2/$CLN LZ chunks, $CR0 raw chunks).
/// Container: 32-byte header, then chunks with a 16-byte header:
///   magic[4], decompressedSize u32 BE, compressedSize u32 BE (includes the 16-byte header), 4 bytes unused.
/// Chunk data is a byte-oriented LZ:
///   bit0 = 1 : copy   -> count = (b &amp; mask) >> 1, offset = ((b >> shift) &lt;&lt; 8) | nextByte
///   bit0 = 0 : literal -> count = b >> 1, followed by 'count' raw bytes
/// shift: $CLN = 8, $CL1 = 7, $CL2 = 6, $CL0 = 6 
/// </summary>
public static class CmpDecoder
{
    private const int ChunkSizeFieldOffset = 4;
    private const int ChunkCompressedSizeFieldOffset = 8;

    /// <summary>
    /// Decompresses <paramref name="source"/> when it is a "$CMP" container; any other input is returned unchanged.
    /// </summary>
    public static byte[] Decompress(byte[] source, out CmpInfo info)
    {
        info = new CmpInfo { CompressedSize = source.Length };

        if (source.Length < CmpFormat.ContainerHeaderSize || !BigEndian.HasMagic(source, CmpFormat.ContainerMagic))
        {
            info.DecompressedSize = source.Length;
            return source;
        }

        info.IsCmp = true;
        uint announcedSize = BigEndian.ReadUInt32(source, CmpFormat.ContainerDecompressedSizeOffset);

        using var output = new MemoryStream();
        var chunkModes = new SortedSet<string>(StringComparer.Ordinal);
        int chunkPosition = CmpFormat.ContainerHeaderSize;

        while (chunkPosition + CmpFormat.ChunkHeaderSize <= source.Length)
        {
            string mode = Encoding.ASCII.GetString(source, chunkPosition, 4);
            bool isLzChunk = mode.StartsWith(CmpFormat.LzChunkPrefix, StringComparison.Ordinal);
            if (!isLzChunk && mode != CmpFormat.RawChunkMagic) break;

            int decompressedChunkSize = checked((int)BigEndian.ReadUInt32(source, chunkPosition + ChunkSizeFieldOffset));
            int compressedChunkSize = checked((int)BigEndian.ReadUInt32(source, chunkPosition + ChunkCompressedSizeFieldOffset));
            if (compressedChunkSize < CmpFormat.ChunkHeaderSize || chunkPosition + compressedChunkSize > source.Length)
                throw new InvalidDataException($"Chunk at 0x{chunkPosition:X} has an invalid size ({compressedChunkSize}).");

            int bodyStart = chunkPosition + CmpFormat.ChunkHeaderSize;
            int bodyLength = compressedChunkSize - CmpFormat.ChunkHeaderSize;

            if (mode == CmpFormat.RawChunkMagic)
            {
                output.Write(source, bodyStart, bodyLength);
            }
            else
            {
                byte[] chunk = DecodeLzChunk(source, bodyStart, bodyLength, CountShiftFor(mode), decompressedChunkSize, chunkPosition);
                output.Write(chunk, 0, chunk.Length);
            }

            chunkModes.Add(mode);
            info.ChunkCount++;
            chunkPosition += compressedChunkSize;
        }

        byte[] result = output.ToArray();
        info.DecompressedSize = result.Length;
        info.Modes = string.Join(", ", chunkModes);
        if (result.Length != announcedSize)
            info.Warning = $"Header says {announcedSize} bytes but {result.Length} were produced.";
        return result;
    }

    /// <summary>Bit position where the high part of the copy distance starts in the command byte.</summary>
    private static int CountShiftFor(string mode) => mode switch
    {
        "$CLN" => 8,
        "$CL0" => 6,
        "$CL1" => 7,
        "$CL2" => 6,
        _ => throw new NotSupportedException($"Unknown chunk mode {mode}.")
    };

    private static byte[] DecodeLzChunk(byte[] source, int bodyStart, int bodyLength, int distanceShift, int decompressedSize, int chunkPosition)
    {
        var output = new byte[decompressedSize];
        int outputPosition = 0;
        int sourcePosition = bodyStart;
        int bodyEnd = bodyStart + bodyLength;
        int countMask = (1 << distanceShift) - 1;

        while (sourcePosition < bodyEnd)
        {
            int command = source[sourcePosition++];
            bool isCopy = (command & 1) != 0;

            if (isCopy)
            {
                if (sourcePosition >= bodyEnd)
                    throw new InvalidDataException($"Chunk at 0x{chunkPosition:X}: truncated copy command.");

                int copyCount = (command & countMask) >> 1;
                int copyDistance = ((command >> distanceShift) << 8) | source[sourcePosition++];

                if (copyDistance <= 0 || copyDistance > outputPosition)
                    throw new InvalidDataException($"Chunk at 0x{chunkPosition:X}: bad copy offset {copyDistance}.");
                if (outputPosition + copyCount > decompressedSize)
                    throw new InvalidDataException($"Chunk at 0x{chunkPosition:X}: output overflow.");

                // Byte by byte on purpose: the copy may overlap its own output.
                for (int copied = 0; copied < copyCount; copied++)
                {
                    output[outputPosition] = output[outputPosition - copyDistance];
                    outputPosition++;
                }
            }
            else
            {
                int literalCount = command >> 1;
                if (sourcePosition + literalCount > bodyEnd || outputPosition + literalCount > decompressedSize)
                    throw new InvalidDataException($"Chunk at 0x{chunkPosition:X}: bad literal run.");

                Buffer.BlockCopy(source, sourcePosition, output, outputPosition, literalCount);
                sourcePosition += literalCount;
                outputPosition += literalCount;
            }
        }

        if (outputPosition != decompressedSize)
            throw new InvalidDataException($"Chunk at 0x{chunkPosition:X}: produced {outputPosition} bytes, expected {decompressedSize}.");

        return output;
    }
}
