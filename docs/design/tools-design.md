# Debugging via MCP M3 — Complete Tool Set & IDE State Design

| | |
|---|---|
| **Status** | Implemented (feature/m3-tools) |
| **Date** | 2026-10-11 |
| **Milestone** | M3 (complete v1 tool set, IDE state) |
| **Parent specs** | [design.md](design.md) (§3 tool contract, §4 runtime model), [safety-design.md](safety-design.md) (permission tiers, consent, audit). Where this document is more specific, it wins. |

## 1. Intent

At the end of M3 the v1 tool contract (parent spec §3.1) is complete, and the agent can see what Visual Studio
knows that the file system and CLI cannot: the Output window, the Error List and the result of a VS build.
M3 also closes the minor issues deferred from the M2 final review.

**Success criteria**
- Every tool in parent spec §3.1 is implemented, plus `output_read`, `errors_list` and `vs_build`.
- A frame index or variable path read before a resume is rejected with `stale`, never silently applied to the new
  program state.
- `debug_attach` never runs without the user's Allow under default settings.
- The agent cannot read the extension's own log or Activity (audit) panes.
- A 2.0 MCP host keeps working against a 2.1 VSIX and vice versa; M3 tools against a 2.0 VSIX return
  `unsupported` with a hint to update the extension.
- All M3 tools pass the end-to-end run in a real Visual Studio 2026 (§8).

**Out of scope:** multi-process debugging (attach while a session is running), Test Explorer results, active
document/selection, code navigation (parent spec §13), program `Console` output (it goes to a separate console
window, not to Visual Studio). M4 keeps `setup`/`doctor`, CI and the macOS test matrix.

## 2. Protocol 2.1

All changes are additive, so `ProtocolInfo.Minor` goes from 0 to 1; `Major` stays 2. No existing method, DTO
field or event changes meaning.

- The VSIX records the client's `HandshakeRequest.ProtocolMinor`. New enum values (`BreakReason.RunTo`) and new
  events (`OnFrameChangedAsync`) are only sent to clients with minor ≥ 1; older clients get `BreakReason.Other`
  and no frame events.
- The MCP host records the VSIX's `HandshakeResponse.ProtocolMinor`. Calling an M3 tool against a minor-0 VSIX
  returns `unsupported` with the hint "Update the Debugging via MCP extension in Visual Studio" without sending the
  request.
- New error codes:

| Code | Name | `data` | When |
|---|---|---|---|
| -32012 | `stale` | `{ hint }` | A frame index or epoch from before the latest resume |
| -32013 | `unsupported` | `{ hint }` | The connected VSIX does not implement the request (minor too old) |

- New `IDebugBridge` methods (each takes `CallContext` before the `CancellationToken`, as in M2):

| Method | Returns |
|---|---|
| `GetThreadsAsync()` | `ThreadInfo[]` |
| `GetThreadStackAsync(int? threadId, int maxFrames)` | `StackInfo { Epoch, ThreadId, Frames }` |
| `SelectFrameAsync(int index, long epoch, int? threadId)` | `FrameSelection` (§3.2) |
| `GetExceptionAsync()` | `ExceptionInfo?` |
| `SetBreakpointEnabledAsync(string id, bool enabled)` | `BreakpointInfo` |
| `RunToAsync(string file, int line)` | `RunToResult { Seq, BindError? }` |
| `ListAttachableAsync(string? processName)` | `ProcessInfo[]` |
| `AttachAsync(int pid)` | `long` (seq) |
| `ListOutputPanesAsync()` | `OutputPaneInfo[]` |
| `ReadOutputAsync(string pane, int tail, int offset)` | `OutputPage` |
| `GetErrorListAsync(Severity[] severities, int offset, int max)` | `ErrorListPage` |
| `BuildAsync(string? project, int timeoutSec)` | `BuildResult` |

- `GetStackAsync` stays for 2.0 clients; the 2.1 host uses `GetThreadStackAsync`.
- `BreakSnapshot` and `StatusInfo` gain `Epoch` (long). Unknown fields are ignored by 2.0 clients.
- `IDebugBridgeEvents` gains `OnFrameChangedAsync(FrameChangedEvent { ThreadId, FrameIndex, Epoch })`, pushed on
  `DebuggerEvents.OnContextChanged` — whether the agent or the user changed the frame or thread.

## 3. Runtime model

### 3.1 Epochs

The VSIX keeps an epoch counter that increments on every break it observes, however the program got there
(continue, step, run_to, or the user in the VS UI). Every resume ends in a new break, or in Run/Design mode where
`frame_select` fails with `wrong_mode` anyway, so "read before the latest resume" and "read in an earlier break" are
the same condition, and counting breaks needs no resume detection. It is carried in `BreakSnapshot`, `StatusInfo`, `StackInfo` and
`FrameChangedEvent`. The VSIX is the source of truth: `SelectFrameAsync` with an epoch other than the current one
throws `stale` ("program resumed since this frame was read"). The parent spec's binding of variable paths to
epochs is satisfied by the same rule: paths are evaluated in the selected frame, and the selection is
epoch-checked.

### 3.2 Frame and thread selection

`frame_select(index, epoch, threadId?)` sets `Debugger.CurrentThread` (when `threadId` is given) and
`Debugger.CurrentStackFrame`. The user sees the change in the Call Stack and Threads windows. It returns a
`FrameSelection`: `{ threadId, index, epoch, location, source, args }` — the same shape as the snapshot's
location/source/args, so the agent needs no second call.

`vars`, `eval` and `set_value` run in the selected frame (DTE evaluates in the current frame). The MCP host
mirrors the selection from `OnFrameChangedAsync` and adds `frame: { index, threadId }` to these tools' responses,
so the agent always sees where it is looking. On every resume Visual Studio resets the selection to frame 0 of the
stopping thread; the next break event resets the mirror. No other state is kept.

A thread or frame change (by the agent or the user) advances the event sequence, and consent questions name the
selected frame's function. If the user picks another frame while a `set_value` question is open, the approval no
longer applies (the M2 rule that consent covers the program state the question described).

`threads` returns `{ id, name, location, isCurrent }` per thread. `stack(threadId?)` shows another thread's stack
without changing the selection.

### 3.3 Temporary run-to breakpoint

`debug_run_to(file, line)` does not use VS's RunToCursor (it moves the editor caret). The VSIX:

1. Resolves the path like `bp_set` (solution-relative or absolute; must exist).
2. If a user breakpoint already exists at that location, uses it and does not own it. Otherwise adds a
   breakpoint and marks it as owned.
3. If the breakpoint does not bind, deletes the owned breakpoint and returns `bindError` without resuming.
4. Calls `Go`; the snapshot of the next break has `reason: runTo` when it stopped at that location.
5. Deletes the owned breakpoint on the next break (wherever it is), on the end of the session, or when the
   connection closes. An owned breakpoint is never listed by `bp_list`.

Allowed in Break and Run mode.

### 3.4 Attach

`debug_attach(pid | processName)`, Design mode only.

- Candidates come from `Debugger.LocalProcesses`, filtered to processes owned by the same user, compared by token
  SID (this also holds when Visual Studio runs elevated). The VSIX's own process is excluded.
- A `processName` matching several processes returns `invalid_argument` whose hint lists the candidates
  (`"1234 FaultDemo, 5678 FaultDemo"`); matching none returns `not_found`. Window titles are not returned; they can leak data.
- The consent text names the process and pid and states that Visual Studio will be able to read and change that
  process's memory.
- `Process.Attach()` lets VS choose the engine. Afterwards the session behaves like a started one, except that
  `debug_stop` calls `Debugger.DetachAll()`: the process keeps running.

### 3.5 Build

`vs_build(project?, timeoutSec = 120)`, Design mode only, `timeoutSec` clamped to 1..600.

- Starts `SolutionBuild.Build` or `BuildProject` (active configuration) asynchronously and waits for
  `BuildEvents.OnBuildDone`.
- If a build is already running (started by the agent or the user), it waits for that build instead of starting
  another.
- On completion returns `{ state: "done", succeeded, failedProjects, durationMs }` and points the agent at
  `errors_list`. Error/warning counts are not included: the Error List refreshes asynchronously after
  `OnBuildDone`, so counts read at that moment can be stale. On timeout returns `{ state: "building" }`; calling `vs_build` again joins the running
  build.
- An unknown `project` returns `not_found` with the solution's project names in the hint.

## 4. Tools

| Tool | Tier | Mode | Notes |
|---|---|---|---|
| `threads` | Read | Break | §3.2 |
| `stack(threadId?, max = 20)` | Read | Break | Response gains `epoch`, `threadId` |
| `frame_select(index, epoch, threadId?)` | Read | Break | §3.2 |
| `exception` | Read | Break | Type, message, inner chain (max 5), stack trace (truncated at 4 KB), throw site; `{ active: false }` when none. All in `data`, untrusted |
| `diag(tail = 50)` | — | any | MCP host only: tail of its own log file (≤ 500 lines), already masked by the logging policy. Not a bridge call, not audited by the VSIX |
| `bp_enable(id, enabled)` | Control; Mutate when enabling a breakpoint with a condition or tracepoint message | any | Same rule as `bp_set`: enabled, that expression runs in the debuggee on every hit. Unknown id → `notFound: true` |
| `debug_run_to(file, line)` | Control | Break, Run | §3.3; waits like `debug_continue` |
| `debug_attach(pid \| processName)` | Mutate | Design | §3.4; waits for Run mode, returns `{ state }` |
| `output_read(pane?, tail = 100, offset = 0)` | Read | any | §5.1 |
| `errors_list(severity = "error,warning", max = 50, offset = 0)` | Read | any | §5.2 |
| `vs_build(project?, timeoutSec = 120)` | Control | Design | §3.5 |

All tiers are added to the single table in `PermissionPolicy`, with new `Operation` values and tool names.
Every bridge call is audited as in M2.

## 5. IDE state

### 5.1 Output window

- `output_read` without `pane` returns `{ panes: [{ name, lines }] }`.
- `pane` matches a pane name case-insensitively; `build` and `debug` also match the localized Build and Debug panes
  by their well-known GUIDs.
- Reading never fetches the whole pane text: an `EditPoint` is positioned at line `max(1, last − offset − tail + 1)`
  and only that range is read.
- Lines over 1 KB are truncated (`truncated: true`); a response stops at about 14 KB. `hasMore` and `nextOffset`
  page towards older lines.
- The extension's own panes (log and Activity) are refused with `denied`, reason `policy`, so the agent cannot read
  the audit trail or be fed by it. The check is by pane GUID, not by name.
- Content is returned in `data` with the untrusted-content note: build output can contain text from packages and
  tools.

### 5.2 Error List

- Source is the table API (`IErrorList.TableControl.Entries`), not DTE `ErrorItems`: DTE lacks the error code and
  the build/IntelliSense origin.
- Returns the entries the Error List control holds, so the user's scope filter (e.g. *Current Document*)
  applies; unfiltered access would need a custom table sink. The tool description says so.
- Each item: `{ severity, code, message, project, file, line, column, origin }`, file relative to the solution.
  Messages are truncated at 1 KB and returned in `data` (untrusted).
- Sorted by severity, then file, then line. The response includes the totals
  `{ errors, warnings, messages }` for all entries, before paging.
- `severity` is a comma-separated subset of `error`, `warning`, `message`; anything else → `invalid_argument`.
- Limitation, stated in the tool description: a `dotnet build` run in a terminal does not populate the Error List;
  use `vs_build`.

## 6. Security and audit

- Consent texts (`ConsentText`): attach shows process name and pid with the memory-access warning; `bp_enable(true)`
  shows the condition or message, like `bp_set`.
- Read-tier tools now expose IDE text (Output, Error List). `docs/security.md` gains: what they expose, that users
  who keep secrets in build output can set Read to Ask or Deny, that the extension's own panes are never readable,
  and that all such content is marked untrusted.
- The owned run-to breakpoint and the attach target are written to the audit log.

## 7. Deferred M2 minors

Each gets a failing test first, then the fix:

1. Queued consent requests re-check the policy after a settings change (a now-Allowed request runs, a now-Denied
   one is refused with `policy`).
2. The info bar shows the remaining time for queued requests, not the full timeout.
3. The client name is sanitised once, at handshake.
4. `set_value` rejected by Visual Studio is audited as `failed`, not `ok`.
5. The `AuditLog` activity sink call moves inside the `try`, so a failing sink cannot break auditing.
6. Concurrent handshakes cannot leak an access registration.
7. Import/Export Settings refreshes the applied settings snapshot.
8. A pipe closing before `OnAccessRevoked` arrives reports `ai_disabled`, not `not_connected`. The VSIX waits
   100 ms after sending the notification before closing. When the pipe is lost and the same instance is still listed,
   the MCP host makes one 300 ms reconnect probe; `ai_disabled` from it is reported and the selection is kept.

The manual E2E also checks very long text wrapping in the info bar.

## 8. Testing

- **Unit (Core, fake `IDteFacade`):** tier table for every new operation; epoch increment and `stale`; frame
  selection and reset; run-to breakpoint ownership (existing user breakpoint, bind failure, removal on break /
  end / disconnect); attach candidate filtering and ambiguity; output paging, truncation and protected panes;
  error list mapping, sorting, totals and severity parsing; build join and timeout; minor-version gating of
  `RunTo` and `OnFrameChangedAsync`.
- **Mcp:** `tools/list` snapshot updated; `unsupported` against a minor-0 VSIX; `frame` field from the mirror;
  `diag` tail and masking.
- **Protocol:** golden JSON for the new DTOs and error codes.
- **E2E (`tests/e2e/full-tool-set.cs`, real VS 2026, `samples/OrderDemo`):** `vs_build` → `errors_list` →
  `output_read(build)` → `bp_set` → `debug_start` → `debug_run_to` → `threads` → `frame_select(1)` → `vars` →
  stale `frame_select` after `debug_continue` → `exception` (sample throws) → `bp_enable` → `debug_stop` →
  `debug_attach` to a separate sample process → `debug_stop` (process keeps running).
- **Manual (`docs/testing/m3-manual-e2e.md`):** attach consent text, `bp_enable` consent, protected pane refusal,
  long info-bar text.

## 9. Release

VSIX and MCP tool version 0.3.0; protocol 2.1. README tool list and CHANGELOG updated. Nothing is published
without the user's explicit go-ahead.
