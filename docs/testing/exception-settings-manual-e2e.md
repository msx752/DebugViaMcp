# Manual end-to-end: `exception_list`, `exception_set`, `exception_reset`

Needs a Visual Studio with the extension, the sample `samples/OrderDemo` and an agent connected. **Not yet run** (written with the feature).
What was tried with Visual Studio's automation alone (spike): reading and writing Break When Thrown in design mode and while an attached
program runs, an unknown name (`0x8002000B`), and that a change did not survive closing and reopening Visual Studio (`DTE.Quit()` and closing
the main window).

| # | Do | Expect |
|---|---|---|
| 1 | Agent calls `exception_list` (no arguments) | Only exceptions with Break When Thrown on, matching the checked rows of Debug → Windows → Exception Settings; `total` and no `truncated` |
| 2 | `exception_list` with `group` = `Common Language Runtime Exceptions`, `onlyEnabled` = false, `limit` = 5 | 5 entries, `truncated: true` and a hint to narrow it |
| 3 | `exception_list` with `filter` = `nullref` | `System.NullReferenceException` (case-insensitive) |
| 4 | `exception_set` with that name, `breakWhenThrown` true | The row in the Exception Settings window is checked at once; the answer shows `previous: false`, `changed: true` and a hint to call `exception_reset` |
| 5 | Same call again | `changed: false`, no hint |
| 6 | `exception_set` with one real and one misspelled name | An error naming the misspelled one with close names; the real one did not change |
| 7 | `exception_set` with a name without its namespace (`TimeoutException`) | An error that offers `System.TimeoutException` |
| 8 | Debug `OrderDemo` and let it throw the exception set in 4 | The debugger stops at the throw, not at the catch; `exception` describes it |
| 9 | While the program is running (F5), then again stopped at a breakpoint, `exception_set` another exception | Works in both; running was checked by script (attached program), the stopped case was not |
| 10 | `exception_reset` | Only the exceptions the agent changed return to their first value; a row you changed yourself in between stays |
| 11 | Options: Control = Ask, then `exception_set` | The question names the group and the exceptions; Deny leaves everything unchanged |
| 12 | Options: Control = Deny | `exception_set` and `exception_reset` are refused; `exception_list` (Read) still works |
| 13 | Another agent holds this Visual Studio | `exception_list` and `exception_set` answer `in_use` (a read never takes the hold, but it is refused while another agent has it) |
| 14 | The agent changes an exception, then disconnects (or calls `vs_release`) without calling `exception_reset` | The exception is back at its first value in the Exception Settings window at once; the Output pane "Debugging via MCP Log" shows no error |
| 15 | Old extension (protocol 2.7) with the new server | The three tools answer `unsupported` and say to update the extension |

## Automated run

    powershell -File scripts\reinstall-vsix.ps1 -Build        # the build under test must be the installed one
    powershell -File tests\e2e\exception-settings.ps1         # phases allow, deny, ask (add -Phases allow for one)

It closes nothing of yours: it refuses to run while a Visual Studio is open, starts its own (minimized, real profile) for each phase,
changes the permission values in Visual Studio's `settings.json` and the sample's `launchSettings.json` for the run, and puts both back
whatever happens. It answers no question and clicks nothing. `tests/e2e/exception-settings.cs` is the driver it runs.
It covers rows 1, 3, 4, 5, 6, 8, 9, 10, 11 (the unanswered question), 12, 13 and 14. Not covered by it: row 2 and 7 (cosmetic), the
question being **answered** (Allow once / Deny in the info bar, which needs UI automation), and row 15 (an older extension: the
`RequireMinor` fallback is unit-tested; no old build is kept to install).

## Run record
- 2026-10-23, Visual Studio 2026 (18.x), dev build 0.4.1 (protocol 2.8): `tests\e2e\exception-settings.ps1`, all three phases passed.
  allow 23 checks (list, misspelled name with a suggestion and no change, set, a second agent gets `in_use` for a read and a set,
  stop at the caught `ArgumentException`, a set while stopped at a break, reset of both, an empty reset, the unhandled exception,
  `vs_release` puts a forgotten change back and lets the second agent in), deny 5 (policy denial, nothing changed, empty reset still works),
  ask 4 (the unanswered question denies after 15 s with `reason: timeout`, nothing changed, an empty reset asks nothing).
- 2026-10-23, same build after reading only what a request needs (a group is enumerated once; set and reset look up only their names): all four phases
  passed. `perf` (`-Phases perf`, 2000 cycles): `debug_status` p50 1.2 ms; `exception_list` p50 121 ms / p95 216 ms; with a filter 68 / 125 ms;
  `exception_set` 61 / 106 ms; `exception_reset` 60 / 100 ms. Visual Studio's private memory rose to 1480 MB and settled near 1370 MB (garbage, not a leak).
  Looking entries up one by one by name for a whole group was tried first and took 12 s per list: a lookup scans the group.

