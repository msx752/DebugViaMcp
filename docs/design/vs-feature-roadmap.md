# Visual Studio features: ordered list (working document, not committed)

Status: **order approved. F12 (mask sensitive values) is built and committed on `develop` (unreleased, manual end-to-end run still to do); the rest is not started.** Source of the ideas: `vs-feature-ideas.md`. Names use Visual Studio's own words.
Today: 27 tools, extension 0.4.1. Rows marked **verify first** need a short try-out in a real Visual Studio before design,
because the automation member is documented but its behaviour was not tried.

## The list

| Order | ID | Feature | Tier | Release | Verify first | Done when |
|---|---|---|---|---|---|---|
| 1 | F12 | **Mask sensitive values** (setting, on by default) | Read | 0.5.0 | no | `vars`, `eval`, `exception` and watch results show a mask instead of values that look like secrets; the setting turns it off; tests cover names (password, token, key, connection string) and value shapes |
| 2 | F11 | **`vars` with `depth` and `expand`** | Read | 0.5.0 | no | Default answer is collapsed to a small depth; `expand=[paths]` opens named members; size is capped |
| 3 | F02 | **`debug_status` with `detail`** | Read | 0.5.0 | no | One call returns mode, break reason, top Call Stack frames, Locals and the exception |
| 4 | F01 | **`process_list`** | Read | 0.5.0 | no | Lists local and debugged processes (id, name); `debug_attach` accepts what it returns |
| 5 | F04 | **`symbol_status`** | Read | 0.5.0 | **yes** | Says whether symbols are loaded manually, the symbol path state, and why a breakpoint may be unbound |
| 6 | F13 | **Debugging guide in the MCP `instructions`** | none | 0.5.0 | no | The server's instructions describe a good order of tool calls; it names the new tools; written last in the release |
| 7 | F03 | **`exception_settings`** | Control | 0.6.0 | **yes** | Lists exception settings and sets Break When Thrown per exception; unknown name gives a clear error |
| 8 | F09 | **`bp_set` option `temporary`** | Control | 0.6.0 | no | The breakpoint removes itself after its first hit; shown in `bp_list` until then |
| 9 | F08 | **`debug_restart`** | Control | 0.6.0 | no | Stops and starts the same target (project, arguments) again in one call |
| 10 | F10 | **`watch_add`, `watch_list`, `watch_remove`** | Read | 0.6.0 | no | Expressions are evaluated at every break and returned with the stop; the list lives for the debugging session |
| 11 | F05 | **`symbol_load`** | Mutate | 0.6.0 | **yes** | Reloads symbols after the user approves; the change to IDE settings is named in the consent text |
| 12 | F06 | **`debug_save_dump`** | Mutate | 0.7.0 | **yes** | Writes a minidump to a folder the user approves; the consent text says it holds program memory |
| 13 | F07 | **`debug_set_next_statement`** | Mutate | 0.7.0 | **yes** | Moves the next statement to a file and line after approval; the editor selection change is documented; may be dropped |

## Why this order
1. **Privacy first (F12).** Every later read tool returns program values; the mask must exist before they do.
2. **Smaller answers (F11, F02, F01).** Cheap, no new Visual Studio behaviour.
3. **Teaching the agent (F13)** is written last in 0.5.0 so it describes the tools that exist.
4. **0.6.0** adds workflow tools; only F03 and F05 depend on behaviour to verify.
5. **0.7.0** holds the two most sensitive and least certain tools, so they cannot hold up the rest.

## Every feature includes
- Wire protocol: additive change, protocol minor + 1, DTO, golden-JSON test.
- Bridge method, `DteFacade` implementation, permission tier, consent text (Mutate), audit line, one-agent lease.
- MCP tool with a description in Visual Studio's words; unit tests; a case in the manual end-to-end list (`docs/testing/`).
- Docs: README and Marketplace overview (tool count), CHANGELOG under *Unreleased*.
- Release: version in the three places, CHANGELOG section, paste the overview again when its content changed.

## Not in this list
Anything whose automation route was not found (Hot Reload report, debugging a test, data breakpoint, dependent breakpoint,
Filter condition, Force Run To Cursor, Object ID, Just My Code, module list).

## Open decisions
- Approve the order and the 0.5.0 / 0.6.0 / 0.7.0 grouping.
- F12: default on, and whether the setting can be turned off at all.
- F06: which folder an agent may write a dump to.
