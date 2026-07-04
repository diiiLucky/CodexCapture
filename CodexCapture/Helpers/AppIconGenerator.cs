using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrawingColor = System.Drawing.Color;
using DrawingBrush = System.Drawing.Drawing2D.LinearGradientBrush;
using DrawingPen = System.Drawing.Pen;

namespace CodexCapture.Helpers;

public static class AppIconGenerator
{
    // Match the floating widget: blue gradient circle + white crop icon
    private static readonly DrawingColor BgStart = DrawingColor.FromArgb(255, 37, 99, 235);  // #2563EB
    private static readonly DrawingColor BgEnd   = DrawingColor.FromArgb(255, 29, 78, 216);  // #1D4ED8
    private static readonly DrawingColor FgWhite = DrawingColor.FromArgb(255, 255, 255, 255);

    public static Icon CreateIcon(int size = 64)
    {
        using var bmp = RenderIconBitmap(size);
        var hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    /// <summary>Returns a WPF ImageSource suitable for Window.Icon.</summary>
    public static ImageSource CreateImageSource(int size = 64)
    {
        using var bmp = RenderIconBitmap(size);
        var hBitmap = bmp.GetHbitmap();
        try
        {
            return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }

    /// <summary>Save the icon to an .ico file.</summary>
    public static void SaveIconFile(string filePath, int size = 64)
    {
        using var bmp = RenderIconBitmap(size);
        var hIcon = bmp.GetHicon();
        using var icon = Icon.FromHandle(hIcon);
        using var fs = new FileStream(filePath, FileMode.Create);
        icon.Save(fs);
    }

    private static Bitmap RenderIconBitmap(int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var margin = size * 0.08f;
        var diameter = size - margin * 2;

        // Blue gradient circle (45° as in floating widget)
        using var gradientBrush = new DrawingBrush(
            new PointF(0, 0), new PointF(size, size), BgStart, BgEnd);
        g.FillEllipse(gradientBrush, margin, margin, diameter, diameter);

        // White crop icon — two overlapping L-shaped corners
        DrawCropIcon(g, size);

        return bmp;
    }

    private static void DrawCropIcon(Graphics g, int size)
    {
        var center = size / 2f;
        var scale = size / 64f; // base design on 64×64

        using var pen = new DrawingPen(FgWhite, 3.5f * scale)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };

        // The Fluent Crop20 symbol: two L-shaped corners
        // ┌──┐  top-left L and bottom-right L
        // └──┘

        // Top-left corner (top-left quadrant)
        float tlx = center - 10f * scale;
        float tly = center - 12f * scale;
        // horizontal →
        g.DrawLine(pen, tlx, tly, tlx + 11f * scale, tly);
        // vertical ↓ (drop the tip)
        g.DrawLine(pen, tlx, tly, tlx, tly + 12f * scale);

        // Bottom-right corner (bottom-right quadrant)
        float brx = center + 10f * scale;
        float bry = center + 12f * scale;
        // horizontal ←
        g.DrawLine(pen, brx, bry, brx - 11f * scale, bry);
        // vertical ↑ (drop the tip)
        g.DrawLine(pen, brx, bry, brx, bry - 12f * scale);
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
