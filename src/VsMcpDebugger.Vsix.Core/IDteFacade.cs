using System;
using System.Collections.Generic;
using VsMcpDebugger.Protocol;

namespace VsMcpDebugger.Vsix.Core;

public sealed class RawBreakpoint
{
    public string? File { get; set; }
    public int? Line { get; set; }
    public string? Function { get; set; }
    public string? Condition { get; set; }
    public ConditionKind ConditionKind { get; set; }
    public int HitCount { get; set; }
    public HitCountKind HitCountKind { get; set; }
    public string? LogMessage { get; set; }
    public bool Enabled { get; set; }
    public int BoundCount { get; set; }
}

public sealed class RawFrame
{
    public string Function { get; set; } = "";
    public string? File { get; set; }
    public int? Line { get; set; }
    public string Language { get; set; } = "";
}

public sealed class RawThread
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Location { get; set; }
}

public sealed class RawExceptionInfo
{
    public string Type { get; set; } = "";
    public string Message { get; set; } = "";
    public string? StackTrace { get; set; }
    public List<(string Type, string Message)> Inner { get; set; } = [];
}

public sealed class RawProcess
{
    public int Pid { get; set; }

    /// <summary>File name without extension, e.g. "FaultDemo".</summary>
    public string Name { get; set; } = "";

    /// <summary>Owner compared by token SID with the user running Visual Studio (also when VS runs elevated).</summary>
    public bool OwnedByCurrentUser { get; set; }
}

public sealed class RawOutputPane
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>Number of lines, not counting the empty line after a trailing newline. Null when Visual Studio has no text document for the pane yet.</summary>
    public int? Lines { get; set; }
}

public sealed class RawErrorItem
{
    public ErrorSeverity Severity { get; set; }
    public string? Code { get; set; }
    public string Message { get; set; } = "";
    public string? Project { get; set; }

    /// <summary>Absolute path, or null for entries without a file.</summary>
    public string? File { get; set; }

    /// <summary>1-based.</summary>
    public int? Line { get; set; }

    /// <summary>1-based.</summary>
    public int? Column { get; set; }
    public bool FromBuild { get; set; }
}

/// <summary>Everything the bridge needs from Visual Studio. All members must be called on the UI thread.</summary>
public interface IDteFacade
{
    string VsVersion { get; }
    string? SolutionPath { get; }
    DebuggerMode Mode { get; }
    RawBreakpoint? LastHitBreakpoint { get; }
    bool FileExists(string path);
    IReadOnlyList<RawBreakpoint> Breakpoints();
    void AddBreakpoint(BreakpointSpec spec);
    int RemoveBreakpoints(Func<RawBreakpoint, bool> match);

    /// <returns>The first matching breakpoint after the change, or null when none matches.</returns>
    RawBreakpoint? SetEnabled(Func<RawBreakpoint, bool> match, bool enabled);
    void Go();
    void Break();
    void Stop();
    void Step(StepKind kind);

    /// <summary>Debugger.LocalProcesses without Visual Studio itself.</summary>
    IReadOnlyList<RawProcess> LocalProcesses();
    void Attach(int pid);
    void DetachAll();
    IReadOnlyList<RawFrame> Stack(int max);
    int? CurrentThreadId { get; }
    IReadOnlyList<RawThread> Threads();

    /// <returns>Null when no thread has that id.</returns>
    IReadOnlyList<RawFrame>? ThreadStack(int threadId, int max);

    /// <summary>Index of Visual Studio's selected frame within the current thread's stack (0 = top).</summary>
    int CurrentFrameIndex { get; }

    /// <summary>Makes the frame current (and the thread, when given). The caller has validated both exist.</summary>
    void SelectFrame(int? threadId, int index);

    /// <summary>$exception of the current break with up to <paramref name="maxInner"/> inner exceptions; null when there is none.</summary>
    RawExceptionInfo? CurrentException(int maxInner);

    IReadOnlyList<RawOutputPane> OutputPanes();

    /// <summary>Lines <paramref name="firstLine"/> (1-based) to <paramref name="firstLine"/> + <paramref name="count"/> - 1; reads only that range.</summary>
    IReadOnlyList<string> OutputLines(Guid pane, int firstLine, int count);

    IReadOnlyList<RawErrorItem> ErrorItems();

    bool IsBuilding { get; }

    /// <summary>UniqueName of every project in the solution (solution folders flattened).</summary>
    IReadOnlyList<string> ProjectNames();

    /// <summary>Source and output times of one project (unique name) for the stale-build check.</summary>
    ProjectBuildInfo GetBuildInfo(string uniqueName);

    /// <summary>Unique names of the projects that contain <paramref name="file"/> (absolute path); empty when none does.</summary>
    IReadOnlyList<string> ProjectsContaining(string file);

    /// <summary>Unique names of the solution's startup projects (several with "Multiple startup projects"); empty when none. Setting it changes the solution's setting.</summary>
    IReadOnlyList<string> StartupProjects { get; set; }

    /// <summary>True when <paramref name="uniqueName"/> is a project Visual Studio can start (an executable or web project, not a library).</summary>
    bool IsLaunchable(string uniqueName);

    /// <summary>Cleans and builds one project (unique name) without waiting. Used by the owner's click on the Rebuild bar.</summary>
    void RebuildProject(string uniqueName);

    /// <summary>Starts a build of the solution (null) or one project, active configuration, without waiting.</summary>
    void StartBuild(string? projectUniqueName);
    IReadOnlyList<VariableInfo> Variables(VariableScope scope, int maxItems);
    IReadOnlyList<VariableInfo> Members(string expression, int maxItems);
    EvaluateResult Evaluate(string expression);

    /// <summary>Assigns <paramref name="value"/> to <paramref name="expression"/> and reads it back. Invalid result when VS rejects it.</summary>
    EvaluateResult SetValue(string expression, string value);
}
