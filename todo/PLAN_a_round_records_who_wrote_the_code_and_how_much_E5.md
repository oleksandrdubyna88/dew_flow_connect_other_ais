# PLAN — Round authors, epic E5: store and show

> Status: **plan only, nothing implemented yet, 2026-10-14; revised 2026-10-16 against `main` at `59463d73` (§9).** Epic E5 of [PLAN_a_round_records_who_wrote_the_code_and_how_much.md](PLAN_a_round_records_who_wrote_the_code_and_how_much.md).
>
> Scope: parent §11 step 4. The round's authors as a nullable snapshot on `RoundRecord` (the session file), **schema step 24**
> (array index 23, `user_version` 24 — step 22 is the round's size, #829; step 23 the plan source, #841) and its projection
> write, `--log`'s state, the reader and attribution run on the answer path under a 2 s / 64 MiB budget, and one line on the
> round card. Six stories, S1–S6. **No cornerstone**: the parent names no area for E5 (`## Epics`, "E1, E5, E6, E7 carry no
> area"); the parent's ledger reads `Cornerstones: 3 of 3 — E2.S1, E3.S4, E4.S2` (parent §16 D21).
>
> Related: [module_server.md](../research/module_server.md) (schema steps 20–23 at `:3434-3481`, the `--log` size and
> plan-source paragraphs after them), [module_extension.md](../research/module_extension.md) (the `asked by` and `reviewed from`
> lines, `:6430`, `:6476-6489`), [PLAN_the_round_knows_which_ai_called_it.md](../research/PLAN_the_round_knows_which_ai_called_it.md)
> (the `RoundRecord.Caller` twin), and [PLAN_the_plan_gate_reads_the_committed_plan_E2.md](PLAN_the_plan_gate_reads_the_committed_plan_E2.md)
> (the shipped step-23 stories whose shape S2, S3 and S6 copy).

## 1. What E5 delivers, and the decision it is built on

After E5, every plan or code round the engine finishes carries **who wrote the code under review** — observed, partial or
unobserved, never guessed — in the session file AND in `coai.db`, and the Review rounds page shows one line for it.

**The decision (main session, 2026-10-14).** The authors are kept as a **nullable snapshot on `RoundRecord`**, like `Caller`
(`src_mcp/src/Server/SessionStore.cs:173-193`), `Size` (`:195-202`) and `PlanSource` (`:213-223`), not in the projection only:
the database is a projection of the session files (`src_mcp/src/Store/Schema.cs:7-10`, `src_mcp/src/Store/Projection.cs:7-10`),
and a round whose transcript is gone cannot be recovered by any backfill. The snapshot is text-free: ids, enumerated words,
counts and tokens.

Three consequences every story obeys:

1. **The snapshot is computed BEFORE `_store.Save`** (`src_mcp/src/Server/Rounds/RoundEngine.cs:591-605`), in ONE statement
   right after the record's `with` block (`:485-496`) — not inside it: the initializer cannot name the record it is building.
   That is also before `_projection.Write` opens the database (`:612`), the parent's condition.
2. **`RecordRound` projects it from `round`, never from `RoundContext`** — the rule `RoundContext`'s remarks state for `Caller`
   (`src_mcp/src/Store/RoundContext.cs:29-36`): one fact, one source. A rebuilt database must come back with the same rows.
3. **A null snapshot is "not recorded"** and is never normalised into a default (`SessionStore.cs:185-191` explains why a getter
   that materialised one would rewrite history on the next progress-tick save; `:196-201` and `:216-222` repeat it for `Size`
   and `PlanSource`).

## 2. Dependencies on other epics

| Needs | From | Exactly what | State on `main`, 2026-10-16 |
|---|---|---|---|
| Steps 22 and 23 before step 24 | E2.S3 (#829); the plan-gate E2.S1 (#841) | `TheRoundsSize`, `ThePlanSource` the last two of `Schema.Steps` (`Schema.cs:31-38`; `:635-652`, `:667-669`) | **merged.** 23 steps; `SqliteMigrator.Migrate` stamps `user_version = index + 1` (`src_mcp/storage/SqliteMigrator.cs:80-90`), so E5 is index 23, `user_version` 24. A step merged first moves E5 by one at merge time |
| `RoundRecord.Size`, `Gitlinks` and their JSON | E2.S3 (#829) | `SessionStore.cs:202`, `:211`; shape `src_mcp/core/Context/RoundSize.cs:39-52` — camelCase `whole`/`delta` `{ state, from, cells[{ coverage, kind, files, added?, removed? }] }`, `origin`; states `:18-29` | **merged.** The extension has NO `size` member yet: E2 changed no TypeScript (its scope line), so S6 adds it |
| The reader's entry point and states | E3.S2 (`ClaudeHome`, `CallerTranscriptLocator`, the test home), E3.S4 (`ObservedSession`, `ReadBudget`, `fromUtc`) | `Locate(vendor, env, home)` → `Found` / `NotObservable(reason)`; `CallerTranscript.Read(where, ReadBudget(MaxBytes, MaxTime, Clock), fromUtc)` → `ObservedSession` with `ObservationState` (E3 doc §3, E3.S2 brief 1–6) | not started. The seams E5 needs are in E3's contract; S4 widens nothing unless E3 ships without one, and never adds a second reader |
| The reader's version | E3.S4 (`ObservedSession.ReaderVersion = 1`) | `authors_reader` (parent §9) | in E3's contract; S1 adds no constant |
| Attribution of ONE round | E4.S1 (`AttributionInput`, `UtcInstant.From`), E4.S2 (`RoundAttribution.ReadFrom`, `Assign`), E4.S3 (`RoundAuthorsBuilder.Build` → `RoundAuthors`) | pure, in `src_mcp/core/Authors/`; `Assign` throws `ArgumentException` when `Validate()` refuses the input | not started |
| Sanitised fixtures with sentinels | E1.S5 (#828) | 20 cases under `src_mcp/tests/fixtures/caller-transcripts/c01`–`c20`, each `projects/<slug>/<id>.jsonl` (+ `<id>/` subagents) and a `rounds.json`; `manifest.json` names each case's `fixtureRoots` (`D:/fixture/*`); instants at `2000-01-01` | **merged.** Re-pointable roots and one-offset instants are E1's own contract (E1 doc `:204-205`), so defect 13 is closed |
| The measured read time | E1.S4 (#828) | `research/RESULTS_caller_transcripts_on_both_sides.md` Q6: Node, largest Windows session 650 MB, day-gated parse p50 **19.9 s** — the prediction (≤ 300 ms) refuted by two orders of magnitude | **merged.** The budget stands on the `fromUtc` slice (D7) and on S4's own C# measurement, never on the whole session |

## 3. Decisions this document had to make (the parent is silent or inconsistent)

Each is a choice the main session may overrule before the story starts; §7 lists them as parent defects.

- **Which rounds.** `PlanReview` and `CodeReview` rounds finished by the engine get a snapshot. Skipped rounds
  (`src_mcp/src/Server/Rounds/RoundSkips.cs:75-80`) and every other stage (feature, document) get none — they read *not recorded*.
- **The peers** (E4.S2 §S2.8). On EVERY round, not only the first: the gate sessions other than this one holding a round whose
  `rounds.authors_session` or `rounds.caller` equals this caller, found by one read-only query on the projection; each peer is
  then built as a `GateTimeline` from its SESSION FILE (`SessionStore.Load(repo_path, branch)`, `SessionStore.cs:461`, with
  `sessions.repo_path`/`branch` from the same query) — `OpenedUtc` from `PersistedSession.OpenedUtc` (`:278`), never
  `sessions.opened_utc` (parent §6), its journal from the file, `CompletedUtc` per round through `UtcInstant.From` — with
  `CallerSessionId` per round from `rounds.authors_session`, else `rounds.caller` when it is a UUID, else `""`. A peer round
  still `running` is in the file with no completion and ranks last (E4.S2 left the file-or-database choice to E5: the file).
  A store that cannot answer (absent, locked, older than step 24 → no column, a peer file unreadable or naming another
  session id) → `PeersKnown = false`, `Peers` empty, and every window `partial: prior windows unknown`. Step 24 indexes
  `rounds(authors_session)`.
- **The id source is persisted** (`authors_id_source`, E3's `CallerSessionId.Source`: `registry` / `environment`; `stored` is
  E7's): parent §5 requires it, §11 step 4's column list omits it.
- **`open`/`status` do not carry the snapshot.** `SessionAnswerFor` sends `session.Rounds` to the calling AI on every reply
  (`src_mcp/src/Server/PanelService.cs:1878-1894`, the list at `:1888`); the rows would cost the caller up to several KB of
  context per round. The answer strips `Authors` (null is omitted, `src_mcp/src/Server/ServerJsonContext.cs:339-344`); the file
  and the page keep it. `Size` and `PlanSource` are NOT stripped today — they are a few hundred bytes; the snapshot is not.
- **The snapshot keeps all of E4's totals; the columns keep the parent's.** E4.S3's `RoundAuthors` also returns `Elsewhere`,
  `ClaimedByPeer` and `UnobservedLaunches`, which §11 step 4 names no column for and E6 reads no column for. They go into the
  snapshot (text-free counts, ≈ 150 B a round), so a later step can project them from the session files without a backfill;
  step 24 projects the ambiguous totals only, `ambiguous_agents` included (E4's `ActivityTotals.Agents`).
- **The card line describes THIS round's windows** (`before-open` + `round`), with the `delta` size when its state is
  `recorded` and the `whole` size otherwise — so the authors and the size describe the same stretch of work. Contributors
  (the union over rounds, parent §6) are the report's (E6), not the card's: computing them in TypeScript would be a second
  implementation of E4.S4 (§8, item 4).
- **A null snapshot never erases projection rows** — so E7's backfilled rows survive a re-record of the round (D14), exactly
  as a null `Size` writes nothing (`src_mcp/src/Store/RoundChangesTable.cs:23-39`).
- **`--log` follows the log's absence rule.** A round with no recorded state has NO `authorsState` member — the rule `size`
  and `planSource` already follow (`research/module_server.md`, the two `--log` paragraphs). The 2026-10-14 text printed the
  words `not recorded`; one rule for every `--log` member is worth more than the words.
- **Growth** is the parent's §14 (`round_authors`, `RoundRecord.Authors` — D15) plus the three totals above, ≈ 150 B a round
  → ≈ 3 MB a year a side in session files; no new surface.

## 4. Order

```mermaid
flowchart TD
    S1[S1 snapshot on RoundRecord] --> S2[S2 step 24 + projection write]
    S2 --> S3[S3 --log carries the state]
    S1 --> S4[S4 authors on the answer path]
    S2 --> S4
    S3 --> S5[S5 end to end: text-free, rebuilt, replayed]
    S4 --> S5
    S1 --> S6[S6 the card line]
```

S6 may run beside S2–S5 once S1 has merged (E2 has). The epic ships as ONE commit on its own branch, the coai gate once
(parent `## Epics`); stories are review units inside it, not commits.

## 5. Stories

### E5.S1 — The snapshot on the round

- **Goal.** `RoundRecord` carries a nullable, text-free `Authors` snapshot that survives a save/load round trip; a session file
  written before it reads `null`; the `open`/`status` answer omits it.
- **Scope.**
  - New `src_mcp/core/Authors/AuthorsSnapshot.cs` (namespace `CoaiMcp.Core.Authors`, beside E4.S3's types, inside the folder
    E4.S1's purity test scans — it is a record and reads nothing): `AuthorsSnapshot(State, Reasons, Session, IdSource, Reader,
    Rows, Ambiguous, Elsewhere, ClaimedByPeer, UnobservedLaunches, SubmoduleWrites, UncoveredWrites)`. `State` and `Reasons` are
    E4.S3's `RoundAuthors.State` (`observed` | `partial` | `unobserved`) and `Reasons`; `Rows` is **E4.S3's `AuthorRow`,
    persisted as it is** (window, stage, number, who, agent, agent type, requested, model, effort, association, launches, turns,
    tokens, write calls); the three totals are E4.S3's `ActivityTotals` (`Unknown` = `-1` in every term, never 0);
    `SubmoduleWrites` and `UncoveredWrites` are counts (gate finding 0). `Rows` and `Reasons` are normalised to `[]` inside the
    snapshot; the snapshot itself is not. **Not named `RoundAuthors`**: that is E4.S3's in-memory result, which carries
    `WritesOutsideTheGateCheckout` with checkout ROOTS — persisting it would put paths into the session file.
  - `Render()` — a method, so the JSON gains no member — gives the column and card text: `State` when `Reasons` is empty, else
    `State + ": " + string.Join(", ", Reasons)` (E3's `ObservationState.Render()` format, E3 doc §3). `IsWellFormed(string)`
    is the one rule for a stored state (a known kind; `observed` with no reason, the other two with at least one) — S3 and S6
    apply it, as `PlanSource.IsWellFormed` (`src_mcp/core/Rounds/PlanSource.cs:29`) is applied by `--log` and `reviewedFrom`.
  - `RoundRecord.Authors { get; init; }` (`AuthorsSnapshot?`) after `PlanSource` (`SessionStore.cs:223`), with remarks naming the
    `Caller` precedent and this plan. No getter default. No `[JsonSerializable]` registration: unlike `PlanSource`
    (`ServerJsonContext.cs:346-347`) it is never serialised on its own — the columns are written field by field.
  - `PanelService.cs:1888`: `[.. session.Rounds.Select(r => r with { Authors = null })]` in `SessionAnswerFor`.
- **The brief.** Primary-constructor records; `WhenWritingNull` already omits a null snapshot. Nothing in this story reads a
  transcript or touches the database. ≤ 4 cyclomatic complexity per method.
- **Tests (RED first; `CoaiMcp.Tests.exe --filter-class "*RoundAuthorsSnapshot*"`):**
  - A round saved with an `observed` snapshot of three rows, ambiguous totals and `Elsewhere = Unknown` loads back equal, field
    for field — `-1` stays `-1`.
  - A session file written by today's binary (no `authors` key) loads `Authors == null`, and saving it again writes no
    `authors` key — the precedent's failure mode, stated as a test (the `TheGitlinkCount_RoundTripsThroughTheSessionFile_AndOneWithoutReadsNull`
    shape, `src_mcp/tests/TheRoundsSizeIsStepTwentyTwoTests.cs:155-167`).
  - `Render()` and `IsWellFormed` over every kind, with zero, one and two reasons, and over `''`, `partial`, `observed: x`,
    `done`.
  - `status` over a session whose round carries a snapshot has no `authors` key in its JSON; the session file still has it.
  - The snapshot's JSON has no member named after a computed property (`isMain`, `rendered`) — see §8 item 3.
  - Break-it: give `Authors` a materialising getter (`field ??= new(...)`) → the second test goes red naming the invented key;
    restore. Break-it: remove the strip in `SessionAnswerFor` → the `status` test goes red.
- **Done when.** Tests green; no change to any existing session-file test.
- **Depends on.** E4.S3 (`AuthorRow`, `ActivityTotals`, `RoundAuthors.State`/`Reasons`). E2 has merged.
- **Model.** Judge: opus (scenarios + brief) → Coder: sonnet → Judge: opus (review) → Coder: sonnet (fixes).

### E5.S2 — Schema step 24 and the projection write

- **Goal.** `coai.db` holds what the snapshot holds for the parent's columns, written from `round` inside `RecordRound`'s
  transaction, and comes back identical from the session file after the database is deleted.
- **Scope.**
  - `Schema.cs`: `internal const string TheRoundsAuthors`, appended after `ThePlanSource` in `Steps` (`:37`) — index 23,
    `user_version` 24: `CREATE TABLE IF NOT EXISTS round_authors (round_id INTEGER NOT NULL REFERENCES rounds(id), ordinal
    INTEGER NOT NULL, who, agent, requested, model, effort, agent_type, association, window TEXT NOT NULL DEFAULT '', launches,
    turns, input_tokens, output_tokens, cache_creation_tokens, cache_read_tokens, write_calls INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (round_id, ordinal))`; on `rounds`: `authors_state`, `authors_session`, `authors_id_source` (TEXT, default
    `''`), `authors_reader` (INTEGER, default 0), `submodule_writes`, `uncovered_writes` (INTEGER, default 0), and
    `ambiguous_agents`, `ambiguous_launches`, `ambiguous_turns`, `ambiguous_input_tokens`, `ambiguous_output_tokens`,
    `ambiguous_cache_creation_tokens`, `ambiguous_cache_read_tokens` (INTEGER, default 0; `-1` written for E4's `Unknown`);
    `CREATE INDEX IF NOT EXISTS rounds_by_authors_session ON rounds (authors_session)`. Remarks in the style of step 22
    (`Schema.cs:618-634`) — the closest shape, a table plus state columns: empty state = not recorded; the zero defaults mean
    nothing without a state; appended, never inserted, for step 20's reason; a read-only reader runs no step.
  - `agent` is `""` for `main`: E4's `AgentKey` spells main `"main"` (E4 doc `:118-119`), the parent's column says empty (§11
    step 4) — the writer maps `row.Agent.IsMain ? "" : row.Agent.Id` (§8 item 2). `window` is the row's `Window`
    (`before-open` | `round`); the row's `Stage`/`Number` are the round's own and are not stored twice.
  - New `src_mcp/src/Store/RoundAuthorsTable.cs` through `RoundsDb.Command()` (`src_mcp/src/Store/RoundsDb.cs:172-181`) — the
    **`RoundChangesTable` precedent** (`RoundChangesTable.cs:6-39`, step 22's writer): `RoundsDb.cs` (1 293 lines) gains ONE
    line, `RoundAuthorsTable.Record(this, roundId, round.Authors)` after `RoundChangesTable.Record` (`:200`), inside the
    transaction (`:196-201`). The table: when `round.Authors` is null, write NOTHING; else `UPDATE rounds SET authors_*,
    submodule_writes, uncovered_writes, ambiguous_* WHERE id = $round`, `DELETE FROM round_authors WHERE round_id = $round`, then
    one `INSERT` per row with its ordinal — the delete-then-insert of `RecordReviewers` (`RoundsDb.cs:928-953`).
  - `src_mcp/tests/ThePairsThemselvesTests.cs`: its step-12 re-run drop list (`:529-554`) gains `DROP INDEX
    rounds_by_authors_session;` **first** — SQLite refuses `DROP COLUMN` on an indexed column — then `ALTER TABLE rounds DROP
    COLUMN` for the thirteen columns above, after `plan_source` (`:552`); `round_authors` and the index are `IF NOT EXISTS`, so
    nothing else. The comment (`:518-528`) names step 24. Without it the reopen meets an existing column, `RoundsDb.Open`
    answers null and the test fails on `db.Pairs` — how step 21 was first seen there.
- **The brief.** Parameterised SQL only; `RecordRoundRow`'s upsert (`RoundsDb.cs:842-926`) is NOT widened — the authors
  columns are written by the table's own `UPDATE`, because a backfilled snapshot must survive a null re-record (the size's
  reason). `plan_source` went the other way (`:852-868`, `:920-923`) because it has no backfill to protect; that precedent
  does not apply here. Copy `src_mcp/tests/ThePlanSourceIsStepTwentyThreeTests.cs` — the newest step test — as the shape of
  the migration tests (`MakeAt` `:40-49`, `Shape` `:51-58`, `OneOldRound` `:71-73`, the two preview forks `:176-192`).
- **Tests (RED first; new `TheRoundsAuthorsAreStepTwentyFourTests` + `RoundAuthorsTableTests`):**
  - `TheRoundsAuthors_IsTheTwentyFourthStep_AppendedNeverInserted`: `Array.IndexOf(Schema.Steps, Schema.TheRoundsAuthors) ==
    23`, `Steps.Length >= 24`, `Steps[23] == TheRoundsAuthors`. **No older step test is relaxed:** step 23's already pins `>=
    23` (`ThePlanSourceIsStepTwentyThreeTests.cs:102`), as 20's, 21's and 22's do (`TheReviewersModelIsStepTwentyTests.cs:84`,
    `TheRoundKeyIsStepTwentyOneTests.cs:80`, `TheRoundsSizeIsStepTwentyTwoTests.cs:113`) — the relaxation the E2 doc's §10 item 6
    foresaw was made when step 23 shipped. A step merged before E5 moves the pin to 24.
  - A database at step 23 (and ones at 22 and 21) opens at the head, its older rounds read state `''` and zero rows, then a
    round recorded after the open writes its authors (the migrate-then-write path); a migrated database opens again unchanged.
  - Both preview forks still reach the head (the consultant's and the security preview's, stamped 17).
  - `RecordRound` with an `observed` snapshot writes the columns and the rows in order; main's row has `agent = ''`; recorded
    twice → `SELECT *` of `round_authors` and of the authors columns identical (the replay case); a re-record with FEWER rows
    leaves none behind; an `unobserved` snapshot writes `-1` in every `ambiguous_*` column.
  - A re-record with `Authors == null` leaves existing columns and rows untouched (§3; the
    `AReRecordWithoutASize_LeavesTheEarlierRowsAndStatesUntouched` shape, `TheRoundsSizeIsStepTwentyTwoTests.cs:197-206`).
  - **Rebuild keeps the authors**: record a round through `SessionStore.Save` + `RecordRound`; delete `coai.db`
    (`SqliteConnection.ClearAllPools()` first); `Load` the session file and `RecordRound` its record into a fresh database →
    rows and columns equal to the first database's.
  - Break-it (rebuild): make the table read a `RoundContext` field instead of `round.Authors` (the shape the decision forbids)
    → the rebuild test goes red with zero rows; restore. Break-it (replay): drop the `DELETE` → recorded-twice doubles the rows.
    Break-it (drop list): leave out the `DROP INDEX` → `ThePairsThemselvesTests` red on the `DROP COLUMN`.
- **Done when.** Tests green; `TheRoundKeyIsStepTwentyOneTests`, `TheRoundsSizeIsStepTwentyTwoTests`,
  `ThePlanSourceIsStepTwentyThreeTests`, `ThePairsThemselvesTests`, `RoundsDbTests` and `RoundCountsCliScenarioTests` green, the
  first three unchanged; `research/module_server.md` gains a **Schema step 24** paragraph after step 23's (`:3471-3481`).
- **Depends on.** E5.S1. Steps 22 and 23 have merged.
- **Model.** Judge: opus → Coder: sonnet → Judge: opus → Coder: sonnet.

### E5.S3 — `--log` carries the state

- **Goal.** Every round in `--log` whose authors were recorded carries its state; every other round, and every round of a file
  older than step 24, carries no `authorsState` member (§3); a read-only `--log` or `--round-counts` on such a file still
  answers and leaves it byte for byte.
- **Scope.**
  - `LoggedRound` (`src_mcp/src/Store/RoundsQuery.cs:27-117`) gains an **init property** `public string? AuthorsState { get;
    init; }` after `PlanSource` (`:116`) — the shape `Size` (`:113`) and `PlanSource` chose: the positional constructor and every
    call site stay as they are; null is "not recorded" and absent from the JSON.
  - New `src_mcp/src/Store/RoundAuthorsLog.cs`, the `RoundPlanSources` shape (`src_mcp/src/Store/RoundPlanSources.cs:23-84`):
    `Attach(SqliteConnection db, List<LoggedRound> rounds)` → each round `with { AuthorsState = … }` when it has one,
    **correlated by the round's OWN id** through `PageIds.Of(round)` (`src_mcp/src/Store/PageIds.cs:23-24`), never by position;
    one `SELECT id, authors_state FROM rounds WHERE authors_state <> '' AND id IN (SELECT value FROM json_each($ids))` per page,
    the ids bound by `PageIds.Bind` (`:15-16`), run ONLY when the page has ids and `RoundsQuery.HasColumn(db, "rounds",
    "authors_state")` (`RoundsQuery.cs:910-913`). `RoundsQuery.Rounds` chains it after `RoundPlanSources.Attach` — the one call,
    `:545`: `RoundAuthorsLog.Attach(db, RoundPlanSources.Attach(db, RoundChangesLog.Attach(db, rounds)))`. NOT a fourth rung on
    the `SqlRoundsNoted/Counted/Plain` ladder (`:502-504`), which would multiply the query texts. `PageIds`' summary (`:5-8`)
    names the new reader beside the two.
  - **Per row, never per page.** A stored value that `AuthorsSnapshot.IsWellFormed` refuses reads as absent, and that check
    wraps each ROW only, never the query — the per-row `JsonException` rule of `RoundPlanSources` (`:17-21`, `:70-80`) in the
    form a TEXT column needs: there is no JSON to fail here, and a catch around the query would swallow *no such column* and
    hollow out the break-it below.
- **The brief.** `RoundsQuery.cs` is 1 006 lines: the init property and the chained call only. Exit codes and stdout shape
  unchanged; one new key. `--round-counts` reads nothing new. `research/architecture.md` § *A field added to the round list*
  (`:754`) gains the field; `research/module_server.md` a `--log` paragraph after the plan-source one.
- **Tests (RED first; `TheLogNamesEachRoundsAuthorsTests`, the `src_mcp/tests/TheLogNamesEachPlansSourceTests.cs` shape):**
  - `ARoundAfterTheStep_CarriesItsState` over one `observed`, one `partial: read budget` and one `unobserved: transcript not
    found` round; `ARoundBeforeTheStep_CarriesNone` (a row with `''`: no `authorsState` key).
  - `AMalformedState_IsAbsentNotAnError` (`[Theory]`: `partial`, `observed: x`, `done`) — the page still answers.
  - `EachStateLandsOnItsOwnRound_AcrossPages` (30 rounds, two pages of 15) and
    `ARoundList_InAnotherOrder_OrFiltered_StillGetsEachStateOnItsOwnRound` (`TheLogNamesEachPlansSourceTests.cs:177-207`).
  - `AStepTwentyThreeFile_IsLoggedAsBefore`: `OneShotProcess.Run(_dir, "--log", "--paged")` over
    `Schema.Steps[..IndexOf(TheRoundsAuthors)]` — exit 0, the round present, no `authorsState` key, `user_version` still 23
    (`:209-223`); and `--round-counts` on the same file leaves its SHA-256 unchanged (`RoundCountsCliScenarioTests.cs:139-171`).
  - `src_vs_code/src/test/anOlderExtensionReadsALogPageCarryingASize.test.ts` gains one case: a round carrying `authorsState`
    parses equal to the same page without it. It proves the RELEASED `parseLog` only — `roundsDb.ts` is byte-identical to
    `extension-v0.78.0`'s (checked 2026-10-16: `git diff extension-v0.78.0 -- src_vs_code/src/roundsDb.ts` is empty; the file's
    comment, `:13-20`, still names `extension-v0.75.0` and is brought up to the newest tag in the same commit).
  - Break-it: drop the `HasColumn` guard → `AStepTwentyThreeFile_IsLoggedAsBefore` red (no such column, exit 74 — the defect the
    ladder's comment, `RoundsQuery.cs:494-501`, records).
- **Done when.** Tests green; `RoundsQueryTests`, `TheLogNamesEachRoundsSizeTests`, `TheLogNamesEachPlansSourceTests`,
  `LogCliScenarioTests` unchanged and green; `npm test` green.
- **Depends on.** E5.S1 (`IsWellFormed`), E5.S2.
- **Model.** Judge: opus → Coder: sonnet → Judge: opus → Coder: sonnet.

### E5.S4 — The authors on the answer path, under a budget

- **Goal.** A finished plan or code round reads its caller's transcript, attributes it, and stores the snapshot — within 2 s and
  64 MiB over the `fromUtc` slice (parent §16 D7), never failing, never slowing a round by more than the budget, and never
  reading a developer's own `~/.claude` from the test suite.
- **Scope.**
  - New `src_mcp/src/Server/Rounds/RoundAuthorsStep.cs` (a stateful service, a class): `For(PersistedSession, RoundRecord) →
    AuthorsSnapshot?` — null for a stage outside §3's two; else, in this order:
    1. **Locate** (E3.S2): `CallerTranscriptLocator.Locate(vendor, Environment.GetEnvironmentVariable, home)` with the vendor from
       the round's `Caller`. A `NotObservable` location ends the step: `unobserved: <its reason>` (`no session id`, `stated id`,
       `transcript not found`, `transcript in two folders`, `no reader for <vendor>`), totals `Unknown`, no peer query.
    2. **The peers** (§3; one read-only connection `Mode=ReadOnly;Pooling=False;Default Timeout=1`, the
       `src_mcp/src/Store/GateHistoryQuery.cs:77` shape, on EVERY round).
    3. `RoundAttribution.ReadFrom(input without the observation)` → E3's `fromUtc` (`DateTimeOffset?` → `UtcDateTime`).
    4. E3's read with `ReadBudget(64 MiB, time left, clock)` over that `fromUtc`.
    5. The ONE mapping of `ObservedSession` onto E4's records (E4.S1's *Mapping from E3*: `FirstLineAt = LaunchedUtc ??
       CalledUtc`, `TokenTerms` term by term, instants through `UtcInstant.From`), with the universe of E4.S2 decision 6 (the
       gate's and every peer's `repo_path` ∪ every `WriteTarget.Where` root E3 resolved).
    6. `RoundAttribution.Assign` → `RoundAuthorsBuilder.Build` → the snapshot. From `Build`'s `WritesOutsideTheGateCheckout`
       the step derives `UncoveredWrites` (every call listed) and `SubmoduleWrites` (the calls whose root, relative to
       `repo_path`, is a `submodule.<name>.path` of the checkout's `.gitmodules` — read once per round, `git config -f
       .gitmodules --get-regexp`, a failed read counting none as submodule and saying so at Debug); no root is stored (gate
       finding 0).

    The attributed round's `CallerSessionId` is the id E3's ladder resolved; earlier rounds' come from `authors_session`, else a
    UUID `rounds.caller`, else `""`. An `ArgumentException` from `Assign` (its `Validate()` refused the input) becomes
    `unobserved: invalid input` — a reason this story adds to its own vocabulary. Every other exception is caught, logged as a
    warning with its type (never a transcript fragment), and becomes `unobserved: reader failed`.
  - The budget as one named value, `AuthorsBudget.Default = (2 s, 64 MiB)`, with a remark citing
    `research/RESULTS_caller_transcripts_on_both_sides.md` Q6 (Node: a day-gated parse of the largest session, p50 19.9 s) and
    the C# measurement this story makes over E1's largest fixture slice BEFORE it fixes the value — prediction first
    (`measurement.md` rule 4), observation in the story's summary. The clock is a `TimeProvider` argument. Hitting either limit
    → `partial: read budget`, the rows read so far kept (E6 decides what a partial round counts for). **The 2 s is ONE deadline
    for the whole `For`**, taken from the `TimeProvider` when `For` starts and passed down: E3's `ReadBudget.MaxTime` is the time
    left at the reader's start, and the peer query, the `.gitmodules` process, E4's attribution and the snapshot construction
    each check the same deadline between phases; a deadline missed after the read → `partial: read budget` with what is built
    so far (no rows when attribution never ran). The 64 MiB stays the reader's own byte limit.
  - The transcripts root: E3.S2's `ClaudeHome.Default.With(_settings.AgentLogDir)` — the expression `WhatTheCallerWasDoing`
    spells today as a ternary (`RoundEngine.cs:902`), which becomes `….ProjectsDir` of the same value, so the default is written
    once.
  - Call sites only: after the `with` block (`RoundEngine.cs:485-496`), one statement `record = record with { Authors =
    _authors.For(session, record) };` — before `:573` reads the record's cost and before the save (`:591`); the round's
    `StartedUtc` is set by `live.Finish`. The engine's constructor (`RoundEngine.cs:23-39`) gains one parameter;
    `PanelService.cs:134-136` passes it.
  - **The suite is not the developer's session.** A test-assembly fixture (the `[assembly: AssemblyFixture]` precedent,
    `src_mcp/tests/TempDirsAreSwept.cs:5`) clears the inherited caller-identity variables — `COAI_CALLER_SESSION`,
    `CLAUDE_CODE_SESSION_ID`, `CODEX_SESSION_ID`, `GEMINI_CLI_SESSION_ID` (`src_mcp/src/Server/CallerSessions.cs:45-51`) and
    `CLAUDE_PID`, which E3's registry rung keys on — so a test round is `unobserved: no session id` unless the test states one.
    The home half is E3.S2's test-home fixture (parent §16 D17), which already points `ClaudeHome.Default` — and `AgentLog`'s
    default with it — at an empty temp directory; this story adds the variable clearing only.
- **The brief.** `RoundEngine.cs` (915 lines) and `PanelService.cs` (1 915) gain call sites only. The step runs synchronously on
  the answer path, after the reviewers have answered and before the save — measured, not assumed: log the elapsed milliseconds
  at Debug. No text from a transcript in any log line. `Outcome`-style results, complexity ≤ 4.
- **Tests (RED first; unit tests over the step with E1 fixtures in a temp home, plus two through `PanelService`):**
  - Fixture session with a Sonnet writer into the repo → `observed`, rows with `who`/`model`/`association` from E4, the id
    source recorded, `authors_reader` = `ObservedSession.ReaderVersion`.
  - Persistence off / no transcript → `unobserved: transcript not found`; no session id → `unobserved: no session id`; a stated
    id alone → `unobserved: stated id` (parent §16 D9); a codex caller → `unobserved: no reader for codex`; none of them opens the
    store (a spy on the peer query); `caller_model` untouched in every case.
  - Byte budget of 1 KiB over a 2 MB fixture → `partial: read budget`; a `FakeTimeProvider` advanced past 2 s mid-read → the
    same; the fake clock advanced inside the peer-query phase (not the reader) → `For` stops there and reports `partial: read
    budget`.
  - A reader that throws → `unobserved: reader failed`, the round still answers and is saved (through `PanelService`, FakeCli).
  - Any round (not only the first) with a peer gate session of the same caller in the store → E4 is handed that peer, built
    from the peer's session file (its `OpenedUtc` from the file even when `sessions.opened_utc` differs); no database, or a
    step-23 database (no column) → `PeersKnown = false` → `partial: prior windows unknown`, not an error.
  - `ReadFrom` is called before the read and its bound reaches E3's `fromUtc` (a spy on the reader's argument).
  - An input `Validate()` refuses (a caller mismatch planted in the session file) → `unobserved: invalid input`; the round still
    answers and is saved.
  - A feature round and a skipped round → `Authors == null`.
  - A fixture write re-pointed into a registered submodule of the temp repository and one into a nested worktree →
    `SubmoduleWrites = 1`, `UncoveredWrites = 2` on the snapshot and the columns; the rows still `wrote here`.
  - The suite guard: with `CLAUDE_CODE_SESSION_ID` set to a real-looking id and the fixture in place, a `PanelService` round in
    a test that did not ask for authors records `unobserved: no session id` — and the whole suite's wall time is within 5 % of
    the run before this story (measured both, numbers in the summary).
  - Break-its: move the statement after `_store.Save` → the session-file assertion goes red (the snapshot only in the
    database); remove the `catch` → the throwing-reader round fails; remove the byte check → the budget test reads the whole
    file; let a `NotObservable` location fall through to the peer query → the spy test goes red.
- **Done when.** Tests green; the C# read-time measurement and the suite-time measurement in the summary; no existing round test
  changed its assertions.
- **Depends on.** E5.S1, E5.S2 (the column the lookup reads); E3.S2 + E3.S4 (locator, reader, states, seams); E4.S1–E4.S3
  (records, attribution of one round); E1.S4 + E1.S5 (merged).
- **Model.** Judge: opus → Coder: sonnet → Judge: opus → Coder: sonnet.

### E5.S5 — End to end: text-free, rebuilt, replayed

- **Goal.** One real round through `PanelService` + FakeCli proves the whole chain: the snapshot is in the session file and the
  database, no sentinel leaves the reader, a deleted database comes back with the same authors, and a replay adds nothing.
- **Scope.** A test class in the `ARoundIsMintedAKeyTests` shape (`src_mcp/tests/ARoundIsMintedAKeyTests.cs`,
  `[Collection("fakecli-env")]` at `:23`, temp repo + data dir): one E1 case copied from
  `src_mcp/tests/fixtures/caller-transcripts/<case>/projects/` into a temp projects root as `<slug>/<sessionId>.jsonl` (+ the
  `<sessionId>/` subagents folder), its `manifest.json` `fixtureRoots` re-pointed at the temp repository and its instants
  shifted by ONE offset (the fixtures sit at `2000-01-01`) so the writes fall between the plan round's start and the code
  round's start (fixture derived from now — `testing.md` § *A test that reads the clock expires*); the session id stated
  through the variable; a `ListSink` (`src_mcp/tests/RoundAuditTests.cs:16`) on the logger. No production code.
- **Tests:**
  - **Positive control first:** the code round's snapshot is `observed` with a `wrote here` row — proving the fixture was read
    and accepted, not rejected (`testing.md` § *A fixture the code REJECTS*). And `rounds.agent_log` DOES contain the sentinel:
    `AgentLog` renders text by design (parent §3, `src_mcp/src/Store/AgentLog.cs:238-294`, `Said` and `Cut`), which proves the
    sentinel reached the server.
  - **Text-free:** the sentinel is absent from the persisted gate-session JSON's bytes (the file `SessionStore` writes — NOT the
    E1 transcript fixture, which carries the sentinel on purpose and stays the positive source the test checks first), from
    `--log`'s stdout, from every `ListSink` line, and from the database's bytes after `UPDATE rounds SET agent_log = ''`,
    `PRAGMA wal_checkpoint(TRUNCATE)` and `VACUUM` — the one column that carries text by design is blanked, so what remains is
    what this epic wrote (§7, defect 1).
  - **Rebuild:** delete `coai.db` (+ `-wal`, `-shm`) after the round; re-project the loaded session file's rounds through
    `RecordRound` → `round_authors` and the authors columns equal to the first database's, the ambiguous totals included.
  - **Replay:** project the same round again → no row and no total changes.
  - Break-it: point the fixture's sentinel at a field the reader is meant to drop and make the reader keep it (e.g. store
    `AgentType` from `description`) → the text-free test goes red naming the file it was found in; restore.
- **Done when.** Green through `CoaiMcp.Tests.exe`, alone and in the whole suite.
- **Depends on.** E5.S3, E5.S4; E1.S5's fixtures (merged).
- **Model.** Judge: opus → Coder: sonnet → Judge: opus → Coder: sonnet.

### E5.S6 — The card line

- **Goal.** An opened round on the Review rounds page says who wrote what it reviewed — *written with Opus 5.5 (main) · 3 Sonnet
  coders · +412 −88 in 9 files*, *… · partial — read budget*, or *authors not observed — transcript not found* — and says
  nothing for a round without a snapshot.
- **Scope.**
  - `src_vs_code/src/rounds.ts`: `RoundRecord` (`:51-99`) gains `readonly authors?: unknown` and `readonly size?: unknown` —
    E2 added no TypeScript, so the size is S6's too — documented like `planSource` (`:93-98`). A pure exported
    `writtenWith(authors: unknown, size: unknown): string` beside `reviewedFrom` (`:225-227`), in its style: the parameters are
    `unknown` because nothing off the disk has been validated, every member read through `member()` (`:196-199`), no type
    assertion in code or tests. No snapshot, or a state `IsWellFormed`'s rule refuses (the same rule as S1, restated for
    TypeScript, the `reviewedFrom` ↔ `PlanSource.IsWellFormed` precedent) → `''`; `unobserved` → `authors not observed —
    <reasons>`; else main = the distinct displayed models of `who = main` rows; coders = the DISTINCT `agent` ids of subagent
    rows with association `wrote here` (a straddler once), grouped by family through `shared/model-display-vectors.json`: one
    family → *N Family coder(s)*; two → *2 Sonnet + 1 Opus coders* (larger group first, ties by family name); more than two →
    *N coders (mixed)*; none → *main only* (parent §8, gate finding 2); both windows counted (§3); size = the `delta` scope when
    its `state` is `recorded`, else the `whole` scope when its state is, else no size clause — lines of `shown` + `elided`, files
    of `shown` + `elided` + `binary` (`added`/`removed` absent in a binary cell, never 0); `partial` appends *· partial —
    <reasons>*; `uncoveredWrites > 0` appends *· size not covered*.
  - The model display: `claude-<family>-<major>-<minor>[-<date>]` → *Family major.minor*; anything else verbatim, never
    guessed. Its cases live in a new `shared/model-display-vectors.json` (the `shared/path-family-vectors.json` precedent, read
    by `src_vs_code/src/test/pathFamily.test.ts` and `src_mcp/tests/SharedPathVectors.cs`) so E6's C# family function reads the
    same file rather than retyping it (D19).
  - `src_vs_code/src/roundsLog.ts`: `LogRow` gains `writtenWith: string` beside `reviewedFrom` (`:159`), set in `rowFrom` beside
    `:541`, `''` for a chat row (`:391`); an exported `writtenWithHtml(line, escape)` beside `reviewedFromHtml` (`:474-485`),
    using ONLY its parameters, embedded by assignment beside it (`:1662`), returning `<div class="reviewer authors">written with
    …</div>` or `''`; `detail(row)` adds it to the `asked` expression (`:1762`), so it shows before the early returns, for
    skipped and reviewer-less rows by the same rule (a skipped round has no snapshot → nothing); a `.reviewer.authors` CSS rule
    beside `.reviewer.source` (`:1475`). `LogRow` gains a required field, so the five fixtures that build one gain
    `writtenWith: ''`: `chatRows.test.ts:46`, `roundsCsv.test.ts:28`, `roundsExport.test.ts:26`, `roundsLogBusyMark.test.ts:27`,
    `roundsLogPaging.test.ts:38`.
  - `research/module_extension.md`: the asked-by / reviewed-from section (`:6430`, `:6476-6489`) gains the line.
- **The brief.** **Read the session file only, never the database** — the row is built from the file (`roundsLog.ts:154-158`,
  the `reviewedFrom` rule), so `--log`'s `authorsState` (S3) is not this page's source. The CSV export (`roundsCsv.ts`, the asked
  column at `:216`) is NOT widened — the report is E6's, as `reviewedFrom` was decided out of it. The embedded function calls
  nothing the minifier can rename: `'writtenWithHtml'` joins `EMBEDDED` (`src_vs_code/src/test/bundledPage.test.ts:29`), which
  the `:188-201` check walks. Every value is escaped. No new action, so no new button.
- **Tests (RED first; `npm test`):**
  - `rounds.test.ts`: `writtenWith` over observed (one main model; a model switch → two), partial, unobserved, absent, a
    malformed state, no size, a `delta` that is `unavailable: first round` (→ the `whole` size), a binary cell (files counted, no
    lines), a `before-open` row, a `child of` subagent (not a coder), a `wrote here` Opus subagent beside two Sonnet ones (*2
    Sonnet + 1 Opus coders*), three families (*3 coders (mixed)*), no `wrote here` subagent (*main only*), one coder straddling
    two windows (counted once), `uncoveredWrites` (*· size not covered*), a member of the wrong type (`turns: "x"`) → `''`.
  - `roundsLog.test.ts`: the row carries the string; a round without `authors` carries `''`; `writtenWithHtml('', esc) === ''`;
    a hostile model id is escaped.
  - Every vector of `shared/model-display-vectors.json` through the display function.
  - `bundledPage.test.ts`, the running page (the `openedRows` pattern of the `reviewedFrom` tests, `:1215-1257`, over a
    `SESSION`-shaped fixture, `src_vs_code/src/test/roundsLogPageHarness.ts:49-58`): opening an observed, a partial and an
    unobserved round shows its `reviewer authors` element; a round without `authors` shows none and still shows `asked by`.
  - Break-it: delete the `writtenWithHtml` term in `detail` → the page test goes red while every unit test stays green (the
    reason the page test exists, `.agents/PROJECT.md` § *A webview page is tested by RUNNING it*).
- **Done when.** `npm test` green, the bundled-page suite included; the page bundle builds.
- **Depends on.** E5.S1 (the JSON shape). E2's `Size` has merged.
- **Model.** Judge: opus → Coder: sonnet → Judge: opus → Coder: sonnet.

## 6. Definition of Done (the epic)

- [ ] A plan or code round finished after E5 carries `authors` in its session file and the same in `coai.db`; a skipped,
      feature or document round and every older round read *not recorded*.
- [ ] A database at step 21, 22 or 23 opens at 24; read-only `--log` and `--round-counts` on a step-23 file answer and leave it
      byte for byte; both preview forks reach the head; `ThePairsThemselvesTests`' drop list carries step 24.
- [ ] Deleting `coai.db` and re-projecting the session file gives back the same authors (S2, S5); recording a round twice adds
      nothing.
- [ ] The budget path records `partial: read budget`; a failing reader never fails a round; the suite reads no developer's
      transcript and its wall time moved < 5 %.
- [ ] No sentinel in the session file, `--log`, the log, or the database outside `rounds.agent_log`.
- [ ] The card line shows for observed / partial / unobserved and not for a round without a snapshot, on the running page.
- [ ] `CoaiMcp.Tests.exe` and `npm test` green; `research/module_server.md` records step 24 after step 23 and the `--log`
      member after the plan-source one; `research/architecture.md` § *A field added to the round list* gains the field;
      `module_extension.md` the card line (the parent's DoD keeps the full write-up for E7).

## 7. Parent-plan defects found while writing this (not fixed here)

> **Settled 2026-10-14** in the parent's §16: 1 and 7 → D13; 3 and 11 → D9; 4 → D4; 6 → D15; 8 → D1; 9 → D14; 12 → D17
> (the test home now lives in E3.S2 and covers `AgentLog`); 14 → D19; 2, 5, 10 and 13 accepted as resolved here. Items 15–17
> were added on 2026-10-16 and are not settled.

1. **"The sentinel is absent from the `.db` bytes" cannot hold** (E5 done-when, §12): `agent_log` keeps its behaviour (§3) and
   stores transcript TEXT in `rounds.agent_log` (`AgentLog.cs:238-294`, written via `RoundEngine.cs:620` → `RoundsDb.cs:887`).
   S5 blanks that column before the byte check and uses it as the positive control.
2. **The card line's scope is undefined** — contributors or this round's window; `whole` or `delta`; which coverages and kinds.
   §3 decides: this round's windows, a `recorded` `delta` else `whole`, `shown` + `elided` lines.
3. **The id source is required (§5) but has no column (§11 step 4)** — `authors_id_source` added.
4. **`write_calls` (§11, a count) vs "kept as a boolean per (agent, repository)" (§5)** — E5 stores what E4 hands over.
5. **`before-open` needs a cross-gate-session lookup with no named source**; E4 is pure, so the wiring supplies it (§3).
6. **§14 has no growth row for `RoundRecord.Authors` in session files** after the snapshot decision: at ~0.4 KB per indented
   row × 3–10 rows ≈ 1.2–4 KB a round, ~700 rounds per two weeks → **≈ 22–75 MB a year** in session files (today 1 743 files,
   28 MB on Windows), and every progress-tick save rewrites the whole file.
7. **`open`/`status` would send the snapshot to the calling AI** (`PanelService.cs:1878-1894`); the plan does not mention it.
8. **Skipped rounds and the feature/document stages** — whether they get authors, and whether a skipped round tiles a window
   (E4's question), is not said.
9. **Backfill (§9, E7) upserts only the database**; under the snapshot decision it must also write `RoundRecord.Authors` into the
   session file, or a rebuild loses every backfilled round. E5 only guarantees a null snapshot never erases rows.
10. **No rounds re-projection exists** — only consultations and question consultations re-project at startup
    (`PanelService.cs:164,176`); "rebuild" is `Schema.cs:7-10`'s "delete the file and write it again", which E5's tests simulate.
11. **§4 cites `CLAUDE_CODE_SESSION_ID` at `CallerSessions.cs:47` as the fallback**, but `COAI_CALLER_SESSION` precedes it
    (`:45-51`) and `RoundCommands.CallerFor` falls back to `repo:<path>` (`RoundCommands.cs:34-35`), which is no session id — E3's.
12. **Tests already read the developer's real transcripts**: `WhatTheCallerWasDoing` uses `AgentLog.DefaultProjectsDir` when
    `AgentLogDir` is empty (`RoundEngine.cs:902`), so every FakeCli round test slices the real `~/.claude/projects` today.
13. **E1's fixtures** must have re-pointable write paths and shiftable timestamps for an end-to-end round — closed by E1 as
    shipped (#828): `fixtureRoots` per case, instants at `2000-01-01`, one offset (E1 doc `:204-205`).
14. **One model-family rule is needed on both sides** (card in TypeScript, report in C#); the plan names neither — S6 adds the
    shared vectors file both read.
15. **The parent's E5 anchors have drifted** with steps 22–23: `RoundEngine.cs:586` (the save) is `:591`; "call sites only in
    `RoundEngine.cs:605-625`" is `:612-630` and the call site that matters is the record at `:485-496`; `RoundsDb.cs:923-945`
    is `:928-953`; the card anchors `rounds.ts:156` and `roundsLog.ts:519` are `rounds.ts:168` (`calledBy`) / `:225`
    (`reviewedFrom`) and `roundsLog.ts:541`; `PanelService.cs:159` is `:164`.
16. **"The ambiguous totals" has no list** — E4.S3's `ActivityTotals` carries an `Agents` count beside the five E5 named; §3
    adds `ambiguous_agents`, and keeps `Elsewhere`, `ClaimedByPeer` and `UnobservedLaunches` in the snapshot only.
17. **`todo/README.md`'s E5 row still says "schema step 23"** (`todo/README.md:21`); the parent and this document say 24.
    Not edited here (this revision touches this document only).

## 8. Open with E4 — where this document and E4's contract disagree

Each item is E4's to confirm or E5's to change; none blocks S1's tests, which are written against the shapes below.

1. **`RoundAuthors` is E4's name, so the snapshot is `AuthorsSnapshot`.** E4.S3 puts `RoundAuthors` and `AuthorRow` in
   `src_mcp/core/Authors/RoundAuthors.cs` (E4 doc `:628-656`); the 2026-10-14 version of this document put a second
   `RoundAuthors` and `AuthorRow` in `src_mcp/src/Store/`. E5 now persists E4's `AuthorRow` and `ActivityTotals` as they are and
   wraps them in its own record, because E4's `RoundAuthors.WritesOutsideTheGateCheckout` holds checkout roots that must never
   reach a file (E4 doc `:656`, `:662`). E4 to confirm `AuthorRow`'s members are final; a rename there renames the session
   file's JSON, so it must land before S1.
2. **`main`'s agent id.** E4's `AgentKey.MainId = "main"` (E4 doc `:118-119`); the parent's `round_authors.agent` is empty for main
   (§11 step 4) and E6 reads it so (E6 doc `:26`). S2 maps at the column; the snapshot keeps E4's `"main"`. If E4 would rather
   spell main `""`, S2's mapping goes — one place either way.
3. **`AgentKey.IsMain` is a computed PROPERTY** (E4 doc `:118-119`): the source-generated context serialises it, so every row in a
   session file would carry `"isMain": …` beside the id. E5 asks E4 for `[JsonIgnore]` on it (the core already references
   `System.Text.Json`, `src_mcp/core/Api/`), or to make it a method; S1's test fails until one of them is done.
4. **The card does not consume E4.S4's contributors.** E4.S4 names "the card line's counts (E5, `rounds.ts:156`)" as a consumer
   of `ContributorSet` (E4 doc `:743`); this document's §3 (accepted in the parent's §16, *writers' resolutions*, row `E5 §3`)
   has the card describe THIS round's stored rows only, in TypeScript, and leaves contributors to E6. E4.S4 should drop the
   card from its consumers; `rounds.ts:156` is no longer a function boundary either (`calledBy` is `:168`).
5. **The peer lookup key.** E4.S1 says E5 finds peers "by `rounds.caller`" (E4 doc `:154-155`); E4.S2 §S2.8 says
   `rounds.caller` or `authors_session` (`:533-537`). E5 follows §S2.8; E4.S1's sentence is older than its own §S2.8.
6. **The state string.** E4.S3 returns `State` plus a `Reasons` array (E4 doc `:650-651`); the column, `--log` and the card
   need one string. S1's `Render()` joins them as E3's `ObservationState.Render()` does (`kind: r1, r2`). E4 to confirm no
   reason it emits contains `", "`.
7. **E4's own anchors** cite `RoundEngine.cs:483-491` / `:586` / `:607` (E4 doc `:15`, `:243`, `:265`) and `RoundsDb.cs:923-945`
   (`:633`); on `main` they are `:485-496`, `:591`, `:612` and `:928-953`. Nothing in E4's logic depends on them.

## 9. Revised 2026-10-16 against `main` — what changed since 2026-10-14

Verified at `59463d73` (E1 #828, E2 #829, the plan-gate E1 #837 and E2 #841 merged). Every `path:line` above was re-read.

- **Step 24, index 23, `user_version` 24** — `Schema.Steps` holds 23 entries ending in `TheRoundsSize`, `ThePlanSource`
  (`Schema.cs:31-38`), and the migrator stamps the count of steps run (`SqliteMigrator.cs:80-90`). §2's "numbered at merge
  time" became a fact with a fallback.
- **No step test is relaxed.** Steps 20–23 already pin `>=` (§S2's list); E2 §10 item 6's foreseen relaxation was made by
  step 23. What step 24 must extend instead is `ThePairsThemselvesTests`' drop list — with a `DROP INDEX` first, because step
  24 is the first step to index a column that list drops.
- **The writer follows `RoundChangesTable`** (step 22), not `QuestionConsultTable`: same seam, same "null writes nothing", and
  it is called after `RoundChangesTable.Record` at `RoundsDb.cs:200`. The `plan_source` upsert precedent is named and
  rejected for the backfill reason.
- **`--log` follows the shipped readers:** an init property on `LoggedRound` like `Size`/`PlanSource`; a reader in its own file
  chained after `RoundPlanSources.Attach`; matching by `PageIds.Of`, binding by `PageIds.Bind`; the `HasColumn` guard; a per-row
  well-formedness check standing in for the per-row `JsonException` catch, since the column is text. Absent now means *not
  recorded* (§3), which changed S3's goal and tests from printing the words.
- **The extension follows `reviewedFrom`:** `unknown` parameters, `member()`, a shared well-formedness rule, `writtenWithHtml`
  using only its parameters and listed in `EMBEDDED`, a CSS rule, the five `LogRow` fixtures, the `openedRows` page test, and the
  older-extension `parseLog` case for `authorsState` (S3). E2 added no `size` to `rounds.ts`, so S6 adds it.
- **The snapshot is `AuthorsSnapshot` in `core/Authors`**, persisting E4's `AuthorRow` and `ActivityTotals` (E4.S3's shapes are
  now written down), and keeping `Elsewhere`, `ClaimedByPeer` and `UnobservedLaunches` too; `ambiguous_agents` joins the columns
  (§3, §8 items 1–3, §7 item 16).
- **S4 follows E3's and E4's written contracts:** `ClaudeHome.With` for the root, `CallerTranscriptLocator.Locate` first (a
  `NotObservable` location ends the step with no peer query), `ReadBudget(MaxBytes, MaxTime, Clock)`, `UtcInstant.From`, and
  the budget remark citing E1's RESULTS (the Node read-time prediction was refuted; the C# value is measured in S4 first).
- **The call site is one statement after the `with` block**, not inside it — the 2026-10-14 text placed `_authors.For(session,
  record)` in the initializer of `record` itself, which does not compile.
- **E1's fixtures exist** (`c01`–`c20`, `manifest.json`, `2000-01-01` instants), so §2 cites them and §7 item 13 is closed.
- **Drifted anchors updated:** `RoundEngine.cs` 910 → 915 lines (save `:591`, projection `:612`, agent log `:620`, root `:902`,
  constructor `:23-39`); `PanelService.cs` 1 849 → 1 915 (`SessionAnswerFor` `:1878-1894`, engine `:134-136`, re-projections
  `:164,176`); `RoundsDb.cs` 1 285 → 1 293 (`Command` `:172-181`, `RecordRound` `:190-202`, `RecordRoundRow` `:842-926`,
  `RecordReviewers` `:928-953`, agent-log bind `:887`); `RoundsQuery.cs` 991 → 1 006 (`LoggedRound` `:27-117`, ladder `:502-504`,
  its comment `:494-501`, `HasColumn` `:910-913`); `AgentLog.cs:246-287` → `:238-294`; `CallerSessions.cs:46-52` → `:45-51`;
  `rounds.ts` `RoundRecord` `:51-99`; `roundsLog.ts` `:384` → `:391`, `:519` → `:541`, `:1637` → `:1662`, `:1737` → `:1762`;
  the migration-test shape is now `ThePlanSourceIsStepTwentyThreeTests.cs` (forks `:176-192`).
- **New sections:** §8 *Open with E4*; §7 items 15–17.
