using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CodexCapture.Models;
using CodexCapture.Services;

namespace CodexCapture.Windows;

public sealed class TranslationWindow : Window
{
    private readonly CaptureResult _capture;
    private readonly CodexImportService _codexImportService;
    private readonly SettingsService _settingsService;

    public TranslationWindow(
        CaptureResult capture,
        TranslationResult result,
        CodexImportService codexImportService,
        SettingsService settingsService)
    {
        _capture = capture;
        _codexImportService = codexImportService;
        _settingsService = settingsService;
        Title = "\u7ffb\u8bd1\u7ed3\u679c";
        Width = 540;
        Height = 440;
        MinWidth = 360;
        MinHeight = 280;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Opacity = 0;

        Content = BuildContent(result);
        Loaded += (_, _) =>
        {
            var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            BeginAnimation(OpacityProperty, anim);
        };
    }

    private UIElement BuildContent(TranslationResult result)
    {
        var grid = new Grid { Margin = new Thickness(18) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new TextBlock
        {
            Text = $"\u7ffb\u8bd1\u7ed3\u679c  -  {_capture.WidthPx} x {_capture.HeightPx} px",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42))
        };
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        var text = new TextBox
        {
            Text = result.IsSuccess ? result.Text : result.Error,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            IsReadOnly = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 12),
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            Padding = new Thickness(12),
            FontSize = 13
        };
        Grid.SetRow(text, 1);
        grid.Children.Add(text);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetRow(buttons, 2);
        grid.Children.Add(buttons);

        var copy = new Button
        {
            Content = "\u590d\u5236\u6587\u672c",
            MinWidth = 90,
            Margin = new Thickness(0, 0, 8, 0)
        };
        copy.Click += (_, _) => Clipboard.SetText(result.Text ?? result.Error ?? string.Empty);

        var ask = new Button
        {
            Content = "\u5bfc\u5165 Codex",
            MinWidth = 90,
            Background = new SolidColorBrush(Color.FromRgb(15, 118, 110)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(15, 118, 110))
        };
        ask.Click += async (_, _) => await _codexImportService.ImportAsync(new CodexImportRequest
        {
            Capture = _capture,
            Options = _settingsService.Current.CodexImportOptions
        });

        buttons.Children.Add(copy);
        buttons.Children.Add(ask);

        return grid;
    }
}
