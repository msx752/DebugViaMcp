using DebugViaMcp.Protocol;

namespace DebugViaMcp.Mcp.Session;

/// <summary>
/// Mirror of the connected IDE debugger. Receives pushed events and lets tools wait for the first
/// break whose sequence number is greater than the one returned by the command they issued, so a
/// break that arrives before the wait begins is never lost.
/// </summary>
public sealed class SessionState : IDebugBridgeEvents
{
    private readonly Lock _gate = new();
    private readonly List<(long AfterSeq, TaskCompletionSource<WaitOutcome> Tcs)> _waiters = [];

    public DebuggerMode Mode { get; private set; } = DebuggerMode.Design;
    public bool IsConnected { get; private set; }
    public long LastSeq { get; private set; }
    public BreakEvent? LastBreak { get; private set; }
    public long LastEndSeq { get; private set; }
    public SelectedFrame Frame { get; private set; } = new(0, null);

    public void SelectFrame(int index, int? threadId)
    {
        lock (_gate) Frame = new SelectedFrame(index, threadId);
    }

    public Task OnFrameChangedAsync(FrameChangedEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        lock (_gate)
        {
            LastSeq = Math.Max(LastSeq, e.Seq);
            Frame = new SelectedFrame(e.FrameIndex, e.ThreadId);
        }

        return Task.CompletedTask;
    }

    /// <summary>Sequence observed right before the latest resume command; debug_wait waits for a break after it.</summary>
    public long LastResumeSeq { get; private set; }

    /// <summary>True after the IDE revoked access (Disconnect AI) on the current connection.</summary>
    public bool AccessRevoked { get; private set; }

    public Task OnAccessRevokedAsync()
    {
        lock (_gate) AccessRevoked = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs a command that can resume or change the debugger's mode and records the sequence it returned as the
    /// resume point, so every such command is covered by construction (no tool has to remember to mark it).
    /// </summary>
    public async Task<long> IssueAsync(Func<Task<long>> command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var seq = await command().ConfigureAwait(false);
        MarkResumed(seq);
        return seq;
    }

    public void MarkResumed(long seq)
    {
        lock (_gate)
        {
            LastResumeSeq = Math.Max(LastResumeSeq, seq);
        }
    }

    private int _watchCount;

    /// <summary>How many entries this agent's watch list has, as the extension last said (watch_add, watch_remove, watch_list). Zero means a stop asks the extension for nothing.</summary>
    public int WatchCount => Volatile.Read(ref _watchCount);

    public void SetWatchCount(int count) => Volatile.Write(ref _watchCount, Math.Max(count, 0));

    public void AddWatches(int delta)
    {
        int current, next;
        do
        {
            current = Volatile.Read(ref _watchCount);
            next = Math.Max(current + delta, 0);
        }
        while (Interlocked.CompareExchange(ref _watchCount, next, current) != current);
    }

    /// <summary>Longest a tool may wait for a break; the user's Options (sent in the handshake) can lower or raise it.</summary>
    public static readonly TimeSpan DefaultMaxWait = TimeSpan.FromSeconds(120);

    /// <summary>Longest vs_build may wait; see <see cref="DefaultMaxWait"/>.</summary>
    public static readonly TimeSpan DefaultMaxBuild = TimeSpan.FromSeconds(600);

    public TimeSpan MaxWait { get; private set; } = DefaultMaxWait;
    public TimeSpan MaxBuild { get; private set; } = DefaultMaxBuild;

    /// <summary>Applies the limits from the handshake; 0 (an older extension) keeps the built-in ones.</summary>
    public void SetLimits(int maxWaitSeconds, int maxBuildSeconds)
    {
        lock (_gate)
        {
            MaxWait = maxWaitSeconds > 0 ? TimeSpan.FromSeconds(maxWaitSeconds) : DefaultMaxWait;
            MaxBuild = maxBuildSeconds > 0 ? TimeSpan.FromSeconds(maxBuildSeconds) : DefaultMaxBuild;
        }
    }

    public void Reset(DebuggerMode mode, long seq)
    {
        lock (_gate)
        {
            Mode = mode;
            LastSeq = seq;
            LastResumeSeq = seq;
            LastEndSeq = 0;
            LastBreak = null;
            Frame = new SelectedFrame(0, null);
            IsConnected = true;
            AccessRevoked = false;
        }
    }

    /// <summary>
    /// Brings mode and sequence in step with what Visual Studio reports. Events of a period in which another agent held the
    /// Visual Studio went to that agent only, so this session may have missed mode changes.
    /// </summary>
    public void Sync(DebuggerMode mode, long seq)
    {
        lock (_gate)
        {
            if (Mode != mode)
            {
                Mode = mode;
                if (mode == DebuggerMode.Run) LastResumeSeq = Math.Max(LastResumeSeq, seq);
                if (mode == DebuggerMode.Design) CompleteAll(new WaitOutcome(WaitOutcomeKind.Ended, null));
            }

            LastSeq = Math.Max(LastSeq, seq);
        }
    }

    private bool _connectionRestored;

    /// <summary>The pipe was lost and re-opened by the MCP server: holds and approvals of the old connection are gone.</summary>
    public void MarkConnectionRestored()
    {
        lock (_gate) _connectionRestored = true;
    }

    /// <summary>True once after a restore, so the agent is told exactly once.</summary>
    public bool TakeConnectionRestored()
    {
        lock (_gate)
        {
            var was = _connectionRestored;
            _connectionRestored = false;
            return was;
        }
    }

    public void MarkDisconnected()
    {
        lock (_gate)
        {
            IsConnected = false;
            CompleteAll(new WaitOutcome(WaitOutcomeKind.Disconnected, null));
        }
    }

    public Task OnModeChangedAsync(ModeChangedEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        lock (_gate)
        {
            Mode = e.Mode;
            LastSeq = Math.Max(LastSeq, e.Seq);
            // Also covers a resume the user did in Visual Studio itself (F5 in the UI).
            if (e.Mode == DebuggerMode.Run) LastResumeSeq = Math.Max(LastResumeSeq, e.Seq);
            if (e.Mode == DebuggerMode.Design)
            {
                LastEndSeq = Math.Max(LastEndSeq, e.Seq);
                CompleteAll(new WaitOutcome(WaitOutcomeKind.Ended, null));
            }
        }

        return Task.CompletedTask;
    }

    public Task OnBreakAsync(BreakEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        lock (_gate)
        {
            Mode = DebuggerMode.Break;
            LastSeq = Math.Max(LastSeq, e.Seq);
            LastBreak = e;
            Frame = new SelectedFrame(0, e.Snapshot.ThreadId);
            for (var i = _waiters.Count - 1; i >= 0; i--)
            {
                if (e.Seq > _waiters[i].AfterSeq)
                {
                    _waiters[i].Tcs.TrySetResult(new WaitOutcome(WaitOutcomeKind.Break, e));
                    _waiters.RemoveAt(i);
                }
            }
        }

        return Task.CompletedTask;
    }

    public async Task<WaitOutcome> WaitForBreakAfterAsync(long seq, TimeSpan timeout, CancellationToken ct)
    {
        TaskCompletionSource<WaitOutcome> tcs;
        lock (_gate)
        {
            if (!IsConnected) return new WaitOutcome(WaitOutcomeKind.Disconnected, null);
            // Whatever happened after 'seq' and is most recent wins: the session ended, or it broke.
            var lastBreakSeq = LastBreak?.Seq ?? 0;
            if (LastEndSeq > seq && LastEndSeq > lastBreakSeq) return new WaitOutcome(WaitOutcomeKind.Ended, null);
            if (LastBreak is { } last && last.Seq > seq) return new WaitOutcome(WaitOutcomeKind.Break, last);

            tcs = new TaskCompletionSource<WaitOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((seq, tcs));
        }

        try
        {
            return await tcs.Task.WaitAsync(timeout, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return new WaitOutcome(WaitOutcomeKind.Timeout, null);
        }
        finally
        {
            lock (_gate)
            {
                _waiters.RemoveAll(w => w.Tcs == tcs);
            }
        }
    }

    private void CompleteAll(WaitOutcome outcome)
    {
        foreach (var (_, tcs) in _waiters) tcs.TrySetResult(outcome);
        _waiters.Clear();
    }
}
