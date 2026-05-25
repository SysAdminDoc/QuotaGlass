using System.Reflection;

namespace QuotaGlass.NMH;

internal static class HostMetadata
{
    public const int SchemaMin = 1;
    public const int SchemaMax = 1;

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
