# Watch (F10) — design

Roadmap item F10 in `vs-feature-roadmap.md`. Status: approved and implemented on `develop`; not part of 0.6.0, it ships with the next release.

## Why
`eval` answers one question about one moment. An agent that steps through code asks the same few questions at every stop
("what are `order.Total` and `i` now?") and today pays one call per question per stop. A watch list returns the values with
the stop itself and shows what changed since the last stop, the way the Watch window does for a person.

## Goals
- The agent keeps a short list of expressions; every answer that reports a stop carries their values.
- A value says whether it changed since the previous stop.
- Nothing is left in the user's Visual Studio: no entries in the user's Watch windows, no pinned DataTips, nothing in the solution's user options.

## Non-goals (first release)
- Reading the user's own Watch windows or pinned DataTips, and adding to them. See "Phase 2 (spike)".
- Watches that run code with side effects (same rule as `eval`: side-effect free only).
- Conditions ("stop when this watch changes"), data breakpoints.
- A list that survives the agent: nothing is stored on disk.

## How it works
- **Whose list.** A watch list belongs to a connection (one agent), is kept in memory, and is dropped when that connection's hold on
  Visual Studio ends (`vs_release`, the connection closes, Disconnect AI), like the exception-settings record. A Visual Studio instance
  has one solution, so the list is per solution without any extra work; agents of other instances never see it.
  *My call (the roadmap says "lives for the debugging session"):* the list survives `debug_stop` and the next `debug_start` of the same
  connection, because an agent restarts the program often and would otherwise re-add the same list each time. It ends with the agent, not with the program.
- **Who adds the values to an answer: the MCP host, by one request.** A stop answer (`debug_start`, `debug_continue`, `debug_step`, `debug_run_to`,
  `debug_wait`, `debug_attach`) is built in the host from the break event, and the event is broadcast to every connection, so it cannot hold one
  agent's list. After shaping a stop (and in `debug_status` in Break mode) the host adds `watches` by calling `ListWatches` on the extension, **only when it
  knows its list is not empty** (it counts what `watch_add` and `watch_remove` returned; a new connection starts at zero). With an empty list
  nothing extra is sent. If the program has already resumed when the request arrives, the extension returns the entries without values and the host
  leaves `watches` out of the answer.
- **Frame.** The currently selected frame (the one `eval` uses, `frame` selects), on the UI thread, in one hop with the snapshot.
- **Cost.** With an empty list nothing is evaluated and nothing is added. With a list the cost is one evaluation per entry per answer.
  The first spike measures it (target: a list of 10 under 50 ms in a typical program); if it is higher the limit goes down, not the check.
- **`changed`.** Each entry keeps the text of its previous value (only that string, truncated) per connection; `changed` is true when the new text differs.
  The first value after adding is `changed: false`. A stop in another frame or function compares the same way (it is the same expression, the value is what it is).

## Tools
| Tool | Tier | Arguments | Answer |
|---|---|---|---|
| `watch_add` | Read | `expression`, `name?` (a label for the answer) | the entry (`id`, `expression`, `name`) and, in Break mode, its value now; `alreadyExisted` when the same expression is on the list |
| `watch_list` | Read | none | every entry with its value now (Break mode) or without value (other modes) |
| `watch_remove` | Read | `id` or `all: true` | what was removed; unknown id is an error naming the ids that exist |

A value in an answer: `id`, `name`, `expression`, `value`, `type`, `changed`, or `error` (one entry failing never fails the call; typical: "not in scope in this frame").

Rules:
- Same expression policy as `eval` without side effects (`ExpressionPolicy.PrepareSideEffectFree`), applied when the entry is **added**, so a rejected
  expression is refused at once and never stored. The rule lives in one place for `eval`, breakpoint conditions and watches.
- At most 10 entries, expression at most 500 characters (like `set_value`). Adding beyond the limit is an error that says to remove one.
- Adding is allowed in any mode (Design, Run, Break); the value appears at the next stop.
- Values obey the existing masking, truncation (200 characters per value) and the untrusted-text note of `eval`; secrets are masked before they leave Visual Studio.
- Why Read and not Control: a watch evaluates what `eval` already evaluates for Read, and changes nothing in Visual Studio. Tier consent texts and the audit line name the expression ("watch: order.Total"). The user who sets Read = Ask is asked once per `watch_add`, not at every stop.
- Property getters and similar code still run when an expression is evaluated (as in the Watch window); this is the same exposure `eval` has and is stated in `docs/security.md`.

## Protocol
- Protocol 2.9, `ProtocolInfo.MinorWatch = 9`. Additive: new requests `AddWatch`, `ListWatches`, `RemoveWatch` and their DTOs (`WatchValue`, `AddWatchResult`,
  `WatchListResult`, `RemoveWatchResult`); no existing DTO changes, `watches` in a stop answer is added by the host, not carried by the break event. Golden-JSON tests for the new DTOs.
- Compatibility rule as in `exception-settings-design.md`: with an extension below 2.9 the three tools answer the existing "update the extension" error
  (`RequireMinor`), and answers from an older extension simply have no `watches`. Tests: a minor-8 extension (fake) receives no watch requests.
- The list lives in the extension (the connection handler), not in the MCP host: the release on disconnect must work when the host is gone. The host keeps only a count.

## Visual Studio side
- Evaluation uses the same `IDteFacade.Evaluate` path as `eval`; no new automation call in the first release.
- Memory: the record per connection is a small list of entries and one previous-value string each; weakly keyed by connection like the exception record, so a connection that disappears leaves nothing.

## Phase 2 (spike, separate step, not in "Done when")
A person's own Watch windows and pinned DataTips are stored per solution in the user's solution options (`.suo`). Unknown, to be tried before any promise:
1. Can automation **read** the entries of Watch 1-4 or the pinned DataTips? (If not, this stays out.)
2. Does `Debug.AddWatch` (via `DTE.ExecuteCommand`) add an entry, and can the same entry be removed again? Without removal the agent would leave entries in the user's `.suo`, so adding stays off unless removal works.
The spike writes down what it found in `docs/design/` before any tool is added.

## Done when
`watch_add`, `watch_list`, `watch_remove` work against a real Visual Studio (manual list in `docs/testing/`, on 2026 and 2022 17.14); unit tests cover: add/duplicate/limit/refused expression,
remove one and all, values with and without Break mode, `changed`, per-entry error, the list ending with release/close/disconnect, survival across `debug_stop` and `debug_start`,
masking, no `ListWatches` request when the host's list is empty, `watches` left out when the program resumed meanwhile, no evaluation with an empty list, minor-8 fallback, golden-JSON of the DTOs. README tool list, `docs/security.md` and the Marketplace overview links updated, CHANGELOG entry at release time.

## Left to the builder
Exact JSON field names (follow the neighbouring DTOs), the `id` format (like breakpoint ids: short and stable inside the connection), the wording of the consent and audit lines.
