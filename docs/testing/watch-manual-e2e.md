# Manual end-to-end: `watch_add`, `watch_list`, `watch_remove`

Needs a Visual Studio with the extension (protocol 2.9), the sample `samples/OrderDemo` (startup project) and an agent connected.
`tests/e2e/watch.ps1` runs all of it against a real Visual Studio and closes it again (`tests/e2e/watch.cs` is the driver; it answers no question and clicks nothing).
Design: `docs/design/watch-design.md`.

| # | Do | Expect |
|---|---|---|
| 1 | Not debugging: `watch_add` with `sum`, then `line.Quantity` with `name` = `quantity`, `order.Lines.Count`, `nope.missing` | Each answer has an `id` (`w1`...), no value, and the hint that the value comes at the next stop |
| 2 | `watch_add` with `sum` again | `alreadyExisted: true`, the same id |
| 3 | `watch_add` with `a; b` | `invalid_argument`, nothing stored |
| 4 | Breakpoint at `OrderService.cs` line 23, `debug_start` | The stop answer has `watches`: `sum` 0, `quantity` 1, `order.Lines.Count` 2, and `nope.missing` with an `error` and no value; the others still come; nothing is `changed` |
| 5 | `debug_continue` | `sum` is 25 and `changed: true`; `order.Lines.Count` is not changed |
| 6 | `debug_status`, `debug_step` over | Both carry `watches` |
| 7 | `watch_remove` with an id, with an unknown id, with neither id nor `all` | Removed; `not_found` naming the ids that exist; `invalid_argument` |
| 8 | `debug_stop`, wait for Design, `debug_start` | The list is still there and read again |
| 9 | `vs_release`, then `watch_list` | Empty list |
| 10 | `debug_start` with an empty list | No `watches` in the answer and no extra request to the extension |
| 11 | Options: Read = Ask, then `watch_add` | One question naming the expression; later stops ask nothing |
| 12 | Open Debug → Windows → Watch while the agent has entries | The user's Watch windows are unchanged |
| 13 | Same on Visual Studio 2022 17.14 (a .NET 8 project; the repository's solution does not load there) | Same answers |

## Run record

| Date | Visual Studio | Rows | Result |
|---|---|---|---|
| 2026-10-24 | 2026 18.x, extension 0.6.0 dev build (protocol 2.9) | 1-10 by `tests/e2e/watch.ps1` | all 21 checks passed, no process, temp folder or instance file left behind |
