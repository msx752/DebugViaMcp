using VsMcpDebugger.Protocol;
using VsMcpDebugger.Vsix.Core;

namespace VsMcpDebugger.Vsix.Core.Tests;

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

    public bool FileExists(string path) => FileExistsFunc(path);
    public IReadOnlyList<RawBreakpoint> Breakpoints() { Ui(); return Bps; }

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
    public void Step(StepKind kind) { Ui(); Calls.Add("Step:" + kind); }
    public IReadOnlyList<RawFrame> Stack(int max) { Ui(); return Frames.Take(max).ToList(); }
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

    public EvaluateResult Evaluate(string expression)
    {
        Ui();
        LastEvaluated = expression;
        return new EvaluateResult { IsValid = true, Value = new string('v', 3000), Type = "string" };
    }
}
