# Manual end-to-end: a new debugviamcp tool against an older extension

The extension and the tool are updated separately in two places (Marketplace and the tool package), so the tool must work against an extension that is one release behind.
The rule is in `docs/design/exception-settings-design.md` (Protocol): a minor never removes or renames anything, and a tool that needs a newer minor answers `unsupported` with the hint to update the extension.

## Run: 0.5.0 tool (protocol 2.8) against the 0.4.1 extension (protocol 2.3), Visual Studio 2026

The 0.4.1 extension was built from the tag `v0.4.1` in a temporary worktree and installed in place of the current build; afterwards the current build was installed again and the worktree removed.

| Call | Result |
|---|---|
| `vs_instances` | the instance, `vsixVersion` 0.4.1 |
| `debug_status`, `bp_list`, `errors_list` | answered as before |
| `exception_list`, `exception_reset` | `unsupported`: "The Visual Studio extension speaks protocol 2.3; this tool needs 2.8." with the hint "Update the Debugging via MCP extension in Visual Studio to the same release as the debugviamcp tool." |

`exception_set` was called without arguments by the script, so it ended in the MCP library's argument error rather than the `unsupported` answer; the other two tools of the same feature show the behaviour.
