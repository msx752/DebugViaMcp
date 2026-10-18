# VsMcpDebugger

Let AI coding agents drive the Visual Studio debugger over the Model Context Protocol (MCP):
breakpoints, stepping, call stacks, variables and QuickWatch-style evaluation.

> Status: early development (milestone M3). Not yet published.

## What works today (M3)

- Breakpoints: file/line or function, conditions, hit counts, tracepoints, enable/disable (`bp_enable`)
- Several Visual Studios, one agent each: the right instance is chosen by folder, unrelated agents need your approval,
  and a second agent gets `in_use` while the first holds a Visual Studio
- Execution: start, continue, pause, step into/over/out, wait for break, run to line, attach to a running process
  (detach on stop)
- Inspection: threads, call stack, frame selection (`frame_select`), the current exception, locals/arguments, drill-down by path, side-effect-free evaluation, `set_value`, and
  evaluation with side effects (`eval` with `allowSideEffects`)
- IDE state: Output window panes (`output_read`), Error List (`errors_list`), Visual Studio builds (`vs_build`)
- Diagnostics: `diag` returns the MCP server's recent log lines
- Safety: permission tiers (Read / Control / Mutate) in **Tools → Options → Debugging → Debugging via MCP**; Mutate operations ask in a
  Visual Studio info bar (Allow once / Allow for session / Deny, no answer = deny); every state change is audited
  (`audit-*.log`, Output → **VsMcpDebugger Activity**); **Disconnect AI** (Ctrl+Alt+Shift+F12, Extensions →
  VsMcpDebugger). See [docs/security.md](docs/security.md).
- Logs: `%LOCALAPPDATA%\VsMcpDebugger\logs` and the **VsMcpDebugger** Output pane in Visual Studio; every call has a
  `callId` that appears in both the MCP server and Visual Studio logs

## Install (local build)

1. Build the release artifacts: `powershell -File scripts/release.ps1` → `artifacts/VsMcpDebugger.vsix` and
   `artifacts/VsMcpDebugger.<version>.nupkg` (nothing is published).
2. Install the extension: double-click `artifacts/VsMcpDebugger.vsix` (Visual Studio 2022 17.14+ or 2026).
3. Install the MCP server and register it with Claude Code:

       dotnet tool install -g VsMcpDebugger --prerelease --add-source artifacts
       claude mcp add vsmcpdebugger -- vsmcpdebugger serve

Installed only the extension? **Extensions → VsMcpDebugger → Set Up AI Agent…** checks the .NET SDK, the MCP server
tool and the Claude Code registration, lists what is missing and runs the fixing commands after you agree.
**Copy MCP Setup Command** just copies the two commands of step 3 to the clipboard.

Once published, steps 1–2 become "install VsMcpDebugger from the Visual Studio Marketplace" and step 3 drops
`--add-source`. Release notes: [CHANGELOG.md](CHANGELOG.md).

## Troubleshooting

| Symptom | What to check |
|---|---|
| The agent says no Visual Studio instance is running | Visual Studio must be open with the VsMcpDebugger extension installed (Extensions → Manage Extensions → Installed). Open a solution; the Output pane "VsMcpDebugger" shows `ready` when it started. |
| `vsmcpdebugger` is not found | The .NET tool folder must be on your PATH (`%USERPROFILE%\.dotnet\tools`). Open a new terminal after installing. |
| `dotnet tool install` fails to find the package | It is a prerelease: add `--prerelease`. It needs the .NET 10 SDK (`dotnet --list-sdks`). |
| `ai_disabled` | Someone chose Extensions → VsMcpDebugger → Disconnect AI. Choose Reconnect AI, then retry. |
| `version_mismatch` or `unsupported` | The extension and the tool are different releases. Update both to the same version. |
| `in_use` | Another agent holds that Visual Studio; ask it to call `vs_release`, or use another instance (`vs_instances`). |
| A consent question ended as `denied` / `timeout` | Visual Studio asks in an info bar at the top of its main window and waits 60 s (Tools → Options → VsMcpDebugger → Consent timeout); no answer means deny. When Visual Studio is in the background its taskbar button flashes. Bring it forward, then ask the agent to retry. |
| `eval` says a type or namespace does not exist | The expression evaluator only sees assemblies the program has already loaded. Use the type in the code before the breakpoint (for example call `JsonSerializer.Serialize` once), or evaluate something else. |
| `eval` says `Evaluation timed out` | Visual Studio stops an evaluation after about 5 s. Evaluate a smaller expression (`Take(1000)`), or read a field or property. Code that waits for another thread (`Task.Result`, a lock held by a paused thread) cannot finish while the program is stopped. |
| `dotnet tool update` fails with "Access denied" | Running agent sessions use the installed tool and lock its files. Close those sessions (the **Set Up AI Agent** command names how many are running), update, then restart the agents. |
| An out-of-date warning (or Rebuild bar) after editing files outside Visual Studio | Visual Studio notices external edits late, sometimes only when its window is activated. Build in Visual Studio (or call `vs_build`) before debugging; the warning is a hint, it never blocks. |
| Visual Studio says the extension is already installed | Same version number: uninstall it first (Extensions → Manage Extensions), restart Visual Studio, then double-click the VSIX again. |
| Something else | Call the `diag` tool for the server log, and look at View → Output → VsMcpDebugger and VsMcpDebugger Activity. |

## Build from source

    powershell -File build.ps1

Requires the .NET 10 SDK and Visual Studio 2022 17.14+ or 2026. The VSIX builds with Visual Studio's MSBuild and the
`Microsoft.VSSDK.BuildTools` package.

## Reinstall the extension while developing

Visual Studio treats a VSIX with the same version as "already installed". Close Visual Studio, then:

    powershell -File scriptseinstall-vsix.ps1 -Build

It builds the VSIX, removes the installed extension and installs the new build (paths are relative to the repository; it never
starts or closes Visual Studio and refuses to run while it is open). `-UninstallOnly` just removes it.

## Try it with Claude Code

See [docs/testing/m2-manual-e2e.md](docs/testing/m2-manual-e2e.md).

## Design

See [docs/design/2026-10-11-vsmcpdebugger-design.md](docs/design/2026-10-11-vsmcpdebugger-design.md).

## License

MIT
