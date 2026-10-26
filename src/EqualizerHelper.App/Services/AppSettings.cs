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
}
