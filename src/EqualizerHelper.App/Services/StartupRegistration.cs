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
    private const string ValueName = "LocoMic";

    // The Run-key entry written before the app was renamed to LocoMic.
    private const string LegacyValueName = "EqualizerHelper";

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
    /// Keeps "start with Windows" working after the app moves or is renamed:
    /// replaces the entry from before the LocoMic rename with one for this
    /// exe, and repoints an entry whose exe no longer exists at this exe.
    /// Entries for another existing copy are left alone.
    /// </summary>
    public static void RepairIfMoved()
    {
        // Only the real app may claim the entry (not tools or tests that host its code).
        string? exePath = Environment.ProcessPath;
        if (!string.Equals(System.IO.Path.GetFileName(exePath), "LocoMic.exe", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        bool hadLegacyEntry;
        string? value;
        using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
        {
            hadLegacyEntry = key?.GetValue(LegacyValueName) != null;
            if (hadLegacyEntry)
            {
                key!.DeleteValue(LegacyValueName, throwOnMissingValue: false);
            }

            value = (key?.GetValue(ValueName) as string)?.Trim('"');
        }

        if ((hadLegacyEntry && string.IsNullOrEmpty(value))
            || (!string.IsNullOrEmpty(value) && !System.IO.File.Exists(value)))
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
