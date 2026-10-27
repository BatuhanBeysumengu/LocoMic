using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Threading;

namespace EqualizerHelper.App.Services;

/// <summary>
/// System-wide hotkeys through low-level keyboard and mouse hooks, the way
/// Discord keybinds work. Unlike RegisterHotKey this keeps working in games
/// that block other apps' hotkeys while they're in front, supports the side
/// mouse buttons, and never swallows input: the game or app in front still
/// receives every key and click. Hook callbacks only match and queue work on
/// the UI dispatcher, so they return immediately (Windows removes hooks that
/// respond too slowly).
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int WmXButtonDown = 0x020B;
    private const int XButton1 = 0x0001;

    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public int VkCode;
        public int ScanCode;
        public int Flags;
        public int Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsLlHookStruct
    {
        public int X;
        public int Y;
        public int MouseData;
        public int Flags;
        public int Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    private readonly HotkeyMatcher _matcher = new();
    private readonly Dispatcher _dispatcher;

    // Delegates must stay referenced while the hooks exist, or the GC frees them.
    private readonly HookProc _keyboardProc;
    private readonly HookProc _mouseProc;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;

    /// <summary>Installs the hooks; call on a thread with a message loop (the UI thread).</summary>
    public GlobalHotkeyService()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _keyboardProc = OnKeyboard;
        _mouseProc = OnMouse;

        IntPtr module = GetModuleHandle(Process.GetCurrentProcess().MainModule?.ModuleName);
        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, module, 0);
        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProc, module, 0);
    }

    /// <summary>True if Windows accepted the hooks.</summary>
    public bool IsActive => _keyboardHook != IntPtr.Zero && _mouseHook != IntPtr.Zero;

    /// <summary>
    /// Adds a hotkey. Always succeeds while the hooks are active: hotkeys are
    /// shared with other apps (both receive the key), so nothing can be "taken".
    /// </summary>
    public bool Register(HotkeyGesture gesture, Action callback)
    {
        _matcher.Add(gesture, callback);
        return IsActive;
    }

    public void UnregisterAll() => _matcher.Clear();

    private static ModifierKeys CurrentModifiers()
    {
        static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        var modifiers = ModifierKeys.None;
        if (Down(VkControl)) modifiers |= ModifierKeys.Control;
        if (Down(VkMenu)) modifiers |= ModifierKeys.Alt;
        if (Down(VkShift)) modifiers |= ModifierKeys.Shift;
        if (Down(VkLWin) || Down(VkRWin)) modifiers |= ModifierKeys.Windows;
        return modifiers;
    }

    private IntPtr OnKeyboard(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int message = wParam.ToInt32();
            var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            if (message is WmKeyDown or WmSysKeyDown)
            {
                Run(_matcher.OnKeyDown(data.VkCode, CurrentModifiers()));
            }
            else if (message is WmKeyUp or WmSysKeyUp)
            {
                _matcher.OnKeyUp(data.VkCode);
            }
        }

        // Always pass the key on, so the game/app in front still gets it.
        return CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && wParam.ToInt32() == WmXButtonDown)
        {
            var data = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
            var button = ((data.MouseData >> 16) & 0xFFFF) == XButton1 ? MouseButton.XButton1 : MouseButton.XButton2;
            Run(_matcher.OnMouseDown(button, CurrentModifiers()));
        }

        return CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private void Run(System.Collections.Generic.IReadOnlyList<Action> callbacks)
    {
        foreach (var callback in callbacks)
        {
            _dispatcher.BeginInvoke(callback);
        }
    }

    public void Dispose()
    {
        _matcher.Clear();
        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
    }
}
