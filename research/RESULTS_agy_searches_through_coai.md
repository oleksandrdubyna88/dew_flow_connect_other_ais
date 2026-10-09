# RESULTS — an antigravity consultant searches through coai, live (2026-10-08)

> The live checks of [PLAN_agy_searches_through_coai.md](PLAN_agy_searches_through_coai.md) §5, run on the branch build
> BEFORE the PR. Harness: the branch's own `coai-mcp` (Debug) driven over stdio by a ~40-line MCP client, a scratch data
> directory per side holding the operator's settings with ONLY the antigravity `question-disk` row (or, for `consult`, an
> antigravity consultant for a claude caller). agy 1.3.1, `gemini-3.8-flash-low` for rows, `gemini-3.8-flash-medium` for
> the consultant. Nothing was deleted; the scratch trees are named in §3. Commit ids below are the commits of the branch
> `feat/agy-searches-through-coai` as it was opened for review; `main` holds them squashed into one merge.

## 1. The question, and what it was before

The same question as the released build's record ([RESULTS_agy_question_row_follow_up.md](RESULTS_agy_question_row_follow_up.md)
§4), so the numbers compare: *"Which other project in the read-only folders already has a reusable helper that retries an
outbound SMTP send with backoff, and where is it?"* — root `D:/rsd` on Windows, `/home/jinx/git` in WSL.

Before this branch (coai-mcp 0.44.x, same question, three runs a side): **Windows 3 of 3 answered**, but in plan mode
the model could not list a folder, so the answers named commands for the caller to run; **WSL 1 of 3**, the other two
`failed` on a denied `read_file` outside the root. The codex row, which can search, found the real file in WSL 3 of 3:
`alert-center/src/notifications/delivery-retry.ts`.

## 2. ask_consultants through the branch build (commit `15e65b3c`)

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
defect and fixed by [PLAN_agy_cannot_write_its_roots.md](PLAN_agy_cannot_write_its_roots.md) (2026-10-09). The
operator's decision (2026-10-08): the installed settings stay as they are, the write block is measured with `--agent`
first, and this branch merges without a release. Both scratch trees are left in place, with their sentinel, for the person
to look at; nothing was deleted.

## 4. The glued fence — found live, fixed before the PR

Windows run 2's model wrote `…Let's do a lookup to see what exists.```coai-lookup` — the fence at the END of a sentence.
The parser read a fence only at the start of a line, so the turn had no block, and the row's answer was the block text.
Fixed in `e7fb51fd`: a fence opens a block anywhere in a line (the text before stays prose), and a closing fence glued to
the last request line closes the block and keeps the line — `LookupRequestsTests.AFenceGluedToTheProse_IsStillABlock_AsTheRealModelWroteIt`,
the live text as its input, red on the old parser (`Expected ask.HadBlock to be True, but found False`) and green after.

## 5. The final build (`e7fb51fd`) and `consult`

At 16:00Z the agy account's quota was spent ("Individual quota reached … Resets in 1h51m"): three runs a side and one
`consult` failed in 5–26 s, each classified `rate limited` / `quota` by the product with the reset time — no turn ran,
so they say nothing about the feature, and they show the quota failure is named, not swallowed. The runs were repeated
after the reset; see §6.

## 6. After the quota reset — the final build (`e7fb51fd`), 17:56–18:05Z

> The binary reports `+fa799287` in its version: it was built from the fixed working tree just before the fix was
> committed. The tree was what `e7fb51fd` holds; the SHAs in this record are the ones after the rebase onto `main`.

> The binary reports `+fa799287` in its version: it was built from the fixed working tree just before the fix was
> committed (the tree was the commit `e7fb51fd` holds; the SHAs in this record are the ones after the rebase onto `main`).

**ask_consultants, the same question:**

| Side | Run | Outcome | Seconds | Lookups served (not served) | Last turn | The answer |
|---|---|---|---|---|---|---|
| Windows | 1 | answered | 57.3 | 3 (0) | 4 | none exists in `D:/rsd`; SMTP appears only in `email-switcher` plans and tasks |
| Windows | 2 | answered | 42.0 | 6 (0) | 3 | none exists; the same plan and task named as designs, not code |
| Windows | 3 | answered | 55.0 | 4 (0) | 4 | none exists; the same two files named |
| WSL | 1 | answered | 105.6 | 5 (0) | 3 | **`alert-center/src/notifications/delivery-retry.ts`** (`afterFailure`, `RETRY_DELAYS_MINUTES`) and `email.adapter.ts` |
| WSL | 2 | answered | 43.8 | 6 (0) | 3 | **`delivery-retry.ts`** and `email.adapter.ts`, with the backoff table — but the advice OPENS with the model's own working notes ("Let's view … The details are clear. Let's compose the answer") before the answer |
| WSL | 3 | answered | 46.7 | 6 (0) | 3 | **`delivery-retry.ts`** and `email.adapter.ts` (`nodemailer`, transient vs terminal) |

**6 of 6, no block left as an answer** (the §4 fix held in all six). WSL named the real file 3 of 3 again; Windows said
"none exists" 3 of 3, which the name search over `D:/rsd` supports. Every run used coai's lookups, 3 to 6 per answer.

**consult, one per side** (agy as a claude caller's consultant, `gemini-3.8-flash-medium`):

| Side | Checkout | Problem | Lookup turns (agy's own transcript) | Seconds | The advice |
|---|---|---|---|---|---|
| Windows | `D:/rsd/_wt/coai-agy-access` (this branch) | where the 32 KB per-turn budget is enforced and which test pins it | 1 (`turn 2 of 4`) | 116.6 | `WorkspaceLookup.Bounded` with `LookupLimits.TurnBytes` (`LookupBudget.cs`), pinned by the `WorkspaceLookupTests` that narrow `TurnBytes` to 64 bytes — correct, with the check command |
| WSL | `/home/jinx/git/SSO` | does this repository send e-mail, and is a failed send retried | 2 (`turn 2`, `turn 3 of 4`) | 59.0 | none: no mail client in `package.json`, outbound calls only to Keycloak, the internal API and the alert-center ingest; "email" appears only as an OpenID scope — with file:line evidence |

Each consult is ONE `consult` call and ONE turn on the record (`followedUp: true`, billed once). The WSL checkout had 0
dirty files before and after.

**What these add.** The defect of §4 did not recur. One answer in six carried the model's working
notes in front of the answer — agy's prose, which coai passes on as written; not fixed here. Both consults reached for
coai's lookups on their own and answered from what they found.

## 7. What these runs are, and are not

They are the branch build on this machine, through the product path (`ask_consultants`, `consult`), against the
operator's real roots, three runs a side — the calibration floor, not a rate. They do not prove the answers are right in
general: the Windows question has no file to find, so "none exists" is the right answer there and a weaker test of the
search. They are one model (`gemini-3.8-flash-low` for rows); a stronger model may search differently.

**After these runs**, the second code round changed three things that no live run has exercised: a question row now asks
its watched roots before every lookup continuation, a continuation whose stream names no conversation keeps the one it
continued, and a search reads at most 20 000 entries of one folder (`a59f9263`). Each is covered by a scenario test
observed red first (module_tests.md); none changes a path these runs took — the question rows' roots were not git checkouts (the consult loop had its tree check already), every
stream named its conversation, and no folder came near the bound.
