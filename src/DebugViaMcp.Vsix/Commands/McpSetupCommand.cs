using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using DebugViaMcp.Vsix.Core.Abstractions;
using DebugViaMcp.Vsix.Core.Setup;
using DebugViaMcp.Vsix.Infrastructure;
using DebugViaMcp.Vsix.Ui;

namespace DebugViaMcp.Vsix.Commands;

/// <summary>
/// Extensions → Debugging via MCP → Set Up AI Agent: makes sure the extension's MCP server is installed, checks the .NET
/// runtime and opens a window where the user picks an AI client and gets the command or configuration to copy.
/// It runs nothing on the user's behalf: the texts come from <see cref="AgentClients"/> and contain only the launcher path.
/// </summary>
internal static class McpSetupCommand
{
    private static readonly TimeSpan QuickProbe = TimeSpan.FromSeconds(20);

    private static int _running;

    public static void Run(AsyncPackage package, ILog log)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;   // one window at a time

        package.JoinableTaskFactory.RunAsync(async () =>
        {
            try
            {
                await RunCoreAsync(package, log).ConfigureAwait(true);
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        }).FileAndForget("DebugViaMcp/McpSetup");
    }

    private static async Task RunCoreAsync(AsyncPackage package, ILog log)
    {
        var jtf = package.JoinableTaskFactory;
        await jtf.SwitchToMainThreadAsync();
        SetStatus("Debugging via MCP: checking the MCP server...");

        await TaskScheduler.Default;
        McpServerInstaller.TryDeploy(log);
        var root = DebugViaMcp.Protocol.DiscoveryPaths.Root;
        var launcher = McpServerDeployer.LauncherPath(root);
        var state = new SetupState { LauncherPath = launcher, ServerDeployed = McpServerDeployer.IsDeployed(root) };
        var runtimes = await ProcessRunner.RunAsync("dotnet", "--list-runtimes", QuickProbe).ConfigureAwait(false);
        if (runtimes.Started && !runtimes.TimedOut && runtimes.ExitCode == 0) state.DotnetRuntimeMajor = McpSetupPlan.ParseRuntimeMajor(runtimes.Output) ?? 0;
        var plan = McpSetupPlan.Create(state);

        await jtf.SwitchToMainThreadAsync();
        SetStatus("");
        new McpSetupDialog(string.Join("\n", plan.Lines), AgentClients.For(launcher)).ShowModal();
    }

    private static void SetStatus(string text)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (Package.GetGlobalService(typeof(SVsStatusbar)) is IVsStatusbar bar) bar.SetText(text);
    }

    private readonly struct RunResult(bool started, int exitCode, bool timedOut, string output)
    {
        public bool Started { get; } = started;
        public int ExitCode { get; } = exitCode;
        public bool TimedOut { get; } = timedOut;
        public string Output { get; } = output;
    }

    private static class ProcessRunner
    {
        public static async Task<RunResult> RunAsync(string fileName, string arguments, TimeSpan timeout)
        {
            var info = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            Process process;
            try
            {
                process = Process.Start(info)!;
            }
            catch (Win32Exception)
            {
                return new RunResult(false, -1, false, "");
            }

            using (process)
            {
                process.StandardInput.Close();   // nothing may wait for input
                var output = new StringBuilder();
                process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var exited = Task.Run(() => process.WaitForExit((int)timeout.TotalMilliseconds));
                if (!await exited.ConfigureAwait(false))
                {
                    try { process.Kill(); }
                    catch (InvalidOperationException) { }
                    catch (Win32Exception) { }
                    lock (output) return new RunResult(true, -1, true, output.ToString());
                }

                process.WaitForExit();   // flushes the redirected streams
                lock (output) return new RunResult(true, process.ExitCode, false, output.ToString());
            }
        }
    }
}
