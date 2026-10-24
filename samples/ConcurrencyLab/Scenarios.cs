namespace ConcurrencyLab;

public sealed class Order(int id)
{
    public int Id { get; } = id;
    public string Status { get; set; } = "Pending";
    public int ChargeCount { get; set; }
}

/// <summary>A lock with a name, so a debugger shows which one a thread holds and which one it waits for. A field, not a property: reading it on a thread that waits for a lock must not need code to run.</summary>
public sealed class NamedLock(string name)
{
#pragma warning disable CA1051 // Visible instance field: that is the point of this sample.
    public readonly string Name = name;
#pragma warning restore CA1051
}

/// <summary>What Handle makes of a failed validation: a different exception that hides where the failure began.</summary>
public sealed class OrderFailedException(string message, Exception inner) : Exception(message, inner);

public static class Scenarios
{
    private static readonly NamedLock LockA = new("A");
    private static readonly NamedLock LockB = new("B");

    /// <summary>Check-then-act on a shared order: both workers see Pending, then both charge. The barrier makes the overlap certain.</summary>
    public static void Race()
    {
        var order = new Order(1);
        using var barrier = new Barrier(2);
        var workers = Enumerable.Range(1, 2).Select(n => new Thread(() => Claim(order, barrier)) { Name = $"worker-{n}" }).ToList();
        workers.ForEach(t => t.Start());
        workers.ForEach(t => t.Join());
        Console.WriteLine($"order {order.Id}: charged {order.ChargeCount} times");
        Thread.Sleep(TimeSpan.FromSeconds(5));   // time to look at it
    }

    private static void Claim(Order order, Barrier barrier)
    {
        if (order.Status != "Pending") return;
        barrier.SignalAndWait();   // both workers have checked
        Charge(order);
    }

    private static void Charge(Order order)
    {
        lock (order)
        {
            order.ChargeCount++;
            order.Status = "Paid";
        }
    }

    /// <summary>Lock-order inversion: each worker holds its first lock before either asks for its second, so neither can go on.</summary>
    public static void Deadlock()
    {
        using var barrier = new Barrier(2);
        var workers = new[]
        {
            new Thread(() => Transfer(LockA, LockB, barrier)) { Name = "transfer-1" },
            new Thread(() => Transfer(LockB, LockA, barrier)) { Name = "transfer-2" },
        };
        foreach (var t in workers) t.Start();
        foreach (var t in workers) t.Join();   // never returns
    }

    private static void Transfer(NamedLock first, NamedLock second, Barrier barrier)
    {
        lock (first)
        {
            barrier.SignalAndWait();   // both workers hold their first lock
            lock (second)
            {
                Console.WriteLine($"moved from {first.Name} to {second.Name}");
            }
        }
    }

    /// <summary>The 7th order fails inside Validate on a thread-pool task; Handle rethrows it as another exception (where Visual Studio stops by default) and the main thread gets it from Wait().</summary>
    public static void Crash()
    {
        var task = Task.Run(ProcessAll);
        Console.WriteLine("processing orders");
        task.Wait();   // throws the AggregateException on the main thread (reached only when the debugger did not stop before)
    }

    private static void ProcessAll()
    {
        for (var id = 1; id <= 10; id++) Handle(new Order(id));
    }

    private static void Handle(Order order)
    {
        try
        {
            Validate(order);
        }
        catch (InvalidOperationException e)
        {
            throw new OrderFailedException($"order {order.Id} failed", e);
        }
    }

    private static void Validate(Order order)
    {
        if (order.Id == 7) throw new InvalidOperationException("stock below zero");
    }
}
