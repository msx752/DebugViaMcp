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

## Driver

    dotnet run tests/e2e/exception-settings.cs -- <repo>\src\DebugViaMcp.Mcp\bin\Debug\net10.0\DebugViaMcp.Mcp.dll

Needs the repository's solution open in Visual Studio with this extension, and a temporary `samples/FaultDemo/Properties/launchSettings.json`
with `"commandLineArgs":"crash 15"` (see the header of the driver). It covers rows 1, 3, 4, 5, 6, 8, 10 and 14 without answering any question.

## Run record
- 2026-10-23, Visual Studio 2026 (18.x), dev build 0.4.1 (protocol 2.8), Control = Allow: `tests/e2e/exception-settings.cs` 17 of 17 checks passed
  (list, misspelled name with suggestion and no change, set, stop at the caught `ArgumentException`, reset, the unhandled exception still stops,
  `vs_release` puts a forgotten change back). Rows 2, 7, 9 (stopped at a breakpoint), 11, 12, 13 and 15 not run.
