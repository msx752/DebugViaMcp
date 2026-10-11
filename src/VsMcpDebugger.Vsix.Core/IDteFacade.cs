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
    void Go();
    void Break();
    void Stop();
    void Step(StepKind kind);
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
    IReadOnlyList<VariableInfo> Variables(VariableScope scope, int maxItems);
    IReadOnlyList<VariableInfo> Members(string expression, int maxItems);
    EvaluateResult Evaluate(string expression);

    /// <summary>Assigns <paramref name="value"/> to <paramref name="expression"/> and reads it back. Invalid result when VS rejects it.</summary>
    EvaluateResult SetValue(string expression, string value);
}
