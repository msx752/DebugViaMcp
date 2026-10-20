# Debugging via MCP

Let AI coding agents such as **Claude Code** drive the Visual Studio debugger over the
[Model Context Protocol](https://modelcontextprotocol.io): set breakpoints, start and step, read the call stack and
variables, and evaluate expressions — while you stay in control.

> Preview: the tool set is growing; feedback is welcome on the repository.

![How Debugging via MCP works](https://raw.githubusercontent.com/msx752/DebugViaMcp/main/docs/marketplace/images/how-it-works.png)

## What the agent can do

27 tools, in these groups:

- **Breakpoints** (`bp_set`, `bp_list`, `bp_enable`, `bp_remove`) — file/line or function, conditions, hit counts, tracepoints (log without stopping)
- **Execution** (`debug_start`, `debug_continue`, `debug_pause`, `debug_step`, `debug_run_to`, `debug_wait`, `debug_stop`, `debug_attach`) — start (the solution's startup project or a named project, which leaves your startup project alone), continue, pause, step into/over/out, run to a line, wait for the next break, attach to a running process (stopping detaches without ending it)
- **Inspection** (`debug_status`, `threads`, `stack`, `frame_select`, `vars`, `eval`, `exception`) — mode and break reason, threads, call stack, frame selection, locals and arguments with drill-down into members (`order.Lines[2]`), QuickWatch-style evaluation, the current exception with inner exceptions and throw site
- **IDE state** (`vs_build`, `errors_list`, `output_read`) — Visual Studio builds, the Error List, and the Output window panes (the build and debug panes by name)
- **Several Visual Studios** (`vs_instances`, `vs_select`, `vs_release`) — one agent each: the instance is chosen by the folder the agent runs in, an agent from an unrelated folder needs the owner's approval, and a second agent gets `in_use` until the first calls `vs_release`
- **Changes** (`set_value`, `eval` with side effects, breakpoint conditions and tracepoints, `debug_attach`) — **only with your approval**
- **Diagnostics** (`diag`) — the MCP server's own log

`debug_start`, `debug_attach` and `bp_set` warn when the built program is older than the sources, and Visual Studio offers an optional **Build** bar.

## You stay in control

- **Permission tiers** — **Read**, **Control** and **Mutate**, each set to Allow, Ask or Deny in
  **Tools → Options → All Settings → Debugging → Debugging via MCP**. Visual Studio enforces them; the agent cannot change them.
  - *Read* (default Allow): status, threads, call stack, variables, evaluation without side effects, the current exception, the Output window and the Error List.
  - *Control* (default Allow): breakpoints without conditions, start, stop, continue, pause, step, run to, and Visual Studio builds.
  - *Mutate* (default **Ask**): `set_value`, evaluation with side effects, breakpoint conditions and tracepoints (they run in your program on every hit), and attaching to a process.
- **Consent info bar** — before anything in Mutate (or anything set to Ask) runs, Visual Studio asks in an info bar:
  *Allow once*, *Allow for session* or *Deny*. No answer within the consent timeout (60 s by default, 15–300) means deny.
- **Audit trail** — every state change is logged with a call id and shown in
  **View → Output → Debugging via MCP Activity**.
- **Disconnect AI** — **Extensions → Debugging via MCP → Disconnect AI** (Ctrl+Alt+Shift+F12) denies pending questions and cuts the agent off at once; **Reconnect AI** lets it back in.
- **Local only** — a per-user named pipe; no network, no telemetry.

The **Extensions → Debugging via MCP** menu also has **Set Up AI Agent…**, **Show Activity**, **Open Logs Folder** and **Options…**.
Other settings: accept AI connections when Visual Studio starts, the status bar indicator, log level, the longest wait for a break
(default 120 s) and for a build (default 600 s), and log retention (default 7 days).

![Several Visual Studios, one agent each](https://raw.githubusercontent.com/msx752/DebugViaMcp/main/docs/marketplace/images/several-visual-studios.png)

## Before you start

The extension alone does nothing visible: it opens a local connection that an AI agent uses. You need three things.

1. **This extension** (Visual Studio 2022 17.14+ or Visual Studio 2026).
2. **The .NET 10 runtime** (the MCP server that comes with the extension is a .NET program).
3. **An MCP client**, for example [Claude Code](https://claude.com/claude-code).

## Get started

1. Install this extension (Visual Studio 2022 17.14+ or Visual Studio 2026), then restart Visual Studio.
2. Tell your AI client about the MCP server. The extension installs the server itself when Visual Studio starts
   (into `%LOCALAPPDATA%\DebugViaMcp`), so only the registration is left. In Visual Studio, open
   **Extensions → Debugging via MCP → Set Up AI Agent…**, pick your client (Claude Code, Codex, Cursor or another MCP
   client) and copy the command or configuration shown under it. For Claude Code that is:

       claude mcp add --scope user debugviamcp -- "%LOCALAPPDATA%\DebugViaMcp\debugviamcp.cmd" serve

   Debugging via MCP works with Visual Studio only, not with Visual Studio Code.

3. Open your solution in Visual Studio (the Output pane "Debugging via MCP" shows *ready*) and ask, for example: *"Break in OrderService.Submit when total > 1000 and show me the order."*

## Troubleshooting

| Symptom | What to check |
|---|---|
| The agent says no Visual Studio instance is running | Visual Studio must be open with the Debugging via MCP extension installed (Extensions → Manage Extensions → Installed). Open a solution; the Output pane "Debugging via MCP" shows `ready` when it started. |
| The agent cannot start the server | The server needs the .NET 10 runtime (`dotnet --list-runtimes`). Start Visual Studio once so the extension installs it, then check that `%LOCALAPPDATA%\DebugViaMcp\debugviamcp.cmd` exists; **Set Up AI Agent…** reports both. |
| `ai_disabled` | Someone chose Extensions → Debugging via MCP → Disconnect AI. Choose Reconnect AI, then retry. |
| `version_mismatch` or `unsupported` | Visual Studio still runs an older extension than the server in use (or the reverse). Restart Visual Studio after updating the extension, and restart the agent session. |
| `in_use` | Another agent holds that Visual Studio; ask it to call `vs_release`, or use another instance (`vs_instances`). |
| A consent question ended as `denied` / `timeout` | Visual Studio asks in an info bar at the top of its main window and waits 60 s (Tools → Options → All Settings → Debugging → Debugging via MCP → Consent timeout); no answer means deny. When Visual Studio is in the background its taskbar button flashes. Bring it forward, then ask the agent to retry. |
| `eval` says a type or namespace does not exist | The expression evaluator only sees assemblies the program has already loaded. Use the type in the code before the breakpoint (for example call `JsonSerializer.Serialize` once), or evaluate something else. |
| `eval` says `Evaluation timed out` | Visual Studio stops an evaluation after about 5 s. Evaluate a smaller expression (`Take(1000)`), or read a field or property. Code that waits for another thread (`Task.Result`, a lock held by a paused thread) cannot finish while the program is stopped. |
| Uninstalling the extension hangs | The server does not run from the extension folder, so agents cannot block the removal. Close every Visual Studio window and any other VSIXInstaller, then try again. |
| An out-of-date warning (or Build bar) after editing files outside Visual Studio | Visual Studio notices external edits late, sometimes only when its window is activated. Build in Visual Studio (or call `vs_build`) before debugging; the warning is a hint, it never blocks. With Visual Studio's default option **On Run, when projects are out of date = Always build** (Tools → Options → Projects and Solutions → Build and Run) Visual Studio builds by itself as debugging starts, so the warning and the bar only appear while a program is running, or when that option is Never build / Prompt to build. |
| Something else | Call the `diag` tool for the server log, and look at View → Output → Debugging via MCP and Debugging via MCP Activity. |

Documentation, security model and source: [github.com/msx752/DebugViaMcp](https://github.com/msx752/DebugViaMcp)
