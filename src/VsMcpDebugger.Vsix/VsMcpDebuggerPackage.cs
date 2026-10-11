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
using VsMcpDebugger.Protocol;
using VsMcpDebugger.Vsix.Commands;
using VsMcpDebugger.Vsix.Core;
using VsMcpDebugger.Vsix.Logging;
using VsMcpDebugger.Vsix.Options;
using VsMcpDebugger.Vsix.Ui;
using Task = System.Threading.Tasks.Task;

namespace VsMcpDebugger.Vsix;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[Guid(PackageGuid)]
[ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideOptionPage(typeof(GeneralOptionsPage), "VsMcpDebugger", "General", 0, 0, true)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Fields are released in Dispose(bool), which VS invokes through Package.Close.")]
public sealed class VsMcpDebuggerPackage : AsyncPackage
{
    public const string PackageGuid = "3a7c1e52-8d4b-4f6e-9a21-6b0c2d9e4f13";

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
        outputWindow.CreatePane(ref paneGuid, "VsMcpDebugger", 1, 1);
        outputWindow.GetPane(ref paneGuid, out var pane);

        var page = (GeneralOptionsPage)GetDialogPage(typeof(GeneralOptionsPage));
        var activityGuid = ActivityPane.PaneGuid;
        outputWindow.CreatePane(ref activityGuid, "VsMcpDebugger Activity", 1, 1);
        outputWindow.GetPane(ref activityGuid, out var activityPane);
        Activity = new ActivityPane(activityPane, () => { ThreadHelper.ThrowIfNotOnUIThread(); dte.ExecuteCommand("View.Output"); });

        int pid;
        using (var self = System.Diagnostics.Process.GetCurrentProcess()) pid = self.Id;
        var log = new LevelFilterLog(new CompositeLog(new OutputPaneLog(pane), new FileLog(DiscoveryPaths.LogsDirectory, pid)), () => page.Applied.LogLevel);
        LogRetention.Prune(DiscoveryPaths.LogsDirectory, "vsix-", page.Applied.LogRetentionDays, DateTimeOffset.UtcNow);
        var vsixVersion = typeof(VsMcpDebuggerPackage).Assembly.GetName().Version.ToString(3);

        try
        {
            var seq = new EventSeq();
            var service = new DebugBridgeService(new DteFacade(dte, errorList), new JtfUiThread(JoinableTaskFactory), seq, log, vsixVersion);
            var broadcaster = new EventBroadcaster(log);
            _pump = new DebuggerEventPump((DTE)dte, service, seq, broadcaster, log);

            var token = DiscoveryWriter.NewToken();
            var pipeName = DiscoveryWriter.NewPipeName();
            var settings = new OptionsBridgeSettings(page);
            var access = new AiAccessSwitch(page.Applied.AcceptConnectionsOnStartup);
            var consent = new ConsentGate(new InfoBarConsentPrompt(this, JoinableTaskFactory), () => settings.ConsentTimeout);
            var audit = new AuditLog(DiscoveryPaths.LogsDirectory, () => page.Applied.LogRetentionDays, Activity, log);
            var security = new BridgeSecurity(settings, consent, audit, access);
            Security = security;
            page.PermissionsChanged += (_, summary) => security.SettingsChanged(summary);
            _host = new PipeHost(pipeName, new SecurePipeFactory(), hooks => new BridgeConnectionHandler(service, token, security, log, hooks), broadcaster, log);
            _host.Start();

            _discovery = new DiscoveryWriter(DiscoveryPaths.InstancesDirectory, RestrictToCurrentUser);
            _discovery.Write(new InstanceRecord
            {
                Pid = pid, PipeName = pipeName, Token = token, SolutionPath = service.SolutionPath,
                VsVersion = dte.Version, VsixVersion = vsixVersion,
                ProtocolMajor = ProtocolInfo.Major, ProtocolMinor = ProtocolInfo.Minor, StartedAt = DateTimeOffset.UtcNow,
            });
            // Keep the solution shown by vs_instances in sync with what is open (held in a field so COM keeps the subscription).
            _solutionEvents = dte.Events.SolutionEvents;
            _solutionEvents.Opened += () => { ThreadHelper.ThrowIfNotOnUIThread(); _discovery.UpdateSolution(service.SolutionPath); };
            _solutionEvents.AfterClosing += () => _discovery.UpdateSolution(null);
            if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commands)
                VsMcpDebuggerCommands.Register(this, commands, security, Activity);
            _indicator = StatusBarIndicator.TryCreate(access, JoinableTaskFactory, () => page.Applied.ShowStatusBarIndicator, Activity, log);
            page.AnyApplied += (_, _) => { ThreadHelper.ThrowIfNotOnUIThread(); _indicator?.Refresh(); };
            log.Info($"VsMcpDebugger {vsixVersion} ready (pid {pid}, protocol {ProtocolInfo.Major}.{ProtocolInfo.Minor})");
        }
#pragma warning disable CA1031 // A startup failure must be logged, never crash Visual Studio.
        catch (Exception e)
#pragma warning restore CA1031
        {
            log.Error("VsMcpDebugger failed to start", e);
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
            _host?.Dispose();
            _discovery?.Delete();
        }

        base.Dispose(disposing);
    }
}

