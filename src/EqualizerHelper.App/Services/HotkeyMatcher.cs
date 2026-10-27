using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace EqualizerHelper.App.Services;

/// <summary>
/// Decides which hotkeys a raw key or mouse event triggers. Fires on the
/// initial press only (holding a key down repeats key-down events, which are
/// ignored until the key is released) and requires the modifiers to match
/// exactly, so Num1 doesn't fire a Ctrl+Num1 hotkey or the other way round.
/// Kept free of Windows hooks so it can be unit tested.
/// </summary>
public sealed class HotkeyMatcher
{
    private readonly List<(HotkeyGesture Gesture, Action Callback)> _registrations = new();
    private readonly HashSet<int> _keysDown = new();

    public void Add(HotkeyGesture gesture, Action callback) => _registrations.Add((gesture, callback));

    public void Clear() => _registrations.Clear();

    /// <summary>Callbacks to run for a key-down event (empty for auto-repeats).</summary>
    public IReadOnlyList<Action> OnKeyDown(int virtualKey, ModifierKeys modifiers)
    {
        if (!_keysDown.Add(virtualKey))
        {
            return Array.Empty<Action>();
        }

        var matches = new List<Action>();
        foreach (var (gesture, callback) in _registrations)
        {
            if (gesture.MatchesKey(virtualKey, modifiers))
            {
                matches.Add(callback);
            }
        }

        return matches;
    }

    public void OnKeyUp(int virtualKey) => _keysDown.Remove(virtualKey);

    /// <summary>Callbacks to run for a mouse button press.</summary>
    public IReadOnlyList<Action> OnMouseDown(MouseButton button, ModifierKeys modifiers)
    {
        var matches = new List<Action>();
        foreach (var (gesture, callback) in _registrations)
        {
            if (gesture.MatchesMouse(button, modifiers))
            {
                matches.Add(callback);
            }
        }

        return matches;
    }
}
