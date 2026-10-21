using System;
using DebugViaMcp.Protocol;

namespace DebugViaMcp.Vsix.Core.Security;

public enum PermissionTier { Read, Control, Mutate }

/// <summary>Every bridge request, as the permission gate sees it.</summary>
public enum Operation
{
    Status, ListBreakpoints, SetBreakpoint, RemoveBreakpoints, Start, Stop, Continue, Pause, Step,
    Stack, Variables, Evaluate, EvaluateWithSideEffects, SetValue,

    /// <summary>bp_set with a condition or tracepoint message: that expression runs in the debuggee on every hit.</summary>
    SetBreakpointWithExpression,

    Threads, FrameSelect, Exception, EnableBreakpoint,

    /// <summary>bp_enable(true) on a breakpoint with a condition or tracepoint: that expression runs again on every hit.</summary>
    EnableBreakpointWithExpression,

    RunTo, ListProcesses,

    /// <summary>symbol_status: the symbol-related options and the breakpoints that are not bound.</summary>
    SymbolStatus,

    /// <summary>debug_attach: the debugger can read and change the attached process's memory.</summary>
    Attach,

    OutputRead, ErrorList,

    Build,

    /// <summary>Approval of a connection whose folder is not the solution's. Never reaches Visual Studio.</summary>
    Connect,

    /// <summary>vs_release: gives up the lease.</summary>
    Release,

    /// <summary>Stale-build check behind the warnings of debug_start, debug_attach and bp_set.</summary>
    BuildCheck,
}

/// <summary>User settings the bridge reads on every call, so changes apply without reconnecting.</summary>
public interface IBridgeSettings
{
    PermissionSet Permissions { get; }
    TimeSpan ConsentTimeout { get; }
    LogVerbosity LogLevel { get; }
    int LogRetentionDays { get; }

    /// <summary>Longest a tool may wait for a break, in seconds; the server clamps the agent's own wait to it.</summary>
    int MaxWaitSeconds { get; }

    /// <summary>Longest vs_build may wait, in seconds.</summary>
    int MaxBuildSeconds { get; }

    /// <summary>Hide values that look like secrets before they reach the agent. On unless the user turned it off and confirmed.</summary>
    bool MaskSensitiveValues { get; }
}

/// <summary>The single tier table (spec §3.1). New operations get their tier here.</summary>
public static class PermissionPolicy
{
    public static PermissionTier TierOf(Operation operation) => operation switch
    {
        Operation.Status or Operation.ListBreakpoints or Operation.Stack or Operation.Variables or Operation.Evaluate
            or Operation.Threads or Operation.FrameSelect or Operation.Exception
            or Operation.ListProcesses or Operation.SymbolStatus or Operation.OutputRead or Operation.ErrorList
            or Operation.Connect or Operation.Release or Operation.BuildCheck => PermissionTier.Read,
        Operation.SetBreakpoint or Operation.RemoveBreakpoints or Operation.Start or Operation.Stop
            or Operation.Continue or Operation.Pause or Operation.Step or Operation.EnableBreakpoint
            or Operation.RunTo or Operation.Build => PermissionTier.Control,
        Operation.EvaluateWithSideEffects or Operation.SetValue or Operation.SetBreakpointWithExpression
            or Operation.EnableBreakpointWithExpression or Operation.Attach => PermissionTier.Mutate,
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
        Operation.FrameSelect => "frame_select",
        Operation.Exception => "exception",
        Operation.EnableBreakpoint or Operation.EnableBreakpointWithExpression => "bp_enable",
        Operation.RunTo => "debug_run_to",
        Operation.ListProcesses or Operation.Attach => "debug_attach",
        Operation.SymbolStatus => "symbol_status",
        Operation.OutputRead => "output_read",
        Operation.ErrorList => "errors_list",
        Operation.Build => "vs_build",
        Operation.Connect => "connect",
        Operation.Release => "vs_release",
        Operation.BuildCheck => "build_check",
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Operation has no tool name."),
    };

    public static string TierName(PermissionTier tier) => tier switch
    {
        PermissionTier.Read => "read",
        PermissionTier.Control => "control",
        _ => "mutate",
    };
}
