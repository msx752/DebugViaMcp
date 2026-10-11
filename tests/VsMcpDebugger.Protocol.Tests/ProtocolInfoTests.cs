using VsMcpDebugger.Protocol;
using Xunit;

namespace VsMcpDebugger.Protocol.Tests;

public class ProtocolInfoTests
{
    [Fact]
    public void Version_is_2_0_and_pipe_prefix_is_stable()
    {
        Assert.Equal(2, ProtocolInfo.Major);
        Assert.Equal(0, ProtocolInfo.Minor);
        Assert.Equal("vsmcpdebugger-", ProtocolInfo.PipeNamePrefix);
    }

    [Fact]
    public void M2_error_codes_have_stable_values_and_names()
    {
        Assert.Equal(-32010, (int)BridgeErrorCode.Denied);
        Assert.Equal(-32011, (int)BridgeErrorCode.AiDisabled);
        Assert.Equal("denied", BridgeErrorNames.ToName(BridgeErrorCode.Denied));
        Assert.Equal("ai_disabled", BridgeErrorNames.ToName(BridgeErrorCode.AiDisabled));
    }
}
