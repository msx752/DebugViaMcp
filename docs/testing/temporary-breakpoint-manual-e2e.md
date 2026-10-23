# Manual end-to-end: `bp_set` with `temporary`

Needs a Visual Studio with the extension, the sample `samples/OrderDemo` and an agent connected. **Not yet run** (written with the feature).

| # | Do | Expect |
|---|---|---|
| 1 | Agent calls `bp_set` with `temporary: true` on `OrderService.cs` line 9, then `bp_list` | The breakpoint is listed with `temporary: true`; Visual Studio's Breakpoints window shows it as a normal breakpoint |
| 2 | Start debugging; it stops at line 9 | The break reports `reason: breakpoint` with that breakpoint's id |
| 3 | `bp_list` after the stop | The breakpoint is gone; the Breakpoints window no longer has it |
| 4 | `debug_continue` | The program does not stop at that line again |
| 5 | A temporary breakpoint that is never reached; stop debugging | It is still there, still `temporary: true` (it waits for its first break) |
| 6 | Set an ordinary breakpoint on line 9, then `bp_set` the same line with `temporary: true` | `alreadyExisted: true` and a hint that it was not made temporary; after a stop the breakpoint is still there |
| 7 | `bp_set` with `temporary: true` and `logMessage` | `invalid_argument`: a tracepoint never stops |
| 8 | A temporary breakpoint with a condition that is false first, then true | Stops (and is removed) only when the condition is true |
| 9 | Remove a temporary breakpoint by hand with `bp_remove`, set the same line again ordinarily | The new one is an ordinary breakpoint and stays after its stop |

## Run record
(empty)
