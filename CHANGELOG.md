# Changelog

All notable changes to Debugging via MCP. Versions follow [Semantic Versioning](https://semver.org/); the extension and
the `debugviamcp` tool are released together with the same version.

## Unreleased

### Changed
- The Output panes are named by what they hold: **Debugging via MCP Log** (the technical log, formerly *Debugging via MCP*)
  and **Debugging via MCP Audit** (what the agent did, formerly *Debugging via MCP Activity*). The menu entry **Show Activity**
  is now **Show Audit**. The agent still cannot read either pane.

## 0.4.1 — First pipeline build (preview)

### Changed
- No change in behaviour. This is the first version built and tagged by the release pipeline: the VSIX on the Marketplace is
  the one attached to the GitHub Release, with the same SHA-256.
- The Marketplace page's release-notes link, tags and author name are fixed in the package.
- The NuGet tool package is gone; the MCP server travels inside the extension only.

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
- The extension carries the MCP server. When Visual Studio starts it copies the server to
  `%LOCALAPPDATA%\DebugViaMcp\bin\<build>` and writes a launcher, `%LOCALAPPDATA%\DebugViaMcp\debugviamcp.cmd`, whose path
  never changes. There is no separate tool to install (the server needs the .NET 10 runtime), updates do not touch a
  server an agent is still running, and uninstalling the extension is never blocked by a running agent.
- **Extensions → Debugging via MCP → Set Up AI Agent…** opens a window: it checks the .NET runtime and the installed
  server, lists AI clients (Claude Code, Codex, Cursor, another MCP client) and, for the one you pick, shows the command or
  configuration to copy. It runs nothing and changes no client's settings. The separate *Copy MCP Setup Command* is gone.
- Visual Studio only: Visual Studio Code is not supported, and the documentation says so.
- `scripts\reinstall-vsix.ps1` stops a stuck VSIXInstaller after `-InstallerTimeoutSeconds` (default 180) instead of
  hanging, and refuses to start while another VSIXInstaller runs.
- The **Log retention (days)** option now also applies to the MCP server's own log files (`mcp-*.log`), not only to the
  extension's logs and the audit trail.
- `debug_start(project)` starts the named project and leaves the solution's startup project as it was.
- `debug_start`, `debug_attach` and `bp_set` return `warnings` when the build is older than the sources; Visual Studio
  offers an optional **Build** bar (hidden while debugging, gone once the project is up to date).

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
- The out-of-date bar says "Debugging via MCP: … is out of date: the built program does not match the sources. Build it now?" and its
  button runs a normal incremental **Build** of that project (it used to clean and rebuild, which a stale output does not need).
- The consent question bar also starts with "Debugging via MCP:", so both bars show which extension is asking.
- No out-of-date warning and no Build bar while nothing is running and Visual Studio builds by itself as debugging starts (its option
  **On Run, when projects are out of date** = *Always build*, the default): it would be a false alarm. They stay when a program is running
  (it keeps its old binary) or when that option is *Never build* / *Prompt to build*.
- The product is now called **Debugging via MCP**. The MCP server (command `debugviamcp`) travels inside the extension, which
  shows as "Debugging via MCP", and the data folder is `%LOCALAPPDATA%\DebugViaMcp`.
- The settings moved to Visual Studio's settings experience: **Tools → Options → All Settings → Debugging → Debugging via
  MCP**. The setup window stays in **Extensions → Debugging via MCP** (**Set Up AI Agent…**).
- Settings apply as soon as they change. A stored **Mutate = Allow** counts only after you confirm it in a dialog (also at
  startup); until then Visual Studio keeps asking, so a value written to the settings file never silently lets the agent
  change program state.
- Two new settings cap how long an agent may keep Visual Studio waiting: **Longest wait for a break** (`debug_start`, `debug_continue`,
  `debug_step`, `debug_run_to`, `debug_wait`; 5–600 s, default 120) and **Longest wait for a build** (`vs_build`; 30–1800 s, default 600).
  The agent may ask for less, never for more; a longer build keeps running and the agent can ask again. Together with the existing
  **Consent timeout** they are in the settings page of Visual Studio.
- Wire protocol 2.3 (additive): the handshake carries the two limits. Older extensions or servers keep the built-in 120 s / 600 s.
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
- Permission tiers (Read / Control / Mutate) in **Tools → Options → Debugging via MCP**, enforced inside Visual Studio.
- Consent info bar for Mutate operations — **Allow once / Allow for session / Deny**; no answer within the timeout
  (default 60 s) means deny. Breakpoint conditions and tracepoints count as Mutate because they run on every hit.
- Audit log (`%LOCALAPPDATA%\DebugViaMcp\logs\audit-*.log`) and the **Debugging via MCP Activity** Output pane.
- `callId` on every call, shown in both the MCP server and Visual Studio logs.
- **Extensions → Debugging via MCP** menu: Disconnect AI (Ctrl+Alt+Shift+F12), Reconnect AI, Show Activity, Open Logs
  Folder, Options. Status bar indicator showing the connected client.
- `debug_status` reports the active permissions; snapshot arguments are returned under `data`.
- The MCP log level follows the Visual Studio setting (`DEBUGVIAMCP_LOG_LEVEL` still overrides it).

### Changed
- Wire protocol 2.0 (every request carries a call context; errors carry a hint and a reason). The extension and the
  tool must both be 0.2.0.
- The dotnet tool package is now called `DebugViaMcp`.

### Security
- Consent text is shown in full with invisible and misleading characters escaped; mutate texts are limited to
  500 characters; Allow clicks in the first second after the bar appears are ignored.
- Disconnect AI refuses everything on existing connections immediately and closes them.
- See [docs/security.md](docs/security.md).

## 0.1.0 — Walking skeleton

- Breakpoints (file/line, function, conditions, hit counts, tracepoints), start/continue/pause/step/wait,
  call stack, variables with drill-down, side-effect-free evaluation.
- Per-user named pipe with token handshake; logs in `%LOCALAPPDATA%\DebugViaMcp\logs`.
