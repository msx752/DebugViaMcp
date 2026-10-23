# Manual end-to-end: `process_list`

Needs a Visual Studio with the extension and an agent connected. Start `samples/OrderDemo` outside Visual Studio (`dotnet run`) so there is something to attach to.
**Not yet run** (written with the feature).

| # | Do | Expect |
|---|---|---|
| 1 | Agent calls `process_list` | A list of your processes: `pid`, `name`, ordered by pid; `devenv` and the MCP server are not in it |
| 2 | `process_list` with `filter: "orderdemo"` | Only OrderDemo (any case, part of the name) |
| 3 | `process_list` with `filter: "zzz"` | `data: []` and a hint to try a shorter filter |
| 4 | `process_list` with `max: 5` on a machine with more processes | 5 entries, `total`, `hasMore: true` and a hint to use `filter` |
| 5 | Take a pid from the list, `debug_attach` with that `pid` | The approval question names the process; after Yes the agent is attached |
| 6 | While debugging (Run or Break mode), call `process_list` | Works; it only lists, it does not need Design mode |
| 7 | The approval bar | `process_list` does not ask (it is a read) |

## Run record
(empty)
