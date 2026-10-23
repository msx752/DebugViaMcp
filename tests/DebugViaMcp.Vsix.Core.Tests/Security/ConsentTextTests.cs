using Xunit;
using DebugViaMcp.Vsix.Core.Security;

namespace DebugViaMcp.Vsix.Core.Tests.Security;

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

    [Fact]
    public void Enable_breakpoint_text_says_the_expression_runs_on_every_hit()
    {
        Assert.Equal(
            "claude-code wants to enable the breakpoint at a.cs:3 when x > 1 — this runs in your program on every hit",
            ConsentText.EnableBreakpoint("claude-code", "a.cs:3", "x > 1", null));
    }

    [Fact]
    public void Exception_settings_text_names_client_group_and_exceptions()
    {
        Assert.Equal(
            "claude-code wants to turn Break When Thrown on for 2 exceptions in Common Language Runtime Exceptions: System.A, System.B",
            ConsentText.ExceptionSettings("claude-code", "Common Language Runtime Exceptions", ["System.A", "System.B"], true));
        Assert.Equal(
            "claude-code wants to turn Break When Thrown off for 1 exception in C++ Exceptions: std::x",
            ConsentText.ExceptionSettings("claude-code", "C++ Exceptions", ["std::x"], false));
    }

    [Fact]
    public void Exception_settings_text_lists_five_names_and_counts_the_rest()
    {
        var text = ConsentText.ExceptionSettings("c", "G", ["A", "B", "C", "D", "E", "F", "G", "H"], true);

        Assert.EndsWith(": A, B, C, D, E and 3 more", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Exception_settings_text_makes_control_characters_visible()
    {
        Assert.Contains("Bad\\nName", ConsentText.ExceptionSettings("c", "G", ["Bad\nName"], true), StringComparison.Ordinal);
    }

    [Fact]
    public void Exception_reset_text_counts_what_it_restores()
    {
        Assert.Equal(
            "claude-code wants to restore Break When Thrown for 3 exceptions it changed",
            ConsentText.ExceptionReset("claude-code", 3));
    }

    [Fact]
    public void Attach_text_escapes_the_process_name()
    {
        Assert.Contains("Fault\\nDemo (pid 7)", ConsentText.Attach("claude-code", "Fault\nDemo", 7), StringComparison.Ordinal);
    }

    /// <summary>Review focus #2.</summary>
    [Fact]
    public void Bidi_override_and_newlines_are_visible()
    {
        Assert.Equal("1\\n\\u202edrowssap", ConsentText.Visible("1\n\u202Edrowssap"));
        Assert.Equal("a\\r\\tb\\u0007", ConsentText.Visible("a\r\tb\u0007"));
    }

    /// <summary>Security review: invisible characters outside the BMP or in other invisible categories must not pass.</summary>
    [Fact]
    public void Invisible_and_ambiguous_characters_are_escaped()
    {
        const string bs = "\\";
        var tag = char.ConvertFromUtf32(0xE0041);           // TAG LATIN CAPITAL LETTER A: invisible, outside the BMP
        var loneSurrogate = ((char)0xD800).ToString();
        var privateUse = ((char)0xE000).ToString();
        var noBreakSpace = ((char)0x00A0).ToString();

        Assert.Equal("a" + bs + "U000e0041b", ConsentText.Visible("a" + tag + "b"));
        Assert.Equal("a" + bs + "ud800b", ConsentText.Visible("a" + loneSurrogate + "b"));
        Assert.Equal("a" + bs + "ue000b", ConsentText.Visible("a" + privateUse + "b"));
        Assert.Equal("a" + bs + "u00a0b", ConsentText.Visible("a" + noBreakSpace + "b"));
        Assert.Equal("a b", ConsentText.Visible("a b"));
        Assert.Equal("ş😀", ConsentText.Visible("ş😀"));
    }

    /// <summary>Security review: a literal backslash must not be able to forge an escape sequence.</summary>
    [Fact]
    public void Literal_backslashes_are_escaped_so_escapes_cannot_be_forged()
    {
        const string bs = "\\";
        var realNewline = "a" + (char)10 + "b";
        var forgedNewline = "a" + bs + "nb";

        Assert.NotEqual(ConsentText.Visible(realNewline), ConsentText.Visible(forgedNewline));
        Assert.Equal("a" + bs + bs + "nb", ConsentText.Visible(forgedNewline));
    }

    /// <summary>Security review: truncating the client name must not leave half of a surrogate pair.</summary>
    [Fact]
    public void Client_name_truncation_never_splits_a_surrogate_pair()
    {
        var name = new string('x', 63) + char.ConvertFromUtf32(0x1F600) + "tail";

        var shown = ConsentText.ClientName(name);

        Assert.DoesNotContain(shown, char.IsSurrogate);
        Assert.EndsWith("…", shown, StringComparison.Ordinal);
    }

    /// <summary>Security review: stacked combining marks can paint over the text; only the first one is kept as is.</summary>
    [Fact]
    public void Stacked_combining_marks_are_escaped()
    {
        const string bs = "\\";
        var acute = ((char)0x0301).ToString();

        Assert.Equal("e" + acute, ConsentText.Visible("e" + acute));
        Assert.Equal("e" + acute + bs + "u0301" + bs + "u0301", ConsentText.Visible("e" + acute + acute + acute));
    }

    /// <summary>Security review: a mark with no visible base of its own would decorate the text around it.</summary>
    [Fact]
    public void Marks_without_a_visible_base_are_escaped()
    {
        const string bs = "\\";
        var acute = ((char)0x0301).ToString();
        var supplementaryMark = char.ConvertFromUtf32(0x1D167);   // MUSICAL SYMBOL COMBINING TREMOLO-1

        Assert.Equal(bs + "u0301x", ConsentText.Visible(acute + "x"));
        Assert.Equal(bs + "n" + bs + "u0301", ConsentText.Visible((char)10 + acute));
        Assert.Equal(" " + bs + "u0301", ConsentText.Visible(" " + acute));
        Assert.Equal("e" + acute + bs + "U0001d167", ConsentText.Visible("e" + acute + supplementaryMark));
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
