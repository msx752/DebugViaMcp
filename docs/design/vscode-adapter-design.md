# Debugging via MCP — Core and platform layers, and the VS Code adapter

| | |
|---|---|
| **Status** | Draft — awaiting review |
| **Date** | 2026-10-18 |
| **Scope** | Makes the product a platform-neutral core plus thin platform layers, and adds a VS Code adapter (Windows, macOS, Linux) next to the Visual Studio 2026 one. Extends `design.md` §2.4 and ADR-007/ADR-008; replaces the "M5" placeholder |

## 1. Intent

Today the product only drives Visual Studio. The owner wants it to drive the debugger of **Microsoft's Visual Studio Code**
as well, so that an agent uses the same tools whichever IDE is behind them. The owner's rule for the structure: **a core that
knows nothing about any IDE or operating system, and platform layers on top of it.** Each layer may be replaced or added
without touching the core.

Why now: the extension already carries the MCP server and a fixed launcher, the wire format is language-neutral by design
(`design.md` §2.4), and the Visual Studio-only parts are known. Doing the split before the first public release is cheaper than
after.

### Goals
- One MCP server and one wire protocol for every IDE. An agent's tool calls do not change with the IDE.
- A VS Code extension (TypeScript, its own package) that implements the bridge contract with the `vscode.debug` API and the
  Debug Adapter Protocol, for .NET (C#) first.
- The safety model is identical in both IDEs: permission tiers, consent prompts, audit trail, one agent per IDE, Disconnect AI.
- What an IDE cannot do is said so (a capability list in the handshake), never faked or silently ignored.
- The Visual Studio extension keeps working unchanged for users while the split happens.

### Non-goals
- VS Code forks (Cursor, VSCodium, and others). The C# debugger (`vsdbg`) is licensed for Microsoft's own products only.
- Visual Studio for Mac (retired), remote machines, HTTP transport, languages other than .NET in the first VS Code release.
- A shared implementation of the safety logic across C# and TypeScript (see §4.3: shared tests instead).

### Anti-goals (a failure even if it works)
- Weaker safety in one IDE than in the other.
- The MCP server having to know which IDE it talks to, beyond what the capability list says.
- A change to the Visual Studio extension that regresses what already works (the existing 659 tests and the manual run stay green).

## 2. The layers

```
 Agent (any MCP client)
        │ stdio (MCP)
 ┌──────▼────────────────────────────────────────────┐
 │ CORE: MCP server (DebugViaMcp.Mcp) + Protocol      │  no IDE types, no OS-specific code paths
 └──────┬────────────────────────────────────────────┘
        │ JSON-RPC 2.0 over a local pipe/socket + instance file   (the bridge contract, IDebugBridge)
 ┌──────▼──────────────┐   ┌──────────────────────────┐
 │ Visual Studio layer │   │ VS Code layer            │   platform layers: one per IDE
 │ (C#, in-process)    │   │ (TypeScript extension)   │
 └─────────────────────┘   └──────────────────────────┘
        OS layer inside the core: where files live, how the server is launched, pipe vs. Unix socket
```

Two kinds of layer exist, and they are separate:
- **IDE layer** (Visual Studio, VS Code): turns the bridge contract into that IDE's debugger calls and enforces safety.
- **OS layer** (Windows, macOS, Linux): data folder, launcher script, local transport, process permissions.

### 2.1 What is core today and what is not

| Part | Where it is now | In the new structure |
|---|---|---|
| Wire contract (`IDebugBridge`, DTOs, errors, events, JSON conventions) | `DebugViaMcp.Protocol` | **Core**, unchanged. Gains a capability list (§3) |
| MCP tools, instance discovery and selection, session state, output shaping | `DebugViaMcp.Mcp` | **Core**. Reads the capability list; no other IDE knowledge |
| Per-OS data folder | `DiscoveryPaths` / `WindowsProfile` (already per OS) | **OS layer**, already in place |
| Permission tiers, expression policy, consent, audit, lease, access switch | `DebugViaMcp.Vsix.Core` (C#) | Stays in the **IDE layer** of each IDE (§4.3) |
| Bridge service over `IDteFacade` | `DebugViaMcp.Vsix.Core` | **Visual Studio layer** (its facade is the VS seam) |
| Build, Output panes, Error List, startup project, build freshness | `IDebugBridge` methods | **Optional capabilities** (§3) |
| Launcher `debugviamcp.cmd` | Windows only | **OS layer**: `.cmd` on Windows, `.sh` on macOS and Linux |

The Visual Studio side is already shaped like this (`Vsix.Core` over `IDteFacade`, architecture tests for the dependency
rule); the new work is the capability list, the shared tests and the VS Code layer.

## 3. Capabilities

The handshake response gains a list of capability names. The protocol version goes up by one minor (additive; an older
server or IDE layer without the list means "all capabilities", as today).

- **Core debugging (every IDE must offer):** breakpoints (file/line, condition, hit count, log message, enable/disable),
  start/stop/continue/pause, step, run to line, threads, stack, frame selection, variables, evaluate, set value, exception,
  attach, wait for break.
- **Optional (an IDE may lack them):** `build` (build the solution), `startProject` (start a named project), `buildFreshness`
  (out-of-date check), `outputPanes` (read IDE output), `errorList`.

The MCP server registers a tool only when its capability is present, or answers `unsupported` with a hint that names the IDE.
Options an IDE cannot honour (for example a breakpoint condition kind) return `invalid_argument` with an explanation
(already the rule in `design.md` §14). Two Visual Studio-only tools (`vs_build`, `vs_instances` wording) keep their names; naming
is the builder's call (§7).

## 4. The VS Code layer

A separate VS Code extension, written in TypeScript, published to the VS Code Marketplace, in this repository under
`src/vscode/` with its own tests. It speaks the same wire protocol, writes the same instance file, and starts the same
named-pipe/Unix-socket server as the Visual Studio layer.

### 4.1 How each core operation maps (to be confirmed by the spike, §6)

| Bridge operation | VS Code mechanism (author's assessment) |
|---|---|
| Breakpoints | `vscode.debug.addBreakpoints` / `removeBreakpoints` with `SourceBreakpoint` and `FunctionBreakpoint` (condition, hit condition, log message); bound state from the adapter's breakpoint events |
| Start / attach / stop | `vscode.debug.startDebugging` (launch or attach configuration) / `stopDebugging` |
| Continue, pause, step | `DebugSession.customRequest` with the DAP requests `continue`, `pause`, `next`, `stepIn`, `stepOut` |
| Threads, stack, scopes, variables | `customRequest`: `threads`, `stackTrace`, `scopes`, `variables` |
| Evaluate, set value | `customRequest`: `evaluate`, `setVariable` / `setExpression` |
| Exception | `customRequest`: `exceptionInfo` |
| Mode and "stopped" events | `registerDebugAdapterTrackerFactory` (sees `stopped`, `continued`, `terminated`) and the `onDidStartDebugSession` / `onDidTerminateDebugSession` events |
| Build (optional) | VS Code tasks (`vscode.tasks`); a build task must be configured, otherwise the capability is off |
| Error List (optional) | `vscode.languages.getDiagnostics` |
| Output panes | No API found to read another extension's output channel: capability off |

Known unknowns the spike must settle: whether the .NET debugger's `evaluate` can be made side-effect-free (the Visual Studio
layer has a policy for this, see `expression-side-effects-spike.md`); how "start debugging" picks a launch configuration when
`launch.json` has several; how reliably a stopped event arrives for the same sequence numbers the protocol expects.

### 4.2 Debugger and licence
Microsoft's VS Code with the C# extension (`vsdbg`, debug type `coreclr`). The licence of `vsdbg` limits it to Microsoft's
own products, which is the target. The extension checks at start that a .NET debug type is available and says so in its
status when it is not.

### 4.3 Safety is enforced inside each IDE layer, not in the core
The MCP server is started by the agent's configuration; the pipe is protected by a per-IDE token and an OS-level restriction
to the current user. Enforcing permissions in the core would let anything that reaches the pipe bypass them, so the check
stays in the process the user trusts: the IDE extension (`safety-design.md`).

Consequence: the TypeScript layer re-implements the same rules (tiers and their defaults, the expression policy, consent with
the same prompt texts and outcomes, audit trail, one holder per IDE, Disconnect AI). To keep the two implementations equal,
the rules are written down as **shared test vectors** (JSON files in `tests/conformance/`: input call, settings, consent
answer, expected outcome and audit line). The C# tests and the TypeScript tests both run all vectors. A rule changes by
changing a vector, so a mismatch fails one side's build.

### 4.4 Packaging
- One VS Code extension package per target platform (`win32-x64`, `win32-arm64`, `darwin-x64`, `darwin-arm64`,
  `linux-x64`, `linux-arm64`), each carrying a **self-contained** build of the MCP server for that platform, so no .NET
  runtime is needed. (Author's call; the Visual Studio package keeps the framework-dependent server and its .NET 10 runtime
  requirement.)
- First start copies the server to the OS data folder and writes the launcher, as the Visual Studio extension does, with
  the same properties: a launcher path that never changes, copies named by content, old copies removed when unused.
- The launcher never runs a program found through the agent's current directory (the Windows `.cmd` already guards this;
  the `.sh` calls the server by absolute path).
- The "Set Up AI Agent" window exists in VS Code too, with the same client list and texts.

## 5. Changes in the existing code (no behaviour change for Visual Studio users)

1. Protocol: capability list in `HandshakeResponse`; `ProtocolInfo.Minor` + 1; golden-JSON test updated.
2. MCP server: register optional tools by capability; neutral wording in hints.
3. Visual Studio layer: declares its capabilities (all of them).
4. OS layer: launcher and deployer gain the `.sh` variant and the per-OS paths for the server copy (the data folder logic exists).
5. Conformance vectors extracted from the existing C# tests of the safety logic; the C# tests read them.
6. Architecture tests: a rule that Core projects reference no IDE or OS-specific assembly.

## 6. Order of work

1. **Spike (throwaway):** a minimal VS Code extension that sets a breakpoint, starts a .NET program, receives the stop,
   reads stack and variables, evaluates an expression, through the real pipe and the real MCP server. Settles the unknowns in §4.1.
2. Core changes (§5) with the Visual Studio layer green.
3. Conformance vectors and the TypeScript safety layer.
4. The VS Code layer in full, per the spike's findings.
5. Per-platform packaging, the `.sh` launcher, docs, a real run on Windows, macOS and Linux.

## 7. Left to the builder
- Names of the capability flags and of any VS Code-specific tool wording.
- The exact protocol minor and field names.
- The TypeScript toolchain (bundler, test runner, lint) and the extension's folder layout.
- How the extension discovers the debug type and reports a missing C# debugger.

## 8. Decisions and who made them

| Decision | By |
|---|---|
| Target: Microsoft's VS Code and Visual Studio 2026 only; forks out of scope | Owner |
| Core plus platform layers; first release for .NET (C#) | Owner |
| Separate VS Code package | Owner |
| `.cmd` is Windows-only, so macOS/Linux need their own launcher | Owner (raised), author (`.sh`) |
| Safety enforcement stays in each IDE layer; shared test vectors instead of shared code | Author's call |
| Self-contained server per platform in the VS Code package | Author's call |
| Capability list in the handshake, optional tools by capability | Author's call |
| Spike first, then the rest | Author's call |

## 9. Review outcome

An independent builder read this document and asked eight questions. The owner decided three; the rest are the author's calls.

### 9.1 Decided by the owner
- **Protocol shape.** Additive only: optional new fields, no redefinition of existing ones. Where VS Code differs (selected frame,
  epoch, path-to-variable-reference, attach by process id), the VS Code layer keeps its own state and emulates the Visual Studio
  semantics. The Visual Studio layer and its tests stay untouched.
- **Starting.** `debug_start` accepts an optional launch configuration name. Without one: a single configuration is used; several
  give an error that lists their names; none gives an error that says what to do. The extension never creates or edits the
  user's `launch.json`.
- **Spike outcome.** If the spike shows that a core capability cannot be offered safely (for example no reliable "stopped" event, or no
  side-effect-free evaluation), the VS Code release is deferred. The core changes and the capability list are still delivered.

### 9.2 The author's calls (the owner may overrule)
- **Conformance vectors** cover the pure policy and decision objects (tier, expression policy, consent outcome, audit line), not whole bridge
  calls. Evaluation-dependent outcomes may differ per IDE; the tier rules may not. The Visual Studio logic is refactored only as far as
  needed to expose these decisions to the vectors.
- **Consent in VS Code** is a non-modal warning message with the same buttons and the same timeout as the Visual Studio info bar; no answer
  means deny.
- **Unix.** The .NET side already uses Unix domain sockets for named pipes; the VS Code layer listens on a socket file in the data folder with
  mode 0600 and the same token. The data folder logic exists per OS but has only been exercised on Windows; macOS and Linux runs are part of step 5.
- **Instance file** gains optional `ide` (name) and `workspace` fields (a protocol minor change; add to §5). `vs_instances` shows the IDE.
- **Several windows.** Each VS Code window is its own instance and its own holder. A Visual Studio and a VS Code window open together are
  selected between by the existing folder matching; `vs_select` overrides.
- **Release scope.** Buildable per-platform VSIX files and a CI build matrix. Publishing to the Marketplace and macOS signing are out of
  scope until the owner asks; whether the unsigned bundled server runs on a clean macOS is a step 5 check.

### 9.3 Start must never hang on a popup (added requirement)
Visual Studio can open a modal question when debugging starts (for example about build errors), and VS Code can show one when a pre-launch task
fails. Today the Visual Studio layer only reports `busy` while its UI thread is blocked. Required for both IDEs:
- `debug_start` / `start_project` first check for build errors and for an out-of-date build, and report them (with the first errors) instead of
  starting into a prompt; an agent can then fix the code and call `vs_build`.
- If a modal prompt still blocks the IDE, the tool says so in plain words (an IDE prompt is open, close it) and names the likely cause.
- Exceptions are already readable (`exception` tool, break reason `exception`); VS Code maps to DAP `exceptionInfo` (§4.1).
