# PLAN — the log asks for a page, and lets SQL do the counting

> Status: **plan only, nothing implemented yet.** Scope: `src_mcp/src/Store/RoundsQuery.cs`,
> `src_mcp/src/Program.cs`, `src_vs_code/src/roundsDbRead.ts`, `src_vs_code/src/roundsLog.ts`,
> `src_vs_code/src/roundsLogPanel.ts`.
>
> Related docs: [module_extension.md](../research/module_extension.md),
> [module_server.md](../research/module_server.md),
> [PLAN_the_log_never_gets_its_findings.md](../research/PLAN_the_log_never_gets_its_findings.md) —
> the fix that made this payload reach the page at all, and whose open tail this plan closes.
>
> **Boundary, named on both sides.** This plan takes over the one unfinished item of
> [PLAN_findings_in_the_log.md](../research/PLAN_findings_in_the_log.md): a round whose findings are
> not kept must SAY so rather than draw a blank that reads as "clean". That plan shipped its list
> through the rounds database instead of the session files it was written against, which left the
> absent case unbuilt — and this plan adds a fourth state to it, because findings that arrive on a
> click can also be *not yet asked for*. The search box that plan's sibling
> [PLAN_local_db_reader.md](../research/PLAN_local_db_reader.md) left unbuilt is NOT here: it is
> [PLAN_the_log_searches_the_findings.md](PLAN_the_log_searches_the_findings.md).

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
4. Sorting, filtering and search keep working over the rows that are loaded, and the page says
   plainly which rows those are — a filter that silently searches only page 1 is worse than no
   filter.
5. The blind-spots and defended tabs are unchanged.
6. Nothing regresses in what the last fix restored: the page still shows findings and
   accepted/rejected counts, and the usage-window buttons still switch.
7. **An opened row is honest about all four states it can be in**, each with its own visible element:
   *asking* (the request is in flight), *none* (the round was reviewed and nothing was found),
   *not kept* (the round predates the database, so its findings were never recorded), and the list
   itself. Three of those look identical today — a blank — and a blank reads as "clean", which is the
   defect [PLAN_findings_in_the_log.md](../research/PLAN_findings_in_the_log.md) named and did not
   get to build.

## Build order

### 1 — the server answers a page (`src_mcp`)

- `RoundsQuery.Read(dataDir, limit, offset)`; `DefaultLimit` becomes **200**.
- `Rounds(db, limit, offset)` gains `OFFSET $offset` after its existing
  `ORDER BY r.started_utc DESC LIMIT $limit`.
- **`FindingsByRound` is dropped from the list read.** `LoggedRound.Findings` goes away.
- `Program.Limit` gains a sibling `Program.Offset`, reading `--offset`; both keep the existing
  "a number nobody can read is the default" behaviour (`Program.cs:290`).

### 2 — the server counts (`src_mcp`)

One aggregate record, one query, returned beside the page:

```csharp
public sealed record LoggedTotals(
    int Rounds, int Findings, int Accepted, int Rejected, int Gating,
    long TokensIn, long TokensOut, double CostUsd);
```

```sql
SELECT (SELECT COUNT(*) FROM rounds),
       (SELECT COUNT(*) FROM findings),
       (SELECT COUNT(*) FROM findings WHERE resolution = 'accept'),
       (SELECT COUNT(*) FROM findings WHERE resolution = 'reject'),
       (SELECT COUNT(*) FROM findings WHERE is_gating = 1),
       (SELECT COALESCE(SUM(tokens_in), 0) FROM rounds),
       (SELECT COALESCE(SUM(tokens_out), 0) FROM rounds),
       (SELECT COALESCE(SUM(cost_usd), 0) FROM rounds)
```

The columns are verified, not assumed: `rounds` carries `started_utc, completed_utc, tokens_in,
tokens_out, cost_usd, accepted, rejected` (`src_mcp/src/Store/RoundsDb.cs:205`, and the accepted /
rejected update at `RoundsDb.cs:180`); `findings` carries `role, is_gating, providers, re_raised` and
`resolution` (`RoundsDb.cs:279`).

### 3 — a round's findings on demand (`src_mcp`)

`--findings --round <sessionId> <stage> <number>` answers the findings of exactly one round, in the
same `LoggedFinding` shape the page already renders. This is the only new command, and it exists so
that opening a row costs one small read instead of the list costing 3.78 MB.

### 4 — the extension asks for a page (`src_vs_code`)

- `readLog(exe, { limit, offset })` in `roundsDbRead.ts`; `readFindings(exe, key)` beside it.
- `roundsLogPanel.ts` keeps the current offset in its state and answers a `page` command.
- `roundsLog.ts`: a footer with **Prev / Next / "rows 1–200 of N"**, the N coming from
  `totals.Rounds`; the header count line reads from `totals`, not from `ROWS.length`.
- Opening a row asks the panel for that round's findings and renders them when they arrive — the
  same shape `detail(r)` renders now, so the renderer does not change, only when it is fed.

### 5 — the boundary, named on both sides

The page merges DATABASE rounds with LIVE rounds read from the session JSON files
(`rowsFrom(sessions, …)`, `roundsLog.ts:129`). **Session rounds are never paged**: they are the
handful that are running right now, they are what the sidebar exists for, and hiding a running round
behind a Next button would be the opposite of the page's purpose. So a page is "200 database rows
plus whatever is live", and the footer counts the database rows only. This sentence is repeated in
`module_extension.md` beside `rowsFrom`.

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
| 11 | end-to-end against the real database: the list payload is under 100 KB and `readLog` stays under a second | it is 3.83 MB |

## Definition of Done

- [ ] `--log` answers 200 rounds by default, takes `--offset`, and carries no findings.
- [ ] `--findings` answers one round's findings.
- [ ] Totals come from SQL over the whole table; the page never counts its own array to report a total.
- [ ] The footer pages with Prev/Next and says which rows are shown out of how many.
- [ ] Live session rounds are on every page and are excluded from the database count, said on both
      sides of the seam.
- [ ] An opened row distinguishes asking, none, not kept and the list — four visible elements, no
      shared blank.
- [ ] Every test above written RED first, with its failure message recorded.
- [ ] The whole suite green — both the extension's and `src_mcp`'s.
- [ ] Documentation: `module_extension.md`, `module_server.md`, CHANGELOG, versions bumped for
      BOTH halves, and this plan promoted.
- [ ] The `X-Coai-Contract` question answered explicitly: `--log`'s output shape changes, so an old
      extension against a new binary must not silently show an empty page. Decide whether the
      installer's version pin already covers it, and write the answer down either way.
