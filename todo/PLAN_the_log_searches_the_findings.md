# PLAN — the log's search box asks the full-text index

> Status: **plan only, nothing implemented yet.** Scope: `src_mcp/src/Store/RoundsQuery.cs`,
> `src_mcp/src/Program.cs`, `src_vs_code/src/{roundsDbRead.ts, roundsLog.ts, roundsLogPanel.ts}`.
>
> Extracted from [research/PLAN_local_db_reader.md](../research/PLAN_local_db_reader.md) when that
> plan was promoted on 2026-09-09: requirement 3 of it, and the only one of its requirements that was
> never built.
>
> **Boundary, named on both sides.** Paging and the SQL totals are
> [PLAN_the_log_asks_for_a_page.md](PLAN_the_log_asks_for_a_page.md). That plan owns *which rows the
> page holds*; this one owns *how a person finds a row that is not among them*. They meet at one
> point: a search must not be a filter over the loaded page, and that plan's footer must be able to
> say it is showing search results rather than a page. Build that one first.

## The gap

`findings_fts` is an FTS5 virtual table over a finding's title, why, fix and file, kept in step with
`findings` by triggers (`src_mcp/src/Store/Schema.cs:115`). **Nothing queries it.** The log page's
search box filters the rows it was sent, in the webview, over the row's own visible text
(`rowMatches`, `src_vs_code/src/roundsLog.ts:474`) — so searching for a sentence that appears in a
finding's *why* finds nothing, and searching at all only ever reaches the loaded page.

The index exists because the plan that created the schema expected this. It has been dead weight in
every database written since 2026-09-05.

## What must be true when this is done

1. Typing in the search box queries `findings_fts` and the table shows the ROUNDS whose findings
   matched, newest first — not a filtered view of the page.
2. The page says plainly that it is showing search results and how many, and one click returns to the
   paged list.
3. The query is debounced, and a query that arrives while an older one is in flight cannot overwrite
   its successor's results.
4. FTS5 syntax a person types by accident (`AND`, a bare `"`, a stray `*`) does not throw — a
   malformed query is answered as "no matches", never as an error dialogue.
5. The row-text filter that exists today stays for what it is good at — narrowing the visible page by
   branch or repository without a round trip. The two are not the same control and must not look like
   one.

## Open questions to answer IN the plan round

- Does a search reach findings whose round is older than the retention the page pages through, and
  should it? A search that silently stops at the same horizon as the list is a search nobody can
  trust; one that does not needs a bound of its own.
- FTS5 ranking or plain `started_utc DESC`? Ranking is what the index is for, but a round is not a
  document, and the thing being ranked is the finding, not the row shown.

## Test plan (RED first)

| # | Test | RED symptom expected |
|---|---|---|
| 1 | `RoundsQueryTests`: a phrase that appears only in a finding's `why` finds its round | no search query exists |
| 2 | `RoundsQueryTests`: a malformed FTS query answers empty, not an exception | it throws |
| 3 | `RoundsQueryTests`: results are bounded and say whether they were cut | unbounded |
| 4 | `roundsLogPanel.test.ts`: a stale response cannot replace a newer one | no sequencing |
| 5 | `roundsLog.test.ts`: the header says it is showing search results, and the clear control returns to the page | no such state |

## Definition of Done

- [ ] The search box queries FTS through the server; the page renders matched rounds.
- [ ] Search state is visible and reversible in one click.
- [ ] Malformed input cannot throw.
- [ ] Debounced, and out-of-order responses cannot win.
- [ ] Every test above written RED first, with its failure message recorded.
- [ ] Documentation: `module_extension.md`, `module_server.md`, CHANGELOG, both versions bumped,
      this plan promoted.
