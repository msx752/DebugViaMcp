using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VsMcpDebugger.Vsix.Core;

public enum ConsentOutcome { AllowedOnce, AllowedForSession, AllowedBySessionGrant, Denied, TimedOut, Cancelled, Disconnected }

/// <summary>
/// Asks the user before gated operations. One instance per Visual Studio session: at most one prompt is open at a
/// time. Each request's timeout starts when it is queued. "Allow for session" grants live in the caller's
/// <see cref="ConsentSession"/> (one per connection).
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Lives for the whole Visual Studio session; a SemaphoreSlim whose wait handle is never read holds no unmanaged resources.")]
public sealed class ConsentGate(IConsentPrompt prompt, Func<TimeSpan> timeout, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private readonly SemaphoreSlim _one = new(1, 1);
    private readonly List<ConsentSession> _sessions = [];
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public ConsentSession CreateSession()
    {
        var session = new ConsentSession(this);
        lock (_sessions) _sessions.Add(session);
        return session;
    }

    /// <summary>Drops every "Allow for session" grant, e.g. after a permission setting changed.</summary>
    public void ClearGrants()
    {
        ConsentSession[] all;
        lock (_sessions) all = _sessions.ToArray();
        foreach (var s in all) s.ClearGrants();
    }

    internal void Remove(ConsentSession session)
    {
        lock (_sessions) _sessions.Remove(session);
    }

    public async Task<ConsentOutcome> RequestAsync(ConsentSession session, Operation operation, string text, CancellationToken ct)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));
        if (session.IsGranted(operation)) return ConsentOutcome.AllowedBySessionGrant;

        var limit = timeout();
        var revoked = session.RevokedToken;
        using var expiry = new CancellationTokenSource();
        using var done = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, revoked, expiry.Token);
        _ = ExpireAsync(limit, expiry, done.Token);

        var entered = false;
        try
        {
            await _one.WaitAsync(linked.Token).ConfigureAwait(false);
            entered = true;
            if (session.IsGranted(operation)) return ConsentOutcome.AllowedBySessionGrant;

            var choice = await prompt.AskAsync(text, limit, linked.Token).ConfigureAwait(false);
            switch (choice)
            {
                case ConsentChoice.AllowSession:
                    session.Grant(operation);
                    return ConsentOutcome.AllowedForSession;
                case ConsentChoice.AllowOnce:
                    return ConsentOutcome.AllowedOnce;
                default:
                    return ConsentOutcome.Denied;
            }
        }
        catch (OperationCanceledException)
        {
            if (revoked.IsCancellationRequested) return ConsentOutcome.Disconnected;
            if (ct.IsCancellationRequested) return ConsentOutcome.Cancelled;
            return ConsentOutcome.TimedOut;
        }
        finally
        {
            done.Cancel();
            if (entered) _one.Release();
        }
    }

    private async Task ExpireAsync(TimeSpan limit, CancellationTokenSource expiry, CancellationToken stop)
    {
        try
        {
            await _delay(limit, stop).ConfigureAwait(false);
            expiry.Cancel();
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }
}

/// <summary>Per-connection consent state: "Allow for session" grants and a revocation signal.</summary>
public sealed class ConsentSession : IDisposable
{
    private readonly ConsentGate _owner;
    private readonly HashSet<Operation> _granted = [];
    private CancellationTokenSource _revoked = new();

    internal ConsentSession(ConsentGate owner) => _owner = owner;

    internal CancellationToken RevokedToken
    {
        get { lock (_granted) return _revoked.Token; }
    }

    internal bool IsGranted(Operation operation)
    {
        lock (_granted) return _granted.Contains(operation);
    }

    internal void Grant(Operation operation)
    {
        lock (_granted) _granted.Add(operation);
    }

    internal void ClearGrants()
    {
        lock (_granted) _granted.Clear();
    }

    /// <summary>Pending requests end as <see cref="ConsentOutcome.Disconnected"/>; grants are cleared.</summary>
    public void Revoke()
    {
        CancellationTokenSource old;
        lock (_granted)
        {
            old = _revoked;
            _revoked = new CancellationTokenSource();
            _granted.Clear();
        }

        // Not disposed: requests in flight may still register on its token.
        old.Cancel();
    }

    public void Dispose()
    {
        Revoke();
        _owner.Remove(this);
    }
}
