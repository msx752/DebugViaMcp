using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace DebugViaMcp.TestSupport;

/// <summary>
/// A test host that is killed (a hang, a Stop-Process) cannot delete the temp folders its tests made. Every test process removes the ones that
/// are ours and old when it starts, so they never pile up. "Old" keeps clear of the folders of a test process that is running right now.
/// Compiled into every test project (linked), not a library.
/// </summary>
internal static partial class StaleTempSweep
{
    private static readonly TimeSpan MinimumAge = TimeSpan.FromHours(2);

    /// <summary>The temp folders our tests and e2e drivers create; anything else in the temp folder is never touched.</summary>
    [GeneratedRegex(@"^(debugviamcp-(gate|host|diag|test|lr|dw|audit|e2e|check)-.+|debugviamcp-fake-profile|dvm-deploy-.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex Ours();

    /// <summary>One audit folder that all tests share: it stays, its old files go.</summary>
    private const string SharedAuditFolder = "debugviamcp-tests-audit";

#pragma warning disable CA2255 // A test assembly, not a library: this is the only place that runs before the first test.
    [ModuleInitializer]
    internal static void Run() => Sweep(Path.GetTempPath(), DateTime.UtcNow - MinimumAge);
#pragma warning restore CA2255

    internal static void Sweep(string root, DateTime cutoffUtc)
    {
        try
        {
            foreach (var dir in new DirectoryInfo(root).EnumerateDirectories())
            {
                if (dir.Name == SharedAuditFolder) { RemoveOldFiles(dir, cutoffUtc); continue; }
                if (Ours().IsMatch(dir.Name) && dir.LastWriteTimeUtc < cutoffUtc) TryDelete(() => dir.Delete(recursive: true));
            }
        }
        catch (IOException) { /* a sweep must never fail a test run */ }
        catch (UnauthorizedAccessException) { }
    }

    private static void RemoveOldFiles(DirectoryInfo dir, DateTime cutoffUtc)
    {
        foreach (var file in dir.EnumerateFiles())
            if (file.LastWriteTimeUtc < cutoffUtc) TryDelete(file.Delete);
    }

    private static void TryDelete(Action delete)
    {
        try { delete(); }
        catch (IOException) { /* in use by another test process: leave it for the next sweep */ }
        catch (UnauthorizedAccessException) { }
    }
}
