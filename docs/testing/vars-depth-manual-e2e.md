# Manual end-to-end: `vars` with `depth` and `expand`

Needs a Visual Studio with the extension, the sample `samples/OrderDemo` and an agent connected. Break in `OrderService.Submit`
(`order` has `Lines`). **Not yet run** (written with the feature).

| # | Do | Expect |
|---|---|---|
| 1 | Agent calls `vars` | One level, as before; no `children` |
| 2 | `vars` with `depth: 2` | `order` carries `children`: `Id`, `Lines`, … ; `Lines` has none (one level further is the next depth) |
| 3 | `vars` with `depth: 3` | `Lines` carries `children`: `[0]`, `[1]`, … (the elements of the list) |
| 4 | `vars` with `depth: 4` | An element carries its members (`Sku`, `Quantity`, …) |
| 5 | `vars` with `depth: 9` | Treated as 4; no error |
| 6 | `vars` with `path: "order"` and `depth: 2` | The members of `order`, and under `Lines` its elements |
| 7 | `vars` with `expand: ["order.Lines", "order.Nope"]` | `expanded` has the elements of `order.Lines` and, for the wrong path, an `error` entry; the other entries are unaffected |
| 8 | Stop in a method with a large object (for example a `DbContext`) and call `vars` with `depth: 4` | The answer stays small; some members stay closed, `hint` says so, and `hasChildren` is still true on them |
| 9 | `vars` with `depth: 2` on an object with more than 20 members | 20 members and `childrenTruncated: true` |
| 10 | A `password` string field inside an object, `vars` with `depth: 2` | The field reads `[masked]` with `masked: true` at its depth |

## Run record
(empty)
