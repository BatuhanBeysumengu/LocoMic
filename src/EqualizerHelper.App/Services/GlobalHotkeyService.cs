using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace EqualizerHelper.App.Services;

/// <summary>
/// System-wide hotkeys via Win32 RegisterHotKey, delivered as WM_HOTKEY to
/// the main window's message loop — so they keep working while the window is
/// hidden in the tray and while another app (a game, Discord) has focus.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _callbacks = new();
    private int _nextId = 1;

    public GlobalHotkeyService(IntPtr windowHandle)
    {
        _source = HwndSource.FromHwnd(windowHandle)
            ?? throw new InvalidOperationException("The window has no HwndSource yet.");
        _source.AddHook(WndProc);
    }

    /// <summary>
    /// Registers a hotkey. Returns false if Windows refused it, which almost
    /// always means another app already owns that combination.
    /// </summary>
    public bool Register(HotkeyGesture gesture, Action callback)
    {
        int id = _nextId++;
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(gesture.Key);
        if (!RegisterHotKey(_source.Handle, id, ToNativeModifiers(gesture.Modifiers) | ModNoRepeat, vk))
        {
            return false;
        }

        _callbacks[id] = callback;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (int id in _callbacks.Keys)
        {
            UnregisterHotKey(_source.Handle, id);
        }

        _callbacks.Clear();
    }

    private static uint ToNativeModifiers(ModifierKeys modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt)) result |= ModAlt;
        if (modifiers.HasFlag(ModifierKeys.Control)) result |= ModControl;
        if (modifiers.HasFlag(ModifierKeys.Shift)) result |= ModShift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) result |= ModWin;
        return result;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _callbacks.TryGetValue(wParam.ToInt32(), out var callback))
        {
            callback();
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
    }
}
