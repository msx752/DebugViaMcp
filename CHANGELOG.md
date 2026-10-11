# Changelog

All notable changes to VsMcpDebugger. Versions follow [Semantic Versioning](https://semver.org/); the extension and
the `vsmcpdebugger` tool are released together with the same version.

## 0.2.0 — Safety & control (preview)

### Added
- `set_value` and `eval` with `allowSideEffects`: change program state from the agent.
- Permission tiers (Read / Control / Mutate) in **Tools → Options → VsMcpDebugger**, enforced inside Visual Studio.
- Consent info bar for Mutate operations — **Allow once / Allow for session / Deny**; no answer within the timeout
  (default 60 s) means deny. Breakpoint conditions and tracepoints count as Mutate because they run on every hit.
- Audit log (`%LOCALAPPDATA%\VsMcpDebugger\logs\audit-*.log`) and the **VsMcpDebugger Activity** Output pane.
- `callId` on every call, shown in both the MCP server and Visual Studio logs.
- **Extensions → VsMcpDebugger** menu: Disconnect AI (Ctrl+Alt+Shift+F12), Reconnect AI, Show Activity, Open Logs
  Folder, Options. Status bar indicator showing the connected client.
- `debug_status` reports the active permissions; snapshot arguments are returned under `data`.
- The MCP log level follows the Visual Studio setting (`VSMCPDEBUGGER_LOG_LEVEL` still overrides it).

### Changed
- Wire protocol 2.0 (every request carries a call context; errors carry a hint and a reason). The extension and the
  tool must both be 0.2.0.
- The dotnet tool package is now called `VsMcpDebugger`.

### Security
- Consent text is shown in full with invisible and misleading characters escaped; mutate texts are limited to
  500 characters; Allow clicks in the first second after the bar appears are ignored.
- Disconnect AI refuses everything on existing connections immediately and closes them.
- See [docs/security.md](docs/security.md).

## 0.1.0 — Walking skeleton

- Breakpoints (file/line, function, conditions, hit counts, tracepoints), start/continue/pause/step/wait,
  call stack, variables with drill-down, side-effect-free evaluation.
- Per-user named pipe with token handshake; logs in `%LOCALAPPDATA%\VsMcpDebugger\logs`.
