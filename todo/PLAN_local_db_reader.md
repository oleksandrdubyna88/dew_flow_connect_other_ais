# PLAN — the rounds log page reads the database

> Status: **plan only, nothing implemented yet.** Scope: `src_vs_code/src/{roundsLog.ts,
> roundsLogPanel.ts, extension.ts}`, a `sql.js` dependency in the VSIX, their tests,
> `research/module_extension.md`.
>
> The other half of [research/PLAN_local_db.md](../research/PLAN_local_db.md), whose Epic 1 shipped on
> 2026-09-05: the server now writes `coai.db` with every finding, its resolution and the reason. The
> page still flattens the session files on every tick and therefore still shows counts.

## What must be true when this is done

1. **The page reads `coai.db`**, not 37 JSON files: rows for the table, findings for an expanded row,
   spending for the tab — by query, with the filters (date range, repository, branch, stage, status,
   verdict, vendor) applied in SQL. The page script keeps only sort and expansion.
2. **Findings appear under an expanded row** — severity, file:line, what it said, and what was decided
   about it with the reason. This closes [PLAN_findings_in_the_log.md](PLAN_findings_in_the_log.md).
3. **Search is FTS**: the search box queries `findings_fts` through the provider with a small
   debounce, and the results replace the table.
4. **No native module.** `sql.js` (SQLite compiled to WebAssembly) bundled with the VSIX, opened
   read-only over the file's bytes. The JSON path stays as the fallback while a database does not
   exist yet — an extension that shows nothing on a machine whose server predates this is worse than
   one that shows what it can.
5. **The projection is checked against what it projects**: a test asserts `roundsFromDb(bytes)`
   returns the same rows as `rowsFrom(sessions)` for the same data.

## What this is FOR, and the query that has to be cheap

The operator's purpose for this data (2026-09-05) is finding the blind spots in an AI's own
reasoning: which findings does it habitually accept — that is, which things did it not see and then
agree were worth having — and which does it argue with and then have raised again. The reader should
make those two queries first-class rather than something a person exports and pivots elsewhere:

- accepted findings by category, by role and by vendor, over a date range;
- rejections that were later `re_raised`, with both texts and the reason given;
- a round's `agent_log` beside its findings, which is what makes "what was being done when this was
  missed" answerable at all.

## What the gate found, 2026-09-06 (three reviewers, eight findings taken)

1. **WAL.** SQLite in write-ahead mode keeps committed transactions in `coai.db-wal`, and `sql.js`
   over a byte snapshot can neither take a read lock nor see that file — so the page would silently
   miss the newest findings and can hit `SQLITE_CORRUPT` on a torn read. Checkpoint before reading,
   copy under a lock, or read through the server. **This is a prerequisite, not a detail.**
2. **The fallback condition is too narrow.** "While a database does not exist yet" leaves out a
   zero-byte, corrupt, locked or newer-schema file — all present, all unusable. Open, schema-check and
   query failures all route to the JSON path, and the UI says which source it is showing.
3. **Pushing every filter into SQL breaks the fallback**, which has no engine. The filters live behind
   a provider abstraction with a predicate implementation for JSON.
4. **FTS returns findings; the table renders rounds.** The search must return the distinct rounds that
   matched, with the matching findings flagged for expansion.
5. **`agent_log` is not in the database** — Epic 1 put findings, resolutions and reasons there. Either
   it joins the schema in an explicit step or it is read from the session file on expansion.
6. **The blind-spot views are the PURPOSE and the least specified part**: they need inputs, a provider
   method, an empty state and their own Definition-of-Done lines, or a build ticks every box without
   them.
7. **One row-equality test proves the table, not the detail.** Findings, resolutions, reasons,
   spending, ordering and row identity are what the change is for.
8. **The filter contract needs writing down**: date inclusivity, nulls, how filters combine, whether
   the search respects them, what clearing does.

## Build order

1. RED: `roundsFromDb` over a database the server tests produced (checked into `src_vs_code/src/test/fixtures`).
2. `sql.js` in the VSIX; a reader opening the bytes read-only; the provider pushes query results.
3. Findings under an expanded row; FTS behind the search box.
4. The two blind-spot views above.

## Definition of Done

- [ ] The page reads the database, with the JSON path as the fallback.
- [ ] Findings, resolutions and reasons appear under an expanded row.
- [ ] Search is FTS, debounced.
- [ ] A test asserts the database rows equal the session-file rows for the same data.
- [ ] `module_extension.md` describes the reader; this plan promoted.
