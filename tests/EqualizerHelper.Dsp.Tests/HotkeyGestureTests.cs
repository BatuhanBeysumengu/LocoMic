using System.Collections.Generic;
using System.Windows.Input;
using EqualizerHelper.App.Services;

namespace EqualizerHelper.Dsp.Tests;

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Alt+B", ModifierKeys.Control | ModifierKeys.Alt, Key.B)]
    [InlineData("Num1", ModifierKeys.None, Key.NumPad1)]
    [InlineData("Ctrl+Num0", ModifierKeys.Control, Key.NumPad0)]
    [InlineData("Ctrl+Alt+3", ModifierKeys.Control | ModifierKeys.Alt, Key.D3)]
    public void TryParse_ThenToString_RoundTrips(string text, ModifierKeys modifiers, Key key)
    {
        var gesture = HotkeyGesture.TryParse(text);

        Assert.Equal(new HotkeyGesture(modifiers, key), gesture);
        Assert.Equal(text, gesture!.ToString());
    }

    [Theory]
    [InlineData("B")]
    [InlineData("Space")]
    [InlineData("")]
    [InlineData("Ctrl+NotAKey")]
    public void TryParse_RejectsPlainTypingKeysAndGarbage(string text)
    {
        Assert.Null(HotkeyGesture.TryParse(text));
    }

    [Fact]
    public void Defaults_MapNumpadToSounds()
    {
        for (int n = 1; n <= 9; n++)
        {
            var action = HotkeyAction.Sound1 + (n - 1);
            Assert.Equal(new HotkeyGesture(ModifierKeys.None, Key.NumPad0 + n), HotkeyGesture.Defaults[action]);
            Assert.Equal(n, HotkeyGesture.SoundNumber(action));
            Assert.True(HotkeyGesture.IsSoundAction(action));
        }

        Assert.Equal(new HotkeyGesture(ModifierKeys.None, Key.NumPad0), HotkeyGesture.Defaults[HotkeyAction.StopAllSounds]);
        Assert.True(HotkeyGesture.IsSoundAction(HotkeyAction.StopAllSounds));
        Assert.False(HotkeyGesture.IsSoundAction(HotkeyAction.ToggleMute));
    }

    [Fact]
    public void FromSettings_OlderSettingsWithoutSoundKeys_GetsDefaults()
    {
        var saved = new Dictionary<string, string> { ["ToggleMute"] = "Ctrl+Shift+M" };

        var hotkeys = HotkeyGesture.FromSettings(saved);

        Assert.Equal(HotkeyGesture.TryParse("Ctrl+Shift+M"), hotkeys[HotkeyAction.ToggleMute]);
        Assert.Equal(HotkeyGesture.Defaults[HotkeyAction.Sound5], hotkeys[HotkeyAction.Sound5]);
    }

    [Fact]
    public void ToSettings_ThenFromSettings_KeepsClearedHotkeysCleared()
    {
        var hotkeys = HotkeyGesture.FromSettings(null);
        hotkeys[HotkeyAction.Sound3] = null;

        var reloaded = HotkeyGesture.FromSettings(HotkeyGesture.ToSettings(hotkeys));

        Assert.Null(reloaded[HotkeyAction.Sound3]);
        Assert.Equal(hotkeys[HotkeyAction.Sound4], reloaded[HotkeyAction.Sound4]);
    }
}
