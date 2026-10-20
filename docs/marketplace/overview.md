# Debugging via MCP

Let AI coding agents such as **Claude Code** drive the Visual Studio debugger over the
[Model Context Protocol](https://modelcontextprotocol.io): set breakpoints, start and step, read the call stack and
variables, and evaluate expressions — while you stay in control.

> Preview: the tool set is growing; feedback is welcome on the repository.

![How Debugging via MCP works](https://raw.githubusercontent.com/msx752/DebugViaMcp/main/docs/marketplace/images/how-it-works.png)

## What the agent can do

- **Breakpoints** — file/line or function, conditions, hit counts, tracepoints, enable/disable
- **Several Visual Studios** — one agent each: instance chosen by folder, owner approval for unrelated agents, `vs_release`
- **Execution** — start, continue, pause, step into/over/out, wait for the next break, run to line, attach to a
  running process (detach on stop)
- **Inspection** — threads, call stack, frame selection, the current exception, locals and arguments, drill into members, QuickWatch-style evaluation
- **IDE state** — Output window panes, the Error List, and Visual Studio builds
- **Changes** — `set_value` and evaluation with side effects, **only with your approval**

## You stay in control

- **Permission tiers** — Read, Control and Mutate, each Allow / Ask / Deny, in
  **Tools → Options → All Settings → Debugging → Debugging via MCP**. Visual Studio enforces them; the agent cannot change them.
- **Consent info bar** — before anything changes your program, Visual Studio asks:
  *Allow once*, *Allow for session* or *Deny*. No answer means deny.
- **Audit trail** — every state change is logged with a call id and shown in
  **View → Output → Debugging via MCP Activity**.
- **Disconnect AI** — **Extensions → Debugging via MCP → Disconnect AI** (Ctrl+Alt+Shift+F12) cuts the agent off at once.
- **Local only** — a per-user named pipe; no network, no telemetry.

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
