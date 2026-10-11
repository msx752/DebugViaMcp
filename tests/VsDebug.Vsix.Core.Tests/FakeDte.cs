using VsDebug.Protocol;
using VsDebug.Vsix.Core;

namespace VsDebug.Vsix.Core.Tests;

public sealed class FakeDte : IDteFacade
{
    public string VsVersion => "18.10";
    public string? SolutionPath { get; set; } = @"C:\src\Demo\Demo.sln";
    public DebuggerMode Mode { get; set; } = DebuggerMode.Break;
    public List<RawBreakpoint> Bps { get; } = [];
    public List<string> Calls { get; } = [];
    public List<RawFrame> Frames { get; set; } = [new() { Function = "Demo.OrderService.Submit(Demo.Order order)", File = @"C:\src\Demo\OrderService.cs", Line = 9, Language = "C#" }];
    public List<VariableInfo> Locals { get; set; } = [];
    public string? LastEvaluated { get; private set; }
    public RawBreakpoint? LastHitBreakpoint { get; set; }
    public Func<string, bool> FileExistsFunc { get; set; } = _ => true;

    public bool FileExists(string path) => FileExistsFunc(path);
    public IReadOnlyList<RawBreakpoint> Breakpoints() => Bps;

    public void AddBreakpoint(BreakpointSpec spec) => Bps.Add(new RawBreakpoint
    {
        File = spec.File, Line = spec.Line, Function = spec.Function, Condition = spec.Condition, ConditionKind = spec.ConditionKind,
        HitCount = spec.HitCount, HitCountKind = spec.HitCountKind, LogMessage = spec.LogMessage, Enabled = true,
        BoundCount = Mode == DebuggerMode.Design ? 0 : 1,
    });

    public int RemoveBreakpoints(Func<RawBreakpoint, bool> match) => Bps.RemoveAll(b => match(b));
    public void Go() => Calls.Add("Go");
    public void Break() => Calls.Add("Break");
    public void Stop() => Calls.Add("Stop");
    public void Step(StepKind kind) => Calls.Add("Step:" + kind);
    public IReadOnlyList<RawFrame> Stack(int max) => Frames.Take(max).ToList();
    public IReadOnlyList<VariableInfo> Variables(VariableScope scope, int maxItems) => Locals.Take(maxItems).ToList();

    public IReadOnlyList<VariableInfo> Members(string expression, int maxItems)
    {
        LastEvaluated = expression;
        return Locals.Take(maxItems).ToList();
    }

    public EvaluateResult Evaluate(string expression)
    {
        LastEvaluated = expression;
        return new EvaluateResult { IsValid = true, Value = new string('v', 3000), Type = "string" };
    }
}
