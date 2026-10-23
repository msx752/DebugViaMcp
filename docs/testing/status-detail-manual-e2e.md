# Manual end-to-end: `debug_status` with `detail`

Needs a Visual Studio with the extension, the sample `samples/OrderDemo` and an agent connected. **Not yet run** (written with the feature).

| # | Do | Expect |
|---|---|---|
| 1 | Break in `OrderService.Calculate`; agent calls `debug_status` | As before: mode, permissions, snapshot; no `detail` |
| 2 | Same break; `debug_status` with `detail: true` | `detail.stack` (top frames, `Calculate` first), `detail.locals` (`sum`, …), no `detail.exception` |
| 3 | Stop on an exception (throw one in the sample); `detail: true` | `detail.exception` holds the type and message |
| 4 | Stop in a method with more than 30 locals | 30 locals and `localsHasMore: true` |
| 5 | Run (not stopped) or Design mode; `detail: true` | Mode only; no `detail`, and no error |
| 6 | Mask on, a local named `password` | Its value reads `[masked]` inside `detail.locals` |
| 7 | One call instead of `debug_status` + `stack` + `vars` + `exception` | The agent gets the same facts in one round trip |

## Run record
(empty)
