using System.Diagnostics;
using Newtonsoft.Json;
using VsDebug.Protocol;

namespace VsDebug.Mcp.Discovery;

/// <summary>Reads the instance files written by running IDE instances and removes those whose process is gone.</summary>
public sealed class InstanceDiscovery(string directory, Func<int, bool> isProcessAlive)
{
    public static InstanceDiscovery CreateDefault() => new(DiscoveryPaths.InstancesDirectory, IsAlive);

    public IReadOnlyList<InstanceRecord> List()
    {
        if (!Directory.Exists(directory)) return [];

        var result = new List<InstanceRecord>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            InstanceRecord? record;
            try
            {
                record = ProtocolJson.Deserialize<InstanceRecord>(File.ReadAllText(file));
            }
            catch (Exception e) when (e is JsonException or IOException)
            {
                continue;
            }

            if (record is null || record.Pid <= 0) continue;
            if (!isProcessAlive(record.Pid))
            {
                TryDelete(file);
                continue;
            }

            result.Add(record);
        }

        return result.OrderBy(r => r.Pid).ToList();
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
