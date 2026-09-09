# PLAN — the log asks for a page, and lets SQL do the counting

> Status: **IMPLEMENTED, 2026-09-09.** Plan round: good_enough, 12 findings, 11 taken.
> Scope: `src_mcp/src/Store/RoundsQuery.cs`,
> `src_mcp/src/Program.cs`, `src_vs_code/src/roundsDbRead.ts`, `src_vs_code/src/roundsLog.ts`,
> `src_vs_code/src/roundsLogPanel.ts`.
>
> Related docs: [module_extension.md](module_extension.md),
> [module_server.md](module_server.md),
> [PLAN_the_log_never_gets_its_findings.md](PLAN_the_log_never_gets_its_findings.md) —
> the fix that made this payload reach the page at all, and whose open tail this plan closes.
>
> **Boundary, named on both sides.** This plan takes over the one unfinished item of
> [PLAN_findings_in_the_log.md](PLAN_findings_in_the_log.md): a round whose findings are
> not kept must SAY so rather than draw a blank that reads as "clean". That plan shipped its list
> through the rounds database instead of the session files it was written against, which left the
> absent case unbuilt — and this plan adds a fourth state to it, because findings that arrive on a
> click can also be *not yet asked for*. The search box that plan's sibling
> [PLAN_local_db_reader.md](PLAN_local_db_reader.md) left unbuilt is NOT here: it is
> [todo/PLAN_the_log_searches_the_findings.md](../todo/PLAN_the_log_searches_the_findings.md).

## Deviations — what the code found that the plan did not know

**The page's rows are not database rows.** The plan assumed paging the database would page the page.
It does not: `rowsFrom` builds every row from the SESSION files and the database only ENRICHES them,
with the decision counts and — now — the finding count. Three consequences, all of them shipped
differently from the plan:

1. **Two axes, not one.** The database read asks for `MAX_LIMIT` (1000) rounds, because that list is
   what gives every row its ✓/✗ badge and a round without its findings costs about 220 bytes. The
   PAGE is `PAGE_SIZE` (200) rows of the merged, filtered, sorted list, in the page. So "200 per
   page" is what a person reads and "not everything from the database" is the findings no longer
   riding the list — which was 98.7 % of the payload, and is the whole of the operator's complaint.
2. **A sixth thing to be honest about.** When the window does not cover the whole table, a row the
   list does not name might simply be older than the window. Calling it *not kept* would be a claim
   about a round nobody checked, so it is `unasked` and the server answers exactly.
3. **`FoundCount` had to be invented.** The plan removed the findings from the list without noticing
   that `statusOf` reads their COUNT — take it away and every clean round reads as awaiting a resolve
   nobody owes. One integer per row, one subquery, and a test that fails without it.

**`--findings` takes flags, not positional arguments.** The plan wrote
`--findings --round <sessionId> <stage> <number>`; it shipped as `--session`/`--stage`/`--number`,
matching `Flags(args)`, which every other one-shot mode in `Program.cs` already uses.

**The one rejected plan finding.** "The totals query blocks the UI on millions of rows" was rejected
with the measurement — 236 rounds and 3 484 findings, and the totals ride the same payload as the
page, so there is no second wait to guard. Its useful half was taken from another finding: the five
scalar subqueries became one aggregation pass.

## The instruction

From the operator, verbatim: *«возвр все из БД не нужно. у нас есть пагинаций. 200 на стр
достаточно.»* and *«суммы - скл счиатть (сколько всего и тд.)»* — do not return everything from the
database, there is pagination, 200 per page is enough; and the totals should be counted in SQL.

## The measurement that decides the shape

`coai-mcp --log --limit 300` today, against the real database:

| part | size | share |
|---|---|---|
| whole payload | **3.83 MB** | |
| the 236 round ROWS, findings stripped | **0.05 MB** | 1.3 % |
| the 3 484 findings carried inside them | **3.78 MB** | **98.7 %** |
| blind spots (25 entries) + defended (0) | < 0.01 MB | |

So the payload is not the rounds. It is the findings, shipped for every round whether or not anybody
opens one — and the page opens them one row at a time, on a click
(`roundsLog.ts:963`, `state.expanded[r.key]`).

Two conclusions, and the second is the one that matters:

1. Paging the rows at 200 saves **0.01 MB**. On its own it is not a payload change at all.
2. **Not shipping findings with the list saves 3.78 MB** — and it is what makes paging honest,
   because a row costs about 220 bytes once it stops carrying a review inside it.

## What is wrong today, with references

- `RoundsQuery.DefaultLimit = 300` (`src_mcp/src/Store/RoundsQuery.cs:69`) and
  `FindingsByRound(db, limit)` (`RoundsQuery.cs:112`) fetch every finding of every one of those
  rounds, joined into `LoggedRound.Findings` (`RoundsQuery.cs:38`).
- The page receives all of it in one message and keeps it as `ROWS`; the count line is computed in
  the webview by walking that array (`roundsLog.ts:969`), so "how many rounds are there" is answered
  by the size of what was sent rather than by the database.
- There is **no pagination** on the log page today. `roundsLog.ts` has no page size, no offset and no
  Prev/Next; `shown.length + ' of ' + ROWS.length` is a FILTER count, not a page count.
- The blind-spots tab is already SQL (`GroupedBy`, `RoundsQuery.cs:176` — `SUM(resolution =
  'accept'), COUNT(*) … GROUP BY`). That part of the instruction is already satisfied and must not be
  rewritten.

## What must be true when this is done

1. `--log` returns a PAGE of rounds — 200 by default — and says how many there are in total.
2. A round in that page carries **no findings**. Its findings are fetched when the row is opened.
3. The totals the page shows are computed by SQL over the whole table, never by counting the array
   that happened to be sent.
4. **Sorting, filtering and search are page-local, and the page SAYS so.** They act on the rows
   that are loaded and on nothing else; the header reads `filtered N of the 200 on this page`, never
   a bare count that could be read as a count of everything. Changing any filter, the search text or
   the sort returns to the first page, because "next page" of a client-side filter over a
   server-side window is a promise nothing can keep — the cursor would move in the unfiltered
   stream and rows would skip or repeat across the boundary. (Plan round, gemini.)
5. The blind-spots and defended tabs are unchanged.
6. Nothing regresses in what the last fix restored: the page still shows findings and
   accepted/rejected counts, and the usage-window buttons still switch.
7. **An opened row is honest about all FIVE states it can be in**, each with its own visible
   element: *asking* (the request is in flight), *failed* (the read errored or timed out, with a
   retry), *none* (the round was reviewed and nothing was found), *not kept* (no database row exists
   for it), and the list itself. Four of those look identical today — a blank — and a blank reads as
   "clean", which is the defect
   [PLAN_findings_in_the_log.md](PLAN_findings_in_the_log.md) named and did not get to
   build. The *failed* state was raised independently by all three reviewers of the plan round; the
   plan had only four states and would have left a killed process spinning for ever.

   **How *none* is told from *not kept*, without a schema change.** An empty answer cannot say which
   it is — codex was right about that. But the row already knows, because the page MERGES two
   sources: a row that has a database round is one whose findings were recorded, so empty means
   genuinely none; a row that exists only in a session file has no database round at all, so its
   findings were never written and *not kept* is the truth about it. The distinction is the row's
   ORIGIN, which the merge knows for free, not a marker the schema would have to carry. Both the
   list and the on-demand read use it.

## Build order

### 1 — the server answers a page (`src_mcp`)

**A cursor, not an offset.** `OFFSET` was in the first draft and the plan round killed it: rounds are
inserted at the TOP of this ordering, so a round finishing while somebody is on page 2 shifts every
later page by one and the reader sees a row twice or never. Paging is keyed on the value it is
ordered by instead:

- `RoundsQuery.Read(dataDir, limit, before)` where `before` is a cursor — empty for the first page.
  `DefaultLimit` becomes **200**.
- `Rounds(db, limit, before)` filters before its existing `ORDER BY … DESC LIMIT $limit`.
  **Ties matter:** `started_utc` is not unique — two rounds can start in the same second — so both
  the order and the cursor are the pair `(started_utc, id)`, and the predicate is the lexicographic
  comparison of that pair. A cursor that can skip a row on a tie is the same defect as an offset,
  arrived at from the other direction.
- **Prev needs no reverse query.** The page keeps the cursors it has already used as a stack: Next
  pushes the last row's cursor, Prev pops. That is Prev/Next and nothing else, which is the budget.
- **`FindingsByRound` is dropped from the list read.** `LoggedRound.Findings` goes away.
- **Both inputs are validated at the boundary** (plan round): a limit is clamped to 1..1000, and a
  cursor that is not the shape the server writes is treated as absent. `Program.Limit` already
  answers the default for a number nobody can read (`Program.cs:290`); the clamp is the same idea
  applied to a number somebody CAN read but should not have.

### 2 — the server counts (`src_mcp`)

One aggregate record, one query, returned beside the page:

```csharp
public sealed record LoggedTotals(
    int Rounds, int Findings, int Accepted, int Rejected, int Gating,
    long TokensIn, long TokensOut, double CostUsd);
```

Two statements, one scan each, rather than the eight scalar subqueries the first draft had — the
plan round pointed out that five separate `SELECT COUNT(*) FROM findings WHERE ...` are five passes
over one table where conditional sums are a single pass:

```sql
SELECT COUNT(*), COALESCE(SUM(tokens_in), 0), COALESCE(SUM(tokens_out), 0),
       COALESCE(SUM(cost_usd), 0)
FROM rounds;

SELECT COUNT(*), SUM(resolution = 'accept'), SUM(resolution = 'reject'), SUM(is_gating = 1)
FROM findings;
```

The columns are verified, not assumed: `rounds` carries `started_utc, completed_utc, tokens_in,
tokens_out, cost_usd, accepted, rejected` (`src_mcp/src/Store/RoundsDb.cs:205`, and the accepted /
rejected update at `RoundsDb.cs:180`); `findings` carries `role, is_gating, providers, re_raised` and
`resolution` (`RoundsDb.cs:279`).

### 3 — a round's findings on demand (`src_mcp`)

`--findings --round <sessionId> <stage> <number>` answers the findings of exactly one round, in the
same `LoggedFinding` shape the page already renders. This is the only new command, and it exists so
that opening a row costs one small read instead of the list costing 3.78 MB.

**A round it does not know is not an empty answer.** It exits non-zero with a one-line reason, so the
page can tell "no such round in the database" from "this round found nothing" — the same distinction
requirement 7 is about, settled at the protocol rather than guessed at in the renderer.

### 4 — the extension asks for a page (`src_vs_code`)

- `readLog(exe, { limit, before })` in `roundsDbRead.ts`; `readFindings(exe, key)` beside it.
  `readLog` today turns EVERY failure into `EMPTY_LOG`; `readFindings` must NOT copy that, because an
  empty log is a legible page and an empty findings list is a lie about a round. It answers a
  discriminated result — `loaded` / `absent` / `failed` — and the page renders each.
- `roundsLogPanel.ts` keeps the cursor stack in its state and answers `pageNext` / `pagePrev`.
- `roundsLog.ts`: a footer with **Prev / Next** and `200 of N rounds`, the N coming from `totals`;
  the header count line reads from `totals`, not from `ROWS.length`, and says when a filter is
  narrowing only the loaded page.
- Opening a row asks the panel for that round's findings and renders them when they arrive — the
  same markup `detail(r)` renders now, so the renderer does not change, only when it is fed and what
  it draws while it is not.
- **Asked once.** A round's findings are cached on the row after the first open; closing and
  reopening does not ask again. A *failed* read is NOT cached — retry is the whole point of the
  state.

### 5 — the boundary, named on both sides

The page merges DATABASE rounds with LIVE rounds read from the session JSON files
(`rowsFrom(sessions, …)`, `roundsLog.ts:129`).

The first draft said session rounds appear on EVERY page. The plan round showed why that is wrong:
the footer counts database rows, so a page carrying 200 counted rows plus 3 uncounted ones renders
203 things under a line that says 200, and pressing Prev then Next redraws the same live rounds in a
different place. **Session rounds belong to the FIRST page only** — they are the newest thing there
is, the first page is the newest page, and that is the same order the rest of the table is in. Page
two and beyond are database rows and nothing else.

This paragraph is repeated in `module_extension.md` beside `rowsFrom`.

## Two versions, four combinations — decided, not deferred

The plan round refused to let this stay a question, and it was right: the extension and `coai-mcp`
update separately, so all four pairings happen in the field. The mechanism is one flag.

**The new extension asks with `--paged`.** An old binary does not know it, answers `unknown argument`
and exits 64, and the extension falls back to a plain `--log`. A new binary given no `--paged`
behaves exactly as it does today.

| extension | binary | what happens |
|---|---|---|
| old | old | today's behaviour |
| **old** | **new** | no `--paged`, so the binary answers the OLD shape — findings inline, limit 300. Nothing breaks and nothing goes silently empty. |
| **new** | **old** | `--paged` is refused with exit 64; the extension retries plain `--log` and renders one unpaged list, with the footer saying that paging needs a newer `coai-mcp`. Degraded, and it SAYS it is. |
| new | new | paged, findings on demand |

`parseLog` is already defensive about absent fields (`raw.rounds ?? []`, `roundsDb.ts`), which is
what makes the second row free. A test covers each of the four.

## The growth surface this creates, and its budget

Paging invites "page 47 of 3 000" — a page that is fast and useless. The budget: **the log page gets
Prev/Next and nothing else.** No page-number strip, no jump-to-page, no configurable page size. If
somebody needs a round from six months ago they will search for it, and search over the whole table
is a different plan with a different name.

## Test plan (RED first, per the repository rule)

| # | Test | RED symptom expected |
|---|---|---|
| 1 | `RoundsQueryTests`: a database with 250 rounds answers 200, and `Totals.Rounds` says 250 | today it answers 250 and there are no totals |
| 2 | `RoundsQueryTests`: `offset` 200 answers the remaining 50, newest-first ordering preserved | no offset exists |
| 3 | `RoundsQueryTests`: a listed round carries no findings | it carries them all |
| 4 | `RoundsQueryTests`: `Totals` counts accepted, rejected and gating across the WHOLE table, not the page | no totals |
| 5 | `RoundsQueryTests`: `--findings` for one round returns exactly that round's findings, in ordinal order | the command does not exist |
| 6 | `roundsLog.test.ts`: the footer says `rows 1–200 of 250` from the totals, not from the array | no footer |
| 7 | `roundsLog.test.ts`: Next moves the offset by 200 and Prev cannot go below zero | no paging |
| 8 | `roundsLog.test.ts`: a live session round appears on every page, and is not counted in the footer | rows are not split by origin |
| 9 | `roundsLogPanel.test.ts`: opening a row asks for that round's findings once, and a second open does not ask again | findings arrive with the list |
| 10 | `roundsLog.test.ts`: the four states of an opened row each render their OWN element — asking, none, not kept, and the list — and no two of them produce the same HTML | three of the four are a blank |
| 11 | `roundsLog.test.ts`: a *failed* read renders its own element with a retry, and retrying asks again | there is no failure state |
| 12 | `RoundsQueryTests`: two rounds sharing one `started_utc` are not skipped or repeated across a page boundary | the cursor is a bare timestamp |
| 13 | `RoundsQueryTests`: a limit of 0, of -1 and of 100000 are clamped; a malformed cursor is treated as absent | unvalidated |
| 14 | `roundsDbRead.test.ts`: `--paged` refused with exit 64 falls back to a plain `--log` and the page says paging needs a newer binary | no fallback |
| 15 | `RoundsQueryTests`: `--findings` for a round the database does not hold exits non-zero, and does not answer an empty list | the command does not exist |
| 16 | `roundsLog.test.ts`: changing a filter, the search text or the sort returns to the first page | no paging |
| 17 | end-to-end against the real database: the list payload is under 100 KB and `readLog` stays under a second | it is 3.83 MB |

## Definition of Done

- [x] `--log --paged` answers 200 rounds by default, takes `--before`, and carries no findings.
- [x] `--findings` answers one round's findings, and exits 69 for a round the database does not hold.
- [x] Totals come from SQL over the whole table; the page never counts its own array to report a total.
- [x] The footer pages with Newer/Older and says which rows are shown out of how many.
- [x] ~~Live session rounds are on every page~~ — **moot**, and the plan round was right for a reason
      that turned out to be bigger than it knew: every row is a session round. See *Deviations*.
- [x] An opened row distinguishes asking, failed, none, not kept and the list — five visible
      elements, no shared blank — and *failed* offers a retry.
- [x] Paging is a cursor over `(started_utc, id)`, never an offset; a round arriving mid-read cannot
      make a row repeat or vanish.
- [x] Limits and cursors are validated at the boundary.
- [x] All four extension/binary version pairings are built and tested.
- [x] Every test above written RED first, with its failure message recorded. Server: **9 of 10 red**,
      each naming its own defect (`found 250` where 200 was asked for, `{1,2,3,1,2,3,…}` for a cursor
      that could not pass a tie, `Known to be True, but found False`). Extension: the fallback test
      was proved to have teeth by breaking the branch — `actual: 1, expected: 2`.
- [x] The whole suite green — **1024 extension tests, 1122 server tests, 0 failures**.
- [x] Documentation: `module_extension.md`, `module_server.md`, CHANGELOG, extension 0.31.17 and
      server 0.18.15, and this plan promoted.
- [x] The mixed-version question is ANSWERED and built — see *Two versions, four combinations*.

## Open tail

A row whose round is outside the 1000-round window loses its accepted/rejected badge until somebody
opens it. Bounded and honest — the row asks the server rather than guessing — but the badge is drawn
from the list, and the list has an end. Closing it means asking the database for the decision counts
of exactly the rows a page holds, which is a per-page read this change deliberately did not add.
