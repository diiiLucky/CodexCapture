using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using CodexCapture.Models;
using CodexCapture.Services;

namespace CodexCapture.Windows;

public sealed class CaptureMenuWindow : Window
{
    private const double CaptureGap = 8;

    private readonly CaptureResult _capture;
    private readonly ScreenCaptureService _screenCaptureService;
    private readonly AiTranslationService _translationService;
    private readonly CodexImportService _codexImportService;
    private readonly SettingsService _settingsService;
    private readonly LogService _logService;
    private readonly Action _closeReviewOverlay;

    public CaptureMenuWindow(
        CaptureResult capture,
        ScreenCaptureService screenCaptureService,
        AiTranslationService translationService,
        CodexImportService codexImportService,
        SettingsService settingsService,
        LogService logService,
        Action closeReviewOverlay)
    {
        _capture = capture;
        _screenCaptureService = screenCaptureService;
        _translationService = translationService;
        _codexImportService = codexImportService;
        _settingsService = settingsService;
        _logService = logService;
        _closeReviewOverlay = closeReviewOverlay;

        Title = "截图操作";
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Opacity = 0;

        Content = BuildContent();
        Loaded += (_, _) =>
        {
            PositionNearCapture();
            AnimateIn();
        };
        Closed += (_, _) => SafeCloseReviewOverlay();
    }

    private UIElement BuildContent()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8)
        };

        panel.Children.Add(ToolButton("复制", CopyIcon(), () => RunAndClose(OnCopy)));
        panel.Children.Add(Separator());
        panel.Children.Add(ToolButton("翻译", TranslateIcon(), async () => await RunAndCloseAsync(OnTranslateAsync)));
        panel.Children.Add(Separator());
        panel.Children.Add(ToolButton("问 Codex", QuoteIcon(), async () => await RunAndCloseAsync(OnAskCodexAsync)));
        panel.Children.Add(Separator());
        panel.Children.Add(ToolButton("打开", OpenIcon(), () => RunAndClose(OnOpenFile)));
        panel.Children.Add(Separator());
        panel.Children.Add(ToolButton("取消", CloseIcon(), CloseWithReview));

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(245, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Child = panel,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 3,
                Opacity = 0.2
            }
        };
    }

    private Button ToolButton(string text, Geometry icon, Action action)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(new System.Windows.Shapes.Path
        {
            Data = icon,
            Width = 18,
            Height = 18,
            Stretch = Stretch.Uniform,
            Stroke = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            StrokeThickness = 1.8,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Margin = new Thickness(0, 0, 7, 0)
        });
        content.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            VerticalAlignment = VerticalAlignment.Center
        });

        var button = new Button
        {
            Content = content,
            MinHeight = 42,
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.Click += (_, _) => action();
        return button;
    }

    private static Border Separator() => new()
    {
        Width = 1,
        Margin = new Thickness(4, 8, 4, 8),
        Background = new SolidColorBrush(Color.FromRgb(226, 232, 240))
    };

    private void PositionNearCapture()
    {
        var captureRect = new Int32Rect(_capture.ScreenX, _capture.ScreenY, _capture.ScreenWidth, _capture.ScreenHeight);
        var screen = System.Windows.Forms.Screen.FromRectangle(new System.Drawing.Rectangle(
            captureRect.X,
            captureRect.Y,
            Math.Max(1, captureRect.Width),
            Math.Max(1, captureRect.Height))).WorkingArea;

        var width = ActualWidth > 0 ? ActualWidth : 520;
        var height = ActualHeight > 0 ? ActualHeight : 58;

        var left = captureRect.X + captureRect.Width / 2.0 - width / 2.0;
        var top = captureRect.Y - height - CaptureGap;
        if (top < screen.Top + CaptureGap)
        {
            top = captureRect.Y + captureRect.Height + CaptureGap;
        }

        Left = Math.Min(Math.Max(screen.Left + CaptureGap, left), screen.Right - width - CaptureGap);
        Top = Math.Min(Math.Max(screen.Top + CaptureGap, top), screen.Bottom - height - CaptureGap);
    }

    private void RunAndClose(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            _logService.Error(exception, "Capture menu action failed.");
            MessageBox.Show(exception.Message, "操作失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            CloseWithReview();
        }
    }

    private async Task RunAndCloseAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            _logService.Error(exception, "Capture menu action failed.");
            MessageBox.Show(exception.Message, "操作失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            CloseWithReview();
        }
    }

    private void OnCopy() => _screenCaptureService.CopyToClipboard(_capture);

    private async Task OnTranslateAsync()
    {
        var result = await _translationService.TranslateAsync(new TranslationRequest
        {
            Capture = _capture,
            TargetLanguage = _settingsService.Current.DefaultTargetLanguage
        });
        var window = new TranslationWindow(_capture, result, _codexImportService, _settingsService);
        window.Show();
    }

    private async Task OnAskCodexAsync()
    {
        await _codexImportService.ImportAsync(new CodexImportRequest
        {
            Capture = _capture,
            Options = _settingsService.Current.CodexImportOptions
        });
    }

    private void OnOpenFile()
    {
        Process.Start(new ProcessStartInfo(_capture.ImagePath) { UseShellExecute = true });
    }

    private void CloseWithReview()
    {
        SafeCloseReviewOverlay();
        Close();
    }

    private void SafeCloseReviewOverlay()
    {
        try
        {
            _closeReviewOverlay();
        }
        catch (InvalidOperationException)
        {
            // Window is already closed.
        }
    }

    private void AnimateIn()
    {
        var opacityAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160));
        var translateAnim = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(160));
        BeginAnimation(OpacityProperty, opacityAnim);
        var renderTransform = new TranslateTransform(0, 0);
        RenderTransform = renderTransform;
        renderTransform.BeginAnimation(TranslateTransform.YProperty, translateAnim);
    }

    private static Geometry CopyIcon() => Geometry.Parse("M7,7 L5,7 C3.9,7 3,7.9 3,9 L3,17 C3,18.1 3.9,19 5,19 L13,19 C14.1,19 15,18.1 15,17 L15,15 M9,3 L17,3 C18.1,3 19,3.9 19,5 L19,13 C19,14.1 18.1,15 17,15 L9,15 C7.9,15 7,14.1 7,13 L7,5 C7,3.9 7.9,3 9,3 Z");

    private static Geometry TranslateIcon() => Geometry.Parse("M4,5 L11,5 M7.5,5 L7.5,15 M5,15 C6.8,12.5 9.2,12.5 11,15 M14,9 L20,9 M17,9 L17,19 M14,19 L20,19");

    private static Geometry QuoteIcon() => Geometry.Parse("M7,7 C5.4,8.1 4.5,9.5 4.5,11.2 C4.5,12.8 5.5,14 7,14 C8.2,14 9,13.2 9,12 C9,10.8 8.1,10 7,10 C6.8,10 6.6,10 6.4,10.1 M16,7 C14.4,8.1 13.5,9.5 13.5,11.2 C13.5,12.8 14.5,14 16,14 C17.2,14 18,13.2 18,12 C18,10.8 17.1,10 16,10 C15.8,10 15.6,10 15.4,10.1");

    private static Geometry OpenIcon() => Geometry.Parse("M4,6 L10,6 L12,8 L20,8 L20,18 L4,18 Z M8,12 L16,12");

    private static Geometry CloseIcon() => Geometry.Parse("M6,6 L18,18 M18,6 L6,18");
}
