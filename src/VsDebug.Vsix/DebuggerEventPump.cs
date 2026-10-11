using System;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using VsDebug.Protocol;
using VsDebug.Vsix.Core;

namespace VsDebug.Vsix;

/// <summary>Turns DTE debugger events into protocol events. Holds a reference to DebuggerEvents so COM does not drop the subscription.</summary>
internal sealed class DebuggerEventPump
{
    private readonly DebuggerEvents _events;
    private readonly DebugBridgeService _service;
    private readonly EventSeq _seq;
    private readonly EventBroadcaster _broadcaster;
    private readonly ILog _log;

    public DebuggerEventPump(DTE dte, DebugBridgeService service, EventSeq seq, EventBroadcaster broadcaster, ILog log)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _service = service;
        _seq = seq;
        _broadcaster = broadcaster;
        _log = log;
        _events = dte.Events.DebuggerEvents;
        _events.OnEnterBreakMode += OnEnterBreakMode;
        _events.OnEnterRunMode += OnEnterRunMode;
        _events.OnEnterDesignMode += OnEnterDesignMode;
    }

    private void OnEnterRunMode(dbgEventReason reason) => PublishMode(DebuggerMode.Run);

    private void OnEnterDesignMode(dbgEventReason reason) => PublishMode(DebuggerMode.Design);

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
            snapshot = _service.BuildSnapshot(mapped);
        }
#pragma warning disable CA1031 // A failed snapshot must still produce a break event, or waiting agents would time out.
        catch (Exception e)
#pragma warning restore CA1031
        {
            _log.Error("Failed to build break snapshot", e);
            snapshot = new BreakSnapshot { Reason = mapped };
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
