using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core.Abstractions;
using DebugViaMcp.Vsix.Core.Builds;
using DebugViaMcp.Vsix.Core.Debugging;
using DebugViaMcp.Vsix.Core.Security;
using DebugViaMcp.Vsix.Core.Support;

namespace DebugViaMcp.Vsix.Core.Bridge;

/// <summary>Implements the bridge operations on top of <see cref="IDteFacade"/>. Handshake lives in BridgeConnectionHandler.</summary>
public sealed class DebugBridgeService(IDteFacade dte, IUiThread ui, EventSeq seq, ILog log, string vsixVersion, BuildMonitor? builds = null, IBuildPrompt? buildPrompt = null, Func<bool>? vsBuildsOnRun = null, Func<bool>? maskSensitiveValues = null)
{
    /// <summary>How long a project's build state is reused: reading it walks the project folder on the UI thread.</summary>
    public const int BuildInfoCacheSeconds = 5;

    private readonly Dictionary<string, (DateTime At, ProjectBuildInfo Info)> _buildInfo = new(StringComparer.OrdinalIgnoreCase);

    internal Func<DateTime> BuildInfoClock { get; set; } = () => DateTime.UtcNow;

    private ProjectBuildInfo BuildInfoOf(string unique)
    {
        var now = BuildInfoClock();
        if (_buildInfo.TryGetValue(unique, out var cached) && now - cached.At < TimeSpan.FromSeconds(BuildInfoCacheSeconds)) return cached.Info;
        var info = dte.GetBuildInfo(unique);
        _buildInfo[unique] = (now, info);
        return info;
    }

    private readonly StaleBuildNotifier? _notifier = buildPrompt is null ? null : new StaleBuildNotifier(buildPrompt, () => dte.Mode, dte.StartBuild);

    /// <summary>Enforced here, not trusted from the client: deep offsets would enumerate members on the UI thread.</summary>
    public const int MaxVariableOffset = 100_000;

    /// <summary>The MCP layer asks for at most 200 items + 1 to detect more; anything above is capped.</summary>
    public const int MaxVariablePage = 201;

    private long _epoch;
    private readonly RunToState _runTo = new();

    /// <summary>Ids of the breakpoints that remove themselves after their first break (bp_set temporary). UI thread only.</summary>
    private readonly HashSet<string> _temporary = new(StringComparer.Ordinal);
    private bool _attached;

    public string VsixVersion => vsixVersion;

    /// <summary>Read on every call, so the Options setting applies at once. On when nothing says otherwise: a secret leaks only if someone chose it.</summary>
    private bool MaskOn => maskSensitiveValues?.Invoke() ?? true;

    /// <summary>Increments on every break: anything read in an earlier break is stale once the program has moved (spec §3.1).</summary>
    public long Epoch => Interlocked.Read(ref _epoch);
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
        Epoch = Epoch,
        Dialog = BlockingDialog(),
    }, ct);

    public const int MaxDialogTitle = 120;

    /// <summary>The dialog Visual Studio is blocked on, as text safe to hand to the agent; null when none. Any thread, no UI-thread hop.</summary>
    public string? GetBlockingDialog() => BlockingDialog();

    /// <summary>The title is Visual Studio's own text and may hold a file name: single line, short.</summary>
    private string? BlockingDialog()
    {
        if (dte.ModalDialogTitle() is not { } raw) return null;
        var text = string.Join(" ", raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length == 0) return "(a dialog without a title)";
        return text.Length <= MaxDialogTitle ? text : text.Substring(0, MaxDialogTitle - 3) + "...";
    }

    public Task<BreakpointInfo[]> ListBreakpointsAsync(CancellationToken ct) =>
        ui.RunAsync(() => dte.Breakpoints().Where(b => !_runTo.Owns(IdOf(b))).Select(ToInfo).ToArray(), ct);

    public async Task<RunToResult> RunToAsync(string file, int line, object owner, CancellationToken ct)
    {
        // Hop 1 registers the run-to (and its breakpoint, if we created it) at once, so that a cancel, a closing
        // connection or a break while Visual Studio is still binding is always cleaned up or recognised.
        var (id, owned, observed, spec) = await ui.RunAsync(() =>
        {
            if (dte.Mode == DebuggerMode.Design)
                throw new BridgeException(BridgeErrorCode.WrongMode, "Requires Break or Run mode; current: Design.", "Start debugging with debug_start.");
            if (line < 1) throw new BridgeException(BridgeErrorCode.InvalidArgument, "line must be 1 or greater.");

            var s = new BreakpointSpec { File = file, Line = line };
            s.File = CheckBreakpoint(s);
            var key = BreakpointKey.Compute(s.File, s.Line, null, null);
            ReleaseRunTo(null);

            var isNew = dte.Breakpoints().All(b => IdOf(b) != key);
            var before = seq.Current;
            if (isNew) dte.AddBreakpoint(s);
            _runTo.Arm(key, isNew, owner);
            return (key, isNew, before, s);
        }, ct).ConfigureAwait(false);

        try
        {
            // Visual Studio binds a new breakpoint a moment after adding it.
            if (owned) await WaitForBindingAsync(id, ct).ConfigureAwait(false);

            return await ui.RunAsync(() =>
            {
                // Disarmed meanwhile: a break already used it up (reported as runTo, the waiting side sees it through
                // 'observed'), or the connection closed. Either way nothing more to do, and never resume past it.
                if (_runTo.TargetId != id) return new RunToResult { Seq = observed };

                if (dte.Mode == DebuggerMode.Design)
                    throw new BridgeException(BridgeErrorCode.WrongMode, "The debugging session ended before the breakpoint was ready.", "Start debugging with debug_start.");
                var bp = dte.Breakpoints().FirstOrDefault(b => IdOf(b) == id)
                    ?? throw new BridgeException(BridgeErrorCode.Internal, "Visual Studio did not create the breakpoint.");
                if (bp.BoundCount == 0)
                {
                    ReleaseRunTo(owner);
                    return new RunToResult { BindError = "No executable code at this location, or symbols are not loaded." };
                }

                if (dte.Mode == DebuggerMode.Break) dte.Go();
                log.Info($"Run to {PathUtil.ToRelative(SolutionDir, spec.File!)}:{line}");
                return new RunToResult { Seq = observed };
            }, ct).ConfigureAwait(false);
        }
        catch
        {
            await ReleaseRunToAsync(owner).ConfigureAwait(false);   // cancelled, timed out or failed: leave nothing behind
            throw;
        }
    }

    /// <summary>How long a new breakpoint may take to bind while debugging.</summary>
    internal static readonly TimeSpan BindWait = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan BindPoll = TimeSpan.FromMilliseconds(50);

    /// <summary>Visual Studio binds a breakpoint asynchronously. Polls without holding the UI thread; gives up after <see cref="BindWait"/>.</summary>
    private async Task WaitForBindingAsync(string id, CancellationToken ct)
    {
        var started = Stopwatch.StartNew();
        while (started.Elapsed < BindWait)
        {
            var settled = await ui.RunAsync(() =>
                dte.Mode == DebuggerMode.Design || dte.Breakpoints().FirstOrDefault(b => IdOf(b) == id) is not { BoundCount: 0 }, ct).ConfigureAwait(false);
            if (settled) return;
            await Task.Delay(BindPoll, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Called by the event pump when debugging ends (UI thread).</summary>
    public void OnEnterDesign()
    {
        _attached = false;
        ReleaseRunTo(null);
        _notifier?.OnSessionEnded();
    }

    /// <summary>Called by the event pump when a debug session starts (UI thread): Build bars step aside.</summary>
    public void OnSessionStarted() => _notifier?.OnSessionStarted();

    /// <summary>Called when the owner closes a Build bar.</summary>
    public void BuildBarDismissed(string project) => _notifier?.Dismissed(project);

    /// <summary>Called by the event pump when a build finishes (UI thread): bars of projects that are up to date now go away.</summary>
    public void OnBuildFinished()
    {
        _buildInfo.Clear();
        if (_notifier is null) return;
        foreach (var unique in _notifier.ShownProjects)
            _notifier.Observe(unique, StaleBuildChecker.IsStale(dte.GetBuildInfo(unique)));
    }

    /// <summary>A closing connection removes the run-to breakpoint it armed.</summary>
    public Task ReleaseRunToAsync(object owner) => ui.RunAsync(() => { ReleaseRunTo(owner); return true; }, CancellationToken.None);

    private void ReleaseRunTo(object? owner)
    {
        if (_runTo.Take(owner) is { Owned: true } t) dte.RemoveBreakpoints(b => IdOf(b) == t.Id);
    }

    public Task<SetBreakpointResult> SetBreakpointAsync(BreakpointSpec spec, CancellationToken ct)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        return SetBreakpointCoreAsync(spec, ct);
    }

    private async Task<SetBreakpointResult> SetBreakpointCoreAsync(BreakpointSpec spec, CancellationToken ct)
    {
        var (result, id) = await ui.RunAsync(() =>
        {
            var absolute = CheckBreakpoint(spec);
            if (absolute is not null) spec.File = absolute;

            var key = BreakpointKey.Compute(spec.File, spec.Line, spec.Function, spec.Condition);
            var existing = dte.Breakpoints().FirstOrDefault(b => IdOf(b) == key);
            if (existing is not null) return (new SetBreakpointResult { Breakpoint = ToInfo(existing), AlreadyExisted = true }, (string?)null);

            dte.AddBreakpoint(spec);
            var created = dte.Breakpoints().FirstOrDefault(b => IdOf(b) == key)
                ?? throw new BridgeException(BridgeErrorCode.Internal, "Visual Studio did not create the breakpoint.");
            if (spec.Temporary) _temporary.Add(key);
            else _temporary.Remove(key);   // a breakpoint set again after removal does not inherit the old flag
            log.Info($"Breakpoint {key} set");
            return (new SetBreakpointResult { Breakpoint = ToInfo(created) }, key);
        }, ct).ConfigureAwait(false);

        // While debugging, Visual Studio binds a new breakpoint a moment after adding it; report the settled state.
        if (id is null || result.Breakpoint.Bound) return result;
        var mode = await ui.RunAsync(() => dte.Mode, ct).ConfigureAwait(false);
        if (mode == DebuggerMode.Design) return result;
        await WaitForBindingAsync(id, ct).ConfigureAwait(false);
        return await ui.RunAsync(() =>
            dte.Breakpoints().FirstOrDefault(b => IdOf(b) == id) is { } now ? new SetBreakpointResult { Breakpoint = ToInfo(now) } : result, ct).ConfigureAwait(false);
    }

    /// <summary>Everything SetBreakpointAsync would reject, checked without changing anything (for consent questions).</summary>
    public Task<bool> ValidateBreakpointAsync(BreakpointSpec spec, CancellationToken ct)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        return ui.RunAsync(() =>
        {
            CheckBreakpoint(spec);
            return true;
        }, ct);
    }

    /// <returns>The absolute file path, or null for a function breakpoint. Caller must be on the UI thread.</returns>
    private string? CheckBreakpoint(BreakpointSpec spec)
    {
        // Shown in full in consent questions, so the same length limit as other agent-supplied expressions.
        if (spec.Condition is not null) ExpressionPolicy.RequireMutationText(spec.Condition, "condition");
        if (spec.LogMessage is not null) ExpressionPolicy.RequireMutationText(spec.LogMessage, "logMessage");
        if (spec.Function is not null) ExpressionPolicy.RequireMutationText(spec.Function, "function");
        if (spec.Temporary && spec.LogMessage is not null)
            throw new BridgeException(BridgeErrorCode.InvalidArgument, "A tracepoint never stops, so it cannot be temporary.", "Drop temporary, or drop logMessage.");

        string? absolute = null;
        if (spec.File is not null)
        {
            // Only files inside the solution: otherwise bp_set would be an oracle for any path on disk.
            absolute = PathUtil.ToAbsolute(SolutionDir, spec.File);
            if (!PathUtil.IsUnder(SolutionDir, absolute))
                throw new BridgeException(BridgeErrorCode.InvalidArgument, "Breakpoint files must be inside the open solution's directory.", "Use a path relative to the solution directory.");
            if (!dte.FileExists(absolute))
                throw new BridgeException(BridgeErrorCode.InvalidArgument, $"File not found: {PathUtil.ToRelative(SolutionDir, absolute)}", "Use a path relative to the solution directory.");
        }

        if (spec.Condition is not null) ExpressionPolicy.RequireNoSideEffects(spec.Condition);
        if (spec.LogMessage is not null)
        {
            foreach (var placeholder in PathUtil.Placeholders(spec.LogMessage))
            {
                ExpressionPolicy.RequireNoSideEffects(placeholder);
                if (MaskOn && SensitiveValueMask.NamesSensitiveValue(placeholder))
                    throw new BridgeException(BridgeErrorCode.InvalidArgument, $"The tracepoint message would print {{{placeholder}}}, which looks sensitive, and \"Mask sensitive values\" is on.",
                        "Print something else, or turn Mask sensitive values off in Tools → Options → Debugging via MCP if you accept the risk.");
            }
        }

        return absolute;
    }

    public Task<int> RemoveBreakpointsAsync(string? id, bool all, CancellationToken ct) => ui.RunAsync(() =>
    {
        var removed = dte.RemoveBreakpoints(b => all || IdOf(b) == id);
        if (all) _temporary.Clear();
        else if (id is not null) _temporary.Remove(id);
        return removed;
    }, ct);

    public Task<BreakpointInfo?> FindBreakpointAsync(string id, CancellationToken ct) =>
        ui.RunAsync<BreakpointInfo?>(() => dte.Breakpoints().Where(b => IdOf(b) == id).Select(ToInfo).FirstOrDefault(), ct);

    public Task<BreakpointInfo?> SetBreakpointEnabledAsync(string id, bool enabled, CancellationToken ct) => ui.RunAsync(() =>
    {
        var bp = dte.SetEnabled(b => IdOf(b) == id, enabled);
        if (bp is not null) log.Info($"Breakpoint {id} {(enabled ? "enabled" : "disabled")}");
        return bp is null ? null : ToInfo(bp);
    }, ct);

    public Task<long> StartAsync(CancellationToken ct) => Command(DebuggerMode.Design, dte.Go, ct);
    /// <summary>
    /// Starts <paramref name="project"/> without leaving the solution's startup project changed: it is switched for the
    /// launch and put back afterwards, whatever happens.
    /// </summary>
    public Task<long> StartProjectAsync(string project, CancellationToken ct) => ui.RunAsync(() =>
    {
        RequireMode(DebuggerMode.Design);
        var unique = ResolveProject(project);
        if (!dte.IsLaunchable(unique))
        {
            var launchable = dte.ProjectNames().Where(dte.IsLaunchable).Select(Path.GetFileNameWithoutExtension);
            throw new BridgeException(BridgeErrorCode.InvalidArgument, $"Project '{Path.GetFileNameWithoutExtension(unique)}' cannot be started.",
                "Projects that can be started: " + string.Join(", ", launchable));
        }

        var observed = seq.Current;
        var original = dte.StartupProjects.ToArray();   // all of them: "Multiple startup projects" must survive
        try
        {
            dte.StartupProjects = [unique];
            dte.Go();
        }
        finally
        {
            dte.StartupProjects = original;
        }

        return observed;
    }, ct);

    public Task<long> ContinueAsync(CancellationToken ct) => Command(DebuggerMode.Break, dte.Go, ct);
    public Task<long> PauseAsync(CancellationToken ct) => Command(DebuggerMode.Run, dte.Break, ct);
    public Task<long> StepAsync(StepKind kind, CancellationToken ct) => Command(DebuggerMode.Break, () => dte.Step(kind), ct);

    public Task StopAsync(CancellationToken ct) => ui.RunAsync(() =>
    {
        if (dte.Mode != DebuggerMode.Design)
        {
            // An attached process keeps running: detach instead of ending it (refinement R2).
            if (_attached) dte.DetachAll();
            else dte.Stop();
        }

        return true;
    }, ct);

    public Task<ProcessInfo[]> ListAttachableAsync(string? processName, CancellationToken ct) =>
        ui.RunAsync(() => AttachCandidates.Filter(dte.LocalProcesses(), processName), ct);

    /// <summary>
    /// The options that decide whether symbols load, and the breakpoints that are not bound with what may be behind it. The notes only say
    /// what can be the cause; they never claim one (Visual Studio does not tell why a module has no symbols).
    /// </summary>
    public Task<SymbolStatus> GetSymbolStatusAsync(CancellationToken ct) => ui.RunAsync(() =>
    {
        var o = dte.DebuggerOptions();
        var unbound = dte.Breakpoints().Where(b => b.BoundCount == 0 && !_runTo.Owns(IdOf(b))).Select(ToInfo).ToArray();
        var notes = new List<string>();
        if (unbound.Length > 0)
        {
            if (dte.Mode == DebuggerMode.Design)
                notes.Add("Breakpoints bind when debugging starts.");
            else
            {
                notes.Add("A breakpoint that stays unbound while debugging means no symbols are loaded for its module, or the line has no executable code. Debug > Windows > Modules shows what is loaded.");
                if (o.RequireSourceToMatch == true)
                    notes.Add("'Require source files to exactly match the original version' is on: a file changed since the build does not bind.");
                if (o.JustMyCode == true)
                    notes.Add("Just My Code is on: modules that are optimized or have no symbols are skipped.");
            }
        }

        return new SymbolStatus
        {
            RequireSourceToMatch = o.RequireSourceToMatch, JustMyCode = o.JustMyCode, WarnIfNoSymbols = o.WarnIfNoSymbols,
            SuppressJitOptimization = o.SuppressJitOptimization, SourceServer = o.SourceServer, SourceServerDiagnostics = o.SourceServerDiagnostics,
            Unbound = unbound, Notes = notes.ToArray(),
        };
    }, ct);

    private readonly ExceptionSettingsEditor _exceptions = new(dte);

    public Task<ExceptionSettingsPage> ListExceptionSettingsAsync(string? group, string? filter, bool onlyEnabled, int limit, CancellationToken ct) =>
        ui.RunAsync(() => _exceptions.List(group, filter, onlyEnabled, limit), ct);

    /// <summary>What a set would change, or the reason it cannot; writes nothing, so the consent question is only asked for a request that can succeed.</summary>
    public Task<ExceptionSetResult> PlanExceptionSettingsAsync(string group, string[] names, bool breakWhenThrown, CancellationToken ct) =>
        ui.RunAsync(() => _exceptions.Plan(group, names, breakWhenThrown), ct);

    /// <param name="owner">The connection; <see cref="ResetExceptionSettingsAsync"/> puts back what this owner changed.</param>
    public Task<ExceptionSetResult> SetExceptionSettingsAsync(object owner, string group, string[] names, bool breakWhenThrown, CancellationToken ct) =>
        ui.RunAsync(() => _exceptions.Set(owner, group, names, breakWhenThrown), ct);

    public Task<int> PendingExceptionChangesAsync(object owner) => ui.RunAsync(() => _exceptions.Pending(owner), CancellationToken.None);

    public Task<ExceptionResetResult> ResetExceptionSettingsAsync(object owner, CancellationToken ct) =>
        ui.RunAsync(() => _exceptions.Reset(owner), ct);

    /// <summary>Puts back what the owner changed and forgets it (the owner's hold on Visual Studio ended).</summary>
    public Task ReleaseExceptionSettingsAsync(object owner) => ui.RunAsync(() => { _exceptions.Release(owner); return true; }, CancellationToken.None);

    private readonly WatchList _watches = new();
    private const int MaxWatchValueLength = 200;

    public Task<AddWatchResult> AddWatchAsync(object owner, string expression, string? name, CancellationToken ct) => ui.RunAsync(() =>
    {
        var (entry, existed) = _watches.Add(owner, expression, name);
        return new AddWatchResult { Watch = ReadWatch(entry), AlreadyExisted = existed };
    }, ct);

    /// <summary>Every entry of the owner with its value read now in the selected frame; without values outside Break mode.</summary>
    public Task<WatchListResult> ListWatchesAsync(object owner, CancellationToken ct) =>
        ui.RunAsync(() => new WatchListResult { Watches = _watches.Entries(owner).Select(ReadWatch).ToArray() }, ct);

    public Task<RemoveWatchResult> RemoveWatchAsync(object owner, string? id, bool all, CancellationToken ct) =>
        ui.RunAsync(() => new RemoveWatchResult { Removed = _watches.Remove(owner, id, all) }, ct);

    /// <summary>The owner's hold on Visual Studio ended: its watch list is forgotten.</summary>
    public Task ReleaseWatchesAsync(object owner) => ui.RunAsync(() => { _watches.Clear(owner); return true; }, CancellationToken.None);

    private WatchValue ReadWatch(WatchList.Entry entry)
    {
        var watch = new WatchValue { Id = entry.Id, Expression = entry.Expression, Name = entry.Name };
        if (dte.Mode != DebuggerMode.Break) return watch;
#pragma warning disable CA1031 // One entry that cannot be read must not fail the others: its message becomes its error.
        try
        {
            var raw = dte.Evaluate(ExpressionPolicy.PrepareSideEffectFree(entry.Expression));
            var shaped = Shape(entry.Expression, raw);
            var (text, _) = ValueShaper.Truncate(shaped.Value, MaxWatchValueLength);
            if (!shaped.IsValid)
            {
                watch.Error = text;
                return watch;
            }

            watch.Value = text;
            watch.Type = shaped.Type;
            watch.Changed = entry.LastText is not null && !string.Equals(entry.LastText, text, StringComparison.Ordinal);
            entry.LastText = text;
        }
        catch (Exception e)
        {
            watch.Error = ValueShaper.Truncate(e.Message, MaxWatchValueLength).Value;
        }
#pragma warning restore CA1031

        return watch;
    }

    /// <summary>The process name for the consent question; validates mode and target first, so a doomed request never asks.</summary>
    public Task<string> DescribeAttachTargetAsync(int pid, CancellationToken ct) => ui.RunAsync(() => RequireAttachable(pid).Name, ct);

    public Task<long> AttachAsync(int pid, CancellationToken ct) => AttachAsync(pid, expectedName: null, ct);

    /// <param name="expectedName">The process name the user approved; a reused pid that now names another process is refused.</param>
    public Task<long> AttachAsync(int pid, string? expectedName, CancellationToken ct) => ui.RunAsync(() =>
    {
        var target = RequireAttachable(pid);
        if (expectedName is not null && !string.Equals(target.Name, expectedName, StringComparison.Ordinal))
            throw new BridgeException(BridgeErrorCode.NotFound, $"Process {pid} is no longer '{expectedName}'.", "Call debug_attach again.");
        var observed = seq.Current;
        // Set first: if Visual Studio attaches and then throws, debug_stop must still detach, never kill the process.
        _attached = true;
        try
        {
            dte.Attach(pid);
        }
        catch
        {
            if (dte.Mode == DebuggerMode.Design) _attached = false;   // nothing attached; a session started later is stopped normally
            throw;
        }

        log.Info($"Attached to {target.Name} (pid {pid})");
        return observed;
    }, ct);

    private ProcessInfo RequireAttachable(int pid)
    {
        RequireMode(DebuggerMode.Design);
        return AttachCandidates.Filter(dte.LocalProcesses(), null).FirstOrDefault(p => p.Pid == pid)
            ?? throw new BridgeException(BridgeErrorCode.NotFound, $"No process {pid} of yours that Visual Studio can attach to.", "Call debug_attach with processName to find it.");
    }

    public const int MaxStackFrames = 200;

    public Task<StackFrameInfo[]> GetStackAsync(int maxFrames, CancellationToken ct) => InBreak(() => ToFrames(dte.Stack(maxFrames)), ct);

    public Task<ThreadInfo[]> GetThreadsAsync(CancellationToken ct) => InBreak(() =>
    {
        var current = dte.CurrentThreadId;
        return dte.Threads().Select(t => new ThreadInfo { Id = t.Id, Name = t.Name, Location = t.Location, IsCurrent = t.Id == current }).ToArray();
    }, ct);

    public Task<StackInfo> GetThreadStackAsync(int? threadId, int maxFrames, CancellationToken ct) => InBreak(() =>
    {
        var max = Math.Min(Math.Max(1, maxFrames), MaxStackFrames);
        var frames = threadId is { } id ? dte.ThreadStack(id, max) ?? throw NoSuchThread(id) : dte.Stack(max);
        return new StackInfo { Epoch = Epoch, ThreadId = threadId ?? dte.CurrentThreadId, Frames = ToFrames(frames) };
    }, ct);

    public Task<FrameSelection> SelectFrameAsync(int index, long epoch, int? threadId, CancellationToken ct) => InBreak(() =>
    {
        if (epoch != Epoch)
            throw new BridgeException(BridgeErrorCode.Stale, "The program resumed since this frame was read.", "Call stack again and pass its epoch.");
        if (index < 0) throw new BridgeException(BridgeErrorCode.InvalidArgument, "index must be 0 or greater.");

        var frames = threadId is { } id ? dte.ThreadStack(id, index + 1) ?? throw NoSuchThread(id) : dte.Stack(index + 1);
        if (index >= frames.Count)
            throw new BridgeException(BridgeErrorCode.NotFound, $"Frame {index} does not exist; the stack has {frames.Count} frames.", "Call stack to see the frames.");

        dte.SelectFrame(threadId, index);
        var frame = frames[index];
        return new FrameSelection
        {
            ThreadId = threadId ?? dte.CurrentThreadId, Index = index, Epoch = Epoch,
            Location = ToLocation(frame), Source = SourceOf(frame),
            Args = dte.Variables(VariableScope.Args, 20).Select(v => Shape(v, null)).ToArray(),
        };
    }, ct);

    /// <summary>
    /// Called by the event pump when the thread or frame selection changes (UI thread). Advances the event sequence,
    /// so consent given while another frame was selected no longer applies.
    /// </summary>
    public FrameChangedEvent OnContextChanged() =>
        new() { Seq = seq.Next(), Epoch = Epoch, ThreadId = dte.CurrentThreadId, FrameIndex = dte.CurrentFrameIndex };

    public const int MaxInnerExceptions = 5;
    public const int MaxStackTraceLength = 4096;

    public Task<ExceptionInfo?> GetExceptionAsync(CancellationToken ct) => InBreak<ExceptionInfo?>(() =>
    {
        var raw = dte.CurrentException(MaxInnerExceptions);
        if (raw is null) return null;
        var anyMasked = false;
        string Hide(string? text)
        {
            if (!MaskOn) return text ?? "";
            var hidden = SensitiveValueMask.MaskText(text, out var changed);
            anyMasked |= changed;
            return hidden;
        }

        var (message, messageCut) = ValueShaper.Truncate(Hide(raw.Message));
        var (trace, traceCut) = raw.StackTrace is null ? ((string?)null, false) : ValueShaper.Truncate(Hide(raw.StackTrace), MaxStackTraceLength);
        var top = dte.Stack(1);
        return new ExceptionInfo
        {
            Type = raw.Type, Message = message, StackTrace = trace, Truncated = messageCut || traceCut,
            ThrowSite = top.Count > 0 ? ToLocation(top[0]) : null,
            Inner = raw.Inner.Take(MaxInnerExceptions).Select(i => new InnerExceptionInfo { Type = i.Type, Message = ValueShaper.Truncate(Hide(i.Message)).Value }).ToArray(),
            Masked = anyMasked ? true : null,
        };
    }, ct);

    public Task<OutputPaneInfo[]> ListOutputPanesAsync(CancellationToken ct) => ui.RunAsync(() =>
        dte.OutputPanes().Where(p => !OutputPaneIds.IsProtected(p.Id)).Select(p => new OutputPaneInfo { Name = p.Name, Lines = p.Lines }).ToArray(), ct);

    public Task<OutputPage> ReadOutputAsync(string pane, int tail, int offset, CancellationToken ct) => ui.RunAsync(() =>
    {
        var target = ResolvePane(pane);
        if (target.Lines is not { } total)
            throw new BridgeException(BridgeErrorCode.Busy, $"The text of the '{target.Name}' pane is not available yet.",
                "Show it once in Visual Studio's Output window (View → Output), then retry.");
        var page = OutputPaging.Page(target.Name, total, tail, offset, (first, count) => dte.OutputLines(target.Id, first, count));
        if (!MaskOn) return page;
        var anyMasked = false;
        page.Lines = page.Lines.Select(line =>
        {
            var hidden = SensitiveValueMask.MaskText(line, out var changed);
            anyMasked |= changed;
            return hidden;
        }).ToArray();
        if (anyMasked) page.Masked = true;
        return page;
    }, ct);

    public Task<ErrorListPage> GetErrorListAsync(ErrorSeverity[] severities, int offset, int max, CancellationToken ct) =>
        ui.RunAsync(() => ErrorListShaping.Page(dte.ErrorItems(), severities ?? [], SolutionDir, offset, max), ct);

    public const int MaxBuildSeconds = 1800;

    public BuildMonitor Builds { get; } = builds ?? new BuildMonitor();

    public async Task<BuildResult> BuildAsync(string? project, int timeoutSec, CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(Math.Min(Math.Max(1, timeoutSec), MaxBuildSeconds));
        var started = Stopwatch.StartNew();
        var completion = await ui.RunAsync(() =>
        {
            RequireMode(DebuggerMode.Design);
            if (dte.IsBuilding) return Builds.Current;      // join the user's (or an earlier) build
            var unique = project is null ? null : ResolveProject(project);
            var current = Builds.Current;                    // tracked before it starts, so OnBuildDone cannot be missed
            dte.StartBuild(unique);
            log.Info($"Build started: {unique ?? "solution"}");
            return current;
        }, ct).ConfigureAwait(false);

        var finished = await Task.WhenAny(completion, Task.Delay(timeout, ct)).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (finished != completion) return new BuildResult { State = BuildState.Building, DurationMs = started.ElapsedMilliseconds };

        var failed = await completion.ConfigureAwait(false);
        return new BuildResult { State = BuildState.Done, Succeeded = failed == 0, FailedProjects = failed, DurationMs = started.ElapsedMilliseconds };
    }

    /// <summary>
    /// Stale builds in scope: <paramref name="project"/>, else every project containing <paramref name="file"/>, else the
    /// startup project. Never fails for an unknown project or file (nothing to warn about) and never reveals paths or values.
    /// </summary>
    public Task<BuildWarning[]> CheckBuildFreshnessAsync(string? project, string? file, CancellationToken ct) => ui.RunAsync(() =>
    {
        IReadOnlyList<string> scope;
        if (!string.IsNullOrWhiteSpace(project))
        {
            var match = dte.ProjectNames().FirstOrDefault(n => IsProjectNamed(n, project!));
            scope = match is null ? [] : [match];
        }
        else if (!string.IsNullOrWhiteSpace(file))
        {
            // Only files inside the solution are looked up: this must not become an oracle for other paths.
            var absolute = PathUtil.ToAbsolute(SolutionDir, file!);
            scope = PathUtil.IsUnder(SolutionDir, absolute) ? dte.ProjectsContaining(absolute) : [];
        }
        else
        {
            scope = dte.StartupProjects.ToArray();
        }

        // Visual Studio builds an out-of-date project itself when debugging starts (Options > Projects and Solutions > Build and
        // Run > On Run, when projects are out of date = Always build), so while nothing runs there is nothing to warn about or offer.
        // A running program keeps its old binary, and with "Never build" / "Prompt to build" Visual Studio would not build alone.
        var vsBuildsOnStart = vsBuildsOnRun?.Invoke() == true && dte.Mode == DebuggerMode.Design;

        var stale = new List<BuildWarning>();
        foreach (var unique in scope)
        {
            var isStale = !vsBuildsOnStart && StaleBuildChecker.IsStale(BuildInfoOf(unique));
            _notifier?.Observe(unique, isStale);
            if (isStale) stale.Add(StaleWarning(unique));
        }

        return stale.ToArray();
    }, ct);

    private static BuildWarning StaleWarning(string unique)
    {
        var name = Path.GetFileNameWithoutExtension(unique);
        return new BuildWarning
        {
            Code = BuildWarningCodes.StaleBuild, Project = name,
            Message = $"{name} — sources are newer than the built output; the program may not match the code. Build the project before trusting breakpoints.",
        };
    }

    private static bool IsProjectNamed(string unique, string name) =>
        string.Equals(unique, name, StringComparison.OrdinalIgnoreCase)
        || string.Equals(Path.GetFileNameWithoutExtension(unique), name, StringComparison.OrdinalIgnoreCase);

    private string ResolveProject(string project)
    {
        var names = dte.ProjectNames();
        return names.FirstOrDefault(n => string.Equals(n, project, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileNameWithoutExtension(n), project, StringComparison.OrdinalIgnoreCase))
            ?? throw new BridgeException(BridgeErrorCode.NotFound, $"No project named '{project}' in the solution.",
                "Projects: " + string.Join(", ", names.Select(Path.GetFileNameWithoutExtension)));
    }

    private RawOutputPane ResolvePane(string? name)
    {
        var wanted = (name ?? "").Trim();
        var panes = dte.OutputPanes();
        Guid? alias = wanted.ToUpperInvariant() switch { "BUILD" => OutputPaneIds.Build, "DEBUG" => OutputPaneIds.Debug, _ => null };
        var match = (alias is { } id ? panes.FirstOrDefault(p => p.Id == id) : null)
            ?? panes.FirstOrDefault(p => string.Equals(p.Name, wanted, StringComparison.OrdinalIgnoreCase))
            ?? throw new BridgeException(BridgeErrorCode.NotFound, $"No Output pane named '{wanted}'.",
                "Panes: " + string.Join(", ", panes.Where(p => !OutputPaneIds.IsProtected(p.Id)).Select(p => p.Name)));
        if (OutputPaneIds.IsProtected(match.Id))
            throw new BridgeException(BridgeErrorCode.Denied, "The extension's own Output panes cannot be read.",
                "They hold this session's log and audit trail.", DeniedReasons.Policy);
        return match;
    }

    private SourceLocation ToLocation(RawFrame f) => new()
    {
        File = f.File is null ? "" : PathUtil.ToRelative(SolutionDir, f.File), Line = f.Line ?? 0, Function = f.Function,
    };

    private static string[] SourceOf(RawFrame f) => f.File is not null && f.Line is { } line ? PathUtil.SourceContext(f.File, line) : [];

    private static BridgeException NoSuchThread(int id) =>
        new(BridgeErrorCode.NotFound, $"No thread with id {id}.", "Call threads to list them.");

    private StackFrameInfo[] ToFrames(IReadOnlyList<RawFrame> frames) => frames.Select((f, i) => new StackFrameInfo
    {
        Index = i, Function = f.Function, Line = f.Line, Language = f.Language,
        File = f.File is null ? null : PathUtil.ToRelative(SolutionDir, f.File),
    }).ToArray();

    public Task<VariableInfo[]> GetVariablesAsync(VariableScope scope, string? path, int offset, int maxItems, CancellationToken ct) => InBreak(() =>
    {
        if (offset < 0 || offset > MaxVariableOffset)
            throw new BridgeException(BridgeErrorCode.InvalidArgument, $"offset must be between 0 and {MaxVariableOffset}.", "Narrow the path instead of paging this deep.");
        var skip = offset;
        var take = Math.Min(Math.Max(0, maxItems), MaxVariablePage);
        var items = path is null
            ? dte.Variables(scope, skip + take)
            : dte.Members(ExpressionPolicy.PrepareSideEffectFree(path), skip + take);
        return items.Skip(skip).Take(take).Select(v => Shape(v, path)).ToArray();
    }, ct);

    public static void ValidateSetValue(string? expression, string? value)
    {
        ExpressionPolicy.RequireAssignableTarget(expression);
        ExpressionPolicy.RequireMutationText(value, "value");
    }

    public static void ValidateSideEffectEvaluation(string? expression) => ExpressionPolicy.RequireMutationText(expression, "expression");

    public Task<EvaluateResult> EvaluateAsync(string expression, bool allowSideEffects, CancellationToken ct) =>
        EvaluateAsync(expression, allowSideEffects, expected: null, ct);

    /// <param name="expected">When set, where the program was when the user approved; any debugger event or line change since then refuses the call.</param>
    public Task<EvaluateResult> EvaluateAsync(string expression, bool allowSideEffects, ConsentPoint? expected, CancellationToken ct)
    {
        if (allowSideEffects) ValidateSideEffectEvaluation(expression);
        return InBreak(() =>
        {
            RequireUnchanged(expected);
            return Shape(expression, dte.Evaluate(allowSideEffects ? expression.Trim() : ExpressionPolicy.PrepareSideEffectFree(expression)));
        }, ct);
    }

    public Task<EvaluateResult> SetValueAsync(string expression, string value, CancellationToken ct) =>
        SetValueAsync(expression, value, expected: null, ct);

    /// <param name="expected">When set, where the program was when the user approved; any debugger event or line change since then refuses the call.</param>
    public Task<EvaluateResult> SetValueAsync(string expression, string value, ConsentPoint? expected, CancellationToken ct)
    {
        ValidateSetValue(expression, value);
        return InBreak(() =>
        {
            RequireUnchanged(expected);
            return Shape(expression, dte.SetValue(expression.Trim(), value.Trim()));
        }, ct);
    }

    /// <summary>Consent covers the frame the question named: if the program resumed or broke elsewhere since, refuse.</summary>
    private void RequireUnchanged(ConsentPoint? expected)
    {
        if (expected is not null && (seq.Current != expected.Seq || CurrentPosition() != expected.Position))
            throw new BridgeException(BridgeErrorCode.WrongMode,
                "The program state changed while waiting for approval (it resumed or stopped elsewhere). Nothing was changed.",
                "Check where the program is now (debug_status), then call again if it still makes sense.");
    }

    /// <summary>Shown in consent questions ("… in OrderService.Submit"). Requires Break mode, so a wrong-mode call fails before the user is asked.</summary>
    public Task<(string Function, ConsentPoint Point)> GetBreakLocationAsync(CancellationToken ct) => InBreak(() =>
    {
        var index = dte.CurrentFrameIndex;
        var frames = dte.Stack(index + 1);
        return (frames.Count > index ? frames[index].Function : "the current frame", new ConsentPoint(seq.Current, PositionOf(frames, index)));
    }, ct);

    /// <summary>File and line of the selected frame; the user can move the instruction pointer without a debugger event.</summary>
    private string CurrentPosition()
    {
        var index = dte.CurrentFrameIndex;
        return PositionOf(dte.Stack(index + 1), index);
    }

    private static string PositionOf(IReadOnlyList<RawFrame> frames, int index) =>
        frames.Count > index ? $"{frames[index].File}:{frames[index].Line}" : "";

    private EvaluateResult Shape(string expression, EvaluateResult r)
    {
        var masked = MaskOn ? SensitiveValueMask.MaskValue(expression, r.Value, r.Type) : new MaskedValue(r.Value, false);
        var (value, truncated) = ValueShaper.Truncate(masked.Value);
        return new EvaluateResult { IsValid = r.IsValid, Value = value, Type = r.Type, Truncated = truncated, Masked = masked.Masked ? true : null };
    }

    /// <summary>Called by the event pump on every break (UI thread): starts a new epoch, then builds the snapshot for it.</summary>
    public BreakSnapshot OnEnterBreak(BreakReason reason)
    {
        Interlocked.Increment(ref _epoch);
        var byBreakpoint = reason == BreakReason.Breakpoint;
        if (_runTo.TargetId is { } target && reason == BreakReason.Breakpoint && dte.LastHitBreakpoint is { } hit && IdOf(hit) == target)
            reason = BreakReason.RunTo;
        var snapshot = BuildSnapshot(reason);
        ReleaseRunTo(null);   // wherever it stopped, the run-to is over
        if (byBreakpoint && dte.LastHitBreakpoint is { } hit2 && IdOf(hit2) is var hitId && _temporary.Remove(hitId))
        {
            dte.RemoveBreakpoints(b => IdOf(b) == hitId);   // the snapshot above still names it
            log.Info($"Temporary breakpoint {hitId} removed after its break");
        }

        return snapshot;
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
            Location = top is null ? null : ToLocation(top),
            Source = top is null ? [] : SourceOf(top),
            Args = dte.Variables(VariableScope.Args, 20).Select(v => Shape(v, null)).ToArray(),
            StackTop = frames.Select(f => f.Line is { } l ? $"{f.Function}:{l}" : f.Function).ToArray(),
            ThreadId = dte.CurrentThreadId,
            Epoch = Epoch,
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

    /// <param name="parent">The path the members were read under: a member of something sensitive is judged by that path too.</param>
    private VariableInfo Shape(VariableInfo v, string? parent)
    {
        var masked = MaskOn ? SensitiveValueMask.MaskValue(parent is null ? v.Name : parent + "." + v.Name, v.Value, v.Type) : new MaskedValue(v.Value, false);
        var (value, truncated) = ValueShaper.Truncate(masked.Value);
        return new VariableInfo { Name = v.Name, Type = v.Type, Value = value, HasChildren = v.HasChildren, Truncated = truncated || v.Truncated, Masked = masked.Masked ? true : null };
    }

    private BreakpointInfo ToInfo(RawBreakpoint b) => new()
    {
        Id = IdOf(b),
        File = b.File is null ? null : PathUtil.ToRelative(SolutionDir, b.File),
        Line = b.Line, Function = b.Function, Condition = b.Condition, ConditionKind = b.ConditionKind,
        HitCount = b.HitCount, HitCountKind = b.HitCountKind, LogMessage = b.LogMessage, Enabled = b.Enabled,
        Bound = b.BoundCount > 0,
        Temporary = _temporary.Contains(IdOf(b)) ? true : null,
        BindError = b.BoundCount > 0 ? null
            : dte.Mode == DebuggerMode.Design ? "Pending: binds when debugging starts."
            : "Will not be hit: no executable code at this location or symbols are not loaded.",
    };
}
