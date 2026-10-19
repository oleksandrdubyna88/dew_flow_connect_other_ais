# PLAN — Round authors, epic E5: store and show

> Status: **S1–S6 implemented on `feat/round-authors-e5`, 2026-10-19 (pull request #863, not merged — stacked on #867 E3 and #862 E4; E4 final and main merged in, judge-reviewed, code gate `good_enough` the same day, §12.1); §12 is the implementation record — its deviations, the two items owed from E7 and PR #868, and what is open. Plan 2026-10-14; revised 2026-10-16 against `main` at `59463d73` (§9), and again
> 2026-10-16 for the E4/E5 reconciliation Fable decided (§8, E4 at `244d953c`) and the points E6 (`3c50a78c`) and E7
> (`740d1a4f`) raised for E5 (§10); and again 2026-10-17 for the independent review of `2af746b3` (eight points) and the
> E5 consequences of E4's §S2.2b (G1–G12, E4 at `7c3bf469`) — §11 records both, against `origin/main` `ce9cd298`, whose
> `src_mcp` is byte-identical to `59463d73`; and again 2026-10-17 for the independent judge review of `f4dcc003` and E4
> `7c3bf469` (B1, H1–H2, M1–M8, L1–L10 — §11.4; E7's corrected rule order and its full change list in §10 item 11; the one
> `ObservedSession → AttributionInput` mapping is E4.S5's `AttributionMapping`, which S4 and E7.S2 call — B1).** Epic E5 of [PLAN_a_round_records_who_wrote_the_code_and_how_much.md](PLAN_a_round_records_who_wrote_the_code_and_how_much.md).
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
| The two versions | E3.S4 (`ObservedSession.ReaderVersion = 1`); E4.S2 (`RoundAttribution.Version = 1`, E4 §S2.5, `7c3bf469`) | `authors_reader` (parent §9) and `authors_attribution` (§11, E4 G3) — E7's "equal reader → keep" compares both | in E3's and E4's contracts; S1 adds no constant |
| Attribution of ONE round | E4.S1 (`AttributionInput`, `UtcInstant.From`, `ObservedWrite.AnsweredAt`), E4.S2 (`RoundAttribution.ReadFrom`, `Assign`), E4.S3 (`RoundAuthorsBuilder.Build` → `RoundAuthors`, with `Stage`, `Number`, `Windows`, `PeerGates` since §S2.2b) | pure, in `src_mcp/core/Authors/`; `Assign` throws `ArgumentException` when `Validate()` refuses the input — and it refuses a round that is not recorded and SETTLED (E4 G6) | not started |
| The ONE mapping | E4.S5 (`src_mcp/src/Store/AttributionMapping.cs`, B1 2026-10-17) | `Observation(ObservedSession) → CallerObservation`, `Timeline(gateSessionId, repoPath, openedUtc, rounds, callerOf) → GateTimeline` (`CompletedUtc` only on `done`), `Universe(gate, peers, observation)` — rule-free, tested by E4.S5's `TheMappingDecidesNothingTests` | not started. S4 step 5 and E7.S2 CALL it; neither writes a mapping of its own |
| Sanitised fixtures with sentinels | E1.S5 (#828) | 20 cases under `src_mcp/tests/fixtures/caller-transcripts/c01`–`c20`, each `projects/<slug>/<id>.jsonl` (+ `<id>/` subagents) and a `rounds.json`; `manifest.json` names each case's `fixtureRoots` (`D:/fixture/*`); instants at `2000-01-01` | **merged.** Re-pointable roots and one-offset instants are E1's own contract (E1 doc `:204-205`), so defect 13 is closed |
| The measured read time | E1.S4 (#828) | `research/RESULTS_caller_transcripts_on_both_sides.md` Q6: Node, largest Windows session 650 MB, day-gated parse p50 **19.9 s** — the prediction (≤ 300 ms) refuted by two orders of magnitude | **merged.** The budget stands on the `fromUtc` slice (D7) and on S4's own C# measurement, never on the whole session |

## 3. Decisions this document had to make (the parent is silent or inconsistent)

Each is a choice the main session may overrule before the story starts; §7 lists them as parent defects.

- **Which rounds — one sentence, one owner** (M6, 2026-10-17). A `PlanReview` or `CodeReview` round that is SETTLED —
  status `done`, verdict not `skipped` (E4 §S2.2b G6) — gets a snapshot; **every other round gets none and keeps an EMPTY
  `authors_state`, which reads *not recorded*: a skipped round** (`src_mcp/src/Server/Rounds/RoundSkips.cs:75-80`), **an
  interrupted round** (`RoundRecord.Interrupted`, written by the startup sweep over a `running` row whose pid is dead,
  `src_mcp/src/Server/SessionStore.cs:608-631`, `:626`, `:718-719`), **a running round, and every feature or document
  round — live here and in E7's backfill alike; E7 computes nothing for them and writes no reason.** The engine's own round
  is always settled at the call site — `live.Finish` records it `done` (`src_mcp/src/Server/LiveRound.cs:235`) — so the guard
  is one condition in the step.
- **The peers** (E4.S2 §S2.8). On EVERY round, not only the first: the gate sessions other than this one holding a round whose
  `rounds.authors_session` or `rounds.caller` equals this caller, found by one read-only query on the projection; each peer is
  then built as a `GateTimeline` from its SESSION FILE (`SessionStore.Load(repo_path, branch)`, `SessionStore.cs:461`, with
  `sessions.repo_path`/`branch` from the same query) — `OpenedUtc` from `PersistedSession.OpenedUtc` (`:278`), never
  `sessions.opened_utc` (parent §6), its journal from the file, `CompletedUtc` per round through `UtcInstant.From` — with
  `CallerSessionId` per round from `rounds.authors_session`, else `rounds.caller` when it is a UUID, else `""` (E4.S2 left the
  file-or-database choice to E5: the file). **A peer round still `running` is NOT without a completion on disk**: the running
  row is saved through `Record()` (`src_mcp/src/Server/LiveRound.cs:256`), which stamps `DateTime.UtcNow` (`:276`) into the
  positional `RoundRecord.CompletedUtc` (`SessionStore.cs:84`). So the mapping fills `GateRound.CompletedUtc` ONLY when the
  round's status is `done`, else null (E4 G1; E4's tiling applies the same rule) — a running peer ranks last and claims nothing.
  **A window overlapped by a running same-caller peer is `partial: peer running`** (E4 G3): E5 stores it as it is, never
  repairs it live and writes no row of another gate session — it holds no claim on that session (the claim key is repo +
  branch + document + feature, never the caller, `src_mcp/src/Server/SessionClaim.cs:32-38`). E7's backfill recomputes
  those rows once every overlapping peer round is FINAL — `done` or `interrupted` (E4 §S2.3, M7; §10 item 11).
  A store that cannot answer (absent, locked, older than step 24 → no column, a peer file unreadable or naming another
  session id) → `PeersKnown = false`, `Peers` empty, and every window `partial: prior windows unknown`. Step 24 indexes
  `rounds(authors_session)`.
  **The query sees projected rounds only** (M3, 2026-10-17): `RecordRound` runs at completion (`RoundEngine.cs:612-614`) and
  on a skip (`RoundSkips.cs:79`), never for a running round, so a peer gate session whose only same-caller round is still
  running is INVISIBLE to the live lookup; it is found when that round completes, and E7's grown-peer-set rule (§10 item 11
  (3)) recomputes this round then — the stated limit. E4's case 8b is therefore set up in S4 with a peer that also holds a
  projected `done` or skipped round.
  **The gate's own timeline** (M4): at the call site `session.Rounds` does NOT hold the round being attributed — the save
  appends it (`RoundEngine.cs:594`, `Rounds = [.. session.Rounds, record]`) and `LiveRound.Persist` saves its OWN
  `_session` (`LiveRound.cs:257`) — so the step hands E4.S5's `AttributionMapping.Timeline` the list `[.. session.Rounds,
  record]`; E4's `Validate()` refuses a `(Stage, Number)` absent from `Gate.Rounds`, and without this every live round
  would be `unobserved: invalid input`. An EARLIER round's `CallerSessionId` is its own snapshot's `Session` when the file
  has one (a round after E5), else `rounds.authors_session`, else a UUID `rounds.caller`, else `""` (E4 decision 2:
  `partial: prior caller unknown`) — read by the SAME read-only query, over `rounds WHERE session_id = this` beside the
  peer lookup (`RoundRecord.Caller` is a `CallerDeclaration(Vendor, Client, ClientVersion, Model)`, `CallerDeclaration.cs:34-38`,
  and holds no session id; `rounds.caller` is `RoundCommands.CallerFor`, `RoundCommands.cs:34-35`). When the store cannot
  answer, the file's snapshots are used and the rest is `""`, beside `PeersKnown = false`.
- **The id source is persisted** (`authors_id_source`, E3's `CallerSessionId.Source`: `registry` / `environment`; `stored`
  and — since 2026-10-17, H1 — `healed` are E7's: `healed` marks a snapshot copied FROM the database, whose peer set is
  unknown, never `[]`): parent §5 requires it, §11 step 4's column list omits it.
- **`open`/`status` do not carry the snapshot.** `SessionAnswerFor` sends `session.Rounds` to the calling AI on every reply
  (`src_mcp/src/Server/PanelService.cs:1878-1894`, the list at `:1888`); the rows would cost the caller up to several KB of
  context per round. The answer strips `Authors` (null is omitted, `src_mcp/src/Server/ServerJsonContext.cs:339-344`); the file
  and the page keep it. `Size` and `PlanSource` are NOT stripped today — they are a few hundred bytes; the snapshot is not.
- **The snapshot carries every text-free member of E4's `RoundAuthors`; the columns keep the parent's.** Since E4 §S2.2b
  (G3, G9) `RoundAuthors` is `Stage`, `Number`, `CallerSessionId`, `State`, `Reasons`, `Rows`, `Ambiguous`, `Elsewhere`,
  `ClaimedByPeer`, `UnobservedLaunches`, `Windows` (one `WindowAuthors` per window — its own state, reasons and totals) and
  `PeerGates` (the peer gate session ids the assignment saw), plus the in-memory `WritesOutsideTheGateCheckout`. The
  snapshot is that record minus the roots, plus E5's own five members (S1) — no longer a hand-picked flat twin of it, and a
  reflection fact keeps it so. §11 step 4 names no column for `Elsewhere`, `ClaimedByPeer`, `UnobservedLaunches`, `Windows`
  or `PeerGates`, and E6 reads none: they live in the snapshot only, so a later step can project them from the session files
  without a backfill. Step 24 projects the round roll-up and the ambiguous totals only, `ambiguous_agents` included (E4's
  `ActivityTotals.Agents`), plus `authors_attribution` (E4's `RoundAttribution.Version`).
- **Every snapshot E5 builds WITHOUT E4 is `AuthorsSnapshot.Unknown(...)`** (review point 4): a `NotObservable` location,
  an input `Validate()` refuses, a reader that throws, and a deadline missed before attribution ran. One factory gives every
  member no attribution measured its unknown value — `Ambiguous`, `Elsewhere`, `ClaimedByPeer` = `ActivityTotals.Unknown`,
  `UnobservedLaunches`, `SubmoduleWrites`, `UncoveredWrites` = `-1`, `Rows`, `Windows`, `PeerGates` = `[]` — so no such round
  reads as a measured zero (an `uncovered_writes` of `0` would say "covered"). **Its two versions say which component ran**
  (H2, 2026-10-17): `Reader = 0` when E3's reader produced no `ObservedSession` (a `NotObservable` location, a reader that
  threw, a deadline missed before the read), else `ObservedSession.ReaderVersion`; `Attribution = 0` when `Assign` did not
  run (`invalid input`, `reader failed`, a deadline missed before attribution), else `RoundAttribution.Version` — so E7 never
  keeps such a row as current (§10 item 11 (5)). **A snapshot E4 itself built as `unobserved`** (E3's `transcript empty`
  after a `Found` location; a blind round) is not `Unknown(...)` — it is E4's record, which carries `-1` in
  `UnobservedLaunches` (E4 S3, M1) — and the step writes `-1` for `SubmoduleWrites`/`UncoveredWrites` on it too, never
  the `0` an empty `WritesOutsideTheGateCheckout` would give (M1).
- **The card line describes THIS round's windows** (`before-open` + `round`), with the `delta` size when its state is
  `recorded` and the `whole` size otherwise — so the authors and the size describe the same stretch of work. Contributors
  (the union over rounds, parent §6) are the report's (E6), not the card's: computing them in TypeScript would be a second
  implementation of E4.S4 (§8, item 4).
- **A null snapshot never erases projection rows** — so E7's backfilled rows survive a re-record of the round (D14), exactly
  as a null `Size` writes nothing (`src_mcp/src/Store/RoundChangesTable.cs:23-39`).
- **An older server strips the snapshot from the FILE; the database keeps the columns — and E5 cannot close that** (E6 §4
  item 8, E7 §10 item 1; decided 2026-10-16). Verified on `main` (`1fe25d19`): `PersistedSession` and `RoundRecord` declare
  no `JsonExtensionData`, and the context's whole option set (`src_mcp/src/Server/ServerJsonContext.cs:339-344`) leaves
  `UnmappedMemberHandling` at its default, *skip*. So a `coai-mcp` released before E5 — one per VS Code window not yet
  reloaded — that loads and saves a session file drops `authors` from EVERY round of it: `open` loads and saves on every call
  (`src_mcp/src/Server/PanelService.cs:497-511`), and so do its rounds, `resolve` and the startup orphan sweep. The
  shipped `Size`/`Gitlinks` (step 22, #829) and `PlanSource` (step 23, #841) carry exactly this exposure today and shipped
  with no guard and no sentence — nothing in `src_mcp` handles it. **E5's answer:**
  - **The database is the durable copy of what step 24 projects.** An older server's upsert names none of the authors
    columns (`RoundsDb.cs:848-869`, `ON CONFLICT … DO UPDATE SET` lists its own), it never touches `round_authors`, it runs
    no step on a file at a later `user_version` (`src_mcp/storage/SqliteMigrator.cs:80-87`), and an E5 server re-recording a
    stripped round writes nothing for its null snapshot (the bullet above). S2 proves it with a test.
  - **What is lost, stated as the limit:** for the stripped file's rounds, the card line (S6 reads the file only), the
    rebuild path (deleting `coai.db` and re-projecting the file gives those rounds no authors), and every member the
    database never held (`Elsewhere`, `ClaimedByPeer`, `UnobservedLaunches`, `Windows`, `PeerGates`, §3 above). **E7 as
    written today repairs none of them** (review point 1, checked on E7 `740d1a4f` §E7.S1, `:167-174`): its heal is rule 3
    (rule 5 is the SIZE rule), and rule 2 — *keep* a non-empty state written by a reader ≥ the current one — fires first, so a
    stripped file whose database row is current (every live E5 round's is) is never reached by the heal. And rule 3 copies the
    snapshot FROM THE DATABASE ROWS, which cannot restore the five members above and needs an inverse of S2's `AgentColumn`.
    The repair E5 needs is therefore stated as a requirement on E7 in §10 item 11: recompute from the transcript while it
    exists (equal versions give back the same placed ids, owners and associations and restore every member; what MAY
    differ is named there — M2); heal from the database only when the transcript is gone, through S2's ONE inverse, with
    `AuthorsSnapshot.Unknown`'s values for the members the database lacks and `authors_id_source = 'healed'` (H1). A round
    whose transcript is gone (Claude Code deletes them after 30 days by default) therefore comes back with its rows, state
    and columns, and with those five members unknown for ever. The window closes when every server on that side runs E5's
    release; E7's O1 gates on exactly that.
  - **Not `JsonExtensionData`, here.** It protects only against servers released AFTER it — the binaries that drop
    `authors` are already released, so it cannot close this exposure. It would also echo every unknown member into the
    `open`/`status` answer unless S1's strip removed it too, and it is a change to the two types every stage saves, owed to
    every field since `Caller`, not to this one. §7 item 18 hands it to the main session as its own change.
  - **Measured, not assumed** (the `module_server.md` precedent, *"What an older server does with the tick"*): S1 runs the
    released `coai-mcp` 0.58.0 once, `open` on a THROWAWAY repository (a temp `git init` with one commit — `open` prunes the
    repository it is given, `PanelService.cs:490`, so never the real one) over a copied data dir whose session file carries
    a snapshot, and records the observation and the code paths the run exercised in its summary — prediction: `authors` gone
    from every round of the file. This paragraph is the one place the run is specified; S1, §10 item 1 and the DoD point here. `open` itself never opens `coai.db` (`PanelService.cs:474-515`: load `:497`, save `:511`),
    so the database half of the prediction is S2's suite test, and the run says whether the startup re-projections
    (`:164`, `:176`) opened the file at all.
- **Uniqueness of STORED ownership across gate sessions is not E5's** (E4 G3: E7's backfill owns it; nothing in this
  document claims it). E5 keeps the tests only where it owns the behaviour (S4): the persisted interleaving (a round overlapped
  by a running same-caller peer stores `partial: peer running` unchanged and leaves the peer's file and rows byte-equal),
  the late result (the mapping fills E4's `AnsweredAt` from E3's `AnsweredUtc`, so E4's horizon decides; E5 adds no rule),
  and the failed projection (`Projection.Write` is best-effort, `src_mcp/src/Store/Projection.cs:24-38`: the snapshot, with
  its `PeerGates`, is in the session file before the projection runs, so E7 can see the peer set the round saw).
- **`--log` follows the log's absence rule.** A round with no recorded state has NO `authorsState` member — the rule `size`
  and `planSource` already follow (`research/module_server.md`, the two `--log` paragraphs). The 2026-10-14 text printed the
  words `not recorded`; one rule for every `--log` member is worth more than the words.
- **Growth** is the parent's §14 rows for `round_authors` and `RoundRecord.Authors` (D15): the snapshot is ≈ 1.2–4 KB a
  round in the indented session file (~0.4 KB a row × 3–10 rows) → **22–75 MB a year a side**, and every progress-tick save
  rewrites the whole file, so the write cost grows with it. The three totals this document adds to the snapshot cost
  ≈ 150 B a round more, and E4's `Windows` and `PeerGates` ≈ 300 B more (E4 G9, G3) — ≈ 450 B a round, ≈ 9 MB a year a
  side; that figure is the ADDITION, not the snapshot (§7 item 20 asks the parent's §14 row to carry it). **Retention:** none of its own —
  session files are kept for ever (parent §14, a decision: they are what the database is rebuilt from), and the snapshot
  lives and dies with the file it is in. **Interruption:** the snapshot has no write of its own; it rides the round's
  own `SessionStore.Save` under `SessionClaim` (temp file + move, or in place under `SessionTurn`,
  `SessionStore.cs:516-582`), so a crash leaves the previous file or the new one, never a half snapshot. No new surface.

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
    E4.S1's purity test scans — it is a record and reads nothing): `AuthorsSnapshot(Stage, Number, Session, State, Reasons, Rows,
    Ambiguous, Elsewhere, ClaimedByPeer, UnobservedLaunches, Windows, PeerGates, IdSource, Reader, Attribution,
    SubmoduleWrites, UncoveredWrites)`. **The first twelve are E4.S3's `RoundAuthors` members, as E4 prints them** (`7c3bf469`,
    S3 *The records*; `Session` is its `CallerSessionId`): `State` (`observed` | `partial` | `unobserved`, the roll-up) and
    `Reasons`; `Rows` of **E4's `AuthorRow`, persisted as it is**; the three totals, E4's `ActivityTotals` (`Unknown` = `-1`
    in every term, never 0); `Windows` of E4's `WindowAuthors` (per window: `Window`, `State`, `Reasons`, the three totals,
    `UnobservedLaunches` — G9); `PeerGates`, ids only (G3). The last five are E5's: `IdSource` (E3's
    `CallerSessionId.Source`), `Reader` (E3's `ObservedSession.ReaderVersion`), `Attribution` (E4's
    `RoundAttribution.Version`), and the counts `SubmoduleWrites`, `UncoveredWrites` (gate finding 0; `-1` = unknown).
    `Rows`, `Reasons`, `Windows` and `PeerGates` are normalised to `[]` inside the snapshot; the snapshot itself is not.
    **Not named `RoundAuthors`, and not a hand-kept twin of it**: E4's record carries `WritesOutsideTheGateCheckout` with
    checkout ROOTS, which must never reach the session file; every OTHER member of it is in the snapshot, by name — a
    reflection fact (below) fails when E4 adds a member the snapshot lacks. `AuthorRow`, `ActivityTotals` and
    `WindowAuthors` are wrapped **as E4 prints them, unchanged** (E4 §S2.2a row *E5 §8.1*: final; a member change there is
    an E4 edit announced to E5 before S1 is coded); a row's `Agent` keeps E4's `"main"` in the snapshot — the column's `""`
    is S2's one mapping, and nothing in S1 maps it. `From(RoundAuthors authors, idSource, reader, attribution, submodule,
    uncovered)` is the one constructor from E4's result.
  - **`AuthorsSnapshot.Unknown(stage, number, session, state, reasons, idSource, reader, attribution)`** (review point 4) is
    the one constructor for a snapshot E5 builds without E4 (§3): `Rows`, `Windows`, `PeerGates` = `[]`; `Ambiguous`,
    `Elsewhere`, `ClaimedByPeer` = `ActivityTotals.Unknown`; `UnobservedLaunches`, `SubmoduleWrites`, `UncoveredWrites` =
    `-1`. No caller spells those values itself. `reader` and `attribution` are `0` when that component did not run (H2,
    §3) — the caller passes `0` on those paths, never the constant. `IdSource` is one of `registry`, `environment` (E3's),
    `stored`, `healed` (E7's, §10 item 11); the snapshot keeps it as a string, as the column does.
  - `Render()` — a method, so the JSON gains no member — gives the column and card text: `State` when `Reasons` is empty, else
    `State + ": " + string.Join(", ", Reasons)` (E3's `ObservationState.Render()` format, E3 doc §3). `IsWellFormed(string)`
    is the one rule for a stored state — S3 and S6 apply it, as `PlanSource.IsWellFormed` (`src_mcp/core/Rounds/PlanSource.cs:29`)
    is applied by `--log` and `reviewedFrom`. **The whole rule, written once** (review point 7): the value is exactly
    `observed`, or `partial: ` or `unobserved: ` followed by one or more reasons joined by `", "`, pairwise distinct, each
    matching `[a-z](?:[a-z -]*[a-z])?` — lower-case letters, spaces and hyphens, starting and ending with a letter. Every E3,
    E4 and E5 reason fits it, `peer running` (E4 G3) and `no reader for <vendor>` included; a count (`×2`, any digit), a colon,
    a comma inside a reason, an empty reason and a repeated reason do not — E4's reasons are distinct and count-free (E4
    §S2.2a row *E5 §8.6*), so any of those reads as malformed, never as a variant. The cases live in a new
    `shared/authors-state-vectors.json` (`{ value, wellFormed }`, the `shared/path-family-vectors.json` precedent), read by
    S1's C# test and S6's TypeScript test, so the two implementations cannot drift.
  - E5's own reasons are constants beside the record — `AuthorsSnapshot.InvalidInput = "invalid input"` and
    `ReaderFailed = "reader failed"` (S4) — so the joiner fact below can enumerate them; every other reason is E3's or E4's
    constant, copied verbatim.
  - `RoundRecord.Authors { get; init; }` (`AuthorsSnapshot?`) after `PlanSource` (`SessionStore.cs:223`), with remarks naming the
    `Caller` precedent and this plan. No getter default. No `[JsonSerializable]` registration: unlike `PlanSource`
    (`ServerJsonContext.cs:346-347`) it is never serialised on its own — the columns are written field by field.
  - `PanelService.cs:1888`: `[.. session.Rounds.Select(r => r with { Authors = null })]` in `SessionAnswerFor`.
- **The brief.** Primary-constructor records; `WhenWritingNull` already omits a null snapshot. Nothing in this story reads a
  transcript or touches the database. ≤ 4 cyclomatic complexity per method.
- **Tests (RED first; `CoaiMcp.Tests.exe --filter-class "*RoundAuthorsSnapshot*"`):**
  - A round saved with an `observed` snapshot of three rows, ambiguous totals, `Elsewhere = Unknown`, two `Windows` (one
    `partial: peer running`, one `observed`) and two `PeerGates` loads back equal, field for field — `-1` stays `-1`.
  - `TheSnapshotCarriesEveryTextFreeMemberOfRoundAuthors` (reflection): the snapshot's member names equal `RoundAuthors`'
    minus `WritesOutsideTheGateCheckout` (`CallerSessionId` as `Session`) plus E5's five; and `From(authors, …)` copies
    every one of them (a `RoundAuthors` with a distinct value in every member round-trips).
  - `AnUnknownSnapshot_HasNoMeasuredZero`: `Unknown(...)` has `-1` in `UnobservedLaunches`, `SubmoduleWrites`,
    `UncoveredWrites` and every term of the three totals, and empty `Rows`, `Windows`, `PeerGates`;
    `AnUnknownSnapshotNamesWhichComponentRan` (H2): built for a `NotObservable` location it has `Reader = 0` and
    `Attribution = 0`; built for `invalid input` it has `Reader = ReaderVersion` and `Attribution = 0`.
  - A session file written by today's binary (no `authors` key) loads `Authors == null`, and saving it again writes no
    `authors` key — the precedent's failure mode, stated as a test (the `TheGitlinkCount_RoundTripsThroughTheSessionFile_AndOneWithoutReadsNull`
    shape, `src_mcp/tests/TheRoundsSizeIsStepTwentyTwoTests.cs:155-167`).
  - `Render()` over every kind with zero, one and two reasons; `IsWellFormed` over every vector of
    `shared/authors-state-vectors.json` — at least `observed`, `partial: peer running`, `partial: read budget, peer running`,
    `unobserved: no reader for codex`, `unobserved: invalid input` (accepted) and `''`, `partial`, `observed: x`, `done`,
    `partial: read budget ×2`, `partial: read budget 2`, `partial: , read budget`, `partial: read budget, read budget`,
    `unobserved: a: b` (refused: no reason, a count, an empty, a repeated reason, a colon) — and every reason constant of E3,
    E4 and E5 rendered alone as `partial: <reason>` is accepted (so the rule cannot refuse a state E5 writes).
  - `status` over a session whose round carries a snapshot has no `authors` key in its JSON; the session file still has it.
  - The snapshot's JSON has no member named after a computed property (`isMain`, `rendered`), and a row's `agent` object has
    exactly one member, `id` — kept although E4.S1 now carries `[JsonIgnore]` on `AgentKey.IsMain` and its own fact (E4 doc
    `:143-144`): this test guards the FILE's shape, E4's guards the key's, and a later computed member on any of E4's records
    reaches the session file only past this one (§8 item 3).
  - `E5sReasonsContainNoJoiner`: no constant of `AuthorsSnapshot` contains `", "`, and every one is non-empty — E5's half of
    E4.S3's `NoReasonContainsTheJoiner` (over `AttributionReasons`, E3's `ObservationReasons` and `NoReaderFor(vendor)`, E4 doc
    `:747-751`), which S1 references and does not repeat. Together they are what makes E6 D18's split on `", "` exact.
  - Break-it: give `Authors` a materialising getter (`field ??= new(...)`) → the second test goes red naming the invented key;
    restore. Break-it: remove the strip in `SessionAnswerFor` → the `status` test goes red. Break-it: set `InvalidInput` to
    `"invalid, input"` → the joiner fact goes red. Break-it: drop `PeerGates` from the snapshot → the reflection fact goes red
    naming it. Break-it: let the C# rule admit a digit → the vectors test goes red on `partial: read budget 2`.
  - **The older server, measured once** — §3 *Measured, not assumed* specifies the run (the released 0.58.0, a throwaway
    repository, the paths recorded: `open`'s load and save `:497`/`:511`, the startup re-projections `:164`/`:176`); it is
    not a suite test, and when the database was not exercised the summary says so and S2's suite test is the only evidence.
- **Done when.** Tests green; no change to any existing session-file test.
- **Depends on.** E4.S3 (`AuthorRow`, `ActivityTotals`, `WindowAuthors`, `RoundAuthors` with `Windows`/`PeerGates`), E4.S2
  (`RoundAttribution.Version`). E2 has merged.
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
    `''`), `authors_reader`, `authors_attribution` (INTEGER, default 0 — E3's `ReaderVersion` and E4's
    `RoundAttribution.Version`; §7 item 19, closed), `submodule_writes`, `uncovered_writes` (INTEGER, default 0; `-1`
    written for unknown, §3's `AuthorsSnapshot.Unknown`), and
    `ambiguous_agents`, `ambiguous_launches`, `ambiguous_turns`, `ambiguous_input_tokens`, `ambiguous_output_tokens`,
    `ambiguous_cache_creation_tokens`, `ambiguous_cache_read_tokens` (INTEGER, default 0; `-1` written for E4's `Unknown`);
    `CREATE INDEX IF NOT EXISTS rounds_by_authors_session ON rounds (authors_session)`. Remarks in the style of step 22
    (`Schema.cs:618-634`) — the closest shape, a table plus state columns: empty state = not recorded; the zero defaults mean
    nothing without a state; appended, never inserted, for step 20's reason; a read-only reader runs no step.
  - `agent` is `""` for `main`: E4's `AgentKey` spells main `"main"` (E4 doc `:143`), the parent's column says empty (§11
    step 4) — **ONE mapping, in ONE place**: a `private static string AgentColumn(AgentKey agent) => agent.IsMain ? "" :
    agent.Id` in `RoundAuthorsTable`, the only code that turns a key into the column (Fable, E4 §S2.2a row *E5 §8.2*). **Its
    one inverse lives beside it** (review point 1): `internal static AgentKey AgentKeyOf(string column) => column == "" ?
    AgentKey.Main : new(column)`, which E6 D18's read and E7's heal from the database both call (§10 items 7 and 11) instead
    of spelling the rule again. The snapshot, `--log` and the card never map it. `window` is the row's `Window`
    (`before-open` | `round`); the row's `Stage`/`Number` are the round's own and are not stored twice. `Windows`,
    `PeerGates`, `Elsewhere`, `ClaimedByPeer` and `UnobservedLaunches` get no column (§3).
  - **`-1` is the contract for an unknown total** (E6 §4 item 2, D17): every `ambiguous_*` column holds `-1` exactly when the
    snapshot's `Ambiguous` is E4's `ActivityTotals.Unknown` term — an `unobserved` round, and any term E4 left unknown; a
    `0` is a measurement and never stands for "unknown". The snapshot carries the same `-1` (it persists E4's record as it
    is). The same holds for `submodule_writes` and `uncovered_writes`: `-1` exactly when no attribution ran (a snapshot from
    `AuthorsSnapshot.Unknown`), so `uncovered_writes > 0` is never false for an unmeasured round by accident — it is unknown,
    and E6 D12 must say so (§10 item 12). E6 reads `-1` as `Unknown`; E7's backfill writes through this writer, so it writes
    the same `-1`. The column DEFAULT `0` means nothing without a state (empty `authors_state` = not recorded), so a reader
    checks the state first.
  - New `src_mcp/src/Store/RoundAuthorsTable.cs` through `RoundsDb.Command()` (`src_mcp/src/Store/RoundsDb.cs:172-181`) — the
    **`RoundChangesTable` precedent** (`RoundChangesTable.cs:6-39`, step 22's writer): `RoundsDb.cs` (1 293 lines) gains ONE
    line, `RoundAuthorsTable.Record(this, roundId, round.Authors)` after `RoundChangesTable.Record` (`:200`), inside the
    transaction (`:196-201`). The table: when `round.Authors` is null, write NOTHING; else `UPDATE rounds SET authors_*,
    submodule_writes, uncovered_writes, ambiguous_* WHERE id = $round`, `DELETE FROM round_authors WHERE round_id = $round`, then
    one `INSERT` per row with its ordinal — the delete-then-insert of `RecordReviewers` (`RoundsDb.cs:928-953`).
    **`Record(RoundsDb db, long roundId, AuthorsSnapshot? authors)` opens NO transaction of its own** (E7 §10 item 2): it
    writes through `RoundsDb.Command()` only, so its statements join whatever transaction the caller holds — `RecordRound`'s
    here, E7.S3's own there — exactly as `RoundChangesTable.Record` does. A nested `BEGIN` would fail inside either caller
    (SQLite refuses a transaction within a transaction) and `Projection` would swallow it as a warning: the authors would
    simply be missing. The remarks say so, naming E7 as the second caller.
  - `src_mcp/tests/ThePairsThemselvesTests.cs`: its step-12 re-run drop list (`:529-554`) gains `DROP INDEX
    rounds_by_authors_session;` **first** — SQLite refuses `DROP COLUMN` on an indexed column — then `ALTER TABLE rounds DROP
    COLUMN` for the fourteen columns above, after `plan_source` (`:553`; `:552` is the `effort` drop — L5); `round_authors` and the index are `IF NOT EXISTS`, so
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
  - `TheWriterJoinsTheCallersTransaction`: `BEGIN` through `db.Command()`, `RoundAuthorsTable.Record` with an `observed`
    snapshot, then `ROLLBACK` → no `round_authors` row and every authors column back at its default; the same with `COMMIT` →
    the rows are there. Proves the writer opened none (E7 §10 item 2) — for a raw `BEGIN` only, which
    `Microsoft.Data.Sqlite` does not track, and which a writer using a `SAVEPOINT` would also pass (review point 8).
  - `AFaultAfterTheWriter_LeavesNoAuthors` (review point 8): the real caller's path — `RecordRound`, whose
    `SqliteTransaction` (`RoundsDb.cs:196`) is what E5's writer must join — with a fault planted AFTER
    `RoundAuthorsTable.Record`: a trigger `BEFORE INSERT ON findings … RAISE(ABORT, 'planted')` created through a second
    connection, and a round with one finding (`RecordFindings` runs after the writer, `:200-201`). `RecordRound` throws;
    then no `rounds` row, no `round_authors` row, and no authors column for the round — the writer left nothing behind. A
    writer that ended the caller's transaction (a raw `COMMIT` after its inserts) would leave its rows and the round's; one
    that opened its own `BEGIN` fails before the fault and is caught by the break-it below.
  - `AnOlderServersReSave_LeavesTheProjectedAuthorsInPlace` (§3; review point 5 — the earlier text stripped the file by hand
    and re-recorded through E5's own code, which proved nothing about an older server): build a step-24 file with an
    `observed` round through `Save` + `RecordRound`, then run on it exactly what an older server runs, which is data-driven
    and therefore the OLD code path: `SqliteMigrator.Migrate(conn, Schema.Steps[..23])` (the older binary's 23 steps;
    `src_mcp/storage/SqliteMigrator.cs:66-93` returns at once when `user_version ≥ steps.Count`, `:82`), and both preview forks,
    `ConsultantPreviewFork.Repair` and `SecurityPreviewFork.Repair` (`RoundsDb.cs:168-169`), on the step-24 file; and a
    re-record of the round with `Authors == null` through `RecordRound`, whose upsert E5 does not widen (the brief) and is
    therefore the older server's SQL. Assert `PRAGMA user_version` = 24, `table_info` of `rounds` and `round_authors`
    unchanged, and `SELECT *` of `round_authors` and of the authors columns byte-equal to before. The file's strip is the
    released binary's and is measured once (S1), not simulated here.
  - `AnUnknownTotal_IsMinusOneNeverZero`: an `unobserved` snapshot writes `-1` in all seven `ambiguous_*` columns, an
    `observed` one with a zero-agent ambiguous total writes `0` — the two never coincide (E6 D17); an
    `AuthorsSnapshot.Unknown(...)` snapshot writes `-1` in `submodule_writes` and `uncovered_writes` too, and so does an
    `unobserved` snapshot E4 built (its `UnobservedLaunches` is `-1` as well — M1).
  - `AgentKeyOf_IsTheInverseOfAgentColumn`: for `AgentKey.Main` and two subagent ids, `AgentKeyOf(AgentColumn(k)) == k`,
    and `AgentKeyOf("")` is main.
  - `TheTwoVersionsAreStored`: `authors_reader` = the snapshot's `Reader`, `authors_attribution` = its `Attribution`.
  - Break-it (rebuild): make the table read a `RoundContext` field instead of `round.Authors` (the shape the decision forbids)
    → the rebuild test goes red with zero rows; restore. Break-it (replay): drop the `DELETE` → recorded-twice doubles the rows.
    Break-it (drop list): leave out the `DROP INDEX` → `ThePairsThemselvesTests` red on the `DROP COLUMN`. Break-it
    (transaction): give `Record` its own `BEGIN`/`COMMIT` → `TheWriterJoinsTheCallersTransaction` red (and `RecordRound`'s
    projection warns instead of writing). Break-it (early commit): end `Record` with a raw `COMMIT` through
    `db.Command()` → `AFaultAfterTheWriter_LeavesNoAuthors` red with the rows left behind. Break-it (mapping): write `agent.Id`
    unmapped → main's row reads `main`, red.
- **Done when.** Tests green; `TheRoundKeyIsStepTwentyOneTests`, `TheRoundsSizeIsStepTwentyTwoTests`,
  `ThePlanSourceIsStepTwentyThreeTests`, `ThePairsThemselvesTests`, `RoundsDbTests` and `RoundCountsCliScenarioTests` green, the
  first three unchanged; `research/module_server.md` gains a **Schema step 24** paragraph after step 23's (`:3471-3481`),
  which states §3's older-server limit in one sentence.
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
  - `ARoundAfterTheStep_CarriesItsState` over one `observed`, one `partial: read budget`, one `partial: peer running` (E4 G3)
    and one `unobserved: transcript not found` round; `ARoundBeforeTheStep_CarriesNone` (a row with `''`: no `authorsState` key).
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

- **Goal.** A finished plan or code round reads its caller's transcript, attributes it, and stores the snapshot — within 2 s
  for the whole step and 64 MiB of the `fromUtc` SLICE (parent §16 D7; E3 §7 F1), never failing, never slowing a round by more than the budget, and never
  reading a developer's own `~/.claude` from the test suite.
- **Scope.**
  - New `src_mcp/src/Server/Rounds/RoundAuthorsStep.cs` (a stateful service, a class): `For(PersistedSession, RoundRecord) →
    AuthorsSnapshot?` — null for a stage outside §3's two, and for a round that is not SETTLED (status `done`, verdict not
    `skipped` — E4 G6; `Assign` is never called for anything else); else, in this order:
    1. **Locate** (E3.S2): `CallerTranscriptLocator.Locate(vendor, _env, home)` with the vendor from the round's `Caller` and
       `_env` the step's INJECTED `Func<string, string?>` (review point 2) — never a hard-wired
       `Environment.GetEnvironmentVariable`. A `NotObservable` location ends the step: `AuthorsSnapshot.Unknown(…,
       "unobserved", [its reason])` (`no session id`, `stated id`, `transcript not found`, `transcript in two folders`,
       `no reader for <vendor>`), no peer query.
    2. **The peers and the gate's own timeline** (§3; one read-only connection `Mode=ReadOnly;Pooling=False;Default
       Timeout=1`, the `src_mcp/src/Store/GateHistoryQuery.cs:77` shape — that one waits 5 s, this one 1 s — on EVERY round).
       The lookup, the earlier-caller read of §3 and the build of each peer's `GateTimeline` from its session file are ONE
       unit of their own: `AuthorPeers.Find(connection, load, deadline)` (`src_mcp/src/Server/Rounds/AuthorPeers.cs`) — the
       read-only connection; a `load` delegate whose default is `SessionStore.Load` (`src_mcp/src/Server/SessionStore.cs:461`;
       the store is sealed with a non-virtual `Load`, so the delegate is the seam a test spies on); and a `deadline`
       (`DateTimeOffset?`, null = unbounded, which is what E7 passes — M5) checked before each peer file is loaded. Each
       peer timeline is built by E4.S5's `AttributionMapping.Timeline` (B1), so `CompletedUtc` is filled ONLY for a `done`
       round (§3, E4 G1), and E7.S2 hands E4 the same peers by calling `Find` rather than by a second query (E7 §10 item 5;
       §10 item 8). The gate's own timeline is `AttributionMapping.Timeline(session.State.SessionId, RepoPath,
       session.OpenedUtc, [.. session.Rounds, record], callerOf)` — the record appended (M4, §3), the earlier rounds'
       callers from §3's order. A peer whose only same-caller round is still running is not in the store and not found
       (M3, §3).
    3. `RoundAttribution.ReadFrom(input without the observation)` → E3's `fromUtc` (`DateTimeOffset?` → `UtcDateTime`).
    4. E3's read with `ReadBudget(64 MiB, time left, clock)` over that `fromUtc`.
    5. **`AttributionMapping.Observation(observedSession)`** and **`AttributionMapping.Universe(gate, peers, observation)`**
       (E4.S5's, B1 — this step writes NO mapping of its own): E4.S1's *Mapping from E3* as E4 states it — `FirstLineAt =
       LaunchedUtc ?? CalledUtc`, `TokenTerms` term by term, `AnsweredAt` from E3's `ObservedWrite.AnsweredUtc` (E4 G3's
       evidence horizon), instants through `UtcInstant.From`, every E3 reason as a `StateReason` with `At = null` EXCEPT the
       two transcript-missing codes, which ride on the launches (E4 G5; tested in E4.S5, L8) — and the universe of E4.S2
       decision 6 (the gate's and every peer's `repo_path` ∪ every `WriteTarget.Where` root E3 resolved), each root in the
       spelling it arrives in: no WSL ↔ Windows translation (L6; E4.S1's `RepositoryOf` folds case and separators).
    6. `RoundAttribution.Assign` (through the `assign` delegate — M5) → `RoundAuthorsBuilder.Build` → `AuthorsSnapshot.From(authors, idSource, reader:
       ObservedSession.ReaderVersion, attribution: RoundAttribution.Version, …)` — `Windows`, `PeerGates` and the
       `partial: peer running` reason are taken as E4 returns them and never repaired here (§3). When E4's result is
       `unobserved`, the two write counts are `-1` (M1, §3). From `Build`'s `WritesOutsideTheGateCheckout`
       the step derives `UncoveredWrites` (every call listed) and `SubmoduleWrites` (the calls whose root, relative to
       `repo_path`, is a declared submodule path of the checkout's `.gitmodules`) — read once per round by the SHIPPED
       parser `CoaiMcp.Runners.Git.GitModules.In(repo_path)` (`src_mcp/runners/Git/GitModules.cs:24-35`, "parsed rather
       than asked of git" because the worktree populator is on the round's critical path too), so **no process is launched
       on the answer path**; an absent or unreadable file counts none as submodule and says so at Debug. No root is stored
       (gate finding 0).

    The attributed round's `CallerSessionId` is the id E3's ladder resolved; earlier rounds' come from `authors_session`, else a
    UUID `rounds.caller`, else `""`. An `ArgumentException` from `Assign` (its `Validate()` refused the input) becomes
    `unobserved: invalid input` — a reason this story adds to its own vocabulary (`AuthorsSnapshot.InvalidInput`, S1),
    confirmed by E4 (§S2.2a row *§9 F3*: a duplicated journal round is refused, never merged; E4 doc `:225`, `:615`). Every
    other exception is caught, logged as a warning with its type (never a transcript fragment), and becomes
    `unobserved: reader failed` (`AuthorsSnapshot.ReaderFailed`). Both are built by `AuthorsSnapshot.Unknown` (S1).
  - **The seams** (review point 3; completed 2026-10-17, M5): the step's constructor takes, beside the `TimeProvider`, the
    `env` delegate (step 1), the peer source (a delegate whose default is `AuthorPeers.Find`, step 2), the reader (a
    delegate whose default is E3's static `CallerTranscript.Read`, step 4), the **`assign` delegate** (default
    `RoundAttribution.Assign`, step 6 — the "spy on `Assign`" of the tests below is this delegate, never a hook on the
    static) and an **`AuthorsBudget`** (default `AuthorsBudget.Default`; the 1 KiB test passes its own instance, never a
    changed constant); `AuthorPeers.Find` takes the `load` delegate and the `deadline` (step 2). Production passes the
    statics; the tests pass spies, a reader that throws, and a reader that records its `fromUtc`. No interface is added
    for one implementation.
  - The budget as one named value, `AuthorsBudget.Default = (2 s, 64 MiB)`, with a remark citing
    `research/RESULTS_caller_transcripts_on_both_sides.md` Q6 (Node: a day-gated parse of the largest session, p50 19.9 s) and
    the C# measurement this story makes over E1's largest fixture slice BEFORE it fixes the value — prediction first
    (`measurement.md` rule 4), observation in the story's summary. The clock is a `TimeProvider` argument. Hitting either limit
    → `partial: read budget`, the rows read so far kept (E6 decides what a partial round counts for). **The 2 s is ONE deadline
    for the whole `For`**, taken from the `TimeProvider` when `For` starts and passed down: E3's `ReadBudget.MaxTime` is the time
    left at the reader's start, and the peer query, the `.gitmodules` parse, E4's attribution and the snapshot construction
    each check the same deadline between phases. **Inside** the peer phase the deadline is checked before EACH peer session
    file is loaded (the files are small — the largest is 190 KB, parent §14 — so one load cannot run long), and the query
    itself runs with `Default Timeout=1`; no phase starts a process, so nothing can outlive the deadline; a deadline missed after the read → `partial: read budget` with what is built
    so far — and when attribution never ran, that is `AuthorsSnapshot.Unknown(…, "partial", ["read budget"])`: no rows, every
    count `-1`, never a measured zero. The 64 MiB stays the reader's own byte limit, and **E3 charges it on the
    SLICE** — `ReadBudget.MaxBytes` against the bytes its byte gate YIELDS (`ScanMeter.BytesYielded`, reported as
    `Diagnostics.BytesInSlice`), never against the bytes it skipped — while `MaxTime` runs over the WHOLE read, skipped
    bytes included (E3 §7 F1, the E3.S4 amendment of 2026-10-16). A long orchestrator session therefore meets the byte
    limit only when its slice is large, and the 2 s whenever its scan is slow.
  - **Diagnostics are never states.** E3's `ReadDiagnostics` counts — `BytesInSlice`, `MetasWithoutModel`, `ValuesOffShape`,
    `MessagesDroppedAtBudget` and the rest — are logged by this step at Debug, as numbers, beside the elapsed milliseconds;
    none becomes a reason, a column or a snapshot member (E3 S4-17; E4 §S2.2a row *§9 F4*). `partial: read budget` is the
    only trace a budget stop leaves in the state.
  - The transcripts root: E3.S2's `ClaudeHome.Default.With(_settings.AgentLogDir)` — the expression `WhatTheCallerWasDoing`
    spells today as a ternary (`RoundEngine.cs:902`), which becomes `….ProjectsDir` of the same value, so the default is written
    once.
  - Call sites only: after the `with` block (`RoundEngine.cs:485-496`), one statement `record = record with { Authors =
    _authors.For(session, record) };` — before `:573` reads the record's cost and before the save (`:591`); the round's
    `StartedUtc` is set by `live.Finish`. The engine's constructor (`RoundEngine.cs:23-39`) gains one parameter;
    `PanelService.cs:134-136` passes it. `PanelService`'s constructor (`:50-56`) gains ONE optional parameter,
    `Func<string, string?>? environment = null` (null → `Environment.GetEnvironmentVariable`), handed to the step — the
    shape `PanelService` already uses for its other environment readers (`:98`, `:102`, `:107`) and
    `CallerIdentity.From(Func<string, string?>)` (`src_mcp/src/Server/CallerSessions.cs:53`); none of the 27 existing
    `new PanelService(` call sites in `src_mcp/tests` changes.
  - **The suite is not the developer's session** (review point 2). The protection is the HOME: E3.S2's test-home fixture
    (parent §16 D17) points `ClaudeHome.Default` — and `AgentLog`'s default with it — at an empty temp directory, so no test
    can read a developer's `~/.claude` whatever session id it sees. The identity is the injected `env`: a test that wants a
    state passes its own, and NO test of this epic sets a caller-identity variable on the process. The 2026-10-16 text
    cleared the variables in an assembly fixture and then SET `CLAUDE_CODE_SESSION_ID` in the guard test; both are dropped —
    the suite already sets `COAI_CALLER_SESSION` and `CLAUDE_CODE_SESSION_ID` process-wide mid-run
    (`src_mcp/tests/FakeCliRoundTests.cs:46`, `SplitOrderTests.cs:172`, `ConsultScenarioTests.cs:497`), so a once-cleared
    process is not a cleared process, and a process-wide set races every parallel test that reads it.
- **The brief.** `RoundEngine.cs` (915 lines) and `PanelService.cs` (1 915) gain call sites only, plus the one optional
  constructor parameter above. The step runs synchronously on
  the answer path, after the reviewers have answered and before the save — measured, not assumed: log the elapsed milliseconds
  at Debug. No text from a transcript in any log line. `Outcome`-style results, complexity ≤ 4.
- **Tests (RED first; unit tests over the step with E1 fixtures in a temp home, plus two through `PanelService`):**
  - Fixture session with a Sonnet writer into the repo → `observed`, rows with `who`/`model`/`association` from E4, the id
    source recorded, `authors_reader` = `ObservedSession.ReaderVersion`, `authors_attribution` = `RoundAttribution.Version`,
    and the snapshot's `Windows` and `PeerGates` as E4 returned them.
  - Persistence off / no transcript → `unobserved: transcript not found`; no session id → `unobserved: no session id`; a stated
    id alone → `unobserved: stated id` (parent §16 D9); a codex caller → `unobserved: no reader for codex`; none of them opens the
    store (a spy on the peer query); `caller_model` untouched in every case; each is an `AuthorsSnapshot.Unknown` snapshot
    (`SubmoduleWrites`, `UncoveredWrites`, `UnobservedLaunches` = `-1`), and so are `invalid input`, `reader failed` and a
    deadline missed before attribution (review point 4).
  - **Identity through the injected `env` only**: the step handed `_ => null` records `unobserved: no session id` whatever the
    test process holds (run inside Claude Code, it holds a real `CLAUDE_CODE_SESSION_ID` — the case the guard is for); handed
    a real-looking id and the empty test home, it records `unobserved: transcript not found` and the reader spy is never
    called. Neither test touches the process environment.
  - An `AuthorsBudget(1 KiB, 2 s)` passed to the step (M5) over a 2 MB fixture whose `fromUtc` slice is larger than 1 KiB → `partial: read budget`; the same
    budget with `fromUtc` past all but a slice SMALLER than 1 KiB → `observed` (the byte limit is the slice's, E3 F1 — the
    step passes the bound, it does not re-charge the file); a `FakeTimeProvider` advanced past 2 s mid-read → the
    same `partial: read budget`; the fake clock advanced inside the peer-query phase (not the reader) → `For` stops there and reports `partial: read
    budget`; with three peers and the clock advanced past the deadline while the FIRST peer file loads → the second and
    third are never loaded (a spy on `AuthorPeers`' `load` delegate) and the state is `partial: read budget`.
  - A reader that throws → `unobserved: reader failed`, the round still answers and is saved (through `PanelService`, FakeCli).
  - Any round (not only the first) with a peer gate session of the same caller in the store → E4 is handed that peer, built
    from the peer's session file (its `OpenedUtc` from the file even when `sessions.opened_utc` differs); no database, or a
    step-23 database (no column) → `PeersKnown = false` → `partial: prior windows unknown`, not an error. The peer of every
    such test holds a projected `done` or skipped round (M3); `ARunningOnlyPeer_IsNotFound_AndTheLimitIsStated`: a peer
    whose only same-caller round is `running` in its file and absent from `rounds` → not in `Peers`, not in `PeerGates`,
    the round `observed` — the limit §3 states, repaired by E7's grown-peer-set rule once that round is projected.
  - `TheGateTimelineHoldsTheRoundBeingAttributed` (M4): the `GateTimeline` handed to E4 ends with `(record.Stage,
    record.Number)` although `session.Rounds` does not hold it; without the append `Assign` would refuse (`invalid input`).
  - `AnEarlierRoundsCaller_ComesFromItsSnapshotThenTheStore` (M4): a session file whose round 1 carries a snapshot with
    `Session = c1` and whose round 2 has none while `rounds.authors_session = c2` for it, and a round 3 with neither and a
    UUID `rounds.caller` → `c1`, `c2`, that UUID; a `repo:` caller → `""`; the store locked → round 1 still `c1`, the rest
    `""` and `PeersKnown = false`.
  - `ARunningPeersStamp_IsNotItsCompletion` (E4 G1): a peer file whose last round is `running` WITH a `CompletedUtc` stamp
    (as `LiveRound.cs:256`/`:276` writes it) → the `GateRound` handed to E4 has `CompletedUtc == null`; a `done` peer round
    keeps its stamp.
  - `APeerRunningRound_IsStoredAsItIs` (E4 G3, the persisted interleaving): a same-caller peer round `running` across this
    round's window → the snapshot and `authors_state` carry `partial: peer running`, and the peer's session file and its
    projected rows are byte-equal before and after (E5 writes no other gate's rows).
  - `AWriteAnsweredAfterTheCompletion_ReachesE4WithItsAnsweredAt` (E4 G3, the late result): the mapping fills `AnsweredAt`
    from E3's `AnsweredUtc` for every write; the decision is E4's and is not re-tested here.
  - `AFailedProjection_KeepsTheSnapshotInTheFile` (E4 G3 (iii)): with `coai.db` locked so `Projection.Write` warns, the round
    answers, and the session file holds the snapshot with its `PeerGates`.
  - A round that is not settled is never handed to `Assign` (the `assign` delegate records no call — M5): the step returns
    null for it.
  - `ReadFrom` is called before the read and its bound reaches E3's `fromUtc` (a spy on the reader's argument).
  - An input `Validate()` refuses (a caller mismatch planted in the session file) → `unobserved: invalid input`; the round still
    answers and is saved.
  - A feature round and a skipped round → `Authors == null`.
  - A fixture write re-pointed into a registered submodule of the temp repository and one into a nested worktree →
    `SubmoduleWrites = 1`, `UncoveredWrites = 2` on the snapshot and the columns; the rows still `wrote here`.
  - The suite guard: a `PanelService` round built with `environment: _ => null` records `unobserved: no session id`; one
    built with a real-looking `CLAUDE_CODE_SESSION_ID` in its `environment` (never on the process) records
    `unobserved: transcript not found` against the test home — both opened with `OpenAsync(…, client: "claude-code")`,
    because the round's VENDOR comes from `CallerIdentity.Current()` at `open` (`PanelService.cs:509`), and a test process
    holding only `CODEX_SESSION_ID` would otherwise record `unobserved: no reader for codex` (L7) — and the whole suite's
    wall time is within 5 % of the run before this story (measured both, numbers in the summary).
  - Break-its: move the statement after `_store.Save` → the session-file assertion goes red (the snapshot only in the
    database); remove the `catch` → the throwing-reader round fails; remove the byte check → the budget test reads the whole
    file; let a `NotObservable` location fall through to the peer query → the spy test goes red; read the identity from
    `Environment.GetEnvironmentVariable` instead of `_env` → the `_ => null` test goes red when the suite runs inside a
    Claude Code session (and the summary says whether it ran inside one); map `CompletedUtc` without the status check →
    `ARunningPeersStamp_IsNotItsCompletion` red.
- **Done when.** Tests green; the C# read-time measurement and the suite-time measurement in the summary; no existing round test
  changed its assertions.
- **Depends on.** E5.S1, E5.S2 (the column the lookup reads); E3.S2 + E3.S4 (locator, reader, states, seams, and
  `ObservedWrite.AnsweredUtc`, which E4 §11 asks of E3); E4.S1–E4.S3 (records, attribution of one round, `Windows`,
  `PeerGates`, `RoundAttribution.Version`); E1.S4 + E1.S5 (merged).
- **Model.** Judge: opus → Coder: sonnet → Judge: opus → Coder: sonnet.

### E5.S5 — End to end: text-free, rebuilt, replayed

- **Goal.** One real round through `PanelService` + FakeCli proves the whole chain: the snapshot is in the session file and the
  database, no sentinel leaves the reader, a deleted database comes back with the same authors, and a replay adds nothing.
- **Scope.** A test class in the `ARoundIsMintedAKeyTests` shape (`src_mcp/tests/ARoundIsMintedAKeyTests.cs`,
  `[Collection("fakecli-env")]` at `:23`, temp repo + data dir): one E1 case copied from
  `src_mcp/tests/fixtures/caller-transcripts/<case>/projects/` into a temp projects root as `<slug>/<sessionId>.jsonl` (+ the
  `<sessionId>/` subagents folder), its `manifest.json` `fixtureRoots` re-pointed at the temp repository and its instants
  shifted by ONE offset (the fixtures sit at `2000-01-01`) so the writes fall between the plan round's start and the code
  round's start (fixture derived from now — `testing.md` § *A test that reads the clock expires*); the session id handed
  through `PanelService(environment: …)` as `CLAUDE_CODE_SESSION_ID` (M8) — NEVER through `COAI_CALLER_SESSION`, which E3's
  `Locate` reads as `stated id` (E3 `:527`) and which would fail the positive control, and never on the process (S4); a
  `ListSink` (`src_mcp/tests/RoundAuditTests.cs:16`) on the logger. No production code.
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
    assertion in code or tests. No snapshot, or a state `IsWellFormed`'s rule refuses (S1's whole rule, restated for
    TypeScript as `reviewedFrom` restates `PlanSource.IsWellFormed`, and held to it by the same
    `shared/authors-state-vectors.json` — review point 7) → `''`; `unobserved` → `authors not observed —
    <reasons>`; else main = the distinct displayed models of `who = main` rows; coders = the DISTINCT `agent` ids of subagent
    rows with association `wrote here` (a straddler once), grouped by family through `shared/model-display-vectors.json` —
    the noun is *coder* when the TOTAL is 1 and *coders* otherwise, in every form below (*1 Sonnet coder*, *3 Sonnet
    coders*, *1 Sonnet + 1 Opus coders*), a test for the total of 1; one family → *N Family coder(s)* by that rule; two → *2 Sonnet + 1 Opus coders* (larger group first, ties by family name); more than two →
    *N coders (mixed)*; none → *main only* (parent §8, gate finding 2); both windows counted (§3); size = the `delta` scope when
    its `state` is `recorded`, else the `whole` scope when its state is, else no size clause — lines of `shown` + `elided`, files
    of `shown` + `elided` + `binary` (`added`/`removed` absent in a binary cell, never 0); `partial` appends *· partial —
    <reasons>* (*· partial — peer running* for a provisional round, E4 G3); `uncoveredWrites > 0` appends *· size not
    covered*, and its `-1` (unknown, S1's `Unknown`) appends nothing — such a round is already `partial` or `unobserved` on
    the card. The card reads the round roll-up only, never `Windows` or `PeerGates`.
  - The model display: `claude-<family>-<major>-<minor>[-<date>]` → *Family major.minor*; anything else verbatim, never
    guessed. Its cases live in a new `shared/model-display-vectors.json` (the `shared/path-family-vectors.json` precedent, read
    by `src_vs_code/src/test/pathFamily.test.ts` and `src_mcp/tests/SharedPathVectors.cs`) so E6's C# family function reads the
    same file rather than retyping it (D19). **Each vector carries the FAMILY too** (E6 §4 item 5, D4): `{ model, display,
    family }`, with `family` = the lower-case `<family>` for a Claude id, `other` for any id the rule does not parse, and
    `unknown` for `""` and `unknown` (E4.S3's launch-only row) — so the card's grouping and E6's coder mix read one answer.
    The card groups coders by that `family` and nothing else — a Claude family by its capitalised word (*Sonnet*), `other`
    as *other*, `unknown` as *unknown* (*1 Sonnet + 1 unknown coders*) — the groups E6 counts, so the card invents none.
    A test in `rounds.test.ts` covers an `other` and an `unknown` coder.
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
  - Every vector of `shared/model-display-vectors.json` through the display function, and every vector of
    `shared/authors-state-vectors.json` through the TypeScript well-formedness rule (the C# half is S1's); a
    `partial: peer running` round and an `uncoveredWrites: -1` round in `rounds.test.ts`.
  - `bundledPage.test.ts`, the running page (the `openedRows` pattern of the `reviewedFrom` tests, `:1215-1257`, over a
    `SESSION`-shaped fixture, `src_vs_code/src/test/roundsLogPageHarness.ts:49-58`): opening an observed, a partial and an
    unobserved round shows its `reviewer authors` element; a round without `authors` shows none and still shows `asked by`.
  - Break-it: delete the `writtenWithHtml` term in `detail` → the page test goes red while every unit test stays green (the
    reason the page test exists, `.agents/PROJECT.md` § *A webview page is tested by RUNNING it*).
- **Done when.** `npm test` green, the bundled-page suite included; the page bundle builds.
- **Depends on.** E5.S1 (the JSON shape). E2's `Size` has merged.
- **Model.** Judge: opus → Coder: sonnet → Judge: opus → Coder: sonnet.

## 6. Definition of Done (the epic)

- [ ] A settled plan or code round finished after E5 carries `authors` in its session file and the same in `coai.db`; a
      skipped, interrupted, running, feature or document round and every older round read *not recorded* — live and in E7's
      backfill (M6).
- [ ] A database at step 21, 22 or 23 opens at 24; read-only `--log` and `--round-counts` on a step-23 file answer and leave it
      byte for byte; both preview forks reach the head; `ThePairsThemselvesTests`' drop list carries step 24.
- [ ] Deleting `coai.db` and re-projecting the session file gives back the same authors (S2, S5); recording a round twice adds
      nothing.
- [ ] What an older server runs on a step-24 file — its 23-step migrator, both preview forks and its unwidened upsert —
      leaves `user_version` 24 and the projected authors byte-equal (S2); the 0.58.0 run of §3 *Measured, not assumed* is
      in S1's summary; the limit §3 states (repair by E7 per §10 item 11) is in `research/module_server.md`'s step-24 paragraph.
- [ ] `RoundAuthorsTable.Record` opens no transaction and leaves nothing behind a fault after it (S2's two tests); `main` →
      `""` and its one inverse `AgentKeyOf` live in that file only; every unknown total and count is `-1` in the snapshot
      and the columns, and every snapshot E5 builds without E4 comes from `AuthorsSnapshot.Unknown`.
- [ ] The snapshot carries every text-free member of E4's `RoundAuthors` (`Stage`, `Number`, `Windows`, `PeerGates`
      included — the reflection fact), plus `IdSource`, `Reader`, `Attribution` and the two write counts; step 24 stores
      both versions (`authors_reader`, `authors_attribution`); `Assign` is called for settled rounds only; a running peer's
      stamp is never its completion; `partial: peer running` is stored as E4 returns it, and no other gate's rows are written.
- [ ] (2026-10-17) S4 maps through E4.S5's `AttributionMapping` only (B1); the gate timeline handed to E4 holds the round
      being attributed (M4); `Unknown(...)` carries `Reader = 0` / `Attribution = 0` for the component that did not run
      (H2); `-1` for the write counts of an `unobserved` result (M1); `authors_id_source` admits `healed` (H1); the seams
      of S4 are the five delegates/parameters named there (M5).
- [ ] `IsWellFormed` is one written rule, and `shared/authors-state-vectors.json` holds C# and TypeScript to it.
- [ ] The budget path records `partial: read budget`; a failing reader never fails a round; the suite reads no developer's
      transcript, no test sets a caller-identity variable on the process for this epic (the step's `env` is injected), and
      the suite's wall time moved < 5 %.
- [ ] No sentinel in the session file, `--log`, the log, or the database outside `rounds.agent_log`.
- [ ] The card line shows for observed / partial / unobserved and not for a round without a snapshot, on the running page.
- [ ] `CoaiMcp.Tests.exe` and `npm test` green; `research/module_server.md` records step 24 after step 23 and the `--log`
      member after the plan-source one; `research/architecture.md` § *A field added to the round list* gains the field;
      `module_extension.md` the card line (the parent's DoD keeps the full write-up for E7).

## 7. Parent-plan defects found while writing this (not fixed here)

> **Settled 2026-10-14** in the parent's §16: 1 and 7 → D13; 3 and 11 → D9; 4 → D4; 6 → D15; 8 → D1; 9 → D14; 12 → D17
> (the test home now lives in E3.S2 and covers `AgentLog`); 14 → D19; 2, 5, 10 and 13 accepted as resolved here. Items 15–19
> were added on 2026-10-16 and are not settled by the parent (17 is fixed in this document's commit; 19 is closed for E5 on
> 2026-10-17 by E4's `RoundAttribution.Version`). Items 20–21 were added on 2026-10-17.

1. **"No sentinel in the `.db` bytes" cannot hold**: `rounds.agent_log` keeps transcript TEXT by design (`AgentLog.cs:238-294`) — S5 blanks it first and uses it as the positive control.
2. **The card line's scope was undefined** — §3: this round's windows, a `recorded` `delta` else `whole`, `shown` + `elided` lines.
3. **The id source is required (§5) but had no column** — `authors_id_source`.
4. **`write_calls` (a count) vs "a boolean per (agent, repository)"** — E5 stores what E4 hands over.
5. **`before-open` needs a cross-gate-session lookup with no named source** — the wiring supplies it (§3, `AuthorPeers`).
6. **No §14 growth row for `RoundRecord.Authors`** — ≈ 1.2–4 KB a round, ≈ 22–75 MB a year a side; every progress-tick save rewrites the file.
7. **`open`/`status` would send the snapshot to the calling AI** (`PanelService.cs:1878-1894`) — stripped (S1).
8. **Skipped rounds and the feature/document stages** were not placed — §3 *Which rounds* (not recorded; E4 D1 for the window).
9. **The backfill upserts only the database** — it must write the file's snapshot too (E7, parent D14).
10. **No rounds re-projection exists** (`PanelService.cs:164,176` re-project consultations only) — "rebuild" is delete-and-write, which E5's tests simulate.
11. **§4's id fallback order is wrong** (`CallerSessions.cs:45-51`; `RoundCommands.cs:34-35` falls back to `repo:<path>`) — E3's.
12. **Tests already read the developer's real transcripts** (`RoundEngine.cs:902`) — the test home, parent D17.
13. **E1's fixtures** needed re-pointable paths and shiftable instants — closed by E1 as shipped (#828).
14. **One model-family rule is needed on both sides** — S6's shared vectors file.
15. **The parent's E5 anchors have drifted** with steps 22–23: `RoundEngine.cs:586` (the save) is `:591`; "call sites only in
    `RoundEngine.cs:605-625`" is `:612-630` and the call site that matters is the record at `:485-496`; `RoundsDb.cs:923-945`
    is `:928-953`; the card anchors `rounds.ts:156` and `roundsLog.ts:519` are `rounds.ts:168` (`calledBy`) / `:225`
    (`reviewedFrom`) and `roundsLog.ts:541`; `PanelService.cs:159` is `:164`.
16. **"The ambiguous totals" has no list** — E4.S3's `ActivityTotals` carries an `Agents` count beside the five E5 named; §3
    adds `ambiguous_agents`, and keeps `Elsewhere`, `ClaimedByPeer` and `UnobservedLaunches` in the snapshot only.
17. **`todo/README.md`'s E5 row still said "schema step 23"** (`todo/README.md:21`); the parent and this document say 24.
    *Fixed 2026-10-16* in the same commit as §10.
18. **No session type carries `JsonExtensionData`, so every field added to `RoundRecord` or `PersistedSession` is dropped by
    any older server that re-saves the file** (§3) — `Caller`, `Size`, `Gitlinks`, `PlanSource`, and now `Authors`. A forward
    guard (`[JsonExtensionData]` on both, stripped from the `open`/`status` answer, with a round-trip test) would protect
    every field added AFTER the release that carries it; it cannot protect E5's own field from the binaries already out.
    It is a change to the two types every stage saves — the main session's to schedule, not this epic's.
    *Update 2026-10-19:* `research/PLAN_a_session_file_keeps_what_a_newer_server_wrote.md` (a file of PR #868's branch, so not linked: the link would not resolve on this branch) is that change, open as
    PR #868 and NOT on `main` yet. It adds a §8 to THIS document — a boundary table between the guard and E5 — which is not carried here:
    when `main` brings #868 in, keep its §8 (this document's own §8 is the E4 reconciliation, so one of the two takes a new number), and point this item at it.
    Until then the boundary is: E5 adds `Authors` as a KNOWN member and depends on no bag; the guard protects only servers released after it; whichever of the
    two lands second puts its strip of the `open`/`status` answer in the same method (`SessionAnswerFor`, where S1's strip of `Authors` already is).
19. **`authors_reader` versions E3's reader only** (E7 §10 item 3). E3's `ReaderVersion` is bumped by a COUNTING rule; an
    E4 attribution-rule change with no E3 change leaves it equal, and E7's "equal reader → keep" would keep a row an older
    rule attributed. E5 stores what parent §9 names (E3's constant); if E4 adds an attribution version, it needs its own
    column — cheapest in step 24 itself, so E4 must say before S2 is coded (§10 item 5). *Closed 2026-10-17 for E5:* E4
    defines `RoundAttribution.Version = 1` (E4 §S2.5, `7c3bf469`, bumped by any change to a rule of its §S2.3–§S2.4 or
    S3/S4); step 24 stores it as `authors_attribution` beside `authors_reader` (S2). The parent's §9 sentence still names
    one version and is the parent's to amend.
20. **The parent's §14 growth row for `RoundRecord.Authors`** (item 6) does not carry E4's §S2.2b additions: `Windows` and
    `PeerGates` add ≈ 300 B a round (E4 G3, G9) beside the ≈ 150 B of the three totals (§3) — ≈ 450 B, ≈ 9 MB a year a side.
    Not settled.
21. **The parent's §9 "never overwrites an `observed`/`partial` row produced by a newer or equal reader version"** now has
    two exceptions it does not state: E4 G3's `peer running` / grown-peer-set recompute, and the lost-snapshot repair of §10
    item 11. Both are E7's to build; the sentence is the parent's to amend. Not settled.

## 8. Open with E4 — where this document and E4's contract disagreed (RESOLVED 2026-10-16)

> **All seven resolved by Fable, 2026-10-16** (E4 doc §S2.2a, rows *E5 §8.1*–*§8.7*, `feat/round-authors-e4` `244d953c`);
> the full question-and-answer text is in that E4 section. Condensed 2026-10-17 to the decisions E5 carries.

| # | Question | Decided | Carried by |
|---|---|---|---|
| 1 | two `RoundAuthors`/`AuthorRow` types (E4's core, E5's `src/Store`) | `AuthorRow`, `ActivityTotals` (and `WindowAuthors`, E4 L1) are final as E4.S3 prints them; E5 wraps them unchanged in `AuthorsSnapshot`; the checkout roots stay in memory | S1 |
| 2 | `main` as `"main"` (E4) vs `""` (the column) | `MainId` stays `"main"` in the core; the ONE mapping is S2's `AgentColumn`, its ONE inverse `AgentKeyOf` beside it — E6 D18 and E7's heal call it (L3, 2026-10-17: the row used to name E6 D18 as the inverse) | S2 |
| 3 | `AgentKey.IsMain` would serialise into every row | E4.S1 puts `[JsonIgnore]` on it with its own fact; S1 keeps the file-shape test | S1 |
| 4 | the card consuming E4.S4's contributors | dropped from E4.S4's consumers; the card reads this round's stored rows | S6 |
| 5 | the peer lookup key | `authors_session`, else a UUID `rounds.caller` (E4 §S2.8) | §3, S4 |
| 6 | the state string | `Render()` joins with `", "`; no E3/E4/E5 reason contains it (E4's `NoReasonContainsTheJoiner`, S1's `E5sReasonsContainNoJoiner`) | S1 |
| 7 | E4's drifted anchors | corrected in E4's §8.1 | — |

## 9. Revised 2026-10-16 against `main` — what changed since 2026-10-14

Verified at `59463d73` (E1 #828, E2 #829, the plan-gate E1 #837 and E2 #841 merged); every `path:line` above was re-read
and the drifted anchors updated in place (the stories cite the current lines). What the revision changed: step 24 is index
23 / `user_version` 24 with no step test relaxed and `ThePairsThemselvesTests`' drop list extended (`DROP INDEX` first); the
writer follows `RoundChangesTable`, `--log` the shipped readers, the extension `reviewedFrom`; the snapshot is
`AuthorsSnapshot` in `core/Authors` wrapping E4's records; S4 follows E3's and E4's written contracts; the call site is one
statement AFTER the `with` block (the 2026-10-14 text put it inside the initializer, which does not compile); E1's fixtures
exist. *(Condensed 2026-10-17; the decisions live in §3 and the stories.)*

## 10. What E6 and E7 asked of E5 — answered 2026-10-16, amended 2026-10-17

E6 `3c50a78c` §4 and E7 `740d1a4f` §10 (*Open with E3–E5*). Each answer is in the story named; nothing here edits E6 or E7.

1. **An older server strips the snapshot** (E6 §4 item 8, E7 §10 item 1). §3's older-server bullet is the one place this is
   decided and specified (the database is the durable copy; what is lost; `JsonExtensionData` declined → §7 item 18; the
   0.58.0 run); E7's repair is item 11. E7's O1 version gate stays the operational guard — the writer that strips is the
   OLD binary, which E5 cannot reach.
2. **`-1` for unknown totals** (E6 §4 item 2). The contract E6 reads: S2's `-1` in every `ambiguous_*` term E4 left
   unknown, in the snapshot and the columns, never `0` (`AnUnknownTotal_IsMinusOneNeverZero`). E4.S3 kept `-1`
   (`ActivityTotals.Unknown`, final — §8 item 1), so no `Captured`-style type ships and D17 maps `-1` as written.
3. **The writer opens no transaction** (E7 §10 item 2). S2: `RoundAuthorsTable.Record(db, roundId, authors)` writes through
   `RoundsDb.Command()` only, joins the caller's transaction, and `TheWriterJoinsTheCallersTransaction` proves it by a
   rollback — and, since 2026-10-17, `AFaultAfterTheWriter_LeavesNoAuthors` through `RecordRound`'s own
   `SqliteTransaction` (review point 8), the shape E7.S3's caller will have too.
4. **Reasons containing `", "`** (E6 §4 item 3, E7 §10 item 6). None can: E4.S3's `NoReasonContainsTheJoiner` covers every
   E3 and E4 reason, S1's `E5sReasonsContainNoJoiner` covers E5's two, and `IsWellFormed` refuses a counted or empty reason.
   D18's split on `", "` is exact.
5. **`authors_reader` versions the reader, not the attribution** (E7 §10 item 3). E5 stores E3's `ReaderVersion`, as parent
   §9 names. An attribution version is E4's to define; if E4 defines one before S2 is coded, step 24 gains its column
   (§7 item 19). *Closed 2026-10-17:* E4 defines `RoundAttribution.Version = 1` (§S2.5, `7c3bf469`); step 24 stores it as
   `authors_attribution`, and the snapshot as `Attribution`. E7's "equal reader" compares both (E4 §11, E7 item 2).
6. **The family column in the vectors file** (E6 §4 item 5, D4). S6: every vector is `{ model, display, family }`, `other`
   for an id the rule does not parse, `unknown` for `""`/`unknown`; the card groups by that family only.
7. **One `"main"` ↔ `""` mapping** (E6 §4 item 4). S2's `AgentColumn` writes it. *Amended 2026-10-17* (review point 1):
   E7's heal from the database needs the inverse too, so the one inverse is S2's `RoundAuthorsTable.AgentKeyOf`, beside
   the mapping; **E6 D18 calls it** instead of spelling `"" → main` itself (a note owed to E6, item 13).
8. **Peers on every round, the same for the backfill** (E7 §10 item 5). S4's peer build is one unit,
   `AuthorPeers.Find(connection, load, deadline)` (deadline null for E7), which E7.S2 calls — **and which sets `PeersKnown = false`** when a peer's session file does not load, names another session or the deadline cuts the walk (E7 §10 item 8, a precondition of E7's rule 3; built and tested in S4, §12 deviation 4 for its exact signature); and the ONE
   `ObservedSession → AttributionInput` mapping is **E4.S5's `AttributionMapping`** (`Observation`, `Timeline`, `Universe`;
   B1, 2026-10-17), which S4 and E7.S2 call — E7.S2 stops reading `before-open` from a database join of its own (E7
   `:223-225`) and hands E4 what `Find` + `Timeline` give. Parity follows from there being one implementation of each.
9. **Step-24 names** (E6 §4 item 1). Unchanged by this revision; the shipped schema wins.
10. **The 60 s ceiling** (E7 §10 item 7) is E7's number; S4's 2 s is fixed only after its own C# measurement.

*Added 2026-10-17 — what E5 now owes E6 and E7 in turn (review points 1 and 4, E4 §11 Cross-epic). Notes only; nothing
here edits E6 or E7.*

11. **To E7 — the corrected `BackfillDecision`: inputs, rule order and the change list** (review point 1 with E4 G3; corrected
    2026-10-17 by the judge review — H1, H2, M2, M6, M7; written here so the E7 gate can apply it). E7.S1 at `740d1a4f`
    (`:167-174`) applies rule (2) *keep* before rule (3) *heal*, its heal copies from the database, and the first text of this
    item ran its (a) before (c), so a file with no snapshot was "recomputed as grown" and, with the transcript gone, the heal
    was never reached while `PeerGates = []` re-triggered (a) on every run (H1).

    **Inputs beyond `740d1a4f`'s `:161-164`.** `StoredRound` gains `AuthorsAttribution`; `SnapshotPresence` gains
    `AuthorsAttribution`, `PeerGates` and `IdSource` (a `healed` snapshot has an UNKNOWN peer set — never read as "no
    peers"); **`TranscriptPresent`** (E7 calls this input **`TranscriptLocated`**, to keep it apart from E3's per-agent `ObservedAgent.TranscriptPresent`; a name
    difference only — the same fact, the same use, and E5 builds nothing under either name) — `CallerTranscriptLocator.LocateStored(authors_session, else a UUID rounds.caller)`
    run BEFORE the decision (or a two-phase decision); **peer status** — for every gate in `PeerGates`, whether a round of
    it that overlaps this round's windows is still `running` (a round is FINAL when `done` or `interrupted`, E4 §S2.3 —
    an interrupted peer never becomes `done`, so waiting for "settled" would leave the row provisional for ever, M7), and
    whether the caller has a gate session holding a settled round that is NOT in `PeerGates`; the two version constants
    (`ObservedSession.ReaderVersion`, `RoundAttribution.Version`), read from the types.

    **Rules, first match** (authors; sizes stay rule (5), decided apart):
    1. `StartedUtc` before `--since` → not selected. Authors are decided for SETTLED `PlanReview`/`CodeReview` rounds only;
       a skipped, interrupted, running, feature or document round is not selected for authors and gets NO state (M6 — §3
       *Which rounds* is the one sentence; E7 `:179-182`'s "every selected round gets a state" is about CALLERS, not stages).
    2. **Transcript GONE.** The file holds a snapshot → `3a re-project` when the database's authors are empty or of an older
       reader/attribution, else `keep: transcript gone`. The file holds none → **`(c) heal`** from the database when its
       state is non-empty: rows through S2's ONE inverse `RoundAuthorsTable.AgentKeyOf` (never a second spelling of
       `"" → main`); state and reasons from `authors_state`, split on `": "` then `", "` (exact by item 4); `Session`,
       `Reader`, `Attribution`, the ambiguous totals and the two write counts from their columns; `Elsewhere`,
       `ClaimedByPeer` = `ActivityTotals.Unknown`, `UnobservedLaunches` = `-1`, `Windows` = `[]`, `PeerGates` = `[]`;
       **`IdSource = "healed"`** (so (a) never retries it); `Stage`/`Number` the round's own. Database state empty too →
       `(4) compute`, which ends `unobserved: transcript not found` through `LocateStored` (every selected round gets a
       state). A `peer running` row without a transcript stays provisional for ever — E7 states that limit.
    3. **Transcript PRESENT — `(a) recompute: provisional`**: the state's reasons include `peer running` and every
       overlapping round of every gate in `PeerGates` is final; or the caller has a settled gate session not in `PeerGates`
       and `IdSource ≠ healed` (a healed snapshot's peer set is unknown, not empty, and never triggers (a) — H1).
    4. **`(b) recompute: snapshot lost`**: `HasAuthors` is false (the file lost it to an older server) — whatever the
       database's versions say. "Equal rows" is NOT promised (M2): at equal versions the placed ids, their owners, the
       associations and the rows come back equal, but a straddling message's tokens may be higher (E4 decision 7, the
       stated limitation), `partial: read budget` may become `observed` (the live budget is 2 s, E7's 60 s, item 10), and the
       five never-projected members come back at all; **`authors_id_source` keeps the LIVE value** (`registry`/
       `environment`) — `stored` is written only where the row had none. When the recompute is WEAKER than the database's
       state (a transcript partly gone) → `(c) heal` instead, so the file gets its snapshot back (`healed`); never `keep:
       stronger existing` with the file still empty.
    5. **`(d) keep: equal or newer`**: `HasAuthors`, the database's state `observed` or `partial` — never `unobserved` (H2:
       its `Reader`/`Attribution` are `0` when the component did not run, S1, and a present transcript is worth a recompute
       even at current versions) — `authors_reader ≥ ReaderVersion` AND `authors_attribution ≥ RoundAttribution.Version`.
    6. **`3a re-project`**: the file's snapshot at both versions ≥ current, the database empty or older → rows FROM the
       snapshot, the transcript not read.
    7. **`(4) compute`**: empty, older, or `unobserved` → compute; written when its strength ≥ the existing one, else `keep:
       stronger existing`.
    8. **`(5) size`**, decided apart, unchanged.
    A second run then writes nothing (E7's idempotence fact): every path above ends in a row the same path reads as `keep`.

    **What E7 must change** (the judge's list of 2026-10-17, corrected by the decisions above):
    1. `StoredRound` gains `AuthorsAttribution`; `SnapshotPresence` gains `AuthorsAttribution`, `PeerGates` and `IdSource`.
    2. New inputs: `TranscriptPresent` (`LocateStored` before the decision, or a two-phase decision); peer status (no
       overlapping `running` round of a named peer — FINAL, not settled; a settled caller gate session not in `PeerGates`).
    3. The order above: `--since` → transcript gone (3a / keep / (c) heal / compute) → (a) → (b) → (d) keep only with
       `HasAuthors`, an `observed`/`partial` state and both versions ≥ current → 3a → (4) → (5).
    4. The heal: only without a transcript; `AgentKeyOf`; `Unknown` values for `Elsewhere`/`ClaimedByPeer`/
       `UnobservedLaunches`/`Windows`/`PeerGates`; `authors_state` split on `": "` and `", "`; `IdSource = "healed"`.
    5. When the (b) recompute is weaker → heal; keep the live `authors_id_source`.
    6. E7.S2 calls `AuthorPeers.Find(connection, load, deadline: null)` and E4.S5's `AttributionMapping` (B1), and stops
       reading `before-open` from a database join on caller/`repo_path` (`:223-225`). Never hand `Assign` an unsettled or
       non-recorded round; such rounds get no state (M6).
    7. Tests: the generated table adds both versions, `PeerGates`, `IdSource` and transcript presence;
       `AFilledRowWithoutItsSnapshot_IsHealedNotRecomputed` splits into a without-transcript case (healed) and a
       with-transcript case (recomputed, (b)); `ASnapshotLostToAConcurrentOpen_IsHealedOnTheNextRun` now recomputes while
       the transcript exists; new tests for `peer running` (final → recompute; still running → keep), a grown peer set, an
       interrupted peer, a healed snapshot that does not retrigger (a), an `unobserved` row at current versions (not kept),
       and a weaker (b) recompute (healed); `TheReaderVersionIsOnEveryWrittenRound` also covers `authors_attribution`.
    8. The E7.S1 goal sentence names the TWO exceptions to "never overwrite a newer or equal reader": a provisional row
       (`peer running` / a grown peer set) and a lost snapshot while the transcript exists.
12. **To E6 — a snapshot E5 builds itself carries `-1`, not `0`** (review point 4). `AuthorsSnapshot.Unknown` writes `-1`
    in `submodule_writes`, `uncovered_writes` (and in every `ambiguous_*`) for `unobserved` rounds and for a `partial: read
    budget` round whose attribution never ran. E6 D12's `uncovered_writes > 0` must treat `-1` as *unknown*, never as
    *covered*: a round with `-1` is neither in the "covered" count nor in the "not covered" one, and the report says how
    many such rounds it left out.
13. **To E6 — E4 §S2.2b's consequences as E5 stores them.** (i) Per-window state and totals (`Windows`, E4 G9) are in the
    SNAPSHOT only; step 24 projects the round roll-up. If D2/D3 must read the round window alone when `before-open` is left
    out, E6 either reads the session files or states the limit; a later step can project `Windows` from the files with no
    backfill. (ii) `partial: peer running` is stored as E4 returns it (provisional until E7's recompute, item 11): count it
    as `partial` (D3). (iii) D18 calls `RoundAuthorsTable.AgentKeyOf` (item 7). (iv) A healed round (item 11 c) has empty
    `Windows` and `-1` in the totals it never measured — the same `Unknown` reading as item 12.
14. **To E7 — the rest of E4's list as E5 provides it.** `AuthorPeers.Find` builds each peer through E4.S5's
    `AttributionMapping.Timeline`, so `GateRound.CompletedUtc` is filled only for `done` rounds (E4 G1), and it is the one
    peer build E7.S2 calls (item 8); the gate's own timeline is `Timeline(…, [.. rounds, record], callerOf)` with §3's
    earlier-caller order (M4); `Assign` sees settled rounds only and no other round gets a state (E4 G6, M6); both
    versions are columns (item 5); `authors_id_source` admits `healed` (H1); the writer leaves nothing behind a fault in its
    caller's transaction (item 3).

## 11. Revised 2026-10-17 — the independent review of `2af746b3` and E4's §S2.2b

Code re-read on `origin/main` `ce9cd298` (its `src_mcp` and `src_vs_code/src` are byte-identical to `59463d73`; the one
change since is `src_vs_code/CHANGELOG.md`). E4 read at `feat/round-authors-e4` `7c3bf469` (§S2.2b, §11 *Cross-epic*); E7
at `740d1a4f`. Nothing here edits E4, E6 or E7.

### 11.1 The review — eight points, all applied

| # | Point | Checked against | Applied as |
|---|---|---|---|
| 1 | §3 / §10 item 1 / DoD describe E7's repair wrongly: rule 5 is the size rule, the heal is rule 3 and copies FROM the database, rule 2 *keep* fires first, a database heal is a second inverse of `AgentColumn` and cannot restore the unprojected members | E7 `740d1a4f` `:167-174` — confirmed word for word | §3 *What is lost* restated; §10 item 11 states the rule order E7 must take, combined with E4 G3's exception; S2 adds the ONE inverse `AgentKeyOf`; the heal's unknown values are `AuthorsSnapshot.Unknown`'s; DoD line rewritten |
| 2 | the S4 suite guard sees the process's `CLAUDE_CODE_SESSION_ID` and races | `Locate` was written with `Environment.GetEnvironmentVariable`; the suite sets the identity variables mid-run (`FakeCliRoundTests.cs:46`, `SplitOrderTests.cs:172`, `ConsultScenarioTests.cs:497`) | the step takes an injected `env`; `PanelService` gains one optional `environment` parameter (the `:98`/`:102`/`:107` shape); the assembly-fixture clearing and the process-wide set are dropped; the home fixture is the protection |
| 3 | the S4 spies need seams: `AuthorPeers` and `CallerTranscript.Read` are static | `SessionStore` is `sealed` with a non-virtual `Load` (`SessionStore.cs:437`, `:461`) | delegate seams with the statics as defaults — peer source, reader, and `AuthorPeers`' `load` — named in S4's scope |
| 4 | snapshots E5 builds itself have no unknown values; `0` write counts read as "covered" | §3's four E5-built paths | `AuthorsSnapshot.Unknown(...)`, one factory (S1); `-1` in the two write counts and their columns (S2); tests in S1, S2, S4; §10 item 12 tells E6 |
| 5 | `AnOlderServersReSave_…` was tautological | `SqliteMigrator.Migrate` is data-driven (`SqliteMigrator.cs:66-93`, `:82`); the forks are `RoundsDb.cs:168-169`; S2's brief leaves the upsert unwidened | the test runs `Migrate(conn, Schema.Steps[..23])`, both forks and an `Authors == null` re-record on a step-24 file, and asserts `user_version` 24 and unchanged columns and rows; the file strip stays S1's real-binary measurement |
| 6 | the 0.58.0 run: `open` never opens `coai.db` and prunes the real repository | `PanelService.cs:474-515`, `PruneOursAsync` at `:490` | a throwaway repository; the summary records the paths the run exercised, and says when the database was not one of them |
| 7 | `IsWellFormed` did not exclude `×2` by its words, and TypeScript can drift | S1's rule text; S6's restatement | the whole rule written once (S1), with `peer running` admitted; `shared/authors-state-vectors.json` read by the C# and TypeScript tests |
| 8 | the rollback test used a raw `BEGIN`, not the callers' `SqliteTransaction` | `RecordRound` `RoundsDb.cs:196-202`; `RecordFindings` runs after the writer (`:200-201`) | `AFaultAfterTheWriter_LeavesNoAuthors` through `RecordRound` with a planted `findings` trigger, plus an early-`COMMIT` break-it |

### 11.2 E4 §S2.2b's E5 list — all six applied, plus the G3 ownership note

(1) G1 → §3 *The peers*, `AuthorPeers` + `AttributionMapping.Timeline`, S4 `ARunningPeersStamp_IsNotItsCompletion`; (2) G6
→ §3 *Which rounds*, S4's null for an unsettled round; (3) G3 → stored unchanged, `APeerRunningRound_IsStoredAsItIs`, E7
repairs (§10 item 11); (4) G9 + G3 → the snapshot is every text-free member of `RoundAuthors` plus E5's five, a reflection
fact, ≈ +300 B (§7 item 20); (5) → `authors_attribution` / `Attribution`; (6) → `IsWellFormed` admits `peer running`.
Stored-ownership uniqueness is E7's (§3); E5 tests only what it owns (interleaving, `AnsweredAt` mapped, failed projection).

### 11.3 Notes owed to the other documents

E6: §10 items 12–13. E7: §10 items 11 and 14 (the complete change list is item 11). The parent: §7 items 20–21. E4: nothing —
B1 is written into E4 by Fable (`AttributionMapping`, E4.S5).

### 11.4 The judge review of 2026-10-17 (`f4dcc003` and E4 `7c3bf469`) — every point checked in the code first

| # | Point | Checked against | Resolved as |
|---|---|---|---|
| B1 | E4.S5 needed the mapping only S4 step 5 built, and E5 is built after E4; E7.S2 needs it too | E3 `:191`/`:712` (`ObservedSession` in `src/Store`); E4 §0, S5 | **Decided (Fable): `src_mcp/src/Store/AttributionMapping.cs`, built by E4.S5** — the one place that sees E3's `src/Store` records and E4's core records; E3 cannot build it (E4's types do not exist yet), E5 comes after E4, and E4.S5 already depends on E3.S4. S4 step 5 and E7.S2 call `Observation`/`Timeline`/`Universe`; §2 row, §3, S4 steps 2 and 5, §10 items 8 and 14 |
| H1 | item 11 ran (a) before (c): a file with no snapshot was "recomputed as grown", the heal never reached, `PeerGates = []` retriggered (a) | the first text of item 11; E7 `:167-174` | item 11 rewritten: transcript presence decided first; (c) heal only when gone and no snapshot; `IdSource = "healed"` marks a healed snapshot whose peer set is unknown, so (a) never retries it |
| H2 | `Unknown` snapshots carried versions no rule set; E7 (2) kept any non-empty state ≥ current, `unobserved` included | S1 `Unknown(...)`; E7 `:167-168`; parent §9 protects observed/partial only | `Reader = 0` when the reader did not run, `Attribution = 0` when `Assign` did not (§3, S1, `AnUnknownSnapshotNamesWhichComponentRan`); item 11 (5): keep applies to `observed`/`partial` only |
| M1 | an `unobserved` result from E4 got `0` write counts, and `UnobservedLaunches` was undefined | S4 step 6 (derived from an empty list); E4 S3 table | `-1` for both counts when E4's result is `unobserved` (§3, S4 step 6, S2's `AnUnknownTotal_…`); E4 states `UnobservedLaunches = -1` |
| M2 | (b) promised "equal rows"; the live budget is 2 s, E7's 60 s; E7 writes `id_source = stored`; a weaker recompute left the file empty | E4 decision 7; item 10; E7 `:229`, `:299` | item 11 (4): what may differ is named, the live `authors_id_source` is kept, a weaker recompute heals |
| M3 | a running peer round is never in `rounds` | `RecordRound` at `RoundEngine.cs:612-614` and `RoundSkips.cs:79` only; `open` opens no database | the limit stated in §3; S4's peer tests hold a projected `done`/skipped round; `ARunningOnlyPeer_IsNotFound_AndTheLimitIsStated`; E7's grown-peer-set rule repairs later |
| M4 | `session.Rounds` lacks this round at the call site; the earlier-caller query and its failure state were unnamed | `RoundEngine.cs:594`, `LiveRound.cs:257`; `CallerDeclaration.cs:34-38`, `RoundCommands.cs:34-35` | `Timeline(…, [.. session.Rounds, record], callerOf)`; the earlier-caller order (snapshot → `authors_session` → UUID `caller` → `""`) in the same read-only query, with its failure state; tests `TheGateTimelineHoldsTheRoundBeingAttributed`, `AnEarlierRoundsCaller_ComesFromItsSnapshotThenTheStore` |
| M5 | undeclared seams: a spy on the static `Assign`, the 1 KiB budget as the constant, the deadline not passed to `AuthorPeers` | S4 scope and tests | an `assign` delegate, an `AuthorsBudget` constructor parameter, `AuthorPeers.Find(connection, load, deadline)` with null = unbounded for E7 |
| M6 | two owners for skipped/interrupted rounds; interrupted undefined | E4 S1/§S2.8 vs §3/DoD/E7 `:178` | one sentence in §3 *Which rounds* (no snapshot, empty `authors_state`, live and backfilled; `interrupted` = the sweep's `RoundRecord.Interrupted`); E4's sentence rewritten by Fable; item 11 rule 1 |
| M7 | the repair waited for "settled", which an interrupted peer never becomes | E4 §S2.3, case 8b | trigger = no overlapping round of a named peer still `running` (FINAL: `done` or `interrupted`); E4 defines *final*; item 11 inputs and rule 3 |
| M8 | S5 stated the id "through the variable" | E3 `:527` (`COAI_CALLER_SESSION` → `stated id`); S4's rule | S5 passes it through `PanelService(environment: …)` as `CLAUDE_CODE_SESSION_ID`, never on the process |
| L3 | §8 item 2 named E6 D18 as the inverse | S2, §10 item 7 | §8 row 2: `AgentKeyOf` |
| L5 | "after `plan_source` (`:552`)" | `ThePairsThemselvesTests.cs:552-553` on `ce9cd298`: `:552` drops `effort`, `:553` `plan_source` | S2: `:553` |
| L6 | the universe's spelling only in E4.S1 | E4 S1 `:205-206` | S4 step 5 states it; `AttributionMapping.Universe` carries it (E4) |
| L7 | the suite guard's vendor comes from `CallerIdentity.Current()` at open | `PanelService.cs:509` | the guard tests open with `client: "claude-code"` |
| L8 | no test for E4 G5's mapping rule | S4 step 5 | the rule is in the mapping (E4.S5) and tested there (`TheTwoTranscriptMissingCodes_AreNeverStateReasons`); S4 step 5 names it |
| L10 | "the ONE exception" | item 11 (b) | the two exceptions named (item 11 list 8); E4's G3 row says "one of two" |
| L1, L2, L4, L9 | E4-only points | — | resolved in E4 by Fable (its §11 *Resolutions II*) |
| size | ≈ 15 KB of restatement | §7 items 1–14, §8, §9, §11.2–11.3, the S1/§10 older-server duplicates | condensed as the judge proposed; the decisions stand in §3 and the stories; the one older-server specification is §3's |

## 12. Implementation record (2026-10-19)

Built on `feat/round-authors-e5` after merging `feat/round-authors-e4` (which holds E3) into it — a merge, not a rebase (`442b7bb5f`; origin's copy of the E4 branch carried
no code yet, so the local ref of the E4 worktree was merged). One commit per story, then this record. **Every test was watched RED for its real symptom before the code
that answers it** — a shell that returned empty values, an absent column, a step that answered null — and every break-it of the stories was planted, watched red by the
test named for it, restored byte for byte, rebuilt and watched green. The committed files carry no stub and no scratch test.

**Facts of E3 and E4 verified in the code before use.** `AttributionMapping` (`src_mcp/src/Store/AttributionMapping.cs`) is the one mapping and E5 calls it
(`Observation`, `Timeline`, `Universe`); it writes none of its own. `RoundAuthors` has 13 members (`WritesOutsideTheGateCheckout` the one that never reaches a file),
`WindowAuthors`, `PeerGates`, `RoundAttribution.Version = 1`, `StateReason`, *settled* = recorded and `done` (`Windows.IsSettled`), *final* = not running;
`RoundAttribution.PeerStillRuns(AttributionInput)` exists and `PeersKnown = false` makes `Assign` read no peer; `main` is never listed in `CallerObservation.Agents`;
E3's `ObservedSession.ReaderVersion = 1` and `ReadDiagnostics.AgentFilesEmpty` exist (the step logs it with the other counts).

**What was built.** S1 `8e55fbf17`, S2 `ac67b97bb`, S3 `c2ca41ddb`, S4 `04d71c572`, S5 `20ee9d803`, S6 `067594862`; this record is the commit after them.

**Deviations from the text above, each the smallest change that keeps the intent.** None changes a column, a member of `AuthorsSnapshot`, an unknown value, an `authors_id_source`
value or a version — what E6 and E7 read.

1. **`ActivityTotals.IsUnknown` gets `[JsonIgnore]`** (E4's record, an attribute only — no member changed). S1's own shape test found the stray `isUnknown` key in the session
   file (red: `authors.ambiguous.isUnknown`): the computed-property class §8 item 3 names, which E4's `[JsonIgnore]` on `AgentKey.IsMain` closed for one record only.
2. **`writtenWith` lives in `src_vs_code/src/authorsLine.ts`, not `rounds.ts`**: `rounds.ts` would pass ESLint's 800-line ceiling with its helpers. `rounds.ts` exports `member()`
   for it and gains the two fields; the tests are `authorsLine.test.ts`, not `rounds.test.ts`. `LogRow.writtenWith`, `writtenWithHtml`, the CSS and the five fixtures are as specified.
3. **The spec contradicts itself on a tie of coder groups** — the rule says *larger group first, ties by family name*, the example for the noun says *1 Sonnet + 1 Opus coders*. The rule
   is implemented (*1 Opus + 1 Sonnet coders*, the two sorted by name) and tested; E6's report should print the same order or say it does not.
4. **`AuthorPeers.Find(connection, lookup, load, deadline, clock)`**: the plan's three-argument shape plus the two inputs it implied — `PeerLookup(callerSessionId, gateSessionId)`
   (who the peers are peers of) and the clock the deadline is read from — returning `AuthorPeerSet(PeersKnown, Peers, EarlierCallers, Why)`. `AuthorPeers.OpenReadOnly(dataDir)` is the
   one read-only, one-second connection, null when the store is absent. E7.S2 calls `Find(connection, lookup, load, deadline: null, TimeProvider.System)`. The earlier-caller read of §3 is the
   same query's second half (`EarlierCallers`), so the live path and the backfill share one build.
5. **The step's seams are optional constructor parameters** — environment, clock, budget, peer source, reader, `assign`, and `load` (the session loader the default peer source uses) —
   and `PanelSettings.TranscriptHome` (`ClaudeHome.Default.With(AgentLogDir)`) is the ONE place the default transcripts folder is written: `RoundEngine.WhatTheCallerWasDoing` reads it too.
6. **The byte-budget test uses 2 KiB, not 1 KiB.** E3's byte gate always yields the HEAD of a file (the first line holding a timestamp); in the fixture that line alone is 1.6 KiB, so a 1 KiB
   budget stops even a bounded read. 2 KiB shows both sides of the slice's limit (`partial: read budget` unbounded, `observed` with the window starting hours after the last line).
7. **"A reader that throws, through `PanelService`"** cannot use a seam: the step is an internal type and `PanelService` is public with one optional parameter (`environment`). The
   engine-level test poisons the transcripts folder name (a NUL), which the locator's directory listing answers with `ArgumentException` — not an IO error, so a real failure inside the
   step. The throwing-reader fact itself is a unit test through the `read` seam.
8. **A RUNNING peer round has no caller on disk** — no row (`RecordRound` never runs for it), no snapshot — so it is built with an empty caller, and E4's `partial: peer running` cannot name it
   in production until it is projected. **This is a finding for E4, E6 and E7, not a defect fixed here:** the spec's §3 order (snapshot, `authors_session`, UUID `caller`, empty) gives nothing else
   to read. The tests project the running round (a state production never writes) to show the reason is stored as E4 returns it; E7's repair therefore leans on the grown-peer-set rule
   (`a settled caller gate session not in PeerGates`), which works, more than on `peer running`.
9. **A fresh data directory's first round is `partial: prior windows unknown`**: the store does not exist yet, so the lookup cannot answer (§3 says exactly this); E7's recompute repairs it.
10. **S2's "doubles the rows" break-it** reads as a UNIQUE-constraint error: the primary key `(round_id, ordinal)` refuses the doubling the plan predicted.
11. **S5 uses the one fixture `resumed-agent`**, placed between the plan round's completion and the code round's start (every stamp moved by one offset and compressed 200×, derived from the
    clock at run time), the checkout `repo-a` of the fixture made a real git repository. The plan round runs first against stamps an hour in the future, so it records nothing of the fixture.
12. **The suite's wall time cannot show 5 %.** The baseline (before S1, this worktree, 27 m 16 s, 11 883 tests, 4 failed) and the final run were taken on a machine other sessions were also loading; see the measurements.

**Owed into E5, applied.** From E7: `AuthorPeers.Find` sets `PeersKnown = false` when a peer's session file does not load (null, or naming another session) or the deadline cuts the walk, and
when the store cannot answer — built (`AuthorPeersTests`: `APeerFileThatDoesNotLoad_…`, `APeerFileThatNamesAnotherSession_…`, `ADeadlineThatPassesBeforeAFileLoads_…`, `ANoDatabase_…`,
`AStoreWithoutTheColumn_…`) and written into §10 item 8; E7 calls E5's `TranscriptPresent` input **`TranscriptLocated`** — a name difference only, noted in §10 item 11. From PR #868: not on `main`
(open, branch `docs/plan-session-keeps-unknown-fields`), so §7 item 18 carries the note; its §8 (a boundary table) will collide with this document's §8 and one of the two takes a new number when it lands.
Both PRs strip the bags from the `open`/`status` answer in `SessionAnswerFor`; whichever lands second puts its strip in the same expression (a merge conflict there is the signal).

**The older-server run, measured once (S1, the plan's §3).** The released `coai-mcp-0.58.0-win-x64` (`gh release download mcp-v0.58.0`, SHA-256 matched the release's `.sha256`), run over stdio from a
scratch folder: `open` on a THROWAWAY repository (a temp `git init` with one commit) over a fresh data directory whose session file — first created by that same binary's own `open` — was then given two rounds
each carrying an `authors` snapshot in the shape S1 writes (hand-shaped to the contract, which S1's own shape test pins); a second `open` ran. **Observed: `authors` was gone from every round of the file after
ONE `open`** (the file shrank from 13 221 to 3 246 bytes; the round keys left are the old binary's own), and **`open` never opened `coai.db`** (none existed in the data directory afterwards; the startup
re-projections had nothing to project). The prediction held; the database half is S2's suite test (`AnOlderServersReSave_LeavesTheProjectedAuthorsInPlace`: the 23-step migrator, both preview forks and an
unwidened upsert leave `user_version` 24 and the projected authors byte-equal). The throwaway folders carried the `testsToDelete-` prefix and were removed by this run; the installed `coai-mcp`, the live databases
and `%LOCALAPPDATA%\coai-mcp` were never touched.

**Measurements (S4; Debug build, a machine shared with other sessions).** *Prediction, written before the runs:* the largest of E1's fixtures reads and attributes in under 50 ms; a synthetic slice of 64 MiB
reads in under 2 s at 40 MiB/s or better; so 2 s / 64 MiB stands. *Observed:* the largest fixture (`resumed-agent`, 103 101 bytes in two files) takes **6.7 ms** per `For` (20-run mean, warm; the slowest of the
twenty cases); a synthetic main file of 8 / 32 / 64 / 96 MiB (one real transcript line repeated, stamps rising a second per line) reads whole in 215 / 785 / 569 / 692 ms (37–139 MiB/s, the first runs cold), and with
`fromUtc` near the end in 167 / 404 / 165 / 173 ms — the bytes before the bound are still SCANNED, which is why the time limit runs over the whole read and the byte limit over the slice (E3 F1). E1's Node figure
for a 650 MB session (19.9 s) is the figure the budget was set against; at C# speeds a 64 MiB slice fits the 2 s with about three times to spare, and the 650 MB case is bounded by the `fromUtc` slice and the deadline, never by the file.

**Suites.** `CoaiMcp.Tests.exe`: 12 012 tests, 11 953 passed, 0 failed, 59 skipped (platform-only cases), exit 0, 17 m 14 s — the whole run, once, after the last code change; nothing needed a rerun alone. The baseline before S1 had failed 4 (`QuestionRowOnAgyScenarioTests` three times, `SessionStoreConcurrencyTests.EightWritersOnOneSession_DoNotDenyEachOther` once — all load-sensitive) and took 27 m 16 s on a more heavily shared machine; 129 tests were added. `npm test`: 6 475 tests, 6 472 passed, 0 failed, 3 skipped, exit 0 (132 s; typecheck and lint clean). `dotnet format --verify-no-changes`: clean (exit 0) over the whole solution. `plan-lifecycle`: clean, and `pin-check`: OK (the conventions pin is at the tip of `release`).

**Tests added** (all under `testsToDelete-` temp folders): `RoundAuthorsSnapshotTests` (17), `TheRoundsAuthorsAreStepTwentyFourTests`, `RoundAuthorsTableTests` (S2), `TheLogNamesEachRoundsAuthorsTests` (S3),
`RoundAuthorsStepTests`, `AuthorPeersTests`, `ThePanelAnswersWhateverTheAuthorsStepDoesTests` (S4), `ARoundRecordsWhoWroteItEndToEndTests` (S5), `authorsLine.test.ts` and additions to `roundsLog.test.ts`,
`bundledPage.test.ts` and `anOlderExtensionReadsALogPageCarryingASize.test.ts` (S6); shared files `shared/authors-state-vectors.json` (read by C# and TypeScript) and `shared/model-display-vectors.json`;
test helpers `AuthorsSnapshotFixtures`, `AuthorsStepHarness`, `PeerWorld`, `FakeCliGateBase`.

**Break-its planted, seen red, restored, rebuilt, seen green** — S1: materialising getter (`ASessionFileWithoutAuthors_…` and the `status` test red, the invented key in the file), strip removed
(`TheStatusAnswer_HasNoAuthors_…`), `InvalidInput = "invalid, input"` (`E5sReasonsContainNoJoiner`), `PeerGates` made internal (the reflection fact, four red), a digit admitted by the C# rule (`EveryVectorOfTheSharedFile_…`
on `partial: read budget 2`). S2: the table reading a `RoundContext` (the rebuild test red with zero rows), the `DELETE` dropped, the `DROP INDEX` left out of `ThePairsThemselvesTests`, `Record` with its own `BEGIN`/`COMMIT`
(sixteen red, `cannot start a transaction within a transaction`), an early `COMMIT` (`AFaultAfterTheWriter_…`: `Expected … rounds … but {1}`), `agent.Id` written unmapped (main's row reads `main`). S3: the `HasColumn` guard dropped
(`AStepTwentyThreeFile_…`: exit 74). S4: the statement moved after `Save` (the file side null), the `catch` removed, the byte check removed (`ReadBudget(long.MaxValue, …)`), a refused location falling through to the peer query,
the identity read from `Environment.GetEnvironmentVariable` (this run was INSIDE a Claude Code session — `CLAUDE_CODE_SESSION_ID` and `CLAUDE_PID` set — and twenty-five of 34 tests went red), `CompletedUtc` mapped without the status check
(`ARunningPeersStamp_…`). S5: the reader keeping a meta's sentinel field as `AgentType` (`NoSentinelLeavesTheReader_…`, naming the session file). S6: the `writtenWithHtml` term deleted from `detail` (two page tests red, every unit test green).

**What is open.** (Superseded by §12.1: the code gate ran, and `research/module_tests.md` carries the E5 row.) Two things E6/E7 should
read before they build: deviation 3 (the tie order) and deviation 8 (`peer running` is unreachable for a still-running peer in production).

### 12.1 The judge's review, E4 final and the code gate (2026-10-19)

**Merges, not rebases** (E6 and E7 merged this branch's earlier ref): the final E4 (`origin/feat/round-authors-e4` at `f0d89dad8` — the CA1502 refactors, the
unobserved-launch double count fixed, `SameRoot`/`Badness`/`UtcInstant.Of`) in `b570416fc`, then `origin/main` at `b1a3266dd` in `7d0992a09`. Conflicts only in
`research/module_tests.md` (both rows kept) and `todo/README.md` (E4's final row, this E5 row). Deviation 1's `[JsonIgnore]` on `ActivityTotals.IsUnknown` survived
E4's refactor unchanged. PR #868 is still open, so main brought no second §8; the note in §12 stands. After the merges the E3, E4 and E5 classes ran green (797 tests).

**The judge's review** checked schema step 24 (appended, `ON CONFLICT … DO UPDATE` keeps row ids, `round_authors` keyed by `(round_id, ordinal)`), the one transaction
(`RoundAuthorsTable.Record` opens none), the strip in `SessionAnswerFor` (both `open` and `status`), the snapshot against E4's final `RoundAuthors`, `-1` for unknown,
every `PeersKnown = false` case, the timeline `session.Rounds + record` (the save appends exactly that), the 2 s deadline (a negative time left stops the reader at
once — `ReadMeter` compares elapsed `>` the limit), S6's defensive reads and escaping, and privacy (ids, closed words, counts; no text, no root). **Every deviation 1–12 is
accepted**; 8 and 9 stay findings for E6/E7. Two defects were fixed red-green in `6ef9cccd1`:

- **Complexity 4 was not enforced on E5's own files.** `.editorconfig` now makes CA1502 an error for `RoundAuthorsStep`, `AuthorPeers`, `AuthorsBudget`,
  `RoundAuthorsTable` and `RoundAuthorsLog`, as E4 did for its core. Red: the build failed on the step's constructor (8), `Observe` (5) and `AuthorPeers.Walk` (5).
  Green: the seams' defaults through one `Or<T>`, `KnownPeers`, `Expired`.
- **`AuthorPeerSet.Why` reached no line.** It was documented "for a Debug line" and nothing logged it, so a lookup that did not complete left only `prior windows unknown`.
  `KnownPeers` now logs it at Debug. Red: `APeerLookupThatDidNotComplete_SaysWhyInTheLog_…` found no `no rounds database` line; green after.

**The code gate** — `good_enough` (threshold 5, one round; 12 of 16 reviewers answered, kimi's four returned empty answers), over a PARENTLESS base commit
(`abac3ff60`) whose tree is E4 final merged with main, so the reviewers saw E5 only. 23 findings: **2 accepted, 21 rejected with a reason checked in the code.**

- Accepted, findings 5 and 20 (`37f963141`): `Why` carried the store's exception MESSAGE, which may hold a path. It is now the type and the SQLite error code
  (`SqliteException (SQLite error 1)`). Red: `AStoreThatCannotAnswer_SaysWhyByTypeAndCode_…` failed on the message text; green after.
- Rejected, the main reasons: the count columns' `DEFAULT 0` is the plan's own contract (§S2: a reader checks the state first); a session file's name is a HASH of
  repo and branch (`SessionStore.FileFor`), so no traversal reaches it; `GitModules` already drops a rooted or `..` mount; a negative time left is graceful; `Reached`
  must survive an exception, so it cannot be an immutable return value; a nullable "not recorded" is `RoundRecord`'s established pattern (`Caller`, `Size`,
  `PlanSource`); per-row inserts, sequential peer loads and the type-only failure log follow the neighbours and the privacy contract, with no measured cost.

**Suites after the last code change.** `CoaiMcp.Tests.exe`: 12 651 tests, 12 530 passed, 1 failed, 120 skipped (26 m 28 s on a shared machine). The one failure,
`ARoundRecordsWhoWroteItEndToEndTests.ADeletedDatabase_…`, was a plan round that answered `call_human` after 40 s under the suite's load (the FakeCli reviewer has
the repository's usual 30 s timeout); its class passed alone three times out of three (4 of 4 each, about 30 s). `npm test`: 6 554 tests, 6 551 passed, 0 failed,
3 skipped, exit 0. `dotnet format --verify-no-changes` on the changed files: clean. `plan-lifecycle`: clean; `pin-check`: OK.
