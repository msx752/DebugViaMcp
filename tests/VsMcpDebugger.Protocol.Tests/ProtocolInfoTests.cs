using VsMcpDebugger.Protocol;
using Xunit;

namespace VsMcpDebugger.Protocol.Tests;

public class ProtocolInfoTests
{
    [Fact]
    public void Version_is_1_0_and_pipe_prefix_is_stable()
    {
        Assert.Equal(1, ProtocolInfo.Major);
        Assert.Equal(0, ProtocolInfo.Minor);
        Assert.Equal("vsmcpdebugger-", ProtocolInfo.PipeNamePrefix);
    }
}
