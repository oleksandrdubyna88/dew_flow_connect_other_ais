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
