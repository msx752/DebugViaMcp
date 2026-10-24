# Marketplace listing texts

Copy these into the fields of the Visual Studio Marketplace upload page. The long description is
[overview.md](overview.md); the extension's own manifest (`source.extension.vsixmanifest`) already carries name,
description, tags, icon (`Resources/icon.png`), preview image (`Resources/preview.png`) and the Preview flag.

## Name

Debugging via MCP

## Short description (the one-line summary under the name; the manifest says the same)

Lets AI coding agents such as Claude Code drive the Visual Studio debugger over MCP: breakpoints, stepping, call
stacks, variables and evaluation — with a consent prompt before anything changes your program.

## Even shorter (social posts, search snippets, 160 characters)

Let Claude Code and other AI agents debug in Visual Studio: breakpoints, stepping, variables. You approve every change.

## Categories

Type: Tools. Categories (1 to 3, from the Marketplace list; there is no *Debugging* category): **Coding**, **Testing**
(already in `publishManifest.json`). "Debugging" lives in the tags instead.

## Tags / keywords

debugger, debugging, mcp, ai, agent, claude, claude code, llm, breakpoints

(The Marketplace allows 80 characters in total for tags, commas included; this list is 66. The manifest `Tags` field
carries the same words.)

## Images, in this order

| # | File | Purpose |
|---|---|---|
| 1 | `images/how-it-works.png` | Explains the idea and the three permission tiers (schematic, not a screenshot) |
| 2 | your screenshot of the consent info bar | Shows the "you stay in control" claim — see [screenshots.md](screenshots.md) |
| 3 | your screenshot of the Options page | Permission tiers |
| 4 | your screenshot of the Audit pane | The audit trail |
| 5 | `images/several-visual-studios.png` | Several Visual Studios, one agent each (schematic) |

Icon and preview image are inside the VSIX; the gallery images above are uploaded on the page (or linked from the
overview with raw GitHub URLs once the repository is public).

## What's new in this preview (release notes box)

0.7.0 — Watch list, long values, concurrency. New tools `watch_add`, `watch_list` and `watch_remove`: the agent follows
expressions and every stop answer carries their values and what changed. `eval` with `offset` and `length` reads a long value
in pages. Stops with several threads no longer fail when a frame has no source line, and blocked threads answer clearly.
Since 0.6.0 nothing else changed; Visual Studio 2022 has its Options page (Tools → Options → Debugging via MCP → General). This
is a preview: feedback is welcome on GitHub.

## Questions and answers (seed the Q&A tab)

**Does it work with other agents?**
Any MCP client can use the `debugviamcp` server. It is tested with Claude Code.

**Can the agent change my program without asking?**
Not by default. Reading and debugging control are allowed; anything that changes program state (`set_value`,
evaluation with side effects, attaching, breakpoint conditions that run code) asks you first. You can set each tier
to Allow, Ask or Deny in Tools → Options → All Settings → Debugging → Debugging via MCP (Visual Studio 2022: Tools → Options → Debugging via MCP → General). No answer means deny.

**Is anything sent over the network?**
No. The extension and the tool talk over a per-user local named pipe. There is no telemetry.

**How do I cut an agent off?**
Extensions → Debugging via MCP → Disconnect AI (Ctrl+Alt+Shift+F12). It denies pending questions and closes every
connection at once; Reconnect AI re-enables it.

**Which Visual Studio versions?**
Visual Studio 2022 17.14 and later, including Visual Studio 2026.

**Where is the MCP server?**
Inside the extension: when Visual Studio starts it copies the server to `%LOCALAPPDATA%\DebugViaMcp`, so there is no
separate tool to install (it needs the .NET 10 runtime). Open Extensions → Debugging via MCP → Set Up AI Agent… and
copy the command shown for your client.

**Two Visual Studios are open. Which one does the agent use?**
The one whose solution folder matches the folder the agent runs in. If that is ambiguous, the agent is asked to pick
with `vs_select`.

## Support and links

- Repository and issues: https://github.com/msx752/DebugViaMcp
- Security model: `docs/security.md` in the repository
- Changelog: `CHANGELOG.md`
