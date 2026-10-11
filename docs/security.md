# Security

VsMcpDebugger lets an AI agent drive the Visual Studio debugger. This page explains what can go wrong and which
controls stand in the way.

## Threat model

There is no network surface: the MCP server talks to Claude Code over stdio and to Visual Studio over a local named
pipe. The relevant threats are:

1. **Another local user** connecting to the pipe.
2. **Prompt injection.** Data read from the program being debugged (a string variable, a file name, an exception
   message) can contain instructions aimed at the agent, steering it into changing program state or running code.
3. **The user not noticing** what the agent is doing.

Out of scope: malicious processes running as the same user. They can already control Visual Studio directly.

## Controls

### Transport
- The pipe's ACL allows only the current user and explicitly denies the NETWORK SID. The pipe name is random.
- A per-instance token lives in a discovery file only the current user can read; it is checked at handshake.
- The MCP server verifies that the pipe is served by the expected Visual Studio process before sending the token.

### Permission tiers
Every request belongs to a tier. Each tier is set to **Allow**, **Ask** or **Deny** in
**Tools → Options → VsMcpDebugger → General**, and Visual Studio enforces it — no tool call can change it.

| Tier | Operations | Default |
|---|---|---|
| Read | status, threads, call stack, frame selection, variables, side-effect-free evaluation, the current exception, list breakpoints, Output window, Error List | Allow |
| Control | breakpoints without conditions (set, remove, enable/disable), start, stop, continue, pause, step, run to, build | Allow |
| Mutate | `set_value`, `eval` with `allowSideEffects`, breakpoint conditions and tracepoint messages, `debug_attach` | **Ask** |

Breakpoint conditions and tracepoint `{…}` placeholders are in the Mutate tier because they run inside your
program on every hit, and a property getter can run arbitrary code. Side-effect-free evaluation uses the C# `, nse`
specifier, which discards side effects.

Only *applied* settings count: values you are still editing in the Options dialog have no effect until you press
OK or Apply. Changing a permission clears every "Allow for session" grant. Setting Mutate to Allow asks for
confirmation first.

### IDE state and attach (0.3)

- `output_read` and `errors_list` are **Read** tier. They expose text that build tools, analyzers and your program
  wrote. If your build output can contain secrets, set Read to Ask or Deny. Their content is returned as untrusted
  data, never as instructions.
- The extension's own Output panes (**VsMcpDebugger** and **VsMcpDebugger Activity**) are never readable through the
  bridge, so the agent cannot read the audit trail or be fed by it. The check is by pane GUID.
- `debug_attach` is **Mutate**: attaching lets Visual Studio read and change the target process's memory. Only
  processes running as your own user (compared by token SID) are offered, and the consent question names the
  process and pid. `debug_stop` detaches; it does not end the process.
- Visual Studio itself (`devenv`) and the `vsmcpdebugger` server are never offered as attach targets: another
  Visual Studio instance holds its own pipe token and your sources.
- `bp_enable(true)` on a breakpoint with a condition or tracepoint is Mutate for the same reason as `bp_set` with one.
- `vs_build` is **Control**: it runs your project's build, including any custom MSBuild targets.

### Consent
With **Ask**, Visual Studio shows an info bar on the main window, for example:

> claude-code wants to set total = 0 in OrderDemo.OrderService.Submit — **[Allow once] [Allow for session] [Deny]**

- No answer within the consent timeout (default 60 s, 15–300 s) means **deny**. Closing the bar with ✕ means deny.
- The text shows the expression and value **in full**. Line breaks, bidi overrides, zero-width and other invisible
  characters, stray combining marks and literal backslashes are shown as escapes, so the text you approve is the
  text that runs. Expressions and values longer than 500 characters are refused before you are asked.
- Allow clicks within the first second after the bar appears are ignored, so a click aimed at something else cannot
  approve a request. Deny always works.
- "Allow for session" covers one kind of operation (for example only `set_value`) on the current connection.
- Requests that would fail anyway (wrong debugger mode, invalid target) fail before you are asked. Requests the
  settings deny never touch Visual Studio.
- An answer that arrives after the timeout, after the agent cancelled, or after Disconnect AI does not count.

### Visibility and audit
- Every Control and Mutate call — and every Read call you were asked about — is written to
  `%LOCALAPPDATA%\VsMcpDebugger\logs\audit-yyyyMMdd.log` (JSON lines) with its `callId`, client, parameters,
  decision and outcome. The same `callId` appears in the MCP server's log.
- Each entry also appears in **View → Output → VsMcpDebugger Activity**.
- Values read from your program are never written to any log.
- The status bar shows which client is connected; click it to open the Activity pane.

### Disconnect AI
**Extensions → VsMcpDebugger → Disconnect AI** (Ctrl+Alt+Shift+F12) immediately:
- denies every pending consent question,
- refuses every further call on existing connections and closes them,
- refuses new connections until **Reconnect AI** or until Visual Studio restarts.

To start every Visual Studio session disconnected, turn off *Accept AI connections when Visual Studio starts*.

## What does not protect you

Tool descriptions tell the agent that values read from your program are untrusted data, not instructions. **This
does not stop prompt injection**; a model can still be talked into things. The consent question for the Mutate tier
is the real control. If you set Mutate to **Allow**, you remove it.

Reading data is not gated by default (Read = Allow). If the program you debug holds secrets, the agent can read
them; set Read to Ask or Deny for such sessions.

## Reporting a vulnerability

Until the repository is public, please contact the maintainer directly.
