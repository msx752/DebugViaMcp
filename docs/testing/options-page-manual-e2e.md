# Manual end-to-end: the Options page of Visual Studio 2022 (17.14)

Visual Studio 2022 17.14 delivers no values for the All Settings page, so the extension has its own page, **Tools → Options → Debugging via MCP → General**.
Its values count only while the settings service delivers none of ours; in Visual Studio 2026 All Settings is the one place and this page is ignored.

Checked by script on 17.14 (extension installed, empty solution): the page is registered and loads; the log line is `Settings: Options page: read=Allow; ...`;
`debug_status` shows the defaults (Allow / Allow / Ask). Values written through Visual Studio's automation are not stored and do not raise Apply, so the rows below need a person at the keyboard.

| # | Do | Expect |
|---|---|---|
| 1 | Tools → Options → Debugging via MCP → General | Four groups (Permissions, Behavior, Limits, Logging) with the values Allow / Allow / Ask, 60, on, on, on, 120, 600, Information, 7 |
| 2 | Set Read = Deny, press OK; agent calls `debug_status` | Denied by settings at once, no restart; Activity shows `Settings changed: Read: Allow → Deny` |
| 3 | Set Read = Allow again, OK | The next call works |
| 4 | Set Mutate = Allow, OK | Visual Studio asks you to confirm; until you do, Mutate stays Ask |
| 5 | Set Mask sensitive values off, OK | Visual Studio asks you to confirm; until you do, values stay masked |
| 6 | Close and reopen Visual Studio | The values are still there; the log line `Settings: Options page: ...` shows them |
| 7 | Same checks in Visual Studio 2026 | The page is listed but changing it has no effect; All Settings is used |
