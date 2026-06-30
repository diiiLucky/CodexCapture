using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CodexCapture.Models;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;

namespace CodexCapture.Windows;

public sealed class CaptureReviewOverlayWindow : Window
{
    private readonly CaptureResult _capture;
    private readonly Canvas _canvas = new();
    private readonly Border _selection = new();

    public CaptureReviewOverlayWindow(CaptureResult capture)
    {
        _capture = capture;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;

        var bounds = GetVirtualScreenBounds();
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;

        Content = _canvas;
        Loaded += (_, _) => DrawReviewOverlay();
    }

    private void DrawReviewOverlay()
    {
        var selectionRect = ToLocalRect(new Int32Rect(
            _capture.ScreenX,
            _capture.ScreenY,
            _capture.ScreenWidth,
            _capture.ScreenHeight));

        DrawDimRegions(selectionRect);
        DrawSelection(selectionRect);
        DrawHandles(selectionRect);
    }

    private void DrawDimRegions(WpfRect selectionRect)
    {
        var dimBrush = new SolidColorBrush(Color.FromArgb(104, 0, 56, 22));

        AddDimRect(0, 0, Width, Math.Max(0, selectionRect.Top), dimBrush);
        AddDimRect(0, selectionRect.Bottom, Width, Math.Max(0, Height - selectionRect.Bottom), dimBrush);
        AddDimRect(0, selectionRect.Top, Math.Max(0, selectionRect.Left), selectionRect.Height, dimBrush);
        AddDimRect(selectionRect.Right, selectionRect.Top, Math.Max(0, Width - selectionRect.Right), selectionRect.Height, dimBrush);
    }

    private void DrawSelection(WpfRect rect)
    {
        _selection.BorderBrush = new SolidColorBrush(Color.FromRgb(18, 24, 38));
        _selection.BorderThickness = new Thickness(1.5);
        _selection.Background = new SolidColorBrush(Color.FromArgb(42, 98, 255, 149));
        _selection.CornerRadius = new CornerRadius(2);
        SetElementBounds(_selection, rect);
        _canvas.Children.Add(_selection);

        var dashed = new Rectangle
        {
            Stroke = Brushes.White,
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 3, 3 },
            Fill = Brushes.Transparent,
            Width = rect.Width,
            Height = rect.Height
        };
        Canvas.SetLeft(dashed, rect.Left);
        Canvas.SetTop(dashed, rect.Top);
        _canvas.Children.Add(dashed);
    }

    private void DrawHandles(WpfRect rect)
    {
        var points = new[]
        {
            new WpfPoint(rect.Left, rect.Top),
            new WpfPoint(rect.Left + rect.Width / 2, rect.Top),
            new WpfPoint(rect.Right, rect.Top),
            new WpfPoint(rect.Left, rect.Top + rect.Height / 2),
            new WpfPoint(rect.Right, rect.Top + rect.Height / 2),
            new WpfPoint(rect.Left, rect.Bottom),
            new WpfPoint(rect.Left + rect.Width / 2, rect.Bottom),
            new WpfPoint(rect.Right, rect.Bottom)
        };

        foreach (var point in points)
        {
            var handle = new Ellipse
            {
                Width = 11,
                Height = 11,
                Fill = new SolidColorBrush(Color.FromRgb(236, 253, 245)),
                Stroke = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                StrokeThickness = 1.2,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 4,
                    ShadowDepth = 1,
                    Opacity = 0.24
                }
            };
            Canvas.SetLeft(handle, point.X - handle.Width / 2);
            Canvas.SetTop(handle, point.Y - handle.Height / 2);
            _canvas.Children.Add(handle);
        }
    }

    private void AddDimRect(double left, double top, double width, double height, Brush brush)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var rect = new Rectangle
        {
            Width = width,
            Height = height,
            Fill = brush
        };
        Canvas.SetLeft(rect, left);
        Canvas.SetTop(rect, top);
        _canvas.Children.Add(rect);
    }

    private WpfRect ToLocalRect(Int32Rect screenRect)
    {
        var topLeft = PointFromScreen(new WpfPoint(screenRect.X, screenRect.Y));
        var bottomRight = PointFromScreen(new WpfPoint(screenRect.X + screenRect.Width, screenRect.Y + screenRect.Height));
        return new WpfRect(topLeft, bottomRight);
    }

    private static void SetElementBounds(FrameworkElement element, WpfRect rect)
    {
        Canvas.SetLeft(element, rect.Left);
        Canvas.SetTop(element, rect.Top);
        element.Width = rect.Width;
        element.Height = rect.Height;
    }

    private static WpfRect GetVirtualScreenBounds() => new(
        SystemParameters.VirtualScreenLeft,
        SystemParameters.VirtualScreenTop,
        SystemParameters.VirtualScreenWidth,
        SystemParameters.VirtualScreenHeight);
}
