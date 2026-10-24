# Manual end-to-end: `eval` with `offset` and `length` (long values in pages)

Needs a Visual Studio with the extension (protocol 2.9), `samples/OrderDemo` (startup project) and an agent connected.
`tests/e2e/watch.ps1 -Driver value-paging` runs the rows below against a real Visual Studio and closes it again (`tests/e2e/value-paging.cs`;
no question is answered, nothing is clicked).

| # | Do | Expect |
|---|---|---|
| 1 | Stop in `OrderService.Calculate`; `eval` with `order.Note` | A value cut at about 1 KB, `truncated: true` |
| 2 | `eval` with `order.Note`, `offset` 0, `length` 1000 | 1000 characters starting with the quote, `totalLength` 5002, `hasMore: true` |
| 3 | Every page of `order.Note` (length 1500), put together | The whole displayed text (4 pages) |
| 4 | A 300,000-character text made by the expression (`new string('a', 150000) + new string('b', 150000)`), pages of 16384 | 19 pages that rebuild the text exactly; Visual Studio does not cut the text itself; the slowest page took 225 ms |
| 4b | A 1,500,000-character text, pages of 16384 | 92 pages that rebuild the text exactly; the text is read and masked once for all the pages of a stop, so only the first page is slow |
| 5 | `offset` past the end | An empty page, `hasMore: false` |
| 6 | A text of 2,100,000 characters | `invalid_argument` naming the limit of 2,000,000 |
| 7 | Paging with `allowSideEffects` | `invalid_argument`, nothing runs |
| 8 | A negative `offset`; an expression that cannot be evaluated | `invalid_argument`; `eval_failed` with Visual Studio's message |
| 9 | Options: Read = Ask, then a paged `eval` | One question like `eval`; Deny refuses it |
| 10 | A secret-looking value (a variable called `password`) read in small pages | Masked as a whole: `[masked]`, `masked: true`; no page shows a part of it |
| 11 | Same on Visual Studio 2022 17.14 | Same answers |

The first page of a value evaluates the expression (Visual Studio stops an evaluation after about 5 s); the pages after it, at the same stop and frame, are cut from the saved masked text. The saved text is dropped by eval with side effects and set_value, replaced at the next stop, and held only weakly. Times depend on the machine, its load and the size of the project: the driver only checks that 1,500,000 characters take less than 30 seconds.

## Run record

| Date | Visual Studio | Rows | Result |
|---|---|---|---|
| 2026-10-25 | 2026 18.x, extension 0.6.0 dev build (protocol 2.9) | 1-8 by `tests/e2e/watch.ps1 -Driver value-paging` | all checks passed, nothing left behind |
| 2026-10-25 | same, with the saved text | 1-8 and 4b | 300,000 characters: slowest page 225 ms before, 77 ms after; 1,500,000 characters in 92 pages, 584 ms in all (first page 268 ms) on this machine |
