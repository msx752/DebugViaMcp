# M2 end-to-end test

The M2 check drives the real `vsmcpdebugger` MCP server over stdio — exactly as Claude Code does — against a running
Visual Studio with the VsMcpDebugger extension, and answers the consent info bars through UI Automation.

## Prerequisites

1. Build: `powershell -File build.ps1`, or build `src/VsMcpDebugger.Vsix` with Visual Studio's MSBuild and
   `-nodeReuse:false` (add `-p:DeployExtension=false` when the shell's user profile is redirected).
2. Install `src/VsMcpDebugger.Vsix/bin/Debug/net48/VsMcpDebugger.Vsix.vsix` into Visual Studio 2026
   (`VSIXInstaller.exe /quiet /instanceIds:<id> <vsix>`; uninstall an older build of the same version first with
   `/uninstall:VsMcpDebugger.Vsix.6f1d2c3a-9b5e-4c7a-8e21-3f4b5a6c7d8e`).
3. Open `samples/OrderDemo/OrderDemo.csproj` and make **OrderDemo** the startup project.
4. Output → **VsMcpDebugger** shows `VsMcpDebugger 0.1.0 ready (pid …, protocol 2.0)`, and Output →
   **VsMcpDebugger Activity** exists.

## Automated driver

    dotnet run scripts/e2e/m2-e2e.cs -- <repo>\src\VsMcpDebugger.Mcp\bin\Debug\net10.0\VsMcpDebugger.Mcp.dll

It clicks the info bars with `scripts/e2e/click-infobar.ps1` and runs Disconnect AI / Reconnect AI through
Visual Studio's automation object.

## With Claude Code

    claude mcp add vsmcpdebugger -- dotnet "<repo>/src/VsMcpDebugger.Mcp/bin/Debug/net10.0/VsMcpDebugger.Mcp.dll" serve

| # | Prompt to Claude | Expected |
|---|---|---|
| 1 | "Show debugger status." | `permissions` with mutate = ask |
| 2 | "Break on OrderService.cs line 8 when order.Id == 7 and start debugging." | info bar for the condition; Allow for session → break at line 8 |
| 3 | "Set total to 0." | info bar in VS; Allow once → value 0 |
| 4 | "Set total to 5." → click Deny | Claude reports that you denied it and does not retry |
| 5 | "Call order.Touch() for real, twice." → Allow for session | 1, then 2 without a second info bar |
| 6 | Tools → Options → VsMcpDebugger → Mutate = Deny; "Set total to 3." | denied by settings, no info bar |
| 7 | Ctrl+Alt+Shift+F12; "Show debugger status." | `ai_disabled` with the Reconnect AI hint |
| 8 | Extensions → VsMcpDebugger → Reconnect AI; "Show debugger status." | works again |
| 9 | "Continue and stop." | the session ends |

## Logs

- `%LOCALAPPDATA%\VsMcpDebugger\logs\audit-*.log` — one JSON line per Control/Mutate call, with `callId`
- `%LOCALAPPDATA%\VsMcpDebugger\logs\mcp-*.log` — one entry per tool call, same `callId`, no variable values
- `%LOCALAPPDATA%\VsMcpDebugger\logs\vsix-*.log` — connections, breaks, mode changes
- Visual Studio Output → **VsMcpDebugger** (technical log) and **VsMcpDebugger Activity** (audit lines)

On Windows `%LOCALAPPDATA%` means the user's real profile, even when the shell that starts `vsmcpdebugger`
redirects `USERPROFILE`/`LOCALAPPDATA`.

## Run 2026-10-11 — Visual Studio Community 2026 (18.10), automated driver

Extension log: `VsMcpDebugger 0.1.0 ready (pid 70676, protocol 2.0)`, no warnings (status bar indicator attached).

| Step | Result |
|---|---|
| `debug_status` | `mode: design`, `permissions: {read: allow, control: allow, mutate: ask}` |
| `bp_set` line 8, `order.Id == 7` → Allow for session | breakpoint `bp-2604a49c`, pending bind |
| `debug_start` | `state: break` at OrderService.cs:8, args under `data` |
| `set_value total = 0` → Allow once | `data.value = "0"`; `eval total` → `0` |
| `set_value total = 5` → Deny | `denied` / `user_denied`, hint "do not retry; ask the user" |
| `eval order.Touch()` with side effects → Allow for session | `1`, then `2` with no second info bar |
| `set_value GetType().Name` | `invalid_argument`, no info bar |
| `set_value total = 1`, no answer | `denied` / `timeout` after 60 s |
| Disconnect AI → `debug_status` | `ai_disabled` |
| Reconnect AI → `debug_status` | reconnected, `mode: break` |
| `debug_continue`, `debug_stop`, `bp_remove all` | session ended, 1 breakpoint removed |

Audit log: one line per Control/Mutate call with matching `callId`s in `mcp-*.log` (e.g. `c-000010`–`c-000012`);
client `m2-e2e`; decisions `allow`, `allowOnce`, `allowSession`, `denied:user`, `denied:timeout`; connect,
disconnect, `ai_disconnect`, `ai_reconnect` recorded. The debuggee value `1750` appears in no log.
