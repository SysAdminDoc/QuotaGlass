using QuotaGlass.Widget.Services;
using Xunit;

namespace QuotaGlass.Tests;

public sealed class UpdateCheckerTests
{
    [Fact]
    public void Is_widget_portable_asset_rejects_neighbor_executables()
    {
        Assert.True(UpdateChecker.IsWidgetPortableAsset("QuotaGlass-Widget-v0.9.0-win-x64.exe", "x64"));
        Assert.False(UpdateChecker.IsWidgetPortableAsset("QuotaGlass-NMH-v0.9.0-win-x64.exe", "x64"));
        Assert.False(UpdateChecker.IsWidgetPortableAsset("QuotaGlass-Widget-v0.9.0-win-arm64.exe", "x64"));
        Assert.False(UpdateChecker.IsWidgetPortableAsset("quota-glass-win-x64.exe", "x64"));
    }

    [Fact]
    public void Self_replace_script_escapes_single_quoted_literals()
    {
        var script = UpdateChecker.BuildSelfReplaceScript(
            new UpdateChecker.UpdateInfo("1.0.0", "https://example.invalid/release's/app.exe", "asset"),
            @"C:\Program Files\Quota'Glass\QuotaGlass.Widget.exe",
            1234,
            @"C:\Temp\Quota'Glass_update.exe",
            @"C:\Temp\Quota'Glass_update.ps1");

        Assert.Contains("'https://example.invalid/release''s/app.exe'", script);
        Assert.Contains("'C:\\Program Files\\Quota''Glass\\QuotaGlass.Widget.exe'", script);
        Assert.Contains("'C:\\Temp\\Quota''Glass_update.exe'", script);
        Assert.Contains("'C:\\Temp\\Quota''Glass_update.ps1'", script);
    }
}
