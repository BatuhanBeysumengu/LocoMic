using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using EqualizerHelper.Dsp;

namespace EqualizerHelper.App.Presets;

/// <summary>
/// Reads and writes shareable ".eqpreset" files: indented JSON wrapping one
/// or more presets in a small envelope, so random JSON files are rejected
/// and the format can evolve later. Imported values are clamped to the
/// ranges the UI allows, and band frequencies/filter types always come from
/// <see cref="DefaultBands"/>, so a hand-edited or malicious file can't push
/// the DSP into unsafe settings.
/// </summary>
public static class PresetFile
{
    public const string Extension = ".eqpreset";
    public const string DialogFilter = "Equalizer Helper preset (*.eqpreset)|*.eqpreset|JSON files (*.json)|*.json";

    private const string FormatName = "EqualizerHelper.Preset";
    private const int CurrentVersion = 1;
    private const long MaxFileBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private class Envelope
    {
        public string? Format { get; set; }
        public int Version { get; set; }
        public List<EqPreset>? Presets { get; set; }
    }

    public static string Serialize(IEnumerable<EqPreset> presets) =>
        JsonSerializer.Serialize(new Envelope { Format = FormatName, Version = CurrentVersion, Presets = presets.ToList() }, JsonOptions);

    public static void Write(string path, EqPreset preset) => File.WriteAllText(path, Serialize(new[] { preset }));

    /// <exception cref="InvalidDataException">The file isn't a valid preset file.</exception>
    public static List<EqPreset> Read(string path)
    {
        if (new FileInfo(path).Length > MaxFileBytes)
        {
            throw new InvalidDataException("The file is too large to be a preset.");
        }

        return Deserialize(File.ReadAllText(path));
    }

    /// <exception cref="InvalidDataException">The text isn't a valid preset file.</exception>
    public static List<EqPreset> Deserialize(string json)
    {
        Envelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The file isn't valid preset JSON.", ex);
        }

        if (envelope?.Format != FormatName)
        {
            throw new InvalidDataException("This isn't an Equalizer Helper preset file.");
        }

        if (envelope.Version > CurrentVersion)
        {
            throw new InvalidDataException("This preset was made with a newer version of Equalizer Helper.");
        }

        if (envelope.Presets == null || envelope.Presets.Count == 0)
        {
            throw new InvalidDataException("The file doesn't contain any presets.");
        }

        return envelope.Presets.Select(Sanitize).ToList();
    }

    private static EqPreset Sanitize(EqPreset preset)
    {
        string name = (preset.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new InvalidDataException("A preset in the file has no name.");
        }

        if (name.Length > 60)
        {
            name = name[..60];
        }

        var roles = DefaultBands.Roles;
        if (preset.Bands == null || preset.Bands.Count != roles.Length)
        {
            throw new InvalidDataException($"\"{name}\" doesn't have the expected {roles.Length} EQ bands.");
        }

        var bands = roles.Select((role, i) => new EqBandSettings
        {
            FrequencyHz = role.FrequencyHz,
            FilterType = role.FilterType,
            GainDb = Clamp(preset.Bands[i].GainDb, -15, 15, 0),
            Q = Clamp(preset.Bands[i].Q, 0.1, 10, role.Q),
        }).ToList();

        return new EqPreset(name, bands)
        {
            PitchSemitones = Clamp(preset.PitchSemitones, -12, 12, 0),
            MicCrunch = Clamp(preset.MicCrunch, 0, 1, 0),
            InputGainDb = Clamp(preset.InputGainDb, -12, 24, 0),
            Gate = Clamp(preset.Gate, 0, 1, 0),
            Loudness = Clamp(preset.Loudness, 0, 1, 0),
            NoiseRemoval = preset.NoiseRemoval,
            NoiseRemovalStrength = Clamp(preset.NoiseRemovalStrength, 0, 1, 1),
            VoiceEffect = Enum.IsDefined(preset.VoiceEffect) ? preset.VoiceEffect : VoiceEffectType.None,
        };
    }

    private static double Clamp(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    /// <summary>"Name", or "Name (2)", "Name (3)"… if that's already taken.</summary>
    public static string MakeUniqueName(string name, IEnumerable<string> existingNames)
    {
        var taken = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(name))
        {
            return name;
        }

        for (int n = 2; ; n++)
        {
            string candidate = $"{name} ({n})";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>A file name made safe for Windows from the preset name.</summary>
    public static string SuggestedFileName(string presetName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string safe = new string(presetName.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return (safe.Length == 0 ? "preset" : safe) + Extension;
    }
}
