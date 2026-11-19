using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace UkiyoFileEditor.Services;

/// <summary>Conversion between top-down BGRA pixel buffers and GDI+ bitmaps.</summary>
public static class BitmapConverter
{
    private const int BytesPerPixel = 4;

    public static Bitmap ToBitmap(byte[] bgra, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        BitmapData bitmapData = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int row = 0; row < height; row++)
                Marshal.Copy(bgra, row * width * BytesPerPixel, bitmapData.Scan0 + row * bitmapData.Stride, width * BytesPerPixel);
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }
        return bitmap;
    }

    /// <summary>Reads a PNG (or any image GDI+ opens) as top-down BGRA.</summary>
    public static byte[] LoadBgra(string path, out int width, out int height)
    {
        using var source = new Bitmap(path);
        width = source.Width;
        height = source.Height;
        using Bitmap converted = source.Clone(new Rectangle(0, 0, width, height), PixelFormat.Format32bppArgb);

        var bgra = new byte[width * height * BytesPerPixel];
        BitmapData bitmapData = converted.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int row = 0; row < height; row++)
                Marshal.Copy(bitmapData.Scan0 + row * bitmapData.Stride, bgra, row * width * BytesPerPixel, width * BytesPerPixel);
        }
        finally
        {
            converted.UnlockBits(bitmapData);
        }
        return bgra;
    }

    /// <summary>True when two BGRA buffers show the same picture. The colour of fully transparent pixels does not count.</summary>
    public static bool SamePicture(byte[] first, byte[] second)
    {
        if (first.Length != second.Length) return false;

        for (int pixel = 0; pixel < first.Length; pixel += BytesPerPixel)
        {
            const int alphaOffset = 3;
            if (first[pixel + alphaOffset] != second[pixel + alphaOffset]) return false;
            if (first[pixel + alphaOffset] == 0) continue;
            if (first[pixel] != second[pixel] || first[pixel + 1] != second[pixel + 1] || first[pixel + 2] != second[pixel + 2]) return false;
        }
        return true;
    }
}
