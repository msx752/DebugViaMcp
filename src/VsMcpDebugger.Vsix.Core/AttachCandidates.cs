using System;
using System.Collections.Generic;
using System.Linq;
using VsMcpDebugger.Protocol;

namespace VsMcpDebugger.Vsix.Core;

/// <summary>
/// Only the current user's processes are ever offered or attached to (spec §3.4). Visual Studio itself and the
/// vsmcpdebugger server never are: another Visual Studio instance holds its own pipe token, sources and credentials,
/// so attaching to it would let the agent take over that instance too.
/// </summary>
public static class AttachCandidates
{
    private static readonly string[] Forbidden = ["devenv", "vsmcpdebugger", "VsMcpDebugger.Mcp"];

    public static bool IsForbidden(string name) => Forbidden.Any(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));

    public static ProcessInfo[] Filter(IEnumerable<RawProcess> processes, string? name)
    {
        if (processes is null) throw new ArgumentNullException(nameof(processes));
        var wanted = name?.Trim();
        if (wanted is not null && wanted.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) wanted = wanted.Substring(0, wanted.Length - 4);
        return processes
            .Where(p => p.OwnedByCurrentUser && !IsForbidden(p.Name)
                && (string.IsNullOrEmpty(wanted) || string.Equals(p.Name, wanted, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(p => p.Pid)
            .Select(p => new ProcessInfo { Pid = p.Pid, Name = p.Name })
            .ToArray();
    }
}
