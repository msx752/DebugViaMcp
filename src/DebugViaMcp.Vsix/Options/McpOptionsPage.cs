using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using DebugViaMcp.Protocol;

namespace DebugViaMcp.Vsix.Options;

/// <summary>
/// Tools → Options → Debugging via MCP → General, for Visual Studio versions without the All Settings page (2022 17.14 has no way to show
/// registration.json unless a preview feature is on). The extension reads these values only when the settings service delivers none of its
/// own; where All Settings exists, that page is the one to use.
/// </summary>
[Guid("5e0b7d94-3c1a-4a6f-8d52-9b7f2e6a1c43")]
public sealed class McpOptionsPage : DialogPage
{
    private const string Permissions = "Permissions", Behavior = "Behavior", Limits = "Limits", Logging = "Logging";

    [Category(Permissions), DisplayName("Read"), Description("Status, threads, call stack, variables, side-effect-free evaluation, the current exception, the Output window and the Error List.")]
    public PermissionLevel Read { get; set; } = PermissionLevel.Allow;

    [Category(Permissions), DisplayName("Control"), Description("Breakpoints without conditions (set, remove, enable/disable), start, stop, continue, pause, step, run to, and Visual Studio builds.")]
    public PermissionLevel Control { get; set; } = PermissionLevel.Allow;

    [Category(Permissions), DisplayName("Mutate"), Description("set_value, evaluation with side effects, breakpoint conditions or tracepoints (they run in your program on every hit), and attaching the debugger to a running process. Ask shows an info bar for each request. Allow lets the agent change program state without asking; Visual Studio asks you to confirm it, and keeps asking until you do.")]
    public PermissionLevel Mutate { get; set; } = PermissionLevel.Ask;

    [Category(Permissions), DisplayName("Consent timeout (seconds)"), Description("No answer within this time denies the request (15–300).")]
    public int ConsentTimeoutSeconds { get; set; } = 60;

    [Category(Permissions), DisplayName("Mask sensitive values"), Description("On: values that look like secrets (passwords, tokens, keys, connection strings) are hidden before the agent sees them. Turning it off lets such values reach the agent and its AI provider; Visual Studio asks you to confirm, and keeps masking until you do.")]
    public bool MaskSensitiveValues { get; set; } = true;

    [Category(Behavior), DisplayName("Accept AI connections when Visual Studio starts"), Description("Off: every session starts as if Disconnect AI was chosen.")]
    public bool AcceptConnectionsOnStartup { get; set; } = true;

    [Category(Behavior), DisplayName("Show status bar indicator"), Description("Shows which AI client is connected.")]
    public bool ShowStatusBarIndicator { get; set; } = true;

    [Category(Limits), DisplayName("Longest wait for a break (seconds)"), Description("Upper limit for debug_start, debug_continue, debug_step, debug_run_to and debug_wait. The agent may ask for less, never for more. Applies to the next connection of an agent (5–600).")]
    public int MaxWaitSeconds { get; set; } = 120;

    [Category(Limits), DisplayName("Longest wait for a build (seconds)"), Description("Upper limit for vs_build. The agent may ask for less, never for more; a build that takes longer keeps running and the agent can ask again (30–1800).")]
    public int MaxBuildSeconds { get; set; } = 600;

    [Category(Logging), DisplayName("Log level"), Description("Applies to the extension and to the debugviamcp MCP server (sent when it connects). Open the logs with Extensions → Debugging via MCP → Open Logs Folder.")]
    public LogVerbosity LogLevel { get; set; } = LogVerbosity.Information;

    [Category(Logging), DisplayName("Log retention (days)"), Description("Log and audit files of the extension and of the debugviamcp MCP server older than this are deleted (1–90).")]
    public int LogRetentionDays { get; set; } = 7;

    /// <summary>Raised after the user pressed OK or Apply (and the values are stored), on the UI thread.</summary>
    public event EventHandler? Applied;

    protected override void OnApply(PageApplyEventArgs e)
    {
        base.OnApply(e);
        if (e.ApplyBehavior == ApplyKind.Apply) Applied?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The values under the same names the settings service uses (without the "debugging.mcp." prefix), as text.</summary>
    internal IReadOnlyDictionary<string, string> ToValues() => new Dictionary<string, string>
    {
        ["read"] = Read.ToString(),
        ["control"] = Control.ToString(),
        ["mutate"] = Mutate.ToString(),
        ["consentTimeoutSeconds"] = ConsentTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
        ["maskSensitiveValues"] = MaskSensitiveValues ? "true" : "false",
        ["acceptConnectionsOnStartup"] = AcceptConnectionsOnStartup ? "true" : "false",
        ["showStatusBarIndicator"] = ShowStatusBarIndicator ? "true" : "false",
        ["maxWaitSeconds"] = MaxWaitSeconds.ToString(CultureInfo.InvariantCulture),
        ["maxBuildSeconds"] = MaxBuildSeconds.ToString(CultureInfo.InvariantCulture),
        ["logLevel"] = LogLevel.ToString(),
        ["logRetentionDays"] = LogRetentionDays.ToString(CultureInfo.InvariantCulture),
    };
}
