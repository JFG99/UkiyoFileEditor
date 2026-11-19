using System.Text;
using UkiyoFileEditor.Formats.Binary;

namespace UkiyoFileEditor.Formats.Cmp;

/// <summary>
/// Writes the "$CMP" container with $CL0 chunks (shift 6, 1023-byte window, matches of 3..31 bytes) of at most
/// <see cref="CmpFormat.MaxChunkSourceSize"/> source bytes each.
/// </summary>
public static class CmpEncoder
{
    private const int MaxDistance = 1023;
    private const int MaxMatchLength = 31;
    private const int MinMatchLength = 3;
    private const int MaxHashCandidates = 64;
    private const int MaxLiteralRun = 127;
    private const int HashedBytes = 3;

    public static byte[] Compress(byte[] data)
    {
        var chunks = new MemoryStream();
        for (int start = 0; start < data.Length; start += CmpFormat.MaxChunkSourceSize)
        {
            int length = Math.Min(CmpFormat.MaxChunkSourceSize, data.Length - start);
            var part = new byte[length];
            Buffer.BlockCopy(data, start, part, 0, length);
            byte[] packed = CompressChunk(part);

            chunks.Write(Encoding.ASCII.GetBytes(CmpFormat.LzChunkMagic));
            BigEndian.WriteUInt32(chunks, (uint)length);
            BigEndian.WriteUInt32(chunks, (uint)(packed.Length + CmpFormat.ChunkHeaderSize));
            BigEndian.WriteUInt32(chunks, 0);
            chunks.Write(packed, 0, packed.Length);
        }

        uint totalChunkSize = (uint)(chunks.Length + CmpFormat.ChunkHeaderSize);
        var result = new MemoryStream();
        result.Write(Encoding.ASCII.GetBytes(CmpFormat.ContainerMagic));
        BigEndian.WriteUInt32(result, totalChunkSize);
        BigEndian.WriteUInt32(result, 0);
        BigEndian.WriteUInt32(result, 0);
        BigEndian.WriteUInt32(result, (uint)data.Length);
        BigEndian.WriteUInt32(result, totalChunkSize);
        BigEndian.WriteUInt32(result, 0);
        BigEndian.WriteUInt32(result, 0);
        chunks.WriteTo(result);
        return result.ToArray();
    }

    /// <summary>
    /// $CL0 stream (shift 6). A byte b with bit 0 set is a match: count = (b &amp; 63) >> 1, distance = ((b >> 6) &lt;&lt; 8) | next byte.
    /// Otherwise it is a literal run of b >> 1 bytes.
    /// </summary>
    private static byte[] CompressChunk(byte[] source)
    {
        var output = new List<byte>(source.Length / 2 + 16);
        var pendingLiterals = new List<byte>();
        var latestPositionByHash = new Dictionary<int, int>();
        var previousPositionWithSameHash = new int[source.Length];
        int sourceLength = source.Length;

        void FlushLiterals()
        {
            int offset = 0;
            while (offset < pendingLiterals.Count)
            {
                int runLength = Math.Min(MaxLiteralRun, pendingLiterals.Count - offset);
                output.Add((byte)(runLength << 1));
                output.AddRange(pendingLiterals.GetRange(offset, runLength));
                offset += runLength;
            }
            pendingLiterals.Clear();
        }

        int HashAt(int position) => source[position] | (source[position + 1] << 8) | (source[position + 2] << 16);

        void IndexPosition(int position)
        {
            if (position + HashedBytes > sourceLength) return;
            int hash = HashAt(position);
            previousPositionWithSameHash[position] = latestPositionByHash.TryGetValue(hash, out int earlier) ? earlier : -1;
            latestPositionByHash[hash] = position;
        }

        int current = 0;
        while (current < sourceLength)
        {
            int bestLength = 0;
            int bestDistance = 0;

            if (current + HashedBytes <= sourceLength)
            {
                int candidate = latestPositionByHash.TryGetValue(HashAt(current), out int first) ? first : -1;
                int attempts = 0;
                while (candidate >= 0 && current - candidate <= MaxDistance && attempts < MaxHashCandidates)
                {
                    int lengthLimit = Math.Min(MaxMatchLength, sourceLength - current);
                    int length = 0;
                    while (length < lengthLimit && source[candidate + length] == source[current + length]) length++;

                    if (length > bestLength)
                    {
                        bestLength = length;
                        bestDistance = current - candidate;
                    }
                    if (length == lengthLimit) break;

                    candidate = previousPositionWithSameHash[candidate];
                    attempts++;
                }
            }

            if (bestLength >= MinMatchLength)
            {
                FlushLiterals();
                output.Add((byte)(((bestDistance >> 8) << 6) | (bestLength << 1) | 1));
                output.Add((byte)(bestDistance & 0xFF));
                for (int covered = 0; covered < bestLength; covered++) IndexPosition(current + covered);
                current += bestLength;
            }
            else
            {
                pendingLiterals.Add(source[current]);
                IndexPosition(current);
                current++;
            }
        }

        FlushLiterals();
        return output.ToArray();
    }
}
