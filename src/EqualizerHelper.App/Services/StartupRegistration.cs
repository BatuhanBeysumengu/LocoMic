using System;
using Microsoft.Win32;

namespace EqualizerHelper.App.Services;

/// <summary>
/// Registers/unregisters this app to launch on Windows login via the
/// per-user Run key — no installer or elevation needed.
/// </summary>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "EqualizerHelper";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            string? value = key?.GetValue(ValueName) as string;
            string? exePath = Environment.ProcessPath;
            return value != null && exePath != null
                && string.Equals(value.Trim('"'), exePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

        if (enabled)
        {
            string? exePath = Environment.ProcessPath;
            if (exePath != null)
            {
                key.SetValue(ValueName, $"\"{exePath}\"");
            }
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
