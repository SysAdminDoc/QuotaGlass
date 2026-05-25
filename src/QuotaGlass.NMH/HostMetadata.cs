using System.Reflection;
using QuotaGlass.Shared;

namespace QuotaGlass.NMH;

internal static class HostMetadata
{
    public const int SchemaMin = QuotaGlass.Shared.SchemaVersion.Min;
    public const int SchemaMax = QuotaGlass.Shared.SchemaVersion.Max;

    public static string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        var asm = typeof(HostMetadata).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(info)) return info!;
        var v = asm.GetName().Version;
        return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
