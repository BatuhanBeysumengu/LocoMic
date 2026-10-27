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
    Sound1,
    Sound2,
    Sound3,
    Sound4,
    Sound5,
    Sound6,
    Sound7,
    Sound8,
    Sound9,
    StopAllSounds,
}

/// <summary>
/// A key (or a side mouse button) plus modifiers, stored as text like
/// "Ctrl+Alt+B" or "Mouse4" in settings.json. When <see cref="Mouse"/> is set
/// the gesture is a mouse button and <see cref="Key"/> is Key.None.
/// </summary>
public record HotkeyGesture(ModifierKeys Modifiers, Key Key, MouseButton? Mouse = null)
{
    /// <summary>A side mouse button (Mouse4 = XButton1, Mouse5 = XButton2), with optional modifiers.</summary>
    public static HotkeyGesture ForMouse(ModifierKeys modifiers, MouseButton button) => new(modifiers, Key.None, button);

    /// <summary>Only the side buttons can be hotkeys; left/right/middle click are needed for normal use.</summary>
    public static bool IsAllowedMouseButton(MouseButton button) => button is MouseButton.XButton1 or MouseButton.XButton2;

    /// <summary>The Windows virtual-key code of the key (0 for mouse gestures).</summary>
    public int VirtualKey => Mouse == null ? KeyInterop.VirtualKeyFromKey(Key) : 0;

    /// <summary>True if a key press with exactly these modifiers should trigger this hotkey.</summary>
    public bool MatchesKey(int virtualKey, ModifierKeys modifiers) =>
        Mouse == null && Modifiers == modifiers && VirtualKey == virtualKey;

    /// <summary>True if a mouse button press with exactly these modifiers should trigger this hotkey.</summary>
    public bool MatchesMouse(MouseButton button, ModifierKeys modifiers) =>
        Mouse == button && Modifiers == modifiers;

    public static IReadOnlyDictionary<HotkeyAction, HotkeyGesture> Defaults { get; } = new Dictionary<HotkeyAction, HotkeyGesture>
    {
        [HotkeyAction.ToggleBypass] = new(ModifierKeys.Control | ModifierKeys.Alt, Key.B),
        [HotkeyAction.ToggleMute] = new(ModifierKeys.Control | ModifierKeys.Alt, Key.M),
        [HotkeyAction.NextPreset] = new(ModifierKeys.Control | ModifierKeys.Alt, Key.Right),
        [HotkeyAction.PreviousPreset] = new(ModifierKeys.Control | ModifierKeys.Alt, Key.Left),
        [HotkeyAction.Sound1] = new(ModifierKeys.None, Key.NumPad1),
        [HotkeyAction.Sound2] = new(ModifierKeys.None, Key.NumPad2),
        [HotkeyAction.Sound3] = new(ModifierKeys.None, Key.NumPad3),
        [HotkeyAction.Sound4] = new(ModifierKeys.None, Key.NumPad4),
        [HotkeyAction.Sound5] = new(ModifierKeys.None, Key.NumPad5),
        [HotkeyAction.Sound6] = new(ModifierKeys.None, Key.NumPad6),
        [HotkeyAction.Sound7] = new(ModifierKeys.None, Key.NumPad7),
        [HotkeyAction.Sound8] = new(ModifierKeys.None, Key.NumPad8),
        [HotkeyAction.Sound9] = new(ModifierKeys.None, Key.NumPad9),
        [HotkeyAction.StopAllSounds] = new(ModifierKeys.None, Key.NumPad0),
    };

    /// <summary>
    /// Soundboard hotkeys are only registered while the engine is running, so
    /// plain numpad keys don't stop the numpad typing in other apps the rest
    /// of the time.
    /// </summary>
    public static bool IsSoundAction(HotkeyAction action) => action >= HotkeyAction.Sound1;

    /// <summary>1-based sound number for Sound1..Sound9, otherwise null.</summary>
    public static int? SoundNumber(HotkeyAction action) =>
        action is >= HotkeyAction.Sound1 and <= HotkeyAction.Sound9 ? action - HotkeyAction.Sound1 + 1 : null;

    /// <summary>
    /// Keys that may be used without Ctrl/Alt/Shift/Win: the numpad and F13–F24,
    /// which aren't used for normal typing in most apps.
    /// </summary>
    public static bool IsAllowedWithoutModifiers(Key key) =>
        key is >= Key.NumPad0 and <= Key.Divide or >= Key.F13 and <= Key.F24;

    /// <summary>Modifiers used with 1–9 to jump straight to a preset by its position in the list.</summary>
    public static ModifierKeys PresetNumberModifiers => ModifierKeys.Control | ModifierKeys.Alt;

    public static string DisplayName(HotkeyAction action) => action switch
    {
        HotkeyAction.ToggleBypass => "Toggle Bypass",
        HotkeyAction.ToggleMute => "Toggle Mute",
        HotkeyAction.NextPreset => "Next preset",
        HotkeyAction.PreviousPreset => "Previous preset",
        HotkeyAction.StopAllSounds => "Stop all sounds",
        _ when SoundNumber(action) is int n => $"Play/stop sound {n}",
        _ => action.ToString(),
    };

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(Mouse is MouseButton button ? MouseName(button) : KeyName(Key));
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
        MouseButton? mouse = null;
        foreach (string part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": modifiers |= ModifierKeys.Control; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                case "win": modifiers |= ModifierKeys.Windows; break;
                case "mouse4": mouse = MouseButton.XButton1; break;
                case "mouse5": mouse = MouseButton.XButton2; break;
                default:
                    key = ParseKeyName(part);
                    if (key == null)
                    {
                        return null;
                    }

                    break;
            }
        }

        if (mouse is MouseButton button)
        {
            return key == null ? ForMouse(modifiers, button) : null;
        }

        if (key == null || (modifiers == ModifierKeys.None && !IsAllowedWithoutModifiers(key.Value)))
        {
            return null;
        }

        return new HotkeyGesture(modifiers, key.Value);
    }

    private static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num" + (int)(key - Key.NumPad0),
        _ => key.ToString(),
    };

    private static string MouseName(MouseButton button) => button == MouseButton.XButton1 ? "Mouse4" : "Mouse5";

    private static Key? ParseKeyName(string name)
    {
        if (name.Length == 1 && char.IsDigit(name[0]))
        {
            return Key.D0 + (name[0] - '0');
        }

        if (name.Length == 4 && name.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && char.IsDigit(name[3]))
        {
            return Key.NumPad0 + (name[3] - '0');
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
