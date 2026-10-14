# PLAN — E7 stories: backfill the authors, hand-check twenty rounds, then answer

> Status: **plan only, nothing implemented yet, 2026-10-14.** Epic E7 of [PLAN_a_round_records_who_wrote_the_code_and_how_much.md](PLAN_a_round_records_who_wrote_the_code_and_how_much.md).
>
> Scope: the parent's §9 (backfill) and §11 steps 6–7 — one new one-shot mode `coai-mcp --backfill-authors` in
> `src_mcp`, run once per side over that side's own store and transcripts; the hand-check of ten rounds per side; the
> before/after answer in a `research/RESULTS_` file; the module docs and the promotion of the parent with its seven
> stories documents. Nothing in `src_vs_code`, nothing on the Team server, no new reader or attribution rule — E7
> REUSES E2's numstat, E3's reader, E4's attribution and E5's row writers, and adds only the selection, the
> reconstruction of a stored round's history, the narrow write and the mode.

## 1. What this epic delivers, and how it is split

The parent's E7 goal: *`--backfill-authors [--since]` recovers what still exists, per side; ten rounds per side are
hand-read before any number is reported; the owner's question is answered in a `RESULTS_` file; the plan is promoted.*
The split follows the one failure a backfill can cause with no symptom: **a historical row rewritten into something it
was not** — a resolution lost, an older observation overwritten by a weaker one, a second run doubling a total, a
round attributed from the wrong open time. So the code stories come first and are table-tested offline against stores
seeded with exactly those hazards; the attended steps then run the finished mode once per side, prove the second run
changes nothing, and only after an independent hand-check does any number appear.

**Roles** ([subagent-models.md](../.agents/conventions/common/subagent-models.md) § *The pipeline*). E7 carries **no
cornerstone area** (the parent's `## Epics`: "an upsert with a shipped twin"), so this document marks no story
`model: fable` and leaves the ledger line as the parent has it. Every **code** story runs: scenarios + brief **Opus** →
code + tests **Sonnet** → review **Opus** → fixes **Sonnet**. Every **operation** step is the **main session (Opus),
attended**: it runs the binary, never through a subagent, and judges nothing but the pass criteria written below. The
**hand-check** is a **judge** — a fresh Opus agent that neither wrote nor reviewed any of E3–E7's code, or the operator
— never the session that wrote the code ([measurement.md](../.agents/conventions/common/measurement.md) § 2 and § 5).

| Kind | Stories | Who |
|---|---|---|
| Code (offline, table-tested) | E7.S1 – E7.S4 | Opus brief → Sonnet code → Opus review → Sonnet fixes |
| Operation (writes the live stores, attended) | E7.O1, E7.O2, E7.O3, E7.O4, E7.O5 | the main session, one side at a time |
| Hand-check (judgement) | E7.J1 | a fresh Opus judge (`model: opus`), or the operator |

## 2. What E7 consumes from E2–E6

E2–E6 are split into stories in parallel with this document; the names below are the PARENT's (its §5–§8, §11 and
`## Epics`). **Their stories documents are authoritative for the exact signatures** — where they rename something,
follow them and keep this document's intent. The architect's concurrent decision is assumed: **the authors are also a
nullable snapshot on `RoundRecord`** (called `RoundRecord.Authors` here), beside E2's nullable `RoundRecord.Size`, both
following the `RoundRecord.Caller` precedent (`src_mcp/src/Server/SessionStore.cs:173-193`).

| From | Piece | Used by |
|---|---|---|
| E2.S1 (the predecessor rule), E2.S2 (the numstats), E2.S3 (`round_changes` writer, `RoundRecord.Size`) | `ReadCounted`, `ChangeSize` (scope × coverage × kind), the delta's predecessor rule ("the gate session's last CODE round with status `done` and a `Sha`", parent §7, `SessionStore.cs:96-104`) and `merge-base --is-ancestor`; the `round_changes` row writer; `RoundRecord.Size` | S2, S3 |
| E3.S2 (`LocateStored`, `NoReaderFor`), E3.S4 (`ObservedSession`, `ReaderVersion`, states, the byte budget) | `CallerTranscript` → `ObservedSession`; the reader's version constant (`authors_reader`); the `partial: <why>` / `unobserved: <why>` reasons; the byte budget | S1, S2 |
| E4.S2, E4.S3, E4.S4 | `RoundAttribution` — pure: an `ObservedSession` + the gate session's rounds → windows, association, ambiguous totals, contributors | S2 |
| E5.S1 (`RoundRecord.Authors`), E5.S2 (step 23, `RoundAuthorsTable`), E5.S4 (the live call site) | schema step 23 (`round_authors`, `rounds.authors_state`, `authors_session`, `authors_reader`, the ambiguous totals); the row writer for one round's authors; the function that decides `no session id` / `no reader for <vendor>`; the live call site in `RoundEngine.cs:605-625`; `RoundRecord.Authors` | S1, S2, S3 |
| E6.S4 (the JSON of sums, `--add`), E6.S5 (the mode) | `coai-mcp --round-authors` with `--add`, `--table`, cohorts and the JSON of sums | S4 (the "no totals changed" oracle), O4 |
| E1.S5 (fixtures), E1.S4 (the WSL measurements) | the sanitised fixtures (every text field a sentinel) and the WSL-side measurements in its `research/RESULTS_` file | S2, S3, S4, O1 |

## 3. Decisions every story shares

Each was settled by opening the file; the line it rests on is given.

- **A backfill never calls `RecordRound`.** `RoundsDb.RecordRound` (`src_mcp/src/Store/RoundsDb.cs:190-202`) rewrites
  every column of the round row (`:846-867`), deletes and re-inserts the round's reviewers (`:925`), and deletes every
  finding past the count it is handed (`:960`). A backfill holds none of those inputs, so through `RecordRound` it would
  erase findings and their resolutions. The parent's E7 scope cites `RoundsDb.cs:849-886` as "upsert by round": that is
  this statement, and it is the wrong tool here (§8, point 8). The backfill writes ONLY the authors columns and the
  round's `round_authors` / `round_changes` rows, through E5's and E2's own row writers.
- **The open time comes from the session file, never from the database.** `sessions.opened_utc` is the clock of the
  FIRST PROJECTION — bound to `DateTime.UtcNow` when the first round is recorded (`RoundsDb.cs:837`) and never updated
  (`:831-832`) — so it is later than `open` by at least one whole round. `open` stamps the real one on the session file
  (`src_mcp/src/Server/PanelService.cs:492-501`, `PersistedSession.OpenedUtc`, `SessionStore.cs:248`). A session file
  whose `OpenedUtc` is the default leaves the first window unbounded, which the live path already treats as "no
  window" (`src_mcp/src/Server/Rounds/RoundEngine.cs:895-900`).
- **The caller's session id is the recorded `rounds.caller`.** It is not on the session file — `RoundContext.Caller`
  (`src_mcp/src/Store/RoundContext.cs:37-42`) carries it straight to the projection (`RoundEngine.cs:614-615`,
  `RoundsDb.cs:885`). So the population a backfill walks is the DATABASE's rounds; the session file adds `OpenedUtc`,
  the rounds' order and the place the snapshot lives. Which id source the live server used (the registry, the variable)
  cannot be re-asked after the fact; a backfilled row records `authors_session` as the recorded caller.
- **Session files are written under the session's claim, and two writers do not take it.** A round and `resolve` hold
  `SessionClaim` (`src_mcp/src/Server/SessionClaim.cs:44-58`; taken at `RoundEngine.cs:222`, `PanelService.cs:1524`,
  `AskHumanService.cs:349`); every read and write also takes `SessionTurn` (`SessionStore.cs:375-405,463-468,502`).
  But `open` loads and saves WITHOUT the claim (`PanelService.cs:492-506`), and so does the startup orphan sweep
  (`SessionStore.cs:586-600`). A backfill takes the claim, and still verifies after writing and heals a lost snapshot
  on its next run (S1 rule 5, S3) — locking `open` is a different change and is not made here.
- **Opening the store.** `RoundsDb.PresenceOf` then `RoundsDb.OpenExisting` (`RoundsDb.cs:89-116`): a missing database
  is "nothing to backfill", and the data directory is never created (`:96-102`). `OpenExisting` migrates, which a
  WRITER may do; an older server meeting a file at a later step simply runs nothing (`src_mcp/storage/SqliteMigrator.cs:80-87`).
- **Exit codes**, as every one-shot mode here (`.agents/PROJECT.md:50-75`, `src_mcp/src/Server/RoundCountsMode.cs:18-25`):
  **0** done (a summary on stdout), **65** a bad argument, **74** a store that will not read or write, **never 64**.
- **Text-free.** Nothing a backfill writes, prints or logs carries a prompt, a description, a tool input other than
  the path test, or message text (parent §5). Every test that runs the reader seeds E1's sentinels and asserts their
  absence from the database bytes, the session files, stdout and stderr.
- **WSL.** Each side backfills its own store from its own transcripts (parent §9); the WSL store is never opened
  across the drvfs boundary (`src_vs_code/src/roundCountsRead.ts:18-21`). In WSL: **every command runs from a script
  file** (written on the Windows side, run as `wsl.exe -e bash <script>`), **nothing is deleted** — no `rm`, no
  `mv` over an existing file, no cleanup step — and every output goes to a NEW file in a new folder.
- **Never** kill a process by image name; **never** set a git identity — the tests' temporary repositories commit
  with identity in the environment of the `git` child, as the existing git fixtures do.
- **Tests** run from the executable — `dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4`, then
  `node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe` with
  `--filter-class "*Backfill*"` while iterating and the whole suite before review — never `dotnet test`. A break-it
  reverts the ONE production line the named guarantee depends on, watches the named test go red with the real symptom,
  restores it, and watches it go green ([testing.md](../.agents/conventions/common/testing.md) § *Every bug fix starts
  with a RED test*). A break-it never disables a test or a tripwire.
- **Place.** New code under `src_mcp/src/Server/Backfill/`; tests under `src_mcp/tests/` named for their guarantee.
  `RoundEngine.cs` (910 lines), `PanelService.cs` (1 849) and `RoundsDb.cs` (1 285) gain nothing but, for `RoundsDb`,
  use of its existing `Command()` seam (`RoundsDb.cs:173-181`), the way `QuestionConsultTable` writes its table.

## 4. Story map

| Id | Title | Kind | Size (changed lines, code + tests) | Depends on |
|---|---|---|---|---|
| E7.S1 | Which rounds, and what a backfill may do to each (pure) | code | ≈ 260 | E3.S4, E3.S2, E5.S2 |
| E7.S2 | A stored round's history, rebuilt exactly as the live path sees it | code | ≈ 420 | E2.S1, E2.S2, E4.S2, E4.S3, E5.S4, E1.S5 |
| E7.S3 | The narrow write — authors and size of one stored round, nothing else | code | ≈ 420 | S1, E2.S3, E5.S1, E5.S2 |
| E7.S4 | `coai-mcp --backfill-authors`: the mode, its backup, its summary, its scenario | code | ≈ 450 | S1, S2, S3, E6.S4 |
| E7.O1 | Before anything is written: the binary, the predictions, the sample rule, the dry runs | operation | — | S4 released (parent §16 D16) |
| E7.O2 | Backfill Windows, twice | operation | — | O1 |
| E7.O3 | Backfill WSL, twice, through script files | operation | — | O1 |
| E7.J1 | Hand-check ten rounds per side | judgement | — | O2, O3 |
| E7.O4 | The answer — `--round-authors` over both sides, the `RESULTS_` file | operation | — | J1 passed |
| E7.O5 | Docs and promotion | operation | — | O4 |

Build order: S1 as soon as E3.S4's state vocabulary and E5.S2's step 23 exist; S2 once E4.S3 and E5.S4 exist; S3 after
S1 and E5.S2's row writer; S4 last of the code. Then O1 → O2 and O3 (either order, one at a time) → J1 → O4 → O5. A defect J1
finds sends the work back through a new RED-test story before O4 (J1's rule).

## 5. Code stories

### E7.S1 — Which rounds, and what a backfill may do to each (pure)

- **Goal.** One pure decider says, for each stored round, whether the backfill computes it, keeps what is there, heals
  the session-file snapshot from the database, or defers it — and with which reason. It is the whole of "never
  overwrite a newer or equal reader" and "never replace a stronger observation with a weaker one", in one place.
- **Scope.** New `src_mcp/src/Server/Backfill/BackfillDecision.cs` (no I/O); tests
  `src_mcp/tests/ABackfillDecidesEachRoundOnceTests.cs`.
- **Brief.**
  - Input per round: `StoredRound(RoundId, SessionId, Stage, Number, Caller, CallerVendor, StartedUtc, AuthorsState,
    AuthorsReader, HasSize, BaseRef, HeadSha)` from the database, `SnapshotPresence(FileHasRound, HasAuthors, HasSize)`
    from the session file, the CURRENT reader version (E3/E5's constant, read from the type — never a literal), and
    `--since`.
  - **Strength** — `observed` 3 > `partial` 2 > `unobserved` 1 > empty 0 — one function over E5's state vocabulary.
  - **Rules, first match**: (1) `StartedUtc` before `--since` → not selected. (2) A non-empty state written by a reader
    ≥ the current one → `keep: equal or newer reader` — never recomputed. (3a) The session file's round holds a
    snapshot of a reader ≥ the current one but the database's authors are empty or of an older reader →
    `re-project`: the rows are written FROM the snapshot, the transcript not read (gate finding 3 — the completion of
    a run killed between its two writes). (3) The database holds such a row but the
    session file's round has no `Authors` snapshot → `heal snapshot` (copied from the database rows, NOT recomputed —
    the transcript may be gone since). (4) Empty, or an older reader → `compute`; after computing, the result is
    written only when its strength ≥ the existing one, else `keep: stronger existing`. (5) Size, decided apart:
    stage `code` only (feature rounds are left for later, parent §7); present → keep; absent with both `BaseRef` and
    `HeadSha` → compute; else `size: no base recorded` / `size: no head recorded`.
  - **Every selected round gets a state** (parent DoD: "every round records `authors_state`"): a caller that is not a
    session id, or a vendor with no reader, is computed as `unobserved: stated id` (a `stated` caller vendor, parent
    §16 D9) / `unobserved: no session id` (`repo:<path>`, blank) / `unobserved: no reader for <vendor>` through E3.S2's
    own locator (`LocateStored`) — not skipped.
  - **The reasons are a closed set** with fixed sentences — E3/E4's `partial`/`unobserved` reasons passed through, plus
    the backfill's own: `transcript not found`, `session file gone`, `session file replaced` (its `SessionId` is not
    the round's), `open time not recorded`, `repository not found`, `commit not found: base|head`, and `deferred: the
    session was busy` (never recorded on the round — it is retried). No reason carries a path or free text.
- **Tests (RED first, against a stub that answers `compute` for everything).**
  - A table over **every** (existing state × existing reader older / equal / newer × computed state) cell, generated
    from E5's state enum and E3's version constant rather than listed by hand
    ([testing.md](../.agents/conventions/common/testing.md) § *A test that repeats a list*).
  - `AnEqualOrNewerReadersRow_IsNeverRecomputed`; `AWeakerResult_NeverReplacesAStrongerOlderOne`;
    `AnOlderReadersRow_IsReplacedOnlyByAnAtLeastAsStrongResult`; `AFilledRowWithoutItsSnapshot_IsHealedNotRecomputed`;
    `ACodexCaller_GetsAStateNotASkip`; `APlanRound_GetsAuthorsAndNoSize`.
  - **Break-its.** The reader comparison made `>` instead of `≥` → `AnEqualOrNewerReadersRow_IsNeverRecomputed` red
    ("expected keep, found compute"); the strength guard removed → `AWeakerResult_…` red naming the downgraded state.
- **Done when.** Every cell of the table passes; both break-its observed red and restored green; no reason sentence
  contains a path.
- **Depends on.** E3.S4 (states, reader version), E3.S2 (`LocateStored`, the no-reader reason), E5.S2 (step 23's column meanings).
- **Model.** scenarios + brief **opus** → code + tests **sonnet** → review **opus** → fixes **sonnet**.

### E7.S2 — A stored round's history, rebuilt exactly as the live path sees it

- **Goal.** For one stored round N, build exactly the inputs E4's `RoundAttribution` and E2's delta predicate receive
  on the live path — from what is on disk — so a backfilled row equals the row the live path would have written.
- **Scope.** New `src_mcp/src/Server/Backfill/StoredHistory.cs` (reads the database rows and the session file) and
  `src_mcp/src/Server/Backfill/CallerReads.cs` (one `ObservedSession` per caller session id, read once per run);
  tests `src_mcp/tests/ABackfilledRoundIsTheRoundTheLivePathWroteTests.cs`.
- **Brief.**
  - **The session file**: `SessionStore.Load(repo_path, branch)` (`SessionStore.cs:431-448`) — code and plan rounds
    live in the branch's own session (no document, no feature). Its `State.SessionId` must equal the database's
    `session_id`, else `session file replaced`; absent → `session file gone`.
  - **The rounds list** E4 tiles windows from is one shape — `(stage, number, startedUtc, completedUtc, status, sha)`
    for every round of the gate session strictly BEFORE N, plus N's own start. Filled from the session file when it
    is there, else from the database rows (a round N > 1 is still computable without the file: its window starts at
    round N−1's START, which both hold). The **first** round's window needs `open`, which only the file holds:
    without the file, or with a default `OpenedUtc`, round 1 is `unobserved: open time not recorded`.
  - **`before-open`** (parent §6) needs the end of the last window this caller session already gave to ANY gate session
    on the same `repo_path`: read from the database — every round whose `caller` is this id, joined to `sessions` by
    `repo_path` — and handed to E4 in E4's shape.
  - **Transcripts**: `PanelSettings.AgentLogDir` (`src_mcp/src/Server/PanelSettings.cs:386,972`, `COAI_AGENT_LOG_DIR`)
    else `AgentLog.DefaultProjectsDir` (`src_mcp/src/Store/AgentLog.cs:39`) — the resolution the live path uses
    (`RoundEngine.cs:897`). One read per caller session id; the rounds of one caller are attributed from that one read.
  - **The budget**: E3's byte cap exactly as live; the live 2 s wall clock is NOT applied (no answer waits on a
    backfill, and a wall clock would make a backfill depend on machine load) — a backfill's own ceiling is 60 s per
    caller session, and hitting it records `partial: read budget` as live does (§8 point 4).
  - **The delta's predecessor**: E2's predicate, fed the same rounds list.
- **Tests.**
  - **Parity, the main one.** Over each E1 fixture: record round N through E5's LIVE path with the transcript cut at
    round N's start; copy the store with the authors columns and rows cleared; backfill the copy over the WHOLE
    transcript. The `round_authors` rows of every window ≤ N are identical, and the state is never weaker
    (`ABackfilledRound_HasTheLiveRowsForEveryWindowUpToIt`). The fixture of a background agent completing after round
    N started is the case where the backfill legitimately sees MORE — its test asserts the window split, not equality
    of state.
  - `TheOpenTimeIsTheSessionFiles_NotTheFirstProjections`: a store whose `sessions.opened_utc` is an hour after the
    file's `OpenedUtc` with work in between. **Break-it**: read the open time from the database → red, the first
    window's tokens short by exactly the hour's.
  - `TheRoundsListIsOneShape_WhicheverSourceFilledIt`: a store holding both — the file-filled and the database-filled
    lists are equal.
  - `ASecondGateSessionOfOneCaller_StartsWhereTheFirstLeftOff` (before-open across sessions);
    `ARound1WithoutItsFile_IsUnobserved_AndRound2IsStillComputed`; `AReplacedSessionFile_IsNamed`.
  - `EachCallersTranscriptIsReadOnce`: a counting reader seam over three rounds of one caller → one read.
  - Sentinels absent from every rendering.
- **Done when.** The parity test passes on every E1 fixture; the open-time break-it observed red and restored.
- **Depends on.** E1 (fixtures), E2 (predecessor rule), E4 (`RoundAttribution`), E5 (live path, row shape).
- **Model.** scenarios + brief **opus** → code + tests **sonnet** → review **opus** → fixes **sonnet**.

### E7.S3 — The narrow write: authors and size of one stored round, nothing else

- **Goal.** Write one round's backfilled authors and size into BOTH places — the database projection and the session
  file's snapshot — without touching a single other byte of either, and so that running it again changes nothing.
- **Scope.** New `src_mcp/src/Server/Backfill/BackfillWriter.cs`; the database half through `RoundsDb.Command()`
  (`RoundsDb.cs:173-181`) calling E5's and E2's row writers — if those are private to `RoundsDb`, they are EXTRACTED
  to a shared unit both use ([reuse-first.md](../.agents/conventions/common/reuse-first.md) step 2.2), never retyped
  as a second `INSERT`; tests `src_mcp/tests/ABackfillLeavesEverythingElseWhereItWasTests.cs`.
- **Brief.**
  - **The session file FIRST, under the claim** (plan gate finding 3): `SessionClaim.TryTake(dataDir, repo_path,
    branch)` (`SessionClaim.cs:44-58`) → `SessionStore.Load` → replace ONLY the matching `(stage, number)` round's
    `Authors` / `Size` / `Gitlinks`, and only where S1 allowed → `SessionStore.Save` (`SessionStore.cs:471-484`) → load
    again and verify. When the claim is busy the round is `deferred` and NEITHER side is written. When the verify fails
    the database is not touched for that round.
  - **Then the projection, FROM the saved snapshot, one transaction**: `UPDATE rounds SET authors_state,
    authors_session, authors_id_source = 'stored', authors_reader, submodule_writes, uncovered_writes, <ambiguous
    totals> WHERE id = $round`; `DELETE` then `INSERT` that round's `round_authors`; the same for `round_changes` (and
    `size_gitlinks`) when S1 said compute-size, with `size_origin = 'backfill'` (parent §16 D12) — every value read
    from the snapshot just verified, never from the in-memory result. Nothing else is written: no `RecordRound`, no
    `RecordSession`. A run killed after the save and before this transaction leaves a snapshot without rows, which
    S1's rule 3a re-projects on the next run without reading the transcript.
  - **The race §3 names** (`open` and the orphan sweep save without the claim): a verify that finds the snapshot gone
    leaves the database row as written; the next run's S1 rule 3 heals it. The summary counts both.
  - **E5's live re-record must not undo a backfill.** When the live projection re-records a round whose session copy
    has `Authors == null`, the database's authors columns and rows must stay (null means *not recorded*, never
    *recorded empty* — the `Caller` precedent, `SessionStore.cs:185-191`). S3 adds the cross-check test; if E5's
    write clears them, S3 changes that write in the minimum — the authors columns kept when the snapshot is null, the
    `round_authors` rows untouched.
- **Tests (RED first, against a writer that calls `RecordRound` with an empty findings list — the plausible wrong
  shape, kept in the suite as `ThroughRecordRound_TheResolutionsWouldBeLost_AndThatIsThePoint`).**
  - `ABackfill_LeavesEveryDecisionWhereItWas`: a store with resolved findings (accept, reject with reason,
    `resolved_utc`), `rounds.accepted`/`rejected`, reviewers with `model`, a `collect_state`, a `round_key`, an
    `agent_log`, a cost. After the backfill a canonical dump of `findings`, `reviewers`, `sessions` and every
    non-authors column of `rounds` is byte-identical. **Break-it**: route through `RecordRound` → red, *expected 3
    findings, found 0*.
  - `ASecondWrite_ChangesNoRow`: the same round written twice → the `round_authors` and `round_changes` multisets and
    every count identical. **Break-it**: drop the `DELETE` before the insert → red with doubled rows.
  - `TheReaderVersionIsOnEveryWrittenRound`: `authors_reader` equals E3's constant, read from the type. **Break-it**:
    bind an empty version → red; and the consequence is asserted too — with the version missing, S1 reselects the round
    on the next run, so a correct first run must leave `written = 0` for the second.
  - `ANewerReadersRow_Survives` and `AStrongerOlderObservation_Survives`, at the writer with S1's decision.
  - `TheSessionFileKeepsEveryOtherField`: the file before and after, parsed, with the touched round's `Authors` and
    `Size` removed from both, is equal; the other rounds' snapshots are untouched.
  - `ABusySession_IsDeferred_AndNeitherSideIsWritten`: the test holds the claim → database and file unchanged,
    `deferred = 1`.
  - `TheSnapshotIsSavedBeforeTheProjection`: a writer seam that throws after the session-file save and before the
    database transaction (a test-only fault point, the run "killed" between the two writes) → the file holds the
    snapshot, the database holds no authors for the round; the next run re-projects it (S1 rule 3a) WITHOUT opening
    the transcript (the fixture's transcript deleted between the runs proves it) and its `round_authors` rows and
    authors columns equal those of an uninterrupted run, byte for byte. **Break-it**: write the database first → after
    the fault the database holds rows the file does not, and the equality fails.
  - `ASnapshotLostToAConcurrentOpen_IsHealedOnTheNextRun`: write; save the file from a copy loaded before the write
    (what `open` does); run again → the snapshot is back, the database unchanged.
  - `ALiveReRecordWithoutTheSnapshot_KeepsTheBackfilledAuthors` (the E5 cross-check).
  - Sentinels absent from the database bytes and the session files.
- **Done when.** All pass; the three break-its observed red with the named symptom and restored green.
- **Depends on.** S1, E2.S3 (`round_changes` writer, `RoundRecord.Size`/`Gitlinks`), E5.S1 (`RoundRecord.Authors`), E5.S2
  (step 23, the authors writer).
- **Model.** scenarios + brief **opus** → code + tests **sonnet** → review **opus** → fixes **sonnet**.

### E7.S4 — `coai-mcp --backfill-authors`: the mode, its backup, its summary, its scenario

- **Goal.** The one-shot mode, end to end through the real process, with the parent's acceptance as its scenario:
  twice over a store holding one `observed` row of a newer reader, the second run changes no totals and the newer row
  and every resolution survive.
- **Scope.** New `src_mcp/src/Server/Backfill/BackfillAuthorsMode.cs` in the shape of `RoundCountsMode`
  (`src_mcp/src/Server/RoundCountsMode.cs:30-88`); its `Startup` member (enum at `src_mcp/src/Program.cs:43`, beside
  `RoundCounts` at `:297`), its arm in the selecting switch (`:328-339`) and its dispatch case (shape of `:441-444`);
  `--backfill-authors` added to the one-shot list in `.agents/PROJECT.md:53-55` (enforced by
  `TheSanctionedModesAreTheOnesTheDocumentNames`, `src_mcp/tests/ARequestFaultIsNotAnOldBinaryTests.cs:50-90`);
  `research/module_server.md` — a section beside `--round-counts` (`:3177`) with a Mermaid flow;
  `research/module_tests.md` — its rows; tests `src_mcp/tests/BackfillAuthorsCliScenarioTests.cs` through
  `OneShotProcess.StartInfo` (`src_mcp/tests/OneShotProcess.cs:32-53`) with `COAI_AGENT_LOG_DIR` pointing at E1's
  fixtures and a temporary git repository for the size.
- **Brief.**
  - `coai-mcp --backfill-authors [--since <ISO-8601 UTC instant>] [--dry-run]` — each flag at most once, anything else
    is 65 with the usage sentence on stderr (`Program.TryParseInstant`, as `RoundCountsMode.cs:84-87`).
  - **Open**: `PresenceOf` confirmed absent → exit 0 with an empty summary, nothing created; present but not opening →
    74 with `Program.WhyUnreadable`'s sentence (`RoundCountsMode.cs:62-68`).
  - **Backup before the first write** (not under `--dry-run`): SQLite's online backup of `coai.db` and a copy of every
    `sessions/session-*.json`, into `<dataDir>/backfill-backups/<yyyy-MM-ddTHH-mm-ssZ>/`, named in the summary. The
    mode never deletes a backup. The online backup is new to this repository — the published Native-AOT binary is
    exercised once in O1 (the dry run does not touch it; O2's first run does), per
    [testing.md](../.agents/conventions/common/testing.md) § *Starting is not working*.
  - **Run**: S1 selects, S2 rebuilds, the reader reads once per caller, S3 writes — ordered by (caller, started).
  - **Summary JSON on stdout**, text-free: `readerVersion`, `since`, `dryRun`, `backupDir`, `selected`, `written`,
    `healed`, `kept: {equalOrNewerReader, strongerExisting}`, `deferred`, `unrecoverable: {<reason>: n}`,
    `size: {written, kept, unrecoverable: {<reason>: n}}`, `reprojected`, `elapsedMs`.
  - **Exit codes** (plan gate finding 5): **0** — done, including nothing to do and a missing store; **65** — a bad,
    repeated or unknown argument (usage on stderr); **74** — a store or session file that will not read or write;
    **75** (`EX_TEMPFAIL`) — the run completed but at least one busy session was deferred: the summary is printed
    and says to run again; **never 64**, which means "this binary does not know the mode".
- **Tests (the scenario; RED first against a mode that answers `{}` and writes nothing).**
  - `BackfillTwice_TheSecondRunChangesNoTotals`: run 1 writes; run 2 has `written = 0`, `healed = 0`; the SHA-256 of
    a canonical dump of the authors and size tables is identical after both runs, and so is `--round-authors`'s JSON
    (E6) taken after each. **Break-its**: S1's reader check removed → run 2's `written` equals run 1's; S3's `DELETE`
    removed → the dump and the report's totals differ.
  - `ANewerReadersRowAndEveryResolutionSurviveTwoRuns` — the parent's done-when, verbatim: one `observed` row of
    reader `current + 1` and a set of resolutions; two runs; the newer row and the resolutions byte-identical.
  - `UnrecoverableRoundsAreCountedPerReason`: a store with one round of each reason → each counted once, and the
    round's state names it.
  - `ADryRunWritesNothing`: SHA-256 and mtime of `coai.db` and every session file unchanged, no backup folder; its
    counts equal the real run's.
  - `TheBackupHoldsTheStoreAsItWas`: the backup opens and its row counts equal the pre-run counts.
  - Exit codes, one test each: 0 with no database and no data directory created, and 0 when every round is already
    current (nothing to do); 65 for a bad, a repeated and an unknown flag; 74 for a `coai.db` that is a directory and for
    a session file that cannot be written; 75 when the test holds one session's claim during the run (the summary
    printed, `deferred = 1`); and a sweep asserting no path through the mode returns 64.
  - `--since`: a round started before it is untouched.
  - Sentinels absent from stdout, stderr, the database bytes and the session files.
- **Done when.** The scenario passes; both break-its observed; `--backfill-authors` is in `.agents/PROJECT.md`'s one-shot
  list IN THE SAME COMMIT that wires it into `Program.cs`, and `ARequestFaultIsNotAnOldBinaryTests` is green; each exit
  code 0 / 65 / 74 / 75 has its test and 64 is never returned; `research/module_server.md` and `module_tests.md` carry it; the whole
  `CoaiMcp.Tests.exe` suite and `node .agents/conventions/tools/plan-lifecycle.mjs` green.
- **Depends on.** S1, S2, S3; E6.S4 (the report's JSON of sums, used as the totals oracle).
- **Model.** scenarios + brief **opus** → code + tests **sonnet** → review **opus** → fixes **sonnet**.

## 6. Operation and hand-check

The outputs of O1–O4 — summaries, `--log` and `--round-authors` JSON, the judge's sheet — are working files in a new
folder per step (the scratchpad on Windows, a new folder under `~/coai-backfill/<utc>/` in WSL). Only the `RESULTS_`
file and what it cites are committed.

### E7.O1 — Before anything is written: the binary, the predictions, the sample rule, the dry runs

- **The binary.** Per side, the RELEASED build that contains S1–S4 (parent §16 D16: the code is released first through
  the normal flow; this run and the RESULTS file are a second, docs-and-data commit; the WSL binary is built INSIDE
  WSL — NativeAOT does not cross-compile Windows → Linux); record `coai-mcp --version` and the commit it was built
  from, per side.
- **Predictions first** ([measurement.md](../.agents/conventions/common/measurement.md) § 4), written into the
  draft `research/RESULTS_who_wrote_the_code_under_review.md` BEFORE any run: per side, the rounds `--since
  2026-09-30` the backfill will select; the share it will find `observed` or `partial` (Windows: ≥ 95 % of code
  rounds with a session-id caller — the parent measured transcripts for 392 of 398, §1; WSL: E1's measurement); the
  counts per unrecoverable reason; the run time.
- **The sample rule**, written before the backfill runs, so the judge cannot be handed a convenient ten: per side, ten
  CODE rounds with a session-id caller, drawn in ascending SHA-256 of `round_key` (`rounds.round_key`, the round's own
  id, `src_mcp/src/Store/Schema.cs:614-616`; rounds older than step 21 by `session_id/number`) subject to: ≥ 3 first
  rounds and ≥ 3 corrective; ≥ 2 with a Sonnet `wrote here` coder and ≥ 1 `main only`; ≥ 1 from each side of
  2026-10-13; ≥ 1 `partial` or with ambiguous totals and ≥ 1 `unobserved`, where any exists; no branch more than three
  times. A stratum the store cannot fill is recorded as unfillable, not padded.
- **Dry runs**, both sides (`--dry-run`), the WSL one from a script file; the two summaries recorded against the
  predictions.
- **Pass.** Both dry runs exit 0; every number is beside its prediction; any prediction missed by more than its own
  stated margin is explained in writing before O2 — not by adjusting the prediction.
- **Model.** main session (**opus**), attended.

### E7.O2 — Backfill Windows, twice

- Run `coai-mcp --backfill-authors --since 2026-09-30T00:00:00Z`; record the summary and the backup folder. Run
  `--round-authors` (JSON) and keep it. Run the backfill again; record its summary; run `--round-authors` again and
  compare the two JSON documents by SHA-256. Re-run while `deferred > 0`, recording each.
- **Pass.** The second run reports `written = 0` and `healed = 0` (or only heals it can name); the two report JSONs are
  byte-identical; the backup opens.
- **Model.** main session (**opus**), attended.

### E7.O3 — Backfill WSL, twice, through script files

- O2's sequence, with every command in a script file written on the Windows side and run as
  `wsl.exe -e bash /mnt/c/<scratchpad>/backfill-wsl-<n>.sh`; each script writes its outputs to a NEW folder
  `~/coai-backfill/<utc>/` (created with `mkdir -p` of a fresh timestamped name) and deletes nothing — no `rm`, no
  cleanup, no `mv` over an existing file. The outputs are read from Windows through `\\wsl.localhost\<distro>\…`,
  read-only. The WSL `--round-authors` JSON of the second run is the file O4 passes to `--add`.
- **Pass.** As O2.
- **Model.** main session (**opus**), attended.

### E7.J1 — Hand-check ten rounds per side

- **Who.** A fresh **Opus** judge launched with `model: opus`, which wrote and reviewed none of E3–E7's code — or the
  operator. Never the main session that briefed the code, never a coder.
- **Input.** The sample rule and the twenty round ids it drew (O1); `coai-mcp --log` output per side for those rounds
  (the state E5 prints, the size E2 prints — the WSL one produced by a script file); read access to the transcripts
  and the repositories. Not the reader's source: the judge re-derives, it does not re-run the product.
- **Per round, the checklist** — each field checked against the transcript or the repository directly:
  1. `authors_state` and its reason are true (an `unobserved: transcript not found` really has no
     `<projects>/*/<id>.jsonl`; a `partial` names a real gap).
  2. Per agent: requested and observed model, effort (or `unknown`), association and the write that earned it, window,
     launches; the main session's row.
  3. Tokens for ONE agent per round, summed once per `message.id` with the judge's own counting helper — written by
     the judge, importing no product code, kept under `scripts/` and named in the RESULTS file by path and sha.
  4. Ambiguous totals: the agents counted there truly have no write evidence.
  5. Size: `git diff --numstat -z <base>..<head>` by hand, against `whole` per coverage and kind; the `delta` where one
     is recorded.
  6. Decisions: the round's findings and resolutions read the same in `--log` as in the backup taken before O2/O3.
- **Agreement.** Every checked field EXACT — the reader's rule is the product's rule, so a disagreement is a bug
  report, not a harness note ([measurement.md](../.agents/conventions/common/measurement.md) § 2). On any
  disagreement: **stop, report no number**; the defect goes through a RED-test story (the round as a fixture, the
  failure watched, the fix, E3's reader version bumped); O2/O3 are re-run, which replaces the older-reader rows only
  where S1 allows; and ALL ten of that side are checked again.
- **Recorded** in the RESULTS file, first section: per round the `round_key` prefix (8 hex), its stratum, the fields
  checked and agree/disagree — no transcript text, no path, no branch of a repository outside this family.
- **Pass.** 10 of 10 per side agree on every field.
- **Model.** **opus** (fresh judge) or the operator.

### E7.O4 — The answer

- Inside WSL (script file): `coai-mcp --round-authors --since 2026-09-30T00:00:00Z --split 2026-10-13T00:00:00Z` →
  `wsl.json`. On Windows: the same flags with `--add <wsl.json> --table`, for `--cohort first` and `--cohort
  corrective` (never pooled), with and without `--with-before-open`.
- **Before any number is copied**, the size caveat is measured, not assumed: `git log --since=2026-09-30 -L` over
  `DiffExclusions.Default` (`src_mcp/runners/Context/ContextAssembler.cs:7-30`) and `DiffShaper.DefaultMaxBytes`
  (`src_mcp/core/Context/DiffShaper.cs:28`). If either changed in the window, a backfilled `elided`/`excluded` split
  may differ from what the reviewers were shown, and the file says for which rounds (§8 point 7).
- **`research/RESULTS_who_wrote_the_code_under_review.md`**, in this order:
  1. **Header** — *association only*: the cells say which coder mixes and reviewer sets co-occurred with which finding
     rates, not that one caused the other; where the coder mix and the reviewer set changed together, the table cannot
     separate them, said in so many words. Conditions: the binary and commit per side, the date, `--since`, `--split`,
     the denominator (`shown + elided` of `code` + `test`), the harness (the two modes, by version).
  2. **The hand-check** (J1), with its agreement.
  3. **The predictions against the observations** (O1–O3).
  4. **The tables**, each cell with its rounds, gate sessions, branch count, largest-branch share and marks; columns for
     reviewer set, effort, cohort and branch share (parent §11 step 7); `Unknown` printed as unknown.
  5. **The naive reading, re-derived beside it** — the parent §1's date split (Windows 4.2 → 7.6, WSL 4.8 → 3.9
     findings per round) — and what the attributed table says about it.
  6. **What it does not settle**: task mix; edits through `Bash` (no evidence, parent §13); lines that survive (§3);
     a Team server's requested ≠ ran; rounds lost before 2026-09-25; every unrecoverable count.
- **Public repository.** Rounds named by `round_key` prefix, repositories outside this family and their branches
  anonymised (`repo-A`, `branch-1`); `wsl.json` is committed only if it holds no branch name and no path, else only
  the table. The private-data scan runs before the push.
- **Pass.** The file exists in that order; every figure in it traces to a mode's output kept in the working folder.
- **Model.** main session (**opus**), attended.

### E7.O5 — Docs and promotion

- `research/module_server.md`: the backfill (S4 wrote its section) and whatever of the reader, the attribution and the
  two tables E3–E6 did not already describe; `research/module_extension.md`: the card line, if E5 did not;
  `.agents/PROJECT.md` lists `--round-authors` and `--backfill-authors`.
- `/promote-plan` of the parent TOGETHER with its seven `_E<n>.md` stories documents
  ([planning-docs.md](../.agents/conventions/common/planning-docs.md) § *A plan split into epics and stories*), each
  `IMPLEMENTED <date>` with its deviations — this document's §8 included; `research/README.md` mentions; the
  `todo/README.md` rows removed in the same commit.
- **Pass.** `node .agents/conventions/tools/plan-lifecycle.mjs` and `pin-check.mjs` clean; `CoaiMcp.Tests.exe` and
  `npm test` in `src_vs_code` green.
- **Model.** main session (**opus**), attended.

## 7. Growth

| Surface | Projected size | Who retires it | Interrupted |
|---|---|---|---|
| `<dataDir>/backfill-backups/<utc>/` | one copy of `coai.db` (72 MB on Windows, parent §14) plus the session files per WRITING run; two runs per side plus one per J1 fix → ~150–360 MB on Windows, the WSL store's size × the same on WSL | the operator, by hand, after the RESULTS file is committed; the mode never deletes, and in WSL nothing is deleted by an agent | a crashed run leaves its folder, named by its start; the next run makes its own |
| Rows written by the backfill | none beyond the parent's §14 (`round_authors`, `round_changes`) — the backfill fills rows the live path would have written | with their round | per round: database first, then the file, under the claim; a crash between leaves a database row without its snapshot, which the next run heals (S1 rule 3) |
| Working folders of O1–O4 | summaries and JSON, < 1 MB per side | the operator | — |

## 8. Points raised to the parent (not settled here)

> **Settled 2026-10-14** in the parent's §16: 1 → D16 (release first, then a second docs-and-data commit; WSL built in
> WSL); 6 and 8 → D14; 7 → D12 (`size_origin = backfill`); 2–5, 9 and 10 accepted as resolved here (the README row now
> exists).

1. **One commit, but the operation needs a binary that contains this epic.** The parent puts E7 on one branch and ONE
   commit, yet O1–O4 must run a build with S1–S4 on BOTH sides. Either the code is merged and released first (the
   RESULTS file and the promotion then land as a second, docs-only commit — a deviation from "one commit"), or each
   side runs a build published locally from the epic's head (NativeAOT does not cross-compile Windows → Linux, so the
   WSL side needs a build inside WSL). The operator chooses; O1 records which.
2. **§9's selection contradicts its own guard.** "Rounds whose `authors_state` is empty" makes "never overwrite an
   `observed`/`partial` row of a newer or equal reader" unreachable. This document selects empty OR older-reader rows
   and never lowers a state's strength (S1) — which is also what lets a J1 fix re-backfill.
3. **§9 vs the DoD.** §9 walks only rounds "whose `caller` is a session id"; the DoD says every round records
   `authors_state`. This document writes `unobserved: <why>` for the others, so the report's exclusions are complete.
4. **"The same reader" is not quite possible.** The live budget includes a 2 s wall clock (§11 step 4), which depends
   on machine load; the backfill keeps the byte cap and drops that clock. A round the live path would have recorded
   `partial: read budget` can be backfilled `observed`; parity is therefore defined over windows ≤ N (S2).
5. **`sessions.opened_utc` is not the open time** (`RoundsDb.cs:837`). Anything in E4–E6 that reads it as one bounds
   the first window wrongly; only the session file's `OpenedUtc` is.
6. **The snapshot makes the backfill a session-file writer**, and `open` (`PanelService.cs:492-506`) and the orphan
   sweep (`SessionStore.cs:586-600`) save without the claim. S3 verifies and heals; E5's stories should state that a
   live re-record with a null snapshot keeps the database's authors.
7. **A backfilled size is a reconstruction.** `elided` is recomputed with today's `DiffShaper.DefaultMaxBytes` and
   `excluded` with today's `DiffExclusions.Default`. Neither change is likely, but nothing on a backfilled `Size`
   says it was reconstructed; E2's stories may want an origin on it. O4 measures whether it matters.
8. **The parent's E7 scope cites `RoundsDb.cs:849-886` as "upsert by round".** That is `RecordRoundRow`'s statement
   (`:846-868`), reached only through `RecordRound`, which a backfill must not call (§3).
9. **Who hand-checks** was unstated in §11 step 7; this document fixes it (J1).
10. **This document needs its own `todo/README.md` row** (planning-docs.md); it is not added here, so
    `plan-lifecycle.mjs` reports it until the row lands in the same commit as this file.

## Definition of Done

- [ ] S1–S4 merged with their tests; every named break-it observed red with its symptom and restored green.
- [ ] A backfill writes only the authors columns and the round's `round_authors` / `round_changes` rows, and the
      session file's snapshot under the claim — never through `RecordRound`.
- [ ] Twice over a store holding one `observed` row of a newer reader: the second run changes no totals; the newer row
      and every resolution survive; the reader version is on every written round.
- [ ] Unrecoverable rounds counted per reason; ran on both sides, twice each, with the predictions written first.
- [ ] Ten rounds per side hand-checked by a judge independent of the code, 10 of 10 agreeing, BEFORE any number.
- [ ] `research/RESULTS_who_wrote_the_code_under_review.md`: association only in its header, the hand-check first, the
      tables second, the naive reading beside them, what it does not settle.
- [ ] `.agents/PROJECT.md`, `research/module_server.md`, `module_extension.md`, `module_tests.md` updated; the parent
      and its seven stories documents promoted together; `plan-lifecycle.mjs` clean.
