using System;
using Microsoft.Win32;

namespace AudioPilotManager.Services;

/// <summary>
/// "Start with Windows" through the per-user Run key. No admin rights, no scheduled tasks,
/// and the uninstaller removes the same value.
/// </summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AudioPilotManager";
    public const string MinimizedArg = "--minimized";

    private static string Command => $"\"{Environment.ProcessPath}\" {MinimizedArg}";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string;
            }
            catch
            {
                return false;
            }
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled)
                key.SetValue(ValueName, Command, RegistryValueKind.String);
            else if (key.GetValue(ValueName) is not null)
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Log.Error("Could not change the start-with-Windows setting", ex);
        }
    }

    /// <summary>If the app was moved or updated, point the Run entry at the current executable.</summary>
    public static void RepairPathIfEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(ValueName) is string value && !string.Equals(value, Command, StringComparison.OrdinalIgnoreCase))
                key.SetValue(ValueName, Command, RegistryValueKind.String);
        }
        catch
        {
            // Not important enough to report.
        }
    }
}
