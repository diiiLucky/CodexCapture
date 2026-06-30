using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CodexCapture.Models;
using CodexCapture.Services;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;

namespace CodexCapture.Windows;

public sealed class CaptureOverlayWindow : Window
{
    private readonly WindowDetectionService _windowDetectionService;
    private readonly ScreenCaptureService _screenCaptureService;
    private readonly string _historyDirectory;
    private readonly Canvas _canvas = new();
    private readonly Border _highlight = new();
    private readonly Border _selection = new();
    private readonly Border _hud = new();
    private readonly TextBlock _hudText = new();
    private WpfPoint? _dragStart;
    private DateTimeOffset? _mouseDownAt;
    private DetectedWindow? _hoveredWindow;
    private bool _isDragging;

    public event EventHandler<CaptureResult>? CaptureCompleted;

    public CaptureOverlayWindow(
        WindowDetectionService windowDetectionService,
        ScreenCaptureService screenCaptureService,
        string historyDirectory)
    {
        _windowDetectionService = windowDetectionService;
        _screenCaptureService = screenCaptureService;
        _historyDirectory = historyDirectory;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(32, 0, 0, 0));
        Topmost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;
        ResizeMode = ResizeMode.NoResize;
        Opacity = 0;

        var bounds = GetVirtualScreenBounds();
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;

        Content = _canvas;
        ConfigureVisuals();

        MouseMove += OnMouseMove;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };

        Loaded += (_, _) =>
        {
            var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
            BeginAnimation(OpacityProperty, anim);
        };
    }

    private void ConfigureVisuals()
    {
        _highlight.BorderBrush = new SolidColorBrush(Color.FromRgb(20, 184, 166));
        _highlight.BorderThickness = new Thickness(3);
        _highlight.Background = new SolidColorBrush(Color.FromArgb(24, 20, 184, 166));
        _highlight.CornerRadius = new CornerRadius(4);
        _highlight.Visibility = Visibility.Collapsed;
        _canvas.Children.Add(_highlight);

        _selection.BorderBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248));
        _selection.BorderThickness = new Thickness(2);
        _selection.Background = new SolidColorBrush(Color.FromArgb(16, 56, 189, 248));
        _selection.CornerRadius = new CornerRadius(2);
        _selection.Visibility = Visibility.Collapsed;
        _canvas.Children.Add(_selection);

        _hud.Child = _hudText;
        _hud.Background = new SolidColorBrush(Color.FromArgb(220, 15, 23, 42));
        _hud.CornerRadius = new CornerRadius(6);
        _hud.Padding = new Thickness(10, 5, 10, 5);
        _hudText.Foreground = Brushes.White;
        _hudText.FontWeight = FontWeights.SemiBold;
        _hudText.FontSize = 13;
        _hud.Visibility = Visibility.Collapsed;
        _canvas.Children.Add(_hud);
    }

    private void OnMouseMove(object sender, WpfMouseEventArgs e)
    {
        var local = e.GetPosition(this);
        var screen = ToScreenPoint(local);

        if (_dragStart is { } dragStart)
        {
            var elapsed = DateTimeOffset.Now - (_mouseDownAt ?? DateTimeOffset.Now);
            if (!_isDragging && (elapsed.TotalMilliseconds > 350 || Distance(dragStart, local) > 8))
            {
                _isDragging = true;
                _selection.Visibility = Visibility.Visible;
                _highlight.Visibility = Visibility.Collapsed;
            }

            if (_isDragging)
            {
                UpdateSelection(dragStart, local);
                return;
            }
        }

        _hoveredWindow = _windowDetectionService.Detect(screen);
        if (_hoveredWindow is null)
        {
            _highlight.Visibility = Visibility.Collapsed;
            _hud.Visibility = Visibility.Collapsed;
            return;
        }

        var rect = ToLocalRect(_hoveredWindow.Bounds);
        SetElementBounds(_highlight, rect);
        _highlight.Visibility = Visibility.Visible;
        ShowHud($"{_hoveredWindow.Bounds.Width} x {_hoveredWindow.Bounds.Height} px", local);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _mouseDownAt = DateTimeOffset.Now;
        _isDragging = false;
        CaptureMouse();
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var end = e.GetPosition(this);
        ReleaseMouseCapture();

        try
        {
            CaptureResult? capture = null;
            var captureTarget = default(PendingCapture);
            if (_isDragging && _dragStart is { } start)
            {
                var bounds = GetPhysicalBounds(start, end);
                if (bounds.Width >= 2 && bounds.Height >= 2)
                {
                    captureTarget = new PendingCapture(
                        bounds,
                        CaptureSourceType.Region,
                        null);
                }
            }
            else if (_hoveredWindow is not null)
            {
                captureTarget = new PendingCapture(
                    _hoveredWindow.Bounds,
                    CaptureSourceType.Window,
                    _hoveredWindow.Title);
            }

            if (captureTarget is not null)
            {
                HideOverlayForCapture();
                capture = _screenCaptureService.CaptureRegion(
                    captureTarget.Bounds,
                    _historyDirectory,
                    captureTarget.SourceType,
                    captureTarget.WindowTitle);
            }

            if (capture is not null)
            {
                CaptureCompleted?.Invoke(this, capture);
            }
        }
        finally
        {
            Close();
            _dragStart = null;
            _mouseDownAt = null;
        }
    }

    private void UpdateSelection(WpfPoint start, WpfPoint end)
    {
        var rect = NormalizeRect(start, end);
        SetElementBounds(_selection, rect);
        var bounds = GetPhysicalBounds(start, end);
        ShowHud($"{bounds.Width} x {bounds.Height} px", new WpfPoint(rect.Right + 8, rect.Bottom + 8));
    }

    private WpfPoint ToScreenPoint(WpfPoint local) => PointToScreen(local);

    private WpfRect ToLocalRect(Int32Rect screenRect)
    {
        var topLeft = PointFromScreen(new WpfPoint(screenRect.X, screenRect.Y));
        var bottomRight = PointFromScreen(new WpfPoint(screenRect.X + screenRect.Width, screenRect.Y + screenRect.Height));
        return new WpfRect(topLeft, bottomRight);
    }

    private Int32Rect GetPhysicalBounds(WpfPoint start, WpfPoint end)
    {
        var screenStart = PointToScreen(start);
        var screenEnd = PointToScreen(end);
        var left = (int)Math.Round(Math.Min(screenStart.X, screenEnd.X));
        var top = (int)Math.Round(Math.Min(screenStart.Y, screenEnd.Y));
        var right = (int)Math.Round(Math.Max(screenStart.X, screenEnd.X));
        var bottom = (int)Math.Round(Math.Max(screenStart.Y, screenEnd.Y));
        return new Int32Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static WpfRect NormalizeRect(WpfPoint a, WpfPoint b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        return new WpfRect(x, y, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }

    private static double Distance(WpfPoint a, WpfPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static void SetElementBounds(FrameworkElement element, WpfRect rect)
    {
        Canvas.SetLeft(element, rect.Left);
        Canvas.SetTop(element, rect.Top);
        element.Width = rect.Width;
        element.Height = rect.Height;
    }

    private void ShowHud(string text, WpfPoint anchor)
    {
        _hudText.Text = text;
        _hud.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var left = Math.Min(Math.Max(8, anchor.X + 12), Math.Max(8, Width - _hud.DesiredSize.Width - 8));
        var top = Math.Min(Math.Max(8, anchor.Y + 12), Math.Max(8, Height - _hud.DesiredSize.Height - 8));
        Canvas.SetLeft(_hud, left);
        Canvas.SetTop(_hud, top);
        _hud.Visibility = Visibility.Visible;
    }

    private static WpfRect GetVirtualScreenBounds() => new(
        SystemParameters.VirtualScreenLeft,
        SystemParameters.VirtualScreenTop,
        SystemParameters.VirtualScreenWidth,
        SystemParameters.VirtualScreenHeight);

    private void HideOverlayForCapture()
    {
        Visibility = Visibility.Hidden;
        Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
        Thread.Sleep(80);
    }

    private sealed record PendingCapture(Int32Rect Bounds, CaptureSourceType SourceType, string? WindowTitle);
}
