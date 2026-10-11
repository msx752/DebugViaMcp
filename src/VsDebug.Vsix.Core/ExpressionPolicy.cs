using System;
using System.Text.RegularExpressions;
using VsDebug.Protocol;

namespace VsDebug.Vsix.Core;

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

    // Invocation "name(" / "x)(" / "a[0](", assignment "=" (incl. += etc., excluding ==, !=, <=, >=, =>), ++ and --.
    private static readonly Regex SideEffects = new(@"[\w\]\)]\s*\(|(?<![=!<>])=(?![=>])|\+\+|--", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

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
