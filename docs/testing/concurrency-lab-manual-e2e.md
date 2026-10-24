# Manual end-to-end: the concurrency lab

Needs a Visual Studio with the extension (protocol 2.9) and the repository's solution (`samples/ConcurrencyLab`). `tests/e2e/watch.ps1 -Driver concurrency-lab`
runs all of it against a real Visual Studio and closes it again (`tests/e2e/concurrency-lab.cs`; no question is answered, nothing is clicked, `scenario.txt` is
removed). Design: `docs/design/concurrency-lab-design.md`.

| # | Scenario | The driver does | Expect |
|---|---|---|---|
| 1 | race | Breakpoints on the line after the check (`both workers have checked`) and after the join; `watch_add` of `order.Status` and `order.ChargeCount`; `debug_start`, `debug_continue` twice | Two stops on two different threads, both with `Pending` and `0`; after the join `ChargeCount` is 2 |
| 2 | deadlock | `debug_start` (returns `running`), `debug_pause`, `threads`, `stack` of every thread | Two threads inside `Transfer`; their stack lines `[Waiting on lock owned by Thread 0x...]` name each other |
| 3 | crash | `debug_start`; `exception`; `exception_set` for `System.InvalidOperationException`; `debug_start` again; `eval order.Id`; `exception_reset` | Default stop in `Handle` with `OrderFailedException` and the cause in `inner`; with Break When Thrown the stop is in `Validate`, order 7; the setting is put back |
| 4 | race, by hand | Same on Visual Studio 2022 17.14 (a copy of the project on .NET 8) | Same answers |

## What the run showed (2026-10-25, Visual Studio 2026)

Calls used for the diagnosis: race 7, deadlock 13, crash 6.

- **A bug of ours, found and fixed:** at a stop where several threads were involved the snapshot came back empty and `debug_status` and `stack` failed with `E_FAIL`: reading the line number of a frame without source
  (framework code, a wait) threw and failed the whole answer. A frame is now listed with what it does give.
- **Deadlock:** Visual Studio writes the answer into the stack of a blocked thread: `[Deadlocked, ...]` and `[Waiting on lock owned by Thread 0xE48, ...]` (the owner's id in hex). `threads` plus one `stack` per thread is enough to see the cycle;
  the agent has to convert the hex id. 13 calls is mostly one `stack` per thread: the program had about 20 threads, most of them idle pool threads.
- **A blocked thread cannot be asked anything:** on a thread waiting for a lock Visual Studio refuses `eval` and `vars` (`0x89711006`), even for a field. Before, this came back as an unhelpful `internal`; now it is `eval_failed` and the message points to `stack` and
  says to select another thread.
- **Crash:** the default stop is not on the main thread at `Wait()` (as planned) but at the wrapper `Handle`, because Visual Studio stops on a user-unhandled exception where it leaves user code. The cause is one frame away and in `inner`;
  Break When Thrown for the inner type stops in `Validate`.
- **Race:** a breakpoint where both workers pass makes both stops reproducible; a `watch` on the status and the count shows the state at each stop, so no conditional breakpoint (a Mutate approval) is needed.

## Gaps this shows (candidates for later)
- A "parallel stacks" answer that groups the threads by their stack and drops the idle pool threads would turn the 13 calls of the deadlock into about 3.
- The wait hint of a blocked thread as data (`waitsFor` thread id in decimal) instead of text in a frame name.
- Freezing and thawing threads; a Tasks list. Memory snapshots and dump analysis stay out of reach.

## Run record

| Date | Visual Studio | Rows | Result |
|---|---|---|---|
| 2026-10-25 | 2026 18.x, extension 0.6.0 dev build (protocol 2.9) | 1-3 by `tests/e2e/watch.ps1 -Driver concurrency-lab` | all 22 checks passed, nothing left behind |
