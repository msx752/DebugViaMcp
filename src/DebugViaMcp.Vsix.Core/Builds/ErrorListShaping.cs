using System;
using System.Collections.Generic;
using System.Linq;
using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core.Abstractions;
using DebugViaMcp.Vsix.Core.Debugging;
using DebugViaMcp.Vsix.Core.Support;

namespace DebugViaMcp.Vsix.Core.Builds;

/// <summary>Filters, sorts (errors first, then file and line), counts and pages Error List entries within the response budget.</summary>
public static class ErrorListShaping
{
    public const int MaxItems = 200;

    public static ErrorListPage Page(IReadOnlyList<RawErrorItem> items, IReadOnlyCollection<ErrorSeverity> severities, string? solutionDir, int offset, int max)
    {
        if (items is null) throw new ArgumentNullException(nameof(items));
        if (severities is null) throw new ArgumentNullException(nameof(severities));
        var skip = Math.Max(0, offset);
        var take = Math.Min(Math.Max(1, max), MaxItems);

        var selected = items.Where(i => severities.Contains(i.Severity))
            .OrderBy(i => i.Severity)
            .ThenBy(i => i.File ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Line ?? 0)
            .ToList();

        var page = new List<ErrorItemInfo>();
        var budget = OutputPaging.BudgetChars;
        foreach (var i in selected.Skip(skip).Take(take))
        {
            var info = new ErrorItemInfo
            {
                Severity = i.Severity, Code = i.Code, Message = ValueShaper.Truncate(i.Message).Value, Project = i.Project,
                File = i.File is null ? null : PathUtil.ToRelative(solutionDir, i.File), Line = i.Line, Column = i.Column,
                Origin = i.FromBuild ? "build" : "live",
            };
            var cost = info.Message.Length + (info.File?.Length ?? 0) + (info.Code?.Length ?? 0) + (info.Project?.Length ?? 0) + 64;
            if (page.Count > 0 && cost > budget) break;
            page.Add(info);
            budget -= cost;
        }

        var hasMore = selected.Count > skip + page.Count;
        return new ErrorListPage
        {
            Items = page.ToArray(), HasMore = hasMore, NextOffset = hasMore ? skip + page.Count : null,
            Errors = items.Count(i => i.Severity == ErrorSeverity.Error),
            Warnings = items.Count(i => i.Severity == ErrorSeverity.Warning),
            Messages = items.Count(i => i.Severity == ErrorSeverity.Message),
        };
    }
}
