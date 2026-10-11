# VsMcpDebugger M2 — Safety & Control Design

| | |
|---|---|
| **Status** | Draft — awaiting review |
| **Date** | 2026-10-11 |
| **Milestone** | M2 (safety & control) |
| **Parent spec** | [2026-10-11-vsmcpdebugger-design.md](2026-10-11-vsmcpdebugger-design.md) — §5 (audit, correlation) and §6 (security). Where this document is more specific, it wins. |

## 1. Intent

An AI agent must never change program state without the user's explicit consent in Visual Studio. The user
can always see what the agent is doing, and can cut it off with one command. Everything about this behaviour is
configurable from inside Visual Studio.

**Success criteria**
- With default settings, `set_value` and side-effecting `eval` never execute without an explicit Allow in VS.
- Every state-changing tool call appears in the audit log with its `callId`, parameters and outcome.
- The user can disconnect the agent instantly; it stays disconnected until the user reconnects or VS restarts.
- All settings live in Tools → Options → VsMcpDebugger and take effect on the next call, without a restart.

**Out of scope (M4):** registering the MCP server with Claude Code from VS, `vsmcpdebugger setup` / `doctor`,
Marketplace packaging.

## 2. Architecture decision

The permission gate is **enforced in the VSIX** (parent spec §6.2: no tool call can change it). The MCP host only
describes the outcome to the agent. `callId` travels as an **explicit RPC parameter**, not as ambient tracing
metadata, so the wire format stays language-neutral (ADR-007) and the id is short and testable.

Rejected alternatives: carrying `callId` in StreamJsonRpc's W3C `traceparent` (opaque 32-hex ids, harder to test,
.NET-specific); gating in the MCP host (a modified client could bypass it).

## 3. Protocol v2

`ProtocolInfo.Major` goes from 1 to 2. A v1 peer is rejected at handshake with `version_mismatch`.

- Every `IDebugBridge` method except `HandshakeAsync` takes a `CallContext { string CallId }` as the parameter just
  before the `CancellationToken`.
- New: `SetValueAsync(string expression, string value, CallContext ctx, CancellationToken ct)` →
  `EvaluateResult` (the new value, read back after the assignment).
- Changed: `EvaluateAsync(string expression, bool allowSideEffects, CallContext ctx, CancellationToken ct)`.
- `HandshakeResponse` and `StatusInfo` gain `Permissions { Read, Control, Mutate }` (each `Allow` | `Ask` | `Deny`).
  Informational only; the VSIX decides on every call.
- `HandshakeResponse` gains `LogLevel` (see §6.1).
- `HandshakeRequest.ClientName` carries the MCP client's name from its `initialize` request (e.g. `claude-code`).
- `IDebugBridgeEvents` gains `OnAccessRevokedAsync()`, sent just before Disconnect AI closes the connection.
- New error codes:

| Code | Name | `data` | When |
|---|---|---|---|
| -32010 | `denied` | `{ reason, hint }`, `reason` ∈ `user_denied` · `timeout` · `policy` · `cancelled` · `disconnected` | The permission gate refused the call |
| -32011 | `ai_disabled` | `{ hint }` | Handshake while the user has disconnected AI access |

### 3.1 Operation tiers

Fixed in code, in one table (`PermissionPolicy`):

| Tier | Operations | Default |
|---|---|---|
| `read` | status, list breakpoints, stack, variables, side-effect-free evaluate | Allow |
| `control` | set breakpoints without condition or tracepoint message, remove breakpoints, start, stop, continue, pause, step | Allow |
| `mutate` | set value, evaluate with side effects, set a breakpoint **with a condition or tracepoint message** | **Ask** |

Breakpoint conditions and tracepoint placeholders run in the debuggee on every hit and cannot carry `, nse`, so a
property getter can run arbitrary code; they therefore need consent (added during implementation, after a security
review).

Operations added in later milestones (`bp_enable`, `run_to`, `attach`, `threads`, …) are assigned a tier when they
are added; the table is the single place to do it.

## 4. MCP tool contract changes

- **New `set_value(expression, value)`** — annotations `ReadOnly = false`, `Destructive = true`. Requires Break mode.
- **`eval(expression, allowSideEffects = false)`** — the `ReadOnly` annotation is removed because the tool can now
  mutate state. `false` keeps today's `, nse` behaviour.
- Both descriptions say: "Requires the user's approval in Visual Studio unless they allowed it in settings. If the
  call returns `denied`, that is the user's decision: do not retry; ask the user how to proceed."
- Deferred from the M1 review and closed here because they belong to prompt-injection defence: snapshot `args`
  move under a `data` field, and every `debug_*` tool that returns a snapshot carries the "untrusted program data"
  note in its description.
- `debug_status` reports `permissions`.

## 5. VSIX core (`VsMcpDebugger.Vsix.Core`, no VS SDK dependency)

Authorization happens in exactly one place: `BridgeConnectionHandler.Guard(operation, ctx, call)`.
`DebugBridgeService` stays unaware of permissions.

### 5.1 `PermissionPolicy`
Pure logic. `TierOf(Operation)` and `Decide(Operation, IPermissionSettings) → Allow | Ask | Deny`.
`IPermissionSettings` is read on every call, so Options changes apply immediately.

### 5.2 `ConsentGate` (one per connection)
- `RequestAsync(ConsentRequest, CancellationToken) → AllowOnce | AllowSession | Deny | Timeout | Cancelled`, shown
  through `IConsentPrompt`.
- The gate owns the timeout (setting, default 60 s; injected clock for tests). On timeout or cancellation the prompt
  is dismissed.
- At most one prompt is visible. Further requests queue; each request's timeout starts when it is queued.
- **Allow for session** applies to this connection and this operation kind only (allowing `set_value` does not
  allow side-effecting `eval`). Grants are cleared when the connection closes, on Disconnect AI, and when any
  permission setting changes.
- **Prompt text:** `"{clientName} wants to set order.Total = 0 in OrderService.Submit"` /
  `"{clientName} wants to evaluate GetOrder().Submit() with side effects in OrderService.Submit"`.
  - The expression and value are shown in full; the user must never approve text they cannot see. Therefore
    `mutate` expressions and values are limited to 500 characters each (`invalid_argument` above that).
  - Control characters and line breaks are rendered as visible escapes (`\n`, `\u0007`).
  - `clientName` comes from the handshake, sanitised the same way and capped at 64 characters.

### 5.3 `AuditLog`
- JSON lines in `<home>/logs/audit-{date}.log`; same rolling rules as the VSIX file log (10 MB per file, retention
  from settings, default 7 days).
- Fields: `ts`, `callId`, `client`, `operation`, `tier`, `params`, `decision`, `outcome`, `durationMs`.
  - `decision` ∈ `allow` · `allowOnce` · `allowSession` · `denied:user` · `denied:timeout` · `denied:policy` ·
    `denied:cancelled` · `denied:disconnected`.
  - `outcome` is `ok` or the error code name.
- Recorded: every `control` and `mutate` call, consent decisions, connect/disconnect, Disconnect AI / Reconnect AI,
  permission setting changes. Not recorded: `read` calls.
- `params` holds what the agent sent (expressions, values, breakpoint specs). Values read from the debuggee are
  never written (consistent with "values not logged by default").
- Each entry is also sent to `IActivitySink` as one readable line (§6.4).

### 5.4 `AiAccessSwitch` (one per VS session)
- `Disconnect()`: sets the flag off, closes all connections through `PipeHost`, cancels pending consent requests
  (`denied:disconnected`), clears session grants.
- While off, handshakes fail with `ai_disabled`. `Reconnect()` sets it back on.
- Initial state comes from the "Accept AI connections when Visual Studio starts" setting.
- Raises `Changed` with the connected client names; the status-bar indicator listens to it.

### 5.5 `DebugBridgeService` additions
- `SetValueAsync`: Break mode only. The target `expression` must pass `ExpressionPolicy.RequireNoSideEffects`
  (`order.Total` passes, `GetOrder().Total` fails with hint "Target must be a variable, field, property or element").
  The `value` is free-form (it went through consent). Assign with DTE `Expression.Value`, read back, return
  `{ value, type }`. VS rejection → `eval_failed` with VS's text.
- `EvaluateAsync(allowSideEffects: true)` evaluates without appending `, nse`.

## 6. Visual Studio UI (`VsMcpDebugger.Vsix`, thin adapters)

### 6.1 Tools → Options → VsMcpDebugger (`DialogPage`, VS 2022 17.14 and VS 2026)

| Group | Setting | Default |
|---|---|---|
| Permissions | Read: Allow / Ask / Deny | Allow |
| | Control: Allow / Ask / Deny | Allow |
| | Mutate: Allow / Ask / Deny | Ask |
| | Consent timeout (seconds, 15–300) | 60 |
| Connection | Accept AI connections when Visual Studio starts | On |
| | Show status bar indicator | On |
| Logging | Log level (Error / Warning / Information / Debug) | Information |
| | Log retention (days) | 7 |
| | Log folder (read-only path; opened with Extensions → VsMcpDebugger → Open Logs Folder) | — |

- Changes take effect on the next call. Only **applied** values count: the bridge reads an immutable snapshot taken
  when the page loads and after each successful Apply, never values still being edited in the dialog.
- Permission changes are audited and clear session grants.
- Setting Mutate to **Allow** asks for confirmation ("The agent will be able to change program state without
  asking you").
- **Single place to configure:** the VSIX sends the log level to the MCP host in the handshake; the host follows it.
  `VSMCPDEBUGGER_LOG_LEVEL` remains as an override. Before the first handshake the host uses its own default.
- There is no "log variable values" setting: neither side logs debuggee values anywhere, so it would control nothing.

### 6.2 Commands — Extensions → VsMcpDebugger
Disconnect AI (default `Ctrl+Alt+Shift+F12`, rebindable), Reconnect AI (visible while disconnected), Show Activity,
Open Logs Folder, Options…. Commands live under the Extensions menu, where Visual Studio (2019 and later) expects
extension commands.

### 6.3 Status bar indicator
"VsMcpDebugger: {client} connected" / "VsMcpDebugger: AI disconnected"; hidden when there is no connection and AI
access is on. Clicking opens the Activity pane. Hidden entirely when the setting is off.

### 6.4 Consent info bar
Shown on the Visual Studio main window (its info-bar host is always present). Buttons **[Allow once] [Allow for session]
[Deny]**; the remaining time is shown as text. Closing it with ✕ counts as Deny. Allow clicks during the first second
after the bar appears are ignored (a click aimed at something else must not approve); Deny always counts.

### 6.5 Output → "VsMcpDebugger Activity"
One line per audit entry, e.g.
`14:02:11  c-000042  set_value  order.Total = 0  → allowed (once)  ok  12 ms`.
The existing "VsMcpDebugger" pane keeps technical logs.

## 7. MCP host (`VsMcpDebugger.Mcp`)

- `ToolResponse.RunAsync(toolName, …)` creates the `CallContext` (`c-` + per-process counter, 6 digits), opens an
  `ILogger.BeginScope` with it, logs one start/end line (tool, duration, result code — no parameter values) and
  passes the context to the bridge.
- Client cancellation (`notifications/cancelled`) cancels the RPC; the VSIX dismisses the prompt and audits
  `denied:cancelled`.
- Error mapping to tool results:

| Situation | Agent sees |
|---|---|
| User denied | `denied` / `user_denied`: "Denied by the user in Visual Studio. This is the user's decision — do not retry; ask the user how to proceed." |
| Timeout | `denied` / `timeout`: "The user did not respond in Visual Studio within {n} s. Ask them to check Visual Studio, then retry if they agree." |
| Setting is Deny | `denied` / `policy`: "Blocked by VsMcpDebugger settings in Visual Studio ({Tier} = Deny)." |
| Disconnect AI | The VSIX sends `OnAccessRevokedAsync`, then closes the pipe. The next call gets `ai_disabled` ("The user disconnected AI access in Visual Studio. Ask them to run Extensions → VsMcpDebugger → Reconnect AI.") and keeps the selected instance; the call after that reconnects, so it works once the user chose Reconnect AI. |
| Expression/value over 500 chars | `invalid_argument` |
| `set_value` target has side effects | `invalid_argument` with the target hint |

## 8. Testing

New behaviour is written test-first. The existing 152 tests stay green.

| Project | Covers |
|---|---|
| `Vsix.Core.Tests` | `PermissionPolicy` (mapping, decisions, live settings); `ConsentGate` with fake prompt and clock (all outcomes, timeout, cancellation dismisses, queueing shows one prompt, session grant scope and clearing); `AuditLog` (fields, `callId`, `read` not recorded); `AiAccessSwitch` (drops connections, cancels pending consent, `ai_disabled`, reconnect); `DebugBridgeService` (`set_value` mode/target policy/VS error, side-effecting eval skips `, nse`, 500-char limit); `BridgeConnectionHandler` |
| `Mcp.Tests` | `callId` unique, forwarded and in log scope; `denied` / `ai_disabled` texts and hints; cancellation → RPC cancellation; `tools/list` snapshot (new tool, parameter, annotations, untrusted notes) |
| `Protocol.Tests` | Round-trip of new DTOs; golden JSON for v2 and new error codes |
| `ArchitectureTests` | New Vsix.Core types do not reference the VS SDK |
| Manual / E2E | `docs/testing/m2-manual-e2e.md` and an `m2-e2e` driver extending `scripts/e2e/m1-e2e.cs`: set_value → Allow → value changed; Deny; timeout; Disconnect → `ai_disabled` → Reconnect; Mutate = Deny; Activity pane lines. Info-bar clicks via UI Automation where possible. VS is launched with the real profile environment. |

**Fitness functions added**
- `mutate` gated: with Mutate = Ask and no answer, `set_value` never reaches `IDteFacade` (asserted on the fake).
- Deny never reaches `IDteFacade`.
- A secret-like debuggee value never appears in log or audit files.
- Every `control`/`mutate` call produces exactly one audit entry carrying its `callId`.

## 9. Documentation

- `docs/security.md`: threat model, tiers, consent, Disconnect AI, and the limits of data marking against prompt
  injection (the consent gate is the real control).
- README "What works" and a short "Safety" section.
- `docs/testing/m2-manual-e2e.md`.
