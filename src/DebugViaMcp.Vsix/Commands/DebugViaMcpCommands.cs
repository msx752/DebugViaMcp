using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio.Shell;
using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core.Abstractions;
using DebugViaMcp.Vsix.Core.Security;
using DebugViaMcp.Vsix.Options;
using DebugViaMcp.Vsix.Ui;

namespace DebugViaMcp.Vsix.Commands;

/// <summary>Extensions → DebugViaMcp.</summary>
internal static class DebugViaMcpCommands
{
    public static readonly Guid CommandSet = new("9e2b6c41-7a5d-4f83-b1e9-0c4d8a27f356");
    private const int DisconnectAi = 0x0100, ReconnectAi = 0x0101, ShowActivity = 0x0102, OpenLogsFolder = 0x0103, OpenOptions = 0x0104, CopyMcpSetup = 0x0105, SetupAgent = 0x0106;

    public static void Register(AsyncPackage package, OleMenuCommandService commands, BridgeSecurity security, ActivityPane activity, ILog log)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Add(commands, DisconnectAi, (_, _) => security.DisconnectAi(), c => c.Visible = c.Enabled = security.Access.Enabled);
        Add(commands, ReconnectAi, (_, _) => security.ReconnectAi(), c => c.Visible = c.Enabled = !security.Access.Enabled);
        Add(commands, ShowActivity, (_, _) =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            activity.Show();
        });
        Add(commands, OpenLogsFolder, (_, _) =>
        {
            Directory.CreateDirectory(DiscoveryPaths.LogsDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + DiscoveryPaths.LogsDirectory + "\"") { UseShellExecute = true })?.Dispose();
        });
        Add(commands, SetupAgent, (_, _) => McpSetupCommand.Run(package, log));
        Add(commands, CopyMcpSetup, (_, _) => McpSetupCommand.CopyCommands(package));
        Add(commands, OpenOptions, (_, _) =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ((EnvDTE.DTE)Package.GetGlobalService(typeof(EnvDTE.DTE))).ExecuteCommand("Tools.Options");
        });
    }

    private static void Add(OleMenuCommandService commands, int id, EventHandler invoke, Action<OleMenuCommand>? query = null)
    {
        var command = new OleMenuCommand(invoke, new CommandID(CommandSet, id));
        if (query is not null) command.BeforeQueryStatus += (_, _) => query(command);
        commands.AddCommand(command);
    }
}
