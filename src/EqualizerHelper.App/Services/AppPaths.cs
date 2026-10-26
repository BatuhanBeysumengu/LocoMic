using System;
using System.IO;

namespace EqualizerHelper.App.Services;

/// <summary>
/// Where the app keeps its data (%APPDATA%\LocoMic), and the one-time move
/// of data saved under the app's old name (%APPDATA%\EqualizerHelper).
/// </summary>
public static class AppPaths
{
    private static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    public static string DataDirectory { get; } = Path.Combine(AppData, "LocoMic");

    private static string LegacyDataDirectory => Path.Combine(AppData, "EqualizerHelper");

    /// <summary>
    /// Copies presets, settings and the sound list saved by older versions
    /// (as "Equalizer Helper") into the new folder, if it doesn't exist yet.
    /// The old folder is left in place as a backup.
    /// </summary>
    public static void MigrateLegacyData()
    {
        if (Directory.Exists(DataDirectory) || !Directory.Exists(LegacyDataDirectory))
        {
            return;
        }

        Directory.CreateDirectory(DataDirectory);
        foreach (string file in Directory.GetFiles(LegacyDataDirectory, "*.json"))
        {
            File.Copy(file, Path.Combine(DataDirectory, Path.GetFileName(file)));
        }
    }
}
