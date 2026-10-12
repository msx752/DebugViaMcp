using System;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using VsMcpDebugger.Protocol;
using VsMcpDebugger.Vsix.Core;

namespace VsMcpDebugger.Vsix;

/// <summary>Turns DTE debugger events into protocol events. Holds a reference to DebuggerEvents so COM does not drop the subscription.</summary>
internal sealed class DebuggerEventPump
{
    private readonly DebuggerEvents _events;
    private readonly BuildEvents _buildEvents;
    private readonly DebugBridgeService _service;
    private readonly EventSeq _seq;
    private readonly EventBroadcaster _broadcaster;
    private readonly ILog _log;
    private readonly DTE _dte;

    public DebuggerEventPump(DTE dte, DebugBridgeService service, EventSeq seq, EventBroadcaster broadcaster, ILog log)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _dte = dte;
        _service = service;
        _seq = seq;
        _broadcaster = broadcaster;
        _log = log;
        _events = dte.Events.DebuggerEvents;
        _events.OnEnterBreakMode += OnEnterBreakMode;
        _events.OnEnterRunMode += OnEnterRunMode;
        _events.OnEnterDesignMode += OnEnterDesignMode;
        _events.OnContextChanged += OnContextChanged;
        _buildEvents = dte.Events.BuildEvents;
        _buildEvents.OnBuildBegin += (scope, action) => _service.Builds.OnBuildBegin();
        _buildEvents.OnBuildDone += (scope, action) =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _service.Builds.OnBuildDone(_dte.Solution.SolutionBuild.LastBuildInfo);
            _service.OnBuildFinished();
        };
    }

    private void OnContextChanged(EnvDTE.Process newProcess, Program newProgram, EnvDTE.Thread newThread, EnvDTE.StackFrame newStackFrame)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_dte.Debugger.CurrentMode != dbgDebugMode.dbgBreakMode) return;
        try
        {
            _broadcaster.PublishFrameChanged(_service.OnContextChanged());
        }
#pragma warning disable CA1031 // A failed frame event must not break Visual Studio's own context switch.
        catch (Exception e)
#pragma warning restore CA1031
        {
            _log.Error("Failed to publish frame change", e);
        }
    }

    private void OnEnterRunMode(dbgEventReason reason)
    {
        _service.OnSessionStarted();
        PublishMode(DebuggerMode.Run);
    }

    private void OnEnterDesignMode(dbgEventReason reason)
    {
        try
        {
            _service.OnEnterDesign();
        }
#pragma warning disable CA1031 // Cleanup failure must not keep the session-end event from being published.
        catch (Exception e)
#pragma warning restore CA1031
        {
            _log.Error("Failed to clean up after the debugging session", e);
        }

        PublishMode(DebuggerMode.Design);
    }

    private void OnEnterBreakMode(dbgEventReason reason, ref dbgExecutionAction executionAction)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var mapped = reason switch
        {
            dbgEventReason.dbgEventReasonBreakpoint => BreakReason.Breakpoint,
            dbgEventReason.dbgEventReasonStep => BreakReason.Step,
            dbgEventReason.dbgEventReasonExceptionThrown or dbgEventReason.dbgEventReasonExceptionNotHandled => BreakReason.Exception,
            dbgEventReason.dbgEventReasonUserBreak => BreakReason.Pause,
            _ => BreakReason.Other,
        };

        BreakSnapshot snapshot;
        try
        {
            snapshot = _service.OnEnterBreak(mapped);
        }
#pragma warning disable CA1031 // A failed snapshot must still produce a break event, or waiting agents would time out.
        catch (Exception e)
#pragma warning restore CA1031
        {
            _log.Error("Failed to build break snapshot", e);
            snapshot = new BreakSnapshot { Reason = mapped, Epoch = _service.Epoch };
        }

        var seq = _seq.Next();
        _log.Info($"Break #{seq} ({mapped}) at {snapshot.Location?.File}:{snapshot.Location?.Line}");
        _broadcaster.PublishBreak(new BreakEvent { Seq = seq, Snapshot = snapshot });
    }

    private void PublishMode(DebuggerMode mode)
    {
        var seq = _seq.Next();
        _log.Info($"Mode #{seq} -> {mode}");
        _broadcaster.PublishMode(new ModeChangedEvent { Mode = mode, Seq = seq });
    }
}
