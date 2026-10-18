# Changelog

All notable changes to VsMcpDebugger. Versions follow [Semantic Versioning](https://semver.org/); the extension and
the `vsmcpdebugger` tool are released together with the same version.

## 0.4.0 — Several Visual Studios (preview)

### Added
- Several Visual Studios, one agent each. The MCP server picks the Visual Studio whose solution folder relates to the
  folder the agent runs in (the agent in the repository root, the solution in `src\`, works too); `vs_select` still
  overrides.
- An agent whose folder is not the solution's must be approved once by the Visual Studio owner (info bar), per
  connection.
- One agent at a time per Visual Studio: the first state-changing command (breakpoint, build, start, attach, ...) takes
  it, a second agent gets `in_use`. `vs_release`, a closed connection or Disconnect AI frees it. The status bar and
  `vs_instances` show who holds it.
- **Extensions → VsMcpDebugger → Set Up AI Agent…**: checks the .NET SDK, the `vsmcpdebugger` tool (and that its version
  matches the extension) and the Claude Code registration, lists what is missing and, after you confirm, installs or
  updates the tool and registers it (`claude mcp add --scope user`). It never runs anything without your confirmation.
- **Extensions → VsMcpDebugger → Copy MCP Setup Command**: puts the two setup commands (install the tool, register it with
  Claude Code) on the clipboard, for people who installed only the extension from the Marketplace.
- The **Log retention (days)** option now also applies to the MCP server's own log files (`mcp-*.log`), not only to the
  extension's logs and the audit trail.
- `debug_start(project)` starts the named project and leaves the solution's startup project as it was.
- `debug_start`, `debug_attach` and `bp_set` return `warnings` when the build is older than the sources; Visual Studio
  offers an optional **Rebuild** bar (hidden while debugging, gone once the project is up to date).

### Fixed
- The out-of-date check no longer compares `deps.json` / `runtimeconfig.json` (incremental builds do not rewrite them) with the
  sources: only the assembly and the symbols count. Found in a real Visual Studio run.
- A refused connection approval or a disconnected AI ends the call instead of being asked twice; a read that waited for an
  answer is refused if another agent took the Visual Studio meanwhile.
- `debug_status` brings the agent's session state in step with Visual Studio after a period in which another agent held it.
- `debug_start(project)` keeps a "Multiple startup projects" setting intact.
- The Visual Studio taskbar button flashes when a consent question appears while Visual Studio is in the background; the status
  bar shows plain text when Visual Studio 2026 hides its custom status bar panel.
- A timed-out `eval` explains the 5-second limit and what to do instead.
- **Set Up AI Agent** never tries to update a running tool (it would fail halfway); it names the running agent sessions.

### Changed
- The settings page moved to **Tools → Options → Debugging → Debugging via MCP** and now starts with an "AI agent setup"
  section (**Set Up AI Agent…** and **Copy setup commands** buttons).
- Wire protocol 2.2 (additive). A 2.1 extension keeps working with the 0.4 tool; the new tools then report
  `unsupported`.
- Break and mode events go only to the agent that holds the Visual Studio.
- `vs_instances` lists availability (`free` / `in_use`), the holder and whether the folder matches.

## 0.3.0 — Complete tool set & IDE state (preview)

### Added
- `threads`, `stack(threadId)`, `frame_select` (with a break epoch: indices from before a resume fail with `stale`),
  `exception`, `bp_enable`, `debug_run_to`, `debug_attach` (Mutate, your own processes only; `debug_stop` detaches),
  `diag`.
- `output_read` (Output window panes; the extension's own panes are never readable), `errors_list` (Error List,
  errors first with totals), `vs_build` (joins a running build).
- `vars`, `eval` and `set_value` report the frame they ran in; consent questions name the selected frame.

### Changed
- Wire protocol 2.1 (additive). A 0.2 extension keeps working with the 0.3 tool; the new tools then report
  `unsupported` and ask to update the extension.

### Fixed
- Queued consent questions re-check the settings and show the time that is left.
- The client name in consent questions is escaped once; a `set_value` Visual Studio rejects is audited as `failed`.
- A failing Activity pane no longer affects the audit file; a connection is registered once even if two handshakes
  race; imported or reset Options take effect immediately.
- After Disconnect AI, the next call reports `ai_disabled` even if the pipe closed before the notification arrived.
- `wrong_mode` raised by the extension reached the agent as a generic SDK error: its wire code (-32003) is reserved by
  StreamJsonRpc. It is now -32014; a 0.2 tool reports the unknown value as `internal` with the message and hint.
- `set_value` and `eval` with side effects are refused if the user moved the instruction pointer to another line while
  the question was open. The Options page is now included in Tools → Import and Export Settings; an imported file can
  never switch Mutate to Allow (it is set to Ask).
- `output_read` and `threads` no longer fail as a whole when Visual Studio cannot give one pane's text or one thread's
  location. New breakpoints are given a moment to bind before `bp_set` and `debug_run_to` judge them.

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
