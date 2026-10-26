using System.Collections.Generic;

namespace EqualizerHelper.App.Services;

public class AppSettings
{
    public string? CaptureDeviceId { get; set; }
    public string? RenderDeviceId { get; set; }
    public string? MonitorDeviceId { get; set; }
    public bool MonitorEnabled { get; set; } = true;
    public string? PresetName { get; set; }

    /// <summary>
    /// HotkeyAction name → gesture text (e.g. "Ctrl+Alt+B"); an empty string
    /// disables that hotkey. Missing actions fall back to the defaults.
    /// </summary>
    public Dictionary<string, string>? Hotkeys { get; set; }

    /// <summary>The soundboard list (files stay where they are on disk).</summary>
    public List<SavedSound>? Sounds { get; set; }

    public double SoundVolume { get; set; } = 0.6;
    public bool DuckSounds { get; set; } = true;
    public bool HearSounds { get; set; } = true;
}

public class SavedSound
{
    public string Path { get; set; } = string.Empty;
    public double Volume { get; set; } = 1.0;
    public bool Loop { get; set; }
}
