# Screenshots to take for the Marketplace page

The schematics in `images/` are generated (`scripts/make-marketplace-images.ps1`). Real screenshots have to come from
a running Visual Studio, which was deliberately not automated, so please take these four yourself.

General: light or dark theme (dark looks good next to the purple icon), window around 1400 px wide, crop to the
relevant part, PNG, no personal paths or project names you would not publish. The Marketplace gallery shows 16:9
well; 1280×720 or larger.

| # | What to show | How to get it |
|---|---|---|
| 1 | **Consent info bar** | In a solution with a breakpoint stopped (or not), let the agent call `set_value` (Mutate = Ask, the default). Capture the info bar "… wants to set total = 0 in …" with Allow once / Allow for session / Deny. |
| 2 | **Options page** | Tools → Options → All Settings → Debugging → Debugging via MCP (Permissions). Show Read / Control / Mutate with Allow / Allow / Ask. |
| 3 | **Audit pane** | View → Output → "Debugging via MCP Audit" after a short debugging session: a few lines with tool names, call ids and decisions. |
| 4 | **Agent and debugger together** | Split view: the agent terminal on one side (a `debug_start` / `bp_set` exchange), Visual Studio stopped at the breakpoint on the other. |

Optional extras: the status bar with "Debugging via MCP: claude-code connected — in use by …" (0.4), and the "is out of
date: … Build it now?" info bar.

Name them `01-consent.png`, `02-options.png`, `03-activity.png`, `04-agent-and-debugger.png`, put them in
`docs/marketplace/images/`, and tell me — I will add them to the overview and the listing.
