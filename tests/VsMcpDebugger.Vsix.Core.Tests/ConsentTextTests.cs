using VsMcpDebugger.Vsix.Core;
using Xunit;

namespace VsMcpDebugger.Vsix.Core.Tests;

public class ConsentTextTests
{
    [Fact]
    public void Set_value_text_names_client_target_value_and_function()
    {
        Assert.Equal(
            "claude-code wants to set total = 0 in OrderDemo.OrderService.Submit",
            ConsentText.SetValue("claude-code", "total", "0", "OrderDemo.OrderService.Submit"));
    }

    [Fact]
    public void Evaluate_text_says_side_effects()
    {
        Assert.Equal(
            "claude-code wants to evaluate order.Touch() with side effects in A.M",
            ConsentText.EvaluateWithSideEffects("claude-code", "order.Touch()", "A.M"));
    }

    /// <summary>Review focus #2.</summary>
    [Fact]
    public void Bidi_override_and_newlines_are_visible()
    {
        Assert.Equal("1\\n\\u202edrowssap", ConsentText.Visible("1\n\u202Edrowssap"));
        Assert.Equal("a\\r\\tb\\u0007", ConsentText.Visible("a\r\tb\u0007"));
    }

    [Fact]
    public void Client_name_is_sanitised_capped_and_defaulted()
    {
        Assert.Equal("AI agent", ConsentText.ClientName("  "));
        Assert.Equal(@"evil\nname", ConsentText.ClientName("evil\nname"));
        Assert.Equal(new string('x', 64) + "…", ConsentText.ClientName(new string('x', 100)));
    }

    [Fact]
    public void Generic_operation_text()
    {
        Assert.Equal("claude-code wants to run debug_step: over", ConsentText.Operation("claude-code", "debug_step", "over"));
        Assert.Equal("claude-code wants to run debug_stop", ConsentText.Operation("claude-code", "debug_stop", ""));
    }
}
