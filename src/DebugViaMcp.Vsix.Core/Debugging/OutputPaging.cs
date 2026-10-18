using System;
using System.Collections.Generic;
using DebugViaMcp.Protocol;

namespace DebugViaMcp.Vsix.Core.Debugging;

/// <summary>Turns "last N lines, skipping the newest M" into one bounded read and a response within budget.</summary>
public static class OutputPaging
{
    public const int MaxTail = 500;

    /// <summary>Payload budget of one page (about 14 KB).</summary>
    public const int BudgetChars = 14 * 1024;

    /// <param name="read">Reads <c>count</c> lines starting at 1-based line <c>first</c>; called at most once.</param>
    public static OutputPage Page(string pane, int totalLines, int tail, int offset, Func<int, int, IReadOnlyList<string>> read)
    {
        if (read is null) throw new ArgumentNullException(nameof(read));
        tail = Math.Min(Math.Max(1, tail), MaxTail);
        offset = Math.Max(0, offset);
        var last = totalLines - offset;
        if (last < 1) return new OutputPage { Pane = pane, TotalLines = totalLines };

        var first = Math.Max(1, last - tail + 1);
        var raw = read(first, last - first + 1);

        // Keep the newest lines when the budget runs out.
        var kept = new List<string>();
        var budget = BudgetChars;
        var truncated = false;
        for (var i = raw.Count - 1; i >= 0; i--)
        {
            var (line, cut) = ValueShaper.Truncate(raw[i]);
            if (kept.Count > 0 && line.Length > budget) break;
            truncated |= cut;
            kept.Add(line);
            budget -= line.Length;
        }

        kept.Reverse();
        var firstKept = last - kept.Count + 1;
        var hasMore = firstKept > 1;
        return new OutputPage
        {
            Pane = pane, TotalLines = totalLines, FirstLine = firstKept, Lines = kept.ToArray(), Truncated = truncated,
            HasMore = hasMore, NextOffset = hasMore ? offset + kept.Count : null,
        };
    }
}
