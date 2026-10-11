using StreamJsonRpc;
using VsMcpDebugger.Protocol;

namespace VsMcpDebugger.Mcp.Tests.Fakes;

/// <summary>Scriptable IDebugBridge; each member can be replaced per test.</summary>
public sealed class FakeDebugBridge : IDebugBridge
{
    public string ExpectedToken { get; set; } = "secret";
    public int ProtocolMajor { get; set; } = ProtocolInfo.Major;
    public int ProtocolMinor { get; set; } = ProtocolInfo.Minor;
    public DebuggerMode Mode { get; set; } = DebuggerMode.Break;
    public long Seq { get; set; } = 10;
    public List<string> Calls { get; } = [];
    public Exception? HandshakeFailure { get; set; }
    public int LastOffset { get; private set; } = -1;
    public bool? LastAllowSideEffects { get; private set; }

    public Func<string, string, EvaluateResult> OnSetValue { get; set; } =
        (_, v) => new EvaluateResult { IsValid = true, Value = v, Type = "decimal" };
    public List<string> CallIds { get; } = [];

    public Func<StepKind, long> OnStep { get; set; } = _ => 10;
    public Func<long> OnContinue { get; set; } = () => 10;
    public Func<BreakpointSpec, SetBreakpointResult> OnSetBreakpoint { get; set; } =
        spec => new SetBreakpointResult { Breakpoint = new BreakpointInfo { Id = "bp-1", File = spec.File, Line = spec.Line, Enabled = true } };
    public Func<string, EvaluateResult> OnEvaluate { get; set; } =
        e => new EvaluateResult { IsValid = true, Value = "42", Type = "int" };
    public Func<StatusInfo> OnStatus { get; set; }

    public Func<VariableScope, string?, int, VariableInfo[]> OnVariables { get; set; } =
        (_, _, _) => [new VariableInfo { Name = "x", Type = "int", Value = "1" }];

    public Func<ThreadInfo[]> OnThreads { get; set; } = () => [new ThreadInfo { Id = 1, Name = "Main Thread", IsCurrent = true }];
    public Func<int?, StackInfo> OnThreadStack { get; set; } = id => new StackInfo
    {
        Epoch = 3, ThreadId = id ?? 1, Frames = [new StackFrameInfo { Index = 0, Function = "A.M", File = "a.cs", Line = 1, Language = "C#" }],
    };

    public Task<ThreadInfo[]> GetThreadsAsync(CallContext context, CancellationToken cancellationToken) { CallIds.Add(context.CallId); Calls.Add("GetThreads"); return Task.FromResult(OnThreads()); }
    public Task<StackInfo> GetThreadStackAsync(int? threadId, int maxFrames, CallContext context, CancellationToken cancellationToken) { CallIds.Add(context.CallId); Calls.Add("GetThreadStack:" + threadId); return Task.FromResult(OnThreadStack(threadId)); }

    public Func<int, long, int?, FrameSelection> OnSelectFrame { get; set; } = (index, epoch, thread) => new FrameSelection
    {
        Index = index, Epoch = epoch, ThreadId = thread ?? 1, Location = new SourceLocation { File = "a.cs", Line = 5, Function = "A.M" },
    };

    public Task<FrameSelection> SelectFrameAsync(int index, long epoch, int? threadId, CallContext context, CancellationToken cancellationToken) { CallIds.Add(context.CallId); Calls.Add("SelectFrame"); return Task.FromResult(OnSelectFrame(index, epoch, threadId)); }

    public Func<ExceptionInfo?> OnException { get; set; } = () => null;
    public Task<ExceptionInfo?> GetExceptionAsync(CallContext context, CancellationToken cancellationToken) { CallIds.Add(context.CallId); Calls.Add("GetException"); return Task.FromResult(OnException()); }

    public Func<string, bool, BreakpointInfo?> OnEnable { get; set; } = (id, enabled) => new BreakpointInfo { Id = id, Enabled = enabled };
    public Task<BreakpointInfo?> SetBreakpointEnabledAsync(string id, bool enabled, CallContext context, CancellationToken cancellationToken) { CallIds.Add(context.CallId); Calls.Add("Enable:" + enabled); return Task.FromResult(OnEnable(id, enabled)); }

    public Func<string, int, RunToResult> OnRunTo { get; set; } = (_, _) => new RunToResult { Seq = 10 };
    public Task<RunToResult> RunToAsync(string file, int line, CallContext context, CancellationToken cancellationToken) { CallIds.Add(context.CallId); Calls.Add($"RunTo:{file}:{line}"); return Task.FromResult(OnRunTo(file, line)); }

    public Func<string?, ProcessInfo[]> OnListAttachable { get; set; } = _ => [new ProcessInfo { Pid = 400, Name = "OrderDemo" }];
    public Task<ProcessInfo[]> ListAttachableAsync(string? processName, CallContext context, CancellationToken cancellationToken) { CallIds.Add(context.CallId); Calls.Add("ListAttachable:" + processName); return Task.FromResult(OnListAttachable(processName)); }
    public Task<long> AttachAsync(int pid, CallContext context, CancellationToken cancellationToken) { CallIds.Add(context.CallId); Calls.Add("Attach:" + pid); return Task.FromResult(Seq); }

    public FakeDebugBridge() => OnStatus = () => new StatusInfo { Mode = Mode, Seq = Seq };

    public Task<HandshakeResponse> HandshakeAsync(HandshakeRequest request, CancellationToken cancellationToken)
    {
        Calls.Add(nameof(HandshakeAsync));
        if (request.Token != ExpectedToken)
            throw new LocalRpcException("Invalid token.") { ErrorCode = (int)BridgeErrorCode.Unauthorized };
        if (HandshakeFailure is { } failure)
            throw failure;
        return Task.FromResult(new HandshakeResponse
        {
            ProtocolMajor = ProtocolMajor, ProtocolMinor = ProtocolMinor, VsVersion = "18.10", VsixVersion = "0.1.0",
            SolutionPath = @"C:\src\OrderDemo.sln", Mode = Mode, Seq = Seq,
        });
    }

    public Task<StatusInfo> GetStatusAsync(CallContext context, CancellationToken cancellationToken) { CallIds.Add(context.CallId); Calls.Add("GetStatus"); return Task.FromResult(OnStatus()); }
    public Task<BreakpointInfo[]> ListBreakpointsAsync(CallContext context, CancellationToken cancellationToken) { CallIds.Add(context.CallId); Calls.Add("ListBreakpoints"); return Task.FromResult(Array.Empty<BreakpointInfo>()); }
    public Task<SetBreakpointResult> SetBreakpointAsync(BreakpointSpec spec, CallContext context, CancellationToken cancellationToken) { CallIds.Add(context.CallId); Calls.Add("SetBreakpoint"); return Task.FromResult(OnSetBreakpoint(spec)); }
    public Task<int> RemoveBreakpointsAsync(string? id, bool all, CallContext context, CancellationToken cancellationToken) { CallIds.Add(context.CallId); Calls.Add("RemoveBreakpoints"); return Task.FromResult(1); }
    public Task<long> StartAsync(CallContext context, CancellationToken cancellationToken) { CallIds.Add(context.CallId); Calls.Add("Start"); return Task.FromResult(Seq); }
    public Task StopAsync(CallContext context, CancellationToken cancellationToken) { CallIds.Add(context.CallId); Calls.Add("Stop"); return Task.CompletedTask; }
    public Task<long> ContinueAsync(CallContext context, CancellationToken cancellationToken) { CallIds.Add(context.CallId); Calls.Add("Continue"); return Task.FromResult(OnContinue()); }
    public Task<long> PauseAsync(CallContext context, CancellationToken cancellationToken) { CallIds.Add(context.CallId); Calls.Add("Pause"); return Task.FromResult(Seq); }
    public Task<long> StepAsync(StepKind kind, CallContext context, CancellationToken cancellationToken) { CallIds.Add(context.CallId); Calls.Add("Step:" + kind); return Task.FromResult(OnStep(kind)); }
    public Task<StackFrameInfo[]> GetStackAsync(int maxFrames, CallContext context, CancellationToken cancellationToken) { CallIds.Add(context.CallId); Calls.Add("GetStack"); return Task.FromResult(new[] { new StackFrameInfo { Index = 0, Function = "A.M", File = "a.cs", Line = 1, Language = "C#" } }); }
    public Task<VariableInfo[]> GetVariablesAsync(VariableScope scope, string? path, int offset, int maxItems, CallContext context, CancellationToken cancellationToken) { CallIds.Add(context.CallId); Calls.Add("GetVariables"); LastOffset = offset; return Task.FromResult(OnVariables(scope, path, maxItems)); }
    public Task<EvaluateResult> SetValueAsync(string expression, string value, CallContext context, CancellationToken cancellationToken) { CallIds.Add(context.CallId); Calls.Add("SetValue"); return Task.FromResult(OnSetValue(expression, value)); }
    public Task<EvaluateResult> EvaluateAsync(string expression, bool allowSideEffects, CallContext context, CancellationToken cancellationToken) { CallIds.Add(context.CallId); Calls.Add("Evaluate"); LastAllowSideEffects = allowSideEffects; return Task.FromResult(OnEvaluate(expression)); }
}
