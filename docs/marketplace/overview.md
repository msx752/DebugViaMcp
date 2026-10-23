# Debugging via MCP

**Let your AI agent debug in Visual Studio, and keep your hand on the brake.**

![How Debugging via MCP works](https://raw.githubusercontent.com/msx752/DebugViaMcp/main/docs/marketplace/images/how-it-works.png)

Ask Claude Code, Codex or Cursor to chase a bug and it can set breakpoints, step through your code, read variables and
evaluate expressions in the real debugger. Anything that would change your program waits for your OK.

> *"Break in OrderService.Submit when total > 1000 and show me the order."*

Here is what happens:

1. The agent sets a conditional breakpoint. The condition runs inside your program, so Visual Studio asks you first:
   *Allow once*, *Allow for session* or *Deny*.
2. It starts debugging and waits for the hit.
3. It reads the call stack and the order, and tells you what it found.

## Why you'll like it

- **The real debugger.** Your solution, your breakpoints, your Output window and Error List. No guessing from logs.
- **You decide.** Read, Control and Mutate are each set to Allow, Ask or Deny. By default the agent may look and steer
  freely and has to ask before it changes anything.
- **Nothing leaves your machine.** A per-user local pipe. No network, no telemetry.
- **Secrets stay hidden.** Values that look like passwords, tokens or keys are replaced by `[masked]` before the agent sees them (a setting, on by default).
- **Bring your own agent.** Any MCP client works. A setup window gives you the exact command for Claude Code, Codex and Cursor.

## Get started

1. Install the extension and restart Visual Studio. You need Visual Studio 2022 17.14+ or 2026, and the
   [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (the MCP server that ships inside the extension is a .NET program).
2. Open **Extensions → Debugging via MCP → Set Up AI Agent…**, pick your client and copy the command it shows. For Claude Code:

       claude mcp add --scope user debugviamcp -- "%LOCALAPPDATA%\DebugViaMcp\debugviamcp.cmd" serve

3. Open your solution (the Output pane **Debugging via MCP Log** says *ready*) and ask your agent for something to debug.

## You stay in control

| Tier | What it covers | Default |
|---|---|---|
| **Read** | Looking only: status, call stack, variables, the current exception, Output window, Error List | Allow |
| **Control** | Steering the debugger: breakpoints without conditions, start, stop, step, builds, Exception Settings | Allow |
| **Mutate** | Anything that runs code in or changes your program: `set_value`, evaluation with side effects, conditions and tracepoints, attaching to a process | **Ask** |

The exact list for each tier is in the [security model](https://github.com/msx752/DebugViaMcp/blob/main/docs/security.md).

Change them in **Tools → Options → All Settings → Debugging → Debugging via MCP** (Visual Studio 2022: **Tools → Options → Debugging via MCP → General**). Visual Studio enforces them; the agent cannot.

- **Consent bar.** A question appears as an info bar in Visual Studio. No answer within the timeout (60 s by default) means *deny*.
- **Audit trail.** Every state change is logged with a call id in **View → Output → Debugging via MCP Audit** and in a file.
- **Mask sensitive values.** Passwords, tokens, keys and connection-string secrets in your program's values show as `[masked]` to the agent. It is a heuristic, not a guarantee; turning it off asks you to confirm.
- **Disconnect AI.** **Extensions → Debugging via MCP → Disconnect AI** (Ctrl+Alt+Shift+F12) denies pending questions and cuts
  every agent off at once. **Reconnect AI** lets them back in.

## What the agent can do

Breakpoints (conditions, hit counts, tracepoints), start, step, run to a line, attach to a process, call stack, locals with drill-down,
evaluation, the current exception, Visual Studio builds and the Error List, Output window panes, which exceptions the debugger stops on, and
more than one Visual Studio at a time. Nothing that changes your program happens without your approval.

The full list of tools, with what each one does, is in the [README](https://github.com/msx752/DebugViaMcp/blob/main/README.md#what-it-can-do). New tools appear there first.

## Several Visual Studios, one agent each

With more than one Visual Studio open, the agent uses the one whose solution folder matches the folder it runs in. An agent from an
unrelated folder needs your approval once, and a second agent gets `in_use` until the first lets go.

![Several Visual Studios, one agent each](https://raw.githubusercontent.com/msx752/DebugViaMcp/main/docs/marketplace/images/several-visual-studios.png)

## Troubleshooting

| Symptom | What to check |
|---|---|
| The agent says no Visual Studio instance is running | Visual Studio must be open with a solution loaded and this extension installed. The **Debugging via MCP Log** pane shows `ready` when it started. |
| The agent cannot start the server | Check the .NET 10 runtime (`dotnet --list-runtimes`), start Visual Studio once so the extension installs the server, and confirm `%LOCALAPPDATA%\DebugViaMcp\debugviamcp.cmd` exists. **Set Up AI Agent…** reports both. |
| A consent question ended as `denied` or `timeout` | Visual Studio asks in an info bar at the top of its main window and waits for the consent timeout. No answer means deny. |

Error codes (`in_use`, `ai_disabled`, `version_mismatch`), `eval` limits, update and uninstall problems are in the
[README troubleshooting table](https://github.com/msx752/DebugViaMcp/blob/main/README.md#troubleshooting). For anything else call the `diag` tool and look at
**Debugging via MCP Log** and **Debugging via MCP Audit** in View → Output.

Documentation: [README](https://github.com/msx752/DebugViaMcp/blob/main/README.md) · [security model](https://github.com/msx752/DebugViaMcp/blob/main/docs/security.md) · [release notes](https://github.com/msx752/DebugViaMcp/blob/main/CHANGELOG.md) · source: [github.com/msx752/DebugViaMcp](https://github.com/msx752/DebugViaMcp)
