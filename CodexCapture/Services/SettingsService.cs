using System.IO;
using System.Text.Json;
using CodexCapture.Models;

namespace CodexCapture.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string SettingsPath { get; } =
        Path.Combine(SettingsDefaults.AppDataDirectory, "settings.json");

    public AppSettings Current { get; private set; } = AppSettings.CreateDefault();

    public async Task<AppSettings> LoadAsync()
    {
        Directory.CreateDirectory(SettingsDefaults.AppDataDirectory);
        Directory.CreateDirectory(SettingsDefaults.LocalDataDirectory);
        Directory.CreateDirectory(Current.HistoryDirectory);

        if (!File.Exists(SettingsPath))
        {
            Current = AppSettings.CreateDefault();
            await SaveAsync(Current);
            return Current;
        }

        await using var stream = File.OpenRead(SettingsPath);
        Current = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions)
                  ?? AppSettings.CreateDefault();

        if (Current.Providers.Count == 0)
        {
            var provider = ProviderProfile.CreateDefault();
            Current.Providers.Add(provider);
            Current.SelectedProviderId = provider.Id;
        }

        Directory.CreateDirectory(Current.HistoryDirectory);
        return Current;
    }

    public async Task SaveAsync(AppSettings settings)
    {
        Current = settings;
        Directory.CreateDirectory(SettingsDefaults.AppDataDirectory);
        Directory.CreateDirectory(SettingsDefaults.LocalDataDirectory);
        Directory.CreateDirectory(settings.HistoryDirectory);

        await using var stream = File.Create(SettingsPath);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions);
    }
}
