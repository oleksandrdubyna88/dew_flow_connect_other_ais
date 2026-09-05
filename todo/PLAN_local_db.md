# PLAN — a local database under the rounds log: structure, and a search that is real

> Status: **plan only, nothing implemented yet.** Scope: `src_mcp/src/Server/{SessionStore.cs, UsageLedger}`
> (the writer), a new `src_mcp/src/Store/` (SQLite), `src_vs_code/src/{roundsLog.ts, roundsLogPanel.ts,
> extension.ts}` (the reader), their tests, `research/module_server.md`, `module_extension.md`.
>
> Related docs: [PLAN_rounds_log_view.md](../research/PLAN_rounds_log_view.md) — the page this feeds;
> [PLAN_findings_in_the_log.md](PLAN_findings_in_the_log.md) — subsumed by this plan's findings table;
> [module_server.md](../research/module_server.md), [module_extension.md](../research/module_extension.md).

## The ask, verbatim

*"нужно добавить локал бд, чтоб поиск был норм и структура была"* — 2026-09-05, over the first rounds
log page. Today the page is built by reading every `sessions/session-*.json` on every tick and
flattening rounds into rows in memory; search is `indexOf` over a handful of strings; findings are
not in the page at all, because a session file keeps only the CURRENT round's pending findings and a
round record keeps counts.

## What exists today, verified

| What | Where |
|---|---|
| The session file: `state`, `rounds[]` (each with `reviewerStates[]`, tokens, verdict), `pending[]` (findings of the current round only) | `src_mcp/src/Server/SessionStore.cs`, written under `SessionTurn` (an OS lock file) |
| The usage ledger: one JSON line per reviewer run — `utc, provider, model, role, stage, seconds, tokensIn, tokensOut, costUsd` | `usage.jsonl` in the data dir; read whole by the extension every tick (`panelProvider.readUsage`, 80 KB / 362 rows on 2026-09-05) |
| The page's rows: flattened in the extension (`rowsFrom`), sorted/filtered/searched in the page script over JSON | `src_vs_code/src/roundsLog.ts` |
| Unparseable answers kept as files | `unparseable/*.txt` |

## What must be true when this is done

1. **One database file in the data directory** — `coai.db`, SQLite — written by the SERVER as rounds
   advance (the server already owns every write to that directory, and its `SessionTurn` lock is
   the serialisation point), and read by the extension. Sessions and the JSON files stay as they are:
   the database is a **projection** the server appends to, never the source of truth for a running
   round. A missing or corrupt database is rebuilt from the session files; nothing else depends on it.
2. **Tables with the structure the page needs**: `sessions(id, repo_path, branch, opened_utc)`,
   `rounds(id, session_id, stage, number, subject, status, verdict, gating, started_utc, completed_utc,
   tokens_in, tokens_out, cost_usd)`, `reviewers(round_id, provider, role, status, findings, seconds,
   tokens_in, tokens_out, note)`, `findings(id, round_id, severity, category, file, line, title, why, fix,
   is_gating, providers, resolution, resolved_utc)` — **every finding of every round**, with what the
   caller decided about it (`resolve` writes the decision), which is what neither the session file
   nor the page has today — and `usage(...)` mirroring the ledger.
3. **Search that is real**: an FTS5 table over `findings(title, why, fix, file)` and
   `rounds(subject, branch)`, so "every finding that mentions `FileShare`" is one query, ranked.
4. **The page reads the database**, not 37 JSON files: rows for the table, findings for an expanded
   row, spending for the tab, all by query with the filters (date range, repository, branch, stage,
   status, verdict, vendor) applied in SQL — and the page script keeps only sort and expansion.
5. **No native module in the extension.** The extension reads SQLite through `sql.js` (SQLite compiled
   to WebAssembly, pure JS, bundled with the VSIX) opened read-only over the file's bytes; the server
   writes through `Microsoft.Data.Sqlite` with the `e_sqlite3` bundle, which Native AOT publishes per
   RID already. Both are proven combinations; neither adds a platform build step.
6. **Concurrency stated, not hoped**: the server writes inside `SessionTurn` (one writer per session)
   with SQLite in WAL mode (many servers, one file — the five-window case); the extension reads a
   snapshot (`sql.js` loads the bytes; WAL means a reader never blocks a writer and never sees a torn
   page). Measured under the bench's five-window run before this is called done.

## Constraints

- **The server stays Native AOT.** `Microsoft.Data.Sqlite` + `SQLitePCLRaw.bundle_e_sqlite3` is
  AOT-compatible; no EF, no reflection-based mapping — hand-written SQL through `SqliteCommand`.
- **Append-only semantics** except `findings.resolution`, which `resolve` fills in once.
- **The bench reads it too**: `OnDisk` gains a query path, and the report's tables can come from SQL
  instead of `runs.json` scans — later, not in this plan.
- Reuse first: the page's `LogRow` shape is the `rounds ⋈ sessions` row; `rowsFrom` becomes a query
  and the pure predicates stay as the page's sort.

## Epics

### Epic 1 — the server writes the projection

1. RED: after `review_plan` returns, `coai.db` has one `sessions` row, one `rounds` row, N `reviewers`
   rows and every finding; after `resolve`, each finding's `resolution` is set. A corrupt file is
   moved aside and rebuilt from the session files at start.
2. `Store/RoundsDb.cs`: open/migrate (`PRAGMA journal_mode=WAL`), `RecordRound`, `RecordResolution`,
   `RecordUsage`; called from `PanelService` where the session and the ledger are written today.
3. The FTS5 virtual table and its triggers.
4. Startup sweep: rebuild when the file is missing, and reconcile `running` rounds the way the session
   sweep does.

### Epic 2 — the extension reads it

1. RED: `roundsFromDb(bytes)` returns the same `LogRow[]` as `rowsFrom(sessions)` for the same data
   (the projection is checked against the files it projects); a search query returns findings ranked.
2. `sql.js` in the VSIX; `RoundsDbReader` opening the file's bytes read-only; the page's provider
   pushes query results; the JSON path stays as the fallback when there is no database yet.
3. The page: findings under an expanded row (closing
   [PLAN_findings_in_the_log.md](PLAN_findings_in_the_log.md)); the search box queries FTS through the
   provider with a small debounce, results replacing the table.

### Epic 3 — measured

1. Five-window bench run with the database on: no `database is locked`, no torn read, every round
   present in `rounds` with the same numbers as its session file.
2. Size and time: the database after a month of this machine's use, the page's open time before and
   after.

## Test plan

Server: `RoundsDbTests` over a temp directory (real SQLite, no mocks); the AOT publish still builds.
Extension: `node:test` over `sql.js` with a database the server tests produced (checked into
`tests/fixtures`), asserting the reader's rows against `rowsFrom` of the same sessions. Bench: epic 3.

## Definition of Done

- [ ] `coai.db` exists after the first round, with sessions, rounds, reviewers, findings (with
      resolutions) and usage.
- [ ] The page reads the database; search is FTS; findings appear under a row.
- [ ] Five windows write it without a locked-database error, measured.
- [ ] Server stays Native AOT; the extension carries no native module.
- [ ] `module_server.md` and `module_extension.md` describe the projection; this plan promoted.
