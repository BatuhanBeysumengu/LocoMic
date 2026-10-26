using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace EqualizerHelper.App.Services;

/// <summary>
/// Asks Windows 10/11 to draw a window's title bar and border dark, so dialogs
/// match the app's dark theme instead of showing a bright system frame.
/// </summary>
public static class DarkTitleBar
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaBorderColor = 34;
    private const int DarkPanelColorRef = 0x00201D1B; // COLORREF (0x00BBGGRR) for #1B1D20

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    public static void Apply(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        int darkMode = 1;
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));

        int borderColor = DarkPanelColorRef;
        DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref borderColor, sizeof(int));
    }

    /// <summary>Applies the dark title bar as soon as the window has a handle.</summary>
    public static void ApplyWhenReady(Window window) => window.SourceInitialized += (_, _) => Apply(window);
}
