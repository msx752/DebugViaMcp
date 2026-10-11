# VsMcpDebugger

Let AI coding agents drive the Visual Studio debugger over the Model Context Protocol (MCP):
breakpoints, stepping, call stacks, variables and QuickWatch-style evaluation.

> Status: early development (milestone M2). Not yet published.

## What works today (M2)

- Breakpoints: file/line or function, conditions, hit counts, tracepoints
- Execution: start, continue, pause, step into/over/out, wait for break
- Inspection: call stack, locals/arguments, drill-down by path, side-effect-free evaluation, `set_value`, and
  evaluation with side effects (`eval` with `allowSideEffects`)
- Safety: permission tiers (Read / Control / Mutate) in **Tools → Options → VsMcpDebugger**; Mutate operations ask in a
  Visual Studio info bar (Allow once / Allow for session / Deny, no answer = deny); every state change is audited
  (`audit-*.log`, Output → **VsMcpDebugger Activity**); **Disconnect AI** (Ctrl+Alt+Shift+F12, Extensions →
  VsMcpDebugger). See [docs/security.md](docs/security.md).
- Logs: `%LOCALAPPDATA%\VsMcpDebugger\logs` and the **VsMcpDebugger** Output pane in Visual Studio; every call has a
  `callId` that appears in both the MCP server and Visual Studio logs

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
