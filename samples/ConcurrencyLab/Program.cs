using ConcurrencyLab;

// Target for the concurrency e2e scenarios (docs/design/concurrency-lab-design.md): three deliberate bugs that happen the same way on every run.
//   race      two workers claim the same order and both charge it
//   deadlock  two workers take the same two locks in opposite order and the program hangs
//   crash     the 7th order fails on a thread-pool task; Handle rethrows it as another exception, so the default stop is at the wrapper, not at the cause
// The scenario is the first argument, or (a start from Visual Studio has none) the text of scenario.txt next to the executable.
var scenario = args.Length > 0 ? args[0] : ReadScenarioFile();
Console.WriteLine($"ConcurrencyLab {scenario} (pid {Environment.ProcessId})");

switch (scenario)
{
    case "race":
        Scenarios.Race();
        break;
    case "deadlock":
        Scenarios.Deadlock();
        break;
    case "crash":
        Scenarios.Crash();
        break;
    default:
        Console.Error.WriteLine("Usage: ConcurrencyLab race|deadlock|crash");
        return 2;
}

return 0;

static string ReadScenarioFile()
{
    var file = Path.Combine(AppContext.BaseDirectory, "scenario.txt");
    return File.Exists(file) ? File.ReadAllText(file).Trim() : "race";
}
