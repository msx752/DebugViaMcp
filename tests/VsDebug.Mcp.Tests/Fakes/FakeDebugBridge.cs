using VsDebug.Protocol;

namespace VsDebug.Mcp.Tests.Fakes;

/// <summary>Scriptable IDebugBridge; each member can be replaced per test.</summary>
public sealed class FakeDebugBridge : IDebugBridge
{
    public string ExpectedToken { get; set; } = "secret";
    public int ProtocolMajor { get; set; } = ProtocolInfo.Major;
    public DebuggerMode Mode { get; set; } = DebuggerMode.Break;
    public long Seq { get; set; } = 10;
    public List<string> Calls { get; } = [];

    public Func<StepKind, long> OnStep { get; set; } = _ => 10;
    public Func<long> OnContinue { get; set; } = () => 10;
    public Func<BreakpointSpec, SetBreakpointResult> OnSetBreakpoint { get; set; } =
        spec => new SetBreakpointResult { Breakpoint = new BreakpointInfo { Id = "bp-1", File = spec.File, Line = spec.Line, Enabled = true } };
    public Func<string, EvaluateResult> OnEvaluate { get; set; } =
        e => new EvaluateResult { IsValid = true, Value = "42", Type = "int" };
    public Func<VariableScope, string?, int, VariableInfo[]> OnVariables { get; set; } =
        (_, _, _) => [new VariableInfo { Name = "x", Type = "int", Value = "1" }];

    public Task<HandshakeResponse> HandshakeAsync(HandshakeRequest request, CancellationToken cancellationToken)
    {
        Calls.Add(nameof(HandshakeAsync));
        if (request.Token != ExpectedToken)
            throw new InvalidOperationException("bad token");
        return Task.FromResult(new HandshakeResponse
        {
            ProtocolMajor = ProtocolMajor, ProtocolMinor = 0, VsVersion = "18.10", VsixVersion = "0.1.0",
            SolutionPath = @"C:\src\OrderDemo.sln", Mode = Mode, Seq = Seq,
        });
    }

    public Task<StatusInfo> GetStatusAsync(CancellationToken cancellationToken) { Calls.Add("GetStatus"); return Task.FromResult(new StatusInfo { Mode = Mode, Seq = Seq }); }
    public Task<BreakpointInfo[]> ListBreakpointsAsync(CancellationToken cancellationToken) { Calls.Add("ListBreakpoints"); return Task.FromResult(Array.Empty<BreakpointInfo>()); }
    public Task<SetBreakpointResult> SetBreakpointAsync(BreakpointSpec spec, CancellationToken cancellationToken) { Calls.Add("SetBreakpoint"); return Task.FromResult(OnSetBreakpoint(spec)); }
    public Task<int> RemoveBreakpointsAsync(string? id, bool all, CancellationToken cancellationToken) { Calls.Add("RemoveBreakpoints"); return Task.FromResult(1); }
    public Task<long> StartAsync(CancellationToken cancellationToken) { Calls.Add("Start"); return Task.FromResult(Seq); }
    public Task StopAsync(CancellationToken cancellationToken) { Calls.Add("Stop"); return Task.CompletedTask; }
    public Task<long> ContinueAsync(CancellationToken cancellationToken) { Calls.Add("Continue"); return Task.FromResult(OnContinue()); }
    public Task<long> PauseAsync(CancellationToken cancellationToken) { Calls.Add("Pause"); return Task.FromResult(Seq); }
    public Task<long> StepAsync(StepKind kind, CancellationToken cancellationToken) { Calls.Add("Step:" + kind); return Task.FromResult(OnStep(kind)); }
    public Task<StackFrameInfo[]> GetStackAsync(int maxFrames, CancellationToken cancellationToken) { Calls.Add("GetStack"); return Task.FromResult(new[] { new StackFrameInfo { Index = 0, Function = "A.M", File = "a.cs", Line = 1, Language = "C#" } }); }
    public Task<VariableInfo[]> GetVariablesAsync(VariableScope scope, string? path, int maxItems, CancellationToken cancellationToken) { Calls.Add("GetVariables"); return Task.FromResult(OnVariables(scope, path, maxItems)); }
    public Task<EvaluateResult> EvaluateAsync(string expression, CancellationToken cancellationToken) { Calls.Add("Evaluate"); return Task.FromResult(OnEvaluate(expression)); }
}
