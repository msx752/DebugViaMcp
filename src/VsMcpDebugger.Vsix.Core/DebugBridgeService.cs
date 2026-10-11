using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VsMcpDebugger.Protocol;

namespace VsMcpDebugger.Vsix.Core;

/// <summary>Implements the bridge operations on top of <see cref="IDteFacade"/>. Handshake lives in BridgeConnectionHandler.</summary>
public sealed class DebugBridgeService(IDteFacade dte, IUiThread ui, EventSeq seq, ILog log, string vsixVersion)
{
    /// <summary>Enforced here, not trusted from the client: deep offsets would enumerate members on the UI thread.</summary>
    public const int MaxVariableOffset = 100_000;

    /// <summary>The MCP layer asks for at most 200 items + 1 to detect more; anything above is capped.</summary>
    public const int MaxVariablePage = 201;

    public string VsixVersion => vsixVersion;
    public string VsVersion => dte.VsVersion;
    public string? SolutionPath => dte.SolutionPath;
    private string? SolutionDir => dte.SolutionPath is { } p ? Path.GetDirectoryName(p) : null;

    public Task<(DebuggerMode Mode, long Seq)> GetModeAsync(CancellationToken ct) => ui.RunAsync(() => (dte.Mode, seq.Current), ct);

    /// <summary>Everything the handshake reports, read in one UI-thread hop (DTE members are UI-thread only).</summary>
    public Task<HandshakeResponse> GetHandshakeInfoAsync(CancellationToken ct) => ui.RunAsync(() => new HandshakeResponse
    {
        ProtocolMajor = ProtocolInfo.Major,
        ProtocolMinor = ProtocolInfo.Minor,
        VsVersion = dte.VsVersion,
        VsixVersion = vsixVersion,
        SolutionPath = dte.SolutionPath,
        Mode = dte.Mode,
        Seq = seq.Current,
    }, ct);

    public Task<StatusInfo> GetStatusAsync(CancellationToken ct) => ui.RunAsync(() => new StatusInfo
    {
        Mode = dte.Mode,
        Seq = seq.Current,
        Snapshot = dte.Mode == DebuggerMode.Break ? BuildSnapshot(BreakReason.Other) : null,
    }, ct);

    public Task<BreakpointInfo[]> ListBreakpointsAsync(CancellationToken ct) =>
        ui.RunAsync(() => dte.Breakpoints().Select(ToInfo).ToArray(), ct);

    public Task<SetBreakpointResult> SetBreakpointAsync(BreakpointSpec spec, CancellationToken ct)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        return ui.RunAsync(() =>
        {
            if (spec.File is not null)
            {
                // Only files inside the solution: otherwise bp_set would be an oracle for any path on disk.
                var absolute = PathUtil.ToAbsolute(SolutionDir, spec.File);
                if (!PathUtil.IsUnder(SolutionDir, absolute))
                    throw new BridgeException(BridgeErrorCode.InvalidArgument, "Breakpoint files must be inside the open solution's directory.", "Use a path relative to the solution directory.");
                if (!dte.FileExists(absolute))
                    throw new BridgeException(BridgeErrorCode.InvalidArgument, $"File not found: {PathUtil.ToRelative(SolutionDir, absolute)}", "Use a path relative to the solution directory.");
                spec.File = absolute;
            }

            if (spec.Condition is not null) ExpressionPolicy.RequireNoSideEffects(spec.Condition);
            if (spec.LogMessage is not null)
            {
                foreach (var placeholder in PathUtil.Placeholders(spec.LogMessage)) ExpressionPolicy.RequireNoSideEffects(placeholder);
            }

            var id = BreakpointKey.Compute(spec.File, spec.Line, spec.Function, spec.Condition);
            var existing = dte.Breakpoints().FirstOrDefault(b => IdOf(b) == id);
            if (existing is not null) return new SetBreakpointResult { Breakpoint = ToInfo(existing), AlreadyExisted = true };

            dte.AddBreakpoint(spec);
            var created = dte.Breakpoints().FirstOrDefault(b => IdOf(b) == id)
                ?? throw new BridgeException(BridgeErrorCode.Internal, "Visual Studio did not create the breakpoint.");
            log.Info($"Breakpoint {id} set");
            return new SetBreakpointResult { Breakpoint = ToInfo(created) };
        }, ct);
    }

    public Task<int> RemoveBreakpointsAsync(string? id, bool all, CancellationToken ct) => ui.RunAsync(() =>
        dte.RemoveBreakpoints(b => all || IdOf(b) == id), ct);

    public Task<long> StartAsync(CancellationToken ct) => Command(DebuggerMode.Design, dte.Go, ct);
    public Task<long> ContinueAsync(CancellationToken ct) => Command(DebuggerMode.Break, dte.Go, ct);
    public Task<long> PauseAsync(CancellationToken ct) => Command(DebuggerMode.Run, dte.Break, ct);
    public Task<long> StepAsync(StepKind kind, CancellationToken ct) => Command(DebuggerMode.Break, () => dte.Step(kind), ct);

    public Task StopAsync(CancellationToken ct) => ui.RunAsync(() =>
    {
        if (dte.Mode != DebuggerMode.Design) dte.Stop();
        return true;
    }, ct);

    public Task<StackFrameInfo[]> GetStackAsync(int maxFrames, CancellationToken ct) => InBreak(() =>
        dte.Stack(maxFrames).Select((f, i) => new StackFrameInfo
        {
            Index = i, Function = f.Function, Line = f.Line, Language = f.Language,
            File = f.File is null ? null : PathUtil.ToRelative(SolutionDir, f.File),
        }).ToArray(), ct);

    public Task<VariableInfo[]> GetVariablesAsync(VariableScope scope, string? path, int offset, int maxItems, CancellationToken ct) => InBreak(() =>
    {
        if (offset < 0 || offset > MaxVariableOffset)
            throw new BridgeException(BridgeErrorCode.InvalidArgument, $"offset must be between 0 and {MaxVariableOffset}.", "Narrow the path instead of paging this deep.");
        var skip = offset;
        var take = Math.Min(Math.Max(0, maxItems), MaxVariablePage);
        var items = path is null
            ? dte.Variables(scope, skip + take)
            : dte.Members(ExpressionPolicy.PrepareSideEffectFree(path), skip + take);
        return items.Skip(skip).Take(take).Select(Shape).ToArray();
    }, ct);

    public static void ValidateSetValue(string? expression, string? value)
    {
        ExpressionPolicy.RequireAssignableTarget(expression);
        ExpressionPolicy.RequireMutationText(value, "value");
    }

    public static void ValidateSideEffectEvaluation(string? expression) => ExpressionPolicy.RequireMutationText(expression, "expression");

    public Task<EvaluateResult> EvaluateAsync(string expression, bool allowSideEffects, CancellationToken ct)
    {
        if (allowSideEffects) ValidateSideEffectEvaluation(expression);
        return InBreak(() => Shape(dte.Evaluate(allowSideEffects ? expression.Trim() : ExpressionPolicy.PrepareSideEffectFree(expression))), ct);
    }

    public Task<EvaluateResult> SetValueAsync(string expression, string value, CancellationToken ct)
    {
        ValidateSetValue(expression, value);
        return InBreak(() => Shape(dte.SetValue(expression.Trim(), value.Trim())), ct);
    }

    /// <summary>Shown in consent questions ("… in OrderService.Submit"). Requires Break mode, so a wrong-mode call fails before the user is asked.</summary>
    public Task<string> GetCurrentFunctionAsync(CancellationToken ct) => InBreak(() =>
    {
        var frames = dte.Stack(1);
        return frames.Count > 0 ? frames[0].Function : "the current frame";
    }, ct);

    private static EvaluateResult Shape(EvaluateResult r)
    {
        var (value, truncated) = ValueShaper.Truncate(r.Value);
        return new EvaluateResult { IsValid = r.IsValid, Value = value, Type = r.Type, Truncated = truncated };
    }

    /// <summary>Builds the snapshot for the current break. Caller must be on the UI thread.</summary>
    public BreakSnapshot BuildSnapshot(BreakReason reason)
    {
        var frames = dte.Stack(5);
        var top = frames.Count > 0 ? frames[0] : null;
        var lastHit = reason == BreakReason.Breakpoint ? dte.LastHitBreakpoint : null;
        return new BreakSnapshot
        {
            Reason = reason,
            BreakpointId = lastHit is null ? null : IdOf(lastHit),
            Location = top is null ? null : new SourceLocation
            {
                File = top.File is null ? "" : PathUtil.ToRelative(SolutionDir, top.File),
                Line = top.Line ?? 0,
                Function = top.Function,
            },
            Source = top?.File is not null && top.Line is { } line ? PathUtil.SourceContext(top.File, line) : [],
            Args = dte.Variables(VariableScope.Args, 20).Select(Shape).ToArray(),
            StackTop = frames.Select(f => f.Line is { } l ? $"{f.Function}:{l}" : f.Function).ToArray(),
        };
    }

    private Task<long> Command(DebuggerMode required, Action action, CancellationToken ct) => ui.RunAsync(() =>
    {
        RequireMode(required);
        var observed = seq.Current;
        action();
        return observed;
    }, ct);

    private Task<T> InBreak<T>(Func<T> work, CancellationToken ct) => ui.RunAsync(() =>
    {
        RequireMode(DebuggerMode.Break);
        return work();
    }, ct);

    private void RequireMode(DebuggerMode required)
    {
        if (dte.Mode != required)
            throw new BridgeException(BridgeErrorCode.WrongMode, $"Requires {required} mode; current: {dte.Mode}.");
    }

    private static string IdOf(RawBreakpoint b) => BreakpointKey.Compute(b.File, b.Line, b.Function, b.Condition);

    private static VariableInfo Shape(VariableInfo v)
    {
        var (value, truncated) = ValueShaper.Truncate(v.Value);
        return new VariableInfo { Name = v.Name, Type = v.Type, Value = value, HasChildren = v.HasChildren, Truncated = truncated || v.Truncated };
    }

    private BreakpointInfo ToInfo(RawBreakpoint b) => new()
    {
        Id = IdOf(b),
        File = b.File is null ? null : PathUtil.ToRelative(SolutionDir, b.File),
        Line = b.Line, Function = b.Function, Condition = b.Condition, ConditionKind = b.ConditionKind,
        HitCount = b.HitCount, HitCountKind = b.HitCountKind, LogMessage = b.LogMessage, Enabled = b.Enabled,
        Bound = b.BoundCount > 0,
        BindError = b.BoundCount > 0 ? null
            : dte.Mode == DebuggerMode.Design ? "Pending: binds when debugging starts."
            : "Will not be hit: no executable code at this location or symbols are not loaded.",
    };
}
