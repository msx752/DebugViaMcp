# VsMcpDebugger

Let AI coding agents such as **Claude Code** drive the Visual Studio debugger over the
[Model Context Protocol](https://modelcontextprotocol.io): set breakpoints, start and step, read the call stack and
variables, and evaluate expressions — while you stay in control.

> Preview: the tool set is growing; feedback is welcome on the repository.

## What the agent can do

- **Breakpoints** — file/line or function, conditions, hit counts, tracepoints
- **Execution** — start, continue, pause, step into/over/out, wait for the next break
- **Inspection** — call stack, locals and arguments, drill into members, QuickWatch-style evaluation
- **Changes** — `set_value` and evaluation with side effects, **only with your approval**

## You stay in control

- **Permission tiers** — Read, Control and Mutate, each Allow / Ask / Deny, in
  **Tools → Options → VsMcpDebugger**. Visual Studio enforces them; the agent cannot change them.
- **Consent info bar** — before anything changes your program, Visual Studio asks:
  *Allow once*, *Allow for session* or *Deny*. No answer means deny.
- **Audit trail** — every state change is logged with a call id and shown in
  **View → Output → VsMcpDebugger Activity**.
- **Disconnect AI** — **Extensions → VsMcpDebugger → Disconnect AI** (Ctrl+Alt+Shift+F12) cuts the agent off at once.
- **Local only** — a per-user named pipe; no network, no telemetry.

## Get started

1. Install this extension (Visual Studio 2022 17.14+ or Visual Studio 2026).
2. Install the MCP server and register it with Claude Code:

       dotnet tool install -g VsMcpDebugger
       claude mcp add vsmcpdebugger -- vsmcpdebugger serve

3. Open your solution and ask, for example: *"Break in OrderService.Submit when total > 1000 and show me the order."*

Documentation, security model and source: [github.com/msx752/VsMcpDebugger](https://github.com/msx752/VsMcpDebugger)
