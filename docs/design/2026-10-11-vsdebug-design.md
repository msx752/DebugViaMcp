# VsDebug — Design Specification

| | |
|---|---|
| **Status** | Draft — awaiting review |
| **Date** | 2026-10-11 |
| **Scope** | v1: .NET (C#) debugging in Visual Studio 2022 (17.14+) and Visual Studio 2026 on Windows (x64, arm64 — including Windows VMs on Apple silicon). Follow-up milestone: a VS Code adapter (C# Dev Kit) for macOS, Linux and Windows, speaking the same protocol |
| **License** | MIT |

## 1. Purpose

VsDebug lets an AI coding agent (Claude Code first, any MCP client in principle) drive the
Visual Studio debugger from the terminal: manage breakpoints (including conditional, hit-count
and tracepoints), control execution (start/attach, continue, pause, step into/over/out,
run-to-line), inspect state (call stack, threads, locals, arguments, exceptions), evaluate
expressions like QuickWatch, and — with user consent — change values.

### Goals
- Full debugger control for .NET code through a small, LLM-friendly MCP tool set.
- Safe by default: the user stays in control of anything that mutates program state.
- Fully observable: every operation is logged and traceable end to end.
- Clean install (four commands) and documentation good enough for a public open-source release.

### Non-goals (v1)
- Native C++, Python, JavaScript or mixed-mode debugging (the protocol must not preclude them).
- The VS Code adapter itself (milestone M5, separate spec). v1 must, however, keep the protocol and
  the MCP host platform- and language-neutral so that adapter needs no change to them (§2.4).
- Visual Studio for Mac (retired by Microsoft on 2024-08-31).
- Remote machines, HTTP/SSE transport, multi-user scenarios.
- Editing source code, building, or running tests — other tools already do that.

## 2. Architecture

```
┌──────────────────────┐   stdio (MCP)   ┌───────────────────────────┐
│ Claude Code / other  │ ──────────────► │ VsDebug.Mcp (dotnet tool) │
│ MCP client           │                 │ net10.0, MCP C# SDK       │
└──────────────────────┘                 └─────────────┬─────────────┘
                                                       │ named pipe + JSON-RPC (StreamJsonRpc)
                                         ┌─────────────▼─────────────┐
                                         │ VsDebug.Vsix (in-proc VS) │
                                         │ net48, VSSDK, AsyncPackage│
                                         └─────────────┬─────────────┘
                                                       ▼
                                             EnvDTE Debugger5 + DebuggerEvents
```

### 2.1 Components

| Project | Target | Responsibility | Depends on |
|---|---|---|---|
| `VsDebug.Protocol` | netstandard2.0 | `IDebugBridge` RPC contract, `IDebugBridgeEvents` callback contract, DTOs, error codes, `ProtocolVersion`, wire/discovery JSON conventions, per-OS paths | Newtonsoft.Json only |
| `VsDebug.Vsix.Core` | netstandard2.0 | All VS-independent VSIX logic (bridge service over `IDteFacade`, handshake gate, event broadcast, pipe host, discovery writer) — unit-testable without VS | Protocol, StreamJsonRpc |
| `VsDebug.Vsix` | net48 | Composition root inside VS: EnvDTE facade, debugger event pump, ACL'd pipe, permission enforcement, consent UI, status-bar indicator, Output pane, options page | Vsix.Core, VSSDK |
| `VsDebug.Mcp` | net10.0, packed as `dotnet tool` (command `vsdebug`) | MCP tools, instance discovery/selection, session state, event buffering, output shaping, `doctor` / `setup` / `serve` commands | Protocol, MCP C# SDK |

**Dependency rule:** `Vsix → Vsix.Core → Protocol ← Mcp`. Mcp never references a Visual Studio
assembly; Vsix and Vsix.Core never reference the MCP SDK; Protocol contains no VS-specific types.
Enforced by `VsDebug.ArchitectureTests` (assembly-reference checks) in CI.

### 2.2 Why this shape (summary — full reasoning in ADRs)
- **Bridge instead of MCP-inside-VS:** the MCP SDK runs on modern .NET without assembly conflicts
  inside the VS process; stdio needs no port, no firewall and no localhost-HTTP hardening; a crash
  in the MCP layer cannot take VS down; the MCP layer is testable without VS.
- **StreamJsonRpc:** ships with Visual Studio (no bundling, no version conflicts), supports
  bidirectional calls so VS can push break events.
- **VisualStudio.Extensibility (out-of-proc) rejected for now:** its debugger surface is not
  sufficient for breakpoints/evaluate/step. Re-evaluate in a future ADR.

### 2.3 Instance discovery
- On package load, each VS instance creates a pipe with a random name and writes
  `<VsDebug home>/instances/{pid}.json` (home per OS: §2.4):
  `{ pid, pipeName, token, solutionPath, vsVersion, vsixVersion, protocolVersion, startedAt }`.
  The file ACL grants access to the current user only. The file is deleted on VS shutdown.
- Mcp reads the directory, drops entries whose process is gone, and:
  - exactly one instance → auto-connects;
  - several → state-changing calls are refused until `vs_select(pid)` is called.
- Handshake: Mcp sends `{ token, protocolVersion, clientName }`. A major-version mismatch is
  rejected with `version_mismatch` and an upgrade hint.

### 2.4 Cross-platform and multi-IDE readiness
An IDE adapter is anything that implements `IDebugBridge` behind a pipe and writes an instance
file. The VSIX is the first; a VS Code extension (TypeScript, `vscode.debug` API) is planned (M5).
To keep that adapter free of changes to Protocol or Mcp:

- **Wire format is language-neutral and fixed:** JSON-RPC 2.0 with `Content-Length` header framing
  (StreamJsonRpc `HeaderDelimitedMessageHandler` + `JsonMessageFormatter`, compatible with
  `vscode-jsonrpc`); camelCase property names; enums as camelCase strings; ISO-8601 dates; nulls
  omitted. Defined once in `ProtocolJson` and locked by a golden-JSON test. No MessagePack.
- **Instance files** use the same JSON conventions.
- **VsDebug home** (instances, logs):
  | OS | Path |
  |---|---|
  | Windows | `%LOCALAPPDATA%\VsDebug` |
  | macOS | `~/Library/Application Support/VsDebug` |
  | Linux | `$XDG_DATA_HOME/VsDebug` or `~/.local/share/VsDebug` |
  On Windows the local-app-data directory is taken from the user's real profile
  (`GetUserProfileDirectory` on the process token), not from `USERPROFILE`/`LOCALAPPDATA`, so a
  normally started Visual Studio and an MCP host launched from a customized shell agree.
  `VSDEBUG_HOME` overrides it (tests, portable setups).
- **Transport per OS:** Windows named pipe with a user-only ACL. On macOS/Linux the instance file's
  `pipeName` is an absolute Unix-domain-socket path inside the VsDebug home; the directory is
  `0700` and the socket `0600`. .NET's `NamedPipeClientStream` accepts such a path directly.
- **Mcp is cross-platform:** `VsDebug.Mcp` builds and its tests run on Windows, macOS and Linux.

## 3. MCP tool contract

Design rule: minimise round trips and output size. Every tool that resumes execution returns a
**BreakSnapshot** when the debugger stops, so the agent never needs a separate "where am I" call.

### 3.1 Tools

**Session**
| Tool | Description |
|---|---|
| `vs_instances` | List running VS instances (pid, solution, VS version, VSIX version) |
| `vs_select(pid)` | Choose the instance to control |
| `debug_status` | Debugger mode (`design` / `run` / `break`) and, in break mode, the current snapshot |
| `diag(tail = 50)` | Recent log lines (values masked per logging policy) for self-diagnosis |

**Breakpoints**
| Tool | Description |
|---|---|
| `bp_list` | id, location, condition, hit count/kind, enabled, bound state |
| `bp_set` | `{file, line}` **or** `{function: "Ns.Class.Method"}`; optional `condition`, `conditionKind` (`true` \| `changed`), `hitCount` + `hitKind` (`equal` \| `atLeast` \| `multiple`), `logMessage` (tracepoint — logs without stopping). Returns `{ id, bound, bindError? }` |
| `bp_remove(id \| all)` | Remove; unknown id returns `notFound: true` |
| `bp_enable(id, enabled)` | Enable/disable |

**Execution**
| Tool | Description |
|---|---|
| `debug_start(project?)` | Start debugging the startup (or given) project |
| `debug_attach(pid \| processName)` | Attach to a process owned by the current user |
| `debug_stop` | Stop debugging |
| `debug_continue` | Resume; waits up to 30 s for the next break → snapshot or `{state: "running"}` |
| `debug_pause` | Break all → snapshot |
| `debug_step(kind: into \| over \| out)` | Step → snapshot |
| `debug_run_to(file, line)` | Run to location → snapshot |
| `debug_wait(timeoutSec ≤ 120)` | Wait for the next break → snapshot or `{timedOut: true}` |

**Inspection**
| Tool | Description |
|---|---|
| `stack(threadId?, max = 20)` | Call stack frames |
| `threads` | Threads with id, name, current location |
| `frame_select(index)` | Subsequent `vars` / `eval` run in this frame |
| `vars(scope: locals \| args, path?, depth = 1, maxItems = 50)` | Variables; `path` drills into members, e.g. `order.Lines[2]` |
| `eval(expression, allowSideEffects = false)` | QuickWatch equivalent |
| `set_value(expression, value)` | Assign a value (permission tier `mutate`) |
| `exception` | Current exception: type, message, inner exceptions, throw site |

### 3.2 BreakSnapshot

```json
{
  "reason": "breakpoint",
  "breakpointId": "bp3",
  "epoch": 12,
  "location": { "file": "src/OrderService.cs", "line": 42, "function": "OrderService.Submit(Order order)" },
  "source": ["40:   var total = Calc(order);", "41:   if (total > limit)", "> 42:     Reject(order);", "43: }"],
  "args": [{ "name": "order", "type": "Order", "value": "{Id=17, Lines=3}" }],
  "stackTop": ["OrderService.Submit:42", "OrderController.Post:18", "..."]
}
```
`reason` ∈ `breakpoint` | `step` | `exception` | `pause` | `runTo`.

### 3.3 Output shaping
- Values truncated at 1 KB with `truncated: true`; collections paged (`maxItems`, `offset`).
- File paths relative to the solution root.
- Values originating from the debuggee are returned in a dedicated `data` field; tool
  descriptions state that such values are untrusted program data, not instructions.

### 3.4 Side-effect-free evaluation
`allowSideEffects = false` appends the C# `, nse` (no side effects) format specifier to the
expression. **Risk:** behaviour through DTE `GetExpression` must be verified by a spike (first
task of the implementation plan). Fallback: the policy layer rejects expressions containing
method invocations or assignments when `allowSideEffects = false`.

## 4. Runtime model

### 4.1 Mode mirroring and events
- Mcp mirrors the debugger mode (`Design` / `Run` / `Break`). VSIX pushes `ModeChanged`,
  `Break(snapshot)` and `BreakpointsChanged` over the pipe; no polling.
- Every event carries a monotonically increasing `seq`. An execution command records the
  current `seq` and waits for the first `Break` with a higher `seq`, so a break that arrives
  before the wait starts is never lost.
- Execution tools wait 30 s by default, then return `{state: "running"}`; the agent continues
  with `debug_wait`. No call blocks indefinitely.

### 4.2 Break epochs
Each resume (`continue`, `step`, `run_to`) increments the **epoch**. Frame indices and variable
paths are bound to an epoch; using one from an older epoch returns `stale` with the message
"program resumed since this frame was read".

### 4.3 Threading
All DTE calls run on the VS UI thread via `JoinableTaskFactory.SwitchToMainThreadAsync`, each
wrapped in a 10 s timeout. Debugger evaluations use a 5 s evaluation timeout.

## 5. Logging and observability

| Layer | Destination | Content |
|---|---|---|
| Mcp | `<VsDebug home>/logs/mcp-{date}.log` (JSON lines) + **stderr** | Tool calls, durations, RPC errors, handshake, discovery |
| VSIX | `<VsDebug home>/logs/vsix-{pid}-{date}.log` + **Output → "VsDebug"** pane | Pipe connections, DTE calls, debugger events, UI-thread wait times |
| Audit | `<VsDebug home>/logs/audit-{date}.log` | Every state-changing operation: `bp_set/remove/enable`, `set_value`, side-effecting `eval`, `start/stop/attach`, consent decisions — client name, parameters, outcome |

- **Correlation:** every MCP call gets a `callId`, propagated over RPC and logged on both sides.
- **stdout is reserved for MCP.** Nothing else may write to stdout; enforced by a test that
  asserts stdout contains only valid JSON-RPC frames.
- **Privacy:** variable *values* are never logged by default. `VSDEBUG_LOG_VALUES=1` opts in
  (documented with a warning). `diag` applies the same masking.
- **Configuration:** `VSDEBUG_LOG_LEVEL` or `--log-level` (default `Information`) for Mcp;
  Tools → Options → VsDebug for VSIX. Rolling: 10 MB per file, 7 days retention.
- **Libraries:** Mcp uses `Microsoft.Extensions.Logging` + Serilog file sink. VSIX uses a small
  built-in file writer and the Output pane — no third-party logging assemblies inside VS.
- **`vsdebug doctor`:** reports .NET version, discovered VS instances and VSIX versions, pipe
  connectivity and protocol compatibility, log folder locations, and whether VsDebug is
  registered in Claude Code.

## 6. Security

### 6.1 Threat model
No network surface. Relevant threats:
1. Another local user connecting to the pipe.
2. **Prompt injection** via debuggee data steering the agent into mutating state or running
   side-effecting code.
3. The user not noticing what the agent is doing.

Out of scope: malicious processes running as the same user (they can already control VS).

### 6.2 Controls
- **Transport:** pipe ACL restricted to the current user; NETWORK SID explicitly denied; random
  pipe name; per-instance token in the user-only discovery file, verified at handshake.
- **Attach:** only processes owned by the current user; never elevates.
- **Permission tiers** — stored in VS Options, **enforced in VSIX** so no tool call can change them:

| Tier | Operations | Default |
|---|---|---|
| `read` | status, stack, threads, vars, side-effect-free `eval`, `bp_list`, `exception` | Allow |
| `control` | `bp_set/remove/enable`, step, continue, pause, run_to, start/stop/attach | Allow |
| `mutate` | `set_value`, `eval(allowSideEffects = true)` | **Ask** |

- **Consent (Ask):** VS shows an info bar, e.g.
  *"Claude wants to set `order.Total = 0` in OrderService.Submit"* — **[Allow once] [Allow for session] [Deny]**.
  No answer within 60 s → denied. Every decision is audited.
- **Visibility:** status-bar indicator "VsDebug: Claude connected" while a client is attached;
  clicking it shows recent operations. **Disconnect AI** command (toolbar + shortcut) drops the
  connection and blocks new ones until re-enabled.
- **Honesty:** `docs/security.md` states that data marking alone does not stop prompt
  injection; the `mutate` consent gate is the real control.

### 6.3 Project security
No telemetry, no network calls. Dependabot, CodeQL, `SECURITY.md` with GitHub private
vulnerability reporting, signed NuGet package, SHA256 checksums on release assets.

## 7. Failure handling

Every error has a **code**, a **human message** and a **next-step hint**, and is logged with its `callId`.

| Situation | Behaviour |
|---|---|
| No VS / VSIX not installed | `vs_instances` empty; hint: "Open Visual Studio with the VsDebug extension, or run `vsdebug doctor`" (`not_connected`) |
| Several instances, none selected | State-changing calls refused with the instance list (`ambiguous_instance`) |
| VS closed or crashed | Session → `Disconnected`; next call gets `not_connected`; reconnect to a reopened solution is suggested, not automatic |
| Mcp crashed | VS unaffected; VSIX logs the disconnect, indicator turns off; client restarts the server and re-handshakes |
| VS UI thread busy (build, modal dialog) | 10 s timeout → `busy` with the reason when detectable |
| Wrong mode (e.g. `vars` while running) | `wrong_mode`: "requires Break mode; current: Run. Call debug_pause or debug_wait." |
| Breakpoint cannot bind | `bp_set` succeeds with `bound: false` and VS's reason (e.g. "No symbols loaded") |
| Evaluation error/timeout | VS error text returned verbatim; 5 s evaluation timeout (`eval_failed`) |
| Protocol mismatch | Handshake rejected (`version_mismatch`) with upgrade hint |
| Consent denied / timed out | `denied`, audited; message tells the agent this was the user's decision |
| Stale frame/variable | `stale` (see §4.2) |

**Idempotency:** `bp_set` on an identical location + condition returns the existing id;
`bp_remove` of an unknown id returns `notFound: true`.

## 8. Quality attributes

| Attribute | Scenario | Measure |
|---|---|---|
| Responsiveness | Agent calls `debug_step` on a breakpoint in a typical method (normal load) | Snapshot returned in < 500 ms p95 (excluding debuggee execution time) |
| Non-blocking | VS shows a modal dialog while a tool call arrives | Call returns `busy` within 11 s; never hangs |
| VS stability | Mcp process is killed mid-call | VS remains responsive; VSIX logs disconnect; no unhandled exception |
| Output size | `vars` on an object with 10 000 elements | Response ≤ 16 KB; paging metadata present |
| Safety | `set_value` with default options | Never executes without an explicit user Allow in VS |
| Traceability | Any state-changing tool call | Appears in audit log with `callId`, parameters and outcome |

## 9. Testing

| Layer | Proves | How |
|---|---|---|
| `VsDebug.Protocol.Tests` | Contract stability | DTO round-trip tests; snapshot of the public API and `tools/list` schema (Verify) |
| `VsDebug.Mcp.Tests` | Tool behaviour | `FakeDebugBridge`: seq race, stale epochs, truncation/paging, permission tiers, error codes, idempotency, stdout purity |
| `VsDebug.Vsix.Tests` | RPC → DTE translation | DTE hidden behind a thin `IDteFacade`; translation tested without VS |
| `VsDebug.ArchitectureTests` | Dependency rule | NetArchTest |
| `VsDebug.E2E` | It actually works | VS experimental instance + `samples/OrderDemo` driven through the real Mcp: conditional bp → start → wait → vars → step → eval → set_value (Allow) → stop. `Microsoft.VisualStudio.Extensibility.Testing`. Local first; nightly CI later |

### Fitness functions
| Rule | Check |
|---|---|
| Dependency direction | ArchitectureTests fail the build |
| Tool contract stable | `tools/list` snapshot diff |
| Wire format language-neutral | Golden-JSON test on `ProtocolJson` output |
| Mcp cross-platform | Mcp + Protocol tests run on Windows and macOS in CI (M4) |
| stdout carries only MCP | stdout-purity test |
| Values not logged by default | Test runs a tool with a secret-like value and asserts it is absent from log files |
| `mutate` gated | Test asserts `set_value` is denied when consent is `Ask` and no answer arrives |
| Docs match tools | `docs/tools.md` is generated from the schema; CI fails on drift |

## 10. Repository, build and release

```
vsdebug/
├─ src/        VsDebug.Protocol · VsDebug.Mcp · VsDebug.Vsix
├─ tests/      VsDebug.Protocol.Tests · VsDebug.Mcp.Tests · VsDebug.Vsix.Tests · VsDebug.ArchitectureTests · VsDebug.E2E
├─ samples/    OrderDemo
├─ docs/       getting-started · tools · security · logging · troubleshooting · architecture · adr/ · design/
├─ .github/    workflows (ci, e2e-nightly, release) · issue / PR templates
├─ README.md · LICENSE · CHANGELOG.md · CONTRIBUTING.md · SECURITY.md · CODE_OF_CONDUCT.md
└─ VsDebug.slnx · global.json · Directory.Build.props · Directory.Packages.props · .editorconfig
```

- Central Package Management, nullable enabled, warnings as errors.
- Versioning: MinVer from git tags (SemVer); Conventional Commits; CHANGELOG generated from commits.
  `ProtocolVersion` is versioned independently of the package version.
- **Publication:** nothing is pushed to GitHub, NuGet or the Marketplace until the maintainer
  explicitly decides to publish. Workflows are prepared but stay local.

## 11. Installation (target user experience)

```powershell
# 1) Extension: "VsDebug" from the Visual Studio Marketplace (or the .vsix from Releases)
# 2) MCP server
dotnet tool install -g VsDebug.Mcp
# 3) Register with Claude Code (writes the config for you)
vsdebug setup claude          # equivalent: claude mcp add vsdebug -- vsdebug serve
# 4) Verify
vsdebug doctor
```
`vsdebug setup` is structured for more targets later; v1 implements `claude` only.

## 12. Documentation

- **README:** what it does, demo GIF, quickstart, example prompt ("stop in OrderService.Submit
  when total > 1000 and show me the order"), security summary.
- **docs/tools.md:** generated from the `tools/list` schema — parameters, examples, error codes.
- **docs/security.md:** threat model, tiers, consent, prompt-injection limits.
- **docs/logging.md:** file locations, levels, correlation ids, value-logging opt-in.
- **docs/troubleshooting.md:** one entry per `doctor` check.
- **docs/adr/:**
  - ADR-001 Bridge architecture (Mcp out-of-proc, VSIX in-proc)
  - ADR-002 StreamJsonRpc over named pipes
  - ADR-003 stdio-only MCP transport
  - ADR-004 `mutate` defaults to Ask
  - ADR-005 Variable values not logged by default
  - ADR-006 v1 scope: .NET only, VS 2022 + 2026
  - ADR-007 Language-neutral wire format and per-OS home (prepares the VS Code adapter)
  - ADR-008 macOS via a VS Code adapter (Visual Studio for Mac is retired)

## 13. Open risks

| Risk | Mitigation |
|---|---|
| `, nse` may not behave as expected through DTE | Spike first; fallback policy filter (§3.4) |
| One VSIX targeting VS 2022 and 2026 | Install target range `[17.0, 19.0)`; E2E on both before release |
| Name availability (`VsDebug` on NuGet / Marketplace) | Check before first package; fallback names: `DebugPilot`, `vs-debug-bridge` |
| `debug_start` on solutions with multiple startup projects | v1 uses VS's configured startup; `project` parameter selects a single one |
| VS Code debug API differs from DTE (e.g. no first-class conditional "when changed", evaluation contexts) | M5 spec maps each tool; unsupported options return `invalid_argument` with an explanation rather than being ignored |
