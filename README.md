# VsMcpDebugger

Let AI coding agents drive the Visual Studio debugger over the Model Context Protocol (MCP):
breakpoints, stepping, call stacks, variables and QuickWatch-style evaluation.

> Status: early development (milestone M3). Not yet published.

## What works today (M3)

- Breakpoints: file/line or function, conditions, hit counts, tracepoints, enable/disable (`bp_enable`)
- Execution: start, continue, pause, step into/over/out, wait for break, run to line, attach to a running process
  (detach on stop)
- Inspection: threads, call stack, frame selection (`frame_select`), the current exception, locals/arguments, drill-down by path, side-effect-free evaluation, `set_value`, and
  evaluation with side effects (`eval` with `allowSideEffects`)
- IDE state: Output window panes (`output_read`), Error List (`errors_list`), Visual Studio builds (`vs_build`)
- Diagnostics: `diag` returns the MCP server's recent log lines
- Safety: permission tiers (Read / Control / Mutate) in **Tools → Options → VsMcpDebugger**; Mutate operations ask in a
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

       dotnet tool install -g VsMcpDebugger --add-source artifacts
       claude mcp add vsmcpdebugger -- vsmcpdebugger serve

Once published, steps 1–2 become "install VsMcpDebugger from the Visual Studio Marketplace" and step 3 drops
`--add-source`. Release notes: [CHANGELOG.md](CHANGELOG.md).

## Build from source

    powershell -File build.ps1

Requires the .NET 10 SDK and Visual Studio 2022 17.14+ or 2026. The VSIX builds with Visual Studio's MSBuild and the
`Microsoft.VSSDK.BuildTools` package.

## Try it with Claude Code

See [docs/testing/m2-manual-e2e.md](docs/testing/m2-manual-e2e.md).

## Design

See [docs/design/2026-10-11-vsmcpdebugger-design.md](docs/design/2026-10-11-vsmcpdebugger-design.md).

## License

MIT
