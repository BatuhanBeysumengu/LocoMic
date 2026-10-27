using System;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace EqualizerHelper.App.Services;

/// <summary>
/// Persists the user's settings (devices, sounds, hotkeys, theme...) so the
/// app remembers them across restarts. Saves are atomic: the new settings are
/// written to a temporary file which then replaces the old one, so a crash or
/// force-close mid-save can never leave a half-written or stale file.
/// </summary>
public static class AppSettingsStore
{
    private static readonly string SettingsPath = Path.Combine(AppPaths.DataDirectory, "settings.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load() => Load(SettingsPath);

    public static void Save(AppSettings settings) => Save(settings, SettingsPath);

    internal static AppSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }

    internal static void Save(AppSettings settings, string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (directory != null)
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(settings, JsonOptions);
        string temp = path + ".tmp";

        // Another program (antivirus, backup, an editor) can hold the file for
        // a moment; retry briefly rather than losing the change.
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.WriteAllText(temp, json);
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(50 * attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < 5)
            {
                Thread.Sleep(50 * attempt);
            }
        }
    }
}
