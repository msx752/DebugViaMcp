using System;
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
using VsMcpDebugger.Vsix.Core;
using VsMcpDebugger.Vsix.Logging;
using Task = System.Threading.Tasks.Task;

namespace VsMcpDebugger.Vsix;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[Guid(PackageGuid)]
[ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Fields are released in Dispose(bool), which VS invokes through Package.Close.")]
public sealed class VsMcpDebuggerPackage : AsyncPackage
{
    public const string PackageGuid = "3a7c1e52-8d4b-4f6e-9a21-6b0c2d9e4f13";
    private static readonly Guid OutputPaneGuid = new("b5e0c9a4-2f17-4d38-8c6e-91a3d7f02b64");

    private PipeHost? _host;
    private DiscoveryWriter? _discovery;
    private DebuggerEventPump? _pump;
    private SolutionEvents? _solutionEvents;

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var dte = (DTE2)(await GetServiceAsync(typeof(DTE)))!;
        var outputWindow = (IVsOutputWindow)(await GetServiceAsync(typeof(SVsOutputWindow)))!;
        var paneGuid = OutputPaneGuid;
        outputWindow.CreatePane(ref paneGuid, "VsMcpDebugger", 1, 1);
        outputWindow.GetPane(ref paneGuid, out var pane);

        int pid;
        using (var self = System.Diagnostics.Process.GetCurrentProcess()) pid = self.Id;
        var log = new CompositeLog(new OutputPaneLog(pane), new FileLog(DiscoveryPaths.LogsDirectory, pid));
        var vsixVersion = typeof(VsMcpDebuggerPackage).Assembly.GetName().Version.ToString(3);

        try
        {
            var seq = new EventSeq();
            var service = new DebugBridgeService(new DteFacade(dte), new JtfUiThread(JoinableTaskFactory), seq, log, vsixVersion);
            var broadcaster = new EventBroadcaster(log);
            _pump = new DebuggerEventPump((DTE)dte, service, seq, broadcaster, log);

            var token = DiscoveryWriter.NewToken();
            var pipeName = DiscoveryWriter.NewPipeName();
            var settings = new DefaultBridgeSettings();
            var security = new BridgeSecurity(settings, new ConsentGate(new DenyAllPrompt(), () => settings.ConsentTimeout),
                new AuditLog(DiscoveryPaths.LogsDirectory, () => 7, new NullActivitySink(), log), new AiAccessSwitch(enabled: true));
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

/// <summary>Temporary until the Options page exists: spec defaults.</summary>
internal sealed class DefaultBridgeSettings : IBridgeSettings
{
    public PermissionSet Permissions { get; } = new();
    public TimeSpan ConsentTimeout => TimeSpan.FromSeconds(60);
    public LogVerbosity LogLevel => LogVerbosity.Information;
}

/// <summary>Temporary until the info bar exists: every consent question is answered Deny (fail closed).</summary>
internal sealed class DenyAllPrompt : IConsentPrompt
{
    public System.Threading.Tasks.Task<ConsentChoice> AskAsync(string text, TimeSpan timeout, CancellationToken ct) =>
        System.Threading.Tasks.Task.FromResult(ConsentChoice.Deny);
}

internal sealed class NullActivitySink : IActivitySink
{
    public void Write(string line)
    {
    }
}
