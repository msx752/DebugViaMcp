using System;

namespace VsDebug.Vsix.Core;

/// <summary>
/// Makes an expression safe to evaluate without side effects using the C# ', nse' format specifier.
/// Verified by spike 2026-10-11 (docs/spikes/2026-10-11-nse.md): the call is evaluated in a sandbox and
/// its side effects are discarded.
/// </summary>
public static class ExpressionPolicy
{
    public static string PrepareSideEffectFree(string expression)
    {
        if (expression is null) throw new ArgumentNullException(nameof(expression));
        var trimmed = expression.Trim();
        return trimmed.EndsWith(", nse", StringComparison.Ordinal) ? trimmed : trimmed + ", nse";
    }
}
