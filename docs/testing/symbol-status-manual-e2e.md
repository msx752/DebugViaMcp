# Manual end-to-end: `symbol_status`

Needs a Visual Studio with the extension, the sample `samples/OrderDemo` and an agent connected. **Not yet run** (written with the feature).
The option names were read from a live Visual Studio 2026 through its automation (Debugging → General); how they behave while debugging was not.

| # | Do | Expect |
|---|---|---|
| 1 | Design mode, a breakpoint set; agent calls `symbol_status` | The options (`requireSourceToMatch`, `justMyCode`, …) match Tools → Options → Debugging → General; the breakpoint is under `unbound`; the note says breakpoints bind when debugging starts |
| 2 | Change **Enable Just My Code** in Options, call again | `justMyCode` follows the checkbox |
| 3 | Start debugging, stop at a breakpoint in `OrderService.Submit`; call again | `unbound` is empty and `notes` is empty |
| 4 | Set a breakpoint on a comment line or a blank line while running | It appears under `unbound` with a bind error; the note points to Debug → Windows → Modules |
| 5 | Same, with **Require source files to exactly match** on | The note names that option |
| 6 | An option Visual Studio does not report | Its field is absent (not `false`) |
| 7 | Agent permissions: Read = Deny | `symbol_status` is refused like the other reads |
| 8 | Another agent holds this Visual Studio | `symbol_status` still works (a read never takes the hold) |

## Run record
(empty)
