# Concurrency lab (sample and e2e scenarios) — design

Status: draft for approval. No code yet.

## Why
`OrderDemo` and `FaultDemo` run on one thread and break in simple ways. What an agent meets in real debugging is different: two workers
touching the same order, a program that hangs, an exception that appears once. Nothing in our tests shows that the tools are enough to find
such a bug, or where they are not. A sample with deliberate, **reproducible** concurrency bugs, and an e2e scenario per bug in which the agent
(the driver) finds it with our tools alone, answers both questions.

## Goals
- One sample, three bugs, each triggered the same way on every run (no luck, no load).
- One e2e scenario per bug: the driver diagnoses it using only MCP tools and checks the diagnosis.
- A list of what the tools could not do, to decide the next features from facts.

## Non-goals
- A distributed system (RabbitMQ, SQL Server, several services): it would leave services, ports and data on the machine; the lab is one small console project.
- Memory leaks, heap snapshots, crash dumps, Parallel Stacks / Tasks windows: we have no tools for them; the lab records them as gaps, it does not simulate them.
- Randomness or timing: a bug that needs a lucky interleaving makes a flaky test. Interleavings are forced with a `Barrier` or an event.

## The sample: `samples/ConcurrencyLab` (net10.0 console, added to `DebugViaMcp.slnx` under `/samples/`)
The scenario is chosen by the first argument, or, when there is none (a start from Visual Studio), by the text in `scenario.txt` next to the executable (the e2e
driver writes it before `debug_start` and deletes it afterwards; nothing is left in the repository).

| Scenario | What happens | Forced by |
|---|---|---|
| `race` | Two workers claim the same order: both read `Status == Pending`, both charge. `order.ChargeCount` ends at 2. | A `Barrier` between the check and the charge |
| `deadlock` | Worker 1 takes lock A then B (`Transfer`), worker 2 takes B then A. The program hangs. | A `Barrier` after the first lock |
| `crash` | The 7th order throws `InvalidOperationException` inside `Validate`, on a thread-pool task; a wrapper catches it and rethrows it as a different exception, so the default stop is far from the cause. | The order number |

After the bug the program stays alive (`race`: a 5 s wait, then exit; `deadlock`: hangs on purpose; `crash`: waits) so the driver has time; the driver always ends the debug session.

## E2E scenarios (driver `tests/e2e/concurrency-lab.cs`, run with `tests/e2e/watch.ps1 -Driver concurrency-lab`)
Each scenario uses only the tools an agent has and asserts the diagnosis, not the steps.

1. **race:** breakpoint on the charge line, `watch_add order.ChargeCount`, `debug_start`; the stop answer says which thread; `debug_continue` stops again on **another thread** at the same order. Assert: two different `threadId`s for one order id, `ChargeCount` 0 then 1 seen in `watches`, and at the second stop `eval order.Status` is still `Pending`.
2. **deadlock:** `debug_start`, `debug_wait` returns `running`, `debug_pause`, `threads`, `stack` of every thread. Assert: two threads with `Transfer` in their stacks, each waiting inside a lock acquisition (`Monitor.Enter` or `Lock`), at different lock lines; the driver names the lock order inversion.
3. **crash:** run it with Break When Thrown off: the stop is at the wrapper. Then `exception_set` for `System.InvalidOperationException`, restart: the stop is in `Validate`. Assert: the location function, `exception` message, order id 7 via `vars`; `exception_reset` puts the setting back; nothing stays changed.

## What it will show about the tools (to be written into `docs/testing/` after the run)
Questions the run answers: is one `threads` plus one `stack` per thread enough to see a deadlock without a Parallel Stacks window? Does `frame_select` with a thread id make
inspecting another thread usable? Is a conditional breakpoint (a Mutate approval) needed to catch the second hit of the race, or is `watch` enough? How many calls does
each diagnosis take? Candidate features, decided from the answers: grouping stacks (a "parallel stacks" answer), freezing and thawing threads, a Tasks list.

## Hygiene and safety
No services, no files outside the build output; `scenario.txt` is removed by the driver, also after a failure; the e2e runner's hygiene check stays as it is (no VS, server, temp
folder or instance file left behind) and also checks that no `ConcurrencyLab` process is left.

## Done when
`samples/ConcurrencyLab` builds in `build.ps1`; the three scenarios pass on Visual Studio 2026 (and the deadlock and race ones on 2022 17.14 with a copy of the project retargeted to .NET 8, as the repository's solution does not load there); a run record in `docs/testing/concurrency-lab-manual-e2e.md` that lists, per scenario, the calls used and what was missing.

## Left to the builder
Names of classes and lock lines, how long the race waits, exact assertions on frame names (follow what Visual Studio returns for `Monitor.Enter`).
