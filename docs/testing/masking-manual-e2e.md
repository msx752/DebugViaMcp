# Manual end-to-end: Mask sensitive values

Needs a Visual Studio with the extension, the sample `samples/OrderDemo`, and an agent connected. **Not yet run** (written with the feature).

| # | Do | Expect |
|---|---|---|
| 1 | In OrderDemo add `var password = "hunter2"; var note = "Password=hunter2;Server=db";` before a breakpoint, run to it | The agent's `vars` shows `password` = `[masked]` with `masked: true`; `note` = `Password=[masked];Server=db` |
| 2 | Agent calls `eval` with `password` | `[masked]`, `masked: true` |
| 3 | Throw `new Exception("Login failed. Token=abc123")` and break on it; agent calls `exception` | Message `Login failed. Token=[masked]`, `masked: true` |
| 4 | Write a line `Password=hunter2` to the Output window; agent calls `output_read` | The line shows `Password=[masked]` |
| 5 | Tools → Options → Debugging via MCP: turn **Mask sensitive values** off | A confirmation dialog appears; **No**: the agent still gets `[masked]` |
| 6 | Turn it off again, answer **Yes** | `vars` now shows the real value; the change is logged |
| 7 | Turn it on | Values are masked again at once |
| 8 | Close Visual Studio, set the setting to off in the settings file, start it | The dialog appears at startup; until you answer, values stay masked |
