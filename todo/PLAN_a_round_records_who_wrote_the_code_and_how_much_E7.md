# PLAN — E7 stories: backfill the authors, hand-check twenty rounds, then answer

> Status: **plan only, nothing implemented yet, 2026-10-14; revised 2026-10-16 against `main` at `2d98f2f8` (§9) and 2026-10-18 against `main` at `e53e35ba` (§9a — the corrected `BackfillDecision`, the safety rules, the consultants' answers).** Epic E7 of [PLAN_a_round_records_who_wrote_the_code_and_how_much.md](PLAN_a_round_records_who_wrote_the_code_and_how_much.md).
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

**The no-overwrite rule and its TWO exceptions** (parent §9; E4 §S2.2b G3; E5 §10 item 11). A row written by a reader AND an
attribution at least as new as the current ones is never recomputed — **except** (1) a **provisional** row: its reasons
include `peer running`, or its peer set has grown since (E4 G3), recomputed once every overlapping round of every named peer
is FINAL (`done` or `interrupted`, E4 §S2.3); and (2) a snapshot **lost from the session file** (an older server stripped
it) while the caller's transcript still exists, recomputed so the file gets a real snapshot back. Both are S1's rules 3
and 4. Everything else the backfill does to a row that already has a state is `keep`, `re-project` or `heal`.

**The owner's safety rules** (2026-10-15/16; §3 *Safety*, each with its test): a **verified backup** of the live store
before any write, the write gated on it; a **dry run** first; **no live repair** — E7 is the only writer of another gate
session's authors rows, offline and under that session's claim; **re-runs change nothing**; **never a weaker row over a
stronger one**. Nothing is ever deleted, on either side.

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

E1 and E2 are SHIPPED (E1 #828 and #851; E2 #829, released in mcp 0.56.0); the plan-gate's step 23 (#837/#841) is in mcp
0.58.0; `main` is at mcp 0.59.0 (`e53e35ba`). E3–E6 are planned; their stories documents are cited **as of** E3 `16628077`
(S1–S5 coded on its branch), E4 `30362785`, E5 `c1216b42` and E6 `131074b4`, and are **authoritative for the exact
signatures** — where a shipped one renames something, follow it and keep this document's intent (§10 lists what may still
move). The authors are a nullable
snapshot on `RoundRecord` (E5.S1's `AuthorsSnapshot`, in `core/Authors/`), beside the shipped `Size` and `Gitlinks`, all
following the `Caller` precedent (`src_mcp/src/Server/SessionStore.cs:173-223`).

| From | Piece | State on `main` | Used by |
|---|---|---|---|
| E2 (#829) | `RoundRecord.Size` / `Gitlinks` (`SessionStore.cs:202`, `:211`); `RoundSize`/`ScopeSize`/`SizeCell`, `ScopeState`, `SizeOrigin.Backfill` (`core/Context/RoundSize.cs:18-52`); the predecessor rule `DeltaPredecessor.Choose(earlier, against)` — the last `done`, non-skipped code round with a sha, held to this round's base through ITS `Size.Whole.From` (`src_mcp/src/Server/Rounds/DeltaPredecessor.cs:31-61`); the numstats `ContextAssembler.CollectAsync` / `AllChangesAsync` / `DeltaAsync` / `GitlinksAsync` (`runners/Context/ContextAssembler.cs:307,394,428,404`); the assembly `RoundSizer.MeasureAsync` (`src_mcp/src/Server/Rounds/RoundSizer.cs:32-56`); the writer `RoundChangesTable.Record(db, roundId, size, gitlinks)` (`src_mcp/src/Store/RoundChangesTable.cs:28-39`) — a null size writes NOTHING; schema step 22 (`Schema.cs:635-652`) | **shipped** | S1, S2, S3 |
| E3.S2 (`CallerTranscriptLocator.LocateStored(id, home)` — source `stored`; a `repo:…` value is `NoSessionId`; `Found` / `TranscriptNotFound` / `TranscriptAmbiguous` / `StatedId`; `ClaudeHome.With(agentLogDir)`), E3.S4 (`CallerTranscript.Read(where, ReadBudget, fromUtc)` → `ObservedSession`; `ObservedSession.ReaderVersion = 1`, bumped by any COUNTING rule change; the four states, the closed `ObservationReasons`; `ObservedWrite.AnsweredUtc`, S4-22; per-agent `ObservedAgent.TranscriptPresent`, which is NOT this document's `TranscriptLocated`) | the reader, its version (`authors_reader`), its reasons, the locator's outcome | planned, coded on its branch (E3 as of `16628077`, §E3.S2 brief 5–6, §3) | S1, S2 |
| E4.S1–S3 (`AttributionInput` with `PeersKnown`, `RoundAttribution.ReadFrom` / `Assign`, `RoundAttribution.Version = 1`, `RoundAuthors` with `PeerGates` (ids only) and `Windows`; *settled* = `done` ∧ recorded, *final* = `done` or `interrupted`, E4 §S2.3; the reasons `no open time`, `peer running`, `prior caller unknown` …), **E4.S5's `AttributionMapping`** (`src_mcp/src/Store/AttributionMapping.cs`: `Observation`, `Timeline`, `Universe` — the ONE `ObservedSession` → `AttributionInput` mapping, B1) | attribution of ONE round — pure; `ReadFrom` gives the `fromUtc` bound; the mapping S2 calls instead of writing one | planned (E4 as of `30362785`, §S2.3, §S2.5, S5) | S1, S2 |
| E5.S1 (`AuthorsSnapshot`, `IsWellFormed`, `Render`, `AuthorsSnapshot.Unknown(…)` — `-1` and `0` versions where a component did not run), E5.S2 (step 24: `authors_state`, `authors_session`, `authors_id_source` admitting `registry`/`environment`/`stored`/`healed`, `authors_reader`, `authors_attribution`, the `ambiguous_*` and two write counts; `RoundAuthorsTable.Record(db, roundId, authors)` — joins the caller's transaction, a null snapshot writes nothing; **`RoundAuthorsTable.AgentKeyOf`**, the one inverse of `AgentColumn`), E5.S4 (`RoundAuthorsStep.For` — the live call; **`AuthorPeers.Find(connection, load, deadline)`**, `src_mcp/src/Server/Rounds/AuthorPeers.cs`, the one peer build, `deadline: null` for E7; the earlier-caller order snapshot → `authors_session` → UUID `caller` → `""`) | the snapshot, the step, the authors writer, the inverse the heal uses, the peers, the live path a backfill must equal | planned (E5 as of `c1216b42`, §3, §5, §10 items 11 and 14) | S1, S2, S3 |
| E6.S4, E6.S5 | `coai-mcp --round-authors` with `--add`, `--table`, cohorts, the JSON of sums, the per-reason `delta unavailable` counts (E6 D10), and per cell the **`provisional`** rounds (D3) and the **`size coverage unknown`** rounds (D12) — the two counts that show how many rows E7's repair changes | planned (E6 as of `131074b4`) | S4 (the "no totals changed" oracle), O2–O4 |
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
  `RoundChangesLog` reads `Whole.From` from it (`src_mcp/src/Store/RoundChangesLog.cs:53`, `:113-114`). Recomputing `merge-base(main, sha)` after the
  branch merged gives `sha` itself and a zero whole. `CollectAsync` resolves its own comparison base from what it is handed
  (`ContextAssembler.cs:307-321`); handed `base_ref`, an ancestor of `sha`, it resolves to `base_ref` — so S2 hands it
  `base_ref` and checks `ComparedAgainst == base_ref` before any size is kept (S1 rule 8, S2).
- **The open time comes from the session file, never from the database.** `sessions.opened_utc` is the clock of the
  FIRST PROJECTION — bound to `DateTime.UtcNow` when the first round is recorded (`RoundsDb.cs:838`) and never updated
  (the upsert sets only `repo_path`/`branch`, `:833`) — so it is later than `open` by at least one whole round. `open`
  stamps the real one on the session file (`src_mcp/src/Server/PanelService.cs:497-505`, `PersistedSession.OpenedUtc`,
  `SessionStore.cs:278`). A session file whose `OpenedUtc` is the default leaves the first window unbounded, which the live
  path already treats as "no window" (`src_mcp/src/Server/Rounds/RoundEngine.cs:898-907`).
- **Which rounds — E5 §3's one sentence, one owner** (M6). Authors are decided for a `PlanReview` or `CodeReview` round that
  is SETTLED — status `done`, verdict not `skipped` (E4 §S2.3). A skipped, interrupted (`RoundRecord.Interrupted`, the
  sweep's word, `SessionStore.cs:626`), running, feature or document round is **not selected**: no snapshot, an empty
  `authors_state`, no reason — live and backfilled alike. `Assign` is never handed such a round (E4 G6).
- **The caller's session id is `rounds.authors_session`, else `rounds.caller` when it is a UUID** (E5 §3's earlier-caller
  order). It is not on the session file — `RoundContext.Caller` (`src_mcp/src/Store/RoundContext.cs:40`) carries it straight
  to the projection (`RoundEngine.cs:612-620`), and a `repo:<path>` value (`RoundCommands.cs:34-35`) is `NoSessionId`. So the
  population a backfill walks is the DATABASE's rounds; the session file adds `OpenedUtc`, the rounds' order and the place
  the snapshot lives. **The id source**: a recompute keeps the LIVE `authors_id_source` (`registry`/`environment`) — the
  live server's choice cannot be re-asked after the fact; `stored` is written only where the row had none; `healed` is the
  `IdSource` of a snapshot S3 rebuilds from the database, and reaches the column only when such a snapshot is later
  re-projected (E5 §10 item 11 (c); E6 §0 reads `healed` like `stored`).
- **E7 is the only repairer of another gate session's rows, and only offline.** Uniqueness of STORED ownership across gate
  sessions is a property only a process that sees them all after they settled can hold (E4 G3); E5's live path stores
  `partial: peer running` as it is and never writes another session's rows. The backfill recomputes exactly those rows
  (S1 rule 3), under THAT session's claim (S3), and a second run then changes nothing.
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
  momentarily busy file is a fresh session"*). A gone file is therefore decided by `SessionStore.Exists`
  (`:450`), and a file that exists but does not load is `deferred: the session file did not read` — retried, never
  recorded on the round. A gone or replaced file is not a deferral: the round is computed from the database rows and
  written to the database only (S1 rule 5, S2, S3 — the consultants' answer Q5, §9a).
- **Opening the store — read-only until the backup verified.** `RoundsDb.PresenceOf` (`RoundsDb.cs:89`): a missing database
  is "nothing to backfill", and the data directory is never created (`:92-102`). The dry run, the pre-backup row counts and
  the selection read through a `Mode=ReadOnly;Pooling=False` connection (the `RoundCounts.Read` shape,
  `src_mcp/src/Store/RoundCounts.cs:100`), which cannot migrate; `RoundsDb.OpenExisting` (`:104`, `Mode=ReadWrite`, migrates)
  is opened ONLY after S4's backup check passed — so even E5's step-24 migration of a store an older server left behind
  happens after the backup. A store below step 24 is a side that has not run the release: the dry run says so and exits
  74; an older server meeting a file at a later step runs nothing (`src_mcp/storage/SqliteMigrator.cs:80-90`).
- **Safety — the owner's rules (2026-10-15/16), each a hard precondition with a test.**
  1. **Backup before any write, verified, the write gated on it** (S4; tests `TheBackupHoldsTheStoreAsItWas`,
     `ABackupThatDoesNotVerify_WritesNothing`, `TheFirstWriteComesAfterTheVerifiedBackup`). The copy is SQLite's online backup
     plus every session file; it is verified by opening, `integrity_check`, `user_version`, per-table row counts equal to the
     source's before AND after, `PRAGMA data_version` unchanged across the backup (a count is not proof — an UPDATE keeps it;
     `data_version` changes on any other connection's commit), and the session files' hashes. **Both live stores** — Windows
     `%LOCALAPPDATA%\coai-mcp` and WSL `~/.local/share/coai-mcp` — are each backed up by the run that writes them, before it
     writes; no run writes a store it did not back up in that same run; no backup is ever deleted.
  2. **A dry run first** (`--dry-run`: read-only, no backup folder, the same counts; O1 — a writing run in O2/O3 is refused by
     the checklist until the dry run of THE SAME binary over THE SAME store exited 0 the same day; tests `ADryRunWritesNothing`,
     `ADryRunNeverOpensTheStoreForWriting`). Not a marker file: a marker would be a write before the backup (consultants, §9a).
  3. **No live repair.** Only this mode writes another gate session's authors rows, offline, under that session's claim
     (above; E4 G3). The live path never does (E5 §3).
  4. **Re-runs change nothing** (S4 `BackfillTwice_TheSecondRunChangesNoTotals`; S3 `ASecondWrite_ChangesNoRow`,
     `AnEqualResult_WritesNothing`).
  5. **Never a weaker row over a stronger one** (S1 `AWeakerResult_NeverReplacesAStrongerOlderOne`; S3
     `AStrongerOlderObservation_Survives`), and never an equal-or-newer row recomputed outside the two exceptions (§1).
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
| E7.S1 | Which rounds, and what a backfill may do to each (pure) | code | ≈ 380 | E3.S4, E3.S2, E4.S2 (`PeerGates`, final), E5.S2 |
| E7.S2 | A stored round's history, rebuilt exactly as the live path sees it | code | ≈ 380 | E2.S1, E2.S2, E4.S2, E4.S3, E4.S5 (`AttributionMapping`), E5.S4 (`AuthorPeers.Find`), E1.S5 |
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

- **Goal.** One pure decider says, for each stored round, whether the backfill computes it, recomputes it, re-projects
  it, keeps it, heals the session file's snapshot from the database, or defers it — and with which reason. It is the
  whole of "never overwrite a newer or equal reader" **with its two exceptions — a provisional row (`peer running`, or a
  grown peer set) and a snapshot lost from the file while the transcript exists** — and of "never replace a stronger
  observation with a weaker one", in one place. The order is E5 §10 item 11's, corrected by the 2026-10-17 judge review
  (H1, H2, M2, M6, M7) and the 2026-10-18 consultants (§9a).
- **Scope.** New `src_mcp/src/Server/Backfill/BackfillDecision.cs` (no I/O); tests
  `src_mcp/tests/ABackfillDecidesEachRoundOnceTests.cs`.
- **Brief.**
  - **Inputs per round.** `StoredRound(RoundId, SessionId, Stage, Number, Status, Verdict, Caller, CallerVendor,
    AuthorsSession, StartedUtc, AuthorsState, AuthorsIdSource, AuthorsReader, AuthorsAttribution, SizeState, BaseRef,
    HeadSha)` from the database (the two versions are E5's INTEGERs; `0` = that component did not run, E5 H2);
    `SnapshotPresence(FileState, FileHasRound, HasAuthors, AuthorsState, Reader, Attribution, PeerGates, IdSource, HasSize)`
    from the session file — `FileState` is `present` | `gone` (`SessionStore.Exists` false) | `replaced` (its
    `State.SessionId` is not the round's) | `unreadable` (exists, `Load` null); **`TranscriptLocated`** —
    `CallerTranscriptLocator.LocateStored(authors_session, else a UUID rounds.caller, home)` answered `Found`, run BEFORE
    the decision (its failure is what `compute` records; the name is chosen apart from E3's per-agent
    `ObservedAgent.TranscriptPresent`, which is a different fact — a located file can yield no stamped line);
    **`PeerStatus(PeersKnown, PeerIds, ANamedPeerRoundStillRuns)`** from S2's one `AuthorPeers.Find` call — whether the
    lookup completed, the gate ids it found, and whether any round of a gate in `PeerGates` that overlaps this round's
    windows is still `running` (a round is FINAL when `done` or `interrupted`, E4 §S2.3 — an interrupted peer never
    becomes `done`, M7); the two version constants (`ObservedSession.ReaderVersion`, `RoundAttribution.Version`), read
    from the types — never literals; and `--since`.
  - **Strength** — `observed` 3 > `partial` 2 > `unobserved` 1 > empty 0 — one function over the kind before `: ` of a state
    `AuthorsSnapshot.IsWellFormed` accepts (E5.S1); a stored state it refuses counts as empty, so it is recomputed.
  - **Rules, first match** (authors; the size is rule 8, decided apart):
    0. **Selection.** `StartedUtc` before `--since` → not selected. Not a SETTLED `PlanReview`/`CodeReview` round (§3 *Which
       rounds*) → not selected, NO state.
    1. **Deferral.** `FileState = unreadable` → `deferred: the session file did not read`; the claim busy (S3) →
       `deferred: the session was busy`. Neither is recorded on the round; both are retried.
    2. **Transcript GONE** (`TranscriptLocated` false). (a) The file holds a snapshot: the database empty or EITHER version
       older → `re-project` (rows FROM the snapshot, the transcript not read — the completion of a run killed between its
       two writes); else `keep: transcript gone`. (b) The file holds none and the database state is non-empty →
       **`heal`** — the FILE gets a snapshot rebuilt from the database (S3: rows through `RoundAuthorsTable.AgentKeyOf`,
       never a second spelling of `"" → main`; state and reasons from `authors_state`, split on `": "` then `", "` — exact,
       E5 §10 item 4; `Session`, `Reader`, `Attribution`, the ambiguous totals and the two write counts from their
       columns; `Elsewhere`, `ClaimedByPeer` = `ActivityTotals.Unknown`, `UnobservedLaunches = -1`, `Windows = []`,
       `PeerGates = []`, **`IdSource = "healed"`**); with `FileState` gone/replaced there is no file to heal →
       `keep: no file`. (c) The database empty too → `compute`, which ends `unobserved: <the locator's reason>` — every
       selected round gets a state. **A `peer running` row without its transcript stays provisional for ever** — the
       stated limit.
    3. **Transcript PRESENT — `recompute: provisional`** when the state's reasons include `peer running` and
       `ANamedPeerRoundStillRuns` is false; or when `PeersKnown`, `HasAuthors`, `IdSource ≠ healed` and `PeerIds ⊄
       PeerGates` (the peer set grew — a failed projection, E4 G3 (iii); a healed or absent snapshot has an UNKNOWN peer
       set, never an empty one, so it never triggers this — H1; a lookup that did not complete proves no growth). A named
       peer round still `running` → `keep: peer still running` (the backfill never sweeps: `SweepOrphanedRounds`,
       `SessionStore.cs:608`, run by a server at startup, `PanelService.cs:144`, is the one owner of `interrupted`).
    4. **`recompute: snapshot lost`** — `FileState = present`, `FileHasRound`, `HasAuthors` false (an older server stripped
       it), whatever the database's versions say. "Equal rows" is NOT promised (M2): at equal versions the placed ids,
       owners, associations and rows come back equal, but a straddling message's tokens may be higher (E4 decision 7),
       `partial: read budget` may become `observed` (2 s live, 60 s here), and the never-projected members come back at
       all.
    5. **`keep: equal or newer`** — (`HasAuthors`, or `FileState` gone/replaced: no file to hold one) ∧ the database state
       `observed` or `partial` (never `unobserved` — its versions are `0` where a component did not run, and a present
       transcript is worth a recompute even at current versions, H2) ∧ `authors_reader ≥ ReaderVersion` ∧
       `authors_attribution ≥ RoundAttribution.Version`.
    6. **`re-project`** — the file's snapshot at both versions ≥ current, the database empty or older → rows FROM the
       snapshot, the transcript not read.
    7. **`compute`** — empty, older, or `unobserved`.
    **After any `compute`/`recompute`:** written when its strength ≥ the database's; a WEAKER result → `keep: stronger
    existing`, and when the file had no snapshot the file is healed from the database instead of being left empty (M2). A
    result EQUAL to the stored snapshot, member for member, versions included → `kept: unchanged`, nothing written (S3) —
    this is what makes an `unobserved` row at current versions, which rule 5 never keeps, idempotent in `written`.
    8. **Size**, decided apart: stage `code`, settled; `size_state` non-empty → keep (a live size is never replaced, and
       a backfill never re-measures its own); empty with both `BaseRef` and `HeadSha` → compute; else `size: no base
       recorded` / `size: no head recorded`.
    A second run then writes nothing: every path above ends in a row the same path reads as `keep` or `unchanged`.
  - **Every selected round gets a state**: a caller that is not a session id, or a vendor with no reader, is computed as
    `unobserved: stated id` (parent §16 D9) / `unobserved: no session id` (`repo:<path>`, blank) / `unobserved: transcript
    not found` / `unobserved: transcript in two folders` / `unobserved: no reader for <vendor>` through E3.S2's `LocateStored`
    — not skipped. A round whose file is gone gets E4's own `unobserved: no open time` for round 1 (S2) — this document
    adds **no** open-time reason of its own.
  - **The reasons are a closed set** with fixed sentences — E3's `ObservationReasons` and E4's `AttributionReasons` passed
    through, plus the backfill's own for the SIZE: `repository not found`, `commit not found: base|head`, `size: base not
    an ancestor` (the recorded `base_ref` is not an ancestor of the head — the live round's two-tip fallback, or a rewritten
    history — so no size is reconstructed rather than a new base guessed, §3). Everything else above — `transcript gone`,
    `no file`, `peer still running`, `stronger existing`, `unchanged`, `deferred: …` — is a COUNT in the summary, never a
    state on the round. No reason carries a path or free text.
- **Tests (RED first, against a stub that answers `compute` for everything).**
  - A table over **every** cell of (`FileState` × `HasAuthors` × database state × database versions older / equal / newer
    per version × snapshot versions × `TranscriptLocated` × `ANamedPeerRoundStillRuns` × `PeerIds ⊆ PeerGates` ×
    `IdSource` healed / live × computed strength), generated from E5's state enum, the two version constants and the
    `FileState` enum rather than listed by hand ([testing.md](../.agents/conventions/common/testing.md) § *A test that
    repeats a list*); every cell's expected verb is a function of the rules above, written once.
  - `AnEqualOrNewerRow_IsKept_OnlyWithItsSnapshotAndAnObservedOrPartialState`; `AnOlderAttributionAlone_IsRecomputed`
    (reader equal, attribution older); `AWeakerResult_NeverReplacesAStrongerOlderOne`;
    `AnOlderRow_IsReplacedOnlyByAnAtLeastAsStrongResult`; `AFilledRowWithoutItsSnapshot_IsHealed_WhenTheTranscriptIsGone`;
    `AFilledRowWithoutItsSnapshot_IsRecomputed_WhileTheTranscriptExists`;
    `APeerRunningRow_IsRecomputed_OnceEveryNamedPeerRoundIsFinal` (a `done` and an `interrupted` peer both release it);
    `APeerRunningRow_IsKept_WhileANamedPeerRoundStillRuns`; `AGrownPeerSet_IsRecomputed_AndAHealedSnapshotNeverIs`;
    `AnIncompletePeerLookup_ProvesNoGrowth`; `AnUnobservedRowAtCurrentVersions_IsNotKept`; `AWeakerRecompute_HealsTheFileInstead`;
    `ARoundWithoutItsFile_IsComputedFromTheDatabase_AndKeptOnTheNextRun`; `ACodexCaller_GetsAStateNotASkip`;
    `APlanRound_GetsAuthorsAndNoSize`; `ASkippedOrInterruptedRound_IsNotSelected`; `ALiveSize_IsNeverReplaced`;
    `AnExistingButUnreadableSessionFile_IsDeferredNotGone`.
  - **Break-its.** Either version comparison made `>` instead of `≥` → `AnEqualOrNewerRow_…` red ("expected keep, found
    compute"); the strength guard removed → `AWeakerResult_…` red naming the downgraded state; the heal placed before the
    transcript check (the H1 order) → `AFilledRowWithoutItsSnapshot_IsRecomputed_WhileTheTranscriptExists` red; a healed
    snapshot's `PeerGates = []` read as "no peers" → `AGrownPeerSet_…` red (the healed row re-triggers on every run).
- **Done when.** Every cell of the table passes; the four break-its observed red and restored green; no reason sentence
  contains a path.
- **Depends on.** E3.S4 (states, reader version), E3.S2 (`LocateStored`, its outcomes), E4.S2 (`PeerGates`, *final*,
  `RoundAttribution.Version`), E5.S1 (`IsWellFormed`, `Unknown`), E5.S2 (step 24's columns — array index 23, `user_version`
  24; steps 22 and 23 are shipped; `AgentKeyOf`).
- **Model.** scenarios + brief **opus** → code + tests **sonnet** → review **opus** → fixes **sonnet**.

### E7.S2 — A stored round's history, rebuilt exactly as the live path sees it

- **Goal.** For one stored round N, build exactly the inputs E4's `RoundAttribution` and E2's delta predicate receive
  on the live path — from what is on disk — so a backfilled row equals the row the live path would have written.
- **Scope.** New `src_mcp/src/Server/Backfill/StoredHistory.cs` (reads the database rows and the session file) and
  `src_mcp/src/Server/Backfill/CallerReads.cs` (one `ObservedSession` per caller session id, read once per run);
  tests `src_mcp/tests/ABackfilledRoundIsTheRoundTheLivePathWroteTests.cs`.
- **Brief.**
  - **The session file**: `SessionStore.Exists` then `SessionStore.Load(repo_path, branch)` (`SessionStore.cs:450`,
    `:461-478`) — code and plan rounds live in the branch's own session (no document, no feature). Its `State.SessionId`
    equal to the database's `session_id` → `present`; not equal → `replaced`; not there → `gone`; there but not loading →
    `unreadable`, deferred (§3). A `gone`/`replaced` file is not a stop: the round is built from the database rows and written
    to the database only (S3's one exception; the consultants' Q5, §9a) — it then survives in the database and its backups,
    never in a file, and its grown-peer trigger can never fire (no `PeerGates` is stored; unknown, not empty).
  - **The gate's own timeline is E4.S5's `AttributionMapping.Timeline(gateSessionId, repo_path, openedUtc, rounds,
    callerOf)`** (B1) — never a list shaped here. `rounds` is the session file's `[.. session.Rounds]` (round N is already
    in it; `completedUtc` only matters for `done` rounds, which the mapping enforces, E4 G1) when the file is present, else
    `RoundRecord`s rebuilt from the database rows (`stage`, `number`, `started_utc`, `completed_utc`, `status`, `verdict`,
    `sha`) — a round N > 1 is still computable without the file: its window starts at round N−1's START, which both hold.
    `openedUtc` is the file's `PersistedSession.OpenedUtc` (`:278`, the real `open`, §3) — without the file, or with a default
    value, it is passed as the default and the mapping yields null, so round 1 is E4's own `unobserved: no open time`.
    `callerOf` is E5 §3's earlier-caller order (snapshot → `authors_session` → UUID `caller` → `""`, the last `partial: prior
    caller unknown`), read from the database rows of that session.
  - **The peers — and with them `before-open` — come from ONE call, E5.S4's `AuthorPeers.Find(connection, load, deadline:
    null)`** (E5 §10 items 8 and 14): the same read-only lookup the live path runs, each peer built from its SESSION FILE
    through `AttributionMapping.Timeline`, so `running` rounds (which exist in files only — `RecordRound` is reached at
    completion and skip alone, `RoundEngine.cs:612`, `RoundSkips.cs:80`) are visible with their `running` status. E4 derives
    `before-open` from these timelines itself (`PriorEnd`, the settled prefix, G2); **this document's earlier database join
    on `caller`/`repo_path` is dropped** — one implementation, so parity follows from construction. `Find`'s result also
    fills S1's `PeerStatus`: `PeersKnown` is E4's input flag (false when the lookup did not complete); `PeerIds` the gate
    ids found; `ANamedPeerRoundStillRuns` from the `running` rounds of the timelines whose ids are in the row's `PeerGates`
    and whose windows overlap this round's. The `universe` is `AttributionMapping.Universe(gate, peers, observation)`.
  - **Transcripts**: `ClaudeHome.Default.With(PanelSettings.AgentLogDir)` (E3.S2 brief 1; `AgentLogDir` is
    `src_mcp/src/Server/PanelSettings.cs:421`, from `COAI_AGENT_LOG_DIR` at `:1009`) — the one resolution E5.S4 makes of
    today's ternary (`RoundEngine.cs:902`, `AgentLog.DefaultProjectsDir`, `src_mcp/src/Store/AgentLog.cs:39`). The location
    is `CallerTranscriptLocator.LocateStored(authors_session, else a UUID rounds.caller, home)` — run for every selected round
    BEFORE S1 decides (`TranscriptLocated`); the id source written is §3's (the live value kept on a recompute, `stored`
    only where the row had none). One read per caller session id per run, at the lowest `fromUtc` (`RoundAttribution.ReadFrom`)
    any of its selected rounds needs, cached for the run; the `ObservedSession` is mapped once through
    `AttributionMapping.Observation` and the rounds of one caller are attributed from that one read. **`Assign` is handed
    settled rounds only** (S1 rule 0; E4 G6, M6).
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
  - `TheTimelineIsOneShape_WhicheverSourceFilledIt`: a store holding both — the file-filled and the database-filled
    `GateTimeline`s are equal, both through `AttributionMapping.Timeline`.
  - `ThePeersAreTheLivePathsPeers`: a spy on the `load` delegate → `AuthorPeers.Find` is called once per round with the
    same connection shape, and the peers handed to `Assign` equal those E5.S4 would hand it on the same store (before-open
    across two gate sessions of one caller follows from E4's `PriorEnd`, not from a join here). **Break-it**: reintroduce a
    database join for `before-open` → red where a peer's only same-caller round is `running` (in the file, not the store).
  - `ARound1WithoutItsFile_IsUnobservedNoOpenTime_AndRound2IsStillComputed_InTheDatabaseOnly`;
    `AReplacedSessionFile_IsTreatedAsNoFile`; `ARunningPeerRound_IsSeenFromItsFile_AndNamedStillRunning`.
  - `EachCallersTranscriptIsReadOnce`: a counting reader seam over three rounds of one caller → one read.
  - `NoUnsettledRound_ReachesAssign`: a store with a skipped, an interrupted and a `running` round → the `assign` seam is
    never called for them and they keep an empty state.
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
  (`RoundAttribution`, E4.S5's `AttributionMapping`), E5 (live path, `AuthorPeers.Find`, row shape).
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
    the database is not touched for that round. **An equal result writes nothing**: a computed snapshot equal to the stored
    one, member for member (versions, `PeerGates` and `IdSource` included) → `kept: unchanged`, no save, no transaction.
  - **A `heal` writes the FILE only.** The database row is the source and is not rewritten: its `authors_id_source` keeps the
    live value, the healed snapshot carries `IdSource = "healed"`, and the two legitimately differ (the file holds `Unknown`
    members the database never had). `healed` reaches the column only through a later re-projection of that snapshot, if
    the database rows are ever lost (consultants' Q2, §9a).
  - **Then the projection, FROM the saved snapshot, in ONE transaction** (E2 §10 point 10): `using var tx =
    db.Transaction();` → **re-read the round's `authors_state`, `authors_reader`, `authors_attribution` and `size_state` inside
    the transaction and compare them with S1's inputs — any difference means a live server wrote this round since the
    selection: rollback, `deferred: changed under us`, nothing written** (equal row counts are no proof of quiet, §3 *Safety*
    1) → `RoundChangesTable.Record(db, id, round.Size, round.Gitlinks)` when S1 said compute-size (its `UPDATE rounds SET
    size_state, delta_state, delta_from, size_origin, size_gitlinks` carries `size_origin = 'backfill'` from `Size.Origin`,
    parent §16 D12) → `RoundAuthorsTable.Record(db, id, round.Authors)` (E5.S2: the `authors_*` columns — `authors_reader`
    AND `authors_attribution` — `submodule_writes`, `uncovered_writes`, `ambiguous_*`, then delete-then-insert of the round's
    `round_authors`) → `tx.Commit()`. Every value comes from the snapshot just verified, never from the in-memory result; a
    null half writes nothing (both writers' rule). Nothing else is written: no `RecordRound`, no `RecordSession`. A run killed
    after the save and before the commit leaves a snapshot without rows — or, with a fault between a delete and its insert,
    the round's earlier rows, never none — which S1's rule 2(a)/6 re-projects on the next run without reading the transcript.
  - **The one exception to "file first" — no file.** With `FileState` gone/replaced (S1 rule 5, S2) the computed result is
    written to the database alone, in the same transaction with the same re-read; it is counted `written.databaseOnly`.
  - **The race §3 names** (`open` and the orphan sweep save without the claim): a verify that finds the snapshot gone
    leaves the database row as written; the next run's S1 rule 4 recomputes it while the transcript exists, rule 2(b)
    heals it when the transcript is gone. The summary counts both.
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
  - `TheTwoVersionsAreOnEveryWrittenRound`: `authors_reader` equals E3's constant and `authors_attribution` E4's, both read
    from the types. **Break-it**: bind an empty version for either → red; and the consequence is asserted too — with a
    version missing, S1 reselects the round on the next run, so a correct first run must leave `written = 0` for the second.
  - `ANewerRow_Survives` and `AStrongerOlderObservation_Survives`, at the writer with S1's decision.
  - `AHeal_WritesTheFileOnly`: after a heal the database bytes are unchanged (SHA-256 of a canonical dump), the file's
    snapshot has `IdSource = "healed"` and `Unknown`'s values, and the column still reads the live source. **Break-it**: project
    the healed snapshot → red, `authors_id_source` reads `healed`.
  - `AnEqualResult_WritesNothing`: a store whose round already holds exactly the result → no save (mtime), no transaction
    (a counting seam), `kept.unchanged = 1`.
  - `ARoundChangedUnderTheRun_IsDeferred`: a seam that rewrites the round's `authors_state` between the selection and the
    transaction → rollback, nothing written, `deferred.changedUnderUs = 1`. **Break-it**: drop the in-transaction re-read →
    red, the live server's row is overwritten.
  - `ARoundWithoutItsFile_IsWrittenToTheDatabaseOnly_AndASecondRunKeepsIt`.
  - `TheSessionFileKeepsEveryOtherField`: the file before and after, parsed, with the touched round's `Authors` and
    `Size` removed from both, is equal; the other rounds' snapshots are untouched.
  - `ABusySession_IsDeferred_AndNeitherSideIsWritten`: the test holds the claim → database and file unchanged,
    `deferred = 1`.
  - `TheSnapshotIsSavedBeforeTheProjection`: a writer seam that throws after the session-file save and before the
    database transaction (a test-only fault point, the run "killed" between the two writes) → the file holds the
    snapshot, the database holds no authors for the round; the next run re-projects it (S1 rule 2(a)/6) WITHOUT opening
    the transcript (the fixture's transcript deleted between the runs proves it) and its `round_authors` rows and
    authors columns equal those of an uninterrupted run, byte for byte. **Break-it**: write the database first → after
    the fault the database holds rows the file does not, and the equality fails.
  - `ASnapshotLostToAConcurrentOpen_IsRecomputedOnTheNextRun_WhileTheTranscriptExists`: write; save the file from a copy
    loaded before the write (what `open` does); run again → the file holds a recomputed snapshot (its placed ids and rows
    equal the first write's, E5 M2), the column's `authors_id_source` unchanged. Its twin
    `ASnapshotLostToAConcurrentOpen_IsHealedOnTheNextRun_WhenTheTranscriptIsGone`: the same with the transcript deleted →
    the snapshot is back as `healed`, the database bytes unchanged.
  - `ALiveReRecordWithoutTheSnapshot_KeepsTheBackfilledAuthors` (the E5 cross-check), and its size twin
    `ALiveReRecordWithoutASize_KeepsTheBackfilledSize`.
  - `TheProjectionIsOneTransaction`: a writer seam that throws after `RoundChangesTable.Record` deleted the round's rows and
    before `RoundAuthorsTable.Record` → the round's earlier `round_changes` rows and states are still there. **Break-it**: drop
    the transaction → red, the round has no size rows.
  - `ABackfilledSize_CarriesTheBackfillOrigin`: `rounds.size_origin = 'backfill'` and the session file's `Size.Origin` agree.
  - Sentinels absent from the database bytes and the session files.
- **Done when.** All pass; the six break-its observed red with the named symptom and restored green.
- **Depends on.** S1, E2.S3 (`RoundChangesTable`, `RoundRecord.Size`/`Gitlinks` — shipped), E5.S1 (`RoundRecord.Authors`,
  `AuthorsSnapshot.Unknown`), E5.S2 (step 24, `RoundAuthorsTable.Record`, `AgentKeyOf`).
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
    74 with `Program.WhyUnreadable`'s sentence (`RoundCountsMode.cs:62-68`). The selection, the pre-backup counts and the
    whole of `--dry-run` go through the READ-ONLY connection (§3 *Opening the store*); `user_version` below 24 → 74 naming
    the step ("this side has not run the release"); `RoundsDb.OpenExisting` is reached only after the backup check passed.
  - **Backup FIRST, verified, and the write gated on it** (not under `--dry-run`; the owner's decision of 2026-10-15: the
    backfill writes the LIVE stores on both sides — Windows `%LOCALAPPDATA%\coai-mcp`, WSL `~/.local/share/coai-mcp` — after
    a backup of each). Into a NEW folder `<dataDir>/backfill-backups/<yyyy-MM-ddTHH-mm-ssZ>/`, beside the store, named in the
    summary:
    1. the database by SQLite's ONLINE backup of `coai.db` into `coai.db` there (`SqliteConnection.BackupDatabase`). It
       carries what the `-wal` holds, consistently — a plain copy of `coai.db` + `-wal` + `-shm` while another server writes
       can catch them mid-checkpoint, which is a copy that opens and is wrong;
    2. every `sessions/session-*.json`, copied byte for byte;
    3. **the check**: the copy opens read-only, `PRAGMA integrity_check` answers `ok`, its `user_version` equals the
       source's, the row count of EVERY table (`sqlite_master`'s list, not a hand-kept one) equals the source's counted
       BEFORE and AFTER the backup, **and the source's `PRAGMA data_version`, read on the mode's own read-only connection
       before and after, is unchanged** — counts alone do not prove quiet (an `UPDATE` keeps them; `data_version` changes on
       any other connection's commit, which is the fact wanted); every copied session file's SHA-256 equals its source's, and
       the file counts agree;
    4. **the gate**: any check that fails → nothing is written, the summary names the check, exit **74**; the folder stays.
    The mode never deletes a backup, in any case and on either side (nothing is deleted in WSL). The online backup is new to
    this repository — the published Native-AOT binary is exercised once in O1 (the dry run does not touch it; O2's first run
    does), per [testing.md](../.agents/conventions/common/testing.md) § *Starting is not working*.
  - **Run**: S1 selects, S2 rebuilds, S3 writes — per gate session in round order (a predecessor's size is saved before its
    successor's delta is measured, S2), the reader reading each caller session ONCE per run and caching the result.
  - **Summary JSON on stdout**, text-free: `readerVersion`, `attributionVersion`, `since`, `dryRun`, `backupDir`,
    `backupCheck: {opened, integrity, userVersion, tables, rowsMatch, dataVersionUnchanged, sessionFiles, filesMatch}`,
    `selected`, `written: {computed, recomputedProvisional, recomputedSnapshotLost, reprojected, databaseOnly}`, `healed`,
    `kept: {equalOrNewer, strongerExisting, transcriptGone, peerStillRunning, noFile, unchanged}`, `deferred: {busy,
    unreadable, changedUnderUs}`, `unobserved: {<reason>: n}` (the states written, per reason), `size: {written, kept,
    unrecoverable: {<reason>: n}, delta: {<ScopeState>: n}}`, `elapsedMs`. `backupDir` is the one path printed — the
    operator's own folder, and never committed (O4). `recomputedProvisional` is the count E6's `provisional` cells shrink by.
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
    round's state names it. `TheProvisionalRowsAreTheOnesRecomputed`: a store with two `peer running` rows, one whose named
    peer rounds are final and one still running → `recomputedProvisional = 1`, `kept.peerStillRunning = 1`, and E6's
    `--round-authors` JSON shows `provisional` one lower after the run.
  - `ADryRunWritesNothing`: SHA-256 and mtime of `coai.db`, `-wal` and every session file unchanged, no backup folder; its
    counts equal the real run's. `ADryRunNeverOpensTheStoreForWriting`: over a store whose `user_version` is 24 but whose
    `-wal` is absent, no `-wal`/`-shm` appears and a connection-string seam saw `Mode=ReadOnly` only. **Break-it**: open
    through `OpenExisting` in the dry run → red, a `-wal` appears.
  - `TheBackupHoldsTheStoreAsItWas`: the backup opens, `integrity_check` is `ok`, every table's row count and every session
    file's SHA-256 equal the pre-run source's — over a store in WAL mode with uncheckpointed rows (they must be in the copy).
  - `ABackupThatDoesNotVerify_WritesNothing`: a seam that changes one source row between the before and after counts (a
    concurrent writer) → exit 74, the summary names `rowsMatch`, `coai.db`'s SHA-256 and every session file unchanged by the
    mode, the backup folder kept. **Break-it**: write before the check → red, the store changed. Its twin
    `AConcurrentCommitDuringTheBackup_IsRefused`: a second connection runs an `UPDATE` that keeps every count → exit 74, the
    summary names `dataVersionUnchanged`. **Break-it**: drop the `data_version` read → red, the run writes.
  - `TheFirstWriteComesAfterTheVerifiedBackup`: an operation-order seam records every open and write; the first write to
    `coai.db` or to any session file is after the backup's verify, and `OpenExisting` is the first ReadWrite open.
  - `TheModeNeverDeletes`: two runs leave two backup folders; no file the mode did not create is missing afterwards.
  - Exit codes, one test each: 0 with no database and no data directory created, and 0 when every round is already
    current (nothing to do); 65 for a bad, a repeated and an unknown flag; 74 for a `coai.db` that is a directory, for
    a session file that cannot be written, for a backup that does not verify, and for a NAMED `COAI_DATA_DIR` that is missing
    (the `WriterModes` refusal); 75 when the test holds one session's claim during the run (the summary printed,
    `deferred = 1`); and a sweep asserting no path through the mode returns 64. `EveryModeIsClassifiedForAMissingNamedRootTests`
    green with the mode in `WriterModes`.
  - `--since`: a round started before it is untouched.
  - Sentinels absent from stdout, stderr, the database bytes and the session files.
- **Done when.** The scenario passes; the six break-its observed; no write precedes a verified backup; `--backfill-authors`
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
- **Dry runs — the hard precondition of O2 and O3** (§3 *Safety* 2), both sides (`--dry-run`), the WSL one from a script
  file; the two summaries recorded against the predictions. A writing run is not started until the dry run of THE SAME
  binary (`coai-mcp --version`) over THE SAME store exited 0 on the same day; the dry run is repeated after any reinstall.
- **The `before` report**, both sides: `coai-mcp --round-authors --since 2026-09-30T00:00:00Z` (JSON) BEFORE the first
  writing run — its per-cell `provisional` and `size coverage unknown` counts are the numbers O4 compares the `after` with.
- **Pass.** The release is recorded; every running server of each side is on it; both dry runs exit 0 and both `before`
  reports are kept; every number is beside its prediction; any prediction missed by more than its own stated margin is
  explained in writing before O2 — not by adjusting the prediction.
- **Model.** main session (**opus**), attended.

### E7.O2 — Backfill Windows, twice

- **Precondition**: O1's dry run of this binary over this store, same day, exit 0; O1's `before` report kept; no round
  running on the side (the backup's before/after counts and `data_version` must agree, S4). Then run `coai-mcp
  --backfill-authors --since 2026-09-30T00:00:00Z` against the LIVE store; record the summary, its `backupCheck` and the
  backup folder — a failed check has written nothing (exit 74) and is re-run, never bypassed. Run `--round-authors` (JSON)
  and keep it. Run the backfill again (its own backup first); record its summary; run `--round-authors` again and compare
  the two JSON documents by SHA-256. Re-run while any `deferred` count is above 0, recording each.
- **Pass.** Every writing run's `backupCheck` passed BEFORE it wrote; the second run reports every `written` count 0 and
  `healed = 0` (or only heals it can name); the two `after` report JSONs are byte-identical; the `after` report's
  `provisional` count is the `before` count minus the first run's `recomputedProvisional`, and `kept.peerStillRunning`
  explains the rest; each backup folder opens read-only with the counts its summary printed. No backup is removed by the
  session (the operator retires them after the RESULTS commit, §7).
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
     the JSON, never recomputed — beside the backfill summaries' `size.delta` counts, which must agree; and **per cell, per
     side, the `provisional` and the `size coverage unknown` counts BEFORE and AFTER the backfill** (E6 D3, D12) — how many
     rows E7's repair changed, and how many rows stay provisional (`kept.peerStillRunning`, a transcript gone) or of unknown
     coverage, with the reason.
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
| Rows written by the backfill | none beyond the parent's §14 (`round_authors`, `round_changes`) — the backfill fills rows the live path would have written | with their round | per round: the session file first (the snapshot), then the database projection from it, under the claim — S3's order; a crash between leaves a snapshot without its projection, which the next run re-projects without opening the transcript (S1 rule 2(a)/6) |
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
      and every resolution survive; both versions (`authors_reader`, `authors_attribution`) are on every written round.
- [ ] The two exceptions to "equal or newer → keep" are the only recomputes of a current row: a provisional row once every
      named peer round is final (or a grown peer set), and a snapshot lost while the transcript exists; a heal writes the
      file only; a round changed by a live server between selection and write is deferred, never overwritten.
- [ ] A dry run (read-only, nothing written, the same counts) ran on each side before its first writing run.
- [ ] Unrecoverable rounds counted per reason; ran on both sides, twice each, with the predictions written first.
- [ ] Ten rounds per side hand-checked by a judge independent of the code, 10 of 10 agreeing, BEFORE any number.
- [ ] `research/RESULTS_who_wrote_the_code_under_review.md`: association only in its header, the hand-check first, the
      tables second, the naive reading beside them, what it does not settle.
- [ ] The code was RELEASED before the operation, and every running server of each side was on that release before its
      first write; the run and the RESULTS landed as a second commit.
- [ ] Every writing run backed up its live store FIRST and wrote only after the copy opened, passed `integrity_check` and
      matched the source's row counts, `data_version` and session-file hashes; nothing was deleted, on either side.
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

## 9a. Revised 2026-10-18 against `main` `e53e35ba` (mcp 0.59.0)

Rebased; no file this document cites in `src_mcp/src`, `core`, `storage` or `runners/Context/ContextAssembler.cs` changed
since `2d98f2f8` (the eight commits are releases, Vibe-on-WSL, the rules budget and E1's #851 fixture re-cut). Anchors
moved: `module_tests.md:3181` → `:3190` (its rows `:3205-3206`); corrected: `RoundChangesLog.cs` is in `src_mcp/src/Store/`,
`RoundChangesTable.Record` is `:28-39`, `SessionStore.Exists` is `:450`. E3 read at `16628077`, E4 `30362785`, E5 `c1216b42`,
E6 `131074b4`; the cross-epic list owed to E7 (E4 §11 *Cross-epic*, E5 §10 items 11 and 14, E6 §3a) applied in full:

- **S1 rewritten to E5 §10 item 11's order** — selection (settled rounds only, M6) → deferral → transcript gone (re-project /
  keep / heal / compute) → `recompute: provisional` (E4 G3; FINAL = `done` or `interrupted`, M7; a healed snapshot's peer
  set is unknown, H1) → `recompute: snapshot lost` (M2) → keep only with a snapshot, an `observed`/`partial` state and BOTH
  versions ≥ current (H2) → re-project → compute → size. New inputs: `AuthorsAttribution`, `PeerGates`, `IdSource`,
  `FileState`, `TranscriptLocated`, `PeerStatus`. The goal sentence names the two exceptions (list item 8).
- **S2 calls E4.S5's `AttributionMapping` and E5.S4's `AuthorPeers.Find(connection, load, deadline: null)`** (B1; E5 items
  8 and 14) and **drops its own `before-open` database join**; `Assign` sees settled rounds only; round 1 without its
  file is E4's `no open time` — E7's own `open time not recorded` is withdrawn as a duplicate.
- **S3**: a heal writes the file only; the two versions on every written round; an equal result writes nothing; the round's
  columns are re-read inside the transaction and a change defers the round; a gone/replaced file is the one database-only
  write.
- **S4**: the dry run and the selection are read-only (`RoundsDb.OpenExisting` only after the verified backup);
  `PRAGMA data_version` joins the backup check; the summary's `written`/`kept`/`deferred` sub-counts; three tests and three
  break-its added. **O1**: the dry run is the stated precondition of O2/O3; a `before` report. **O4**: the `provisional`
  and `size coverage unknown` counts before and after (E6 §3a's two counts).
- **The consultants (question `20cb5f62…`, two rows, both answered)** agreed on all six design forks, each verified in
  the code before it was written: the name `TranscriptLocated` apart from E3's per-agent `TranscriptPresent`
  (`ObservedSession.cs`, `CallerTranscriptLocator.cs`); a heal writes the file only (the column keeps the live source);
  the grown-peer trigger is `PeerIds ⊄ PeerGates` with a complete lookup and a known set; the backfill never sweeps a dead
  `running` row (`SweepOrphanedRounds` at startup, `PanelService.cs:144`, is the one owner); a gone/replaced file is a
  database-only write kept on later runs (and never rebuildable — stated), with an equal-result no-op so an `unobserved`
  row at current versions does not rewrite itself; the dry run is an operator precondition, not a marker file. Their two
  corrections were taken: equal row counts do not prove no concurrent write (`data_version` added), and
  `RoundsDb.OpenExisting` migrates, so nothing before the verified backup may open it (`RoundCounts.cs:100` is the
  read-only shape).

## 10. Open with E3–E5

What this document assumes of contracts still being amended; each is the named epic's to confirm, or E7's to follow when it
ships. None blocks S1's table tests. Items 2, 3, 5 and 6 were closed by E5 on 2026-10-16/17 and are kept as one line each.

1. **E5 — no `JsonExtensionData`.** Decided and declined by E5 (§3 *older server*; §7 item 18): the database is the durable
   copy. E7 gates its operation on the running servers' versions (O1) and recomputes or heals on the next run.
2. **E5 — the writer's signature. CLOSED:** `RoundAuthorsTable.Record` opens no transaction and joins the caller's (E5 §10
   item 3, `TheWriterJoinsTheCallersTransaction`), which is what S3's `Transaction()` seam needs.
3. **E5/E4 — two versions. CLOSED:** `authors_reader` is E3's `ReaderVersion`, `authors_attribution` E4's
   `RoundAttribution.Version` (E5 §10 item 5; E4 §S2.5); S1 compares both, `0` meaning the component did not run.
4. **E3 — `LocateStored` and the registry.** `stored` has no registry rung; a round whose live server read the registry and a
   backfill that reads `authors_session`/`rounds.caller` must name the same session. E1 measured the registry equal to the
   variable in every probe (RESULTS Q4); the `/clear` check was answered "ok" by the owner on 2026-10-16 — a cleared
   session's rounds may still carry a stale id, which the backfill cannot detect; O1's prediction of `transcript not found`
   counts is where it would show.
5. **E4/E5 — peers on every round. CLOSED:** one unit, `AuthorPeers.Find`, called by E5.S4 and E7.S2 (E5 §10 item 8).
6. **E4 — reasons containing `", "`. CLOSED:** none can (E4 `NoReasonContainsTheJoiner`, E5 `E5sReasonsContainNoJoiner`,
   E5 §10 item 4); S1's heal split is exact.
7. **E5 — the 60 s ceiling.** E5.S4 measures the C# read time before fixing the live 2 s (E5 §10 item 10); S2's 60 s per
   caller is this document's number, not a measurement. O1's prediction of `partial: read budget` counts is what tests it.
8. **E4 — `PeersKnown` on the input.** S1 reads it as "the lookup completed" (an incomplete lookup proves no growth). E4.S5's
   loader sets it `true` for fixtures; E4 to confirm that `AuthorPeers.Find` sets it `false` when a peer file did not load
   or the deadline cut the walk — else S1 needs its own flag from S2.
9. **E4 — overlap for `ANamedPeerRoundStillRuns`.** S1 takes "a round of a named peer that overlaps this round's windows"
   as E4 §S2.3's window overlap over the peer's timeline; if E4's `partial: peer running` is set from a different test, S2
   uses E4's predicate rather than its own.
10. **The parent — §9's no-overwrite sentence.** Gains the two exceptions and "last SETTLED window" for `before-open`
    (E4 §11 *Cross-epic*); edited by the main session, not here.
