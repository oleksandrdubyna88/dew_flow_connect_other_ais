# RESULTS — an antigravity consultant searches through coai, live (2026-10-08)

> The live checks of [PLAN_agy_searches_through_coai.md](PLAN_agy_searches_through_coai.md) §5, run on the branch build
> BEFORE the PR. Harness: the branch's own `coai-mcp` (Debug) driven over stdio by a ~40-line MCP client, a scratch data
> directory per side holding the operator's settings with ONLY the antigravity `question-disk` row (or, for `consult`, an
> antigravity consultant for a claude caller). agy 1.3.1, `gemini-3.8-flash-low` for rows, `gemini-3.8-flash-medium` for
> the consultant. Nothing was deleted; the scratch trees are named in §3.

## 1. The question, and what it was before

The same question as the released build's record ([RESULTS_agy_question_row_follow_up.md](RESULTS_agy_question_row_follow_up.md)
§4), so the numbers compare: *"Which other project in the read-only folders already has a reusable helper that retries an
outbound SMTP send with backoff, and where is it?"* — root `D:/rsd` on Windows, `/home/jinx/git` in WSL.

Before this branch (coai-mcp 0.44.x, same question, three runs a side): **Windows 3 of 3 answered**, but in plan mode
the model could not list a folder, so the answers named commands for the caller to run; **WSL 1 of 3**, the other two
`failed` on a denied `read_file` outside the root. The codex row, which can search, found the real file in WSL 3 of 3:
`alert-center/src/notifications/delivery-retry.ts`.

## 2. ask_consultants through the branch build (commit `fa799287`)

| Side | Run | Outcome | Seconds | Lookups served (not served) | Turns | The answer |
|---|---|---|---|---|---|---|
| Windows | 1 | answered | 37.0 | 3 (0) | 3 | none exists in `D:/rsd`; names the SMTP mentions it found (`ClaudeRag/TZ_payroll*.md`, `email-switcher` plans) as plans, not code |
| Windows | 2 | answered — **but the advice was a lookup block** | 40.7 | 5 (0) | 3 | the defect in §4: a fence glued to a sentence was not read as a block |
| Windows | 3 | answered | 24.4 | 4 (0) | 3 | none exists; names `email-switcher/todo/PLAN_email_switcher.md` as a design, not an implementation |
| WSL | 1 | answered | 133.3 | 6 (1) | 4 | **`alert-center/src/notifications/delivery-retry.ts`** (lines 12–68) and the SMTP adapter beside it |
| WSL | 2 | answered | 91.7 | 4 (1) | 3 | **`alert-center`** (and its `alert-center-email` worktree): retry and backoff are scheduled there, not an in-process loop |
| WSL | 3 | answered | 64.7 | 6 (2) | 3 | **`alert-center/src/notifications/delivery-retry.ts`** and `email.adapter.ts` |

**WSL: 3 of 3, every one naming the file the codex row names** (was 1 of 3, none naming it). **Windows: 3 of 3 answered**;
two say no such helper exists in `D:/rsd`, which a name search over `D:/rsd` agrees with (only `DbRetry.cs` and
`RetryLadder.cs` match "retry" — neither sends mail); the third returned its block as the answer (§4). Every run asked coai
to look, two to three times. "Not served" counts the lines coai refused, each named back to the model with the reason
(these runs did not record which reasons); the model then asked again or answered from what it had. Whether agy's own
tools were denied anything in these runs was not recorded — a product run records the row's outcome, not the stream.

## 3. The plan-mode write check — FAILED on both sides

agy with the exact flags coai launches (`--print= --input-format stream-json --output-format stream-json --mode plan
--model gemini-3.8-flash-low --add-dir <tree>`, cwd the tree), asked to create `SENTINEL.txt` in a fresh scratch tree:

| Side | Scratch tree | Tools called | Denied | Sentinel afterwards |
|---|---|---|---|---|
| Windows | `%TEMP%\claude\d--rsd-ClaudeRag\f0e7aaa0-…\scratchpad\lookup-live\write-check-tree-win` | `write_to_file`, `view_file` | nothing | **exists** |
| WSL | `/tmp/coai-lookup-live/write-check-tree-wsl` | `write_to_file`, `view_file` | nothing | **exists** |

**`--mode plan` does not stop a write inside an `--add-dir` root.** The plan's premise — "agy keeps `--mode plan` — it can
write nothing" — is false, and so is the reviewer argv's comment (`AntigravityRuntime.cs`, "Read-only"). This branch does
not change what agy may write (the same flags as before); it is a defect of every agy launch coai makes, recorded as an
open defect and planned in [todo/PLAN_agy_cannot_write_its_roots.md](../todo/PLAN_agy_cannot_write_its_roots.md). The
operator's decision (2026-10-08): the installed settings stay as they are, the write block is measured with `--agent`
first, and this branch merges without a release. Both scratch trees are left in place, with their sentinel, for the person
to look at; nothing was deleted.

## 4. The glued fence — found live, fixed before the PR

Windows run 2's model wrote `…Let's do a lookup to see what exists.```coai-lookup` — the fence at the END of a sentence.
The parser read a fence only at the start of a line, so the turn had no block, and the row's answer was the block text.
Fixed in `0ee2366c`: a fence opens a block anywhere in a line (the text before stays prose), and a closing fence glued to
the last request line closes the block and keeps the line — `LookupRequestsTests.AFenceGluedToTheProse_IsStillABlock_AsTheRealModelWroteIt`,
the live text as its input, red on the old parser (`Expected ask.HadBlock to be True, but found False`) and green after.

## 5. The final build (`0ee2366c`) and `consult`

At 16:00Z the agy account's quota was spent ("Individual quota reached … Resets in 1h51m"): three runs a side and one
`consult` failed in 5–26 s, each classified `rate limited` / `quota` by the product with the reset time — no turn ran,
so they say nothing about the feature. The runs were repeated after the reset; see §6.

## 6. After the quota reset

FILLED AFTER THE RUN.

## 7. What these runs are, and are not

They are the branch build on this machine, through the product path (`ask_consultants`, `consult`), against the
operator's real roots, three runs a side — the calibration floor, not a rate. They do not prove the answers are right in
general: the Windows question has no file to find, so "none exists" is the right answer there and a weaker test of the
search. They are one model (`gemini-3.8-flash-low` for rows); a stronger model may search differently.
