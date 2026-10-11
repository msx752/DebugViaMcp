using VsDebug.Protocol;
using Xunit;

namespace VsDebug.Protocol.Tests;

public class DiscoveryPathsTests
{
    private static string Root(Dictionary<string, string?> env, bool win, bool mac) =>
        DiscoveryPaths.ResolveRoot(k => env.GetValueOrDefault(k), win, mac, localAppData: @"C:\Users\u\AppData\Local", home: "/Users/u");

    [Fact]
    public void Windows_uses_local_app_data()
        => Assert.Equal(Path.Combine(@"C:\Users\u\AppData\Local", "VsDebug"), Root([], win: true, mac: false));

    [Fact]
    public void Mac_uses_application_support()
        => Assert.Equal(Path.Combine("/Users/u", "Library", "Application Support", "VsDebug"), Root([], win: false, mac: true));

    [Fact]
    public void Linux_prefers_xdg_data_home()
    {
        Assert.Equal(Path.Combine("/x", "VsDebug"), Root(new() { ["XDG_DATA_HOME"] = "/x" }, win: false, mac: false));
        Assert.Equal(Path.Combine("/Users/u", ".local", "share", "VsDebug"), Root([], win: false, mac: false));
    }

    [Fact]
    public void VSDEBUG_HOME_overrides_everything()
        => Assert.Equal("/custom", Root(new() { ["VSDEBUG_HOME"] = "/custom" }, win: true, mac: false));
}
