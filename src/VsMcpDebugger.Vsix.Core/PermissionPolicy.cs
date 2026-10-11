using System;
using VsMcpDebugger.Protocol;

namespace VsMcpDebugger.Vsix.Core;

public enum PermissionTier { Read, Control, Mutate }

/// <summary>Every bridge request, as the permission gate sees it.</summary>
public enum Operation
{
    Status, ListBreakpoints, SetBreakpoint, RemoveBreakpoints, Start, Stop, Continue, Pause, Step,
    Stack, Variables, Evaluate, EvaluateWithSideEffects, SetValue,

    /// <summary>bp_set with a condition or tracepoint message: that expression runs in the debuggee on every hit.</summary>
    SetBreakpointWithExpression,

    Threads,
}

/// <summary>User settings the bridge reads on every call, so changes apply without reconnecting.</summary>
public interface IBridgeSettings
{
    PermissionSet Permissions { get; }
    TimeSpan ConsentTimeout { get; }
    LogVerbosity LogLevel { get; }
}

/// <summary>The single tier table (spec §3.1). New operations get their tier here.</summary>
public static class PermissionPolicy
{
    public static PermissionTier TierOf(Operation operation) => operation switch
    {
        Operation.Status or Operation.ListBreakpoints or Operation.Stack or Operation.Variables or Operation.Evaluate
            or Operation.Threads => PermissionTier.Read,
        Operation.SetBreakpoint or Operation.RemoveBreakpoints or Operation.Start or Operation.Stop
            or Operation.Continue or Operation.Pause or Operation.Step => PermissionTier.Control,
        Operation.EvaluateWithSideEffects or Operation.SetValue or Operation.SetBreakpointWithExpression => PermissionTier.Mutate,
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Operation has no tier."),
    };

    public static PermissionLevel Decide(Operation operation, PermissionSet permissions)
    {
        if (permissions is null) throw new ArgumentNullException(nameof(permissions));
        return TierOf(operation) switch
        {
            PermissionTier.Read => permissions.Read,
            PermissionTier.Control => permissions.Control,
            _ => permissions.Mutate,
        };
    }

    public static string ToolName(Operation operation) => operation switch
    {
        Operation.Status => "debug_status",
        Operation.ListBreakpoints => "bp_list",
        Operation.SetBreakpoint or Operation.SetBreakpointWithExpression => "bp_set",
        Operation.RemoveBreakpoints => "bp_remove",
        Operation.Start => "debug_start",
        Operation.Stop => "debug_stop",
        Operation.Continue => "debug_continue",
        Operation.Pause => "debug_pause",
        Operation.Step => "debug_step",
        Operation.Stack => "stack",
        Operation.Variables => "vars",
        Operation.Evaluate or Operation.EvaluateWithSideEffects => "eval",
        Operation.SetValue => "set_value",
        Operation.Threads => "threads",
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Operation has no tool name."),
    };

    public static string TierName(PermissionTier tier) => tier switch
    {
        PermissionTier.Read => "read",
        PermissionTier.Control => "control",
        _ => "mutate",
    };
}
