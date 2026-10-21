using DebugViaMcp.Protocol;
using Xunit;

namespace DebugViaMcp.Protocol.Tests;

public class ProtocolInfoTests
{
    [Fact]
    public void Version_is_2_3_and_pipe_prefix_is_stable()
    {
        Assert.Equal(2, ProtocolInfo.Major);
        Assert.Equal(4, ProtocolInfo.Minor);
        Assert.Equal(1, ProtocolInfo.MinorM3);
        Assert.Equal(2, ProtocolInfo.MinorM31);
        Assert.Equal(3, ProtocolInfo.MinorTimeouts);
        Assert.Equal(-32015, (int)BridgeErrorCode.InUse);
        Assert.Equal("in_use", BridgeErrorNames.ToName(BridgeErrorCode.InUse));
        Assert.Equal("debugviamcp-", ProtocolInfo.PipeNamePrefix);
    }

    [Fact]
    public void M2_error_codes_have_stable_values_and_names()
    {
        Assert.Equal(-32010, (int)BridgeErrorCode.Denied);
        Assert.Equal(-32011, (int)BridgeErrorCode.AiDisabled);
        Assert.Equal("denied", BridgeErrorNames.ToName(BridgeErrorCode.Denied));
        Assert.Equal("ai_disabled", BridgeErrorNames.ToName(BridgeErrorCode.AiDisabled));
    }

    [Fact]
    public void M3_error_codes_have_stable_values_and_names()
    {
        Assert.Equal(-32012, (int)BridgeErrorCode.Stale);
        Assert.Equal(-32013, (int)BridgeErrorCode.Unsupported);
        Assert.Equal("stale", BridgeErrorNames.ToName(BridgeErrorCode.Stale));
        Assert.Equal("unsupported", BridgeErrorNames.ToName(BridgeErrorCode.Unsupported));
    }
}
