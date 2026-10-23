using DebugViaMcp.Mcp.Tests.Fakes;
using DebugViaMcp.Mcp.Tools;
using DebugViaMcp.Protocol;
using Xunit;
using static DebugViaMcp.Mcp.Tests.ExecutionToolsTests;

namespace DebugViaMcp.Mcp.Tests;

public class SymbolStatusToolTests
{
    private static IdeTools Tools(FakeConnectionManager cm) => new(cm);

    [Fact]
    public async Task Returns_the_options_the_unbound_breakpoints_and_the_notes()
    {
        var bridge = new FakeDebugBridge
        {
            OnSymbolStatus = () => new SymbolStatus
            {
                RequireSourceToMatch = true, JustMyCode = false,
                Unbound = [new BreakpointInfo { Id = "bp-1", File = "OrderService.cs", Line = 14, BindError = "Will not be hit" }],
                Notes = ["Breakpoints bind when debugging starts."],
            },
        };

        var json = Json(await Tools(new FakeConnectionManager(bridge, DebuggerMode.Design)).SymbolStatus(CancellationToken.None));

        Assert.True(json.GetProperty("requireSourceToMatch").GetBoolean());
        Assert.False(json.GetProperty("justMyCode").GetBoolean());
        Assert.False(json.TryGetProperty("sourceServer", out _));   // not reported: left out, not false
        Assert.Equal(14, json.GetProperty("unbound")[0].GetProperty("line").GetInt32());
        Assert.Equal("Breakpoints bind when debugging starts.", json.GetProperty("notes")[0].GetString());
    }

    [Fact]
    public async Task With_every_breakpoint_bound_the_lists_are_empty()
    {
        var bridge = new FakeDebugBridge { OnSymbolStatus = () => new SymbolStatus() };

        var json = Json(await Tools(new FakeConnectionManager(bridge)).SymbolStatus(CancellationToken.None));

        Assert.Equal(0, json.GetProperty("unbound").GetArrayLength());
        Assert.Equal(0, json.GetProperty("notes").GetArrayLength());
    }

    [Theory]
    [InlineData(DebuggerMode.Design)]
    [InlineData(DebuggerMode.Run)]
    [InlineData(DebuggerMode.Break)]
    public async Task Works_in_every_debugger_mode(DebuggerMode mode)
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge(), mode);

        var json = Json(await Tools(cm).SymbolStatus(CancellationToken.None));

        Assert.False(json.TryGetProperty("error", out _));
        Assert.Contains("GetSymbolStatus", cm.Bridge.Calls);
    }

    [Fact]
    public async Task An_extension_without_symbol_status_is_unsupported()
    {
        var cm = new FakeConnectionManager(new FakeDebugBridge()) { RemoteProtocolMinor = ProtocolInfo.MinorMasking };

        var json = Json(await Tools(cm).SymbolStatus(CancellationToken.None));

        Assert.Equal("unsupported", json.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("GetSymbolStatus", cm.Bridge.Calls);
    }
}
