using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace EqualizerHelper.App.Presets;

public static class PresetStore
{
    private static readonly string UserPresetsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EqualizerHelper",
        "presets.json");

    public static List<EqPreset> GetBuiltInPresets()
    {
        return new List<EqPreset>
        {
            BuildPreset("Flat", 0, 0, 0, 0, 0, 0, 0, 0),
            BuildPreset("Voice Clarity", 0, 0, -3, -3, 0, 4, -2, 2),
            BuildPreset("De-Boom", 0, 0, -6, -4, 0, 0, 0, 0),
            BuildPreset("De-Harsh Sibilance", 0, 0, 0, 0, 0, 0, -6, 0),
            // Fun preset: heavy low-end, hard-cut highs, an actual pitch
            // drop, and mic crunch (distortion + compression) — comedic
            // "chaotic gaming voice chat" effect. EQ alone can't change
            // pitch or add that overdriven-mic character, so those two are
            // what actually sell the bit. -8 semitones is close to the -9
            // ratio already covered by PitchShifterTests, kept shy of it for
            // headroom; 0.6 crunch is noticeably distorted without becoming
            // unintelligible noise.
            BuildPreset("Deep Bass (Fun)", -8, 0.6, 10, 6, 2, -5, -8, -12),
        };
    }

    private static EqPreset BuildPreset(string name, double pitchSemitones, double micCrunch, params double[] gainsDb)
    {
        var bands = new List<EqBandSettings>();
        for (int i = 0; i < DefaultBands.Roles.Length; i++)
        {
            var role = DefaultBands.Roles[i];
            bands.Add(new EqBandSettings
            {
                FrequencyHz = role.FrequencyHz,
                Q = role.Q,
                FilterType = role.FilterType,
                GainDb = i < gainsDb.Length ? gainsDb[i] : 0,
            });
        }

        return new EqPreset(name, bands) { PitchSemitones = pitchSemitones, MicCrunch = micCrunch };
    }

    public static List<EqPreset> LoadUserPresets()
    {
        if (!File.Exists(UserPresetsPath))
        {
            return new List<EqPreset>();
        }

        string json = File.ReadAllText(UserPresetsPath);
        return JsonSerializer.Deserialize<List<EqPreset>>(json) ?? new List<EqPreset>();
    }

    public static void SaveUserPreset(EqPreset preset)
    {
        var presets = LoadUserPresets();
        presets.RemoveAll(p => p.Name == preset.Name);
        presets.Add(preset);

        string? directory = Path.GetDirectoryName(UserPresetsPath);
        if (directory != null)
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(UserPresetsPath, JsonSerializer.Serialize(presets, new JsonSerializerOptions { WriteIndented = true }));
    }
}
