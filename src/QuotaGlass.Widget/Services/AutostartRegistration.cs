using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace QuotaGlass.Widget.Services;

/// <summary>
/// Writes / removes the HKCU\Software\Microsoft\Windows\CurrentVersion\Run
/// entry so the widget launches at user logon. Per-user; never touches HKLM.
/// </summary>
[SupportedOSPlatform("windows")]
public static class AutostartRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "QuotaGlass";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null) return;
            if (enabled)
            {
                var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exe)) return;
                key.SetValue(ValueName, $"\"{exe}\"", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Best-effort. Failure to write Run-key is not worth crashing for.
        }
    }

    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is not null;
        }
        catch
        {
            return false;
        }
    }
}
