using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DebugViaMcp.Vsix.Core.Setup;

public enum SetupStepKind { RemoveOldRegistration, RegisterClaude }

/// <summary>How the server is registered with Claude Code.</summary>
public enum ClaudeRegistration { Unknown, None, Current, Other }

/// <summary>What was found on this machine; null means "could not be determined".</summary>
public sealed class SetupState
{
    public int? DotnetRuntimeMajor { get; set; }

    /// <summary>The extension has put its MCP server and launcher in place.</summary>
    public bool ServerDeployed { get; set; }

    public string LauncherPath { get; set; } = "";
    public bool? ClaudeFound { get; set; }
    public ClaudeRegistration Registration { get; set; } = ClaudeRegistration.Unknown;
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
    public const int RequiredRuntimeMajor = 10;

    public static SetupPlan Create(SetupState state)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));
        var lines = new List<string>();
        var steps = new List<SetupStep>();

        lines.Add(state.DotnetRuntimeMajor switch
        {
            null => "UNKNOWN  .NET runtime: could not run dotnet.",
            >= RequiredRuntimeMajor => $"OK       .NET {state.DotnetRuntimeMajor.Value.ToString(CultureInfo.InvariantCulture)} runtime is installed.",
            _ => $"MISSING  The .NET {RequiredRuntimeMajor.ToString(CultureInfo.InvariantCulture)} runtime is required for the MCP server (the highest found is {state.DotnetRuntimeMajor.Value.ToString(CultureInfo.InvariantCulture)}).",
        });

        lines.Add(state.ServerDeployed
            ? $"OK       The MCP server of this extension is installed ({state.LauncherPath})."
            : "MISSING  The extension could not install its MCP server. See View → Output → Debugging via MCP, then restart Visual Studio.");

        if (state.ClaudeFound is false)
        {
            lines.Add($"MISSING  Claude Code was not found. In another MCP client, register the command: {Quote(state.LauncherPath)} serve");
        }
        else if (state.ClaudeFound is null)
        {
            lines.Add("UNKNOWN  Claude Code: could not be checked.");
        }
        else switch (state.Registration)
        {
            case ClaudeRegistration.Current:
                lines.Add("OK       The server is registered with Claude Code.");
                break;
            case ClaudeRegistration.None:
                lines.Add("MISSING  The server is not registered with Claude Code.");
                AddRegister(steps, state);
                break;
            case ClaudeRegistration.Other:
                lines.Add("MISSING  The server is registered with Claude Code under an older command.");
                if (state.ServerDeployed)
                    steps.Add(Step(SetupStepKind.RemoveOldRegistration, "Remove the old registration", "cmd.exe", "/c claude mcp remove --scope user debugviamcp", "claude mcp remove --scope user debugviamcp"));
                AddRegister(steps, state);
                break;
            default:
                lines.Add("UNKNOWN  Claude Code registration could not be checked.");
                break;
        }

        return new SetupPlan { Lines = lines, Steps = steps };
    }

    /// <summary>The registration command; the launcher path never changes with the extension version.</summary>
    public static string RegisterCommand(string launcherPath) => $"claude mcp add --scope user debugviamcp -- {Quote(launcherPath)} serve";

    // Registering a command that does not exist helps nobody: only when the server is in place.
    private static void AddRegister(List<SetupStep> steps, SetupState state)
    {
        if (!state.ServerDeployed) return;
        steps.Add(Step(SetupStepKind.RegisterClaude, "Register the server with Claude Code (for all your projects)", "cmd.exe", "/c " + RegisterCommand(state.LauncherPath), RegisterCommand(state.LauncherPath)));
    }

    private static string Quote(string path) => "\"" + path + "\"";

    private static SetupStep Step(SetupStepKind kind, string title, string file, string arguments, string? display = null) =>
        new() { Kind = kind, Title = title, FileName = file, Arguments = arguments, Display = display ?? (file + " " + arguments) };

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

    /// <summary>How <c>claude mcp list</c> output shows the debugviamcp server: not at all, with this launcher, or with another command.</summary>
    public static ClaudeRegistration ParseRegistration(string? output, string launcherPath)
    {
        var line = (output ?? "").Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith("debugviamcp:", StringComparison.OrdinalIgnoreCase));
        if (line is null) return ClaudeRegistration.None;
        return launcherPath.Length > 0 && line.IndexOf(launcherPath, StringComparison.OrdinalIgnoreCase) >= 0 ? ClaudeRegistration.Current : ClaudeRegistration.Other;
    }
}
