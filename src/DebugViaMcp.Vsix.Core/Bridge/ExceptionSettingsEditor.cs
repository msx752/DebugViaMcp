using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core.Abstractions;

namespace DebugViaMcp.Vsix.Core.Bridge;

/// <summary>
/// exception_list / exception_set / exception_reset on top of <see cref="IDteFacade"/>. A set is all or nothing, and each owner (a connection)
/// can put back exactly what it changed. All members run on the UI thread.
/// </summary>
internal sealed class ExceptionSettingsEditor(IDteFacade dte)
{
    public const int MaxLimit = 500;
    public const int MaxSuggestions = 5;
    private const int MaxEdits = 3;

    private sealed class Original(string group, string name, bool value)
    {
        public string Group { get; } = group;
        public string Name { get; } = name;
        public bool Value { get; } = value;
    }

    /// <summary>The first value of every setting an owner changed. Weak, so a connection that is gone never keeps its record alive.</summary>
    private readonly ConditionalWeakTable<object, List<Original>> _changed = new();

    public ExceptionSettingsPage List(string? group, string? filter, bool onlyEnabled, int limit)
    {
        limit = Math.Min(Math.Max(limit, 1), MaxLimit);
        var groupNames = dte.ExceptionGroupNames();
        IReadOnlyList<string> selected = group is null ? groupNames : new[] { RequireGroup(groupNames, group) };
        var left = limit;
        var result = new List<ExceptionGroupInfo>();
        foreach (var name in selected)
        {
            // One enumeration per group asked for (never one lookup per entry: that is a scan each time).
            var matches = dte.ExceptionEntries(name).Where(e => Matches(e.Name, filter) && (!onlyEnabled || e.BreakWhenThrown)).ToList();
            var total = matches.Count;
            var shown = matches.Take(left).Select(e => new ExceptionSettingInfo { Name = e.Name, BreakWhenThrown = e.BreakWhenThrown }).ToList();

            if (group is null && total == 0) continue;
            left -= shown.Count;
            result.Add(new ExceptionGroupInfo { Name = name, Settings = shown.ToArray(), Total = total, Truncated = shown.Count < total });
        }

        return new ExceptionSettingsPage { Groups = result.ToArray() };
    }

    /// <summary>Checks the group and every name and says what a set would change, without writing. Reads one value per name; the names of the group only when one is unknown.</summary>
    public ExceptionSetResult Plan(string group, string[] names, bool value)
    {
        if (names is null || names.Length == 0)
            throw new BridgeException(BridgeErrorCode.InvalidArgument, "names is empty.", "Give the exact exception names, for example System.NullReferenceException.");

        if (names.Any(string.IsNullOrWhiteSpace))
            throw new BridgeException(BridgeErrorCode.InvalidArgument, "names contains an empty entry.", "Give the exact exception names, for example System.NullReferenceException. Nothing was changed.");

        var groupName = RequireGroup(dte.ExceptionGroupNames(), group);
        var wanted = names.Distinct(StringComparer.Ordinal).ToArray();
        var current = new Dictionary<string, bool>(StringComparer.Ordinal);
        var unknown = new List<string>();
        foreach (var n in wanted)
        {
            if (dte.BreakWhenThrown(groupName, n) is { } on) current[n] = on;
            else unknown.Add(n);
        }

        if (unknown.Count > 0)
            throw new BridgeException(BridgeErrorCode.NotFound, UnknownMessage(group, unknown.ToArray(), dte.ExceptionEntries(groupName).Select(e => e.Name)),
                "Names are exact and case-sensitive. Call exception_list with group and filter to see them. Nothing was changed.");

        return new ExceptionSetResult
        {
            Group = groupName,
            Changes = wanted.Select(n => new ExceptionChange { Name = n, Previous = current[n], Current = value, Changed = current[n] != value }).ToArray(),
        };
    }

    public ExceptionSetResult Set(object owner, string group, string[] names, bool value)
    {
        var plan = Plan(group, names, value);
        var written = new List<ExceptionChange>();
        try
        {
            foreach (var change in plan.Changes.Where(c => c.Changed))
            {
                dte.SetBreakWhenThrown(group, change.Name, value);
                written.Add(change);
            }
        }
#pragma warning disable CA1031 // Put back what was written, then report the original failure.
        catch
        {
            foreach (var change in Enumerable.Reverse(written))
            {
                try { dte.SetBreakWhenThrown(group, change.Name, change.Previous); }
                catch { Remember(owner, group, change); }   // could not be put back: keep it undoable (reset, release) instead of forgetting it
            }

            throw;
        }
#pragma warning restore CA1031

        foreach (var change in written) Remember(owner, group, change);

        return plan;
    }

    /// <summary>Keeps the first value of a setting an owner changed; a second change of the same setting does not replace it.</summary>
    private void Remember(object owner, string group, ExceptionChange change)
    {
        var record = _changed.GetOrCreateValue(owner);
        if (!record.Any(o => o.Group == group && o.Name == change.Name))
            record.Add(new Original(group, change.Name, change.Previous));
    }

    public int Pending(object owner) => _changed.TryGetValue(owner, out var record) ? record.Count : 0;

    public ExceptionResetResult Reset(object owner)
    {
        if (!_changed.TryGetValue(owner, out var record) || record.Count == 0) return new ExceptionResetResult();

        var restored = new List<ExceptionChange>();
        foreach (var original in record)
        {
            if (dte.BreakWhenThrown(original.Group, original.Name) is not { } now) continue;   // the entry is gone: nothing to put back
            var changed = now != original.Value;
            if (changed) dte.SetBreakWhenThrown(original.Group, original.Name, original.Value);
            restored.Add(new ExceptionChange { Name = original.Group + "/" + original.Name, Previous = now, Current = original.Value, Changed = changed });
        }

        _changed.Remove(owner);
        return new ExceptionResetResult { Restored = restored.ToArray() };
    }

    /// <summary>
    /// Puts back what an owner changed and forgets it, for when the owner's hold on Visual Studio ends: the agent that is gone cannot
    /// call <c>exception_reset</c>, and the user should find their settings as they were. A failing write is thrown to the caller to log;
    /// the record is gone either way, so a later owner never undoes this one's changes.
    /// </summary>
    public void Release(object owner)
    {
        try { Reset(owner); }
        finally { _changed.Remove(owner); }
    }

    private static string RequireGroup(IReadOnlyList<string> groups, string name) =>
        groups.FirstOrDefault(g => string.Equals(g, name, StringComparison.Ordinal))
        ?? throw new BridgeException(BridgeErrorCode.NotFound, $"No exception group '{name}'. Groups: {string.Join(", ", groups)}.", "Call exception_list without group to see them.");

    private static bool Matches(string name, string? filter) =>
        string.IsNullOrEmpty(filter) || name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

    private static string UnknownMessage(string group, string[] unknown, IEnumerable<string> known)
    {
        var parts = unknown.Take(MaxSuggestions).Select(n =>
        {
            var close = Suggest(n, known);
            return close.Length == 0 ? $"'{n}'" : $"'{n}' (did you mean: {string.Join(", ", close)}?)";
        });
        var more = unknown.Length > MaxSuggestions ? $" and {unknown.Length - MaxSuggestions} more" : "";
        return $"No such exception in '{group}': {string.Join("; ", parts)}{more}.";
    }

    /// <summary>Names a caller probably meant: the same name without its namespace first, then names that contain it, then a few typos away.</summary>
    private static string[] Suggest(string wanted, IEnumerable<string> known)
    {
        var ranked = new List<(int Rank, int Score, string Name)>();
        foreach (var name in known)
        {
            if (name.Equals(wanted, StringComparison.OrdinalIgnoreCase) || name.EndsWith("." + wanted, StringComparison.OrdinalIgnoreCase))
                ranked.Add((0, name.Length, name));
            else if (name.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0)
                ranked.Add((1, name.Length, name));
            else if (Math.Abs(name.Length - wanted.Length) <= MaxEdits && Distance(name, wanted) is var d && d <= MaxEdits)
                ranked.Add((2, d, name));
        }

        return ranked.OrderBy(r => r.Rank).ThenBy(r => r.Score).ThenBy(r => r.Name, StringComparer.Ordinal).Take(MaxSuggestions).Select(r => r.Name).ToArray();
    }

    /// <summary>Edit distance, case-insensitive; two rows, so the cost is the product of the lengths and nothing is kept.</summary>
    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var row = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            row[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = char.ToUpperInvariant(a[i - 1]) == char.ToUpperInvariant(b[j - 1]) ? 0 : 1;
                row[j] = Math.Min(Math.Min(row[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, row) = (row, previous);
        }

        return previous[b.Length];
    }
}
