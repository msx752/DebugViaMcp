using System;
using System.Text.RegularExpressions;
using VsMcpDebugger.Protocol;

namespace VsMcpDebugger.Vsix.Core;

/// <summary>
/// Keeps debugger expressions from mutating program state.
/// <list type="bullet">
/// <item><see cref="PrepareSideEffectFree"/> — for evaluate/drill-down: appends the C# ', nse' format specifier.
/// Verified by spike 2026-10-11 (docs/spikes/2026-10-11-nse.md): calls are evaluated in a sandbox and their
/// side effects are discarded.</item>
/// <item><see cref="RequireNoSideEffects"/> — for breakpoint conditions and tracepoint placeholders, which run
/// on every hit and cannot carry a format specifier: rejects anything that could have a side effect.</item>
/// </list>
/// </summary>
public static class ExpressionPolicy
{
    // Comments or statement/line breaks would swallow an appended ', nse' or smuggle a second statement.
    private static readonly Regex Neutralizers = new(@"//|/\*|\*/|[\r\n;]", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    // Invocation "name(" / "x)(" / "a[0](" / generic "M<T>(" (also rejects "a > (b)": drop the parentheses),
    // shift-assignments "<<=" ">>=" ">>>=", any other assignment "=" (incl. += ??= etc., excluding ==, !=, <=, >=, =>), ++ and --.
    // Property getters and indexers can still run arbitrary code; that residual risk is documented (spec §6) and
    // closed by the M2 consent gate, not by this filter.
    private static readonly Regex SideEffects = new(@"[\w\]\)>]\s*\(|(<<|>>>?)=|(?<![=!<>])=(?![=>])|\+\+|--", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    // Conditions/placeholders may contain only letters/digits (any script), whitespace and plain C# operators.
    // Escapes (\), interpolation ($, braces), backticks and non-ASCII punctuation never reach the evaluator.
    private static readonly Regex ConditionAllowlist = new(@"^[\p{L}\p{N}_ \t.\[\]()<>=!&|+\-*/%?:""',@^~]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>Longest expression or value the user can be asked to approve; it is always shown in full.</summary>
    public const int MaxMutationLength = 500;

    /// <summary>For anything shown in a consent question: present, single line, at most <see cref="MaxMutationLength"/> chars.</summary>
    public static void RequireMutationText(string? text, string name)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new BridgeException(BridgeErrorCode.InvalidArgument, $"{name} is required.");
        if (text!.Length > MaxMutationLength)
            throw new BridgeException(BridgeErrorCode.InvalidArgument, $"{name} is longer than {MaxMutationLength} characters.",
                "Shorten it: the user has to read it in full before approving.");
        RejectNeutralizers(text);
    }

    /// <summary>set_value targets: a variable, field, property or element — nothing that runs code or assigns.</summary>
    public static void RequireAssignableTarget(string? expression)
    {
        RequireMutationText(expression, "expression");
        if (!ConditionAllowlist.IsMatch(expression!) || SideEffects.IsMatch(expression!))
            throw new BridgeException(BridgeErrorCode.InvalidArgument,
                "The set_value target must not call methods, assign, use ++/-- or special characters.",
                "Target must be a variable, field, property or element, e.g. total or order.Lines[2].Quantity.");
    }

    public static string PrepareSideEffectFree(string expression)
    {
        if (expression is null) throw new ArgumentNullException(nameof(expression));
        RejectNeutralizers(expression);
        var trimmed = expression.Trim();
        return trimmed.EndsWith(", nse", StringComparison.Ordinal) ? trimmed : trimmed + ", nse";
    }

    public static void RequireNoSideEffects(string expression)
    {
        if (expression is null) throw new ArgumentNullException(nameof(expression));
        RejectNeutralizers(expression);
        if (!ConditionAllowlist.IsMatch(expression))
            throw new BridgeException(BridgeErrorCode.InvalidArgument,
                "Breakpoint conditions and tracepoint placeholders may only use identifiers, literals and plain C# operators.",
                "Avoid escapes, interpolated strings, braces and special characters, e.g. order.Id == 7.");
        if (SideEffects.IsMatch(expression))
            throw new BridgeException(BridgeErrorCode.InvalidArgument,
                "Breakpoint conditions and tracepoint placeholders run on every hit and must not call methods, assign, or use ++/--.",
                "Compare fields or properties instead, e.g. order.Id == 7 or total > 1000.");
    }

    private static void RejectNeutralizers(string expression)
    {
        if (Neutralizers.IsMatch(expression))
            throw new BridgeException(BridgeErrorCode.InvalidArgument,
                "Expressions must be a single line without comments or ';'.",
                "Pass one C# expression, e.g. order.Lines.Count.");
    }
}
