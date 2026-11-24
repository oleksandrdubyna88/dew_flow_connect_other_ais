# coai v2 — epoch 1, epic E1 (gate core) — epic plan for the gate (generated)

Parent plan: v2/PLAN.md; architecture: v2/architecture/server.md (§1, §2, §7, §16-18).

## v2/epoch-1/e1-gate-simplify/epic.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: e1-gate-simplify
title: "Gate core: four outcomes, v1 data migrated, live proof"
mvp: true
half: server
dependsOn: []
parallelWith:
  - e2-thin-shell
doneWhen:
  - "only proceed, revise, good_enough and not_reviewed come out of a round"
  - "the deleted classes are gone and the MCP test executable is green"
  - "--migrate-v2 converts every released fixture exactly once; an interrupted run ends equal to one clean run"
  - "3 real plan→code gates end with 0 person waits; migration on copies of real data: dry-run = apply, second run 0; outcome shares recorded"
```

## v2/epoch-1/e1-gate-simplify/stories/s1-verdicts-and-holds/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s1-verdicts-and-holds
title: "Remove the policy enum, ladder, held gates, cards and ask_human"
dependsOn: []
```

## v2/epoch-1/e1-gate-simplify/stories/s1-verdicts-and-holds/tasks/t1-policy-ladder/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-policy-ladder
title: "Remove StagePolicy/EscalationStep, PanelConfig.OnExhausted/Autonomous, PolicyOf and the ContinueAnyway/CallHuman/Escalated/Ladder/GateHeld verdicts; keep Proceed/Revise/GoodEnough/NotReviewed"
kind: code
complexity: 7
dependsOn: []
parallel: false
files:
  - src_mcp/core/Rounds/SessionState.cs
  - src_mcp/core/Rounds/RoundMachine.cs
  - src_mcp/src/Server/PanelSettings.cs
dod:
  - id: d1
    item: "a round can end only in the four kept outcomes"
  - id: d2
    item: "an over-threshold round with budget left returns revise"
  - id: d3
    item: "the solution builds with warnings as errors"
```

## v2/epoch-1/e1-gate-simplify/stories/s1-verdicts-and-holds/tasks/t2-holds-cards-askhuman/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-holds-cards-askhuman
title: "Remove RoundEngine.ApplyAnyHumanDecision, Escalations (move AtomicJson and ScratchDirectory out), EscalationRetention, SessionHolds, AskHumanService, AskUnderAuto, EscalationBudget; ask_human becomes an alias reply"
kind: code
complexity: 6
dependsOn:
  - t1-policy-ladder
parallel: false
files:
  - src_mcp/src/Server/Rounds/RoundEngine.cs
  - src_mcp/src/Server/Escalations.cs
  - src_mcp/src/Server/AskHumanService.cs
  - src_mcp/src/Server/AskUnderAuto.cs
  - src_mcp/src/Tools.cs
dod:
  - id: d1
    item: "no code path waits for a person"
  - id: d2
    item: "ask_human returns status ask_in_conversation with note 'removed in v2'"
```

## v2/epoch-1/e1-gate-simplify/stories/s1-verdicts-and-holds/tasks/t3-verdict-tests/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-verdict-tests
title: "Delete tests of removed behaviour; add RoundMachine tests for the four outcomes"
kind: test
complexity: 5
dependsOn:
  - t2-holds-cards-askhuman
parallel: false
files:
  - src_mcp/tests
dod:
  - id: d1
    item: "the MCP test executable is green"
  - id: d2
    item: "each kept outcome has a test that fails if its branch is deleted"
```

## v2/epoch-1/e1-gate-simplify/stories/s4-migrate-v2/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s4-migrate-v2
title: "--migrate-v2: held sessions, cards, stores, settings keys"
dependsOn:
  - s1-verdicts-and-holds
```

## v2/epoch-1/e1-gate-simplify/stories/s4-migrate-v2/tasks/t1-migrate-mode/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-migrate-mode
title: "One-shot --migrate-v2 [--apply], automatic at first 2.0 serve under a lease with marker migrations/v2.done; legacy-hold reader kept"
kind: code
complexity: 7
dependsOn: []
parallel: false
files:
  - src_mcp/src/Program.cs
  - src_mcp/src/Server/Migrations
dod:
  - id: d1
    item: "a held session becomes AwaitingResolve + AdvanceOnResolve with the converted note"
  - id: d2
    item: "active cards move to escalations/archive as withdrawn_v2; qphase and cadence stores archived, not deleted"
  - id: d3
    item: "removed settings keys land in settings.v1-removed.json"
  - id: d4
    item: "a step journal migrations/v2.journal makes every step idempotent across partial writes"
```

## v2/epoch-1/e1-gate-simplify/stories/s4-migrate-v2/tasks/t2-migrate-fixtures/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-migrate-fixtures
title: "Fixtures of each released session format migrated twice: one advance, note present, no active card; kill and restart at each step boundary"
kind: test
complexity: 5
dependsOn:
  - t1-migrate-mode
parallel: false
files:
  - src_mcp/tests
dod:
  - id: d1
    item: "second run changes nothing"
```

## v2/epoch-1/e1-gate-simplify/stories/s4-migrate-v2/tasks/t3-migrate-docs/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-migrate-docs
title: "PROJECT.md one-shot list and shared/data-inventory.json updated for the added and removed modes"
kind: docs
complexity: 2
dependsOn:
  - t1-migrate-mode
parallel: true
files:
  - .agents/PROJECT.md
  - shared/data-inventory.json
dod:
  - id: d1
    item: "every one-shot mode in Program.cs is listed and no removed one is"
```

## v2/epoch-1/e1-gate-simplify/stories/s6-live-proof/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s6-live-proof
title: "Live proof: real plan→code gates and migration on copies of real data"
dependsOn:
  - s1-verdicts-and-holds
  - s4-migrate-v2
```

## v2/epoch-1/e1-gate-simplify/stories/s6-live-proof/tasks/t1-live-gates/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-live-gates
title: "Run 3 real plan→code gates on the E1 binary; record 0 person waits and only the four outcomes"
kind: measure
complexity: 4
dependsOn: []
parallel: false
files:
  - research/RESULTS_v2_gate_core.md
dod:
  - id: d1
    item: "3 real gates recorded with verdicts and reviewer counts"
```

## v2/epoch-1/e1-gate-simplify/stories/s6-live-proof/tasks/t2-migrate-copies/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-migrate-copies
title: "--migrate-v2 on a COPY of each real data folder: dry-run counts = apply counts; second run 0 changes"
kind: measure
complexity: 5
dependsOn: []
parallel: true
files:
  - research/RESULTS_v2_gate_core.md
dod:
  - id: d1
    item: "counts recorded per data folder"
```

## v2/epoch-1/e1-gate-simplify/stories/s6-live-proof/tasks/t3-outcome-share/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-outcome-share
title: "Share of good_enough/not_reviewed vs the v1 call_human share from RoundsDb"
kind: measure
complexity: 3
dependsOn:
  - t1-live-gates
parallel: false
files:
  - research/RESULTS_v2_gate_core.md
dod:
  - id: d1
    item: "both shares recorded with the query used"
```

# Spec — s4-migrate-v2 (cornerstone: data migration)

> Written by Fable, 2026-11-22, from the story, server.md §7/§16.12/§17.2/§18.5, PROJECT.md, worktree coai-v2-e1 at
> 358b51f1 and c24e5141. Main-session decisions on the risks are at the end.

## 1. Contract

**Scope:** one resolved data folder (`COAI_DATA_DIR` + `COAI_DATA_SIDE`); each side migrates itself.

**Inputs** (raw JSON or the unknown-member bag; the v1 types are gone):
- `sessions/session-*.json`, three released shapes: F1 (mcp 0.3.1–0.39.x: `state.humanGate`, `state.escalationsUsed`,
  `config.onExhausted`); F2 (0.40.0–0.65.x: + `state.holdQuestions`, `state.requestQuestions`); F3 (0.66.0: +
  `config.autonomous`), and sessions v1 auto already released (old note "released by policy auto…", no `humanGate`).
  Kinds: plan, code, document (`state.document`), feature (`state.feature`, branch `:feature`).
- `escalations/<id>.json` and `<id>.answer.json`; card shapes: bare (< 0.41.0), `status`/`expiredUtc` (0.41.0), `kind`
  (0.41.1), `decisions` (#920).
- `qphase/`, `cadence/` directories.
- `settings.json` (flat `COAI_*`). Removed by s1: `COAI_ON_EXHAUSTED`, `COAI_ESCALATION_MINUTES`,
  `COAI_ESCALATION_SECONDS`. Renamed: none yet (E3 adds rows — see risk 1).

**Outputs, in order:**
1. Session where `LegacyHold.Stands` → `LegacyHold.Released` (AwaitingResolve + AdvanceOnResolve, hold members dropped);
   last round `Note = "good_enough — converted from a held gate by v2"`; verdict text, stage, rejections, pending and
   round count unchanged. Written to the SAME path under `SessionClaim` + `SessionTurn`, re-read under the claim; then
   `RoundNotes.Write` (best effort).
2. Open card (status not `expired`, no non-empty answer file) → `status: "withdrawn_v2"` in place (atomic, other
   members kept), then rename to `escalations/archive/<id>.json`. Answered or expired cards untouched.
3. `qphase/` → `migrations/v1-archive/qphase/`, `cadence/` → `migrations/v1-archive/cadence/` by directory rename;
   existing target → merge file by file, never overwrite (suffix).
4. Settings under the extension's `settings.lock` protocol: write `settings.v1-removed.json` first (merge; existing key
   never overwritten), then `settings.json`. Removed key → moved out; renamed key → new name; both → new key wins, old
   value to `settings.v1-removed.json`.
5. `migrations/v2.done`: `{schema, tablesHash, finishedUtc, version}`.

**Journal `migrations/v2.journal`:** JSON Lines, append-only, flushed per line:
`{"seq","utc","kind":"session|card|store|settings","id","state":"planned|done|skipped|deferred|unreadable","detail"}`.
A torn last line is ignored. Evidence, not authority; its one use: a session with the note but no `done` line gets its
projection again.

**Idempotence (from the object):** session — bag has `humanGate: true` or not; card — top level + open → both steps,
top level `withdrawn_v2` → move only, only in archive → done; store — source absent → done; settings key — absent → done.

**Lease:** `claims/migrate-v2.lease`, an OS lock like `SessionClaim` (kernel releases on kill); only apply takes it.

**Automatic run:** in `ServeAsync`, after the logger, before `StartingHost.Start`; only when assembly major ≥ 2 (pure
function; dev builds are 0.0.0) and `v2.done` is absent or its `tablesHash` differs. Never throws into serve, never
writes stdout. Lease busy / read-only folder / deferred objects → warning, serve, retry next start.

**One-shot `--migrate-v2 [--apply]`:** always scans (the marker gates only the automatic run). Stdout = one JSON object
`{"mode":"migrate-v2","apply","dataDir","status":"plan|done|busy|partial|failed","counts":{"sessions","cards","stores",
"settingsRemoved","settingsRenamed","unreadable","deferred"},"answeredHolds":[…],"steps":[{kind,id,action,detail}]}`.
A dry run creates nothing (no `migrations/`, no lease, no log file). Exit: 0 plan printed or apply complete (unreadable
reported, not blocking); 65 wrong argument; 74 data folder missing or not writable; 75 lease busy or objects deferred,
no marker; never 64.

## 2. Scenarios (each is a test)

1. Held plan session, findings resolved → AwaitingResolve + AdvanceOnResolve; empty `resolve` advances once; a second
   `resolve` is refused.
2. Held with pending findings → pending kept; one `resolve` advances once.
3. Same for code, document (→ Done) and feature (→ Done, round count kept) holds, in F1, F2, F3.
4. `humanGate: true` stands with `config.autonomous` absent, false or true.
5. Not held, and v1-auto-released → bytes unchanged.
6. Card with no session → `withdrawn_v2`, archived; held session whose card is missing still converts.
7. Answered and expired cards untouched; a held session with an answered card converts and is listed in `answeredHolds`.
8. Kill at each step boundary k = 0..K (before planned, after planned, after the object write, after done, before the
   marker), then one clean run → tree equals one clean run (excluding journal, marker, `claims/`, `*.turn`).
9. Second apply → no byte changes, counts 0; same after the journal and marker are deleted.
10. Dry-run counts = apply counts on a copy; the dry run changes no byte and creates no file.
11. Two processes → one migrates, the other 75/`busy`; result equals one run.
12. Read-only folder → dry run 0; apply 74, nothing changed; serve starts.
13. Corrupt session file → `unreadable`, untouched, the others migrate, exit 0, marker written.
14. Session claim held → `deferred`, exit 75, no marker; the next run completes.
15. Session file name different from its key → rewritten in place, no second file.
16. Settings: removed keys to `settings.v1-removed.json` with value kinds kept, other keys unchanged; rename (test table)
    rewritten; both → new wins; non-JSON `settings.json` untouched; lock held → `deferred`.
17. `qphase/` and `cadence/` archived with all files; a re-created source merges without overwrite.
18. Version gate: `0.0.0` and `0.89.2` false, `2.0.0` true; marker with another `tablesHash` → runs again.
19. Real binary: `--migrate-v2 --bogus` → 65; stdout exactly one JSON object; stderr only logs.
20. `humanGate` is NOT in `RetiredMembers.Shipped`: an ordinary save before the migration keeps the hold evidence.

**Fixtures.** `LegacyHoldTests`/`HoldFromBeforeAutoFixture` ran v1 code; restore them as DATA: from `LegacyHoldTests`
(c24e5141, `HeldOf(stage, resolved)` line 94, 4 stages × 2) frozen session files; port
`AReleasedHold_ThenAnEmptyResolve_AdvancesTheStage` and `Released_LeavesTheSessionAsAnAutoVerdictWould` (without config
and `EscalationsUsed` asserts); not `Stamped_*`, `PredatesAuto`, `TheNaiveRelease…`. Generate once in a throwaway
worktree at c24e5141: a temporary test calls `HoldFromBeforeAutoFixture.RaiseAsync(kind, resolved)` and copies
`sessions/` + `escalations/`; `repoPath` = `/fixture/repo`, each file renamed to `SessionStore.FileFor` of the new key;
commit under `src_mcp/tests/fixtures/migrate-v2/f3/…`. Derive F2 (remove `config.autonomous`) and F1 (also remove
`holdQuestions`, `requestQuestions`, card `status`/`kind`/`decisions`) by editing copies.

## 3. Brief for the coder (worktree /home/jinx/git/coai-v2-e1)

Order: (1) fixtures and red tests `src_mcp/tests/MigrateV2*Tests.cs`; (2) `src_mcp/src/Server/Migrations/`:
`MigrationV2Tables.cs` (removed keys, renames, store names, hash), `MigrationV2Journal.cs`, `MigrationV2Lease.cs`,
`MigrationV2.cs` (`Plan`, `Apply`, internal fault seam `AtBoundary(int)`), `MigrateV2Mode.cs`; (3) `SessionStore.cs`:
internal `LoadFile(path)` and `SaveTo(path, session)` (scrub + turn + `AtomicFile`), public behaviour unchanged;
(4) `Program.cs`: `Startup.MigrateV2`, `Classify`, `WriterModes`, help text, the `ServeAsync` hook;
(5) `ServerJsonContext.cs`: source-generated types for the journal line and the report (AOT; cards and settings as
`JsonNode`); (6) in the SAME commit as (2): `shared/data-inventory.json` (`migrations/`, `settings.v1-removed.json`, the
`escalations/` reason), `.agents/PROJECT.md` (add `--migrate-v2`), `EveryModeIsClassifiedForAMissingNamedRootTests`.

Must NOT change: `LegacyHold.Released`/`Stands`; `RetiredMembers.Shipped` (stays empty); `RoundMachine`; verdict texts;
cadence and qphase code; any Team-server wire field; non-held sessions; a session's file name; no catch-all that hides
the fault seam.

Commands: `dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4`; the MTP executable via
`node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests` (exact name as built;
never `dotnet test`); `cd src_vs_code && npm ci && npm test` (the inventory is shared with the extension tests).
RED-GREEN-RED per scenario: fails before the code (record the failing line), passes after, fails again when the guarded
branch is broken, then restore. Report a table: scenario, test name, mutation.

## 4. Risks (Fable) and main-session decisions

1. Cadence/qphase code stays live until E3.S2, and E3.S2/S3 add removed/renamed keys after s4 → tables are data,
   `tablesHash` in the marker, automatic run gated on version ≥ 2. **Decision:** each E3 removal story adds its rows and a
   fixture; **E5 releases mcp as 2.0.0** (added to the release epic).
2. A v1 `coai-mcp` still running on the same folder can re-save a held session or write a card after `v2.done` → only
   a one-shot run finds it. **Decision:** the 2.0 release notes say to restart every client; the one-shot is documented.
3. Extension 0.89.x rewrites `settings.json` at each activation, so removed keys come back. **Decision:** the server
   ignores removed keys (E3 fallback reads); the 2.0 extension stops writing them.
4. A person's answer v1 did not apply (`fix`, `discuss`) is overridden by the good_enough conversion. **Decision:**
   accepted (auto mode never waits); `answeredHolds` is raised as one notification per item.
5. `Stands` converts ALL holds (v1 released only pre-auto holds). Intended.
6. Retiring `humanGate` before the migration ran loses the evidence → `RetiredMembers.Shipped` stays empty (scenario 20).
7. After s1 nothing sweeps `escalations/`; answered/expired cards stay forever. **Decision:** follow-up item in E3
   (archive cards older than 30 days); not in s4.
8. t3 (inventory) must be in the t1 commit, not parallel. **Decision:** follow the brief order (6) — same commit.
9. The schema step (`coder_jobs`, `work_*`) and `device-settings.json` are not s4 — they belong to epoch 3 E1 and
   epoch 2 E3.

## 5. Epic plan gate round 1 (2026-11-22) — accepted changes

1. **Convert on load.** `SessionStore` load converts a session whose unknown-member bag has `humanGate: true` with the
   same `LegacyHold.Released` + note, saves it under the claim and journals it (`kind: session, detail: on-load`). This
   covers a session the automatic run deferred, a file that was unreadable and is readable later, and a held session a
   still-running v1 process writes after `v2.done`. Test: a client request on a deferred session; a v1-shaped held file
   written after the marker; both convert once.
2. **Unreadable blocks the marker.** An `unreadable` object leaves `v2.done` unwritten and the automatic run retries at
   the next start (the one-shot reports it); the run never loops inside one start. Test: unreadable → no marker; file
   fixed → next start completes and writes the marker.
3. **Restart-safe store merge.** Per-file moves are deterministic: a collision gets the suffix `.v1-<sha256-8 of the
   source content>`; a source file whose content already exists in the target under any name is removed, not copied.
   Test: kill between individual file moves, including a collision; the end tree equals one clean run, no duplicates.
4. **Answered holds reach the person durably.** The automatic run and the one-shot write one entry per answered hold to
   `server-notices.jsonl` through `NoticeWriter` with a new catalog code `v2-converted-answered-hold` (session, card,
   decision); the extension already shows server notices. Restart-safe: the notice key is the card id, written once.
5. **Torn journal tail.** Before appending, a journal whose last byte is not `\n` is truncated back to its last `\n`.
   Test: crash during a journal write, then two restarts; every line parses.

Rejected (reason recorded in the gate): fixtures must come from released binaries — real artifacts were verified by the
live proof (three real data folders, many releases).

## 6. Epic plan gate round 2 (proceed, 2026-11-22) — accepted changes (supersede §1 and §2 where they differ)

1. **Merge identity includes the path.** A source file is removed only when the SAME relative path in the target holds
   the same bytes; a different file at that path gets the `.v1-<sha256-8>` suffix; two distinct names with identical bytes
   are both kept. Test: two names, identical bytes.
2. **Dry run writes no notice.** The dry run lists proposed notices in the report (`notices: [...]`); only apply (and the
   automatic run) writes them. Test: an answered hold, no notice file, dry run → file still absent.
3. **Late v1 cards.** Every v2 start (not gated by the marker) withdraws and archives any OPEN card at the top level of
   `escalations/` (same step as §1 output 2, journaled). Test: a card written after `v2.done` is withdrawn at the next start.
4. **Unreadable = incomplete.** Contract change: an `unreadable` object makes apply exit **75** with status `partial`, no
   marker; scenario 13 becomes "corrupt session → unreadable, untouched, others migrate, exit 75, no marker; after repair
   the next run converts it and writes the marker". Convert-on-load (§5.1) also converts it once readable.
