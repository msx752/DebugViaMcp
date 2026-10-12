using System;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json;
using VsMcpDebugger.Protocol;

namespace VsMcpDebugger.Vsix.Core;

/// <summary>
/// Writes this instance's discovery file. Every write goes to a temporary file that is hardened first
/// (<paramref name="harden"/>: user-only ACL, applied by the VSIX) and only then moved into place, so the
/// token never sits in a readable file.
/// </summary>
public sealed class DiscoveryWriter(string directory, Action<string>? harden = null)
{
    private readonly object _gate = new();
    private InstanceRecord? _record;
    private string? _path;

    public (InstanceRecord Record, string Path) Write(InstanceRecord record)
    {
        if (record is null) throw new ArgumentNullException(nameof(record));
        lock (_gate)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, record.Pid + ".json");
            var temp = Path.Combine(directory, record.Pid + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(temp, JsonConvert.SerializeObject(record, Formatting.Indented, ProtocolJson.Settings));
                harden?.Invoke(temp);
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }

            _record = record;
            _path = path;
            return (record, path);
        }
    }

    /// <summary>Rewrites the record when the open solution changes (null when no solution is open).</summary>
    public void UpdateSolution(string? solutionPath)
    {
        InstanceRecord? record;
        lock (_gate) record = _record;
        if (record is null) return;
        record.SolutionPath = solutionPath;
        Write(record);
    }

    /// <summary>Rewrites the record with the agent that holds this Visual Studio (both null when it is free).</summary>
    public void UpdateLease(string? holder, string? folder)
    {
        InstanceRecord? record;
        lock (_gate) record = _record;
        if (record is null) return;
        record.LeaseHolder = holder;
        record.LeaseHolderFolder = folder;
        Write(record);
    }

    public void Delete()
    {
        lock (_gate)
        {
            if (_path is not null && File.Exists(_path)) File.Delete(_path);
            _record = null;
        }
    }

    public static string NewToken()
    {
        var bytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string NewPipeName() => ProtocolInfo.PipeNamePrefix + Guid.NewGuid().ToString("N");
}
