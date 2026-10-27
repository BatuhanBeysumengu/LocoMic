using System.Windows.Input;
using EqualizerHelper.App.Services;

namespace EqualizerHelper.Dsp.Tests;

public class HotkeyMatcherTests
{
    private const int VkNumPad1 = 0x61;
    private const int VkB = 0x42;

    [Fact]
    public void KeyDown_WithExactModifiers_Fires()
    {
        var matcher = new HotkeyMatcher();
        int fired = 0;
        matcher.Add(new HotkeyGesture(ModifierKeys.Control | ModifierKeys.Alt, Key.B), () => fired++);

        foreach (var callback in matcher.OnKeyDown(VkB, ModifierKeys.Control | ModifierKeys.Alt)) callback();

        Assert.Equal(1, fired);
    }

    [Fact]
    public void Modifiers_MustMatchExactly()
    {
        var matcher = new HotkeyMatcher();
        int plain = 0, ctrl = 0;
        matcher.Add(new HotkeyGesture(ModifierKeys.None, Key.NumPad1), () => plain++);
        matcher.Add(new HotkeyGesture(ModifierKeys.Control, Key.NumPad1), () => ctrl++);

        foreach (var c in matcher.OnKeyDown(VkNumPad1, ModifierKeys.Control)) c();
        matcher.OnKeyUp(VkNumPad1);
        Assert.Equal((0, 1), (plain, ctrl));

        foreach (var c in matcher.OnKeyDown(VkNumPad1, ModifierKeys.None)) c();
        Assert.Equal((1, 1), (plain, ctrl));
    }

    [Fact]
    public void HoldingAKey_FiresOnlyOnce_UntilReleased()
    {
        var matcher = new HotkeyMatcher();
        int fired = 0;
        matcher.Add(new HotkeyGesture(ModifierKeys.None, Key.NumPad1), () => fired++);

        // Windows repeats key-down while a key is held.
        for (int i = 0; i < 5; i++)
        {
            foreach (var c in matcher.OnKeyDown(VkNumPad1, ModifierKeys.None)) c();
        }

        Assert.Equal(1, fired);

        matcher.OnKeyUp(VkNumPad1);
        foreach (var c in matcher.OnKeyDown(VkNumPad1, ModifierKeys.None)) c();
        Assert.Equal(2, fired);
    }

    [Fact]
    public void MouseButtons_MatchButtonAndModifiers()
    {
        var matcher = new HotkeyMatcher();
        int mouse4 = 0, ctrlMouse5 = 0;
        matcher.Add(HotkeyGesture.ForMouse(ModifierKeys.None, MouseButton.XButton1), () => mouse4++);
        matcher.Add(HotkeyGesture.ForMouse(ModifierKeys.Control, MouseButton.XButton2), () => ctrlMouse5++);

        foreach (var c in matcher.OnMouseDown(MouseButton.XButton1, ModifierKeys.None)) c();
        foreach (var c in matcher.OnMouseDown(MouseButton.XButton2, ModifierKeys.None)) c();
        foreach (var c in matcher.OnMouseDown(MouseButton.XButton2, ModifierKeys.Control)) c();

        Assert.Equal((1, 1), (mouse4, ctrlMouse5));
    }

    [Fact]
    public void KeyGestures_IgnoreMouseEvents_AndViceVersa()
    {
        var matcher = new HotkeyMatcher();
        int fired = 0;
        matcher.Add(new HotkeyGesture(ModifierKeys.None, Key.NumPad1), () => fired++);
        matcher.Add(HotkeyGesture.ForMouse(ModifierKeys.None, MouseButton.XButton1), () => fired++);

        Assert.Empty(matcher.OnMouseDown(MouseButton.XButton2, ModifierKeys.None));
        Assert.Single(matcher.OnKeyDown(VkNumPad1, ModifierKeys.None));
        Assert.Single(matcher.OnMouseDown(MouseButton.XButton1, ModifierKeys.None));
    }

    [Fact]
    public void Clear_RemovesAllHotkeys()
    {
        var matcher = new HotkeyMatcher();
        matcher.Add(new HotkeyGesture(ModifierKeys.None, Key.NumPad1), () => { });
        matcher.Clear();

        Assert.Empty(matcher.OnKeyDown(VkNumPad1, ModifierKeys.None));
    }

    [Theory]
    [InlineData("Mouse4", ModifierKeys.None, MouseButton.XButton1)]
    [InlineData("Mouse5", ModifierKeys.None, MouseButton.XButton2)]
    [InlineData("Ctrl+Mouse4", ModifierKeys.Control, MouseButton.XButton1)]
    public void MouseGestures_RoundTripThroughSettings(string text, ModifierKeys modifiers, MouseButton button)
    {
        var gesture = HotkeyGesture.TryParse(text);

        Assert.Equal(HotkeyGesture.ForMouse(modifiers, button), gesture);
        Assert.Equal(text, gesture!.ToString());
    }

    [Fact]
    public void OnlySideMouseButtons_AreAllowed()
    {
        Assert.True(HotkeyGesture.IsAllowedMouseButton(MouseButton.XButton1));
        Assert.True(HotkeyGesture.IsAllowedMouseButton(MouseButton.XButton2));
        Assert.False(HotkeyGesture.IsAllowedMouseButton(MouseButton.Left));
        Assert.False(HotkeyGesture.IsAllowedMouseButton(MouseButton.Right));
        Assert.False(HotkeyGesture.IsAllowedMouseButton(MouseButton.Middle));
    }
}
