using System;
using System.IO;
using System.Text.Json;

namespace EqualizerHelper.App.Services;

/// <summary>
/// Persists the user's last device selections so the app remembers them
/// across restarts instead of recomputing a "smart" default every launch.
/// </summary>
public static class AppSettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EqualizerHelper",
        "settings.json");

    public static AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }

        string json = File.ReadAllText(SettingsPath);
        return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        string? directory = Path.GetDirectoryName(SettingsPath);
        if (directory != null)
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
