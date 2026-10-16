# PLAN — E2 stories: plan provenance in history and in the rounds log

> Status: **plan only, nothing implemented yet, 2026-10-16** — written 2026-10-15 with E1's stories, revised 2026-10-16
> against `main` at `03ec1c1a` (E1 merged as #837; the round-size step 22 merged as #829). Epic E2 of
> [PLAN_the_plan_gate_reads_the_committed_plan.md](PLAN_the_plan_gate_reads_the_committed_plan.md).
>
> Scope: one schema step — **step 23** (`rounds.plan_source`) — with its projection write, the `--log` read, and one detail
> line in the extension's rounds log; `research/module_server.md`, `research/module_extension.md`,
> `research/architecture.md`. No new argument, no prompt change, no Team server change. No cornerstone in this epic (the
> parent's ledger: `Cornerstones: 1 of 1 — E1.S2`). Stories of E1:
> [PLAN_the_plan_gate_reads_the_committed_plan_E1.md](PLAN_the_plan_gate_reads_the_committed_plan_E1.md) (its § 10 says
> what shipped differently).

## 1. Goal, before any solution

E1 records what each plan round reviewed in the session file and `status`. This epic exposes that record where history is
read: `--log` (from the database) and the rounds log page (from the session file). The failure to avoid is the one D10 and
D11 name: a round from before the field, or a database from before the step, reading as an EMPTY source instead of *not
recorded*. No backfill (parent `## Epics`, E2): a plan round finished between E1's merge and E2's reads `''` in the
database and keeps its `PlanSource` in its session file.

## 2. What exists — verified at `03ec1c1a`, 2026-10-16

| Fact | Where |
|---|---|
| Schema: **22** append-only steps, last `TheRoundsSize` (step 22, index 21) | `src_mcp/src/Store/Schema.cs:31-38`; step 21 `:602-616`, step 22 `:618-652` |
| The migrator runs `steps[version]`, then sets `user_version = version + 1` — step *n* is index *n−1*, `user_version` *n* | `src_mcp/storage/SqliteMigrator.cs:89-90` |
| The step tests already pin `≥`, not `==`: step 21's `Steps.Length ≥ 21`, step 22's `≥ 22` with its position pins `SizeStep == 21`, `Steps[21] == TheRoundsSize`; `Shape()` compares against `Steps.Length` | `src_mcp/tests/TheRoundKeyIsStepTwentyOneTests.cs:67`, `:80`; `TheRoundsSizeIsStepTwentyTwoTests.cs:72`, `:110-115` (`MakeAt` `:42`, `Shape` `:53`) |
| No other test pins the count: every other check reads `Schema.Steps.Length` or `≥` | `ASkippedRoundIsWrittenDownTests.cs:49`, `:153`; `RoundsDbTests.cs:566`; `QuestionConsultProjectionTests.cs:97` |
| The step-12 re-run test drops every column added after step 12, step 22's six included; its comment stops at step 21 | `src_mcp/tests/ThePairsThemselvesTests.cs:518-553` (drops `:528-551`) |
| `RecordRound` = one transaction: row, reviewers, `RoundChangesTable.Record`, findings | `src_mcp/src/Store/RoundsDb.cs:190-202` |
| `RecordRoundRow` = one upsert on `(session_id, stage, number)`; binds from `round` | `RoundsDb.cs:842-922` (columns `:848-852`, `VALUES` `:853-857`, `ON CONFLICT` `:858-868`, `$roundKey` `:916-919`) |
| A JSON column bound through the source-generated context (`$commands`) | `RoundsDb.cs:905-907` |
| `RoundsDb.cs` 1 289 lines, `RoundsQuery.cs` 1 005 — past the 800 ceiling; the seam for code elsewhere | `RoundsDb.cs:181` (`Command()`) |
| `LoggedRound` — positional, last `string Note = ""`; step 22's field is an **init property** `Size`, not a positional one | `src_mcp/src/Store/RoundsQuery.cs:27-114` (`Size` `:113`) |
| The rounds page read: a three-text SQL ladder chosen by `HasColumn`; the page's ids handed to `RoundChangesLog.Attach` | `RoundsQuery.cs:482-545` (ladder `:499-501`, `Attach` `:544`) |
| The page-scoped reader to mirror: `HasColumn` first, ids bound as one JSON array through `json_each($ids)` | `src_mcp/src/Store/RoundChangesLog.cs:23-42`, `:52`, `BindIds` `:122-123` |
| `HasColumn(db, table, column)` | `RoundsQuery.cs:909-917` |
| `--log` = `LogJson` → `RoundsQuery.Read` → `LoggedLog` through `ServerJsonContext` (nulls omitted) | `src_mcp/src/Program.cs:715-751`; `RoundsQuery.cs:422`; `src/Server/ServerJsonContext.cs:339-344`, `:362` |
| `PlanSource(Origin, Commit, Path, Blob, Bytes, TextId)`; `Origin` is `commit` or `text` (`PlanOrigin`) | `src_mcp/core/Rounds/PlanSource.cs` |
| `RoundRecord.PlanSource` — nullable, not normalised; null for code/document/feature/skipped and pre-E1 rounds | `src_mcp/src/Server/SessionStore.cs:213-223` |
| The stamp: plan rounds only, `Commit` = the engine's own sha for both origins; a committed round's subject is its file name | `src_mcp/src/Server/Rounds/PlanSources.cs:17-29` |
| `PlanSource` is registered by name for this column | `ServerJsonContext.cs:345-347` |
| The `--log` size reader's tests, process case included (`OneShotProcess`) | `src_mcp/tests/TheLogNamesEachRoundsSizeTests.cs:121-152` |
| The rounds log reads the SESSION FILE: `SessionFile`/`RoundRecord` imported; a row built per round | `src_vs_code/src/roundsLog.ts:20`, `rowFrom` `:466-527` (`subject` `:501`) |
| The detail-line precedent: `calledBy(round.caller)` → `LogRow.calledBy` → `askedByHtml(calledBy, escape)`, exported and embedded by `toString()`, called in `detail(row)` before the early returns | `src_vs_code/src/rounds.ts:148-159`; `roundsLog.ts:153`, `:384`, `:519`, `:462-464`, `:1637`, `:1733-1745` |
| `RoundRecord` (TS) optional fields; `roundsLog.ts` 2 376 lines, `rounds.ts` 516 | `src_vs_code/src/rounds.ts:51-93` |
| Tests to mirror: the row field; the line function itself; the page as it SHIPS | `src_vs_code/src/test/roundsLog.test.ts:514-546`, `:556-575`; `bundledPage.test.ts:173-180` |
| An older extension reading a `--log` page that carries a new member | `src_vs_code/src/test/anOlderExtensionReadsALogPageCarryingASize.test.ts` |
| Where the module doc lists schema steps; the `--log` seam rule; the asked-by line | `research/module_server.md:3446-3484`; `research/architecture.md:754`; `research/module_extension.md:6426-6459` |

## 3. Conventions every story follows

E1's §3 applies (tests first, RED on the assertion; the MTP executable, never `dotnet test`; break-its in production code
with both runs reported). Plus:

- **Extension**: `cd src_vs_code && npm ci && npm test`; the page is checked as it ships (`bundledPage.test.ts`).
- **No new lines in `RoundsDb.cs` or `RoundsQuery.cs` beyond one column in the upsert, one bind, and one call** — the
  read lives in a new file through the read-only connection, as `RoundChangesLog` does.
- **Not recorded is never empty.** `''` in the column, an absent field in JSON, no line on the page.
- **The step number is 23** (index 22, `user_version` 23) — `main` holds 22 steps and no other step-adding PR is open on
  it today. D11's rule still binds: before merging, compare `Schema.Steps` with `main`; if a step landed first, this PR
  renumbers its constant's doc comment, its test name and its doc paragraph. No build of this branch is pointed at a real
  data directory. The authors plan's E5 moves to step 24 (its documents say so, 2026-10-16).

## 4. Shared contracts

- **C1 — the column.** `ALTER TABLE rounds ADD COLUMN plan_source TEXT NOT NULL DEFAULT '';` — the JSON of E1's
  `PlanSource` as shipped (`core/Rounds/PlanSource.cs`) through `ServerJsonContext.Default.PlanSource`: camelCase
  (`origin`, `commit`, `path`, `blob`, `bytes`, `textId`), indented as that context writes. `''` = not recorded.
- **C2 — the wire.** `LoggedRound` gains an **init property** `PlanSource? PlanSource { get; init; }` — the `Size`
  precedent (`RoundsQuery.cs:113`), so the positional constructor and every call site stay as they are; this amends D11's
  "trailing positional parameter". JSON `planSource`, absent when null (`WhenWritingNull`). An older extension ignores the
  member (`parseLog` reads field by field); the Team server reads neither `--log` nor the session file.
- **C3 — the line.** Committed: `<path> @ <commit7> · blob <blob7> · <bytes> bytes`; text: `pasted planText @ <commit7> ·
  text <textId> · <bytes> bytes` (`textId` is 16 hex, so two pastes of one size stay apart; `@ <commit7>` omitted when the
  commit is empty — E1 fills it for both origins). `<commit7>`/`<blob7>` are `slice(0, 7)` of a string value — a
  non-string is unusable, a shorter one is shown whole; `<bytes>` must be a non-negative safe integer (else unusable),
  grouped by three with a plain space (`98 304`) by a pure function, never `toLocaleString`. The
  row's *What* already shows a committed round's FILE NAME (E1, `PlanSources.SubjectOf`); the line adds the path, the
  commit and the blob. Decided here: D16 named only the committed form, and a text round deserves a line too — it is
  "what was reviewed".

## 5. Stories

### E2.S1 — Schema step 23: `rounds.plan_source`, written with the round

- **Goal.** Every plan round recorded after the step writes its `PlanSource` JSON; every other round writes `''`; every
  database shape in the field reaches the step; read-only readers of a step-22 file keep answering.
- **Scope.** `Schema.cs`: `internal const string ThePlanSource` = C1, appended after `TheRoundsSize` in `Steps` (`:37`),
  with a doc comment in the step-21/22 style (`:602-634`) naming step 23. `RoundsDb.RecordRoundRow`: `plan_source` in the
  column list, the `VALUES` list and the `ON CONFLICT` set; one bind `$planSource` from `round.PlanSource` serialised
  through `ServerJsonContext.Default.PlanSource` (`''` when null), commented as `$roundKey` is (`:916-919`) — the
  `round_key` precedent, not the size's separate `UPDATE`: the value has no backfill to protect, so the upsert rewriting it
  from the record is correct. `ThePairsThemselvesTests.cs`: `ALTER TABLE rounds DROP COLUMN plan_source;` joins the drop
  list after step 22's columns (`:551`) and the comment (`:518-527`) names steps 22 and 23 — without it the step-12 re-run
  meets an existing column and the test fails on `db.Pairs`. No step test is relaxed: step 21's and 22's already pin `≥`
  (§2). `research/module_server.md`: a **Schema step 23** paragraph after the step-22 one (`:3455-3469`).
- **Brief.** Mirror `TheRoundsSizeIsStepTwentyTwoTests` — `MakeAt`, `Shape`, both preview forks, the
  appended-never-inserted pin. The column is rewritten on every re-record from the record itself, so a re-recorded round
  keeps the same value.
- **Test scenarios** (`ThePlanSourceIsStepTwentyThreeTests.cs`, real SQLite; RED: step present, bind writes `''`):
  `ThePlanSource_IsTheTwentyThirdStep_AppendedNeverInserted` (index 22, `Steps.Length ≥ 23`) ·
  `ADatabaseAtStepTwentyTwo_OpensAtTheHead_WithEveryRowNotRecorded_ThenRecordsAPlanRound` (the migrate-then-write path) · `ACommittedPlanRound_WritesItsSourceAsJson` (round
  trip through `ServerJsonContext` equals the record) · `APastedPlanRound_WritesOriginText` · `ACodeRound_WritesNothing` ·
  `TheSameRoundRecordedTwice_KeepsOneRowAndOneValue` · `AReadOnlyReader_OnAStepTwentyTwoFile_LeavesItAtTwentyTwo` · both
  preview databases still reach the head.
- **Break-its.** **B16** bind `''` always → `ACommittedPlanRound_WritesItsSourceAsJson` red.
- **Done when.** Scenarios, `TheRoundKeyIsStepTwentyOneTests`, `TheRoundsSizeIsStepTwentyTwoTests`,
  `ThePairsThemselvesTests`, `RoundsDbTests` green; the whole executable green.
- **Depends on.** E1 merged (done, #837). **Size.** ≈ 210 lines (≈ 35 code + doc, ≈ 175 tests). **Coder: sonnet** ·
  review: opus.

### E2.S2 — `--log` names each plan round's source, and a step-22 file still answers

- **Goal.** T13's `--log` half: a round recorded after the step carries `planSource`; a round recorded before it, and
  every round of a step-22 database, carries nothing.
- **Scope.** `LoggedRound` gains C2's init property (`RoundsQuery.cs:113`, beside `Size`). New
  `src/Store/RoundPlanSources.cs`, the `RoundChangesLog` shape: `Attach(SqliteConnection db, List<LoggedRound> rounds,
  IReadOnlyList<long> ids)` → the page's rounds, each `with { PlanSource = … }` when it has one; one
  `SELECT id, plan_source FROM rounds WHERE plan_source <> '' AND id IN (SELECT value FROM json_each($ids))` per page,
  run ONLY when `HasColumn(db, "rounds", "plan_source")`; JSON that does not parse, or parses with no `origin`, maps to
  absent (a malformed row never fails the page). `Rounds(...)` (`:544`) chains it after `RoundChangesLog.Attach` — the one
  call; the SQL ladder is not given a fourth text. `research/architecture.md` § *A field added to the round list*
  (`:754`): one more field, same rule. `research/module_server.md`: a `--log` paragraph after the size one (`:3471-3484`).
- **Brief.** Bind the ids the way `RoundChangesLog` does — move its `BindIds` (`:122`) once into a small shared internal
  Store helper that both readers call (no cross-reader coupling, no second copy), never string concatenation of values
  into the SQL text.
- **Test scenarios** (`TheLogNamesEachPlansSourceTests.cs`, the `TheLogNamesEachRoundsSizeTests` shape):
  `ARoundAfterTheStep_CarriesItsSource` · `ARoundBeforeTheStep_CarriesNone` (row with `''`) ·
  `AMalformedColumn_IsAbsentNotAnError` · `EachSourceLandsOnItsOwnRound_AcrossPages` (30 rounds, two pages of 15) ·
  `AStepTwentyTwoFile_IsLoggedAsBefore` (`OneShotProcess`: exit 0, rounds present, no `planSource` key, file still at
  22). `anOlderExtensionReadsALogPageCarryingASize.test.ts` gains one case: a plan round carrying `planSource` parses and
  renders as before.
- **Break-its.** **B10** remove the `HasColumn` guard → `AStepTwentyTwoFile_IsLoggedAsBefore` red (no such column, exit
  74).
- **Done when.** Scenarios, `RoundsQueryTests`, `TheLogNamesEachRoundsSizeTests`, `LogCliScenarioTests` green; `npm test`
  green; `architecture.md` and `module_server.md` updated.
- **Depends on.** E2.S1. **Size.** ≈ 240 lines (≈ 60 code + doc, ≈ 180 tests). **Coder: sonnet** · review: opus.

### E2.S3 — The rounds log shows what a plan round reviewed

- **Goal.** T17: opening a plan round's row shows C3's line when its session record has `planSource`, and nothing when it
  does not (an older server, a pre-E1 round, a code round).
- **Scope.** `rounds.ts`: `interface PlanSourceRecord { origin; commit; path; blob; bytes; textId }` — the session file's
  camelCase names, all optional, read defensively — and `readonly planSource?: PlanSourceRecord` on `RoundRecord`
  (`:51-93`); a pure exported `reviewedFrom(source)` beside `calledBy` (`:148`) → C3's string, or `''` when absent or
  unusable (no or unknown `origin`, `bytes` not a non-negative safe integer, a committed record without `path`). `roundsLog.ts`:
  `LogRow.reviewedFrom: string` beside `calledBy` (`:153`), set in `rowFrom` beside `:519`, `''` for a conversation row
  (`:384`); a `reviewedFromHtml(reviewedFrom, escape)` exported beside `askedByHtml` (`:462`) and embedded by
  `toString()` beside it (`:1637`), returning `<div class="reviewer source">…</div>` or `''`; `detail(row)` (`:1733`)
  puts it right after the asked-by line, before the early returns, so a row written without reviewer detail still shows
  it. `research/module_extension.md`: the asked-by section (`:6426-6459`) gains the line.
- **Brief.** Do not read the database for this — the row is built from the session file (parent `## Epics`, the
  consultant correction; E1 § 10: the line depends on the session-file field only). `escape` every value: `path` is
  caller-chosen text. No new action, so no new button (D16).
- **Test scenarios.** `rounds.test.ts`: `reviewedFrom` for a committed source (`98 304 bytes`), a text source with and
  without a commit (its `textId` shown), a short `commit`, `undefined`, an object missing `origin`, `bytes: "x"`. `roundsLog.test.ts`: the row carries the string
  (the `:514` shape); a round without the field carries `''`; `reviewedFromHtml` escapes a hostile path and answers `''`
  for `''` (the `:556-575` shape). `bundledPage.test.ts` (the `:173` shape): opening a row with `planSource` shows the
  line; opening one without shows no `source` element.
- **Break-its.** **B11** render the line for an absent `planSource` (drop the non-empty test) → the "shows nothing" case
  red.
- **Done when.** Scenarios green; `npm test` green; the page bundle builds; `module_extension.md` updated.
- **Depends on.** E1 merged (done); NOT on E2.S1/S2. **Size.** ≈ 190 lines (≈ 45 code + doc, ≈ 145 tests). **Coder:
  sonnet** · review: opus.

## 6. Build order

```
E1 merged (#837) ──► S1 step 23 + writer ──► S2 --log
                 └─► S3 rounds-log line (parallel with S1)
```

One pull request on `feat/plan-gate-e2`, the coai gate once (parent `## Epics`). Before merging: compare `Schema.Steps`
with `main` and renumber if another step landed first.

## 7. Test plan — the parent's rows this epic owns

| Row | Story | Kind |
|---|---|---|
| T13: the `--log` row has the source; a database without the column reads `null` | S1, S2 | real SQLite + process |
| T17: the page shows the line / shows nothing | S3 | node test, bundled page |
| Break-its B10 (S2), B11 (S3), B16 (S1) | each story | red → restore |

## 8. Growth

`rounds.plan_source` ≈ 300 B per plan round (indented JSON, two 40-hex ids); at 50 plan rounds a day ≈ 5.5 MB a year,
kept for as long as its round row (parent §9). One more query per `--log` page. No directory, cache or process; no
in-flight state, so no sweep.

## 9. Definition of Done — epic E2

- [ ] A step-22 database opens at step 23; read-only `--log` on a step-22 file answers and leaves it at 22.
- [ ] A round recorded before the step reads *not recorded* in `--log`; one recorded after it shows commit, path, blob
      and bytes; a text round shows its origin.
- [ ] The rounds log shows C3's line for a round with `planSource` and nothing without.
- [ ] B10, B11, B16 observed red then green; `CoaiMcp.Tests.exe` and `npm test` green.
- [ ] The step number checked against `main` at merge; `module_server.md`, `module_extension.md`, `architecture.md`
      updated; `node .agents/conventions/tools/plan-lifecycle.mjs` clean.
- [ ] The parent, E1's and this document promoted together.

## 10. Revised against `main`, 2026-10-16 — what changed since 2026-10-15

- **Step 23, not 22.** The round-size plan's step 22 (`TheRoundsSize`) merged first (#829). Every number here moved by
  one; the authors plan's E5 moves from 23 to 24 in its parent and its stories document.
- **No step test to relax.** The 2026-10-15 text relaxed step 21's `== 21`; step 22's PR already did that, and step 22's
  own test was written with `≥`. What a new step must extend instead is `ThePairsThemselvesTests`' drop list.
- **`LoggedRound.PlanSource` is an init property**, not a trailing positional parameter (C2) — the shape step 22 chose for
  `Size`; the reader is an `Attach` in `RoundPlanSources.cs` chained after `RoundChangesLog.Attach`, binding ids through a helper
  shared with that file. After the gate (2026-10-16): the text line carries `textId`; hash slicing and `bytes` validation
  are stated; S1 tests migrate-then-write.
- **E1 as shipped:** `PlanSource` lives in `core/Rounds/PlanSource.cs`; `PlanSources.Stamp` fills `Commit` for both
  origins and stamps plan rounds only; a committed round's subject is its file name. C1 and C3 follow those facts.
- **The line is a function the page runs** (`reviewedFromHtml`, the `askedByHtml` precedent), so it is tested directly
  and through the bundled page, never by matching page source text.
