using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CodexCapture.Models;
using CodexCapture.Services;
using Forms = System.Windows.Forms;

namespace CodexCapture.Windows;

public sealed class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly SecretService _secretService;
    private readonly AutoStartService _autoStartService;
    private readonly Action _restartHotkey;

    private readonly CheckBox _autoStartBox = new() { Content = "\u5f00\u673a\u81ea\u542f" };
    private readonly TextBox _historyDirectoryBox = new();
    private readonly TextBox _targetLanguageBox = new();
    private readonly TextBox _baseUrlBox = new();
    private readonly ComboBox _protocolBox = new();
    private readonly ComboBox _imagePayloadBox = new();
    private readonly TextBox _modelBox = new();
    private readonly PasswordBox _apiKeyBox = new();
    private readonly TextBox _codexPathBox = new();
    private readonly TextBox _workspacePathBox = new();
    private readonly TextBox _newChatShortcutBox = new();
    private readonly TextBlock _status = new() { Margin = new Thickness(0, 12, 0, 0), FontSize = 13 };

    public SettingsWindow(
        AppSettings settings,
        SettingsService settingsService,
        SecretService secretService,
        AutoStartService autoStartService,
        bool firstRun,
        Action restartHotkey)
    {
        _settings = settings;
        _settingsService = settingsService;
        _secretService = secretService;
        _autoStartService = autoStartService;
        _restartHotkey = restartHotkey;

        Title = "\u622a\u56fe\u5de5\u5177\u8bbe\u7f6e";
        Width = 620;
        Height = 760;
        MinWidth = 540;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Opacity = 0;

        Content = BuildContent();
        LoadValues();
        Loaded += (_, _) =>
        {
            var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            BeginAnimation(OpacityProperty, anim);
        };
    }

    private UIElement BuildContent()
    {
        var root = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new StackPanel { Margin = new Thickness(28, 20, 28, 20) }
        };

        var panel = (StackPanel)root.Content;
        panel.Children.Add(SectionHeader("\u57fa\u7840"));

        var basicSection = BeginSection(panel);
        basicSection.Children.Add(_autoStartBox);
        basicSection.Children.Add(Label("\u622a\u56fe\u5386\u53f2\u76ee\u5f55"));
        basicSection.Children.Add(Row(_historyDirectoryBox, BrowseFolderButton(_historyDirectoryBox)));
        basicSection.Children.Add(Label("\u9ed8\u8ba4\u7ffb\u8bd1\u76ee\u6807\u8bed\u8a00"));
        basicSection.Children.Add(_targetLanguageBox);
        basicSection.Children.Add(Label("\u6d6e\u52a8\u7ec4\u4ef6: \u70b9\u51fb\u622a\u56fe, \u62d6\u62fd\u79fb\u52a8"));
        basicSection.Children.Add(Hint("\u9f20\u6807\u60ac\u505c\u65f6\u6309\u94ae\u4f1a\u8f7b\u5fae\u653e\u5927"));

        panel.Children.Add(SectionHeader("AI \u4f9b\u5e94\u5546"));

        _protocolBox.ItemsSource = new[]
        {
            new OptionItem<AiProtocolMode>("Chat Completions", AiProtocolMode.ChatCompletions),
            new OptionItem<AiProtocolMode>("Responses", AiProtocolMode.Responses)
        };
        _protocolBox.DisplayMemberPath = nameof(OptionItem<AiProtocolMode>.Label);
        _imagePayloadBox.ItemsSource = new[]
        {
            new OptionItem<ImagePayloadMode>("Data URL (\u591a\u6570\u670d\u52a1\u5546)", ImagePayloadMode.DataUrl),
            new OptionItem<ImagePayloadMode>("\u7eaf Base64", ImagePayloadMode.RawBase64)
        };
        _imagePayloadBox.DisplayMemberPath = nameof(OptionItem<ImagePayloadMode>.Label);

        var aiSection = BeginSection(panel);
        aiSection.Children.Add(Label("\u534f\u8bae"));
        aiSection.Children.Add(_protocolBox);
        aiSection.Children.Add(Label("\u56fe\u7247\u4f20\u8f93\u683c\u5f0f"));
        aiSection.Children.Add(_imagePayloadBox);
        aiSection.Children.Add(Label("Base URL"));
        aiSection.Children.Add(_baseUrlBox);
        aiSection.Children.Add(Label("\u6a21\u578b"));
        aiSection.Children.Add(_modelBox);
        aiSection.Children.Add(Label("API Key"));
        aiSection.Children.Add(_apiKeyBox);

        panel.Children.Add(SectionHeader("\u5bfc\u5165 Codex"));

        var codexSection = BeginSection(panel);
        codexSection.Children.Add(Label("Codex \u7a0b\u5e8f\u8def\u5f84"));
        codexSection.Children.Add(Row(_codexPathBox, BrowseFileButton(_codexPathBox)));
        codexSection.Children.Add(Label("\u9ed8\u8ba4\u5de5\u4f5c\u76ee\u5f55"));
        codexSection.Children.Add(Row(_workspacePathBox, BrowseFolderButton(_workspacePathBox)));
        codexSection.Children.Add(Label("\u65b0\u5efa\u5bf9\u8bdd\u5feb\u6377\u952e"));
        codexSection.Children.Add(_newChatShortcutBox);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var save = new Button
        {
            Content = "\u4fdd\u5b58",
            MinWidth = 80,
            Background = new SolidColorBrush(Color.FromRgb(15, 118, 110)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(15, 118, 110))
        };
        save.Click += async (_, _) => await SaveAsync();
        var close = new Button { Content = "\u5173\u95ed", MinWidth = 80, Margin = new Thickness(8, 0, 0, 0) };
        close.Click += (_, _) => Close();
        actions.Children.Add(save);
        actions.Children.Add(close);
        panel.Children.Add(actions);
        panel.Children.Add(_status);
        return root;
    }

    private static StackPanel BeginSection(StackPanel parent)
    {
        var inner = new StackPanel();
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 16),
            Child = inner
        };
        parent.Children.Add(border);
        return inner;
    }

    private void LoadValues()
    {
        var provider = _settings.Providers.First();
        _autoStartBox.IsChecked = _settings.AutoStartEnabled;
        _historyDirectoryBox.Text = _settings.HistoryDirectory;
        _targetLanguageBox.Text = _settings.DefaultTargetLanguage;
        SelectOption(_protocolBox, provider.ProtocolMode);
        SelectOption(_imagePayloadBox, provider.ImagePayloadMode);
        _baseUrlBox.Text = provider.BaseUrl;
        _modelBox.Text = provider.Model;
        _codexPathBox.Text = _settings.CodexImportOptions.CodexExePath ?? string.Empty;
        _workspacePathBox.Text = _settings.CodexImportOptions.PreferredWorkspacePath;
        _newChatShortcutBox.Text = _settings.CodexImportOptions.NewChatShortcut;
    }

    private async Task SaveAsync()
    {
        var provider = _settings.Providers.First();
        _settings.AutoStartEnabled = _autoStartBox.IsChecked == true;
        _settings.HistoryDirectory = _historyDirectoryBox.Text.Trim();
        _settings.DefaultTargetLanguage = _targetLanguageBox.Text.Trim();
        provider.ProtocolMode = SelectedValue(_protocolBox, AiProtocolMode.ChatCompletions);
        provider.ImagePayloadMode = SelectedValue(_imagePayloadBox, ImagePayloadMode.DataUrl);
        provider.BaseUrl = _baseUrlBox.Text.Trim();
        provider.Model = _modelBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(_apiKeyBox.Password))
        {
            provider.EncryptedApiKey = _secretService.Protect(_apiKeyBox.Password);
        }

        _settings.CodexImportOptions.CodexExePath = string.IsNullOrWhiteSpace(_codexPathBox.Text)
            ? null
            : _codexPathBox.Text.Trim();
        _settings.CodexImportOptions.PreferredWorkspacePath = _workspacePathBox.Text.Trim();
        _settings.CodexImportOptions.NewChatShortcut = string.IsNullOrWhiteSpace(_newChatShortcutBox.Text)
            ? "^n"
            : _newChatShortcutBox.Text.Trim();

        _autoStartService.SetEnabled(_settings.AutoStartEnabled);
        await _settingsService.SaveAsync(_settings);
        _restartHotkey();
        _status.Text = "\u5df2\u4fdd\u5b58";
        _status.Foreground = new SolidColorBrush(Color.FromRgb(15, 118, 110));
    }

    private static TextBlock SectionHeader(string text) => new()
    {
        Text = text,
        FontSize = 18,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 8, 0, 10),
        Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42))
    };

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Margin = new Thickness(0, 8, 0, 2),
        FontSize = 13,
        Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105))
    };

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        Margin = new Thickness(0, 4, 0, 0),
        FontSize = 12,
        Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184))
    };

    private static UIElement Row(TextBox textBox, Button button)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(textBox, 0);
        Grid.SetColumn(button, 1);
        grid.Children.Add(textBox);
        grid.Children.Add(button);
        return grid;
    }

    private static Button BrowseFolderButton(TextBox target)
    {
        var button = new Button { Content = "\u6d4f\u89c8", MinWidth = 56 };
        button.Click += (_, _) =>
        {
            using var dialog = new Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == Forms.DialogResult.OK)
            {
                target.Text = dialog.SelectedPath;
            }
        };
        return button;
    }

    private static Button BrowseFileButton(TextBox target)
    {
        var button = new Button { Content = "\u6d4f\u89c8", MinWidth = 56 };
        button.Click += (_, _) =>
        {
            using var dialog = new Forms.OpenFileDialog { Filter = "Executable|*.exe|All files|*.*" };
            if (dialog.ShowDialog() == Forms.DialogResult.OK)
            {
                target.Text = dialog.FileName;
            }
        };
        return button;
    }

    private static void SelectOption<T>(ComboBox comboBox, T value)
    {
        foreach (var item in comboBox.Items.OfType<OptionItem<T>>())
        {
            if (EqualityComparer<T>.Default.Equals(item.Value, value))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
    }

    private static T SelectedValue<T>(ComboBox comboBox, T fallback) =>
        comboBox.SelectedItem is OptionItem<T> item ? item.Value : fallback;

    private sealed record OptionItem<T>(string Label, T Value);
}
