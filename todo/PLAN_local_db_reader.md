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
