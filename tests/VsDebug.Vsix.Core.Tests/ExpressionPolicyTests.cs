using VsDebug.Vsix.Core;
using Xunit;

namespace VsDebug.Vsix.Core.Tests;

/// <summary>Spike 2026-10-11 (docs/spikes/2026-10-11-nse.md): ', nse' discards side effects via DTE.</summary>
public class ExpressionPolicyTests
{
    [Fact]
    public void Appends_nse_specifier()
        => Assert.Equal("order.Touch(), nse", ExpressionPolicy.PrepareSideEffectFree("order.Touch()"));

    [Fact]
    public void Does_not_double_append()
        => Assert.Equal("x, nse", ExpressionPolicy.PrepareSideEffectFree("x, nse"));
}
