# VsMcpDebugger M3.1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Several Visual Studios with one agent each: automatic instance choice by folder, owner approval for unrelated agents, one-agent-at-a-time lease per Visual Studio, `debug_start(project)` that leaves the startup project alone, and stale-build warnings with an optional Rebuild bar.

**Architecture:** The extension (Vsix.Core) is the authority: connection approval and the lease live in `BridgeConnectionHandler`/`BridgeSecurity`, shared by all connections of one Visual Studio. The MCP server only chooses an instance and reports. All new wire pieces are additive (protocol 2.2): new bridge methods, one new handshake field, one new error code, two new instance-record fields; existing methods keep their signatures so 2.1 peers keep working.

**Tech Stack:** C# (`Vsix.Core` netstandard2.0, `Mcp` net10, `Vsix` net48), StreamJsonRpc, xUnit v3 (`dotnet test --solution`), MCP C# SDK.

**Spec:** `docs/design/2026-10-12-vsmcpdebugger-m3.1-multi-instance-design.md` (parent specs linked there).

## Global Constraints

- `Vsix.Core` targets netstandard2.0: no `record`, no `init`, no `required`, no nullable-attribute-dependent APIs that the project does not already use. `Mcp` targets net10.
- `TreatWarningsAsErrors` with analyzers (CA, VSTHRD): the build must have 0 warnings. A general `catch (Exception)` needs the same `#pragma warning disable CA1031` + reason comment the code already uses.
- Edit files with their existing line endings (CRLF) and BOM; `git diff --stat` must show only the intended lines.
- Repository content (code, comments, docs, commit messages) is English.
- Wire error codes stay outside StreamJsonRpc's reserved `-32000..-32003`. `BridgeErrorCode.InUse = -32015`.
- Protocol is additive: `ProtocolInfo.Minor` 1 → 2, `Major` stays 2; new constant `ProtocolInfo.MinorM31 = 2`. Existing `IDebugBridge` members keep their signatures. A 2.1 extension answers new tools with `unsupported` (the M3 `ToolResponse.RequireMinor` mechanism).
- Every agent-supplied text shown to the user or to other agents (client name, folder, project name) goes through `ConsentText.ClientName` / `ConsentText.Visible` (length-capped, control and bidi characters escaped).
- Debuggee values never enter logs or audit entries (unchanged rule).
- **Do not start, close, restart, automate or script Visual Studio, and do not use UI Automation or DTE from scripts, anywhere in this plan.** Verification is unit tests plus the VSIX compile check. Behaviour that only a real Visual Studio can show is written into `docs/testing/m3.1-manual-e2e.md` (Task 14) for the owner to run.
- Test command (whole suite): `dotnet test --solution VsMcpDebugger.Core.slnf`. VSIX compile check: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" src\VsMcpDebugger.Vsix\VsMcpDebugger.Vsix.csproj /restore /p:Configuration=Debug /p:DeployExtension=false -nodeReuse:false /v:minimal` — expected: exit 0, no warning lines. Run the compile check in every task that changes `src/VsMcpDebugger.Vsix` or `Vsix.Core` public API.
- Commit dates are 2026-10-12, increasing: each task's commit step names its time; set `GIT_AUTHOR_DATE` and `GIT_COMMITTER_DATE` to `2026-10-12T<time>:00+03:00`.

## Review Focus

1. **Two agents issue their first state-changing command at the same instant.** Exactly one wins; the other gets `in_use`; no state is changed by the loser. Pinned in Task 4 (`InstanceLease`) and Task 5.
2. **The holder's pipe drops mid-session or the user picks Disconnect AI.** The lease is free at once; the old holder never silently keeps it after reconnecting. Pinned in Task 5.
3. **A folder that merely shares a prefix with the solution folder** (`D:\proj-evil` vs `D:\proj`), `..` segments, case, trailing separators, relative or empty paths. Never trusted. Pinned in Task 2.
4. **`debug_start(project)` fails or is cancelled halfway.** The startup project is what it was before. Pinned in Task 10.
5. **Twenty `bp_set` calls on a stale project, and a debug session running.** At most one Rebuild bar per project, and none while debugging. Pinned in Task 12.

---

## File structure

| File | Responsibility |
|---|---|
| `src/VsMcpDebugger.Protocol/FolderMatch.cs` (new) | Pure path rules: `Trusts`, `Relates` |
| `src/VsMcpDebugger.Protocol/{ProtocolInfo,Dtos,BridgeErrorCode,IDebugBridge,InstanceRecord}.cs` | Protocol 2.2 additions |
| `src/VsMcpDebugger.Vsix.Core/InstanceLease.cs` (new) | Thread-safe lease state, no I/O |
| `src/VsMcpDebugger.Vsix.Core/LeaseRules.cs` (new) | Which `Operation` needs what from the lease |
| `src/VsMcpDebugger.Vsix.Core/{BridgeSecurity,BridgeConnectionHandler,PermissionPolicy,ConsentText,EventBroadcaster,PipeHost,ConnectionHooks,DiscoveryWriter}.cs` | Approval, lease enforcement, event routing, publication |
| `src/VsMcpDebugger.Vsix.Core/{StaleBuildChecker,StaleBuildNotifier,IRebuildPrompt}.cs` (new) | Stale-build logic and the Rebuild bar's behaviour (no UI types) |
| `src/VsMcpDebugger.Vsix.Core/{IDteFacade,DebugBridgeService}.cs` | `StartProjectAsync`, `CheckBuildFreshnessAsync`, facade seams |
| `src/VsMcpDebugger.Vsix/{DteFacade,Ui/InfoBarRebuildPrompt,VsMcpDebuggerPackage,Ui/StatusBarIndicator}.cs` | Visual Studio implementations only; compile-checked, not unit-testable |
| `src/VsMcpDebugger.Mcp/{Connection/ConnectionManager,Connection/BridgeConnection,Tools/*}.cs` | Instance choice, `vs_release`, `debug_start(project)`, warnings |

---

### Task 1: Protocol 2.2 surface

**Files:**
- Modify: `src/VsMcpDebugger.Protocol/{ProtocolInfo,Dtos,BridgeErrorCode,IDebugBridge,InstanceRecord}.cs`, `src/VsMcpDebugger.Vsix.Core/BridgeConnectionHandler.cs`, `tests/VsMcpDebugger.Mcp.Tests/Fakes/FakeDebugBridge.cs`, `tests/VsMcpDebugger.Mcp.Tests/BridgeConnectionTests.cs`

**Interfaces:**
- Produces:
  - `ProtocolInfo.Minor = 2`, `ProtocolInfo.MinorM31 = 2`
  - `HandshakeRequest.ClientWorkingDirectory : string?`
  - `BridgeErrorCode.InUse = -32015`; `BridgeErrorNames.ToName(InUse) == "in_use"`
  - `InstanceRecord.LeaseHolder : string?`, `InstanceRecord.LeaseHolderFolder : string?`
  - `sealed class BuildWarning { string Code; string Message; string? Project }` (settable properties, default `""`)
  - `IDebugBridge.ReleaseAsync(CallContext, CancellationToken) : Task`
  - `IDebugBridge.StartProjectAsync(string project, CallContext, CancellationToken) : Task<long>`
  - `IDebugBridge.CheckBuildFreshnessAsync(string? project, string? file, CallContext, CancellationToken) : Task<BuildWarning[]>`

- [ ] **Step 1: Write the failing tests**
  - `BridgeConnectionTests.Every_code_the_extension_sends_reaches_the_server_as_a_coded_error`: add `[InlineData(BridgeErrorCode.InUse)]`.
  - `BridgeConnectionTests.New_bridge_methods_round_trip_over_the_pipe` (Fact): `FakeDebugBridge` gets `OnStartProject`, `OnCheckBuild` delegates and a `Released` counter; over `PipeTestServer`, `StartProjectAsync("OrderDemo", …)` returns the fake's seq, `CheckBuildFreshnessAsync("OrderDemo", null, …)` returns one `BuildWarning { Code = "stale_build" }`, `ReleaseAsync` increments `Released`.
  - `BridgeConnectionTests.Handshake_carries_the_client_working_directory`: the fake records `LastHandshake`; connect with `workingDirectory` argument deferred to Task 3, so here assert the DTO property round-trips by calling `bridge.HandshakeAsync(new HandshakeRequest { Token = fake.ExpectedToken, ClientWorkingDirectory = @"D:\x" }, …)` directly and checking `fake.LastHandshake.ClientWorkingDirectory`.
  - In a Protocol test file (existing `ProtocolInfoTests` or new `ProtocolSurfaceTests`): `InstanceRecord` with both lease fields null serialises without them (`ProtocolJson` omits nulls) and a record JSON from 2.1 (no lease fields) deserialises with them null.
- [ ] **Step 2: Run to verify failure**
  Run: `dotnet test --solution VsMcpDebugger.Core.slnf`
  Expected: build errors (new members missing).
- [ ] **Step 3: Implement** the Interfaces block. `BridgeConnectionHandler` implements the three new methods as stubs that throw `Rpc(BridgeErrorCode.Unsupported, "Not implemented yet.", null)`; later tasks replace them. `FakeDebugBridge` implements them with the delegates above (`CallIds`/`Calls` bookkeeping like its neighbours).
- [ ] **Step 4: Run to verify pass**
  Run: `dotnet test --solution VsMcpDebugger.Core.slnf` then the VSIX compile check.
  Expected: all pass; 0 warnings.
- [ ] **Step 5: Commit** (09:10)
  `git commit -m "feat: protocol 2.2 surface - working directory in the handshake, in_use, lease fields, vs_release/start-project/build-freshness bridge methods"`

---

### Task 2: Folder rules

**Files:**
- Create: `src/VsMcpDebugger.Protocol/FolderMatch.cs`, `tests/VsMcpDebugger.Mcp.Tests/FolderMatchTests.cs` (Protocol has no test project of its own; `Mcp.Tests` references it)

**Interfaces:**
- Produces (`namespace VsMcpDebugger.Protocol`, `public static class FolderMatch`):
  - `bool IsInsideOrEqual(string? candidate, string? folder)` — `candidate` is `folder` or below it.
  - `string? SolutionFolder(string? solutionPath)` — directory of the solution file, null for null/empty.
  - `bool Trusts(string? clientWorkingDirectory, string? solutionPath)` — working directory at or below the solution folder.
  - `bool Relates(string? clientWorkingDirectory, string? solutionPath)` — `Trusts` or the solution folder is below the working directory.

- [ ] **Step 1: Write the failing tests** (`FolderMatchTests`, all pure):
  - `Equal_folder_and_children_are_inside` (equal, `\src`, `\src\a\b`).
  - `Sibling_with_the_same_prefix_is_not_inside` (`D:\proj-evil` vs `D:\proj`; `D:\project` vs `D:\proj`).
  - `Comparison_ignores_case_and_trailing_separators_on_windows`.
  - `Dot_dot_segments_are_resolved_before_comparing` (`D:\proj\x\..\..\other` is not inside `D:\proj`; `D:\proj\x\..` is).
  - `Null_empty_and_relative_paths_never_match` (null, `""`, `"   "`, `proj\src`, `.`).
  - `Trusts_needs_the_working_directory_at_or_below_the_solution_folder` (solution `D:\proj\src\App.sln`: `D:\proj\src` and `D:\proj\src\x` trust; `D:\proj` does not).
  - `Relates_also_accepts_a_solution_below_the_working_directory` (`D:\proj` relates to `D:\proj\src\App.sln` but does not trust it; `D:\other` relates to neither).
  - `Solution_path_null_means_no_solution_and_matches_nothing`.
  - `UNC_paths_compare_like_drive_paths`.
- [ ] **Step 2: Run to verify failure** — `dotnet test --solution VsMcpDebugger.Core.slnf --filter-class "*FolderMatchTests"` — Expected: build error (`FolderMatch` missing).
- [ ] **Step 3: Implement** `FolderMatch`. Normalise with `Path.GetFullPath` after rejecting non-rooted input; trim trailing separators; compare with a separator boundary (candidate equals folder, or starts with folder + separator); case-insensitive on Windows, ordinal elsewhere (`RuntimeInformation.IsOSPlatform`). Catch `ArgumentException`/`NotSupportedException`/`PathTooLongException` from `GetFullPath` and return false.
- [ ] **Step 4: Run to verify pass** — whole suite — Expected: pass.
- [ ] **Step 5: Commit** (09:15) `git commit -m "feat: folder rules - trust and relation of a client folder to a solution"`

---

### Task 3: Instance choice by folder (MCP side)

**Files:**
- Modify: `src/VsMcpDebugger.Mcp/Connection/{ConnectionManager,BridgeConnection}.cs`, `src/VsMcpDebugger.Mcp/Tools/SessionTools.cs`, `src/VsMcpDebugger.Mcp/Program.cs`, `tests/VsMcpDebugger.Mcp.Tests/Fakes/FakeConnectionManager.cs`
- Test: `tests/VsMcpDebugger.Mcp.Tests/ConnectionManagerSelectionTests.cs` (new), `tests/VsMcpDebugger.Mcp.Tests/BridgeConnectionTests.cs`, `tests/VsMcpDebugger.Mcp.Tests/InstancesToolTests.cs` (new), snapshot if the tool text changes

**Interfaces:**
- Consumes: `FolderMatch.Relates` (Task 2), `InstanceRecord.LeaseHolder*` (Task 1).
- Produces:
  - `IConnectionManager.WorkingDirectory : string`
  - `ConnectionManager(…, ILogger logger, string? workingDirectory = null)` — default `Environment.CurrentDirectory`.
  - `BridgeConnection.ConnectAsync(…, string clientName = "vsmcpdebugger", string? workingDirectory = null)` — sends it as `HandshakeRequest.ClientWorkingDirectory` (default `Environment.CurrentDirectory`).
  - `vs_instances` entries gain `availability` (`"free"` | `"in_use"`), `heldBy`, `heldByFolder` (omitted when free), `matchesFolder : bool`.

- [ ] **Step 1: Write the failing tests**
  - `ConnectionManagerSelectionTests` (fake `listInstances`/`connect`, `workingDirectory` given):
    - `Single_instance_is_chosen_whatever_the_folder`.
    - `Several_instances_choose_the_only_one_related_to_the_folder` (cwd below instance 2's solution folder → connects to pid 2).
    - `Solution_below_the_working_directory_selects_when_it_is_the_only_one` (cwd = repo root, `.sln` in `src\`).
    - `Several_related_instances_are_ambiguous_and_the_error_lists_each_with_availability` (message contains each pid, solution file name, `free`/`in use by claude-code`).
    - `No_related_instance_among_several_is_ambiguous`.
    - `Explicit_select_wins_over_the_folder_rule`.
    - `Selection_never_runs_two_connections` (second `SelectAsync` disposes the first; existing test pattern).
  - `BridgeConnectionTests.Connect_sends_the_working_directory_in_the_handshake` (via `FakeDebugBridge.LastHandshake`).
  - `InstancesToolTests.Instances_report_availability_holder_and_folder_match` (`FakeConnectionManager` with three records: free, in use by `codex` at `D:\other`, and one related to `WorkingDirectory`; assert JSON fields).
- [ ] **Step 2: Run to verify failure** — whole suite — Expected: build errors, then failures.
- [ ] **Step 3: Implement.** In `GetBridgeAsync`, replace the `instances.Count switch` auto-choice: 0 → `NotConnected` (unchanged); 1 → it; more → `related = instances.Where(r => FolderMatch.Relates(WorkingDirectory, r.SolutionPath))`, exactly one → it, otherwise `AmbiguousInstance` whose message lists candidates (one line per instance: pid, solution file name, availability). `Program.cs` passes nothing new (defaults). `FakeConnectionManager.WorkingDirectory` settable.
- [ ] **Step 4: Run to verify pass** — whole suite; compile check not needed (no Vsix change).
- [ ] **Step 5: Commit** (09:20) `git commit -m "feat: the MCP server picks the Visual Studio related to its working directory; vs_instances shows availability"`

---

### Task 4: `InstanceLease`

**Files:**
- Create: `src/VsMcpDebugger.Vsix.Core/InstanceLease.cs`, `tests/VsMcpDebugger.Vsix.Core.Tests/InstanceLeaseTests.cs`

**Interfaces:**
- Produces (`public sealed class InstanceLease`):
  - `sealed class LeaseHolder { int ConnectionId; string Client; string? Folder; }` (get-only, ctor)
  - `enum LeaseOutcome { Acquired, AlreadyHeld, HeldByOther }`
  - `sealed class LeaseAttempt { LeaseOutcome Outcome; LeaseHolder? Holder; }` (`Holder` = the current holder after the attempt)
  - `LeaseAttempt TryAcquire(int connectionId, string client, string? folder)`
  - `bool Release(int connectionId)` — true when that connection was the holder
  - `LeaseHolder? Holder { get; }`
  - `event EventHandler? Changed` — raised after acquire and after release only, outside the internal lock.

- [ ] **Step 1: Write the failing tests** (`InstanceLeaseTests`): `Free_lease_is_acquired_by_the_first_connection`; `Same_connection_acquiring_again_is_already_held_and_raises_no_event`; `Other_connection_is_held_by_other_and_sees_the_holder`; `Release_by_the_holder_frees_it_and_raises_changed`; `Release_by_another_connection_changes_nothing`; `Changed_is_raised_once_per_real_change`; `Handler_of_Changed_may_call_back_into_the_lease_without_deadlock`; `Concurrent_acquire_has_exactly_one_winner` (64 tasks on a barrier; one `Acquired`, 63 `HeldByOther`).
- [ ] **Step 2: Run to verify failure** — filter `*InstanceLeaseTests` — Expected: build error.
- [ ] **Step 3: Implement** with a private lock; raise `Changed` after leaving the lock.
- [ ] **Step 4: Run to verify pass** — whole suite — Expected: pass.
- [ ] **Step 5: Commit** (09:25) `git commit -m "feat(vsix-core): InstanceLease - one holder per Visual Studio"`

---

### Task 5: Lease enforcement and `vs_release` (extension side)

**Files:**
- Create: `src/VsMcpDebugger.Vsix.Core/LeaseRules.cs`
- Modify: `src/VsMcpDebugger.Vsix.Core/{PermissionPolicy,BridgeSecurity,BridgeConnectionHandler,ConnectionHooks,PipeHost,ConsentText}.cs`, `tests/VsMcpDebugger.Vsix.Core.Tests/SecurityHarness.cs`
- Test: `tests/VsMcpDebugger.Vsix.Core.Tests/{LeaseRulesTests,LeaseTests,PermissionPolicyTests,PipeHostTests}.cs`

**Interfaces:**
- Consumes: `InstanceLease` (Task 4), `BridgeErrorCode.InUse` (Task 1), `ReleaseAsync` stub (Task 1).
- Produces:
  - New `Operation` values `Connect`, `Release`, `BuildCheck`; `PermissionPolicy`: `Connect` and `BuildCheck` → Read tier, `Release` → Read tier; tool names `"connect"`, `"vs_release"`, `"build_check"`.
  - `enum LeaseNeed { None, Read, Change }`; `static class LeaseRules { static LeaseNeed For(Operation operation) }`: `Change` for every Control- and Mutate-tier operation (`Start`, `Stop`, `Continue`, `Pause`, `Step`, `RunTo`, `FrameSelect`, `SetBreakpoint*`, `RemoveBreakpoints`, `EnableBreakpoint*`, `Attach`, `Build`, `SetValue`, `EvaluateWithSideEffects`); `Read` for the other Read-tier operations; `None` for `Connect`, `Release`.
  - `BridgeSecurity(…, AiAccessSwitch access, InstanceLease? lease = null)` with `InstanceLease Lease { get; }` (new instance when null).
  - `ConnectionHooks(Action<int> onAuthenticated, Func<Task> revokeAndClose, int connectionId = 0)` with `int ConnectionId`.
  - `PipeHost` gives each accepted client a unique increasing `connectionId` in its `ConnectionHooks`.
  - `ConsentText.InUse(string holderClient, string? holderFolder) : string` (uses `ClientName`-style escaping: the holder name arrives already escaped, the folder goes through `Visible` and is capped at 120 characters with `…`).
  - `SecurityHarness.AddConnection(string client = "claude-code", string? folder = null) : BridgeConnectionHandler` — a second/third handler sharing the same `Security`, `Service`, `Dte` and a distinct `connectionId`; each does its own handshake with that client name/folder. The harness's own `Handler` keeps connection id 1.

- [ ] **Step 1: Write the failing tests**
  - `LeaseRulesTests.Every_operation_has_a_rule` (all `Enum.GetValues<Operation>()` return without throwing); `Rules_follow_the_tiers` (Control/Mutate → Change, Read-tier → Read, `Connect`/`Release` → None).
  - `PermissionPolicyTests`: the three new operations map to the tiers and names above.
  - `LeaseTests` (two handlers `a`, `b` on one harness):
    - `First_state_changing_command_takes_the_lease_and_the_second_agent_gets_in_use` (a: `SetBreakpointAsync`; b: `SetBreakpointAsync` → `LocalRpcException` with `ErrorCode == (int)InUse`; `s.Dte` shows only a's breakpoint).
    - `A_non_holder_cannot_read_either` (b: `ListBreakpointsAsync`, `GetStatusAsync` → `in_use`; a still reads).
    - `Reading_never_takes_the_lease` (a: `ListBreakpointsAsync`; b: `SetBreakpointAsync` succeeds).
    - `The_error_names_the_holder_and_the_folder_and_escapes_them` (a connects as `"bo\nt"` with folder `"D:\\x\u202Ey"`; message contains the escaped forms and no raw control or bidi character).
    - `The_lease_survives_debug_stop` (a: start, stop; b still `in_use`).
    - `Release_frees_the_lease_and_is_audited` (a: `ReleaseAsync`; b succeeds; audit has `lease` entries for acquire and release).
    - `Disposing_the_holders_connection_frees_the_lease` (lost pipe).
    - `Disconnect_AI_frees_the_lease_at_once` (`s.Security.DisconnectAi()` then `Reconnect`; b succeeds).
    - `A_call_denied_by_policy_does_not_take_the_lease` (Control = Deny; a denied; b succeeds after policy back to Allow).
    - `A_call_the_user_denies_does_not_take_the_lease` (Mutate = Ask, prompt answered Deny).
    - `A_question_in_front_of_the_user_is_not_asked_when_the_lease_is_taken` (a holds; b `SetValueAsync` → `in_use` and `s.Prompt.Texts` empty).
    - `Concurrent_first_commands_have_exactly_one_winner` (a and b `SetBreakpointAsync` started together with the gated UI thread; one succeeds, one `in_use`, one breakpoint exists).
    - `Refusals_are_audited` (an `in_use` refusal leaves one audit entry with outcome `in_use`).
  - `PipeHostTests.Each_client_gets_its_own_connection_id` (two accepted clients → distinct ids in the hooks the factory receives).
- [ ] **Step 2: Run to verify failure** — whole suite — Expected: build errors, then failures.
- [ ] **Step 3: Implement.** In `Guard`: after the existing `Deny` policy decision and before the consent question, `EnsureLeaseAvailable(operation)` (`LeaseRules`: `None` skips; `Read`/`Change`: if another connection holds the lease throw `Rpc(InUse, ConsentText.InUse(...), "Use another Visual Studio instance or retry later.")`); immediately before `call()`, for `Change` call `TryAcquire` and throw the same `InUse` on `HeldByOther`; audit acquisitions and refusals through `security.Audit` (`Operation = "lease"`, summary `acquired`/`released`/`refused`). `Revoke()` and `Dispose()` release; `ReleaseAsync` releases and returns. Store the handshake's `ClientWorkingDirectory` in the handler for later tasks.
- [ ] **Step 4: Run to verify pass** — whole suite, then the VSIX compile check — Expected: pass, 0 warnings.
- [ ] **Step 5: Commit** (09:35) `git commit -m "feat(vsix-core): one agent at a time per Visual Studio - lease taken by the first state-changing command, released by vs_release, disconnect or Disconnect AI"`

---

### Task 6: `vs_release`, `in_use` guidance and MCP reconnect rules

**Files:**
- Modify: `src/VsMcpDebugger.Mcp/Tools/{SessionTools,ToolResponse}.cs`, `src/VsMcpDebugger.Mcp/Connection/ConnectionManager.cs`, `tests/VsMcpDebugger.Mcp.Tests/Snapshots/tools.verified.json`
- Test: `tests/VsMcpDebugger.Mcp.Tests/{SessionToolsTests,HostTests,ConnectionManagerSelectionTests}.cs`

**Interfaces:**
- Consumes: `IDebugBridge.ReleaseAsync` (Task 1), `ProtocolInfo.MinorM31`, lease semantics (Task 5).
- Produces: MCP tool `vs_release()` (no arguments): description "Call when you are finished with this Visual Studio so another agent can use it. Visual Studio is held by one agent from its first change (breakpoint, build, start, attach) until you call this, disconnect or the user chooses Disconnect AI." Result `{ released: true }`. Against a minor-1 extension: `unsupported` (via `RequireMinor`). Without a connection: `{ released: true, note: "not connected" }` (nothing held, no error).

- [ ] **Step 1: Write the failing tests**
  - `SessionToolsTests.Release_calls_the_bridge_and_reports_released`; `Release_without_a_connection_is_a_no_op` (manager has none; does not try to connect); `Release_against_a_2_1_extension_is_unsupported`.
  - `HostTests`: add `"vs_release"` to the tool-name list; snapshot accepted after review.
  - `ConnectionManagerSelectionTests.Release_drops_the_selection_state_nothing_else` — after `vs_release` the connection stays usable (next state-changing call re-acquires); assert via the fake that no disconnect happened.
  - `ToolResponseTests.In_use_error_maps_to_the_in_use_code_and_keeps_the_holders_message` (`RemoteInvocationException` with code -32015 → `error.code == "in_use"`).
- [ ] **Step 2: Run to verify failure** — whole suite.
- [ ] **Step 3: Implement** `vs_release` in `SessionTools` (uses the manager's current connection without creating one: add `IConnectionManager.TryGetBridge() : IDebugBridge?` returning the live bridge or null; fakes updated).
- [ ] **Step 4: Run to verify pass** — whole suite; accept the tool snapshot (`tests/VsMcpDebugger.Mcp.Tests/Snapshots/tools.received.json` → `tools.verified.json`) after reading the diff: only `vs_release` added.
- [ ] **Step 5: Commit** (09:45) `git commit -m "feat: vs_release tool"`

---

### Task 7: Events go to the lease holder

**Files:**
- Modify: `src/VsMcpDebugger.Vsix.Core/{EventBroadcaster,PipeHost}.cs`, `src/VsMcpDebugger.Vsix/VsMcpDebuggerPackage.cs`
- Test: `tests/VsMcpDebugger.Vsix.Core.Tests/EventBroadcasterTests.cs` (new)

**Interfaces:**
- Consumes: `InstanceLease.Holder` (Task 4), `ConnectionHooks.ConnectionId` (Task 5).
- Produces:
  - `EventBroadcaster(ILog log, InstanceLease? lease = null)`.
  - `EventBroadcaster.Add(JsonRpc rpc, int clientMinor, int connectionId)` (the two-argument overload is removed; `PipeHost` passes `hooks.ConnectionId`).
  - `internal static IReadOnlyList<int> SelectRecipients(IReadOnlyList<int> connectionIds, LeaseHolder? holder)` — all ids when `holder` is null, only the holder's id otherwise (empty when the holder is not among them).

- [ ] **Step 1: Write the failing tests** (`EventBroadcasterTests`): `Without_a_holder_every_connection_receives_events`; `With_a_holder_only_the_holder_receives_them`; `A_holder_that_is_not_connected_receives_nothing_and_nobody_else_does`; `Selection_keeps_the_input_order`.
- [ ] **Step 2: Run to verify failure** — filter `*EventBroadcasterTests`.
- [ ] **Step 3: Implement**; `VsMcpDebuggerPackage` constructs `InstanceLease` first and passes the same instance to `EventBroadcaster` and `BridgeSecurity`.
- [ ] **Step 4: Run to verify pass** — whole suite and the VSIX compile check.
- [ ] **Step 5: Commit** (09:50) `git commit -m "feat(vsix-core): break and mode events go only to the lease holder"`

---

### Task 8: Publish the holder and show it

**Files:**
- Modify: `src/VsMcpDebugger.Vsix.Core/DiscoveryWriter.cs`, `src/VsMcpDebugger.Vsix/{VsMcpDebuggerPackage,Ui/StatusBarIndicator}.cs`
- Test: `tests/VsMcpDebugger.Vsix.Core.Tests/DiscoveryWriterTests.cs`

**Interfaces:**
- Consumes: `InstanceLease.Changed`/`Holder`, `InstanceRecord.LeaseHolder*` (Task 1).
- Produces: `DiscoveryWriter.UpdateLease(string? holder, string? folder)` — rewrites the instance file atomically like `UpdateSolution`; no-op before the first `Write`.

- [ ] **Step 1: Write the failing tests**: `UpdateLease_writes_holder_and_folder_into_the_file`; `Clearing_the_lease_removes_both_fields`; `UpdateLease_keeps_every_other_field_and_the_token`; `UpdateLease_before_write_does_nothing`.
- [ ] **Step 2: Run to verify failure** — filter `*DiscoveryWriterTests`.
- [ ] **Step 3: Implement** `UpdateLease`. In `VsMcpDebuggerPackage`: `lease.Changed += (_, _) => _discovery.UpdateLease(holder?.Client, holder?.Folder)` (read `lease.Holder` inside the handler). `StatusBarIndicator.TryCreate` gets a `Func<string?> leaseHolder` and appends the holder (`" — in use by <client>"`) to its text when non-null; refreshed from the same `Changed` event. These two Visual-Studio-only edits are compile-checked, not unit-tested.
- [ ] **Step 4: Run to verify pass** — whole suite and the VSIX compile check.
- [ ] **Step 5: Commit** (09:55) `git commit -m "feat: the instance file and the status bar show who holds the Visual Studio"`

---

### Task 9: Connection approval

**Files:**
- Modify: `src/VsMcpDebugger.Vsix.Core/{BridgeConnectionHandler,ConsentText}.cs`
- Test: `tests/VsMcpDebugger.Vsix.Core.Tests/ConnectionApprovalTests.cs` (new); extend `SecurityHarness` so `AddConnection` and the harness `Handshake` accept a client folder and the service's solution path comes from `FakeDte.SolutionPath`.

**Interfaces:**
- Consumes: `FolderMatch.Trusts` (Task 2), `Operation.Connect` (Task 5), `ConsentGate.RequestAsync`.
- Produces: `ConsentText.Connect(string client, string? folder) : string` — "*client* (folder `…`) wants to control this Visual Studio" (client already escaped; folder through `Visible`, capped at 120 characters); handler behaviour: a connection whose handshake folder `Trusts` the solution is approved at handshake; any other connection asks once, on the first call that needs control, through `ConsentGate` with `Operation.Connect`. Allow once / Allow for session both approve that connection only; Deny and timeout answer `denied` (reasons `user_denied`, `timeout`).

- [ ] **Step 1: Write the failing tests** (`ConnectionApprovalTests`):
  - `A_client_in_the_solution_folder_is_approved_without_a_question`; `A_client_in_a_subfolder_is_approved_without_a_question`.
  - `A_client_in_an_unrelated_folder_is_asked_once` (two calls, `s.Prompt.Texts.Count == 1`, text contains the folder).
  - `A_client_above_the_solution_folder_is_asked` (trust is stricter than selection).
  - `A_client_with_no_folder_is_asked` (older client).
  - `No_solution_loaded_means_ask` (`FakeDte.SolutionPath = null`).
  - `Deny_answers_denied_user_denied_and_runs_nothing` (no `Dte.Calls` beyond the status read; audit entry decision `deniedUser`).
  - `Timeout_answers_denied_timeout`.
  - `Reads_need_approval_too` (`GetStatusAsync` from an unapproved connection prompts).
  - `Concurrent_first_calls_ask_a_single_question` (two simultaneous calls → one prompt).
  - `A_call_denied_by_policy_asks_no_connection_question` (Read = Deny).
  - `The_question_escapes_client_and_folder` (control and bidi characters; long folder capped).
  - `Approval_belongs_to_the_connection` (a second connection from the same folder text but unrelated is asked again).
  - `Approval_is_asked_before_the_lease_is_checked` (b unapproved while a holds the lease → question first, then `in_use` after Allow).
- [ ] **Step 2: Run to verify failure** — filter `*ConnectionApprovalTests`.
- [ ] **Step 3: Implement.** `EnsureApprovedAsync(ct)` in `Guard` right after the policy-`Deny` branch and before lease/consent: a `SemaphoreSlim` serialises the question; the decision is stored in `_approved`; the audit entry uses `Operation.Connect`'s name with `ToDecision`. Handshake computes `_approved = FolderMatch.Trusts(request.ClientWorkingDirectory, response.SolutionPath)`.
- [ ] **Step 4: Run to verify pass** — whole suite and the VSIX compile check.
- [ ] **Step 5: Commit** (10:05) `git commit -m "feat(vsix-core): an agent whose folder is not the solution's must be approved by the Visual Studio owner"`

---

### Task 10: `debug_start(project)`

**Files:**
- Modify: `src/VsMcpDebugger.Vsix.Core/{IDteFacade,DebugBridgeService,BridgeConnectionHandler}.cs`, `src/VsMcpDebugger.Vsix/DteFacade.cs`, `src/VsMcpDebugger.Mcp/Tools/ExecutionTools.cs`, `tests/VsMcpDebugger.Vsix.Core.Tests/FakeDte.cs`, `tests/VsMcpDebugger.Mcp.Tests/Fakes/FakeDebugBridge.cs`
- Test: `tests/VsMcpDebugger.Vsix.Core.Tests/StartProjectTests.cs` (new), `tests/VsMcpDebugger.Mcp.Tests/ExecutionToolsTests.cs`, snapshot

**Interfaces:**
- Consumes: `IDebugBridge.StartProjectAsync` (Task 1), `DebugBridgeService.ResolveProject`/`ProjectNames` (M3 Task 11).
- Produces:
  - `IDteFacade.StartupProject : string?` (get/set, project unique name; null when none), `IDteFacade.IsLaunchable(string uniqueName) : bool`.
  - `DebugBridgeService.StartProjectAsync(string project, CancellationToken ct) : Task<long>` — Design mode only; resolves like `vs_build`; not launchable → `invalid_argument` with the launchable project names in the hint; otherwise `original = dte.StartupProject; try { dte.StartupProject = unique; dte.Go(); } finally { dte.StartupProject = original; }`; returns the observed sequence like `StartAsync`.
  - `BridgeConnectionHandler.StartProjectAsync(string project, CallContext, CancellationToken)` — `Guard(Operation.Start, …)` with the project in the request summary and parameters.
  - MCP `debug_start(project?)`: `project` omitted → unchanged behaviour; given → `RequireMinor(MinorM31)` then `session.IssueAsync(() => bridge.StartProjectAsync(…))` inside the existing `Resume` flow (still waits for the first break).

- [ ] **Step 1: Write the failing tests**
  - `StartProjectTests`: `Named_project_is_started_with_the_startup_switched_for_the_launch` (fake `Go` observes `StartupProject == unique` at call time); `Startup_project_is_restored_after_success`; `Startup_project_is_restored_when_go_throws`; `Startup_project_is_restored_when_the_call_is_cancelled_before_and_after_the_switch` (cancellation token cancelled via a UI thread that cancels inside `Go`); `Unknown_project_is_not_found_and_lists_the_names`; `Project_that_cannot_launch_is_invalid_argument_naming_the_launchable_ones`; `Break_mode_is_wrong_mode_and_nothing_changes`; `Project_name_is_matched_case_insensitively_by_name_or_unique_name`; `No_startup_project_before_means_it_is_cleared_again` (original null → setter called with null).
  - `BridgeConnectionHandlerTests` (or `LeaseTests`): `Start_project_takes_the_lease_and_is_audited_with_the_project_name`.
  - `ExecutionToolsTests`: `Start_with_a_project_calls_start_project`; `Start_without_a_project_calls_the_plain_start`; `Start_with_a_project_against_a_2_1_extension_is_unsupported`; `Start_project_not_found_is_returned_as_not_found`.
  - `HostTests`/snapshot: the `debug_start` schema gains `project`.
- [ ] **Step 2: Run to verify failure** — whole suite.
- [ ] **Step 3: Implement.** `DteFacade.StartupProject` reads/writes `dte.Solution.SolutionBuild.StartupProjects` (object array of unique names; null/empty when none); `IsLaunchable` is true for projects whose output type is Exe/WinExe or that Visual Studio reports as startable (builder's choice of property, documented in a one-line comment). The tool description of `debug_start` mentions `project` and that the startup project is not changed.
- [ ] **Step 4: Run to verify pass** — whole suite, snapshot reviewed and accepted, VSIX compile check.
- [ ] **Step 5: Commit** (10:15) `git commit -m "feat: debug_start(project) starts the named project and leaves the startup project as it was"`

---

### Task 11: Stale-build detection

**Files:**
- Create: `src/VsMcpDebugger.Vsix.Core/StaleBuildChecker.cs`
- Modify: `src/VsMcpDebugger.Vsix.Core/{IDteFacade,DebugBridgeService,BridgeConnectionHandler}.cs`, `src/VsMcpDebugger.Vsix/DteFacade.cs`, `tests/VsMcpDebugger.Vsix.Core.Tests/FakeDte.cs`
- Test: `tests/VsMcpDebugger.Vsix.Core.Tests/{StaleBuildCheckerTests,BuildFreshnessTests}.cs`

**Interfaces:**
- Consumes: `BuildWarning` (Task 1), `Operation.BuildCheck` (Task 5).
- Produces:
  - `sealed class ProjectBuildInfo { string UniqueName; DateTime? NewestSourceUtc; DateTime? OldestOutputUtc; bool? UpToDateByVisualStudio; }`
  - `IDteFacade.GetBuildInfo(string uniqueName) : ProjectBuildInfo`, `IDteFacade.ProjectsContaining(string file) : IReadOnlyList<string>` (unique names of every project that contains the file).
  - `static class StaleBuildChecker { bool IsStale(ProjectBuildInfo info); }` — Visual Studio's own answer wins when present; otherwise stale when no output exists, or `NewestSourceUtc > OldestOutputUtc`.
  - `DebugBridgeService.CheckBuildFreshnessAsync(string? project, string? file, CancellationToken ct) : Task<BuildWarning[]>` — scope: `project` (name or unique name; matched like `ResolveProject`, an unknown name yields no warnings), else every project containing `file`, else the startup project; one warning per stale project: `Code = "stale_build"`, `Project = name`, `Message = "<name> — sources are newer than the built output; the program may not match the code. Build or rebuild before trusting breakpoints."`
  - `BridgeConnectionHandler.CheckBuildFreshnessAsync` → `Guard(Operation.BuildCheck, …)`.
  - Marker constant `BuildWarningCodes.StaleBuild = "stale_build"` in Protocol.

- [ ] **Step 1: Write the failing tests**
  - `StaleBuildCheckerTests`: `Fresh_output_is_not_stale`; `A_newer_source_is_stale`; `Missing_output_is_stale`; `An_older_pdb_makes_it_stale_even_when_the_dll_is_new` (oldest output counts); `Visual_Studio_saying_stale_wins_over_timestamps`; `Visual_Studio_saying_up_to_date_wins_over_timestamps`; `No_sources_known_and_an_output_present_is_not_stale`.
  - `BuildFreshnessTests` (service + `FakeDte` with a project table): `Named_project_is_checked`; `Unknown_project_gives_no_warning`; `File_scope_checks_every_project_that_contains_it`; `No_scope_checks_the_startup_project`; `Fresh_projects_give_an_empty_list`; `Warning_text_names_the_project_and_carries_no_paths_or_values`; `Handler_audits_nothing_for_a_read_tier_check_without_a_question`.
- [ ] **Step 2: Run to verify failure** — whole suite.
- [ ] **Step 3: Implement.** `DteFacade.GetBuildInfo`: sources = the project's compile items (`ProjectItems` walk) newest write time; outputs = the primary output assembly and `.pdb` from the active configuration's output path; `UpToDateByVisualStudio` from Visual Studio's own up-to-date check for the project's configuration when available, null otherwise (builder's choice of API; any COM failure → null via `ComSafe.TryRead`). `ProjectsContaining`: walk projects' items for the full path (case-insensitive).
- [ ] **Step 4: Run to verify pass** — whole suite and the VSIX compile check.
- [ ] **Step 5: Commit** (10:25) `git commit -m "feat: stale-build detection per project"`

---

### Task 12: Optional Rebuild bar

**Files:**
- Create: `src/VsMcpDebugger.Vsix.Core/{IRebuildPrompt,StaleBuildNotifier}.cs`, `src/VsMcpDebugger.Vsix/Ui/InfoBarRebuildPrompt.cs`
- Modify: `src/VsMcpDebugger.Vsix.Core/{IDteFacade,DebugBridgeService}.cs`, `src/VsMcpDebugger.Vsix/{DteFacade,VsMcpDebuggerPackage}.cs`, `tests/VsMcpDebugger.Vsix.Core.Tests/FakeDte.cs`
- Test: `tests/VsMcpDebugger.Vsix.Core.Tests/StaleBuildNotifierTests.cs`

**Interfaces:**
- Consumes: `StaleBuildChecker`, `DebugBridgeService.CheckBuildFreshnessAsync` (Task 11).
- Produces:
  - `interface IRebuildPrompt { void Show(string project, string text, Action rebuild); void Hide(string project); }` (called on any thread; implementations marshal to the UI thread).
  - `StaleBuildNotifier(IRebuildPrompt prompt, Func<DebuggerMode> mode, Action<string> rebuild)`: `Observe(string project, bool stale)`, `OnSessionEnded()`, `Dismissed(string project)` (called by the bar's dismiss).
  - `IDteFacade.RebuildProject(string uniqueName)` — clean + build of that project without waiting.
  - `DebugBridgeService(…, BuildMonitor? builds = null, IRebuildPrompt? rebuildPrompt = null)`; `OnEnterDesign()` also calls `notifier.OnSessionEnded()`.

- [ ] **Step 1: Write the failing tests** (`StaleBuildNotifierTests`, fake `IRebuildPrompt` recording calls):
  - `Stale_project_shows_one_bar`; `Twenty_observations_of_the_same_stale_project_show_one_bar` (Review Focus 5); `Two_stale_projects_show_two_bars`.
  - `A_fresh_observation_hides_the_bar`.
  - `No_bar_while_a_session_runs_or_is_stopped_at_a_breakpoint` (mode Run, Break).
  - `A_project_found_stale_during_a_session_gets_its_bar_when_the_session_ends`.
  - `A_project_that_became_fresh_during_the_session_gets_no_bar_afterwards`.
  - `Dismissed_bar_is_not_shown_again_until_the_project_turns_fresh_and_stale_again`.
  - `Pressing_Rebuild_invokes_the_rebuild_action_with_the_project` (the `rebuild` Action passed to `Show` calls the notifier's rebuild callback).
  - Service level: `Checking_freshness_feeds_the_notifier` (stale project → prompt shown once; later fresh → hidden; `Dte.Calls` shows no `RebuildProject` without a click); `Rebuild_click_does_not_need_the_lease` (callback path is not routed through the handler).
- [ ] **Step 2: Run to verify failure** — filter `*StaleBuildNotifierTests`.
- [ ] **Step 3: Implement** `StaleBuildNotifier` (lock-protected set of shown/dismissed/pending projects). `InfoBarRebuildPrompt` follows `InfoBarConsentPrompt`: one info bar per project with a **Rebuild** button and the normal close ×, text "*project* is out of date. Rebuild?", removed by `Hide`. `DteFacade.RebuildProject` uses Visual Studio's solution build manager rebuild of the single project (builder's choice; not unit-testable). The package builds the notifier with the info-bar prompt and passes it to the service.
- [ ] **Step 4: Run to verify pass** — whole suite and the VSIX compile check.
- [ ] **Step 5: Commit** (10:35) `git commit -m "feat: optional Rebuild bar for stale projects, hidden during a session and after a rebuild"`

---

### Task 13: Warnings in tool results

**Files:**
- Modify: `src/VsMcpDebugger.Mcp/Tools/{ToolResponse,ExecutionTools,BreakpointTools}.cs`, `tests/VsMcpDebugger.Mcp.Tests/Fakes/FakeDebugBridge.cs`
- Test: `tests/VsMcpDebugger.Mcp.Tests/{ExecutionToolsTests,BreakpointToolsTests}.cs`

**Interfaces:**
- Consumes: `IDebugBridge.CheckBuildFreshnessAsync` (Task 1/11).
- Produces: `ToolResponse.OkWithWarnings(object payload, IReadOnlyList<BuildWarning> warnings)` — the payload's JSON gains `warnings: [{code, message, project?}]` only when the list is non-empty; `debug_start`, `debug_attach` (scope = the process name; the extension maps it to a project of that name or yields nothing) and `bp_set` (scope = file) call the check first when `connections.RemoteProtocolMinor >= ProtocolInfo.MinorM31`, never block on it, and attach its result. A failed check never fails the tool, except `in_use`, which is returned as the tool's error.

- [ ] **Step 1: Write the failing tests**
  - `Start_reports_stale_build_warnings_and_still_starts` (fake returns one warning; result JSON has `warnings[0].code == "stale_build"` and `Calls` contains `Start`).
  - `Start_without_warnings_has_no_warnings_property`.
  - `Attach_by_name_checks_that_name`; `Bp_set_checks_the_file_scope` (`Calls` shows `CheckBuild::src/x.cs`).
  - `Check_is_skipped_against_a_2_1_extension`.
  - `A_failing_check_does_not_fail_the_tool` (fake throws `RemoteInvocationException` code Internal).
  - `In_use_from_the_check_is_the_tools_result` (fake throws code -32015 → `error.code == "in_use"`, `Start` not called).
- [ ] **Step 2: Run to verify failure** — whole suite.
- [ ] **Step 3: Implement** in the three tools; `FakeDebugBridge.OnCheckBuild` default returns an empty array.
- [ ] **Step 4: Run to verify pass** — whole suite; snapshot unchanged (descriptions only mention warnings if the builder adds a sentence; if so review and accept).
- [ ] **Step 5: Commit** (10:45) `git commit -m "feat: debug_start, debug_attach and bp_set report stale-build warnings"`

---

### Task 14: Documentation, version, manual checks for the owner

**Files:**
- Create: `docs/testing/m3.1-manual-e2e.md`, `scripts/e2e/m3.1-two-instances.cs`
- Modify: `README.md`, `CHANGELOG.md`, `docs/security.md`, `marketplace/overview.md`, `Directory.Build.props`, `src/VsMcpDebugger.Vsix/source.extension.vsixmanifest`, `docs/design/2026-10-12-vsmcpdebugger-m3.1-multi-instance-design.md`

- [ ] **Step 1: Version.** `Directory.Build.props` `<Version>0.4.0</Version>`; vsixmanifest `Version="0.4.0"`.
- [ ] **Step 2: README / overview / CHANGELOG** (add `## 0.4.0 — Several Visual Studios (preview)` above 0.3.0): added — instance choice by folder, approval for unrelated agents, one agent at a time with `vs_release`, `debug_start(project)`, stale-build warnings with an optional Rebuild bar; changed — protocol 2.2 (additive; a 2.1 extension answers the new tools with `unsupported`), events go to the lease holder, `vs_instances` shows availability. Overview feature list gets "Several Visual Studios, one agent each".
- [ ] **Step 3: `docs/security.md`** — new subsection "Several agents and several Visual Studios (0.4)": lease and approval live in the extension; the working directory is self-reported (it prevents mistakes, it does not stop a hostile same-user process, which is out of scope); an unrelated folder needs a one-time approval; approvals and leases die with the connection and with Disconnect AI; the `in_use` and approval texts escape agent-supplied names and folders.
- [ ] **Step 4: `docs/testing/m3.1-manual-e2e.md`** — for the owner, in the style of the M3 document: setup (two Visual Studios on two solutions, two terminals); the six success scenarios of the design (§1) as a table of action → expected; additional rows — an agent started in the repo root with the `.sln` in `src\` is chosen automatically; an agent in an unrelated folder triggers the approval bar and Deny gives `denied`; the Rebuild bar appears for a stale project, is absent during debugging, disappears after Rebuild or any up-to-date build; `debug_start(project)` leaves the startup project unchanged in Solution Explorer; `vs_release` frees the Visual Studio for the other agent; closing an agent's terminal frees it; Disconnect AI frees all. A "Run record" section left empty.
- [ ] **Step 5: `scripts/e2e/m3.1-two-instances.cs`** — a file-based driver (same style as `m3-e2e.cs`) that starts two MCP servers with different working directories (`--cwd` arguments) and prints the results of: `vs_instances`, `debug_status` from each, a state-changing call from the second agent against the first agent's Visual Studio. **It is written and compile-checked only (`dotnet build` of the file via `dotnet run` is NOT done); it is run by the owner.** Verify it compiles by running `dotnet build scripts/e2e/m3.1-two-instances.cs` if the SDK supports it, otherwise leave it unbuilt and say so in the commit body.
- [ ] **Step 6: Design status.** Set `**Status** | Implemented (feature/m3.1)`.
- [ ] **Step 7: Verify** — whole suite and the VSIX compile check — Expected: all pass; 0 warnings.
- [ ] **Step 8: Commit** (10:55) `git commit -m "docs: M3.1 security notes, README, changelog, manual checks; version 0.4.0"`

Then use superpowers:requesting-code-review for the whole branch and superpowers:finishing-a-development-branch. Merge only on the owner's word; nothing is pushed. The real-Visual-Studio run (`docs/testing/m3.1-manual-e2e.md`) belongs to the owner.
