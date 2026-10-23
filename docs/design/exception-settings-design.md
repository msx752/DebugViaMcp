# Exception settings (F03) — design

Roadmap item F03 in `vs-feature-roadmap.md`. Status: draft for approval.

## Why
An agent that debugs a program often needs the debugger to stop where an exception is *thrown*, not where it is
finally caught or crashes the process. Today the only way is for the user to open Debug → Windows → Exception Settings
by hand. The agent can already read the current exception (`debug_exception`); it cannot say which exceptions to stop on.

## Goals
- List Visual Studio's exception settings and set *Break When Thrown* per exception.
- Leave the user's IDE as it was: the agent can undo exactly what it changed.
- Every answer small enough for an agent to read (the CLR group alone has 414 entries).

## Non-goals
- Adding or removing user-defined exceptions, exception codes (Win32 and similar groups), *Break When User-Unhandled*,
  conditions and module filters. Possible later; not in this release.
- Changing a setting for a debug session other than the one open in the leased Visual Studio.

## What the spike showed (VS 2026 18.x, real profile, no solution loaded)
- `DTE.Debugger` as `EnvDTE90.Debugger3` gives `ExceptionGroups`: 9 groups (CLR 414 settings, Win32 36, C++ 19,
  Python 62, JavaScript 2 and 134, MDA 27, CMake 9, GPU 7).
- `ExceptionSetting.BreakWhenThrown` reads; `ExceptionSettings.SetBreakWhenThrown(bool, setting)` writes, visible at
  once on the next read. Works in design mode (no debug session).
- `ExceptionSetting.Code` throws in groups that do not support codes; never read it there (`SupportsExceptionCodes`).
- A name that does not exist fails with `0x8002000B` (invalid index); we translate it to a clear error.
- A change did **not** survive closing and reopening Visual Studio, whether closed through `DTE.Quit()` or by closing the main window
  (checked with no solution loaded). Settings are therefore per Visual Studio session.
- Writing while the debugger is attached and the program **runs** (`dbgRunMode`) works: set, read back, set back. In **break mode**
  the write was not tried: Visual Studio answered `RPC_E_CALL_REJECTED` right after `Break All`. The extension runs on the UI thread
  and retries nothing, so in break mode treat it as untested until the manual list (row 9) is run.
- Opening the sample project through `Solution.Open(csproj)` from a script failed (`E_ABORT`), so no run was started from Visual Studio itself;
  the program was started outside and attached.

## Tools
All names in Visual Studio's words (Exception Settings, Break When Thrown).

| Tool | Tier | Arguments | Answer |
|---|---|---|---|
| `exception_list` | Read | `group?`, `filter?` (substring, case-insensitive), `onlyEnabled` (default true), `limit` (default 100, max 500) | groups with `name`, `breakWhenThrown`; `total` and `truncated` |
| `exception_set` | Control | `group`, `names[]` (exact), `breakWhenThrown` | for each name the previous value; a change summary |
| `exception_reset` | Control | none | restores what this connection changed through `exception_set`; answer lists what was restored |

Rules:
- `exception_set` is all or nothing: every name is checked first; one unknown name fails the call and nothing changes.
  The error names the group and offers up to 5 close names.
- An unknown group lists the group names. Names are matched exactly (case-sensitive, as in Visual Studio).
- Setting a value that is already set is not an error; it is reported as unchanged and not recorded for reset.
- The first change of a setting records its original value. A second change keeps the first original. `exception_reset`
  writes the originals back and clears the record. *My call:* reset covers only what the agent changed, never
  `ExceptionGroups.ResetAll()`, which would also undo the user's own settings.
- When this connection's hold on Visual Studio ends (`vs_release`, the connection closes, Disconnect AI), the settings it changed are put back
  at once and the record is dropped, so an agent that never calls `exception_reset` leaves nothing behind. A failing write is only logged;
  the record is dropped either way, so a later agent never undoes this one's changes. (The user chose this over only dropping the record.)
- `exception_reset` with nothing recorded is a read: it asks no question and takes no lease, so a cleanup call never locks other agents out.
- Reads are refused with `in_use` while another agent holds the lease, like every read; only `exception_set` and `exception_reset` (with something to put back) take it.
- Control tier: the user's consent prompt says which exceptions in which group change and to what
  ("Break When Thrown on: System.NullReferenceException"). Every call writes an audit line with its `callId`.
- Reads obey the existing output masking and size caps; a `truncated` answer says how to narrow it (`filter`, `group`).

## Protocol
- Protocol 2.8, constant `ProtocolInfo.MinorExceptionSettings = 8`. Strictly additive: new requests
  `ListExceptionSettings`, `SetExceptionSettings`, `ResetExceptionSettings` and their DTOs; no existing field changes
  meaning. Golden-JSON tests for each DTO.
- **Compatibility rule (applies to this and every later change):** a minor never removes or renames a field or request.
  Something replaced is marked `[Obsolete("Use X from protocol 2.n.")]`, keeps working, and is removed only in a new
  major. The host adapts to the extension's minor reported in the handshake: with a minor below 8 the three tools answer
  with the existing "update the extension" error from `ToolResponse.RequireMinor`; an older host ignores requests it
  does not know. Tests: a minor-7 extension (fake) receives no exception requests; a minor-7 host-side client round trips
  the unchanged DTOs.
- Wire protocol docs: `design.md` list of requests and the CHANGELOG entry say which minor added what.

## Visual Studio side
- `IDteFacade.ExceptionGroups()` / `SetBreakWhenThrown(group, names, value)`; implemented in `DteFacade` on the UI
  thread like the other DTE calls, with the same busy and dialog checks as other calls that change state.
- The reset record lives in the bridge connection handler (per connection), not in `DteFacade`.

## Done when
`exception_list`, `exception_set`, `exception_reset` work against a real Visual Studio (manual list in `docs/testing/`),
unit tests cover: pagination and truncation, all-or-nothing, unknown name and group, unchanged value, second change keeps
the first original, reset restores, the lease ending (release, close, disconnect) puts the settings back, an empty reset takes no lease, the consent text, minor-7 fallback. README and Marketplace
overview tool lists updated, CHANGELOG under Unreleased.

## Left to the builder
Exact JSON field names (follow the neighbouring DTOs), the close-name search (any simple edit-distance or prefix
approach), the consent text wording within the rules above.
