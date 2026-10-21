# Debugging via MCP

[![Install from the Visual Studio Marketplace](https://img.shields.io/badge/Visual%20Studio%20Marketplace-Install-5C2D91?logo=visualstudio&logoColor=white)](https://marketplace.visualstudio.com/items?itemName=msx752.DebugViaMcp)

Let AI coding agents drive the Visual Studio debugger over the Model Context Protocol (MCP):
breakpoints, stepping, call stacks, variables and QuickWatch-style evaluation.

It works with Visual Studio 2022 (17.14+) and Visual Studio 2026.

**[Install it from the Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=msx752.DebugViaMcp)**, or in Visual Studio: **Extensions → Manage Extensions** and search for *Debugging via MCP*.

> Status: preview. The tool set is growing; feedback and issues are welcome.

## What works today (M3)

- Breakpoints: file/line or function, conditions, hit counts, tracepoints, enable/disable (`bp_enable`)
- Several Visual Studios, one agent each: the right instance is chosen by folder, unrelated agents need your approval,
  and a second agent gets `in_use` while the first holds a Visual Studio
- Execution: start, continue, pause, step into/over/out, wait for break, run to line, attach to a running process
  (detach on stop)
- Inspection: threads, call stack, frame selection (`frame_select`), the current exception, locals/arguments, drill-down by path, side-effect-free evaluation, `set_value`, and
  evaluation with side effects (`eval` with `allowSideEffects`)
- IDE state: Output window panes (`output_read`), Error List (`errors_list`), Visual Studio builds (`vs_build`); `debug_start` builds first and returns a failed build (read it with `errors_list`) instead of opening Visual Studio's build-error dialog
- Diagnostics: `diag` returns the MCP server's recent log lines
- Safety: permission tiers (Read / Control / Mutate) in **Tools → Options → All Settings → Debugging → Debugging via MCP**; Mutate operations ask in a
  Visual Studio info bar (Allow once / Allow for session / Deny, no answer = deny); every state change is audited
  (`audit-*.log`, Output → **Debugging via MCP Audit**); **Disconnect AI** (Ctrl+Alt+Shift+F12, Extensions →
  Debugging via MCP). See [docs/security.md](docs/security.md).
- Logs: `%LOCALAPPDATA%\DebugViaMcp\logs` and the **Debugging via MCP Log** Output pane in Visual Studio; every call has a
  `callId` that appears in both the MCP server and Visual Studio logs

## Install from source (local build)

1. Build the release artifacts: `powershell -File scripts/release.ps1` → `artifacts/DebugViaMcp.vsix` (nothing is published). For a normal install use the Marketplace link above.
2. Install the extension: double-click `artifacts/DebugViaMcp.vsix` (Visual Studio 2022 17.14+ or 2026).
3. Start Visual Studio once. The extension carries the MCP server and copies it to `%LOCALAPPDATA%\DebugViaMcp\bin`
   (nothing to install separately; it needs the .NET 10 runtime). Then tell your AI client about it:
   **Extensions → Debugging via MCP → Set Up AI Agent…** checks the .NET runtime and the installed server, and shows a
   list of clients (Claude Code, Codex, Cursor, another MCP client). Pick yours and copy the command or configuration
   shown under it. For example, for Claude Code:

       claude mcp add --scope user debugviamcp -- "%LOCALAPPDATA%\DebugViaMcp\debugviamcp.cmd" serve

   The window only shows text; it never changes any client's settings.

The launcher path never changes when the extension is updated, so the registration stays valid. Updating the extension
installs the new server next to the old one; an old copy that a running agent still uses is removed on a later start.
Uninstalling the extension leaves `%LOCALAPPDATA%\DebugViaMcp` (logs, settings of the server, old copies) behind; delete the
folder if you want it gone. Release notes: [CHANGELOG.md](CHANGELOG.md).

## Troubleshooting

| Symptom | What to check |
|---|---|
| The agent says no Visual Studio instance is running | Visual Studio must be open with the Debugging via MCP extension installed (Extensions → Manage Extensions → Installed). Open a solution; the Output pane "Debugging via MCP Log" shows `ready` when it started. |
| The agent cannot start the server | The server needs the .NET 10 runtime (`dotnet --list-runtimes`). Start Visual Studio once so the extension installs it, then check that `%LOCALAPPDATA%\DebugViaMcp\debugviamcp.cmd` exists; **Set Up AI Agent…** reports both. |
| `ai_disabled` | Someone chose Extensions → Debugging via MCP → Disconnect AI. Choose Reconnect AI, then retry. |
| `version_mismatch` or `unsupported` | Visual Studio still runs an older extension than the server in use (or the reverse). Restart Visual Studio after updating the extension, and restart the agent session. |
| `in_use` | Another agent holds that Visual Studio; ask it to call `vs_release`, or use another instance (`vs_instances`). |
| A consent question ended as `denied` / `timeout` | Visual Studio asks in an info bar at the top of its main window and waits 60 s (Tools → Options → All Settings → Debugging → Debugging via MCP → Consent timeout); no answer means deny. When Visual Studio is in the background its taskbar button flashes. Bring it forward, then ask the agent to retry. |
| `eval` says a type or namespace does not exist | The expression evaluator only sees assemblies the program has already loaded. Use the type in the code before the breakpoint (for example call `JsonSerializer.Serialize` once), or evaluate something else. |
| `eval` says `Evaluation timed out` | Visual Studio stops an evaluation after about 5 s. Evaluate a smaller expression (`Take(1000)`), or read a field or property. Code that waits for another thread (`Task.Result`, a lock held by a paused thread) cannot finish while the program is stopped. |
| Uninstalling the extension hangs | The server does not run from the extension folder, so agents cannot block the removal; close every Visual Studio window and any other VSIXInstaller. For development, `scripts\reinstall-vsix.ps1` stops a stuck installer after 3 minutes (`-InstallerTimeoutSeconds`). |
| An out-of-date warning (or Build bar) after editing files outside Visual Studio | Visual Studio notices external edits late, sometimes only when its window is activated. Build in Visual Studio (or call `vs_build`) before debugging; the warning is a hint, it never blocks. With Visual Studio's default option **On Run, when projects are out of date = Always build** (Tools → Options → Projects and Solutions → Build and Run) Visual Studio builds by itself as debugging starts, so the warning and the bar only appear while a program is running, or when that option is Never build / Prompt to build. |
| Visual Studio says the extension is already installed | Same version number: uninstall it first (Extensions → Manage Extensions), restart Visual Studio, then double-click the VSIX again. |
| Something else | Call the `diag` tool for the server log, and look at View → Output → Debugging via MCP Log and Debugging via MCP Audit. |

## Build from source

    powershell -File scripts\build.ps1

Requires the .NET 10 SDK and Visual Studio 2022 17.14+ or 2026. The VSIX builds with Visual Studio's MSBuild and the
`Microsoft.VSSDK.BuildTools` package.

## Repository layout

| Folder | What is in it |
|---|---|
| `src/DebugViaMcp.Mcp` | The MCP server (`debugviamcp serve`; it travels inside the extension): tools, connection to Visual Studio, logging |
| `src/DebugViaMcp.Protocol` | Wire protocol shared by the server and the extension (DTOs, errors, discovery paths) |
| `src/DebugViaMcp.Vsix.Core` | The extension's logic without Visual Studio types, by feature: `Security` (consent, permissions, audit), `Bridge` (pipe host, request handling), `Instances` (lease, discovery), `Builds`, `Setup`, `Debugging`, `Support`, `Abstractions` |
| `src/DebugViaMcp.Vsix` | The Visual Studio extension: package, commands, UI, settings (`UnifiedSettings`), `Dte` and `Infrastructure` adapters |
| `tests/` | One test project per source project (the Vsix.Core tests mirror its folders), `ArchitectureTests` for the dependency rule, and `e2e/` drivers that run against a real Visual Studio |
| `samples/` | `OrderDemo` and `FaultDemo`, small programs to debug with the agent |
| `docs/` | `security.md`, `releasing.md` (versions and how to cut a release), `design/` (specifications), `testing/` (manual end-to-end runs), `marketplace/` (listing text, images and the publish manifest) |
| `scripts/` | Build (`build.ps1`), release (`release.ps1`) and development helpers (`reinstall-vsix.ps1`, icon and image generators) |

## Reinstall the extension while developing

Visual Studio treats a VSIX with the same version as "already installed". Close Visual Studio, then:

    powershell -File scripts\reinstall-vsix.ps1 -Build

It builds the VSIX, removes the installed extension and installs the new build (paths are relative to the repository;
it never starts or closes Visual Studio and refuses to run while it is open). `-UninstallOnly` just removes it.

## Try it with Claude Code

See [docs/testing/m2-manual-e2e.md](docs/testing/m2-manual-e2e.md).

## Design

See [docs/design/design.md](docs/design/design.md).

## License

MIT
