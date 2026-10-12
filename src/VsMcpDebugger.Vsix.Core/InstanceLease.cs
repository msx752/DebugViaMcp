using System;

namespace VsMcpDebugger.Vsix.Core;

public sealed class LeaseHolder(int connectionId, string client, string? folder)
{
    public int ConnectionId { get; } = connectionId;
    public string Client { get; } = client;
    public string? Folder { get; } = folder;
}

public enum LeaseOutcome
{
    Acquired,
    AlreadyHeld,
    HeldByOther,
}

public sealed class LeaseAttempt(LeaseOutcome outcome, LeaseHolder? holder)
{
    public LeaseOutcome Outcome { get; } = outcome;

    /// <summary>The holder after the attempt: the caller on <see cref="LeaseOutcome.Acquired"/> and <see cref="LeaseOutcome.AlreadyHeld"/>, the other agent on <see cref="LeaseOutcome.HeldByOther"/>.</summary>
    public LeaseHolder? Holder { get; } = holder;
}

/// <summary>
/// Which connection holds this Visual Studio. State only, no I/O; shared by every connection of one instance.
/// <see cref="Changed"/> is raised outside the internal lock, so handlers may call back into the lease.
/// </summary>
public sealed class InstanceLease
{
    private readonly object _gate = new();
    private LeaseHolder? _holder;

    public event EventHandler? Changed;

    public LeaseHolder? Holder
    {
        get { lock (_gate) return _holder; }
    }

    public LeaseAttempt TryAcquire(int connectionId, string client, string? folder)
    {
        LeaseHolder current;
        lock (_gate)
        {
            if (_holder is { } held)
                return new LeaseAttempt(held.ConnectionId == connectionId ? LeaseOutcome.AlreadyHeld : LeaseOutcome.HeldByOther, held);
            current = _holder = new LeaseHolder(connectionId, client, folder);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return new LeaseAttempt(LeaseOutcome.Acquired, current);
    }

    /// <summary>Frees the lease whoever holds it (Disconnect AI).</summary>
    public void Clear()
    {
        lock (_gate)
        {
            if (_holder is null) return;
            _holder = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <returns>True when <paramref name="connectionId"/> was the holder.</returns>
    public bool Release(int connectionId)
    {
        lock (_gate)
        {
            if (_holder is not { } held || held.ConnectionId != connectionId) return false;
            _holder = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
