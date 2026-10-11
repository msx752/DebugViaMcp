# VsMcpDebugger

Let AI coding agents drive the Visual Studio debugger over the Model Context Protocol (MCP):
breakpoints, stepping, call stacks, variables and QuickWatch-style evaluation.

> Status: early development (milestone M1). Not yet published.

## What works today (M1)

- Breakpoints: file/line or function, conditions, hit counts, tracepoints
- Execution: start, continue, pause, step into/over/out, wait for break
- Inspection: call stack, locals/arguments, drill-down by path, side-effect-free evaluation
- Safety: breakpoint conditions and tracepoint placeholders must not have side effects; evaluation runs with `, nse`;
  breakpoint files must be inside the open solution; the pipe and instance file are restricted to the current user
- Logs: `%LOCALAPPDATA%\VsMcpDebugger\logs` and the **VsMcpDebugger** Output pane in Visual Studio

## Build from source

    powershell -File build.ps1

Requires the .NET 10 SDK and Visual Studio 2022 17.14+ or 2026. The VSIX builds with Visual Studio's MSBuild and the
`Microsoft.VSSDK.BuildTools` package.

## Try it with Claude Code

See [docs/testing/m1-manual-e2e.md](docs/testing/m1-manual-e2e.md).

## Design

See [docs/design/2026-10-11-vsmcpdebugger-design.md](docs/design/2026-10-11-vsmcpdebugger-design.md).

## License

MIT
