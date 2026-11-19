using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace UkiyoFileEditor.UI.Tabs;

/// <summary>Tab that lists .srdv files and renders them as images.</summary>
public sealed class TextureTab : UserControl
{
    // ---- Controls -------------------------------------------------------
    private readonly TextBox _filterBox = new() { Dock = DockStyle.Top, Text = "*.srdv", PlaceholderText = "File filter, Enter to apply" };
    private readonly ListBox _fileList = new() { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };

    private readonly ComboBox _formatBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly ComboBox _widthBox = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 70, MaxDropDownItems = 20 };
    private readonly ComboBox _zoomBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
    private readonly ComboBox _backgroundBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly Button _savePngButton = new() { Text = "Save PNG…", AutoSize = true };
    private readonly Button _saveRawButton = new() { Text = "Save decompressed…", AutoSize = true };
    private readonly Label _infoLabel = new() { Dock = DockStyle.Top, AutoSize = false, Height = 22, Padding = new Padding(4, 4, 0, 0) };

    private readonly Panel _viewHost = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(40, 40, 40) };
    private readonly PixelView _pixelView = new();

    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _statusLabel = new() { Text = "Open a folder containing .srdv files." };

    // ---- State ----------------------------------------------------------
    private byte[]? _decoded;
    private CmpInfo _info = new();
    private string _currentPath = "";
    private string _folder = "";
    private bool _recursive = true;
    private bool _suppress;

    public TextureTab()
    {
        Dock = DockStyle.Fill;

        BuildUi();
        WireEvents();

        _formatBox.Items.AddRange(new object[]
        {
            "8-bit alpha (white glyphs)",
            "8-bit grayscale",
            "32-bit BGRA",
            "32-bit RGBA"
        });
        _formatBox.SelectedIndex = 0;

        _zoomBox.Items.AddRange(new object[] { "Fit", "25%", "50%", "100%", "200%", "300%", "400%", "800%" });
        _zoomBox.SelectedIndex = 0;

        _backgroundBox.Items.AddRange(new object[] { "Black", "White", "Checkerboard" });
        _backgroundBox.SelectedIndex = 0;
    }

    // ---- UI construction ------------------------------------------------
    private void BuildUi()
    {
        var viewBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(4),
            WrapContents = true
        };
        viewBar.Controls.Add(new Label { Text = "Format:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        viewBar.Controls.Add(_formatBox);
        viewBar.Controls.Add(new Label { Text = "Width:", AutoSize = true, Padding = new Padding(8, 6, 0, 0) });
        viewBar.Controls.Add(_widthBox);
        viewBar.Controls.Add(new Label { Text = "Zoom:", AutoSize = true, Padding = new Padding(8, 6, 0, 0) });
        viewBar.Controls.Add(_zoomBox);
        viewBar.Controls.Add(new Label { Text = "Background:", AutoSize = true, Padding = new Padding(8, 6, 0, 0) });
        viewBar.Controls.Add(_backgroundBox);
        viewBar.Controls.Add(_savePngButton);
        viewBar.Controls.Add(_saveRawButton);

        // Fill control first, then docked-top controls (later-added controls dock first).
        var right = new Panel { Dock = DockStyle.Fill };
        _viewHost.Controls.Add(_pixelView);
        right.Controls.Add(_viewHost);
        right.Controls.Add(_infoLabel);
        right.Controls.Add(viewBar);

        var left = new Panel { Dock = DockStyle.Fill };
        left.Controls.Add(_fileList);
        left.Controls.Add(_filterBox);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 280,
            FixedPanel = FixedPanel.Panel1
        };
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);

        _statusStrip.Items.Add(_statusLabel);

        Controls.Add(split);
        Controls.Add(_statusStrip);
    }

    private void WireEvents()
    {
        _filterBox.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.Enter)
            {
                eventArgs.SuppressKeyPress = true;
                Reload();
            }
        };

        _fileList.SelectedIndexChanged += (_, _) => OnFileSelected();

        _formatBox.SelectedIndexChanged += (_, _) =>
        {
            RebuildWidths();
            Render();
        };

        _widthBox.SelectedIndexChanged += (_, _) => Render();
        _widthBox.Leave += (_, _) => Render();
        _widthBox.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.Enter)
            {
                eventArgs.SuppressKeyPress = true;
                Render();
            }
        };

        _zoomBox.SelectedIndexChanged += (_, _) => ApplyZoom();
        _backgroundBox.SelectedIndexChanged += (_, _) =>
        {
            _pixelView.Background = _backgroundBox.SelectedIndex switch
            {
                1 => ViewBackground.White,
                2 => ViewBackground.Checkerboard,
                _ => ViewBackground.Black
            };
        };

        _viewHost.Resize += (_, _) =>
        {
            if (_zoomBox.SelectedIndex == 0) ApplyZoom();
        };

        _savePngButton.Click += (_, _) => SavePng();
        _saveRawButton.Click += (_, _) => SaveDecompressed();
    }

    // ---- Folder / file handling ----------------------------------------
    public void LoadFolder(string folder, bool recursive)
    {
        _folder = folder;
        _recursive = recursive;
        Reload();
    }

    private void Reload()
    {
        if (string.IsNullOrEmpty(_folder)) return;

        _fileList.BeginUpdate();
        _fileList.Items.Clear();

        try
        {
            string pattern = string.IsNullOrWhiteSpace(_filterBox.Text) ? "*.srdv" : _filterBox.Text.Trim();
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = _recursive,
                IgnoreInaccessible = true
            };

            var files = Directory.EnumerateFiles(_folder, pattern, options)
                                 .OrderBy(filePath => filePath, StringComparer.OrdinalIgnoreCase);

            foreach (string filePath in files)
            {
                _fileList.Items.Add(new SrdvFileEntry
                {
                    FullPath = filePath,
                    Display = Path.GetRelativePath(_folder, filePath)
                });
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Cannot read folder", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _fileList.EndUpdate();
        }

        _statusLabel.Text = $"{_fileList.Items.Count} file(s) found";

        if (_fileList.Items.Count > 0)
            _fileList.SelectedIndex = 0;
        else
            ClearView();
    }

    private void ClearView()
    {
        var previousImage = _pixelView.Image;
        _pixelView.Image = null;
        previousImage?.Dispose();
        _decoded = null;
        _currentPath = "";
        _infoLabel.Text = "";
    }

    private void OnFileSelected()
    {
        if (_fileList.SelectedItem is not SrdvFileEntry entry) return;

        try
        {
            byte[] rawBytes = File.ReadAllBytes(entry.FullPath);
            _decoded = CmpDecoder.Decompress(rawBytes, out _info);
            _currentPath = entry.FullPath;
        }
        catch (Exception exception)
        {
            ClearView();
            _infoLabel.Text = $"{entry.Display}: {exception.Message}";
            _statusLabel.Text = "Error reading file";
            return;
        }

        RebuildWidths();
        Render();
    }

    // ---- Rendering ------------------------------------------------------
    private PixelKind CurrentKind => _formatBox.SelectedIndex switch
    {
        1 => PixelKind.Gray8,
        2 => PixelKind.Bgra32,
        3 => PixelKind.Rgba32,
        _ => PixelKind.Alpha8
    };

    private static int BytesPerPixel(PixelKind kind) =>
        kind is PixelKind.Bgra32 or PixelKind.Rgba32 ? 4 : 1;

    /// <summary>Fills the width box with every divisor of the pixel count and picks the one closest to a square.</summary>
    private void RebuildWidths()
    {
        if (_decoded == null || _decoded.Length == 0) return;

        long pixels = _decoded.Length / BytesPerPixel(CurrentKind);
        if (pixels <= 0) return;

        var widths = new List<int>();
        for (int width = 8; width <= 16384 && width <= pixels; width++)
        {
            if (pixels % width == 0) widths.Add(width);
        }

        double root = Math.Sqrt(pixels);
        int best = widths.Count > 0
            ? widths.OrderBy(width => Math.Abs(width - root)).First()
            : (int)Math.Max(1, Math.Round(root));

        _suppress = true;
        _widthBox.Items.Clear();
        _widthBox.Items.AddRange(widths.Cast<object>().ToArray());
        _widthBox.Text = best.ToString();
        _suppress = false;
    }

    private void Render()
    {
        if (_suppress || _decoded == null) return;

        if (!int.TryParse(_widthBox.Text, out int width) || width < 1 || width > 16384)
        {
            _statusLabel.Text = "Width must be a number between 1 and 16384.";
            return;
        }

        PixelKind kind = CurrentKind;
        int bytesPerPixel = BytesPerPixel(kind);
        int rowBytes = width * bytesPerPixel;
        int height = (_decoded.Length + rowBytes - 1) / rowBytes;

        if (height < 1 || (long)width * height > 100_000_000)
        {
            _statusLabel.Text = "Image dimensions are out of range.";
            return;
        }

        Bitmap bitmap = BuildBitmap(_decoded, width, height, kind);
        var previousImage = _pixelView.Image;
        _pixelView.Image = bitmap;
        previousImage?.Dispose();

        ApplyZoom();

        string container = _info.IsCmp
            ? $"$CMP, {_info.ChunkCount} chunk(s) [{_info.Modes}]"
            : "raw (no $CMP header)";
        string text = $"{Path.GetFileName(_currentPath)}  |  {container}  |  {_info.CompressedSize:N0} → {_info.DecompressedSize:N0} bytes  |  {width}×{height}";
        if (!string.IsNullOrEmpty(_info.Warning)) text += "  |  " + _info.Warning;

        _infoLabel.Text = text;
        _statusLabel.Text = $"{_fileList.SelectedIndex + 1} / {_fileList.Items.Count}";
    }

    private static Bitmap BuildBitmap(byte[] data, int width, int height, PixelKind kind)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, width, height);
        BitmapData bitmapData = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            int bytesPerPixel = BytesPerPixel(kind);
            byte[] row = new byte[width * 4];

            for (int pixelY = 0; pixelY < height; pixelY++)
            {
                Array.Clear(row);

                for (int pixelX = 0; pixelX < width; pixelX++)
                {
                    long sourceOffset = ((long)pixelY * width + pixelX) * bytesPerPixel;
                    if (sourceOffset + bytesPerPixel > data.Length) break; // leave the rest transparent

                    int sourceIndex = (int)sourceOffset;
                    int targetIndex = pixelX * 4;

                    switch (kind)
                    {
                        case PixelKind.Alpha8:
                            row[targetIndex] = 255; row[targetIndex + 1] = 255; row[targetIndex + 2] = 255; row[targetIndex + 3] = data[sourceIndex];
                            break;
                        case PixelKind.Gray8:
                            row[targetIndex] = data[sourceIndex]; row[targetIndex + 1] = data[sourceIndex]; row[targetIndex + 2] = data[sourceIndex]; row[targetIndex + 3] = 255;
                            break;
                        case PixelKind.Bgra32:
                            row[targetIndex] = data[sourceIndex]; row[targetIndex + 1] = data[sourceIndex + 1]; row[targetIndex + 2] = data[sourceIndex + 2]; row[targetIndex + 3] = data[sourceIndex + 3];
                            break;
                        case PixelKind.Rgba32:
                            row[targetIndex] = data[sourceIndex + 2]; row[targetIndex + 1] = data[sourceIndex + 1]; row[targetIndex + 2] = data[sourceIndex]; row[targetIndex + 3] = data[sourceIndex + 3];
                            break;
                    }
                }

                Marshal.Copy(row, 0, bitmapData.Scan0 + pixelY * bitmapData.Stride, row.Length);
            }
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }

        return bitmap;
    }

    private void ApplyZoom()
    {
        Bitmap? bitmap = _pixelView.Image;
        if (bitmap == null) return;

        float zoom;
        string choice = _zoomBox.SelectedItem?.ToString() ?? "Fit";

        if (choice == "Fit")
        {
            Size avail = _viewHost.ClientSize;
            float zoomToFitWidth = (avail.Width - 2f) / bitmap.Width;
            float zoomToFitHeight = (avail.Height - 2f) / bitmap.Height;
            zoom = Math.Min(Math.Min(zoomToFitWidth, zoomToFitHeight), 8f);
            if (zoom <= 0f) zoom = 1f;
        }
        else
        {
            zoom = float.Parse(choice.TrimEnd('%')) / 100f;
        }

        _pixelView.Zoom = zoom;
    }

    // ---- Saving ---------------------------------------------------------
    private void SavePng()
    {
        if (_pixelView.Image == null) return;

        using var dialog = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            FileName = Path.GetFileNameWithoutExtension(_currentPath) + ".png"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            _pixelView.Image.Save(dialog.FileName, ImageFormat.Png);
            _statusLabel.Text = "Saved " + dialog.FileName;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveDecompressed()
    {
        if (_decoded == null) return;

        using var dialog = new SaveFileDialog
        {
            Filter = "Binary data (*.bin)|*.bin|All files (*.*)|*.*",
            FileName = Path.GetFileName(_currentPath) + ".dec.bin"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            File.WriteAllBytes(dialog.FileName, _decoded);
            _statusLabel.Text = "Saved " + dialog.FileName;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
