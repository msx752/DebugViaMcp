# VsMcpDebugger M3.1 — Several Visual Studios, several agents

| | |
|---|---|
| **Status** | Approved by the human partner for planning (pending final read) |
| **Date** | 2026-10-12 |
| **Milestone** | M3.1 (multi-instance, doing the right thing in the right Visual Studio) |
| **Parent specs** | [2026-10-11-vsmcpdebugger-design.md](2026-10-11-vsmcpdebugger-design.md) (§3.1 tools, §4 runtime model), [2026-10-11-vsmcpdebugger-m2-safety-design.md](2026-10-11-vsmcpdebugger-m2-safety-design.md) (tiers, consent, audit), [2026-10-11-vsmcpdebugger-m3-design.md](2026-10-11-vsmcpdebugger-m3-design.md) (tool set, protocol 2.1). Where this document is more specific, it wins. |

## 1. Intent

A developer keeps several Visual Studios open at once, for example the backend API in one and a scheduler job in
another, and runs one AI agent per Visual Studio in separate terminals. The two pieces of work are independent. Each agent
develops code in its own folder and debugs in its own Visual Studio. Today that is unsafe and clumsy:

- The pipe server already accepts any number of clients, so a second agent can connect to the same Visual Studio and
  disturb a debug session that another agent is in the middle of (breakpoints, build, selected frame, stop).
- With two Visual Studios running, an agent that does not pick one gets `ambiguous_instance`; one that picks wrongly
  controls somebody else's work.
- `debug_start` cannot choose which project to run (parent spec §3.1 lists `project?`; M1–M3 start only the startup
  project).
- A program can run stale code when only an incremental build happened (old `obj` output, missing rebuild). The agent
  then reasons about code that is not what runs, and nothing tells it so.

This milestone makes the multi-agent, multi-Visual-Studio case safe by construction: **the Visual Studio owner decides
which agent controls it, one agent at a time, and every agent is told when what it debugs is not what is on disk.**

**Success** (the acceptance scenario, also the real-Visual-Studio run in §9):
1. Two Visual Studios are open (backend, scheduler). Agent A, started in the backend folder, calls
   `debug_start(project)` without choosing an instance. Debugging starts in the backend Visual Studio, in the named
   project. The scheduler Visual Studio is untouched.
2. At the same time agent B, started in the scheduler folder, sets breakpoints and debugs in the scheduler Visual
   Studio. Neither disturbs the other.
3. If B tries to use the backend Visual Studio while A holds it (A holds it until A calls `vs_release` or disconnects), B gets a clear "not available, in use by another agent"
   error. If an agent started in an unrelated folder asks to control a Visual Studio, that Visual Studio asks its
   owner first.
4. If sources are newer than the built output, `debug_start`, `debug_attach` and `bp_set` tell the agent so, and
   Visual Studio offers an optional "Rebuild" action. Rebuilding removes the warning in both places.
5. When debugging ends, the startup project is what it was before.
6. Disconnect AI cuts every agent off in that Visual Studio at once.

**Goals**
- Pick the right Visual Studio automatically when the agent runs in (or under) the solution folder.
- Let the Visual Studio owner approve any agent that is not clearly "theirs", per connection.
- One agent at a time per Visual Studio, from its first state-changing command until it is done.
- `debug_start(project)` runs the named project without changing the solution's startup project.
- Make stale builds visible to agents and to the user.

**Non-goals**
- Several agents in one Visual Studio at the same time; a "pass the debugger from one agent to another" feature.
- Remote Visual Studio instances or any network transport (still a per-user local pipe).
- A new tool set: M3's tools stay as they are; M3.1 adds `vs_release` and the `project` argument.
- Choosing a build configuration, running tests, or code navigation (parent spec §13).

**Anti-goals** (a failure even if everything "works")
- An agent controlling a Visual Studio whose owner never agreed, or without being told who else uses it.
- An agent silently debugging stale code.
- A change to the user's solution state the user did not ask for (startup project, build outputs of unrelated
  projects). Breakpoints are Visual Studio's state, not an agent's: they stay when a lease ends and are visible to the
  next agent in `bp_list`.
- New friction in the single-Visual-Studio, single-agent case when the agent runs in the solution folder.

## 2. What exists today (facts the design builds on)

- `InstanceRecord` files in `%LOCALAPPDATA%\VsMcpDebugger\instances\<pid>.json` hold pid, pipe name, token and
  `SolutionPath`. The pipe is per-user (ACL) and accepts many clients (`MaxAllowedServerInstances`).
- Each agent runs its own `vsmcpdebugger serve` process (stdio), with one `ConnectionManager` and one `SessionState`,
  connected to one instance. `vs_instances` / `vs_select` choose the instance; with two instances and no choice,
  `ambiguous_instance`.
- The handshake carries token, protocol version and `ClientName`; it carries **no working directory**.
- Consent questions are Visual Studio info bars driven by `ConsentGate` (one question at a time, timeout = deny).
- Break/mode events are broadcast to every authenticated connection.

## 3. Design

### 3.1 Choosing the Visual Studio (MCP side)

- The MCP server learns its working directory and picks an instance automatically when **exactly one** running instance
  *relates* to it: the working directory equals the solution folder or is below it, or the solution folder is below
  the working directory (an agent started at the repository root while the `.sln` lives in `src/`). Paths are compared
  case-insensitively on Windows, after full-path normalisation. A Visual Studio with no solution, or in Open Folder
  mode, relates to nothing.
- Selecting is only convenience. Trust (§3.2) is stricter than selecting: it needs the working directory to be at or
  below the solution folder.
- No match, or more than one: no automatic choice. The error lists the candidates with their solution, whether each is
  in use (§3.3) and by whom, and says to call `vs_select`.
- `vs_select(pid)` stays available. It never bypasses §3.2: selecting a Visual Studio does not give control of it.
- An agent never controls two Visual Studios; a second `vs_select` replaces the first and releases anything held there.

### 3.2 Connection approval (Visual Studio side)

The Visual Studio owner decides which agent controls it.

- The MCP server sends its working directory in the handshake (new field, additive).
- **Folder matches** (the client's working directory equals the solution folder or is below it): the connection is
  approved silently. This is today's behaviour; the single-Visual-Studio case stays friction-free.
- **Folder does not match** (including: the working directory is above the solution folder, no solution is loaded, or
  the folder is missing because the client is older): the first call that needs control raises a Visual Studio
  info bar: "*<client name>* (folder `<path>`) wants to control this Visual Studio. Allow / Deny". It uses the existing
  consent mechanism (one question at a time, the consent timeout, timeout = deny, text escaped as in M2). Allow applies
  to that connection only and ends with it. Deny answers the call with `denied` (reason `user_denied`).
- Read tools are included: an unapproved connection can read nothing except what §3.3 allows for discovery.
- The working directory is self-reported by the client. This does not widen the trust boundary: a process running as
  the same user can already read the token (security.md, "Out of scope"). The check exists so that a mistaken or
  unrelated agent cannot control a Visual Studio by accident, not to stop a hostile same-user process.
- Disconnect AI revokes all approvals and leases in that Visual Studio.

### 3.3 One agent at a time: the lease

- A connection takes the **lease** of its Visual Studio on its first *state-changing* command: every Control- and
  Mutate-tier operation (breakpoints set/remove/enable, `vs_build`, `debug_start`, `debug_attach`, `debug_run_to`,
  stepping, `set_value`, side-effect `eval`, `frame_select`, `debug_stop`, `vs_release`). Reading without ever changing
  state does not take it.
- Reads while another agent holds the lease are refused with `in_use` too (a non-holder must not see another agent's
  locals, stack or breakpoints mid-session). Two things stay open to everyone: `vs_instances`, and `debug_status`,
  which for a non-holder returns only that the instance is in use and by whom.
- While a connection holds the lease, any other connection's state-changing command fails with the new error `in_use`
  ("This Visual Studio is in use by another agent: <client name> (folder <path>). Use another instance or retry
  later.").
- The lease ends when the holder: calls the new `vs_release` tool ("I am done"); disconnects or the MCP process ends
  (including a lost pipe: a reconnect is a new connection and starts without lease or approval, and the agent is told
  so); or the user chooses Disconnect AI. It does **not** end at `debug_stop`: the usual loop is stop, edit, build,
  start again, and another agent must not slip in between. The `vs_release` tool description tells the agent to call
  it when the work is finished. There is no idle timeout.
- The holder name and folder are shown to other agents in `vs_instances` (§3.6) and in the VS status bar indicator.
- Break/mode events go only to the lease holder (and to connections that hold nothing and ask for status).
- The user is never locked out: stepping, stopping and clicking in the Visual Studio UI always work. If the user
  starts or stops debugging by hand while an agent holds the lease, the agent's next call reports the changed state
  (`stale` / `wrong_mode` as today) and the lease follows the debug session: when the session ends, the lease ends.

### 3.4 `debug_start(project?)`

- `project`: a project name as in `vs_build` (name or unique name; not found lists the names). Omitted: the startup
  project, as today.
- The solution's startup project is **not changed permanently**. How the project is started without that is the
  builder's choice: either the startup project is switched for the launch and restored in a `finally` before returning,
  or the project is launched through Visual Studio's debug-launch interface directly. The result, not the mechanism,
  is the requirement (§1, success 5): after the call, and after any failure or cancellation, the startup project is
  what it was.
- Tier: Control, as `debug_start` today. The consent text names the project when one is given.
- `project` for a project that cannot be launched (class library) fails with `invalid_argument` and a hint naming the
  launchable projects.

### 3.5 Stale-build awareness

- Before `debug_start`, `debug_attach` (target's project, when it can be mapped) and `bp_set`/`bp_enable` on a file
  that belongs to a project, the extension checks whether the project's built output is older than its sources
  (or missing). The signal is the builder's choice; Visual Studio's own up-to-date check for the project is preferred,
  timestamps of compile inputs against the output and its `.pdb` are the fallback.
- Result for the agent: the response gains a `warnings` entry, e.g. `stale_build: OrderDemo — sources newer than the
  built output; the running program may not match the code. Build or rebuild before trusting breakpoints.` The call is
  **not** blocked or delayed.
- Result for the user: a Visual Studio info bar "*OrderDemo* is out of date. Rebuild?" with a **Rebuild** button and a
  dismiss. It is optional and nothing waits for it. Rebuild means clean + build of that project (not an incremental
  build), because the problem to cure is a stale `obj`.
- The Rebuild bar is not shown while a debug session is running (rebuilding a project whose output is locked by the
  running program would fail or break the session) and appears after the session if the output is still stale. The
  Rebuild button is a user action: it is exempt from leases and approvals.
- The file-to-project mapping uses every project that contains the file (a file linked into several projects checks
  all of them). The check is cached per project for a few seconds and the info bar is shown once per project, so
  setting twenty breakpoints raises at most one bar per project.
- Both disappear once the check passes again (after a Rebuild, or after any build that brings the output up to date).
  If an incremental build was done but the stale output remains detectable (§ builder's signal), the warning stays.
- Warnings are part of the agent's text channel and carry no program values.

### 3.6 Discovery

- `vs_instances` lists, per instance: pid, solution, and **availability**: `free`, or `in_use` with the holder's
  client name and folder. Availability is read from the instance record file, which the extension rewrites when the
  lease changes (so listing does not require connecting to, or being approved by, any Visual Studio).
- `debug_status` includes the lease holder when called by the holder.

## 4. Protocol 2.2 (additive)

- `HandshakeRequest.ClientWorkingDirectory` (optional string).
- `BridgeErrorCode.InUse` (new value outside StreamJsonRpc's reserved -32000..-32003; the builder picks the number
  and pins it with the existing wire-code test).
- Tool `vs_release` (MCP) and a matching bridge method; `debug_start` gains `project`; call results may carry
  `warnings` (array of `{code, message}`).
- `InstanceRecord` gains `LeaseHolder` and `LeaseHolderFolder` (nullable).
- A 2.1 extension keeps working with a 2.2 tool: the new fields are ignored, `vs_release` and `project` return
  `unsupported` with the update hint (the M3 mechanism). A 2.1 tool against a 2.2 extension: no working directory,
  so any connection is treated as "folder missing" (§3.2) and asks once; leases apply as for any client.

## 5. Security notes

- Same-user local processes stay outside the threat model (security.md). What this milestone adds is protection from
  mistakes and from an agent reaching the wrong Visual Studio, plus owner consent for unrelated agents.
- Approval and lease state live in the extension (the authority), never in the MCP process. A modified MCP cannot
  grant itself either.
- Approvals and leases are per connection and die with it. Nothing is persisted across Visual Studio restarts.
- Consent texts and `in_use` messages show client names and folders; both are agent-supplied, so they are escaped and
  length-capped exactly like M2 client names.
- Audit: approval decisions, lease acquired/released (and why), `vs_release`, `in_use` refusals are audit entries
  with the call id, like other state changes.
- No new data leaves the machine; no new files beyond the extended instance record.

## 6. Decisions

**Made with the human partner**
- Two independent Visual Studios, one agent each, separate terminals; the agent normally runs in the solution folder
  but that cannot be guaranteed.
- A busy Visual Studio refuses a second agent with a clear "not available" error; one agent at a time.
- The lock covers the preparation phase (breakpoints, build) as well as the debug session, and is released by a
  `vs_release` call, disconnect (terminal closed, pipe lost), or Disconnect AI; not by `debug_stop` and not by an idle
  timeout.
- Folder match connects silently; any other agent needs a one-time approval click in that Visual Studio.
- `debug_start(project)` never changes the startup project permanently.
- Stale builds: warn the agent (not blocking) and offer an optional Rebuild bar in Visual Studio; both vanish after a
  rebuild.

**My calls (change them if you disagree)**
- Non-holders cannot read a leased Visual Studio (only `vs_instances` and a minimal `debug_status`); a lost pipe is a
  new connection with no lease or approval (no grace period); breakpoints stay when a lease ends; selecting by folder
  is broader than trusting by folder (§3.1); the Rebuild bar is suppressed during a session.
- The lease is taken by state-changing commands only; the signal for staleness is Visual Studio's up-to-date check
  first, timestamps second; `vs_instances` reads availability from the instance file; events go only to the holder;
  protocol 2.2.
- The approval prompt appears on the first call that needs control, not at connect (the MCP connect timeout is 5 s
  and a prompt can take a minute).

**Left to the builder:** mechanism for the temporary start of a non-startup project, the staleness signal and its
fallbacks, file/format of the extended instance record, how the status bar shows the holder, exact error numbers and
texts, how `warnings` are rendered per tool.

## 7. Carry-over from M3 (to be sized in the plan)

M3's real-Visual-Studio run left architectural work that multi-instance makes more valuable: a single place that maps
COM/HRESULT failures and re-checks the mode, event-based settling instead of polling (breakpoint binding, attach, stop),
the MCP mirror no longer acting as the authority, a deadline for commands blocked by a modal dialog, and three small
fixes (disabled/tracepoint breakpoint with `debug_run_to`, `bp_set` on an armed run-to location, an untrusted-text note
on thread names). See the M3 ledger rulings and the backlog memory. This document does not require them; the plan
decides how much rides along.

## 8. Out of this milestone

Marketplace packaging and the custom Options page; M4 (`setup`/`doctor`, CI); M5 (VS Code adapter).

## 9. Verification

- Unit tests with fakes for: instance choice by folder (one, none, several, case, parent/child paths), approval flow
  (match, mismatch, missing folder, deny, timeout, Disconnect AI), lease (acquire on first state-changing command,
  `in_use` for the second, all release paths, user-driven stop), `debug_start(project)` (named, unknown, not launchable,
  startup project restored after success, failure and cancel), staleness (fresh, stale, missing output, warning
  cleared after rebuild), `vs_instances` availability, protocol 2.1↔2.2 combinations.
- A real run with **two Visual Studio instances** and two MCP clients exactly as in §1 "Success". The run must not
  disturb the person at the keyboard: drive Visual Studio through its automation interface and answer consent through
  the documented test helper only when the owner has agreed to a moment for it, never steal focus in the middle of
  their work, and ask before closing or restarting any Visual Studio.
