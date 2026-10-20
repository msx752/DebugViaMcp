# Marketplace listing texts (0.4.0 preview)

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

Debugging, Coding (already in `publishManifest.json`)

## Tags / keywords

debugger, debugging, mcp, model context protocol, ai, agent, claude, claude code, breakpoints, llm,
visual studio 2026

(The manifest `Tags` field holds the first nine; add the rest on the page if it allows more.)

## Images, in this order

| # | File | Purpose |
|---|---|---|
| 1 | `images/how-it-works.png` | Explains the idea and the three permission tiers (schematic, not a screenshot) |
| 2 | your screenshot of the consent info bar | Shows the "you stay in control" claim — see [screenshots.md](screenshots.md) |
| 3 | your screenshot of the Options page | Permission tiers |
| 4 | your screenshot of the Activity pane | The audit trail |
| 5 | `images/several-visual-studios.png` | Several Visual Studios, one agent each (schematic) |

Icon and preview image are inside the VSIX; the gallery images above are uploaded on the page (or linked from the
overview with raw GitHub URLs once the repository is public).

## What's new in this preview (release notes box)

0.4.0 — Several Visual Studios, one agent each. The tool picks the Visual Studio that matches the folder the agent
runs in; an agent from an unrelated folder needs your approval; a second agent gets `in_use` while the first holds a
Visual Studio (`vs_release` frees it). `debug_start(project)` starts a named project and leaves your startup project
alone. `debug_start`, `debug_attach` and `bp_set` warn when the build is older than the sources, and Visual Studio
offers an optional Build. This is a preview: feedback is welcome on GitHub.

## Questions and answers (seed the Q&A tab)

**Does it work with other agents?**
Any MCP client can use the `debugviamcp` server. It is tested with Claude Code.

**Can the agent change my program without asking?**
Not by default. Reading and debugging control are allowed; anything that changes program state (`set_value`,
evaluation with side effects, attaching, breakpoint conditions that run code) asks you first. You can set each tier
to Allow, Ask or Deny in Tools → Options → Debugging → Debugging via MCP. No answer means deny.

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
