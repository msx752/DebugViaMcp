using System;
using System.Collections.Generic;
using System.Linq;
using VsMcpDebugger.Protocol;

namespace VsMcpDebugger.Vsix.Core;

/// <summary>Only the current user's processes are ever offered or attached to (spec §3.4).</summary>
public static class AttachCandidates
{
    public static ProcessInfo[] Filter(IEnumerable<RawProcess> processes, string? name)
    {
        if (processes is null) throw new ArgumentNullException(nameof(processes));
        var wanted = name?.Trim();
        if (wanted is not null && wanted.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) wanted = wanted.Substring(0, wanted.Length - 4);
        return processes
            .Where(p => p.OwnedByCurrentUser && (string.IsNullOrEmpty(wanted) || string.Equals(p.Name, wanted, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(p => p.Pid)
            .Select(p => new ProcessInfo { Pid = p.Pid, Name = p.Name })
            .ToArray();
    }
}
