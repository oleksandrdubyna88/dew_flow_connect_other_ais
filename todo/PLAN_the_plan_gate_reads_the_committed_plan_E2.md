# PLAN — E2 stories: plan provenance in history and in the rounds log

> Status: **plan only, nothing implemented yet, 2026-10-15** — written together with E1's stories; starts when E1 has
> merged. Epic E2 of [PLAN_the_plan_gate_reads_the_committed_plan.md](PLAN_the_plan_gate_reads_the_committed_plan.md).
>
> Scope: one schema step (`rounds.plan_source`) with its projection write, the `--log` read, and one detail line in the
> extension's rounds log; `research/module_server.md`, `research/module_extension.md`, `research/architecture.md`. No new
> argument, no prompt change, no Team server change. No cornerstone in this epic (the parent's ledger:
> `Cornerstones: 1 of 1 — E1.S2`). Stories of E1: [PLAN_the_plan_gate_reads_the_committed_plan_E1.md](PLAN_the_plan_gate_reads_the_committed_plan_E1.md).

## 1. Goal, before any solution

E1 records what each plan round reviewed in the session file and `status`. This epic exposes that record where history is
read: `--log` (from the database) and the rounds log page (from the session file). The failure to avoid is the one D10 and
D11 name: a round from before the field, or a database from before the step, reading as an EMPTY source instead of *not
recorded*. No backfill (parent `## Epics`, E2): a plan round finished between E1's merge and E2's reads `''` in the
database and keeps its `PlanSource` in its session file.

## 2. What exists — verified at `7de47482`, 2026-10-15

| Fact | Where |
|---|---|
| Schema: 21 append-only steps, last `TheRoundsKey` (step 21, index 20) | `src_mcp/src/Store/Schema.cs:31-38`, `:603-616` |
| The migrator runs `steps[version]`, then sets `user_version = version + 1` | `src_mcp/storage/SqliteMigrator.cs:89-90` |
| The step-21 test pins `Steps.Length == 21`; its `MakeAt`/`Shape` helpers | `src_mcp/tests/TheRoundKeyIsStepTwentyOneTests.cs:67`, `:79` |
| `RecordRound` = one transaction; `RecordRoundRow` is one upsert on `(session_id, stage, number)` with binds from `round` | `src_mcp/src/Store/RoundsDb.cs:190-202`; `:841-921` (column list `:847-851`, `ON CONFLICT` `:857-867`, binds `:902-918`) |
| A JSON column bound through the source-generated context (`$commands`) | `RoundsDb.cs:904-905` |
| `RoundsDb.cs` is 1 285 lines, `RoundsQuery.cs` 991 — past the 800 ceiling; the seam for code elsewhere | `RoundsDb.cs:181` (`Command()`) |
| `LoggedRound` — positional, trailing defaults, last `string Note = ""` | `src_mcp/src/Store/RoundsQuery.cs:27-102` |
| The rounds page read: a three-text SQL ladder chosen by `HasColumn`; one grouped findings query per page | `RoundsQuery.cs:470-531` (ladder `:487-489`); `FindingsByRound` `:476`, `:579` |
| `HasColumn(db, table, column)` | `RoundsQuery.cs:895-903` |
| `--log` = `LogJson` → `RoundsQuery.Read` → `LoggedLog` through `ServerJsonContext` (nulls omitted) | `src_mcp/src/Program.cs:715-745`; `RoundsQuery.cs:410`; `src/Server/ServerJsonContext.cs:344`, `:360` |
| `--log` as a process, on old files | `src_mcp/tests/LogCliScenarioTests.cs` |
| The rounds log reads the SESSION FILE: `SessionFile`/`RoundRecord` imported; a row built per round | `src_vs_code/src/roundsLog.ts:20`, `rowFrom` `:466-527` (`subject` `:501`) |
| The precedent for a detail line: `calledBy(round.caller)` → `LogRow.calledBy` → `askedByHtml` in the page's `detail(row)` | `src_vs_code/src/rounds.ts:148`; `roundsLog.ts:153`, `:384`, `:519`, `:462`, `:1637`, `:1733-1737` |
| `RoundRecord` (TS) optional fields; `roundsLog.ts` is 2 376 lines | `src_vs_code/src/rounds.ts:51-100` |
| Tests to mirror: the row field; the page as it SHIPS (bundled, minified) | `src_vs_code/src/test/roundsLog.test.ts:514-546`; `src_vs_code/src/test/bundledPage.test.ts:173` |
| Where the module doc lists schema steps; the `--log` seam rule | `research/module_server.md:3268`; `research/architecture.md:753` |

## 3. Conventions every story follows

E1's §3 applies (tests first, RED on the assertion; the MTP executable, never `dotnet test`; break-its in production code
with both runs reported). Plus:

- **Extension**: `cd src_vs_code && npm ci && npm test`; the page is checked as it ships (`bundledPage.test.ts`).
- **No new lines in `RoundsDb.cs` or `RoundsQuery.cs` beyond one column in the upsert, one bind, and one call** — the
  read lives in a new file through a read-only connection, as the size epic's readers do.
- **Not recorded is never empty.** `''` in the column, an absent field in JSON, no line on the page.
- **The step number is taken at merge** (D11): written below as step 22 / index 21; the PR that merges after another
  step-adding PR (the round-size epic's step 22, the authors epic's step 23) renumbers its constant, its test name and its
  doc paragraph before merging. No build of this branch is pointed at a real data directory.

## 4. Shared contracts

- **C1 — the column.** `ALTER TABLE rounds ADD COLUMN plan_source TEXT NOT NULL DEFAULT '';` — the JSON of E1's
  `PlanSource` (E1 §4 C1) through `ServerJsonContext.Default.PlanSource`, camelCase, `''` = not recorded.
- **C2 — the wire.** `LoggedRound.PlanSource` is `PlanSource?`; absent on the page JSON when null (`WhenWritingNull`).
  An older extension ignores the field (it is additive); the Team server reads neither `--log` nor the session file.
- **C3 — the line.** Committed: `<path> @ <commit7> · blob <blob7> · <bytes> bytes`; text: `pasted planText @ <commit7> ·
  <bytes> bytes` (`@ <commit7>` omitted when the commit is empty); `<bytes>` grouped by three with a space (`98 304`).
  Decided here: D16 named only the committed form, and a text round deserves a line too — it is "what was reviewed".

## 5. Stories

### E2.S1 — Schema step 22: `rounds.plan_source`, written with the round

- **Goal.** Every plan round recorded after the step writes its `PlanSource` JSON; every other round writes `''`; every
  database shape in the field reaches the step; read-only readers of a step-21 file keep answering.
- **Scope.** `Schema.cs`: `internal const string ThePlanSource` = C1, appended after `TheRoundsKey` in `Steps` (`:37`),
  with a doc comment in the step-21 style (`:603-613`). `RoundsDb.RecordRoundRow`: `plan_source` in the column list, the
  `VALUES` list and the `ON CONFLICT` set; one bind `$planSource` from `round.PlanSource` (`''` when null), commented as
  `$roundKey` is (`:916-918`). `TheRoundKeyIsStepTwentyOneTests.cs:79` relaxed to `≥ 21` (step 21 did the same to
  step 20's test). `research/module_server.md`: a step paragraph after `:3268`.
- **Brief.** Mirror `TheRoundKeyIsStepTwentyOneTests` — `MakeAt`, `Shape`, both forks of the preview check. The column is
  rewritten on every re-record from the record itself, so a re-recorded round keeps the same value.
- **Test scenarios** (`ThePlanSourceIsStepTwentyTwoTests.cs`, real SQLite; RED: step present, bind writes `''`):
  `AStepTwentyOneDatabase_OpensAtTwentyTwo_WithEveryRowNotRecorded` · `ACommittedPlanRound_WritesItsSourceAsJson` (round
  trip through `ServerJsonContext` equals the record) · `APlanTextRound_WritesOriginText` · `ACodeRound_WritesNothing` ·
  `TheSameRoundRecordedTwice_KeepsOneRowAndOneValue` · `AReadOnlyOpenOfAStepTwentyOneFile_LeavesItAtTwentyOne`.
- **Break-its.** **B16** bind `''` always → `ACommittedPlanRound_WritesItsSourceAsJson` red.
- **Done when.** Scenarios, `TheRoundKeyIsStepTwentyOneTests`, `RoundsDbTests` green; the whole executable green.
- **Depends on.** E1 merged (`RoundRecord.PlanSource`, the `PlanSource` registration). **Size.** ≈ 200 lines (≈ 30 code
  + doc, ≈ 170 tests). **Coder: sonnet** · review: opus.

### E2.S2 — `--log` names each plan round's source, and a step-21 file still answers

- **Goal.** T13's `--log` half: a round recorded after the step carries `planSource`; a round recorded before it, and
  every round of a step-21 database, carries nothing.
- **Scope.** `LoggedRound` gains a trailing `PlanSource? PlanSource = null` (`RoundsQuery.cs:27-102`). New
  `src/Store/RoundPlanSources.cs`: `ByRound(SqliteConnection db, IReadOnlyCollection<long> ids)` → `Dictionary<long,
  PlanSource>`, one `SELECT id, plan_source FROM rounds WHERE id IN (…)` per page, run ONLY when `HasColumn(db, "rounds",
  "plan_source")`; `''` and JSON that does not parse map to absent (a malformed row never fails the page). `Rounds(...)`
  (`:470-531`) collects the ids and applies it with `with { PlanSource = … }` — one call; the SQL ladder is not given a
  fourth text. `research/architecture.md` § *A field added to the round list* (`:753`): one more field, same rule.
- **Brief.** Follow `FindingsByRound` (`:579`) for the one-query-per-page shape. Bind ids as parameters, never by string
  concatenation of values.
- **Test scenarios** (`TheLogNamesEachPlansSourceTests.cs` for the reader; `LogCliScenarioTests.cs` gains one case):
  `ARoundAfterTheStep_CarriesItsSource` · `ARoundBeforeTheStep_CarriesNone` (row with `''`) ·
  `AMalformedColumn_IsAbsentNotAnError` · `AStepTwentyOneFile_IsLoggedAsBefore` (process: exit 0, rounds present, no
  `planSource` key, file still at 21) · `APageOfTwoHundredRounds_AsksOnce` (a counting connection or the query log).
- **Break-its.** **B10** remove the `HasColumn` guard → `AStepTwentyOneFile_IsLoggedAsBefore` red (no such column / exit
  74).
- **Done when.** Scenarios, `RoundsQueryTests`, `LogCliScenarioTests` green; `architecture.md` updated.
- **Depends on.** E2.S1. **Size.** ≈ 230 lines (≈ 60 code + doc, ≈ 170 tests). **Coder: sonnet** · review: opus.

### E2.S3 — The rounds log shows what a plan round reviewed

- **Goal.** T17: opening a plan round's row shows C3's line when its session record has `planSource`, and nothing when it
  does not (an older server, a pre-E1 round, a code round).
- **Scope.** `rounds.ts`: `interface PlanSourceRecord { origin; commit; path; blob; bytes; textId }` (all optional,
  read defensively) and `readonly planSource?: PlanSourceRecord` on `RoundRecord`; a pure exported `reviewedFrom(source)`
  beside `calledBy` (`:148`) → C3's string or `''` when absent or unusable (no origin, a non-numeric `bytes`).
  `roundsLog.ts`: `LogRow.reviewedFrom: string` beside `calledBy` (`:153`), set at `:519`, `''` for a conversation row
  (`:384`); the page's `detail(row)` (`:1733`) adds `<div class="reviewer source">` + `esc(row.reviewedFrom)` after the
  asked-by line, only when non-empty. `research/module_extension.md`: the rounds log section gains the line.
- **Brief.** Do not read the database for this — the row is built from the session file (parent `## Epics`, the
  consultant correction). `esc` every value: `path` is caller-chosen text. No new action, so no new button (D16).
- **Test scenarios.** `rounds.test.ts`: `reviewedFrom` for a committed source, a text source with and without a commit,
  `undefined`, an object missing `origin`, `bytes: "x"`. `roundsLog.test.ts` (the `:514` shape): the row carries the
  string; a round without the field carries `''`. `bundledPage.test.ts` (the `:173` shape): opening a row with
  `planSource` shows the line; opening one without shows no `source` element.
- **Break-its.** **B11** render the line for an absent `planSource` (drop the non-empty test) → the "shows nothing" case
  red.
- **Done when.** Scenarios green; `npm test` green; the page bundle builds; `module_extension.md` updated.
- **Depends on.** E1 merged (the session-file field); NOT on E2.S1/S2. **Size.** ≈ 180 lines (≈ 40 code + doc, ≈ 140
  tests). **Coder: sonnet** · review: opus.

## 6. Build order

```
E1 merged ──► S1 schema + writer ──► S2 --log
          └─► S3 rounds-log line (parallel with S1)
```

One pull request on its own branch, the coai gate once (parent `## Epics`). Before merging: compare `Schema.Steps` with
`main` and renumber if another step landed first.

## 7. Test plan — the parent's rows this epic owns

| Row | Story | Kind |
|---|---|---|
| T13: the `--log` row has the source; a database without the column reads `null` | S1, S2 | real SQLite + process |
| T17: the page shows the line / shows nothing | S3 | node test, bundled page |
| Break-its B10 (S2), B11 (S3), B16 (S1) | each story | red → restore |

## 8. Growth

`rounds.plan_source` ≈ 200 B per plan round; at 50 plan rounds a day ≈ 3.6 MB a year, kept for as long
as its round row (parent §9). One more query per `--log` page. No directory, cache or process.

## 9. Definition of Done — epic E2

- [ ] A step-21 database opens at the new step; read-only `--log` on a step-21 file answers and leaves it at 21.
- [ ] A round recorded before the step reads *not recorded* in `--log`; one recorded after it shows commit, path, blob
      and bytes; a text round shows its origin.
- [ ] The rounds log shows C3's line for a round with `planSource` and nothing without.
- [ ] B10, B11, B16 observed red then green; `CoaiMcp.Tests.exe` and `npm test` green.
- [ ] The step number checked against `main` at merge; `module_server.md`, `module_extension.md`, `architecture.md`
      updated; `node .agents/conventions/tools/plan-lifecycle.mjs` clean.
- [ ] The parent, E1's and this document promoted together.
