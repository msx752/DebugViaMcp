using FaultDemo;

// Target for the M3 end-to-end run. "loop" (default) runs until killed, for attach/detach.
// "crash <seconds>" throws an unhandled exception after that delay, for the exception tool.
var mode = args.Length > 0 ? args[0] : "loop";
var delay = TimeSpan.FromSeconds(args.Length > 1 && int.TryParse(args[1], out var s) ? s : 20);
Console.WriteLine($"FaultDemo {mode} (pid {Environment.ProcessId})");

var started = DateTime.UtcNow;
for (var tick = 0; ; tick++)
{
    Ticker.Work(tick);
    if (mode == "crash" && DateTime.UtcNow - started > delay) Ticker.Fail(tick);
    Thread.Sleep(500);
}
