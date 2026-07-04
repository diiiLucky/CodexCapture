using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using CodexCapture.Models;

namespace CodexCapture.Services;

public sealed class ScreenCaptureService
{
    private readonly IClipboardService _clipboardService;

    public ScreenCaptureService()
        : this(new ClipboardService())
    {
    }

    internal ScreenCaptureService(IClipboardService clipboardService)
    {
        _clipboardService = clipboardService;
    }

    public CaptureResult CaptureRegion(Int32Rect bounds, string historyDirectory, CaptureSourceType sourceType, string? windowTitle = null)
    {
        bounds = ScreenCoordinateService.ClipToPhysicalVirtualScreen(bounds);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException("Capture bounds are empty.");
        }

        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, new System.Drawing.Size(bounds.Width, bounds.Height), CopyPixelOperation.SourceCopy);
        }

        var capture = CreateCaptureResult(bounds, historyDirectory, sourceType, windowTitle);
        var dir = Path.GetDirectoryName(capture.ImagePath);
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
        }

        bitmap.Save(capture.ImagePath, ImageFormat.Png);
        return capture;
    }

    public void CopyToClipboard(CaptureResult capture)
    {
        if (!File.Exists(capture.ImagePath))
        {
            throw new FileNotFoundException("Capture image not found.", capture.ImagePath);
        }

        BitmapFrame image;
        using (var stream = File.OpenRead(capture.ImagePath))
        {
            image = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        }

        var dataObject = new DataObject();
        dataObject.SetImage(image);
        dataObject.SetFileDropList([capture.ImagePath]);
        if (!_clipboardService.TrySetDataObject(dataObject, out var errorMessage))
        {
            throw new InvalidOperationException($"复制截图到剪贴板失败：{errorMessage}");
        }
    }

    private static CaptureResult CreateCaptureResult(
        Int32Rect bounds,
        string historyDirectory,
        CaptureSourceType sourceType,
        string? windowTitle)
    {
        var now = DateTimeOffset.Now;
        var id = now.ToString("yyyyMMdd-HHmmss-fff");
        var dayDirectory = Path.Combine(historyDirectory, now.ToString("yyyy-MM-dd"));
        var path = Path.Combine(dayDirectory, $"{id}.png");
        return new CaptureResult
        {
            Id = id,
            ImagePath = path,
            SourceType = sourceType,
            WidthPx = bounds.Width,
            HeightPx = bounds.Height,
            ScreenX = bounds.X,
            ScreenY = bounds.Y,
            ScreenWidth = bounds.Width,
            ScreenHeight = bounds.Height,
            WindowTitle = windowTitle,
            CreatedAt = now
        };
    }
}
