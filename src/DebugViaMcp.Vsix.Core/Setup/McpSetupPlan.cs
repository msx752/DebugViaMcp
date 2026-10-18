using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DebugViaMcp.Vsix.Core.Setup;

public enum SetupStepKind { InstallTool, UpdateTool, RegisterClaude }

/// <summary>What was found on this machine; null means "could not be determined".</summary>
public sealed class SetupState
{
    public int? DotnetSdkMajor { get; set; }
    public string? ToolVersion { get; set; }
    public bool? ClaudeFound { get; set; }
    public bool? ClaudeRegistered { get; set; }

    /// <summary>Number of running debugviamcp processes (agent sessions use one each); they lock the installed files.</summary>
    public int ToolProcessesRunning { get; set; }

    public string ExtensionVersion { get; set; } = "";
}

/// <summary>One command the setup may run. Every command is a fixed string: nothing an agent or a file supplies goes into it.</summary>
public sealed class SetupStep
{
    public SetupStepKind Kind { get; set; }
    public string Title { get; set; } = "";

    /// <summary>The command as the user sees and could type it.</summary>
    public string Display { get; set; } = "";

    public string FileName { get; set; } = "";
    public string Arguments { get; set; } = "";
}

public sealed class SetupPlan
{
    /// <summary>Status lines, each starting with OK, MISSING or UNKNOWN.</summary>
    public IReadOnlyList<string> Lines { get; set; } = [];

    public IReadOnlyList<SetupStep> Steps { get; set; } = [];

    public bool IsComplete => Lines.All(l => l.StartsWith("OK", StringComparison.Ordinal));
}

/// <summary>Decides what "Set Up AI Agent" has to do, from what was detected. Pure: no processes, no file access.</summary>
public static class McpSetupPlan
{
    public const int RequiredSdkMajor = 10;

    public static SetupPlan Create(SetupState state)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));
        var lines = new List<string>();
        var steps = new List<SetupStep>();
        var sdkOk = state.DotnetSdkMajor is >= RequiredSdkMajor;

        lines.Add(state.DotnetSdkMajor switch
        {
            null => "UNKNOWN  .NET SDK: could not run dotnet.",
            >= RequiredSdkMajor => $"OK       .NET SDK {state.DotnetSdkMajor.Value.ToString(CultureInfo.InvariantCulture)} is installed.",
            _ => $"MISSING  The .NET {RequiredSdkMajor.ToString(CultureInfo.InvariantCulture)} SDK is required for the MCP server (the highest found is {state.DotnetSdkMajor.Value.ToString(CultureInfo.InvariantCulture)}).",
        });

        if (state.ToolVersion is null)
        {
            lines.Add("MISSING  The MCP server tool (debugviamcp) is not installed.");
            if (sdkOk) steps.Add(Step(SetupStepKind.InstallTool, "Install the MCP server tool", "dotnet", "tool install -g DebugViaMcp --prerelease"));
        }
        else if (SameRelease(state.ToolVersion, state.ExtensionVersion))
        {
            lines.Add($"OK       MCP server tool {state.ToolVersion} matches this extension.");
        }
        else
        {
            lines.Add($"MISSING  MCP server tool {state.ToolVersion} is not the release of this extension ({state.ExtensionVersion}).");
            if (state.ToolProcessesRunning > 0)
            {
                // A running tool locks its files, so the update would fail halfway. Closing somebody's agent session is not ours to do.
                lines.Add($"MISSING  {state.ToolProcessesRunning.ToString(CultureInfo.InvariantCulture)} debugviamcp process(es) are running (an agent such as Claude Code uses one per session). " +
                    "Close those agent sessions, then run Set Up AI Agent again; the old version keeps running until then.");
            }
            else if (sdkOk)
            {
                steps.Add(Step(SetupStepKind.UpdateTool, "Update the MCP server tool", "dotnet", "tool update -g DebugViaMcp --prerelease"));
            }
        }

        if (state.ClaudeFound is false)
        {
            lines.Add("MISSING  Claude Code was not found. In another MCP client, register the command: debugviamcp serve");
        }
        else if (state.ClaudeFound is null)
        {
            lines.Add("UNKNOWN  Claude Code: could not be checked.");
        }
        else if (state.ClaudeRegistered is true)
        {
            lines.Add("OK       The server is registered with Claude Code.");
        }
        else if (state.ClaudeRegistered is false)
        {
            lines.Add("MISSING  The server is not registered with Claude Code.");
            // Registering a command that will not exist helps nobody: only when the tool is there or about to be installed.
            if (state.ToolVersion is not null || steps.Any(s => s.Kind == SetupStepKind.InstallTool)) steps.Add(Step(SetupStepKind.RegisterClaude, "Register the server with Claude Code (for all your projects)", "cmd.exe", "/c claude mcp add --scope user debugviamcp -- debugviamcp serve",
                "claude mcp add --scope user debugviamcp -- debugviamcp serve"));
        }
        else
        {
            lines.Add("UNKNOWN  Claude Code registration could not be checked.");
        }

        return new SetupPlan { Lines = lines, Steps = steps };
    }

    private static SetupStep Step(SetupStepKind kind, string title, string file, string arguments, string? display = null) =>
        new() { Kind = kind, Title = title, FileName = file, Arguments = arguments, Display = display ?? (file + " " + arguments) };

    /// <summary>The same major.minor.patch counts as the same release; a preview suffix does not matter.</summary>
    private static bool SameRelease(string tool, string extension) =>
        Core(tool) is { } a && Core(extension) is { } b && a == b;

    private static Version? Core(string version)
    {
        var cut = version.IndexOfAny(['-', '+']);
        var text = cut >= 0 ? version.Substring(0, cut) : version;
        var parts = text.Split('.');
        if (parts.Length < 3) return Version.TryParse(text, out var two) ? two : null;
        return Version.TryParse(string.Join(".", parts.Take(3)), out var v) ? v : null;
    }

    /// <summary>Highest SDK major in <c>dotnet --list-sdks</c> output; null when none is listed.</summary>
    public static int? ParseSdkMajor(string? output)
    {
        int? best = null;
        foreach (var line in (output ?? "").Split('\n'))
        {
            var m = Regex.Match(line, @"^\s*(\d+)\.\d+\.\d+", RegexOptions.None, TimeSpan.FromSeconds(1));
            if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major) && (best is null || major > best)) best = major;
        }

        return best;
    }

    /// <summary>Version of the debugviamcp package in <c>dotnet tool list -g</c> output; null when it is not listed.</summary>
    public static string? ParseToolVersion(string? output)
    {
        foreach (var line in (output ?? "").Split('\n'))
        {
            var cells = line.Split([' ', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries);
            if (cells.Length >= 2 && string.Equals(cells[0], "debugviamcp", StringComparison.OrdinalIgnoreCase)) return cells[1];
        }

        return null;
    }

    /// <summary>True when <c>claude mcp list</c> output has a server named debugviamcp.</summary>
    public static bool ParseRegistered(string? output) =>
        (output ?? "").Split('\n').Any(l => l.TrimStart().StartsWith("debugviamcp:", StringComparison.OrdinalIgnoreCase));
}
