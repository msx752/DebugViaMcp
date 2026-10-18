using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using DebugViaMcp.Vsix.Core.Abstractions;
using DebugViaMcp.Vsix.Core.Setup;
using DebugViaMcp.Vsix.Infrastructure;

namespace DebugViaMcp.Vsix.Commands;

/// <summary>
/// Extensions → Debugging via MCP → Set Up AI Agent: makes sure the extension's MCP server is installed, finds out what else is
/// there (.NET runtime, Claude Code and its registration), lists what is missing and, only after the user agrees, runs the fixed commands that fix it.
/// Nothing here takes input from an agent or a file: every command comes from <see cref="McpSetupPlan"/>.
/// </summary>
internal static class McpSetupCommand
{
    private static readonly TimeSpan QuickProbe = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan SlowProbe = TimeSpan.FromSeconds(60);   // claude mcp list health-checks every server
    internal static readonly string Root = DebugViaMcp.Protocol.DiscoveryPaths.Root;

    private static readonly TimeSpan StepTimeout = TimeSpan.FromMinutes(5);

    private static int _running;

    public static void CopyCommands(AsyncPackage package)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var copied = false;
        try
        {
            System.Windows.Clipboard.SetText(McpSetup.Commands(McpServerDeployer.LauncherPath(Root)));
            copied = true;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Another program holds the clipboard; the message below shows the commands instead.
        }

        VsShellUtilities.ShowMessageBox(package, copied ? McpSetup.Explanation : McpSetup.ClipboardFailed(McpServerDeployer.LauncherPath(Root)), McpSetup.Title,
            Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_INFO, Microsoft.VisualStudio.Shell.Interop.OLEMSGBUTTON.OLEMSGBUTTON_OK,
            Microsoft.VisualStudio.Shell.Interop.OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }

    public static void Run(AsyncPackage package, ILog log)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;   // one setup at a time

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
        SetStatus(package, "Debugging via MCP: checking the AI agent setup...");

        await TaskScheduler.Default;
        McpServerInstaller.TryDeploy(log);
        var plan = McpSetupPlan.Create(await DetectAsync().ConfigureAwait(false));

        await jtf.SwitchToMainThreadAsync();
        SetStatus(package, "");
        if (plan.Steps.Count == 0)
        {
            Show(package, Summary(plan) + "\n\n" + (plan.IsComplete
                ? "Everything is set up. Start your agent in the folder of your solution."
                : "There is nothing this command can fix by itself. See the lines above."));
            return;
        }

        var question = Summary(plan) + "\n\nRun these commands now?\n\n" + string.Join("\n", plan.Steps.Select(s => "  " + s.Display));
        if (!Ask(package, question)) return;

        await TaskScheduler.Default;
        string? failure = null;
        foreach (var step in plan.Steps)
        {
            log.Info($"Setup: running '{step.Display}'");
            var result = await ProcessRunner.RunAsync(step.FileName, step.Arguments, StepTimeout).ConfigureAwait(false);
            log.Info($"Setup: '{step.Display}' finished with exit code {result.ExitCode}{(result.TimedOut ? " (timed out)" : "")}: {Tail(result.Output, 1500)}");
            if (!result.Started || result.TimedOut || result.ExitCode != 0)
            {
                failure = $"'{step.Display}' failed" + (result.TimedOut ? " (timed out)" : result.Started ? $" (exit code {result.ExitCode})" : " (could not be started)") +
                    ".\n\n" + Tail(result.Output, 600) + "\n\nYou can run it yourself in a terminal.";
                break;
            }
        }

        var after = McpSetupPlan.Create(await DetectAsync().ConfigureAwait(false));
        await jtf.SwitchToMainThreadAsync();
        Show(package, (failure is null ? "" : failure + "\n\n") + "Status now:\n" + Summary(after) +
            (after.IsComplete ? "\n\nDone. Restart agent sessions that were already running (they keep the old registration until then), then start your agent in the folder of your solution." : ""));
    }

    private static async Task<SetupState> DetectAsync()
    {
        var launcher = McpServerDeployer.LauncherPath(Root);
        var state = new SetupState { LauncherPath = launcher, ServerDeployed = McpServerDeployer.IsDeployed(Root) };

        var runtimes = await ProcessRunner.RunAsync("dotnet", "--list-runtimes", QuickProbe).ConfigureAwait(false);
        if (runtimes.Started && !runtimes.TimedOut && runtimes.ExitCode == 0) state.DotnetRuntimeMajor = McpSetupPlan.ParseRuntimeMajor(runtimes.Output) ?? 0;

        var where = await ProcessRunner.RunAsync("cmd.exe", "/c where claude", QuickProbe).ConfigureAwait(false);
        if (where.Started && !where.TimedOut)
        {
            state.ClaudeFound = where.ExitCode == 0;
            if (state.ClaudeFound is true)
            {
                var list = await ProcessRunner.RunAsync("cmd.exe", "/c claude mcp list", SlowProbe).ConfigureAwait(false);
                if (list.Started && !list.TimedOut && list.ExitCode == 0) state.Registration = McpSetupPlan.ParseRegistration(list.Output, launcher);
            }
        }

        return state;
    }

    private static string Summary(SetupPlan plan) => string.Join("\n", plan.Lines);

    private static string Tail(string text, int max)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= max ? trimmed : "..." + trimmed.Substring(trimmed.Length - max);
    }

    private static void Show(IServiceProvider provider, string text)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        VsShellUtilities.ShowMessageBox(provider, text, "Debugging via MCP - set up AI agent", OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }

    private static bool Ask(IServiceProvider provider, string text)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        const int yes = 6;
        return VsShellUtilities.ShowMessageBox(provider, text, "Debugging via MCP - set up AI agent", OLEMSGICON.OLEMSGICON_QUERY, OLEMSGBUTTON.OLEMSGBUTTON_YESNO, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND) == yes;
    }

    private static void SetStatus(AsyncPackage package, string text)
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
