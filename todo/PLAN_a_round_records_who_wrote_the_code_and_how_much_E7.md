# PLAN — E7 stories: backfill the authors, hand-check twenty rounds, then answer

> Status: **plan only, nothing implemented yet, 2026-10-14; revised 2026-10-16 against `main` at `2d98f2f8` (§9).** Epic E7 of [PLAN_a_round_records_who_wrote_the_code_and_how_much.md](PLAN_a_round_records_who_wrote_the_code_and_how_much.md).
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

E1 and E2 are SHIPPED (E1 #828; E2 #829, released in mcp 0.56.0); the plan-gate's step 23 (#837/#841) is in mcp 0.58.0
(published 2026-10-16 with extension 0.78.0). E3–E6 are planned; their stories documents are cited **as of** E3 `e3a94e0c`,
E4 `36560698`, E5 `fe4f878f` and this branch's E6, and are **authoritative for the exact signatures** — where a shipped one
renames something, follow it and keep this document's intent (§10 lists what may still move). The authors are a nullable
snapshot on `RoundRecord` (E5.S1's `AuthorsSnapshot`, in `core/Authors/`), beside the shipped `Size` and `Gitlinks`, all
following the `Caller` precedent (`src_mcp/src/Server/SessionStore.cs:173-223`).

| From | Piece | State on `main` | Used by |
|---|---|---|---|
| E2 (#829) | `RoundRecord.Size` / `Gitlinks` (`SessionStore.cs:202`, `:211`); `RoundSize`/`ScopeSize`/`SizeCell`, `ScopeState`, `SizeOrigin.Backfill` (`core/Context/RoundSize.cs:18-52`); the predecessor rule `DeltaPredecessor.Choose(earlier, against)` — the last `done`, non-skipped code round with a sha, held to this round's base through ITS `Size.Whole.From` (`src_mcp/src/Server/Rounds/DeltaPredecessor.cs:31-61`); the numstats `ContextAssembler.CollectAsync` / `AllChangesAsync` / `DeltaAsync` / `GitlinksAsync` (`runners/Context/ContextAssembler.cs:307,394,428,404`); the assembly `RoundSizer.MeasureAsync` (`src_mcp/src/Server/Rounds/RoundSizer.cs:32-56`); the writer `RoundChangesTable.Record(db, roundId, size, gitlinks)` (`src_mcp/src/Store/RoundChangesTable.cs:27-39`) — a null size writes NOTHING; schema step 22 (`Schema.cs:635-652`) | **shipped** | S1, S2, S3 |
| E3.S2 (`CallerTranscriptLocator.LocateStored(id, home)` — source `stored`, a `repo:…` value is `no session id`; `ClaudeHome.With(agentLogDir)`), E3.S4 (`CallerTranscript.Read(where, ReadBudget, fromUtc)` → `ObservedSession`; `ObservedSession.ReaderVersion = 1`; the four states and the closed `ObservationReasons`) | the reader, its version (`authors_reader`), its reasons | planned (E3 as of `e3a94e0c`, §E3.S2 brief 1–6, §3) | S1, S2 |
| E4.S1–S3 (`AttributionInput`, `RoundAttribution.ReadFrom` / `Assign`, `RoundAuthorsBuilder.Build` → `RoundAuthors`) | attribution of ONE round — pure; `ReadFrom` gives the `fromUtc` bound | planned (E4 as of `36560698`) | S2 |
| E5.S1 (`AuthorsSnapshot`, `IsWellFormed`, `Render`), E5.S2 (step 24, `RoundAuthorsTable.Record(db, roundId, authors)` — a null snapshot writes nothing), E5.S4 (`RoundAuthorsStep.For` — the live call; its peer lookup and `.gitmodules` read) | the snapshot, the step, the authors writer, the live path a backfill must equal | planned (E5 as of `fe4f878f`, §5) | S1, S2, S3 |
| E6.S4, E6.S5 | `coai-mcp --round-authors` with `--add`, `--table`, cohorts, the JSON of sums and the per-reason `delta unavailable` counts (E6 D10) | planned | S4 (the "no totals changed" oracle), O4 |
| E1 (#828) | fixtures `src_mcp/tests/fixtures/caller-transcripts/c01`–`c20` (each a `projects/` tree and a `rounds.json`; `manifest.json` names each case's `fixtureRoots`; instants at `2000-01-01`, ranks not real counts); `research/RESULTS_caller_transcripts_on_both_sides.md` (Windows 405 code rounds since 2026-09-30, 399 with a transcript, the 6 without are codex callers; WSL 516/516; largest session 650 MB, a day-gated Node parse p50 19.9 s) | **shipped** | S2, S3, S4, O1 |

## 3. Decisions every story shares

Each was settled by opening the file; the line it rests on is given.

- **A backfill never calls `RecordRound`.** `RoundsDb.RecordRound` (`src_mcp/src/Store/RoundsDb.cs:190-202`) upserts every
  column of the round row (`RecordRoundRow`, `:842-926`), deletes and re-inserts the round's reviewers (`RecordReviewers`,
  `:928-953`, the delete at `:930`), and deletes every finding past the count it is handed (`RecordFindings`, `:966-968`). A
  backfill holds none of those inputs, so through `RecordRound` it would erase findings and their resolutions (§8, point 8).
  The backfill writes ONLY the authors columns and size states and the round's `round_authors` / `round_changes` rows,
  through E5's `RoundAuthorsTable.Record` and the shipped `RoundChangesTable.Record` — never a second `INSERT`.
- **Both writers run inside ONE transaction the backfill opens** (E2 §10 point 10). `RoundChangesTable.Record` deletes the
  round's rows and inserts them again (`RoundChangesTable.cs:27-39`); only `RecordRound` wraps it in a transaction today
  (`RoundsDb.cs:196-201`), and `RoundsDb` exposes no transaction outward — its one seam is `Command()` (`:172-181`). So S3
  adds ONE internal line beside it, `internal SqliteTransaction Transaction() => _db.BeginTransaction();`, with the same
  remark (`RoundsDb.cs` is 1 293 lines and gains a seam, not a writer). A failure between the delete and the insert then
  leaves the round's earlier rows, never none.
- **The whole scope's `From` is `rounds.base_ref`, never a merge-base computed today** (E2 §10 point 9). `base_ref` is the
  base the live diff was resolved against — `CollectedDiff.ComparedAgainst` (`src_mcp/src/Server/PanelService.cs:891`) — and
  `RoundChangesLog` reads `Whole.From` from it (`RoundChangesLog.cs:53`, `:113-114`). Recomputing `merge-base(main, sha)` after the
  branch merged gives `sha` itself and a zero whole. `CollectAsync` resolves its own comparison base from what it is handed
  (`ContextAssembler.cs:307-321`); handed `base_ref`, an ancestor of `sha`, it resolves to `base_ref` — so S2 hands it
  `base_ref` and checks `ComparedAgainst == base_ref` before any size is kept (S1 rule 5, S2).
- **The open time comes from the session file, never from the database.** `sessions.opened_utc` is the clock of the
  FIRST PROJECTION — bound to `DateTime.UtcNow` when the first round is recorded (`RoundsDb.cs:838`) and never updated
  (the upsert sets only `repo_path`/`branch`, `:833`) — so it is later than `open` by at least one whole round. `open`
  stamps the real one on the session file (`src_mcp/src/Server/PanelService.cs:497-505`, `PersistedSession.OpenedUtc`,
  `SessionStore.cs:278`). A session file whose `OpenedUtc` is the default leaves the first window unbounded, which the live
  path already treats as "no window" (`src_mcp/src/Server/Rounds/RoundEngine.cs:898-907`).
- **The caller's session id is the recorded `rounds.caller`.** It is not on the session file — `RoundContext.Caller`
  (`src_mcp/src/Store/RoundContext.cs:40`) carries it straight to the projection (`RoundEngine.cs:612-620`). So the population a backfill walks is the DATABASE's rounds; the session file adds `OpenedUtc`,
  the rounds' order and the place the snapshot lives. Which id source the live server used (the registry, the variable)
  cannot be re-asked after the fact; a backfilled row records `authors_session` as the recorded caller.
- **Session files are written under the session's claim, and two writers do not take it.** A round and `resolve` hold
  `SessionClaim` (`src_mcp/src/Server/SessionClaim.cs:44-58`; taken at `RoundEngine.cs:222`, `PanelService.cs:1590`,
  `AskHumanService.cs:349`); every read and write also takes `SessionTurn` (`SessionStore.cs:405-431`, `:495`, `:532`).
  But `open` loads and saves WITHOUT the claim (`PanelService.cs:497-511`), and so does the startup orphan sweep
  (`SessionStore.cs:608-632`). A backfill takes the claim, and still verifies after writing and heals a lost snapshot
  on its next run (S1 rule 5, S3) — locking `open` is a different change and is not made here.
- **An OLDER server strips the snapshot.** `PersistedSession` declares no `JsonExtensionData` (`ServerJsonContext.cs:339-345`
  is the whole option set), so a `coai-mcp` older than E5's release that loads and saves a session file — `open`, a round,
  the orphan sweep, in any VS Code window on that side — drops its `authors` member. The database rows survive (an older
  server knows no authors column and `RoundChangesTable`'s null rule writes nothing), but the session file, the rebuild
  source, loses the snapshot until the next backfill run heals it. So O1 gates the write on every running server of the
  side being on the release that carries E5 and E7 (§6).
- **`SessionStore.Load` answers null for a missing file AND for a torn or busy one** (`SessionStore.cs:461-478`, *"a torn or
  momentarily busy file is a fresh session"*). `session file gone` is therefore decided by `SessionStore.Exists`
  (`:449-450`), and a file that exists but does not load is `deferred: the session file did not read` — retried, never
  recorded on the round.
- **Opening the store.** `RoundsDb.PresenceOf` then `RoundsDb.OpenExisting` (`RoundsDb.cs:89-116`): a missing database
  is "nothing to backfill", and the data directory is never created (`:92-102`). `OpenExisting` migrates, which a
  WRITER may do; an older server meeting a file at a later step simply runs nothing (`src_mcp/storage/SqliteMigrator.cs:80-90`).
- **A writer mode.** `--backfill-authors` joins `Program.WriterModes` (`src_mcp/src/Program.cs:310-314`), so a NAMED data
  folder that is missing is refused with 74 before the mode runs (`Program.cs:389-395`) — which is also how
  `EveryModeIsClassifiedForAMissingNamedRootTests` (`src_mcp/tests/EveryModeIsClassifiedForAMissingNamedRootTests.cs:45-58`)
  classifies it. An unnamed default directory with no store is the mode's own exit 0.
- **Exit codes**, as every one-shot mode here (`.agents/PROJECT.md:49-75`, `src_mcp/src/Server/RoundCountsMode.cs:18-25`):
  **0** done (a summary on stdout), **65** a bad argument, **74** a store that will not read or write — or a backup that
  did not verify (S4) — **75** at least one busy session deferred, **never 64**.
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
  `RoundEngine.cs` (915 lines) and `PanelService.cs` (1 915) gain nothing; `RoundsDb.cs` (1 293) gains the one-line
  `Transaction()` seam above beside its `Command()` (`RoundsDb.cs:172-181`), the way `RoundChangesTable` and
  `QuestionConsultTable` reach their tables; `Program.cs` (2 519) gains registration lines only.

## 4. Story map

| Id | Title | Kind | Size (changed lines, code + tests) | Depends on |
|---|---|---|---|---|
| E7.S1 | Which rounds, and what a backfill may do to each (pure) | code | ≈ 260 | E3.S4, E3.S2, E5.S2 |
| E7.S2 | A stored round's history, rebuilt exactly as the live path sees it | code | ≈ 420 | E2.S1, E2.S2, E4.S2, E4.S3, E5.S4, E1.S5 |
| E7.S3 | The narrow write — authors and size of one stored round, nothing else | code | ≈ 420 | S1, E2.S3, E5.S1, E5.S2 |
| E7.S4 | `coai-mcp --backfill-authors`: the mode, its backup, its summary, its scenario | code | ≈ 450 | S1, S2, S3, E6.S4 |
| E7.O1 | Before anything is written: the release, the running servers, the predictions, the sample rule, the dry runs | operation | — | E5, E6 and S1–S4 released (parent §16 D16; §6 O1) |
| E7.O2 | Backfill Windows, twice | operation | — | O1 |
| E7.O3 | Backfill WSL, twice, through script files | operation | — | O1 |
| E7.J1 | Hand-check ten rounds per side | judgement | — | O2, O3 |
| E7.O4 | The answer — `--round-authors` over both sides, the `RESULTS_` file | operation | — | J1 passed |
| E7.O5 | Docs and promotion | operation | — | O4 |

Build order: S1 as soon as E3.S4's state vocabulary and E5.S2's step 24 exist; S2 once E4.S3 and E5.S4 exist; S3 after
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
  - Input per round: `StoredRound(RoundId, SessionId, Stage, Number, Status, Verdict, Caller, CallerVendor, StartedUtc,
    AuthorsState, AuthorsReader, SizeState, BaseRef, HeadSha)` from the database (`authors_reader` is E5's INTEGER, `0` = none),
    `SnapshotPresence(FileHasRound, HasAuthors, AuthorsReader, HasSize)` from the session file, the CURRENT reader version
    (`ObservedSession.ReaderVersion`, read from the type — never a literal), and `--since`.
  - **Strength** — `observed` 3 > `partial` 2 > `unobserved` 1 > empty 0 — one function over the kind before `: ` of a state
    `AuthorsSnapshot.IsWellFormed` accepts (E5.S1); a stored state it refuses counts as empty, so it is recomputed.
  - **Rules, first match**: (1) `StartedUtc` before `--since` → not selected. (2) A non-empty state written by a reader
    ≥ the current one → `keep: equal or newer reader` — never recomputed. (3a) The session file's round holds a
    snapshot of a reader ≥ the current one but the database's authors are empty or of an older reader →
    `re-project`: the rows are written FROM the snapshot, the transcript not read (gate finding 3 — the completion of
    a run killed between its two writes). (3) The database holds such a row but the
    session file's round has no `Authors` snapshot → `heal snapshot` (copied from the database rows, NOT recomputed —
    the transcript may be gone since). (4) Empty, or an older reader → `compute`; after computing, the result is
    written only when its strength ≥ the existing one, else `keep: stronger existing`. (5) Size, decided apart:
    stage `code` only, a reviewed round only (status `done`, verdict not `skipped` — a skip reviewed nothing, parent §16 D1;
    feature rounds are left for later, parent §7); `size_state` non-empty → keep (a live size is never replaced, and a backfill
    never re-measures its own); empty with both `BaseRef` and `HeadSha` → compute; else `size: no base recorded` / `size: no
    head recorded`. Authors are decided for `PlanReview` and `CodeReview` rounds only (E5 §3), skipped rounds excluded.
  - **Every selected round gets a state** (parent DoD: "every round records `authors_state`"): a caller that is not a
    session id, or a vendor with no reader, is computed as `unobserved: stated id` (a `stated` caller vendor, parent
    §16 D9) / `unobserved: no session id` (`repo:<path>`, blank) / `unobserved: no reader for <vendor>` through E3.S2's
    own locator (`LocateStored`) — not skipped.
  - **The reasons are a closed set** with fixed sentences — E3/E4's `partial`/`unobserved` reasons passed through, plus
    the backfill's own: `transcript not found`, `session file gone`, `session file replaced` (its `SessionId` is not
    the round's), `open time not recorded`, `repository not found`, `commit not found: base|head`, `size: base not an
    ancestor` (the recorded `base_ref` is not an ancestor of the head — the live round's two-tip fallback, or a rewritten
    history — so no size is reconstructed rather than a new base guessed, §3), and `deferred: the session was busy` /
    `deferred: the session file did not read` (never recorded on the round — it is retried). No reason carries a path or
    free text.
- **Tests (RED first, against a stub that answers `compute` for everything).**
  - A table over **every** (existing state × existing reader older / equal / newer × computed state) cell, generated
    from E5's state enum and E3's version constant rather than listed by hand
    ([testing.md](../.agents/conventions/common/testing.md) § *A test that repeats a list*).
  - `AnEqualOrNewerReadersRow_IsNeverRecomputed`; `AWeakerResult_NeverReplacesAStrongerOlderOne`;
    `AnOlderReadersRow_IsReplacedOnlyByAnAtLeastAsStrongResult`; `AFilledRowWithoutItsSnapshot_IsHealedNotRecomputed`;
    `ACodexCaller_GetsAStateNotASkip`; `APlanRound_GetsAuthorsAndNoSize`; `ASkippedRound_GetsNeitherSizeNorAuthors`;
    `ALiveSize_IsNeverReplaced`; `AnExistingButUnreadableSessionFile_IsDeferredNotGone`.
  - **Break-its.** The reader comparison made `>` instead of `≥` → `AnEqualOrNewerReadersRow_IsNeverRecomputed` red
    ("expected keep, found compute"); the strength guard removed → `AWeakerResult_…` red naming the downgraded state.
- **Done when.** Every cell of the table passes; both break-its observed red and restored green; no reason sentence
  contains a path.
- **Depends on.** E3.S4 (states, reader version), E3.S2 (`LocateStored`, the no-reader reason), E5.S1 (`IsWellFormed`), E5.S2
  (step 24's column meanings — array index 23, `user_version` 24; steps 22 and 23 are shipped).
- **Model.** scenarios + brief **opus** → code + tests **sonnet** → review **opus** → fixes **sonnet**.

### E7.S2 — A stored round's history, rebuilt exactly as the live path sees it

- **Goal.** For one stored round N, build exactly the inputs E4's `RoundAttribution` and E2's delta predicate receive
  on the live path — from what is on disk — so a backfilled row equals the row the live path would have written.
- **Scope.** New `src_mcp/src/Server/Backfill/StoredHistory.cs` (reads the database rows and the session file) and
  `src_mcp/src/Server/Backfill/CallerReads.cs` (one `ObservedSession` per caller session id, read once per run);
  tests `src_mcp/tests/ABackfilledRoundIsTheRoundTheLivePathWroteTests.cs`.
- **Brief.**
  - **The session file**: `SessionStore.Exists` then `SessionStore.Load(repo_path, branch)` (`SessionStore.cs:449-450`,
    `:461-478`) — code and plan rounds live in the branch's own session (no document, no feature). Its `State.SessionId` must
    equal the database's `session_id`, else `session file replaced`; not there → `session file gone`; there but not loading
    → `deferred: the session file did not read` (§3).
  - **The rounds list** E4 tiles windows from is one shape — `(stage, number, startedUtc, completedUtc, status, sha)`
    for every round of the gate session strictly BEFORE N, plus N's own start. Filled from the session file when it
    is there, else from the database rows (a round N > 1 is still computable without the file: its window starts at
    round N−1's START, which both hold). The **first** round's window needs `open`, which only the file holds:
    without the file, or with a default `OpenedUtc`, round 1 is `unobserved: open time not recorded`.
  - **`before-open`** (parent §6) needs the end of the last window this caller session already gave to ANY gate session
    on the same `repo_path`: read from the database — every round whose `caller` is this id, joined to `sessions` by
    `repo_path` — and handed to E4 in E4's shape.
  - **Transcripts**: `ClaudeHome.Default.With(PanelSettings.AgentLogDir)` (E3.S2 brief 1; `AgentLogDir` is
    `src_mcp/src/Server/PanelSettings.cs:421`, from `COAI_AGENT_LOG_DIR` at `:1009`) — the one resolution E5.S4 makes of
    today's ternary (`RoundEngine.cs:902`, `AgentLog.DefaultProjectsDir`, `src_mcp/src/Store/AgentLog.cs:39`). The location
    is `CallerTranscriptLocator.LocateStored(rounds.caller, home)` — source `stored`, so `authors_id_source = 'stored'`.
    One read per caller session id per run, at the lowest `fromUtc` (`RoundAttribution.ReadFrom`) any of its selected rounds
    needs, cached for the run; the rounds of one caller are attributed from that one read.
  - **The budget**: E3's byte cap exactly as live (64 MiB over the `fromUtc` slice, parent §16 D7); the live 2 s wall clock is
    NOT applied (no answer waits on a backfill, and a wall clock would make a backfill depend on machine load) — a backfill's
    own ceiling is 60 s per caller session, and hitting it records `partial: read budget` as live does (§8 point 4). E1
    measured the largest Windows session at 650 MB and a day-gated Node parse of it at 19.9 s p50 (RESULTS Q6), so the
    ceiling is not decorative: O1 predicts how many rounds it will mark.
  - **The size** (code rounds S1 marked compute-size), reconstructed the live way but never re-based: `CollectAsync(repo_path,
    base_ref, head_sha)` (`ContextAssembler.cs:307`) and keep the result only when its `ComparedAgainst` equals `base_ref`
    (else `size: base not an ancestor`, nothing written); `DiffShaper.Shape` with TODAY's `DefaultMaxBytes`
    (`core/Context/DiffShaper.cs:28-30`); then `RoundSizer.MeasureAsync(context, repo_path, head_sha, collected, shaped,
    earlier, log, ct)` (`RoundSizer.cs:32-56`) — the live assembly, so the partition, the delta numstat, `numstats disagree`
    and the gitlink count are E2's own code, not a second copy. The result is `with { Origin = SizeOrigin.Backfill }`
    (parent §16 D12) and its `Whole.From` is `base_ref` by construction (E2 §10 point 9).
  - **The delta's predecessor**: E2's `DeltaPredecessor.Choose(earlier, base_ref)` (`DeltaPredecessor.cs:31-37`), fed the
    session file's rounds BEFORE N — it reads each predecessor's `Size.Whole.From` (`:61`). So the run walks each gate session
    in round order and saves a round's backfilled `Size` (S3) BEFORE it sizes the next: a predecessor older than step 22 then
    carries the size this run gave it; one that could not be sized has none, and the delta reads `unavailable: base unknown`
    — the honest state, never a base filled in from `rounds.base_ref` behind the predicate's back.
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
  - **Size parity** (`ABackfilledSize_IsTheLiveSize_FromTheRecordedBase`), on a temp git repository: record two code rounds
    through the LIVE path; clear their sizes in a copy; backfill → the `round_changes` rows and the four states equal the live
    ones, `size_origin = backfill`. Then merge the HEAD into the base branch (the branch shipped — `merge-base(base, sha)` is now
    `sha` itself, E2 §10 point 9) and backfill again from a cleared copy → still the live rows, never a zero whole. **Break-it**: hand `CollectAsync` the base branch NAME instead of `base_ref` → red after the merge, the whole
    scope's `From` and lines differ.
  - `ATwoTipRound_IsNotResized`: a `base_ref` that is not an ancestor of the head → no size written, `size: base not an
    ancestor` counted.
  - `APredecessorSizedInThisRun_GivesTheNextRoundItsDelta`: two pre-step-22 code rounds of one session → round 2's delta is
    `recorded` against round 1's head; with round 1 unsizable → round 2's delta `unavailable: base unknown`.
  - Sentinels absent from every rendering.
- **Done when.** The parity tests pass on every E1 fixture and on the temp repository; the open-time and base break-its
  observed red and restored.
- **Depends on.** E1 (fixtures, shipped), E2 (predecessor rule and `RoundSizer`, shipped), E3.S2/S4 (locator, reader), E4
  (`RoundAttribution`), E5 (live path, row shape).
- **Model.** scenarios + brief **opus** → code + tests **sonnet** → review **opus** → fixes **sonnet**.

### E7.S3 — The narrow write: authors and size of one stored round, nothing else

- **Goal.** Write one round's backfilled authors and size into BOTH places — the database projection and the session
  file's snapshot — without touching a single other byte of either, and so that running it again changes nothing.
- **Scope.** New `src_mcp/src/Server/Backfill/BackfillWriter.cs`; the database half inside the new
  `RoundsDb.Transaction()` seam (§3) calling the shipped `RoundChangesTable.Record` (`RoundChangesTable.cs:27-39`, public, on
  `RoundsDb.Command()`) and E5.S2's `RoundAuthorsTable.Record` (its own file, the same seam, E5 §5) — never retyped as a second
  `INSERT` ([reuse-first.md](../.agents/conventions/common/reuse-first.md) step 1); tests
  `src_mcp/tests/ABackfillLeavesEverythingElseWhereItWasTests.cs`.
- **Brief.**
  - **The session file FIRST, under the claim** (plan gate finding 3): `SessionClaim.TryTake(dataDir, repo_path,
    branch)` (`SessionClaim.cs:44-58`) → `SessionStore.Load` → replace ONLY the matching `(stage, number)` round's
    `Authors` / `Size` / `Gitlinks`, and only where S1 allowed → `SessionStore.Save` (`SessionStore.cs:501-513`) → load
    again and verify. When the claim is busy the round is `deferred` and NEITHER side is written. When the verify fails
    the database is not touched for that round.
  - **Then the projection, FROM the saved snapshot, in ONE transaction** (E2 §10 point 10): `using var tx =
    db.Transaction();` → `RoundChangesTable.Record(db, id, round.Size, round.Gitlinks)` when S1 said compute-size (its
    `UPDATE rounds SET size_state, delta_state, delta_from, size_origin, size_gitlinks` carries `size_origin = 'backfill'` from
    `Size.Origin`, parent §16 D12) → `RoundAuthorsTable.Record(db, id, round.Authors)` (E5.S2: the `authors_*`,
    `submodule_writes`, `uncovered_writes`, `ambiguous_*` columns with `authors_id_source = 'stored'`, then delete-then-insert of
    the round's `round_authors`) → `tx.Commit()`. Every value comes from the snapshot just verified, never from the in-memory
    result; a null half writes nothing (both writers' rule). Nothing else is written: no `RecordRound`, no `RecordSession`. A
    run killed after the save and before the commit leaves a snapshot without rows — or, with a fault between a delete and its
    insert, the round's earlier rows, never none — which S1's rule 3a re-projects on the next run without reading the
    transcript.
  - **The race §3 names** (`open` and the orphan sweep save without the claim): a verify that finds the snapshot gone
    leaves the database row as written; the next run's S1 rule 3 heals it. The summary counts both.
  - **E5's live re-record must not undo a backfill.** When the live projection re-records a round whose session copy
    has `Authors == null`, the database's authors columns and rows must stay (null means *not recorded*, never
    *recorded empty* — the `Caller` precedent, `SessionStore.cs:180-191`). E5.S2 now states it (*"a null snapshot never
    erases projection rows"*, E5 §3), as the shipped `RoundChangesTable.Record` already does for a null size
    (`RoundChangesTable.cs:29-32`). S3 keeps the cross-check test; if E5's write clears them, S3 changes that write in the
    minimum.
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
  - `ALiveReRecordWithoutTheSnapshot_KeepsTheBackfilledAuthors` (the E5 cross-check), and its size twin
    `ALiveReRecordWithoutASize_KeepsTheBackfilledSize`.
  - `TheProjectionIsOneTransaction`: a writer seam that throws after `RoundChangesTable.Record` deleted the round's rows and
    before `RoundAuthorsTable.Record` → the round's earlier `round_changes` rows and states are still there. **Break-it**: drop
    the transaction → red, the round has no size rows.
  - `ABackfilledSize_CarriesTheBackfillOrigin`: `rounds.size_origin = 'backfill'` and the session file's `Size.Origin` agree.
  - Sentinels absent from the database bytes and the session files.
- **Done when.** All pass; the four break-its observed red with the named symptom and restored green.
- **Depends on.** S1, E2.S3 (`RoundChangesTable`, `RoundRecord.Size`/`Gitlinks` — shipped), E5.S1 (`RoundRecord.Authors`), E5.S2
  (step 24, `RoundAuthorsTable`).
- **Model.** scenarios + brief **opus** → code + tests **sonnet** → review **opus** → fixes **sonnet**.

### E7.S4 — `coai-mcp --backfill-authors`: the mode, its backup, its summary, its scenario

- **Goal.** The one-shot mode, end to end through the real process, with the parent's acceptance as its scenario:
  twice over a store holding one `observed` row of a newer reader, the second run changes no totals and the newer row
  and every resolution survive.
- **Scope.** New `src_mcp/src/Server/Backfill/BackfillAuthorsMode.cs` in the shape of `RoundCountsMode`
  (`src_mcp/src/Server/RoundCountsMode.cs:39-88`); its `Startup` member (enum at `src_mcp/src/Program.cs:43`, beside
  `RoundCounts` at `:297`), its arm in `Classify` (`:323-339`), its dispatch case (shape of `:441-444`), its help paragraph
  after `--round-counts`'s (`:2457-2461`), and its entry in `Program.WriterModes` (`:310-314`, §3);
  `--backfill-authors` added to the one-shot list in `.agents/PROJECT.md:53-55` (enforced by
  `TheSanctionedModesAreTheOnesTheDocumentNames`, `src_mcp/tests/ARequestFaultIsNotAnOldBinaryTests.cs:50-69`);
  `research/module_server.md` — a section beside `--round-counts` (`:3350`) with a Mermaid flow;
  `research/module_tests.md` — its rows after the `--round-counts` section (`:3181`); tests
  `src_mcp/tests/BackfillAuthorsCliScenarioTests.cs` through `OneShotProcess.StartInfo` (`src_mcp/tests/OneShotProcess.cs:40-55`)
  with `COAI_AGENT_LOG_DIR` pointing at a temp copy of E1's fixtures (re-pointed by their `fixtureRoots`, instants shifted by
  one offset — E1's contract) and a temporary git repository for the size.
- **Brief.**
  - `coai-mcp --backfill-authors [--since <ISO-8601 UTC instant>] [--dry-run]` — each flag at most once, anything else
    is 65 with the usage sentence on stderr (`Program.TryParseInstant`, as `RoundCountsMode.cs:84-87`).
  - **Open**: `PresenceOf` confirmed absent → exit 0 with an empty summary, nothing created; present but not opening →
    74 with `Program.WhyUnreadable`'s sentence (`RoundCountsMode.cs:62-68`).
  - **Backup FIRST, verified, and the write gated on it** (not under `--dry-run`; the owner's decision of 2026-10-15: the
    backfill writes the LIVE stores on both sides — Windows `%LOCALAPPDATA%\coai-mcp`, WSL `~/.local/share/coai-mcp` — after
    a backup of each). Into a NEW folder `<dataDir>/backfill-backups/<yyyy-MM-ddTHH-mm-ssZ>/`, beside the store, named in the
    summary:
    1. the database by SQLite's ONLINE backup of `coai.db` into `coai.db` there (`SqliteConnection.BackupDatabase`). It
       carries what the `-wal` holds, consistently — a plain copy of `coai.db` + `-wal` + `-shm` while another server writes
       can catch them mid-checkpoint, which is a copy that opens and is wrong;
    2. every `sessions/session-*.json`, copied byte for byte;
    3. **the check**: the copy opens read-only, `PRAGMA integrity_check` answers `ok`, its `user_version` equals the
       source's, and the row count of EVERY table (`sqlite_master`'s list, not a hand-kept one) equals the source's counted
       BEFORE and AFTER the backup — equal before and after is what proves no server wrote in between; every copied session
       file's SHA-256 equals its source's, and the file counts agree;
    4. **the gate**: any check that fails → nothing is written, the summary names the check, exit **74**; the folder stays.
    The mode never deletes a backup, in any case and on either side (nothing is deleted in WSL). The online backup is new to
    this repository — the published Native-AOT binary is exercised once in O1 (the dry run does not touch it; O2's first run
    does), per [testing.md](../.agents/conventions/common/testing.md) § *Starting is not working*.
  - **Run**: S1 selects, S2 rebuilds, S3 writes — per gate session in round order (a predecessor's size is saved before its
    successor's delta is measured, S2), the reader reading each caller session ONCE per run and caching the result.
  - **Summary JSON on stdout**, text-free: `readerVersion`, `since`, `dryRun`, `backupDir`, `backupCheck: {opened,
    integrity, tables, rowsMatch, sessionFiles, filesMatch}`, `selected`, `written`, `healed`, `kept: {equalOrNewerReader,
    strongerExisting}`, `deferred`, `unrecoverable: {<reason>: n}`, `size: {written, kept, unrecoverable: {<reason>: n},
    delta: {<ScopeState>: n}}`, `reprojected`, `elapsedMs`. `backupDir` is the one path printed — the operator's own folder,
    and never committed (O4).
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
  - `TheBackupHoldsTheStoreAsItWas`: the backup opens, `integrity_check` is `ok`, every table's row count and every session
    file's SHA-256 equal the pre-run source's — over a store in WAL mode with uncheckpointed rows (they must be in the copy).
  - `ABackupThatDoesNotVerify_WritesNothing`: a seam that changes one source row between the before and after counts (a
    concurrent writer) → exit 74, the summary names `rowsMatch`, `coai.db`'s SHA-256 and every session file unchanged by the
    mode, the backup folder kept. **Break-it**: write before the check → red, the store changed.
  - `TheModeNeverDeletes`: two runs leave two backup folders; no file the mode did not create is missing afterwards.
  - Exit codes, one test each: 0 with no database and no data directory created, and 0 when every round is already
    current (nothing to do); 65 for a bad, a repeated and an unknown flag; 74 for a `coai.db` that is a directory, for
    a session file that cannot be written, for a backup that does not verify, and for a NAMED `COAI_DATA_DIR` that is missing
    (the `WriterModes` refusal); 75 when the test holds one session's claim during the run (the summary printed,
    `deferred = 1`); and a sweep asserting no path through the mode returns 64. `EveryModeIsClassifiedForAMissingNamedRootTests`
    green with the mode in `WriterModes`.
  - `--since`: a round started before it is untouched.
  - Sentinels absent from stdout, stderr, the database bytes and the session files.
- **Done when.** The scenario passes; the three break-its observed; no write precedes a verified backup; `--backfill-authors`
  is in `.agents/PROJECT.md`'s one-shot list IN THE SAME COMMIT that wires it into `Program.cs`, and `ARequestFaultIsNotAnOldBinaryTests` is green; each exit
  code 0 / 65 / 74 / 75 has its test and 64 is never returned; `research/module_server.md` and `module_tests.md` carry it; the whole
  `CoaiMcp.Tests.exe` suite and `node .agents/conventions/tools/plan-lifecycle.mjs` green.
- **Depends on.** S1, S2, S3; E6.S4 (the report's JSON of sums, used as the totals oracle).
- **Model.** scenarios + brief **opus** → code + tests **sonnet** → review **opus** → fixes **sonnet**.

## 6. Operation and hand-check

The outputs of O1–O4 — summaries, `--log` and `--round-authors` JSON, the judge's sheet — are working files in a new
folder per step (the scratchpad on Windows, a new folder under `~/coai-backfill/<utc>/` in WSL). Only the `RESULTS_`
file and what it cites are committed.

### E7.O1 — Before anything is written: the release, the running servers, the predictions, the sample rule, the dry runs

- **The release order — the backfill binary ships first** (parent §16 D16). E5 (step 24 and the live writer) and E6 (the
  report, S4's oracle) merge and release before E7; E7's code (S1–S4) merges on its own branch and is RELEASED through the
  normal flow — release-please cuts `mcp-v<x>` and `release.yml` publishes every RID, `linux-x64` and `win-x64` among them
  (`.github/workflows/release.yml:192-214`). Only then do O1–O5 run, and the run, the RESULTS file and the promotion land as a
  SECOND, docs-and-data commit. **Deviation from D16, recorded:** D16's "WSL needs its own build" applied to running an
  unreleased build; with release first, the WSL side installs the released `linux-x64` asset and no build happens in WSL. The
  version is the first `mcp-v*` tag that contains E7's merge commit (`git merge-base --is-ancestor <merge> mcp-v<x>`);
  record it, and `coai-mcp --version` per side.
- **Every running server on the side is on that release** before its first write — an older one strips `authors` from any
  session file it saves (§3). Listed by executable path and version (`Get-Process` / `ps` with the path, read-only), never
  stopped by image name; the operator reloads the VS Code windows (or the WSL remote) that still run an older one. Recorded per
  side: the count of servers seen and that each runs `<x>`.
- **Predictions first** ([measurement.md](../.agents/conventions/common/measurement.md) § 4), written into the
  draft `research/RESULTS_who_wrote_the_code_under_review.md` BEFORE any run: per side, the rounds `--since
  2026-09-30` the backfill will select; the share it will find `observed` or `partial` — E1 OBSERVED (not predicted) a
  transcript for 399 of 405 Windows code rounds (the 6 without are codex callers, `unobserved: no reader for codex`) and
  516 of 516 in WSL (`research/RESULTS_caller_transcripts_on_both_sides.md` Q5), so the prediction is about what the
  reader makes of them, written against those counts; the counts per unrecoverable reason, `size: base not an ancestor`
  included; how many rounds end `partial: read budget` under the 60 s / 64 MiB ceiling (the largest session is 650 MB,
  RESULTS Q6); the share of CORRECTIVE code rounds whose delta will end `unavailable`, per reason (parent §16 D26); the run
  time.
- **The sample rule**, written before the backfill runs, so the judge cannot be handed a convenient ten: per side, ten
  CODE rounds with a session-id caller, drawn in ascending SHA-256 of `round_key` (`rounds.round_key`, the round's own
  id, `src_mcp/src/Store/Schema.cs:614-616`; rounds older than step 21 by `session_id/number`) subject to: ≥ 3 first
  rounds and ≥ 3 corrective; ≥ 2 with a Sonnet `wrote here` coder and ≥ 1 `main only`; ≥ 1 from each side of
  2026-10-13; ≥ 1 `partial` or with ambiguous totals and ≥ 1 `unobserved`, where any exists; no branch more than three
  times. A stratum the store cannot fill is recorded as unfillable, not padded.
- **Dry runs**, both sides (`--dry-run`), the WSL one from a script file; the two summaries recorded against the
  predictions.
- **Pass.** The release is recorded; every running server of each side is on it; both dry runs exit 0; every number is
  beside its prediction; any prediction missed by more than its own stated margin is explained in writing before O2 — not by
  adjusting the prediction.
- **Model.** main session (**opus**), attended.

### E7.O2 — Backfill Windows, twice

- With no round running on the side (the backup's before/after counts must agree, S4), run `coai-mcp --backfill-authors
  --since 2026-09-30T00:00:00Z` against the LIVE store; record the summary, its `backupCheck` and the backup folder — a
  failed check has written nothing (exit 74) and is re-run, never bypassed. Run `--round-authors` (JSON) and keep it. Run the
  backfill again (its own backup first); record its summary; run `--round-authors` again and compare the two JSON documents
  by SHA-256. Re-run while `deferred > 0`, recording each.
- **Pass.** Every writing run's `backupCheck` passed BEFORE it wrote; the second run reports `written = 0` and `healed = 0`
  (or only heals it can name); the two report JSONs are byte-identical; each backup folder opens read-only with the counts
  its summary printed. No backup is removed by the session (the operator retires them after the RESULTS commit, §7).
- **Model.** main session (**opus**), attended.

### E7.O3 — Backfill WSL, twice, through script files

- O2's sequence, with every command in a script file written on the Windows side and run as
  `wsl.exe -e bash /mnt/c/<scratchpad>/backfill-wsl-<n>.sh`; each script writes its outputs to a NEW folder
  `~/coai-backfill/<utc>/` (created with `mkdir -p` of a fresh timestamped name) and deletes nothing — no `rm`, no
  cleanup, no `mv` over an existing file. The outputs are read from Windows through `\\wsl.localhost\<distro>\…`,
  read-only. The WSL store is backed up by the mode itself, inside WSL, into its own `backfill-backups/` — never copied
  across the drvfs boundary, and nothing there is ever deleted by an agent. No value is passed through `wsl.exe`'s command
  line (it re-parses after `--`): the script holds every argument. The WSL `--round-authors` JSON of the second run is the
  file O4 passes to `--add`.
- **Pass.** As O2.
- **Model.** main session (**opus**), attended.

### E7.J1 — Hand-check ten rounds per side

- **Who.** A fresh **Opus** judge launched with `model: opus`, which wrote and reviewed none of E3–E7's code — or the
  operator. Never the main session that briefed the code, never a coder.
- **Input.** The sample rule and the twenty round ids it drew (O1); `coai-mcp --log` output per side for those rounds
  (the state E5 prints, the size E2 prints — the WSL one produced by a script file) PLUS the per-round row-level authors dump — the
  persisted `RoundRecord.Authors` of the session file / the `round_authors` rows, exported per round id (a read-only SQL or JSON
  dump the operator runs; `--log` carries no per-agent model, effort, association, launches, tokens or write evidence, which
  checklist items 2-4 need); read access to the transcripts
  and the repositories. Not the reader's source: the judge re-derives, it does not re-run the product.
- **Per round, the checklist** — each field checked against the transcript or the repository directly:
  1. `authors_state` and its reason are true (an `unobserved: transcript not found` really has no
     `<projects>/*/<id>.jsonl`; a `partial` names a real gap).
  2. Per agent: requested and observed model, effort (or `unknown`), association and the write that earned it, window,
     launches; the main session's row.
  3. Tokens for ONE agent per round, summed once per `message.id` with the judge's own counting helper — written by
     the judge, importing no product code, kept under `scripts/` and named in the RESULTS file by path and sha.
  4. Ambiguous totals: the agents counted there truly have no write evidence.
  5. Size: `git diff --numstat -z <rounds.base_ref>..<head>` by hand — the RECORDED base, never a merge-base taken today —
     against `whole` per coverage and kind; the `delta` where one is recorded, and for an `unavailable` delta that its word is
     true (e.g. `base moved`: the predecessor's `Whole.From` differs from this round's `base_ref`).
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
  3. **The predictions against the observations** (O1–O3), including **the share of corrective code rounds whose delta
     ended `unavailable`, per reason, per side** (parent §16 D26) — read from E6's per-reason `delta unavailable` counts in
     the JSON, never recomputed — beside the backfill summaries' `size.delta` counts, which must agree.
  4. **The tables**, each cell with its rounds, gate sessions, branch count, largest-branch share and marks; columns for
     reviewer set, effort (`not recorded` apart from `none`, E6 D7), cohort and branch share (parent §11 step 7); the
     corrective tables with their per-100-delta figures and delta coverage (E6 D10); `Unknown` printed as unknown.
  5. **The naive reading, re-derived beside it** — the parent §1's date split (Windows 4.2 → 7.6, WSL 4.8 → 3.9
     findings per round) — and what the attributed table says about it.
  6. **What it does not settle**: task mix; edits through `Bash` (no evidence, parent §13); lines that survive (§3);
     a Team server's requested ≠ ran; rounds lost before 2026-09-25; every unrecoverable count.
- **Public repository.** Rounds named by `round_key` prefix, repositories outside this family and their branches
  anonymised (`repo-A`, `branch-1`) — on the WSL side that is every caller session E1 measured (RESULTS, *What E1 leaves
  open*: none of them may be published); `wsl.json` is committed only if it holds no branch name and no path, else only
  the table; no backup path. The private-data scan runs before the push.
- **Pass.** The file exists in that order; every figure in it traces to a mode's output kept in the working folder.
- **Model.** main session (**opus**), attended.

### E7.O5 — Docs and promotion

- `research/module_server.md`: the backfill (S4 wrote its section) and whatever of the reader, the attribution and the
  two tables E3–E6 did not already describe; `research/module_extension.md`: the card line, if E5 did not;
  `.agents/PROJECT.md` lists `--round-authors` and `--backfill-authors`.
- **The closing `/promote-plan` of the WHOLE parent plan**, TOGETHER with its seven `_E<n>.md` stories documents
  ([planning-docs.md](../.agents/conventions/common/planning-docs.md) § *A plan split into epics and stories*), in that
  rule's order — every status line, link and README row written while the eight files are still in `todo/`, the `git mv`
  LAST and committed in the same breath, each destination `git add`ed again after the move (git-workflow rule 8):
  - each document `IMPLEMENTED <date>` with ITS deviations — this document's §8–§10 included (D16's WSL build superseded by
    the released `linux-x64` asset; the `Transaction()` seam; `size: base not an ancestor`);
  - an unbuilt tail is EXTRACTED into a fresh `todo/` plan rather than holding the record hostage: whatever is still open
    then — E1.S4's attended `/clear` probe if it never ran (its README row says *open* today), feature and merge-time rounds
    (parent §7), readers for codex and gemini callers (parent §3), a `JsonExtensionData` guard against older servers (§3) if
    nobody built it;
  - all three link cases (planning-docs § *The three link cases*), including each mover's links to plans that stay in
    `todo/`; every inbound citation of `todo/PLAN_a_round_records_who_wrote_the_code_and_how_much*.md` outside the two folders
    rewritten to `research/` — on `main` today `Schema.cs`, `RoundChangesLog.cs`, `RoundSizer.cs`, four test classes
    (`ACallerTranscriptFixtureCarriesNoTextTests`, `ACodeRoundRecordsItsSizeTests`, `TheLogNamesEachRoundsSizeTests`,
    `TheRoundsSizeIsStepTwentyTwoTests`), five `scripts/` files and one extension test — found again with
    `git grep -l PLAN_a_round_records_who_wrote` at promotion time, since E3–E7 add more;
  - one `research/README.md` mention per promoted document and the eight `todo/README.md` rows removed in the same commit.
- **Pass.** `node .agents/conventions/tools/plan-lifecycle.mjs` and `pin-check.mjs` clean; `CoaiMcp.Tests.exe` and
  `npm test` in `src_vs_code` green.
- **Model.** main session (**opus**), attended.

## 7. Growth

| Surface | Projected size | Who retires it | Interrupted |
|---|---|---|---|
| `<dataDir>/backfill-backups/<utc>/` | one online-backup copy of `coai.db` (72 MB on Windows, parent §14) plus the session files (28 MB on Windows, parent §14) per WRITING run, a refused one included (its folder stays); two runs per side plus one per J1 fix → ~150–360 MB on Windows, the WSL store's size × the same on WSL | the operator, by hand, after the RESULTS file is committed; the mode never deletes, and in WSL nothing is deleted by an agent | a crashed run leaves its folder, named by its start; the next run makes its own; a run refused by the backup check writes nothing |
| Rows written by the backfill | none beyond the parent's §14 (`round_authors`, `round_changes`) — the backfill fills rows the live path would have written | with their round | per round: the session file first (the snapshot), then the database projection from it, under the claim — S3's order; a crash between leaves a snapshot without its projection, which the next run re-projects without opening the transcript (S1 rule 3a) |
| Working folders of O1–O4 | summaries and JSON, < 1 MB per side | the operator | — |

## 8. Points raised to the parent (not settled here)

> **Settled 2026-10-14** in the parent's §16: 1 → D16 (release first, then a second docs-and-data commit; WSL built in
> WSL — superseded 2026-10-16 by the released `linux-x64` asset, §6 O1); 6 and 8 → D14; 7 → D12 (`size_origin = backfill`,
> shipped in step 22); 2–5, 9 and 10 accepted as resolved here (the README row now exists). The line numbers below are as
> written on 2026-10-14; §3 carries the 2026-10-16 ones.

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
- [ ] The code was RELEASED before the operation, and every running server of each side was on that release before its
      first write; the run and the RESULTS landed as a second commit.
- [ ] Every writing run backed up its live store FIRST and wrote only after the copy opened, passed `integrity_check` and
      matched the source's row counts and session-file hashes; nothing was deleted, on either side.
- [ ] A backfilled size takes `Whole.From` from `rounds.base_ref` and carries `size_origin = backfill`; the two row writers
      ran inside one transaction.
- [ ] The RESULTS file reports the share of corrective rounds whose delta ended `unavailable`, per reason, per side.
- [ ] `.agents/PROJECT.md`, `research/module_server.md`, `module_extension.md`, `module_tests.md` updated; the parent
      and its seven stories documents promoted together by `/promote-plan`, any open tail extracted into a fresh `todo/`
      plan; `plan-lifecycle.mjs` clean.

## 9. Revised 2026-10-16 against `main`

Verified at `2d98f2f8`. Shipped since 2026-10-14: E1 (#828), E2 (#829, mcp 0.56.0), the plan gate's step 23 (#837/#841, mcp
0.58.0; extension 0.78.0, both published 2026-10-16). E3/E4/E5 are cited as of `e3a94e0c` / `36560698` / `fe4f878f`, E6 as
revised on its own branch the same day. Every `path:line` was re-read on `main`.

- **§2 rewritten from the shipped code**: `RoundSizer`, `DeltaPredecessor`, `RoundChangesTable`, `RoundRecord.Size`/`Gitlinks`,
  the fixtures `c01`–`c20` and E1's RESULTS numbers; the planned names are E3's/E4's/E5's own (`LocateStored`,
  `ReaderVersion = 1`, `AuthorsSnapshot`, `RoundAuthorsTable`, `RoundAuthorsStep.For`).
- **Release order made explicit (O1).** E5 and E6 before E7; E7 released through release-please; the operation and RESULTS a
  second commit. The WSL side takes the released `linux-x64` asset (`release.yml:192-214`) — D16's in-WSL build is no longer
  needed, recorded as a deviation. New gate: every running server of the side on that release, because an older one strips
  `authors` on its next save (§3, no `JsonExtensionData`).
- **The owner's backup decision (2026-10-15) written into S4/O2/O3**: the live stores are written; the online backup and the
  session files go to a new dated folder beside the store; the copy must open, pass `integrity_check`, match every table's
  row count before AND after the backup and every session file's hash; the write is gated on it (exit 74 otherwise); the
  mode never deletes; nothing in WSL is deleted, and no value goes through `wsl.exe`'s command line. Two tests and a break-it.
- **`Whole.From` from `rounds.base_ref`** (E2 §10 point 9): S2 hands `CollectAsync` the recorded base and keeps the size only
  when `ComparedAgainst` equals it; a non-ancestor base is `size: base not an ancestor`, never re-based. The size is assembled
  by the shipped `RoundSizer.MeasureAsync`, with `Origin = backfill`.
- **`RoundChangesTable.Record` inside a transaction** (E2 §10 point 10): `RoundsDb` gains a one-line `Transaction()` seam;
  both writers run in it; a test and a break-it.
- **Order of a run**: per gate session in round order (the delta predicate reads each predecessor's `Size.Whole.From`), one
  cached read per caller — replacing "ordered by (caller, started)".
- **Smaller corrections**: `SessionStore.Load` is null for a busy file too, so `Exists` decides `session file gone`; skipped
  rounds get neither size nor authors; a live size is never replaced; `--backfill-authors` joins `Program.WriterModes`;
  exit 74 also for a refused backup; J1 checks the size against the RECORDED base; O4 carries D26's share and the WSL
  anonymisation E1 found necessary; O5 spells out the promotion of all eight documents and the extraction of the open tail.
- **Drifted anchors updated:** `RoundsDb.cs` `:846-867` → `RecordRoundRow` `:842-926`, `:925` → `:930`, `:960` → `:968`,
  `:837` → `:838`, `:831-832` → `:833`, `:173-181` → `:172-181`; `PanelService.cs` `:492-506` → `:497-511`, `:1524` → `:1590`;
  `SessionStore.cs` `:248` → `:278`, `:375-405,463-468,502` → `:405-431,495,532`, `:431-448` → `:461-478`, `:471-484` →
  `:501-513`, `:586-600` → `:608-632`; `RoundEngine.cs:895-900` → `:898-907`, `:897` → `:902`, `:614-615` → `:612-620`;
  `PanelSettings.cs:386,972` → `:421,1009`; `SqliteMigrator.cs:80-87` → `:80-90`; `RoundCountsMode.cs:30-88` → `:39-88`;
  `ARequestFaultIsNotAnOldBinaryTests.cs:50-90` → `:50-69`; `OneShotProcess.cs:32-53` → `:40-55`; `module_server.md:3177` →
  `:3350`.

## 10. Open with E3–E5

What this document assumes of contracts still being amended; each is the named epic's to confirm, or E7's to follow when it
ships. None blocks S1's table tests.

1. **E5 — no `JsonExtensionData`.** E5 does not say what an older server does to a session file carrying `authors` (it
   drops it). E7 gates its operation on the running servers' versions (O1) and heals on the next run; E5's live path has the
   same exposure in every window not yet updated and may want to state it, or add the guard.
2. **E5 — the writer's signature.** S3 calls `RoundAuthorsTable.Record(db, roundId, authors)` inside its own transaction; E5.S2
   calls it from `RecordRound`'s transaction (`RoundsDb.cs:196-201`). If E5 makes it open its own transaction, S3 cannot nest
   it — the writer must stay transaction-free, as `RoundChangesTable.Record` is.
3. **E5 — `authors_reader`.** E5.S2 makes it INTEGER (default 0) holding `ObservedSession.ReaderVersion`; S1 compares numbers.
   A reader version that also versions E4's attribution (an E4 rule change with no E3 change) is not covered by E3's
   constant alone — E3's `ReaderVersion` is bumped only by a COUNTING rule (E3 §3). E4 or E5 to say which constant a change to
   the attribution bumps, or S1's "equal reader → keep" would keep a row attributed by an older rule.
4. **E3 — `LocateStored` and the registry.** `stored` has no registry rung; a round whose live server read the registry and a
   backfill that reads `rounds.caller` must name the same session. E1 measured the registry equal to the variable in every
   probe (RESULTS Q4), but `/clear` is still open there — a cleared session's rounds may carry a stale id.
5. **E4 — peers on every round.** E5.S4 builds peers from their session files; S2 must hand E4 the same peers for parity,
   including peers whose rounds the backfill has not yet given authors (their `authors_session` empty → `rounds.caller` when
   it is a UUID, E5 §3). If E4.S2's completion rank changes, the parity test is where it shows.
6. **E4 — reasons containing `", "`.** S1's strength parse reads the kind before `: `; it does not split reasons. E6 D18 does
   (E6 §4 item 3); the two documents share that open point.
7. **E5 — the 60 s ceiling.** E5.S4 measures the C# read time before fixing the live 2 s; S2's 60 s per caller is this
   document's number, not a measurement. O1's prediction of `partial: read budget` counts is what tests it.
