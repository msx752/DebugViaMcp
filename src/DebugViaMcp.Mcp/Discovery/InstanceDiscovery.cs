using System.Diagnostics;
using Newtonsoft.Json;
using DebugViaMcp.Protocol;

namespace DebugViaMcp.Mcp.Discovery;

/// <summary>Reads the instance files written by running IDE instances and removes those whose process is gone.</summary>
public sealed class InstanceDiscovery(string directory, Func<InstanceRecord, bool> isLive)
{
    public static InstanceDiscovery CreateDefault() => new(DiscoveryPaths.InstancesDirectory, IsLive);

    /// <summary>
    /// True when the record's pid is a running process we can see that started no later than the record was
    /// written. Inaccessible (protected/other-user) processes and pids reused by a newer process are stale.
    /// </summary>
    public static bool IsLive(InstanceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        try
        {
            using var process = Process.GetProcessById(record.Pid);
            if (process.HasExited) return false;
            if (record.StartedAt == default) return true;
            return process.StartTime.ToUniversalTime() <= record.StartedAt.UtcDateTime.AddSeconds(1);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

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
            if (!isLive(record))
            {
                TryDelete(file);
                continue;
            }

            result.Add(record);
        }

        return result.OrderBy(r => r.Pid).ToList();
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
