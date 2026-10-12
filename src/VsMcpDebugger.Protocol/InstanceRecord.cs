using System;
using System.IO;
using System.Runtime.InteropServices;

namespace VsMcpDebugger.Protocol;

/// <summary>Content of &lt;VsMcpDebugger home&gt;/instances/{pid}.json, written by each IDE instance.</summary>
public sealed class InstanceRecord
{
    public int Pid { get; set; }
    public string PipeName { get; set; } = "";
    public string Token { get; set; } = "";
    public string? SolutionPath { get; set; }
    public string VsVersion { get; set; } = "";
    public string VsixVersion { get; set; } = "";
    public int ProtocolMajor { get; set; }
    public int ProtocolMinor { get; set; }
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Protocol 2.2. Client name of the agent that holds this Visual Studio; null when it is free.</summary>
    public string? LeaseHolder { get; set; }

    /// <summary>Protocol 2.2. Working directory the holder reported; null when free or unknown.</summary>
    public string? LeaseHolderFolder { get; set; }
}

/// <summary>VsMcpDebugger home per OS (spec §2.4). Every adapter, in any language, must resolve the same directory.</summary>
public static class DiscoveryPaths
{
    public static string Root { get; } = ResolveRoot(
        Environment.GetEnvironmentVariable,
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX),
        WindowsProfile.LocalAppData() ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public static string InstancesDirectory { get; } = Path.Combine(Root, "instances");
    public static string LogsDirectory { get; } = Path.Combine(Root, "logs");

    public static string InstanceFile(int pid) => Path.Combine(InstancesDirectory, pid + ".json");

    public static string ResolveRoot(Func<string, string?> env, bool isWindows, bool isMac, string localAppData, string home)
    {
        if (env is null) throw new ArgumentNullException(nameof(env));

        var overridden = env("VSMCPDEBUGGER_HOME");
        if (!string.IsNullOrEmpty(overridden)) return overridden!;
        if (isWindows) return Path.Combine(localAppData, "VsMcpDebugger");
        if (isMac) return Path.Combine(home, "Library", "Application Support", "VsMcpDebugger");
        var xdg = env("XDG_DATA_HOME");
        return Path.Combine(string.IsNullOrEmpty(xdg) ? Path.Combine(home, ".local", "share") : xdg!, "VsMcpDebugger");
    }
}
