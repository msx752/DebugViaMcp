using System.Runtime.InteropServices;
using VsMcpDebugger.Vsix.Core;
using Xunit;

#pragma warning disable CA2201 // The tests throw COMException on purpose: that is what Visual Studio does.

namespace VsMcpDebugger.Vsix.Core.Tests;

public class ComSafeTests
{
    [Fact]
    public void Value_is_returned_when_the_read_works()
    {
        Assert.True(ComSafe.TryRead(() => 7, out var value));
        Assert.Equal(7, value);
    }

    [Theory]
    [InlineData(unchecked((int)0x80004005))]   // E_FAIL: OutputWindowPane.TextDocument on a pane that has no text document yet
    [InlineData(unchecked((int)0x80010108))]   // RPC_E_DISCONNECTED
    public void A_com_failure_makes_the_field_unavailable_instead_of_failing_the_call(int hresult)
    {
        Assert.False(ComSafe.TryRead<int>(() => throw new COMException("boom", hresult), out var value));
        Assert.Equal(0, value);
    }

    [Fact]
    public void A_released_com_object_makes_the_field_unavailable()
    {
        Assert.False(ComSafe.TryRead<string>(() => throw new InvalidComObjectException("released"), out _));
    }

    [Fact]
    public void Bugs_in_our_own_code_are_not_swallowed()
    {
        Assert.Throws<InvalidOperationException>(() => ComSafe.TryRead<int>(() => throw new InvalidOperationException("ours"), out _));
    }
}
