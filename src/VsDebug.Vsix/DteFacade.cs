using System;
using System.Collections.Generic;
using System.Linq;
using EnvDTE;
using EnvDTE80;
using EnvDTE90a;
using Microsoft.VisualStudio.Shell;
using VsDebug.Protocol;
using VsDebug.Vsix.Core;

namespace VsDebug.Vsix;

/// <summary>IDteFacade over EnvDTE. Every member runs on the UI thread (enforced by IUiThread callers).</summary>
internal sealed class DteFacade(DTE2 dte) : IDteFacade
{
    private const int EvalTimeoutMs = 5000;

    public string VsVersion
    {
        get { ThreadHelper.ThrowIfNotOnUIThread(); return dte.Version; }
    }

    public string? SolutionPath
    {
        get
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var path = dte.Solution?.FullName;
            return string.IsNullOrEmpty(path) ? null : path;
        }
    }

    public DebuggerMode Mode
    {
        get
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return dte.Debugger.CurrentMode switch
            {
                dbgDebugMode.dbgBreakMode => DebuggerMode.Break,
                dbgDebugMode.dbgRunMode => DebuggerMode.Run,
                _ => DebuggerMode.Design,
            };
        }
    }

    public bool FileExists(string path) => System.IO.File.Exists(path);

    public RawBreakpoint? LastHitBreakpoint
    {
        get
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var bp = dte.Debugger.BreakpointLastHit;
            return bp is null ? null : ToRaw(bp.Parent ?? bp);
        }
    }

    public IReadOnlyList<RawBreakpoint> Breakpoints()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return dte.Debugger.Breakpoints.Cast<Breakpoint>().Select(ToRaw).ToList();
    }

    public void AddBreakpoint(BreakpointSpec spec)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var created = dte.Debugger.Breakpoints.Add(
            Function: spec.Function ?? "",
            File: spec.File ?? "",
            Line: spec.Line ?? 1,
            Column: 1,
            Condition: spec.Condition ?? "",
            ConditionType: spec.ConditionKind == ConditionKind.WhenChanged
                ? dbgBreakpointConditionType.dbgBreakpointConditionTypeWhenChanged
                : dbgBreakpointConditionType.dbgBreakpointConditionTypeWhenTrue,
            Language: "",
            Data: "",
            DataCount: 1,
            Address: "",
            HitCount: spec.HitCount,
            HitCountType: spec.HitCountKind switch
            {
                HitCountKind.Equal => dbgHitCountType.dbgHitCountTypeEqual,
                HitCountKind.AtLeast => dbgHitCountType.dbgHitCountTypeGreaterOrEqual,
                HitCountKind.Multiple => dbgHitCountType.dbgHitCountTypeMultiple,
                _ => dbgHitCountType.dbgHitCountTypeNone,
            });

        if (spec.LogMessage is not null)
        {
            foreach (Breakpoint2 bp in created)
            {
                bp.Message = spec.LogMessage;
                bp.BreakWhenHit = false;
            }
        }
    }

    public int RemoveBreakpoints(Func<RawBreakpoint, bool> match)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var doomed = dte.Debugger.Breakpoints.Cast<Breakpoint>().Where(b => match(ToRaw(b))).ToList();
        foreach (var b in doomed) b.Delete();
        return doomed.Count;
    }

    public void Go() { ThreadHelper.ThrowIfNotOnUIThread(); dte.Debugger.Go(WaitForBreakOrEnd: false); }

    public void Break() { ThreadHelper.ThrowIfNotOnUIThread(); dte.Debugger.Break(WaitForBreakMode: false); }

    public void Stop() { ThreadHelper.ThrowIfNotOnUIThread(); dte.Debugger.Stop(WaitForDesignMode: false); }

    public void Step(StepKind kind)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        switch (kind)
        {
            case StepKind.Into: dte.Debugger.StepInto(WaitForBreakOrEnd: false); break;
            case StepKind.Out: dte.Debugger.StepOut(WaitForBreakOrEnd: false); break;
            default: dte.Debugger.StepOver(WaitForBreakOrEnd: false); break;
        }
    }

    public IReadOnlyList<RawFrame> Stack(int max)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var thread = dte.Debugger.CurrentThread;
        if (thread is null) return [];
        return thread.StackFrames.Cast<EnvDTE.StackFrame>().Take(max).Select(ToFrame).ToList();
    }

    public IReadOnlyList<VariableInfo> Variables(VariableScope scope, int maxItems)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var frame = dte.Debugger.CurrentStackFrame;
        if (frame is null) return [];
        var expressions = scope == VariableScope.Args ? frame.Arguments : frame.Locals;
        return expressions.Cast<Expression>().Take(maxItems).Select(ToInfo).ToList();
    }

    public IReadOnlyList<VariableInfo> Members(string expression, int maxItems)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var e = dte.Debugger.GetExpression(expression, true, EvalTimeoutMs);
        if (!e.IsValidValue)
            throw new BridgeException(BridgeErrorCode.EvalFailed, e.Value, "Check the path against vars output.");
        return e.DataMembers.Cast<Expression>().Take(maxItems).Select(ToInfo).ToList();
    }

    public EvaluateResult Evaluate(string expression)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var e = dte.Debugger.GetExpression(expression, true, EvalTimeoutMs);
        return new EvaluateResult { IsValid = e.IsValidValue, Value = e.Value, Type = e.Type };
    }

    private static RawFrame ToFrame(EnvDTE.StackFrame f)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var f2 = f as StackFrame2;
        return new RawFrame
        {
            Function = f.FunctionName,
            Language = f.Language,
            File = string.IsNullOrEmpty(f2?.FileName) ? null : f2!.FileName,
            Line = f2 is null || f2.LineNumber == 0 ? null : (int)f2.LineNumber,
        };
    }

    private static VariableInfo ToInfo(Expression e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return new VariableInfo { Name = e.Name, Type = e.Type, Value = e.Value, HasChildren = e.DataMembers.Count > 0 };
    }

    private static RawBreakpoint ToRaw(Breakpoint b)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var b2 = b as Breakpoint2;
        var isFileLine = !string.IsNullOrEmpty(b.File);
        return new RawBreakpoint
        {
            File = isFileLine ? b.File : null,
            Line = isFileLine ? b.FileLine : null,
            Function = isFileLine ? null : b.FunctionName,
            Condition = string.IsNullOrEmpty(b.Condition) ? null : b.Condition,
            ConditionKind = b.ConditionType == dbgBreakpointConditionType.dbgBreakpointConditionTypeWhenChanged ? ConditionKind.WhenChanged : ConditionKind.WhenTrue,
            HitCount = b.HitCountTarget,
            HitCountKind = b.HitCountType switch
            {
                dbgHitCountType.dbgHitCountTypeEqual => HitCountKind.Equal,
                dbgHitCountType.dbgHitCountTypeGreaterOrEqual => HitCountKind.AtLeast,
                dbgHitCountType.dbgHitCountTypeMultiple => HitCountKind.Multiple,
                _ => HitCountKind.None,
            },
            LogMessage = b2 is not null && !b2.BreakWhenHit && !string.IsNullOrEmpty(b2.Message) ? b2.Message : null,
            Enabled = b.Enabled,
            BoundCount = b.Children?.Count ?? 0,
        };
    }
}
