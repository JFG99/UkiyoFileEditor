using System.Drawing.Drawing2D;

namespace UkiyoFileEditor.UI.Controls;

/// <summary>Zoomable bitmap view with nearest-neighbour scaling. Sizes itself to the zoomed image so a parent panel can scroll it.</summary>
public sealed class PixelView : Control
{
    private Bitmap? _image;
    private float _zoom = 1f;
    private ViewBackground _background = ViewBackground.Black;

    public PixelView()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public Bitmap? Image
    {
        get => _image;
        set
        {
            _image = value;
            UpdateViewSize();
            Invalidate();
        }
    }

    public float Zoom
    {
        get => _zoom;
        set
        {
            _zoom = Math.Clamp(value, 0.05f, 64f);
            UpdateViewSize();
            Invalidate();
        }
    }

    public ViewBackground Background
    {
        get => _background;
        set
        {
            _background = value;
            Invalidate();
        }
    }

    private void UpdateViewSize()
    {
        if (_image == null)
        {
            Size = new Size(1, 1);
            return;
        }

        Size = new Size(
            Math.Max(1, (int)Math.Round(_image.Width * _zoom)),
            Math.Max(1, (int)Math.Round(_image.Height * _zoom)));
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;

        switch (_background)
        {
            case ViewBackground.White:
                graphics.Clear(Color.White);
                break;
            case ViewBackground.Checkerboard:
                DrawChecker(graphics, eventArgs.ClipRectangle);
                break;
            default:
                graphics.Clear(Color.Black);
                break;
        }

        if (_image == null) return;

        graphics.InterpolationMode = _zoom >= 1f ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.DrawImage(_image, new Rectangle(0, 0, Width, Height));
    }

    private static void DrawChecker(Graphics graphics, Rectangle clip)
    {
        const int cell = 8;
        using var light = new SolidBrush(Color.FromArgb(204, 204, 204));
        using var dark = new SolidBrush(Color.FromArgb(153, 153, 153));

        int firstCellX = clip.Left / cell * cell;
        int firstCellY = clip.Top / cell * cell;

        for (int cellY = firstCellY; cellY < clip.Bottom; cellY += cell)
        {
            for (int cellX = firstCellX; cellX < clip.Right; cellX += cell)
            {
                bool isDarkCell = ((cellX / cell) + (cellY / cell)) % 2 != 0;
                graphics.FillRectangle(isDarkCell ? dark : light, cellX, cellY, cell, cell);
            }
        }
    }
}
