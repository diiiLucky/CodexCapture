using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using CodexCapture.Models;
using CodexCapture.Services;

namespace CodexCapture.Windows;

public sealed class FloatingWidgetWindow : Window
{
    private readonly AppController _controller;
    private readonly System.Windows.Media.Effects.DropShadowEffect _shadow = new()
    {
        BlurRadius = 16,
        ShadowDepth = 3,
        Opacity = 0.24,
        Color = Color.FromRgb(15, 118, 110)
    };
    private Point? _mouseDownPoint;
    private bool _dragged;

    public FloatingWidgetWindow(AppController controller, AppSettings settings)
    {
        _controller = controller;
        Width = 58;
        Height = 44;
        Left = settings.FloatingWidgetState.Left;
        Top = settings.FloatingWidgetState.Top;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;

        var shell = BuildShell();
        Content = shell;

        shell.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _mouseDownPoint = e.GetPosition(this);
            _dragged = false;
            shell.CaptureMouse();
            e.Handled = true;
        };
        shell.PreviewMouseMove += (_, e) =>
        {
            if (_mouseDownPoint is null || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var current = e.GetPosition(this);
            var dx = current.X - _mouseDownPoint.Value.X;
            var dy = current.Y - _mouseDownPoint.Value.Y;
            if (Math.Sqrt(dx * dx + dy * dy) < 3)
            {
                return;
            }

            _dragged = true;
            Left += dx;
            Top += dy;
            e.Handled = true;
        };
        shell.PreviewMouseLeftButtonUp += (_, e) =>
        {
            shell.ReleaseMouseCapture();
            _mouseDownPoint = null;
            if (!_dragged)
            {
                _controller.BeginCapture();
            }

            e.Handled = true;
        };
    }

    private Border BuildShell()
    {
        var scaleTransform = new ScaleTransform(1.0, 1.0);
        var shell = new Border
        {
            CornerRadius = new CornerRadius(18),
            Background = new LinearGradientBrush(
                Color.FromRgb(15, 118, 110),
                Color.FromRgb(20, 184, 166),
                35),
            BorderBrush = new SolidColorBrush(Color.FromArgb(130, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.SizeAll,
            ToolTip = "点击截图 / 拖拽移动",
            Effect = _shadow,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = scaleTransform,
            Child = new Grid
            {
                Children =
                {
                    new System.Windows.Shapes.Path
                    {
                        Data = Geometry.Parse("M8,13 L8,8 L13,8 M25,8 L30,8 L30,13 M30,25 L30,30 L25,30 M13,30 L8,30 L8,25 M14,14 L24,14 L24,24 L14,24 Z"),
                        Stroke = Brushes.White,
                        StrokeThickness = 2.2,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        StrokeLineJoin = PenLineJoin.Round,
                        Stretch = Stretch.Uniform,
                        Width = 28,
                        Height = 28,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            }
        };

        shell.MouseEnter += (_, _) =>
        {
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.08, TimeSpan.FromMilliseconds(150)));
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.08, TimeSpan.FromMilliseconds(150)));
            _shadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(22, TimeSpan.FromMilliseconds(150)));
            _shadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, new DoubleAnimation(0.36, TimeSpan.FromMilliseconds(150)));
        };
        shell.MouseLeave += (_, _) =>
        {
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(150)));
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(150)));
            _shadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(16, TimeSpan.FromMilliseconds(150)));
            _shadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, new DoubleAnimation(0.24, TimeSpan.FromMilliseconds(150)));
        };

        return shell;
    }
}
