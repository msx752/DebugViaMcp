# Security

Debugging via MCP lets an AI agent drive the Visual Studio debugger. This page explains what can go wrong and which
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
**Tools → Options → All Settings → Debugging → Debugging via MCP** (Visual Studio 2022 17.14: **Tools → Options → Debugging via MCP → General**), and Visual Studio enforces it — no tool call can
change it. The values live in your user settings file, so anything that can write that file can edit them; a stored
**Mutate = Allow** therefore counts only after you confirm it in a dialog (also at startup), and Ask is enforced until then.

| Tier | Operations | Default |
|---|---|---|
| Read | status, threads, call stack, frame selection, variables, side-effect-free evaluation, the current exception, list breakpoints, the processes you can attach to, symbol status, `exception_list`, Output window, Error List | Allow |
| Control | breakpoints without conditions (set, remove, enable/disable), start, stop, continue, pause, step, run to, build, Exception Settings (`exception_set`, `exception_reset`) | Allow |
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
- The extension's own Output panes (**Debugging via MCP Log** and **Debugging via MCP Audit**) are never readable through the
  bridge, so the agent cannot read the audit trail or be fed by it. The check is by pane GUID.
- `debug_attach` is **Mutate**: attaching lets Visual Studio read and change the target process's memory. Only
  processes running as your own user (compared by token SID) are offered, and the consent question names the
  process and pid. `debug_stop` detaches; it does not end the process.
- Visual Studio itself (`devenv`) and the `debugviamcp` server are never offered as attach targets: another
  Visual Studio instance holds its own pipe token and your sources.
- `bp_enable(true)` on a breakpoint with a condition or tracepoint is Mutate for the same reason as `bp_set` with one.
- `exception_set` and `exception_reset` are **Control**: they change a setting of Visual Studio (which exceptions it stops on), not the program. Only what the agent changed is put back, by `exception_reset` or automatically when the agent lets go or disconnects; your own settings are never reset.
- `vs_build` is **Control**: it runs your project's build, including any custom MSBuild targets.

### Sensitive values are masked (0.5)

Values of your program travel to the AI agent and its provider. **Mask sensitive values** (Tools → Options → All Settings → Debugging
→ Debugging via MCP, on by default) hides the ones that look like secrets before they leave Visual Studio:

- **By name:** a string under a name such as `password`, `secret`, `token`, `apiKey`, `credentials`, `connectionString`,
  `authorization` or `privateKey` (also inside an expression such as `config.Password`).
- **By shape:** a JWT, `Bearer …`, a PEM private key, a GitHub or AWS key, `Password=…` and similar parts of a
  connection string, and the password in `https://user:password@host`.

It covers `vars`, `eval`, the arguments in a break, the exception message, stack trace and inner exceptions, and `output_read`.
A masked value reads `[masked]` and the result carries `masked: true`; Visual Studio still shows you the real value.

A name counts by its words, so `config.Password`, `password, nq` (a format specifier that drops the quotes), `password[0]` (one
character), `pass\u0077ord` (a unicode escape in the identifier, `\U` with eight digits too) and the members of `credentials` are all treated as sensitive.
An expression built on such a name (`password.GetHashCode()`, `password == "abc"`, `password.StartsWith("a")`, a tuple or object holding it) is masked whatever its
type, because it would tell the agent about the secret one answer at a time; `password.Length` and `credentials.Count` (read straight off the name, not after a call) stay readable, as
do plain numbers and flags under a sensitive name such as `tokenCount` or `hasPassword`.
A tracepoint (`bp_set` with a message) that would print such a value, for example `{password}`, is refused while the mask is on,
because its output would reach the agent through `output_read`.

**What it is not.** It is a heuristic that lowers the chance of an accidental leak. It is not a security boundary against an
agent that is determined, or steered by text inside the program (prompt injection): a secret copied into a variable with an
innocent name, or learned bit by bit from yes/no questions about a copy under an innocent name, is not stopped. What stops that are the
permission tiers and the consent questions: set Read to Ask or Deny for data you must never share.

Turning the setting off lets such values reach the agent. Visual Studio asks you to confirm it (also when the value was
written to the settings file while Visual Studio was closed) and keeps masking until you do. The change is audited.

### Several agents and several Visual Studios (0.4)

- Approval and the one-agent-at-a-time lease live in the extension, inside each Visual Studio. The MCP server only
  chooses an instance and reports.
- The agent's working directory is **self-reported**. It prevents mistakes (an agent landing in the wrong Visual Studio);
  it does not stop a hostile process of the same user, which is out of scope (see below).
- An agent whose folder is not the solution's folder or below it needs a one-time approval from the owner, on its first
  call. Allow once and Allow for session both approve that connection only; Deny and no answer refuse.
- The lease is taken by the first state-changing command and freed by `vs_release`, a closed connection or
  Disconnect AI. While it is held, other agents get `in_use` for reads too.
- Approvals and leases die with the connection and with Disconnect AI; a reconnecting agent starts again.
- The client name is **self-reported** by the MCP client (for example `claude-code`); any client can choose any name. It is
  shown to you, never used to decide anything. Authentication is the per-user token and the per-user pipe.
- A consent question that appears while Visual Studio is in the background makes its taskbar button flash. No answer within
  the timeout means deny.
- Client names, folders and project names supplied by an agent are escaped and length-capped wherever the owner or
  another agent sees them (consent questions, `in_use` messages).

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
  `%LOCALAPPDATA%\DebugViaMcp\logs\audit-yyyyMMdd.log` (JSON lines) with its `callId`, client, parameters,
  decision and outcome. The same `callId` appears in the MCP server's log.
- Each entry also appears in **View → Output → Debugging via MCP Audit**.
- Values read from your program are never written to any log.
- The status bar shows which client is connected; click it to open the Audit pane.

### Disconnect AI
**Extensions → Debugging via MCP → Disconnect AI** (Ctrl+Alt+Shift+F12) immediately:
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
