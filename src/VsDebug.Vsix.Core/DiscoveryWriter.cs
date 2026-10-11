using System;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json;
using VsDebug.Protocol;

namespace VsDebug.Vsix.Core;

/// <summary>Writes this instance's discovery file. File ACL hardening happens in the VSIX (Windows-only API).</summary>
public sealed class DiscoveryWriter(string directory)
{
    private string? _path;

    public (InstanceRecord Record, string Path) Write(InstanceRecord record)
    {
        if (record is null) throw new ArgumentNullException(nameof(record));
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, record.Pid + ".json");
        File.WriteAllText(_path, JsonConvert.SerializeObject(record, Formatting.Indented, ProtocolJson.Settings));
        return (record, _path);
    }

    public void Delete()
    {
        if (_path is not null && File.Exists(_path)) File.Delete(_path);
    }

    public static string NewToken()
    {
        var bytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string NewPipeName() => ProtocolInfo.PipeNamePrefix + Guid.NewGuid().ToString("N");
}
