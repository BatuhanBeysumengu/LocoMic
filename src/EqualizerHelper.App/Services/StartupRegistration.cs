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

    /// <summary>
    /// If "start with Windows" points at an exe that no longer exists (the app
    /// was moved, renamed or updated to a new location), point it at this exe
    /// instead so startup keeps working. Entries for another existing copy are
    /// left alone.
    /// </summary>
    public static void RepairIfMoved()
    {
        // Only the real app may claim the entry (not tools or tests that host its code).
        string? exePath = Environment.ProcessPath;
        if (!string.Equals(System.IO.Path.GetFileName(exePath), "EqualizerHelper.exe", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        string? value = (key?.GetValue(ValueName) as string)?.Trim('"');
        if (!string.IsNullOrEmpty(value) && !System.IO.File.Exists(value))
        {
            SetEnabled(true);
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
