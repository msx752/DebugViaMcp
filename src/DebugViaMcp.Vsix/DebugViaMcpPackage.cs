using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Commands;
using DebugViaMcp.Vsix.Core;
using DebugViaMcp.Vsix.Logging;
using DebugViaMcp.Vsix.Options;
using DebugViaMcp.Vsix.Ui;
using Task = System.Threading.Tasks.Task;

namespace DebugViaMcp.Vsix;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[Guid(PackageGuid)]
[ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Fields are released in Dispose(bool), which VS invokes through Package.Close.")]
public sealed class DebugViaMcpPackage : AsyncPackage
{
    public const string PackageGuid = "3a7c1e52-8d4b-4f6e-9a21-6b0c2d9e4f13";

    private McpSettings? _settings;
    private PipeHost? _host;
    private DiscoveryWriter? _discovery;
    private DebuggerEventPump? _pump;
    private SolutionEvents? _solutionEvents;
    private StatusBarIndicator? _indicator;

    internal BridgeSecurity? Security { get; private set; }
    internal ActivityPane? Activity { get; private set; }

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var dte = (DTE2)(await GetServiceAsync(typeof(DTE)))!;
        var outputWindow = (IVsOutputWindow)(await GetServiceAsync(typeof(SVsOutputWindow)))!;
        var errorList = await GetServiceAsync(typeof(SVsErrorList)) as IErrorList;
        var paneGuid = OutputPaneIds.Log;
        outputWindow.CreatePane(ref paneGuid, "Debugging via MCP", 1, 1);
        outputWindow.GetPane(ref paneGuid, out var pane);

        McpSettings mcpSettings;
        Exception? settingsError = null;
#pragma warning disable CA1031 // Without Visual Studio's settings service the extension still runs with the default settings.
        try
        {
            mcpSettings = await McpSettings.CreateAsync(this, cancellationToken);
        }
        catch (Exception e)
        {
            mcpSettings = new McpSettings();
            settingsError = e;
        }
#pragma warning restore CA1031
        _settings = mcpSettings;
        var activityGuid = ActivityPane.PaneGuid;
        outputWindow.CreatePane(ref activityGuid, "Debugging via MCP Activity", 1, 1);
        outputWindow.GetPane(ref activityGuid, out var activityPane);
        Activity = new ActivityPane(activityPane, () => { ThreadHelper.ThrowIfNotOnUIThread(); dte.ExecuteCommand("View.Output"); });

        int pid;
        using (var self = System.Diagnostics.Process.GetCurrentProcess()) pid = self.Id;
        var log = new LevelFilterLog(new CompositeLog(new OutputPaneLog(pane), new FileLog(DiscoveryPaths.LogsDirectory, pid)), () => mcpSettings.Applied.LogLevel);
        LogRetention.Prune(DiscoveryPaths.LogsDirectory, "vsix-", mcpSettings.Applied.LogRetentionDays, DateTimeOffset.UtcNow);
        log.Info("Settings: " + mcpSettings.ReadSummary);
        if (settingsError is not null) log.Error("Could not read Visual Studio's settings; using the default settings", settingsError);
        var vsixVersion = typeof(DebugViaMcpPackage).Assembly.GetName().Version.ToString(3);

        try
        {
            var seq = new EventSeq();
            var rebuildPrompt = new InfoBarRebuildPrompt(this, JoinableTaskFactory);
            var service = new DebugBridgeService(new DteFacade(dte, errorList), new JtfUiThread(JoinableTaskFactory), seq, log, vsixVersion, rebuildPrompt: rebuildPrompt);
            rebuildPrompt.OnDismissed = service.RebuildBarDismissed;
            var lease = new InstanceLease();
            var broadcaster = new EventBroadcaster(log, lease);
            _pump = new DebuggerEventPump((DTE)dte, service, seq, broadcaster, log);

            var token = DiscoveryWriter.NewToken();
            var pipeName = DiscoveryWriter.NewPipeName();
            var settings = new OptionsBridgeSettings(mcpSettings);
            var access = new AiAccessSwitch(mcpSettings.Applied.AcceptConnectionsOnStartup);
            var consent = new ConsentGate(new InfoBarConsentPrompt(this, JoinableTaskFactory), () => settings.ConsentTimeout);
            var audit = new AuditLog(DiscoveryPaths.LogsDirectory, () => mcpSettings.Applied.LogRetentionDays, Activity, log);
            var security = new BridgeSecurity(settings, consent, audit, access, lease);
            Security = security;
            mcpSettings.PermissionsChanged += (_, summary) => security.SettingsChanged(summary);
            _host = new PipeHost(pipeName, new SecurePipeFactory(), hooks => new BridgeConnectionHandler(service, token, security, log, hooks), broadcaster, log);
            _host.Start();

            _discovery = new DiscoveryWriter(DiscoveryPaths.InstancesDirectory, RestrictToCurrentUser);
            _discovery.Write(new InstanceRecord
            {
                Pid = pid, PipeName = pipeName, Token = token, SolutionPath = service.SolutionPath,
                VsVersion = dte.Version, VsixVersion = vsixVersion,
                ProtocolMajor = ProtocolInfo.Major, ProtocolMinor = ProtocolInfo.Minor, StartedAt = DateTimeOffset.UtcNow,
            });
            // Publish who holds this Visual Studio, so other agents' vs_instances shows it.
            lease.Changed += (_, _) =>
            {
                try
                {
                    _discovery.UpdateLease(lease);
                }
#pragma warning disable CA1031 // Publishing the holder is advisory; a failed write must never fail the bridge call that changed the lease.
                catch (Exception e)
#pragma warning restore CA1031
                {
                    log.Warn($"Could not publish the lease holder: {e.Message}");
                }
            };
            // Keep the solution shown by vs_instances in sync with what is open (held in a field so COM keeps the subscription).
            _solutionEvents = dte.Events.SolutionEvents;
            _solutionEvents.Opened += () => { ThreadHelper.ThrowIfNotOnUIThread(); _discovery.UpdateSolution(service.SolutionPath); };
            _solutionEvents.AfterClosing += () => _discovery.UpdateSolution(null);
            if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commands)
                DebugViaMcpCommands.Register(this, commands, security, Activity, log);
            _indicator = StatusBarIndicator.TryCreate(access, lease, JoinableTaskFactory, () => mcpSettings.Applied.ShowStatusBarIndicator, Activity, log);
            // Mutate = Allow in the settings (also one written while Visual Studio was closed) counts only after the user confirms it here.
            mcpSettings.MutateAllowRequested += (_, _) => _ = JoinableTaskFactory.RunAsync(async () =>
            {
#pragma warning disable CA1031 // A failure here must not leave the confirmation pending forever: it is logged and treated as declined.
                try
                {
                    log.Info("Mutate = Allow is stored: asking the user to confirm it");
                    // Yield first: the request can arise during package initialisation, which the modal dialog must not block.
                    await System.Threading.Tasks.Task.Yield();
                    await JoinableTaskFactory.SwitchToMainThreadAsync();
                    var answer = VsShellUtilities.ShowMessageBox(this,
                        "The settings say Mutate = Allow: the agent could change program state without asking you. Until you confirm, Debugging via MCP keeps asking. Allow it?",
                        "Debugging via MCP", OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_YESNO, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND);
                    log.Info(answer == 6 ? "Mutate = Allow confirmed by the user" : "Mutate = Allow declined by the user");
                    if (answer == 6) mcpSettings.ConfirmMutateAllow();
                    else mcpSettings.DeclineMutateAllow();
                }
                catch (Exception e)
                {
                    log.Error("The Mutate = Allow confirmation failed", e);
                    mcpSettings.DeclineMutateAllow();
                }
#pragma warning restore CA1031
            });
            mcpSettings.CheckMutateConfirmation();
            // Settings changes arrive on a background thread; the status bar belongs to the UI thread.
            mcpSettings.AnyApplied += (_, _) => _ = JoinableTaskFactory.RunAsync(async () =>
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync();
                _indicator?.Refresh();
            });
            log.Info($"Debugging via MCP {vsixVersion} ready (pid {pid}, protocol {ProtocolInfo.Major}.{ProtocolInfo.Minor})");
        }
#pragma warning disable CA1031 // A startup failure must be logged, never crash Visual Studio.
        catch (Exception e)
#pragma warning restore CA1031
        {
            log.Error("Debugging via MCP failed to start", e);
        }
    }

    /// <summary>The instance file holds the pipe token: only the current user may read it.</summary>
    private static void RestrictToCurrentUser(string path)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        using var identity = WindowsIdentity.GetCurrent();
        security.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl, AccessControlType.Allow));
        File.SetAccessControl(path, security);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _settings?.Dispose();
            _host?.Dispose();
            _discovery?.Delete();
        }

        base.Dispose(disposing);
    }
}

