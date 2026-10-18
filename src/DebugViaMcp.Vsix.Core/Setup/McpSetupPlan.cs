using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DebugViaMcp.Vsix.Core.Setup;

/// <summary>What was found on this machine; null means "could not be determined".</summary>
public sealed class SetupState
{
    public int? DotnetRuntimeMajor { get; set; }

    /// <summary>The extension has put its MCP server and launcher in place.</summary>
    public bool ServerDeployed { get; set; }

    public string LauncherPath { get; set; } = "";
}

public sealed class SetupPlan
{
    /// <summary>Status lines, each starting with OK, MISSING or UNKNOWN.</summary>
    public IReadOnlyList<string> Lines { get; set; } = [];

    public bool IsComplete => Lines.All(l => l.StartsWith("OK", StringComparison.Ordinal));
}

/// <summary>Describes what the MCP server needs on this machine, from what was detected. Pure: no processes, no file access.</summary>
public static class McpSetupPlan
{
    public const int RequiredRuntimeMajor = 10;

    public static SetupPlan Create(SetupState state)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));
        var lines = new List<string>
        {
            state.DotnetRuntimeMajor switch
            {
                null => "UNKNOWN  .NET runtime: could not run dotnet.",
                >= RequiredRuntimeMajor => $"OK       .NET {state.DotnetRuntimeMajor.Value.ToString(CultureInfo.InvariantCulture)} runtime is installed.",
                _ => $"MISSING  The .NET {RequiredRuntimeMajor.ToString(CultureInfo.InvariantCulture)} runtime is required for the MCP server (the highest found is {state.DotnetRuntimeMajor.Value.ToString(CultureInfo.InvariantCulture)}).",
            },
            state.ServerDeployed
                ? $"OK       The MCP server of this extension is installed ({state.LauncherPath})."
                : "MISSING  The extension could not install its MCP server. See View → Output → Debugging via MCP, then restart Visual Studio.",
        };

        return new SetupPlan { Lines = lines };
    }

    /// <summary>Highest major of the Microsoft.NETCore.App runtime in <c>dotnet --list-runtimes</c> output; null when none is listed.</summary>
    public static int? ParseRuntimeMajor(string? output)
    {
        int? best = null;
        foreach (var line in (output ?? "").Split('\n'))
        {
            var m = Regex.Match(line, @"^\s*Microsoft\.NETCore\.App\s+(\d+)\.\d+\.\d+", RegexOptions.None, TimeSpan.FromSeconds(1));
            if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major) && (best is null || major > best)) best = major;
        }

        return best;
    }
}
