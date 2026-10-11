using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using VsMcpDebugger.Protocol;

namespace VsMcpDebugger.Vsix.Options;

/// <summary>Tools → Options → VsMcpDebugger → General. Values are read on every bridge call; no restart needed.</summary>
[Guid("6c2f4d81-3e9a-4b57-a0d2-8f1e7c3b5a96")]
public sealed class GeneralOptionsPage : DialogPage
{
    private const int IdYes = 6;

    private int _consentTimeoutSeconds = 60;
    private int _logRetentionDays = 7;
    private PermissionLevel _appliedRead = PermissionLevel.Allow;
    private PermissionLevel _appliedControl = PermissionLevel.Allow;
    private PermissionLevel _appliedMutate = PermissionLevel.Ask;

    /// <summary>
    /// What the bridge enforces: the last applied values. The property grid writes into this page while the user is
    /// still editing, so the live properties may hold values that were never applied (or confirmed).
    /// </summary>
    internal AppliedSettings Applied { get; private set; } = AppliedSettings.Defaults;

    /// <summary>Raised after Apply/OK when a permission changed; the argument summarises the change.</summary>
    public event EventHandler<string>? PermissionsChanged;

    /// <summary>Raised after every successful Apply/OK.</summary>
    public event EventHandler? AnyApplied;

    [Category("Permissions"), DisplayName("Read"), Description("Status, threads, call stack, variables, side-effect-free evaluation, the current exception, the Output window and the Error List.")]
    public PermissionLevel Read { get; set; } = PermissionLevel.Allow;

    [Category("Permissions"), DisplayName("Control"), Description("Breakpoints without conditions (set, remove, enable/disable), start, stop, continue, pause, step, run to, and Visual Studio builds.")]
    public PermissionLevel Control { get; set; } = PermissionLevel.Allow;

    [Category("Permissions"), DisplayName("Mutate"), Description("set_value, evaluation with side effects, breakpoint conditions or tracepoints (set or re-enabled; they run in your program on every hit), and attaching the debugger to a running process. 'Ask' shows an info bar for each request.")]
    public PermissionLevel Mutate { get; set; } = PermissionLevel.Ask;

    [Category("Permissions"), DisplayName("Consent timeout (seconds)"), Description("No answer within this time denies the request (15–300).")]
    public int ConsentTimeoutSeconds
    {
        get => _consentTimeoutSeconds;
        set => _consentTimeoutSeconds = Math.Max(15, Math.Min(300, value));
    }

    [Category("Connection"), DisplayName("Accept AI connections when Visual Studio starts"), Description("Off: every session starts as if Disconnect AI was chosen.")]
    public bool AcceptConnectionsOnStartup { get; set; } = true;

    [Category("Connection"), DisplayName("Show status bar indicator"), Description("Shows which AI client is connected.")]
    public bool ShowStatusBarIndicator { get; set; } = true;

    [Category("Logging"), DisplayName("Log level"), Description("Applies to the extension and to the vsmcpdebugger MCP server (sent when it connects).")]
    public LogVerbosity LogLevel { get; set; } = LogVerbosity.Information;

    [Category("Logging"), DisplayName("Log retention (days)"), Description("Log and audit files older than this are deleted (1–90).")]
    public int LogRetentionDays
    {
        get => _logRetentionDays;
        set => _logRetentionDays = Math.Max(1, Math.Min(90, value));
    }

    [Category("Logging"), DisplayName("Log folder"), Description("Open it with Extensions → VsMcpDebugger → Open Logs Folder."), ReadOnly(true)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "The options property grid only shows instance properties.")]
    public string LogFolder => DiscoveryPaths.LogsDirectory;

    private AppliedSettings Snapshot() => new(
        new PermissionSet { Read = Read, Control = Control, Mutate = Mutate },
        TimeSpan.FromSeconds(ConsentTimeoutSeconds), AcceptConnectionsOnStartup, ShowStatusBarIndicator, LogLevel, LogRetentionDays);

    public override void LoadSettingsFromStorage()
    {
        base.LoadSettingsFromStorage();
        (_appliedRead, _appliedControl, _appliedMutate) = (Read, Control, Mutate);
        Applied = Snapshot();
    }

    protected override void OnApply(PageApplyEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (e is null) throw new ArgumentNullException(nameof(e));
        if (Mutate == PermissionLevel.Allow && _appliedMutate != PermissionLevel.Allow)
        {
            var answer = VsShellUtilities.ShowMessageBox(Site, "The agent will be able to change program state without asking you. Continue?",
                "VsMcpDebugger", OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_YESNO, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND);
            if (answer != IdYes)
            {
                e.ApplyBehavior = ApplyKind.CancelNoNavigate;
                return;
            }
        }

        base.OnApply(e);
        CommitApplied();
    }

    /// <summary>Makes the current values the applied ones and tells the bridge; <paramref name="origin"/> labels non-Apply changes.</summary>
    private void CommitApplied(string? origin = null)
    {
        var changes = new List<string>();
        if (Read != _appliedRead) changes.Add($"Read: {_appliedRead} → {Read}");
        if (Control != _appliedControl) changes.Add($"Control: {_appliedControl} → {Control}");
        if (Mutate != _appliedMutate) changes.Add($"Mutate: {_appliedMutate} → {Mutate}");
        (_appliedRead, _appliedControl, _appliedMutate) = (Read, Control, Mutate);
        Applied = Snapshot();
        if (changes.Count > 0) PermissionsChanged?.Invoke(this, (origin is null ? "" : origin + ": ") + string.Join(", ", changes));
        AnyApplied?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Tools → Import and Export Settings: imported values take effect like applied ones.</summary>
    public override void LoadSettingsFromXml(IVsSettingsReader reader)
    {
        var before = _appliedMutate;
        base.LoadSettingsFromXml(reader);

        // A settings file must not silently grant Mutate = Allow: the Options dialog asks for confirmation, an import cannot.
        var downgraded = Mutate == PermissionLevel.Allow && before != PermissionLevel.Allow;
        if (downgraded) Mutate = PermissionLevel.Ask;
        CommitApplied(downgraded ? "Imported settings (Mutate = Allow ignored; set it in Options)" : "Imported settings");
    }

    public override void ResetSettings()
    {
        base.ResetSettings();
        CommitApplied("Reset settings");
    }
}
