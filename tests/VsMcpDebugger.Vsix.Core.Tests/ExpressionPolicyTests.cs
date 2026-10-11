using VsMcpDebugger.Protocol;
using VsMcpDebugger.Vsix.Core;
using Xunit;

namespace VsMcpDebugger.Vsix.Core.Tests;

/// <summary>Spike 2026-10-11 (docs/spikes/2026-10-11-nse.md): ', nse' discards side effects via DTE.</summary>
public class ExpressionPolicyTests
{
    [Fact]
    public void Appends_nse_specifier()
        => Assert.Equal("order.Touch(), nse", ExpressionPolicy.PrepareSideEffectFree("order.Touch()"));

    [Fact]
    public void Does_not_double_append()
        => Assert.Equal("x, nse", ExpressionPolicy.PrepareSideEffectFree("x, nse"));

    /// <summary>A comment or line break would swallow the appended ', nse' and let side effects run.</summary>
    [Theory]
    [InlineData("x = 5 //")]
    [InlineData("x = 5 /* */")]
    [InlineData("x = 5\n")]
    [InlineData("x = 5\r\ny")]
    [InlineData("x = 5; y")]
    public void Rejects_input_that_could_neutralize_the_specifier(string expression)
        => Assert.Equal(BridgeErrorCode.InvalidArgument,
            Assert.Throws<BridgeException>(() => ExpressionPolicy.PrepareSideEffectFree(expression)).Code);

    /// <summary>Breakpoint conditions and tracepoint messages run on every hit and cannot carry ', nse'.</summary>
    [Theory]
    [InlineData("order.Touch() > 0")]
    [InlineData("x = 5")]
    [InlineData("total += 1")]
    [InlineData("i++ > 3")]
    [InlineData("--i == 0")]
    [InlineData("x // comment")]
    [InlineData("order.Get<int>() > 0")]
    [InlineData("order .Touch () > 0")]
    [InlineData("x <<= 1")]
    [InlineData("x >>= 1")]
    [InlineData("x >>>= 1")]
    [InlineData("x ??= y")]
    public void Rejects_possible_side_effects_in_conditions(string expression)
        => Assert.Equal(BridgeErrorCode.InvalidArgument,
            Assert.Throws<BridgeException>(() => ExpressionPolicy.RequireNoSideEffects(expression)).Code);

    /// <summary>Allowlist: escapes, interpolation, braces and non-ASCII punctuation never reach the evaluator.</summary>
    [Theory]
    [InlineData(@"order.Touch\u0028\u0029 > 0")]
    [InlineData("order.Touch\uFF08\uFF09 > 0")]
    [InlineData("$\"{x}\" != null")]
    [InlineData("new { A = 1 } != null")]
    [InlineData("x == 1 `")]
    [InlineData("x\u2028== 1")]
    public void Rejects_characters_outside_the_condition_allowlist(string expression)
        => Assert.Equal(BridgeErrorCode.InvalidArgument,
            Assert.Throws<BridgeException>(() => ExpressionPolicy.RequireNoSideEffects(expression)).Code);

    [Theory]
    [InlineData("sipariş.Tutar > 10")]
    [InlineData("name == \"Ali\" && count % 2 != 0")]
    [InlineData("flags ^ mask | ~bits")]
    [InlineData("x is null || y?.Length >= 3")]
    public void Allows_unicode_identifiers_and_common_operators(string expression)
        => ExpressionPolicy.RequireNoSideEffects(expression);

    [Theory]
    [InlineData("total > 1000")]
    [InlineData("order.Id == 7")]
    [InlineData("x != null")]
    [InlineData("order.Lines.Count >= 3")]
    [InlineData("arr[2] <= (a + b)")]
    public void Allows_pure_member_access_and_comparisons(string expression)
        => ExpressionPolicy.RequireNoSideEffects(expression);
}
