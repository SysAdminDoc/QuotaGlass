using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using QuotaGlass.Shared;

namespace QuotaGlass.NMH;

/// <summary>
/// Installs / uninstalls the native messaging host registration for
/// Chrome, Edge, and Firefox under the current user (HKCU). System-wide
/// (HKLM) install is intentionally not supported — admin elevation would
/// be required and the rest of QuotaGlass is per-user.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class HostRegistrar
{
    private const string HostName = "com.sysadmindoc.quotaglass";
    private const string HostDescription = "QuotaGlass desktop bridge for AI-Usage_Tracker.";

    // AI-Usage_Tracker extension IDs. Update if the official ID changes.
    private static readonly string[] ChromeExtensionIds =
    {
        // Placeholder — replace with the real chrome.google.com/webstore ID once published.
        // The 32-char ID assigned to AI-Usage_Tracker.
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
    };

    // Source of truth: AI-Usage_Tracker/manifests/firefox.json -> browser_specific_settings.gecko.id
    private static readonly string[] FirefoxExtensionIds =
    {
        "ai-usage-tracker@sysadmindoc.dev",
    };

    public static int Register()
    {
        try
        {
            var exePath = Environment.ProcessPath ?? throw new InvalidOperationException("could not resolve executable path");
            var manifestPath = AppPaths.NmhManifestFile;

            WriteChromiumManifest(manifestPath, exePath, ChromeExtensionIds);
            WriteRegistryKey(@"Software\Google\Chrome\NativeMessagingHosts\" + HostName, manifestPath);
            WriteRegistryKey(@"Software\Microsoft\Edge\NativeMessagingHosts\" + HostName, manifestPath);
            WriteRegistryKey(@"Software\Chromium\NativeMessagingHosts\" + HostName, manifestPath);
            Logger.Info($"registered Chromium NMH at {manifestPath}");

            var firefoxManifest = Path.Combine(AppPaths.LocalAppDataRoot, "nmh-manifest-firefox.json");
            WriteFirefoxManifest(firefoxManifest, exePath, FirefoxExtensionIds);
            WriteRegistryKey(@"Software\Mozilla\NativeMessagingHosts\" + HostName, firefoxManifest);
            Logger.Info($"registered Firefox NMH at {firefoxManifest}");

            Console.Error.WriteLine($"QuotaGlass.NMH registered for {HostName}");
            Console.Error.WriteLine($"  Chromium manifest: {manifestPath}");
            Console.Error.WriteLine($"  Firefox  manifest: {firefoxManifest}");
            return 0;
        }
        catch (Exception ex)
        {
            Logger.Error("registration failed", ex);
            Console.Error.WriteLine($"Registration failed: {ex.Message}");
            return 1;
        }
    }

    public static int Unregister()
    {
        try
        {
            DeleteRegistryKey(@"Software\Google\Chrome\NativeMessagingHosts\" + HostName);
            DeleteRegistryKey(@"Software\Microsoft\Edge\NativeMessagingHosts\" + HostName);
            DeleteRegistryKey(@"Software\Chromium\NativeMessagingHosts\" + HostName);
            DeleteRegistryKey(@"Software\Mozilla\NativeMessagingHosts\" + HostName);

            TryDelete(AppPaths.NmhManifestFile);
            TryDelete(Path.Combine(AppPaths.LocalAppDataRoot, "nmh-manifest-firefox.json"));

            Logger.Info("unregistered NMH");
            Console.Error.WriteLine($"QuotaGlass.NMH unregistered.");
            return 0;
        }
        catch (Exception ex)
        {
            Logger.Error("unregistration failed", ex);
            Console.Error.WriteLine($"Unregistration failed: {ex.Message}");
            return 1;
        }
    }

    private static void WriteChromiumManifest(string path, string exePath, string[] extensionIds)
    {
        var allowedOrigins = extensionIds.Select(id => $"chrome-extension://{id}/").ToArray();
        var doc = new
        {
            name = HostName,
            description = HostDescription,
            path = exePath,
            type = "stdio",
            allowed_origins = allowedOrigins,
        };
        var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void WriteFirefoxManifest(string path, string exePath, string[] extensionIds)
    {
        var doc = new
        {
            name = HostName,
            description = HostDescription,
            path = exePath,
            type = "stdio",
            allowed_extensions = extensionIds,
        };
        var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void WriteRegistryKey(string subKey, string manifestPath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(subKey, writable: true);
        key.SetValue(string.Empty, manifestPath, RegistryValueKind.String);
    }

    private static void DeleteRegistryKey(string subKey)
    {
        Registry.CurrentUser.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // best-effort
        }
    }
}
