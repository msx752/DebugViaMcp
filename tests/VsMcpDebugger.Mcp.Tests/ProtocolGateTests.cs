using VsMcpDebugger.Mcp.Tests.Fakes;
using VsMcpDebugger.Mcp.Tools;
using VsMcpDebugger.Protocol;
using Xunit;
using static VsMcpDebugger.Mcp.Tests.ExecutionToolsTests;

namespace VsMcpDebugger.Mcp.Tests;

public class ProtocolGateTests
{
    [Fact]
    public void Old_extension_gets_unsupported_with_an_update_hint()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge()) { RemoteProtocolMinor = 0 };

        var result = ToolResponse.RequireMinor(cm, ProtocolInfo.MinorM3);

        Assert.NotNull(result);
        var error = Json(result!).GetProperty("error");
        Assert.Equal("unsupported", error.GetProperty("code").GetString());
        Assert.Contains("Update the VsMcpDebugger extension", error.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Current_extension_passes_the_gate()
    {
        Assert.Null(ToolResponse.RequireMinor(new FakeConnectionManager(new FakeDebugBridge()), ProtocolInfo.MinorM3));
    }
}
