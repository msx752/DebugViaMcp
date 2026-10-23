using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using DebugViaMcp.Mcp.Connection;
using DebugViaMcp.Protocol;

namespace DebugViaMcp.Mcp.Tools;

/// <summary>Visual Studio's Exception Settings window: which exceptions the debugger stops on when they are thrown.</summary>
[McpServerToolType]
public sealed class ExceptionSettingsTools(IConnectionManager connections)
{
    internal const int MaxLimit = 500;
    internal const int MaxNames = 50;

    [McpServerTool(Name = "exception_list", ReadOnly = true),
     Description("Lists Visual Studio's Exception Settings (Debug > Windows > Exception Settings): for each exception whether Break When Thrown is on. By default only the exceptions that are on, grouped as Visual Studio groups them (for example 'Common Language Runtime Exceptions'). The CLR group alone has hundreds of entries, so use filter (part of the name, case-insensitive) or group to look for one. 'exception' shows the exception of the current break; this tool shows what the debugger will stop on.")]
    public Task<CallToolResult> ExceptionList(
        [Description("Group name exactly as Visual Studio shows it. Omit to list every group that has a match.")] string? group = null,
        [Description("Part of an exception name, case-insensitive.")] string? filter = null,
        [Description("True (default): only exceptions with Break When Thrown on. False: all of them.")] bool onlyEnabled = true,
        [Description("Most entries to return over all groups (1-500).")] int limit = 100,
        CancellationToken ct = default) => ToolResponse.RunAsync(connections, "exception_list", async ctx =>
    {
        var bridge = await connections.GetBridgeAsync(ct).ConfigureAwait(false);
        if (ToolResponse.RequireMinor(connections, ProtocolInfo.MinorExceptionSettings) is { } unsupported) return unsupported;

        var page = await bridge.ListExceptionSettingsAsync(
            string.IsNullOrWhiteSpace(group) ? null : group, string.IsNullOrEmpty(filter) ? null : filter, onlyEnabled, Math.Min(Math.Max(limit, 1), MaxLimit), ctx, ct).ConfigureAwait(false);
        return ToolResponse.Ok(new
        {
            groups = page.Groups,
            hint = page.Groups.Any(g => g.Truncated) ? "More entries match than were returned: narrow it with group or filter, or raise limit (at most 500)." : null,
        });
    });

    [McpServerTool(Name = "exception_set", Idempotent = true),
     Description("Turns Break When Thrown on or off for exceptions in Visual Studio's Exception Settings, so the debugger stops where they are thrown. Names are exact and case-sensitive (find them with exception_list); all names are checked first and nothing changes if one is unknown. This changes a Visual Studio setting, not the program; call exception_reset when you are done to put back what you changed. Depending on the user's Options (Control tools are allowed by default) the user may be asked first.")]
    public Task<CallToolResult> ExceptionSet(
        [Description("Group name exactly as Visual Studio shows it, for example 'Common Language Runtime Exceptions'.")] string group,
        [Description("Exact exception names, for example System.NullReferenceException (at most 50).")] string[] names,
        [Description("True (default) to break when thrown, false to stop breaking.")] bool breakWhenThrown = true,
        CancellationToken ct = default) => ToolResponse.RunAsync(connections, "exception_set", async ctx =>
    {
        if (string.IsNullOrWhiteSpace(group)) return ToolResponse.Error(BridgeErrorCode.InvalidArgument, "group is required.", "Call exception_list to see the group names.");
        if (names is null || names.Length == 0 || names.Any(string.IsNullOrWhiteSpace)) return ToolResponse.Error(BridgeErrorCode.InvalidArgument, "names must list at least one exception name, none of them empty.");
        if (names.Length > MaxNames) return ToolResponse.Error(BridgeErrorCode.InvalidArgument, $"At most {MaxNames} names per call.");

        var bridge = await connections.GetBridgeAsync(ct).ConfigureAwait(false);
        if (ToolResponse.RequireMinor(connections, ProtocolInfo.MinorExceptionSettings) is { } unsupported) return unsupported;

        var result = await bridge.SetExceptionSettingsAsync(group, names, breakWhenThrown, ctx, ct).ConfigureAwait(false);
        return ToolResponse.Ok(new
        {
            group = result.Group,
            changes = result.Changes,
            hint = result.Changes.Any(c => c.Changed) ? "Call exception_reset when you are done to put these back." : null,
        });
    });

    [McpServerTool(Name = "exception_reset", Idempotent = true),
     Description("Puts back the Exception Settings that exception_set changed in this session (only those; the user's own settings are not touched). Safe to call when nothing was changed.")]
    public Task<CallToolResult> ExceptionReset(CancellationToken ct = default) => ToolResponse.RunAsync(connections, "exception_reset", async ctx =>
    {
        var bridge = await connections.GetBridgeAsync(ct).ConfigureAwait(false);
        if (ToolResponse.RequireMinor(connections, ProtocolInfo.MinorExceptionSettings) is { } unsupported) return unsupported;

        var result = await bridge.ResetExceptionSettingsAsync(ctx, ct).ConfigureAwait(false);
        return ToolResponse.Ok(new { restored = result.Restored });
    });
}
