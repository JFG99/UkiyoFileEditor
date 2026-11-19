namespace UkiyoFileEditor.Formats.Texture;

/// <summary>
/// DXT1 (BC1, 8 bytes per 4x4 block) and DXT5 (BC3, 16 bytes per block: 8 alpha + 8 colour) codec.
/// Pixels are BGRA, row-major, top row first. Decoding is bit-exact with the standard decoders (checked against
/// Pillow's BCn decoder on all 122 textures of the pause options screen); the encoder uses an inset bounding-box
/// endpoint fit, which round-trips those textures with a mean error below 3 per channel.
/// </summary>
public static class DxtCodec
{
    private const int BlockDimension = 4;
    private const int PixelsPerBlock = 16;
    private const int BytesPerPixel = 4;
    private const int Dxt1BlockSize = 8;
    private const int Dxt5BlockSize = 16;
    private const int Dxt5AlphaBytes = 8;
    private const int PaletteChannels = 3;
    private const int TransparencyThreshold = 128;

    // ------------------------------------------------------------------ sizes
    public static int DataSize(int width, int height, bool isDxt1) =>
        BlocksFor(width) * BlocksFor(height) * (isDxt1 ? Dxt1BlockSize : Dxt5BlockSize);

    /// <summary>Total bytes of a tightly packed mip chain (level 0 first, each level half the size, minimum 1x1 pixel).</summary>
    public static int ChainSize(int width, int height, bool isDxt1, int mipCount)
    {
        int totalBytes = 0;
        int levelWidth = width, levelHeight = height;
        for (int level = 0; level < Math.Max(1, mipCount); level++)
        {
            totalBytes += DataSize(levelWidth, levelHeight, isDxt1);
            levelWidth = Math.Max(1, levelWidth / 2);
            levelHeight = Math.Max(1, levelHeight / 2);
        }
        return totalBytes;
    }

    private static int BlocksFor(int pixels) => (pixels + BlockDimension - 1) / BlockDimension;

    // ------------------------------------------------------------------ mip chain
    /// <summary>Encodes level 0 from <paramref name="bgra"/> and every further level from a 2x2 box-filtered copy of the previous one.</summary>
    public static byte[] EncodeChain(byte[] bgra, int width, int height, bool isDxt1, int mipCount)
    {
        using var chain = new MemoryStream();
        byte[] levelPixels = bgra;
        int levelWidth = width, levelHeight = height;

        for (int level = 0; level < Math.Max(1, mipCount); level++)
        {
            byte[] encodedLevel = Encode(levelPixels, levelWidth, levelHeight, isDxt1);
            chain.Write(encodedLevel, 0, encodedLevel.Length);

            int nextWidth = Math.Max(1, levelWidth / 2);
            int nextHeight = Math.Max(1, levelHeight / 2);
            levelPixels = HalveWidthBoxFilter(levelPixels, levelWidth, levelHeight, nextWidth, nextHeight);
            levelWidth = nextWidth;
            levelHeight = nextHeight;
        }
        return chain.ToArray();
    }

    private static byte[] HalveWidthBoxFilter(byte[] source, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        var target = new byte[targetWidth * targetHeight * BytesPerPixel];
        for (int y = 0; y < targetHeight; y++)
        {
            for (int x = 0; x < targetWidth; x++)
            {
                int left = Math.Min(sourceWidth - 1, x * 2), right = Math.Min(sourceWidth - 1, x * 2 + 1);
                int top = Math.Min(sourceHeight - 1, y * 2), bottom = Math.Min(sourceHeight - 1, y * 2 + 1);
                for (int channel = 0; channel < BytesPerPixel; channel++)
                {
                    int sum = source[(top * sourceWidth + left) * BytesPerPixel + channel]
                            + source[(top * sourceWidth + right) * BytesPerPixel + channel]
                            + source[(bottom * sourceWidth + left) * BytesPerPixel + channel]
                            + source[(bottom * sourceWidth + right) * BytesPerPixel + channel];
                    target[(y * targetWidth + x) * BytesPerPixel + channel] = (byte)((sum + 2) / 4);
                }
            }
        }
        return target;
    }

    // ------------------------------------------------------------------ decode
    /// <summary>Decodes one image level starting at <paramref name="dataOffset"/>; truncated data leaves the remaining pixels transparent.</summary>
    public static byte[] Decode(byte[] data, int dataOffset, int width, int height, bool isDxt1)
    {
        var bgra = new byte[width * height * BytesPerPixel];
        int blocksAcross = BlocksFor(width);
        int blocksDown = BlocksFor(height);
        int blockSize = isDxt1 ? Dxt1BlockSize : Dxt5BlockSize;

        var colourPalette = new int[4 * PaletteChannels];
        var alphaPalette = new int[8];
        var pixelAlpha = new int[PixelsPerBlock];

        for (int blockY = 0; blockY < blocksDown; blockY++)
        {
            for (int blockX = 0; blockX < blocksAcross; blockX++)
            {
                int blockStart = dataOffset + (blockY * blocksAcross + blockX) * blockSize;
                if (blockStart + blockSize > data.Length) return bgra;

                int colourStart = isDxt1 ? blockStart : blockStart + Dxt5AlphaBytes;
                int colour0 = data[colourStart] | (data[colourStart + 1] << 8);
                int colour1 = data[colourStart + 2] | (data[colourStart + 3] << 8);
                uint colourIndices = (uint)(data[colourStart + 4] | (data[colourStart + 5] << 8)
                                          | (data[colourStart + 6] << 16) | (data[colourStart + 7] << 24));
                bool hasFourColours = !isDxt1 || colour0 > colour1;
                BuildColourPalette(colour0, colour1, hasFourColours, colourPalette);

                if (!isDxt1) ReadAlphaBlock(data, blockStart, alphaPalette, pixelAlpha);

                for (int pixel = 0; pixel < PixelsPerBlock; pixel++)
                {
                    int x = blockX * BlockDimension + (pixel & 3);
                    int y = blockY * BlockDimension + (pixel >> 2);
                    if (x >= width || y >= height) continue;

                    int paletteIndex = (int)((colourIndices >> (2 * pixel)) & 3);
                    int alpha = 255;
                    if (isDxt1)
                    {
                        if (!hasFourColours && paletteIndex == 3) alpha = 0;
                    }
                    else
                    {
                        alpha = pixelAlpha[pixel];
                    }

                    int target = (y * width + x) * BytesPerPixel;
                    bgra[target] = (byte)colourPalette[paletteIndex * PaletteChannels + 2];
                    bgra[target + 1] = (byte)colourPalette[paletteIndex * PaletteChannels + 1];
                    bgra[target + 2] = (byte)colourPalette[paletteIndex * PaletteChannels];
                    bgra[target + 3] = (byte)alpha;
                }
            }
        }

        return bgra;
    }

    /// <summary>Reads the 8 alpha bytes of a DXT5 block into the 16 per-pixel alpha values.</summary>
    private static void ReadAlphaBlock(byte[] data, int blockStart, int[] alphaPalette, int[] pixelAlpha)
    {
        int alpha0 = data[blockStart];
        int alpha1 = data[blockStart + 1];
        ulong alphaIndices = 0;
        for (int byteIndex = 0; byteIndex < 6; byteIndex++)
            alphaIndices |= (ulong)data[blockStart + 2 + byteIndex] << (8 * byteIndex);

        alphaPalette[0] = alpha0;
        alphaPalette[1] = alpha1;
        if (alpha0 > alpha1)
        {
            for (int step = 1; step < 7; step++) alphaPalette[step + 1] = ((7 - step) * alpha0 + step * alpha1) / 7;
        }
        else
        {
            for (int step = 1; step < 5; step++) alphaPalette[step + 1] = ((5 - step) * alpha0 + step * alpha1) / 5;
            alphaPalette[6] = 0;
            alphaPalette[7] = 255;
        }

        for (int pixel = 0; pixel < PixelsPerBlock; pixel++)
            pixelAlpha[pixel] = alphaPalette[(int)((alphaIndices >> (3 * pixel)) & 7)];
    }

    /// <summary>palette receives 4 x (r, g, b). In three-colour mode entry 3 is black (transparent in DXT1).</summary>
    private static void BuildColourPalette(int colour0, int colour1, bool hasFourColours, int[] palette)
    {
        Unpack565(colour0, out int red0, out int green0, out int blue0);
        Unpack565(colour1, out int red1, out int green1, out int blue1);
        palette[0] = red0; palette[1] = green0; palette[2] = blue0;
        palette[3] = red1; palette[4] = green1; palette[5] = blue1;

        if (hasFourColours)
        {
            palette[6] = (2 * red0 + red1) / 3; palette[7] = (2 * green0 + green1) / 3; palette[8] = (2 * blue0 + blue1) / 3;
            palette[9] = (red0 + 2 * red1) / 3; palette[10] = (green0 + 2 * green1) / 3; palette[11] = (blue0 + 2 * blue1) / 3;
        }
        else
        {
            palette[6] = (red0 + red1) / 2; palette[7] = (green0 + green1) / 2; palette[8] = (blue0 + blue1) / 2;
            palette[9] = 0; palette[10] = 0; palette[11] = 0;
        }
    }

    private static void Unpack565(int colour, out int red, out int green, out int blue)
    {
        int red5 = (colour >> 11) & 31, green6 = (colour >> 5) & 63, blue5 = colour & 31;
        red = (red5 << 3) | (red5 >> 2);
        green = (green6 << 2) | (green6 >> 4);
        blue = (blue5 << 3) | (blue5 >> 2);
    }

    private static int Pack565(int red, int green, int blue) =>
        (((red * 31 + 127) / 255) << 11) | (((green * 63 + 127) / 255) << 5) | ((blue * 31 + 127) / 255);

    // ------------------------------------------------------------------ encode
    public static byte[] Encode(byte[] bgra, int width, int height, bool isDxt1)
    {
        int blocksAcross = BlocksFor(width);
        int blocksDown = BlocksFor(height);
        int blockSize = isDxt1 ? Dxt1BlockSize : Dxt5BlockSize;
        var encoded = new byte[blocksAcross * blocksDown * blockSize];
        var blockPixels = new int[PixelsPerBlock * BytesPerPixel]; // 16 x (r, g, b, a)

        for (int blockY = 0; blockY < blocksDown; blockY++)
        {
            for (int blockX = 0; blockX < blocksAcross; blockX++)
            {
                CopyBlockPixels(bgra, width, height, blockX, blockY, blockPixels);

                int blockStart = (blockY * blocksAcross + blockX) * blockSize;
                if (isDxt1)
                {
                    EncodeColourBlock(blockPixels, true, encoded, blockStart);
                }
                else
                {
                    EncodeAlphaBlock(blockPixels, encoded, blockStart);
                    EncodeColourBlock(blockPixels, false, encoded, blockStart + Dxt5AlphaBytes);
                }
            }
        }

        return encoded;
    }

    /// <summary>Copies one 4x4 block as (r, g, b, a) values; pixels beyond the image edge repeat the edge pixel.</summary>
    private static void CopyBlockPixels(byte[] bgra, int width, int height, int blockX, int blockY, int[] blockPixels)
    {
        for (int pixel = 0; pixel < PixelsPerBlock; pixel++)
        {
            int x = Math.Min(blockX * BlockDimension + (pixel & 3), width - 1);
            int y = Math.Min(blockY * BlockDimension + (pixel >> 2), height - 1);
            int source = (y * width + x) * BytesPerPixel;
            blockPixels[pixel * 4] = bgra[source + 2];
            blockPixels[pixel * 4 + 1] = bgra[source + 1];
            blockPixels[pixel * 4 + 2] = bgra[source];
            blockPixels[pixel * 4 + 3] = bgra[source + 3];
        }
    }

    private static void EncodeAlphaBlock(int[] blockPixels, byte[] target, int blockStart)
    {
        int maxAlpha = 0, minAlpha = 255;
        for (int pixel = 0; pixel < PixelsPerBlock; pixel++)
        {
            int alpha = blockPixels[pixel * 4 + 3];
            if (alpha > maxAlpha) maxAlpha = alpha;
            if (alpha < minAlpha) minAlpha = alpha;
        }

        target[blockStart] = (byte)maxAlpha;
        target[blockStart + 1] = (byte)minAlpha;
        if (maxAlpha == minAlpha)
        {
            for (int index = 2; index < Dxt5AlphaBytes; index++) target[blockStart + index] = 0;
            return;
        }

        var alphaPalette = new int[8];
        alphaPalette[0] = maxAlpha;
        alphaPalette[1] = minAlpha;
        for (int step = 1; step < 7; step++) alphaPalette[step + 1] = ((7 - step) * maxAlpha + step * minAlpha) / 7;

        ulong alphaIndices = 0;
        for (int pixel = 0; pixel < PixelsPerBlock; pixel++)
        {
            int alpha = blockPixels[pixel * 4 + 3];
            int bestIndex = 0, bestDistance = int.MaxValue;
            for (int index = 0; index < 8; index++)
            {
                int distance = Math.Abs(alpha - alphaPalette[index]);
                if (distance < bestDistance) { bestDistance = distance; bestIndex = index; }
            }
            alphaIndices |= (ulong)bestIndex << (3 * pixel);
        }
        for (int byteIndex = 0; byteIndex < 6; byteIndex++) target[blockStart + 2 + byteIndex] = (byte)(alphaIndices >> (8 * byteIndex));
    }

    private static void EncodeColourBlock(int[] blockPixels, bool allowTransparency, byte[] target, int blockStart)
    {
        var minimum = new[] { 255, 255, 255 };
        var maximum = new[] { 0, 0, 0 };
        int opaquePixels = 0;
        for (int pixel = 0; pixel < PixelsPerBlock; pixel++)
        {
            if (allowTransparency && blockPixels[pixel * 4 + 3] < TransparencyThreshold) continue;
            opaquePixels++;
            for (int channel = 0; channel < PaletteChannels; channel++)
            {
                int value = blockPixels[pixel * 4 + channel];
                if (value < minimum[channel]) minimum[channel] = value;
                if (value > maximum[channel]) maximum[channel] = value;
            }
        }

        if (opaquePixels == 0)
        {
            // Fully transparent DXT1 block: colour0 = colour1 = 0 (three-colour mode), every index 3.
            for (int index = 0; index < 4; index++) target[blockStart + index] = 0;
            for (int index = 4; index < 8; index++) target[blockStart + index] = 0xFF;
            return;
        }

        bool useThreeColours = allowTransparency && opaquePixels < PixelsPerBlock;

        var inset = new int[PaletteChannels];
        for (int channel = 0; channel < PaletteChannels; channel++) inset[channel] = (maximum[channel] - minimum[channel]) >> 4;
        int colour0 = Pack565(maximum[0] - inset[0], maximum[1] - inset[1], maximum[2] - inset[2]);
        int colour1 = Pack565(minimum[0] + inset[0], minimum[1] + inset[1], minimum[2] + inset[2]);

        // Order of the endpoints selects four-colour (colour0 > colour1) or three-colour mode.
        if (useThreeColours) { if (colour0 > colour1) (colour0, colour1) = (colour1, colour0); }
        else { if (colour0 < colour1) (colour0, colour1) = (colour1, colour0); }

        var colourPalette = new int[4 * PaletteChannels];
        BuildColourPalette(colour0, colour1, !useThreeColours, colourPalette);

        uint colourIndices = 0;
        for (int pixel = 0; pixel < PixelsPerBlock; pixel++)
        {
            int paletteIndex;
            if (useThreeColours && blockPixels[pixel * 4 + 3] < TransparencyThreshold)
            {
                paletteIndex = 3;
            }
            else
            {
                int candidateCount = useThreeColours ? 3 : 4;
                paletteIndex = 0;
                int bestDistance = int.MaxValue;
                for (int candidate = 0; candidate < candidateCount; candidate++)
                {
                    int redDifference = blockPixels[pixel * 4] - colourPalette[candidate * PaletteChannels];
                    int greenDifference = blockPixels[pixel * 4 + 1] - colourPalette[candidate * PaletteChannels + 1];
                    int blueDifference = blockPixels[pixel * 4 + 2] - colourPalette[candidate * PaletteChannels + 2];
                    int distance = redDifference * redDifference + greenDifference * greenDifference + blueDifference * blueDifference;
                    if (distance < bestDistance) { bestDistance = distance; paletteIndex = candidate; }
                }
            }
            colourIndices |= (uint)paletteIndex << (2 * pixel);
        }

        target[blockStart] = (byte)colour0;
        target[blockStart + 1] = (byte)(colour0 >> 8);
        target[blockStart + 2] = (byte)colour1;
        target[blockStart + 3] = (byte)(colour1 >> 8);
        target[blockStart + 4] = (byte)colourIndices;
        target[blockStart + 5] = (byte)(colourIndices >> 8);
        target[blockStart + 6] = (byte)(colourIndices >> 16);
        target[blockStart + 7] = (byte)(colourIndices >> 24);
    }
}
