namespace FaultDemo;

public static class Ticker
{
    /// <summary>Called every 500 ms; the E2E run puts a function breakpoint on FaultDemo.Ticker.Work.</summary>
    public static int Work(int tick)
    {
        var square = tick * tick;
        if (tick % 10 == 0) Console.WriteLine($"tick {tick}: {square}");
        return square;
    }

    /// <summary>Throws an unhandled exception with an inner one, for the exception tool.</summary>
    public static void Fail(int tick)
    {
        try
        {
            throw new ArgumentException($"tick {tick} is out of range");
        }
        catch (ArgumentException inner)
        {
            throw new InvalidOperationException("FaultDemo crashed on purpose", inner);
        }
    }
}
