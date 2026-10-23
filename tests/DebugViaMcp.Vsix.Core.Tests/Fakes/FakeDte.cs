using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core.Abstractions;
using DebugViaMcp.Vsix.Core.Bridge;
using DebugViaMcp.Vsix.Core.Builds;
using DebugViaMcp.Vsix.Core.Debugging;

namespace DebugViaMcp.Vsix.Core.Tests.Fakes;

public sealed class FakeDte : IDteFacade
{
    private string? _solutionPath = @"C:\src\Demo\Demo.sln";

    /// <summary>When true, VS state may only be read inside IUiThread.RunAsync, like real DTE.</summary>
    public bool EnforceUiThread { get; set; }

    public string VsVersion { get { Ui(); return "18.10"; } }

    public string? SolutionPath
    {
        get { Ui(); return _solutionPath; }
        set => _solutionPath = value;
    }

    private void Ui()
    {
        if (EnforceUiThread && !InlineUiThread.OnUiThread)
            throw new InvalidOperationException("DTE must be called on the UI thread.");
    }
    private DebuggerMode _mode = DebuggerMode.Break;

    public DebuggerMode Mode
    {
        get { Ui(); return _mode; }
        set => _mode = value;
    }

    public List<RawBreakpoint> Bps { get; } = [];
    public List<string> Calls { get; } = [];
    public List<RawFrame> Frames { get; set; } = [new() { Function = "Demo.OrderService.Submit(Demo.Order order)", File = @"C:\src\Demo\OrderService.cs", Line = 9, Language = "C#" }];
    public List<VariableInfo> Locals { get; set; } = [];
    public string? LastEvaluated { get; private set; }
    public RawBreakpoint? LastHitBreakpoint { get; set; }
    public Func<string, bool> FileExistsFunc { get; set; } = _ => true;

    public int? CurrentThreadId { get; set; } = 1;
    public List<RawThread> ThreadList { get; set; } = [new() { Id = 1, Name = "Main Thread", Location = "Demo.OrderService.Submit" }];
    public Dictionary<int, List<RawFrame>> ThreadStacks { get; } = [];

    public IReadOnlyList<RawThread> Threads() { Ui(); return ThreadList; }

    public RawExceptionInfo? ThrownException { get; set; }

    public RawExceptionInfo? CurrentException(int maxInner)
    {
        Ui();
        if (ThrownException is not { } e) return null;
        return new RawExceptionInfo { Type = e.Type, Message = e.Message, StackTrace = e.StackTrace, Inner = e.Inner.Take(maxInner).ToList() };
    }

    public RawBreakpoint? SetEnabled(Func<RawBreakpoint, bool> match, bool enabled)
    {
        Ui();
        var bp = Bps.FirstOrDefault(match);
        if (bp is not null) bp.Enabled = enabled;
        return bp;
    }

    public List<RawProcess> Processes { get; set; } =
    [
        new() { Pid = 100, Name = "FaultDemo", OwnedByCurrentUser = true },
        new() { Pid = 200, Name = "FaultDemo", OwnedByCurrentUser = true },
        new() { Pid = 300, Name = "lsass", OwnedByCurrentUser = false },
        new() { Pid = 400, Name = "OrderDemo", OwnedByCurrentUser = true },
    ];

    public string? Dialog { get; set; }
    public int ModalDialogChecks { get; private set; }
    public string? ModalDialogTitle() { ModalDialogChecks++; return Dialog; }   // any thread, like the real one

    public RawDebuggerOptions Options { get; set; } = new();
    public RawDebuggerOptions DebuggerOptions() { Ui(); return Options; }

    public IReadOnlyList<RawProcess> LocalProcesses() { Ui(); return Processes; }
    public List<RawExceptionGroup> ExceptionGroupsData { get; } = [];
    public IReadOnlyList<RawExceptionGroup> ExceptionGroups() { Ui(); return ExceptionGroupsData; }
    public void SetBreakWhenThrown(string group, string name, bool value)
    {
        Ui();
        var setting = ExceptionGroupsData.FirstOrDefault(g => g.Name == group)?.Settings.FirstOrDefault(s => s.Name == name)
            ?? throw new BridgeException(BridgeErrorCode.NotFound, $"No exception '{name}' in '{group}'.");
        Calls.Add($"SetBreakWhenThrown:{group}:{name}:{value}");
        ((RawExceptionSetting)setting).BreakWhenThrown = value;
    }

    public Action<int>? OnAttach { get; set; }
    public void Attach(int pid) { Ui(); Calls.Add("Attach:" + pid); OnAttach?.Invoke(pid); }
    public void DetachAll() { Ui(); Calls.Add("DetachAll"); }

    public List<RawOutputPane> Panes { get; set; } =
    [
        new() { Id = OutputPaneIds.Build, Name = "Build", Lines = 3 },
        new() { Id = OutputPaneIds.Log, Name = "Debugging via MCP Log", Lines = 4 },
        new() { Id = OutputPaneIds.Activity, Name = "Debugging via MCP Audit", Lines = 2 },
    ];

    public List<(Guid Pane, int First, int Count)> OutputReads { get; } = [];

    public IReadOnlyList<RawOutputPane> OutputPanes() { Ui(); return Panes; }

    public Func<int, string>? OutputLineText { get; set; }

    public IReadOnlyList<string> OutputLines(Guid pane, int firstLine, int count)
    {
        Ui();
        OutputReads.Add((pane, firstLine, count));
        return Enumerable.Range(firstLine, count).Select(n => OutputLineText?.Invoke(n) ?? "line " + n).ToList();
    }

    public List<RawErrorItem> Errors { get; set; } = [];
    public IReadOnlyList<RawErrorItem> ErrorItems() { Ui(); return Errors; }

    public bool IsBuilding { get; set; }
    public List<string> Projects { get; set; } = [@"samples\OrderDemo\OrderDemo.csproj", @"samples\FaultDemo\FaultDemo.csproj"];
    public IReadOnlyList<string> ProjectNames() { Ui(); return Projects; }
    public void StartBuild(string? projectUniqueName) { Ui(); Calls.Add("StartBuild:" + projectUniqueName); IsBuilding = true; }

    public int CurrentFrameIndex { get; set; }
    public List<(int? ThreadId, int Index)> Selections { get; } = [];

    public void SelectFrame(int? threadId, int index)
    {
        Ui();
        Selections.Add((threadId, index));
        if (threadId is { } id) CurrentThreadId = id;
        CurrentFrameIndex = index;
    }

    public IReadOnlyList<RawFrame>? ThreadStack(int threadId, int max)
    {
        Ui();
        return ThreadStacks.TryGetValue(threadId, out var frames) ? frames.Take(max).ToList() : null;
    }

    public bool FileExists(string path) => FileExistsFunc(path);
    public IReadOnlyList<RawBreakpoint> Breakpoints() { Ui(); return Bps; }

    public void AddBreakpoint(BreakpointSpec spec) => Bps.Add(new RawBreakpoint
    {
        File = spec.File, Line = spec.Line, Function = spec.Function, Condition = spec.Condition, ConditionKind = spec.ConditionKind,
        HitCount = spec.HitCount, HitCountKind = spec.HitCountKind, LogMessage = spec.LogMessage, Enabled = true,
        BoundCount = BoundCountFor?.Invoke(spec) ?? (Mode == DebuggerMode.Design ? 0 : 1),
    });

    public Func<BreakpointSpec, int>? BoundCountFor { get; set; }

    public int RemoveBreakpoints(Func<RawBreakpoint, bool> match) => Bps.RemoveAll(b => match(b));
    public void Go()
    {
        Calls.Add("Go");
        StartupAtGo.Add(_startupProjects.Count > 0 ? _startupProjects[0] : null);
        OnGo?.Invoke();
    }

    private IReadOnlyList<string> _startupProjects = [];

    public IReadOnlyList<string> StartupProjects
    {
        get { Ui(); return _startupProjects; }
        set { Ui(); StartupHistory.Add(value.Count > 0 ? value[0] : null); _startupProjects = value; }
    }

    /// <summary>Single-project view of <see cref="StartupProjects"/> for tests.</summary>
    public string? StartupProject
    {
        get => StartupProjects.Count > 0 ? StartupProjects[0] : null;
        set => StartupProjects = value is null ? [] : [value];
    }

    /// <summary>Every assignment to <see cref="StartupProject"/>, in order.</summary>
    public List<string?> StartupHistory { get; } = [];

    /// <summary>The startup project at the moment of each <see cref="Go"/>.</summary>
    public List<string?> StartupAtGo { get; } = [];

    public Action? OnGo { get; set; }

    /// <summary>Projects that can be started; null means all of them.</summary>
    public List<string>? LaunchableProjects { get; set; }

    /// <summary>Build state per project; a project that is not listed counts as freshly built.</summary>
    public Dictionary<string, ProjectBuildInfo> BuildInfos { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int GetBuildInfoCalls { get; private set; }

    public ProjectBuildInfo GetBuildInfo(string uniqueName)
    {
        Ui();
        GetBuildInfoCalls++;
        return BuildInfos.TryGetValue(uniqueName, out var info)
            ? info
            : new ProjectBuildInfo { UniqueName = uniqueName, NewestSourceUtc = null, OldestOutputUtc = DateTime.UtcNow };
    }

    public Dictionary<string, List<string>> FileProjects { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> ProjectsContainingCalls { get; } = [];

    public IReadOnlyList<string> ProjectsContaining(string file)
    {
        Ui();
        ProjectsContainingCalls.Add(file);
        return FileProjects.TryGetValue(file, out var projects) ? projects : [];
    }


    public bool IsLaunchable(string uniqueName) { Ui(); return LaunchableProjects?.Contains(uniqueName) ?? true; }
    public void Break() => Calls.Add("Break");
    public void Stop() => Calls.Add("Stop");
    public void Step(StepKind kind) { Ui(); Calls.Add("Step:" + kind); }
    /// <summary>When set, Stack() waits for it (signalling <see cref="StackEntered"/>), to hold a call on the "UI thread".</summary>
    public ManualResetEventSlim? StackGate { get; set; }
    public TaskCompletionSource StackEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<RawFrame> Stack(int max)
    {
        Ui();
        if (StackGate is { } gate)
        {
            StackEntered.TrySetResult();
            gate.Wait(TimeSpan.FromSeconds(10));
        }

        return Frames.Take(max).ToList();
    }
    public IReadOnlyList<VariableInfo> Variables(VariableScope scope, int maxItems) { Ui(); return Locals.Take(maxItems).ToList(); }

    public IReadOnlyList<VariableInfo> Members(string expression, int maxItems)
    {
        LastEvaluated = expression;
        return Locals.Take(maxItems).ToList();
    }

    public List<(string Expression, string Value)> Assignments { get; } = [];

    public Func<string, string, EvaluateResult> OnSetValue { get; set; } =
        (_, value) => new EvaluateResult { IsValid = true, Value = value, Type = "decimal" };

    public EvaluateResult SetValue(string expression, string value)
    {
        Ui();
        Assignments.Add((expression, value));
        return OnSetValue(expression, value);
    }

    public Func<string, EvaluateResult>? OnEvaluate { get; set; }

    public EvaluateResult Evaluate(string expression)
    {
        Ui();
        LastEvaluated = expression;
        return OnEvaluate?.Invoke(expression) ?? new EvaluateResult { IsValid = true, Value = new string('v', 3000), Type = "string" };
    }
}
