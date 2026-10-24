using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core.Security;

namespace DebugViaMcp.Vsix.Core.Bridge;

/// <summary>
/// The watch lists of the connections (agents). Kept in memory only, one list per owner, and weakly: a connection that is gone
/// never keeps its list alive. All members run on the UI thread, like the rest of the bridge service.
/// </summary>
internal sealed class WatchList
{
    public const int MaxEntries = 10;
    public const int MaxExpressionLength = 500;

    internal sealed class Entry(string id, string expression, string? name, string prepared)
    {
        public string Id { get; } = id;
        public string Expression { get; } = expression;
        public string? Name { get; } = name;

        /// <summary>The expression as it is evaluated (side-effect free), made once when the entry was added, not at every stop.</summary>
        public string Prepared { get; } = prepared;

        /// <summary>The value text of the last read that gave one; what <c>changed</c> is compared with.</summary>
        public string? LastText { get; set; }
    }

    private sealed class Holder
    {
        public List<Entry> Entries { get; } = [];
        public int Next { get; set; } = 1;
    }

    private readonly ConditionalWeakTable<object, Holder> _lists = new();

    /// <summary>Adds the expression, or returns the entry that already has it. Refuses what <c>eval</c> would refuse and more than <see cref="MaxEntries"/> entries.</summary>
    public (Entry Entry, bool AlreadyExisted) Add(object owner, string? expression, string? name)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new BridgeException(BridgeErrorCode.InvalidArgument, "expression is required.");
        var text = expression!.Trim();
        if (text.Length > MaxExpressionLength)
            throw new BridgeException(BridgeErrorCode.InvalidArgument, $"expression is longer than {MaxExpressionLength} characters.");
        var prepared = ExpressionPolicy.PrepareSideEffectFree(text);   // throws the same invalid_argument as eval

        var holder = _lists.GetOrCreateValue(owner);
        var existing = holder.Entries.FirstOrDefault(e => string.Equals(e.Expression, text, StringComparison.Ordinal));
        if (existing is not null) return (existing, true);

        if (holder.Entries.Count >= MaxEntries)
            throw new BridgeException(BridgeErrorCode.InvalidArgument, $"The watch list holds at most {MaxEntries} entries.",
                "Remove one with watch_remove (watch_list shows the ids), then add again.");

        var label = string.IsNullOrWhiteSpace(name) ? null : name!.Trim();
        var entry = new Entry("w" + holder.Next++.ToString(System.Globalization.CultureInfo.InvariantCulture), text, label, prepared);
        holder.Entries.Add(entry);
        return (entry, false);
    }

    public IReadOnlyList<Entry> Entries(object owner) => _lists.TryGetValue(owner, out var holder) ? holder.Entries.ToArray() : [];

    /// <summary>Removes one entry by id, or all of them; returns the ids removed.</summary>
    public string[] Remove(object owner, string? id, bool all)
    {
        if (!all && string.IsNullOrWhiteSpace(id))
            throw new BridgeException(BridgeErrorCode.InvalidArgument, "Give the id of the entry to remove, or all = true.", "watch_list shows the ids.");

        _lists.TryGetValue(owner, out var holder);
        var entries = holder?.Entries ?? [];
        if (all)
        {
            var ids = entries.Select(e => e.Id).ToArray();
            entries.Clear();
            return ids;
        }

        var target = entries.FirstOrDefault(e => string.Equals(e.Id, id!.Trim(), StringComparison.Ordinal));
        if (target is null)
            throw new BridgeException(BridgeErrorCode.NotFound, $"No watch with id '{id}'.",
                entries.Count == 0 ? "The watch list is empty." : "Ids: " + string.Join(", ", entries.Select(e => e.Id)) + ".");
        entries.Remove(target);
        return [target.Id];
    }

    /// <summary>The owner's hold on Visual Studio ended (or it is gone): forget its list.</summary>
    public void Clear(object owner) => _lists.Remove(owner);
}
