# M3 end-to-end test

Same setup as [m2-manual-e2e.md](m2-manual-e2e.md) (build, install the 0.3.0 VSIX with the real profile
environment, open `samples/OrderDemo/OrderDemo.csproj`, OrderDemo = startup project). The Output pane shows
`Debugging via MCP 0.3.0 ready (…, protocol 2.1)`.

## Automated driver

    dotnet run tests/e2e/full-tool-set.cs -- <repo>\src\DebugViaMcp.Mcp\bin\Debug\net10.0\DebugViaMcp.Mcp.dll

It builds and starts `samples/FaultDemo` for the attach and exception steps and answers info bars with
`tests/e2e/click-infobar.ps1`.

## Manual checks

| # | Action | Expected |
|---|---|---|
| 1 | `debug_attach` to FaultDemo | info bar: "… attach the debugger to FaultDemo (pid N) — Visual Studio will be able to read and change that process's memory" |
| 2 | `bp_enable` true on a conditional breakpoint | info bar: "… enable the breakpoint at … when … — this runs in your program on every hit" |
| 3 | `output_read` pane "Debugging via MCP Activity" | `denied` / `policy` |
| 4 | Tools → Import and Export Settings: import a file with Mutate = Deny | the next `set_value` is denied by settings without restarting; Activity shows "Imported settings: Mutate: Ask → Deny" |
| 5 | A `set_value` whose expression is 500 characters long | the info bar wraps or truncates visibly; Allow/Deny buttons stay reachable |
| 6 | Click another frame in the Call Stack window while a `set_value` question is open, then Allow | `wrong_mode` "program state changed", nothing assigned |

## Run record

### Run 2026-10-13 — Visual Studio Community 2026 (18.0), automated driver

Extension log: `Debugging via MCP 0.3.0 ready (pid 61796, protocol 2.1)`. VS had the whole `DebugViaMcp.slnx` open
(OrderDemo = startup project); the driver builds only `OrderDemo` and runs a private copy of the MCP server, because
`vs_build` of the whole solution also builds the MCP project and a running server locks its own output files.

| Step | Result |
|---|---|
| `vs_build project=OrderDemo` | `state: done`, `succeeded: true` (0.5 s, up to date) |
| `errors_list` | totals 0 / 0 / 0 |
| `output_read` (no pane) | 11 panes, none of the Debugging via MCP ones; panes without a text document listed without `lines` |
| `output_read pane=build` | the 3 build lines |
| `output_read pane="Debugging via MCP Activity"` | `denied` / `policy` |
| `bp_set` line 23, `debug_start` | break in `Calculate`, `epoch: 1` |
| `threads`, `stack` | 7 threads with locations (native threads without one are still listed); 3 frames |
| `frame_select 1`, `vars args`, `eval order.Id` | `Submit`; each answer carries `frame: {index: 1, threadId}`; `1` |
| `debug_run_to` line 14 | `reason: runTo` at line 14 |
| `frame_select 0` with the old epoch | `stale` |
| `bp_list` | empty (the run-to breakpoint is never listed) |
| `bp_set` with condition → Allow once | `bound: true` |
| `bp_enable false` | no info bar |
| `bp_enable true` → Allow once | info bar shown, enabled |
| `debug_continue` | break at line 8 (`order.Id == 9`), `epoch: 3` |
| `debug_attach FaultDemo` → Allow once | `attached`; `bp_set function`, `debug_wait` → break in `Ticker.Work` |
| `debug_stop` | detached; "FaultDemo still running after detach: True" |
| `debug_attach` to `crash 12`, `debug_wait` | `reason: exception` |
| `exception` | `InvalidOperationException` "FaultDemo crashed on purpose", inner `ArgumentException` |
| `diag tail=5` | 5 log lines |

Audit log: one line per Control/Mutate call with matching `callId`s in `mcp-*.log`; client `m3-e2e`; decisions `allow`,
`allowOnce`; `debug_attach` recorded with the pid. The strings `crashed on purpose`, `is out of range` and `1750` appear
in no log.

Findings of the first run, fixed before this one (each with a test):

- `output_read` and `threads` failed completely on one unreadable pane text / thread location (E_FAIL).
- A breakpoint added while debugging was reported as not bound, and `debug_run_to` as "no executable code", because
  Visual Studio binds a moment after it adds.
- `debug_wait` after `debug_attach` returned "session ended": attach did not move the wait point past the previous
  session's end.

Deferred to M3.1 (see the ledger): HRESULT mapping and mode re-check at the Visual Studio boundary, event-based
settling instead of polling, a deadline for commands that block in a modal dialog.

### Manual checks 1–6 and the instruction pointer (2026-10-17)

| # | Result |
|---|---|
| 1 | `debug_attach`: info bar answered by the driver (the audit line shows `allowOnce`) |
| 2 | `bp_enable true`: info bar answered by the driver |
| 3 | Activity pane: `denied` / `policy` |
| 4 | Import and Export Settings: the page is listed (`ProvideProfile` was missing before this run and was added). Importing a file with Mutate = Deny took effect at once (audit: `Imported settings: Mutate: Allow → Deny`). A file with Mutate = Allow left Mutate at Ask (audit: `Imported settings (Mutate = Allow ignored; set it in Options): Mutate: Deny → Ask`). The unrelated errors VS lists for the JavaScript/TypeScript categories of the exported file are Visual Studio's own. |
| 5 | A 497-character value: the info bar showed Allow once / Allow for session / Deny; Deny gave `user_denied`, nothing assigned |
| 6 | `tests/e2e/consent-check.ps1 -Scenario frame` (moves the selected frame through DTE while the question is open, then Allow once): frame `Calculate` → `Submit`, audit `allowOnce` + `wrong_mode`, nothing assigned; the agent receives a structured `wrong_mode` |
| 7 | `-Scenario ip` (caret to line 21, Debug > Set Next Statement, then Allow once): audit `allowOnce` + `wrong_mode`, `sum` still 0 |

Findings of this part, fixed with a test each: the extension's `wrong_mode` reached the agent as a generic SDK error because its
wire code (-32003) is reserved by StreamJsonRpc (now -32014; pinned for every code the extension sends); the Options page was not
registered for Import and Export Settings; an imported file could have granted Mutate = Allow without the Options confirmation.

Persistence (real Visual Studio, restart): a file with Read = Deny and Mutate = Allow was imported with
`Tools.ImportandExportSettings /import:` (through DTE). Read became Deny, Mutate stayed Ask. After closing and starting Visual
Studio again, importing the original settings reported `Read: Deny → Allow` and no Mutate change, so Read = Deny had been
stored and Mutate had been stored as Ask, not Allow.
