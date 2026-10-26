using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace EqualizerHelper.App.Services;

public enum HotkeyAction
{
    ToggleBypass,
    ToggleMute,
    NextPreset,
    PreviousPreset,
}

/// <summary>
/// A key plus modifiers, stored as text like "Ctrl+Alt+B" in settings.json.
/// </summary>
public record HotkeyGesture(ModifierKeys Modifiers, Key Key)
{
    public static IReadOnlyDictionary<HotkeyAction, HotkeyGesture> Defaults { get; } = new Dictionary<HotkeyAction, HotkeyGesture>
    {
        [HotkeyAction.ToggleBypass] = new(ModifierKeys.Control | ModifierKeys.Alt, Key.B),
        [HotkeyAction.ToggleMute] = new(ModifierKeys.Control | ModifierKeys.Alt, Key.M),
        [HotkeyAction.NextPreset] = new(ModifierKeys.Control | ModifierKeys.Alt, Key.Right),
        [HotkeyAction.PreviousPreset] = new(ModifierKeys.Control | ModifierKeys.Alt, Key.Left),
    };

    /// <summary>Modifiers used with 1–9 to jump straight to a preset by its position in the list.</summary>
    public static ModifierKeys PresetNumberModifiers => ModifierKeys.Control | ModifierKeys.Alt;

    public static string DisplayName(HotkeyAction action) => action switch
    {
        HotkeyAction.ToggleBypass => "Toggle Bypass",
        HotkeyAction.ToggleMute => "Toggle Mute",
        HotkeyAction.NextPreset => "Next preset",
        HotkeyAction.PreviousPreset => "Previous preset",
        _ => action.ToString(),
    };

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join("+", parts);
    }

    public static HotkeyGesture? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var modifiers = ModifierKeys.None;
        Key? key = null;
        foreach (string part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": modifiers |= ModifierKeys.Control; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                case "win": modifiers |= ModifierKeys.Windows; break;
                default:
                    key = ParseKeyName(part);
                    if (key == null)
                    {
                        return null;
                    }

                    break;
            }
        }

        return key == null || modifiers == ModifierKeys.None ? null : new HotkeyGesture(modifiers, key.Value);
    }

    private static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        _ => key.ToString(),
    };

    private static Key? ParseKeyName(string name)
    {
        if (name.Length == 1 && char.IsDigit(name[0]))
        {
            return Key.D0 + (name[0] - '0');
        }

        return Enum.TryParse(name, ignoreCase: true, out Key key) ? key : null;
    }

    /// <summary>
    /// Builds the effective hotkey map: defaults, overridden by whatever the
    /// user saved (an empty string means "no hotkey" for that action).
    /// </summary>
    public static Dictionary<HotkeyAction, HotkeyGesture?> FromSettings(Dictionary<string, string>? saved)
    {
        var result = Defaults.ToDictionary(kv => kv.Key, kv => (HotkeyGesture?)kv.Value);
        if (saved == null)
        {
            return result;
        }

        foreach (var (actionName, gestureText) in saved)
        {
            if (Enum.TryParse(actionName, out HotkeyAction action))
            {
                result[action] = TryParse(gestureText);
            }
        }

        return result;
    }

    public static Dictionary<string, string> ToSettings(IReadOnlyDictionary<HotkeyAction, HotkeyGesture?> hotkeys) =>
        hotkeys.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value?.ToString() ?? string.Empty);
}
