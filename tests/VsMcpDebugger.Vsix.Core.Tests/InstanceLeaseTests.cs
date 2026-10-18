using VsMcpDebugger.Vsix.Core;
using Xunit;

namespace VsMcpDebugger.Vsix.Core.Tests;

public class InstanceLeaseTests
{
    [Fact]
    public void Free_lease_is_acquired_by_the_first_connection()
    {
        var lease = new InstanceLease();

        var attempt = lease.TryAcquire(1, "claude-code", @"D:\proj");

        Assert.Equal(LeaseOutcome.Acquired, attempt.Outcome);
        Assert.Equal(1, attempt.Holder!.ConnectionId);
        Assert.Equal("claude-code", lease.Holder!.Client);
        Assert.Equal(@"D:\proj", lease.Holder.Folder);
    }

    [Fact]
    public void Same_connection_acquiring_again_is_already_held_and_raises_no_event()
    {
        var lease = new InstanceLease();
        lease.TryAcquire(1, "a", null);
        var raised = 0;
        lease.Changed += (_, _) => raised++;

        var attempt = lease.TryAcquire(1, "a", null);

        Assert.Equal(LeaseOutcome.AlreadyHeld, attempt.Outcome);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void Other_connection_is_held_by_other_and_sees_the_holder()
    {
        var lease = new InstanceLease();
        lease.TryAcquire(1, "other-agent", @"D:\x");

        var attempt = lease.TryAcquire(2, "claude-code", null);

        Assert.Equal(LeaseOutcome.HeldByOther, attempt.Outcome);
        Assert.Equal("other-agent", attempt.Holder!.Client);
        Assert.Equal(@"D:\x", attempt.Holder.Folder);
        Assert.Equal(1, lease.Holder!.ConnectionId);
    }

    [Fact]
    public void Release_by_the_holder_frees_it_and_raises_changed()
    {
        var lease = new InstanceLease();
        lease.TryAcquire(1, "a", null);
        var raised = 0;
        lease.Changed += (_, _) => raised++;

        Assert.True(lease.Release(1));

        Assert.Null(lease.Holder);
        Assert.Equal(1, raised);
        Assert.Equal(LeaseOutcome.Acquired, lease.TryAcquire(2, "b", null).Outcome);
    }

    [Fact]
    public void Release_by_another_connection_changes_nothing()
    {
        var lease = new InstanceLease();
        lease.TryAcquire(1, "a", null);
        var raised = 0;
        lease.Changed += (_, _) => raised++;

        Assert.False(lease.Release(2));

        Assert.Equal(1, lease.Holder!.ConnectionId);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void Release_of_a_free_lease_is_a_no_op()
    {
        var lease = new InstanceLease();
        var raised = 0;
        lease.Changed += (_, _) => raised++;

        Assert.False(lease.Release(1));

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Changed_is_raised_once_per_real_change()
    {
        var lease = new InstanceLease();
        var raised = 0;
        lease.Changed += (_, _) => raised++;

        lease.TryAcquire(1, "a", null);
        lease.TryAcquire(1, "a", null);
        lease.TryAcquire(2, "b", null);
        lease.Release(2);
        lease.Release(1);
        lease.Release(1);

        Assert.Equal(2, raised);
    }

    [Fact]
    public async Task Handler_of_Changed_may_call_back_into_the_lease_without_deadlock()
    {
        var lease = new InstanceLease();
        LeaseHolder? seen = null;
        lease.Changed += (_, _) => seen = lease.Holder;

        var work = Task.Run(() => lease.TryAcquire(1, "a", null));
        var done = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(work, done);
        Assert.Equal(1, seen!.ConnectionId);
    }

    [Fact]
    public async Task Concurrent_acquire_has_exactly_one_winner()
    {
        var lease = new InstanceLease();
        using var barrier = new Barrier(64);

        var attempts = await Task.WhenAll(Enumerable.Range(1, 64).Select(id => Task.Factory.StartNew(() =>
        {
            // Dedicated threads: 64 blocked waiters would otherwise starve the shared thread pool.
            barrier.SignalAndWait();
            return lease.TryAcquire(id, "client" + id, null);
        }, TaskCreationOptions.LongRunning)));

        Assert.Single(attempts, a => a.Outcome == LeaseOutcome.Acquired);
        Assert.Equal(63, attempts.Count(a => a.Outcome == LeaseOutcome.HeldByOther));
        Assert.All(attempts, a => Assert.Equal(lease.Holder!.ConnectionId, a.Holder!.ConnectionId));
    }
}
