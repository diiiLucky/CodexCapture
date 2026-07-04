namespace CodexCapture.Models;

internal static class AppSettingsSnapshot
{
    public static AppSettings Clone(AppSettings settings)
    {
        var clone = new AppSettings
        {
            AutoStartEnabled = settings.AutoStartEnabled,
            GlobalHotkey = new HotkeySettings
            {
                Key = settings.GlobalHotkey.Key,
                Modifiers = settings.GlobalHotkey.Modifiers
            },
            FloatingWidgetState = new FloatingWidgetState
            {
                Left = settings.FloatingWidgetState.Left,
                Top = settings.FloatingWidgetState.Top,
                IsVisible = settings.FloatingWidgetState.IsVisible,
                AutoHideInFullscreen = settings.FloatingWidgetState.AutoHideInFullscreen
            },
            HistoryDirectory = settings.HistoryDirectory,
            DefaultTargetLanguage = settings.DefaultTargetLanguage,
            SelectedProviderId = settings.SelectedProviderId,
            CodexImportOptions = new CodexImportOptions
            {
                CodexExePath = settings.CodexImportOptions.CodexExePath,
                PreferredWorkspacePath = settings.CodexImportOptions.PreferredWorkspacePath,
                NewChatShortcut = settings.CodexImportOptions.NewChatShortcut,
                PasteRetryCount = settings.CodexImportOptions.PasteRetryCount,
                PasteDelayMs = settings.CodexImportOptions.PasteDelayMs,
                FallbackMode = settings.CodexImportOptions.FallbackMode,
                OptionalPromptTemplate = settings.CodexImportOptions.OptionalPromptTemplate
            }
        };

        foreach (var provider in settings.Providers)
        {
            clone.Providers.Add(new ProviderProfile
            {
                Id = provider.Id,
                DisplayName = provider.DisplayName,
                ProtocolMode = provider.ProtocolMode,
                ImagePayloadMode = provider.ImagePayloadMode,
                BaseUrl = provider.BaseUrl,
                Model = provider.Model,
                EncryptedApiKey = provider.EncryptedApiKey,
                TimeoutSeconds = provider.TimeoutSeconds,
                CustomHeaders = new Dictionary<string, string>(provider.CustomHeaders)
            });
        }

        return clone;
    }
}
