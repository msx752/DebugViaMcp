using DebugViaMcp.Mcp.Connection;
using DebugViaMcp.Protocol;

namespace DebugViaMcp.Mcp.Tools;

/// <summary>
/// Opens a few levels of an object for one <c>vars</c> call, so the agent does not need a call per level. Level by level, so a wide
/// first level cannot use up the budget before the second one is seen; whatever does not fit stays closed (<c>hasChildren</c> still says it can be opened).
/// </summary>
internal sealed class VariableTree(Func<string, int, CancellationToken, Task<VariableInfo[]>> readMembers, int charBudget)
{
    public const int MaxDepth = 4;
    public const int ChildrenPerNode = 20;
    public const int MaxNodes = 150;
    public const int MaxExpandPaths = 5;
    public const int MaxExpandItems = 50;

    private int nodes;
    private int chars = charBudget;

    /// <summary>True when the size limit left something closed that could have been opened.</summary>
    public bool Closed { get; private set; }

    public static int CostOf(VariableInfo v) => v.Name.Length + v.Type.Length + v.Value.Length + 96;

    /// <summary>Opens <paramref name="depth"/> - 1 levels under <paramref name="items"/>, which were read under <paramref name="basePath"/> (null: the frame's own variables).</summary>
    public async Task OpenAsync(string? basePath, IReadOnlyList<VariableInfo> items, int depth, CancellationToken ct)
    {
        var level = items.Select(i => (Parent: basePath, Item: i)).ToList();
        for (var d = 1; d < Math.Min(depth, MaxDepth) && level.Count > 0; d++)
        {
            var next = new List<(string? Parent, VariableInfo Item)>();
            foreach (var (parent, item) in level)
            {
                if (!item.HasChildren || ChildPath(parent, item.Name) is not { } path) continue;
                if (nodes >= MaxNodes || chars <= 0) { Closed = true; continue; }

                var members = await TryReadAsync(path, ct).ConfigureAwait(false);
                if (members is null) continue;

                var kids = new List<VariableInfo>();
                foreach (var kid in members.Take(ChildrenPerNode))
                {
                    if (nodes >= MaxNodes || chars <= 0) { Closed = true; break; }
                    kids.Add(kid);
                    nodes++;
                    chars -= CostOf(kid);
                    next.Add((path, kid));
                }

                item.Children = kids.ToArray();
                if (kids.Count < members.Length) item.ChildrenTruncated = true;
            }

            level = next;
        }
    }

    /// <summary>The first <paramref name="max"/> members of a named path; Visual Studio's refusal comes through as the bridge exception.</summary>
    public Task<VariableInfo[]> ReadAsync(string path, int max, CancellationToken ct) => readMembers(path, max, ct);

    /// <summary>The members of one named path; null when Visual Studio cannot read it (a wrong path, a member that throws): the node stays closed.</summary>
    public async Task<VariableInfo[]?> TryReadAsync(string path, CancellationToken ct)
    {
        try
        {
            return await readMembers(path, ChildrenPerNode + 1, ct).ConfigureAwait(false);
        }
        catch (McpBridgeException e) when (e.Code is BridgeErrorCode.EvalFailed or BridgeErrorCode.InvalidArgument or BridgeErrorCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// The expression that reads the members of <paramref name="name"/> under <paramref name="parent"/>. Null for what is no expression:
    /// the debugger's groups ("Non-Public members", "Raw View") and compiler-made names.
    /// </summary>
    internal static string? ChildPath(string? parent, string name)
    {
        if (name.Length == 0) return null;
        if (name[0] == '[') return parent is null ? null : parent + name;
        if (!(char.IsLetter(name[0]) || name[0] == '_')) return null;
        foreach (var c in name)
            if (!(char.IsLetterOrDigit(c) || c == '_')) return null;
        return parent is null ? name : parent + "." + name;
    }
}
