# M1 end-to-end test

The M1 end-to-end check drives the real `vsdebug` MCP server over stdio — exactly as Claude Code does — against a
running Visual Studio with the VsDebug extension.

## Prerequisites

1. Build: `powershell -File build.ps1` (or build `src/VsDebug.Vsix` with Visual Studio's MSBuild and `-nodeReuse:false`).
2. Install `src/VsDebug.Vsix/bin/Debug/net48/VsDebug.Vsix.vsix` into Visual Studio 2026 (double-click, or
   `VSIXInstaller.exe /quiet /instanceIds:<id> <vsix>`). Remove it later via Extensions → Manage Extensions.
3. Open `samples/OrderDemo/OrderDemo.csproj` in Visual Studio and make **OrderDemo** the startup project.
4. Check the Output window → **VsDebug** pane shows `VsDebug 0.1.0 ready (pid …, protocol 1.0)`.

## Automated driver

    dotnet run scripts/e2e/m1-e2e.cs -- <repo>\src\VsDebug.Mcp\bin\Debug\net10.0\VsDebug.Mcp.dll

## With Claude Code

    claude mcp add vsdebug -- dotnet "<repo>/src/VsDebug.Mcp/bin/Debug/net10.0/VsDebug.Mcp.dll" serve

| # | Prompt to Claude | Expected |
|---|---|---|
| 1 | "List Visual Studio instances." | `vs_instances` shows the instance and its solution |
| 2 | "Break in OrderService.Submit on the `var total` line when order.Id == 7." | `bp_set` returns an id; `bound: false`, "Pending" in design mode |
| 3 | "Start debugging." | `debug_start` → `state: break` at OrderService.cs:7 with `order` in `args` |
| 4 | "Step over and show me total." | `debug_step over` → line 8; `eval total` = 1750 |
| 5 | "Show order.Lines." | `vars path=order.Lines` lists 4 items |
| 6 | "Evaluate order.Touch(), then order.TouchCount." | Touch returns 1, TouchCount stays 0 (side effects discarded) |
| 7 | "Evaluate order.Note." | value ≤ 1024 chars, `truncated: true` |
| 8 | "Set a breakpoint with condition order.Touch() > 0." | `invalid_argument` (conditions must not have side effects) |
| 9 | "Continue, then stop and remove all breakpoints." | `state: design` once the program ends; breakpoints removed |
| 10 | Close Visual Studio, then "Show debugger status." | `not_connected` with a hint, no hang |

## Logs

- `%LOCALAPPDATA%\VsDebug\logs\mcp-*.log` — one entry per tool call, no variable values
- `%LOCALAPPDATA%\VsDebug\logs\vsix-*.log` — connections, breaks, mode changes
- Visual Studio Output → **VsDebug** pane — same as the vsix log

On Windows `%LOCALAPPDATA%` here means the user's real profile (`C:\Users\<user>\AppData\Local`), even when the shell
that starts `vsdebug` redirects `USERPROFILE`/`LOCALAPPDATA`.

## Run 2026-10-11 — Visual Studio Community 2026 (18.10), automated driver

| Step | Result |
|---|---|
| vs_instances / debug_status | PASS — one instance, mode `design` |
| bp_set with `order.Id == 7` | PASS — `bp-cf8f57ae`, pending bind |
| bp_set with `order.Touch() > 0` | PASS — rejected with `invalid_argument` |
| debug_start | PASS — `state: break`, reason `breakpoint`, OrderService.cs:7, source context, `order` arg, stack top |
| debug_step over | PASS — line 8 |
| eval total | PASS — `1750` (order 7) |
| vars order.Lines | PASS — 4 lines with SKU/quantity/price |
| eval order.Touch() then order.TouchCount | PASS — `1`, then `0` (no mutation) |
| eval order.Note | PASS — truncated to 1024 chars |
| stack | PASS — Submit:8, Program.Main:9 |
| debug_continue | PASS — program ran to completion → `design` |
| debug_stop / bp_remove all / debug_status | PASS |
| debug_wait right after a break-returning debug_start | Waits for the *next* break and returns `running` after the timeout — by design (see plan, M3 note) |

Deviations found by the first run (fixed before this run): handshake read DTE state off the UI thread; internal
handshake failures were reported as `unauthorized`. Cosmetic: breakpoints without a hit count report `hitCount: 1`.
