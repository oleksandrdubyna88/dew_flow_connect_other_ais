# PLAN — round authors: backfill the two live stores, check ten rounds per side by hand, then answer the owner

> Status: **plan only, nothing implemented yet, 2026-10-21.** Scope: the open tail of the promoted [round-authors plan](../research/PLAN_a_round_records_who_wrote_the_code_and_how_much.md) and its [E7 stories](../research/PLAN_a_round_records_who_wrote_the_code_and_how_much_E7.md). The `--backfill-authors` mode itself is built and merged (#865). This plan holds what remains: two scripts, one missing test oracle, the owner-approved runs on the live stores, the hand-check, the `RESULTS_` file, and the small leftovers of epic E1. No new reader, no new attribution rule, no schema step.
>
> Related docs: [module_server.md](../research/module_server.md) (*`--backfill-authors`*, *`--round-authors`*), [module_tests.md](../research/module_tests.md), [RESULTS_caller_transcripts_on_both_sides.md](../research/RESULTS_caller_transcripts_on_both_sides.md), [PLAN_a_session_file_keeps_what_a_newer_server_wrote.md](../research/PLAN_a_session_file_keeps_what_a_newer_server_wrote.md).

## 1. The goal, before any solution

The owner asked on 2026-10-14: *is the code better or worse since coders moved to Sonnet?* The round-authors plan builds the data to answer that. The data exists only for rounds that run after the live path shipped. Older rounds have no authors and, for code rounds before 2026-10-15, no size.

Today the answer cannot be given, for these reasons:

1. **No live store has been backfilled.** The `--backfill-authors` mode exists on `main`. Nobody has run it on `%LOCALAPPDATA%\coai-mcp` or on the WSL store `~/.local/share/coai-mcp`. Every test run used a `testsToDelete-` temporary folder.
2. **The report has never read a real store.** `coai-mcp --round-authors` ([E6](../research/PLAN_a_round_records_who_wrote_the_code_and_how_much_E6.md), #864) is merged. Its cells for the older rounds are empty or `provisional` until the backfill runs.
3. **Nobody has checked a backfilled row against a transcript.** The plan asks for ten rounds per side, checked by a judge who wrote none of the code, before any number is published.
4. **Two scripts that the runs need do not exist.** `git ls-files` shows no `scripts/backfill-preflight-servers.ps1`, no `.sh`, and no `scripts/round-authors-dump.mjs`.
5. **No release carries the code.** On 2026-10-21 the newest `mcp-v*` tag is `mcp-v0.62.0` (2026-10-19). `git merge-base --is-ancestor` says it contains neither E5 (`bc5b367df`) nor E7 (`42eb3a95a`). A live run needs a released binary on both sides.

The goal of this plan: **the owner's question is answered from a `research/RESULTS_who_wrote_the_code_under_review.md` file, every figure of which traces to a kept output of the two modes, after a backfill that changed no live row it should not change.**

## 2. The boundary with the promoted E7 plan, on both sides

The promoted [E7 stories](../research/PLAN_a_round_records_who_wrote_the_code_and_how_much_E7.md) carry the same table (§12 there). Order: items 1–3 go first, because the live runs need them.

| # | Item | Built by | The other plan's part |
|---|---|---|---|
| 1 | S1–S4: the decider, the stored-history rebuild, the narrow writer, the mode, its backup and dry run | **E7 (shipped, #865)** | This plan runs the mode. It changes none of its code. A defect that J1 finds gets a RED-test story here (§6, N6), and the fix lands in the files E7 shipped. |
| 2 | `scripts/backfill-preflight-servers.ps1` and `.sh` | **this plan, N1** | E7 §6 O1 names the script and its exit codes. |
| 3 | `scripts/round-authors-dump.mjs` and its tests | **this plan, N2** | E7 §6 J1 names the script and says it is "written and reviewed in O1". |
| 4 | The `--round-authors` JSON identity in the S4 scenario | **this plan, N3** | E7 S4 lists the test. The shipped scenario uses a canonical store dump instead (E7 §11, deviation 6), because E6 was not on the E7 branch. |
| 5 | O1–O4 and J1: the release record, the rehearsal, the dry runs, the two live runs, the hand-check, the `RESULTS_` file | **this plan, N5–N9** | E7 §6 is the design text. This plan is the work. |
| 6 | O5: module docs, `.agents/PROJECT.md` entries, and the promotion of the parent with its seven stories documents | **done by the promotion commit of 2026-10-21** | This plan adds only the run's own record to `module_server.md`. |
| 7 | E1 leftovers: the attended `/clear` probe, the line-by-line hand read, the torn-tail frequency | **this plan, N4** | The [E1 stories](../research/PLAN_a_round_records_who_wrote_the_code_and_how_much_E1.md) and the E1 RESULTS file name them as open. |

The two plans share no code. Items 1–7 are the complete list. Nothing else in the round-authors family is open work (the unbuilt items that stay unbuilt by decision are in §7).

## 3. What exists on `main`, verified 2026-10-21 at `42eb3a95a`

| Piece | Where |
|---|---|
| The mode: parse, presence check, read-only open, step check, backup gate, run | `src_mcp/src/Server/Backfill/BackfillAuthorsMode.cs:67-89` (`RunAsync`), `:144-166` (`Execute`), `:169-180` (`MakeBackup`) |
| The dry run runs on the read-only connection alone. `RoundsDb.OpenExisting` comes only after the backup verified | `BackfillAuthorsMode.cs:182-200` (`Run`), `:204-210` (`OpenReadOnly`) |
| The backup: online backup of `coai.db`, every session file, the verdict file `check.json` | `src_mcp/src/Server/Backfill/BackfillBackup.cs:73-87` (`Make`), `:229-239` (`Verify`) |
| The decider: selection, the rule order, the strength guard, `CorrectedBelow` (default `0`/`0`) | `src_mcp/src/Server/Backfill/BackfillDecision.cs:58` (`IsSelected`), `:67` (`Decide`), `:180` (`Settle`), `:36` (`CorrectedBelow`) |
| The writer: claim, `BEGIN IMMEDIATE`, re-read, save, verify, two row writers, commit | `src_mcp/src/Server/Backfill/BackfillWriter.cs:93-129` (`Locked`, `Inside`, `Begin`); `src_mcp/src/Store/RoundsDb.cs:187` (`Transaction()`) |
| The size is rebuilt only from a hex commit name that exists | `src_mcp/src/Server/Backfill/StoredSizer.cs:84-95` |
| The mode is a writer mode (a missing named data folder is exit 74) | `src_mcp/src/Program.cs:330-334` (`WriterModes`), `:362` (`Classify`), `:483-486` (dispatch) |
| The mode is on the one-shot list | `.agents/PROJECT.md:53` |
| The scenario that proves a second run changes nothing | `src_mcp/tests/BackfillAuthorsCliScenarioTests.cs:60-83` (`BackfillTwice_TheSecondRunChangesNoTotals`); the oracle is `StoreDump` in `src_mcp/tests/BackfillRig.cs:112` |
| The report | `src_mcp/src/Server/RoundAuthorsMode.cs`, `src_mcp/src/Store/RoundAuthorsCells.cs` (`Provisional`, `SizeCoverageUnknown` per cell) |
| The fixtures and the measuring scripts of E1 | `src_mcp/tests/fixtures/caller-transcripts/c01`–`c20`, `scripts/export-code-rounds.py`, `scripts/measure-caller-transcripts.mjs`, `scripts/cut-transcript-fixtures.mjs` |

## 4. The safety model

These rules come from the owner (2026-10-15 and 2026-10-16). Each is a precondition. None may be weakened to make a run pass.

1. **Owner approval for each live run.** Each of O2 (Windows) and O3 (WSL) starts only after the owner approves that run in writing, in the session that runs it. An approval for one run does not cover the next one. A session never starts a live run on its own.
2. **A verified backup of BOTH stores before any write.** Each writing run backs up its own store first. The mode makes the copy by SQLite's online backup, copies every session file, and checks the copy (opens, `integrity_check`, `user_version`, every table's row count before and after, `PRAGMA data_version` unchanged, SHA-256 of every session file). A failed check writes nothing and exits 74. No run writes a store it did not back up in the same run.
3. **A dry run first, and it only reads.** `--dry-run` opens the store `Mode=ReadOnly`, makes no backup folder and writes no store file. A writing run starts only after the dry run of the SAME binary over the SAME store exited 0 the same day.
4. **Idempotent.** The second run of the mode writes nothing: every `written` count is 0, and the dump of the authors and size tables is byte-identical.
5. **A weaker result never replaces a stronger one.** The strength order is `observed` > `partial` > `unobserved` > empty. A result from an older reader never replaces a row of a newer reader. `CorrectedBelow` is the only exception, and only a RED-test fix story that proves a version wrong may set it.
6. **A live size is never replaced.** A round that already has a `size_state` keeps it. The backfill never re-measures a size it wrote.
7. **Nothing is deleted.** The mode deletes no backup, no session file and no row it did not write. In WSL no agent deletes anything: no `rm`, no `mv` over an existing file, no cleanup step.
8. **No running server on an older release.** An older `coai-mcp` that saves a session file drops the `authors` member (E5 measured it: 13 221 bytes became 3 246 bytes after one `open`). Every running server of the side must be on the release before the first write. N1 checks this.
9. **WSL runs from script files.** Every WSL command runs from a script file written on the Windows side and started as `wsl.exe -e bash <script>`. No value goes through `wsl.exe`'s command line. Every output goes to a new folder.
10. **No process is killed by image name.** If a session holds a claim, the operator waits for it or asks for the window to close.

## 5. Growth

| Surface | Projected size | Who retires it | If interrupted |
|---|---|---|---|
| `<dataDir>/backfill-backups/<utc>/` (one `coai.db` copy and the session files per WRITING run, a refused run included) | Windows: 72 MB database + 28 MB session files per run (parent plan §14); two runs plus one per J1 fix: about 150–360 MB. WSL: the same arithmetic over the WSL store, measured by the dry run before the first write | The operator, by hand, after the `RESULTS_` file is committed. The mode never deletes | A crashed run leaves its folder. A folder without `check.json` is a run killed mid-copy. The next run makes its own folder |
| Working outputs of N5–N8 (summaries, JSON, the judge sheet) | under 1 MB per side | The operator. Only the `RESULTS_` file and what it cites are committed | none |
| `RESULTS_who_wrote_the_code_under_review.md` | about 30 KB | none, it is a record | none |

## 6. Stories and build order

N1–N3 are code or script work. They need no approval and no live store. N4 is attended. N5–N9 touch live data.

```
N1 ─┐
N2 ─┼─► (release carrying E5, E6, E7) ─► N5 ─► N6 ─► N7 (Windows) ─► N8 (WSL) ─► N9 hand-check ─► N10 answer
N3 ─┘          N4 (any time, attended)
```

### N1 — The servers pre-flight script

- **Goal.** One read-only script says whether every running `coai-mcp` of a side is on the release. It never stops a process.
- **Files.** `scripts/backfill-preflight-servers.ps1` (Windows) and `scripts/backfill-preflight-servers.sh` (WSL, run from a script file); a `node --test` file for the parts that parse the version output (`scripts/lib/`).
- **Behaviour.** The script lists each `coai-mcp` process by executable path and asks each path `--version`. It exits 1 and names the path when any is not the expected version. It exits 0 when all are. It takes the expected version as an argument.
- **Why not in the mode.** A cross-platform process walk is new scope. `ProcessLiveness` (`src_mcp/src/Server/ProcessLiveness.cs:17`) answers by process id only (E7 §6 O1).
- **Tests.** The version-parsing helper runs under `node --test "scripts/**/*.test.mjs"` (the CI step *Probe script tests*). A fixture with three process lines (two current, one older) gives exit 1 and names the older path. A break-it makes the comparison `>=` instead of equality and goes red.

### N2 — The round dump for the judge

- **Goal.** The judge reads one round's stored authors without writing a query during the run.
- **Files.** `scripts/round-authors-dump.mjs`; its `node --test` file; one C# test in `src_mcp/tests` that dumps a round of the S4 scenario store and compares it with the rows the test seeded.
- **Behaviour.** One round, chosen by `round_key` prefix, gives JSON: its `rounds.authors_*` columns, its `round_authors` rows, and the `Authors` member of the session file's round. It opens the database read-only. On Node 20 (WSL) there is no `node:sqlite`, so the script reads the database through the Python 3 `sqlite3` module that `scripts/export-code-rounds.py` already uses, or takes a JSON export of the rows. N2 picks one and says why.
- **Reuse first.** Read `scripts/export-code-rounds.py` and `scripts/lib/caller-transcripts.mjs` before writing the reader. Do not write a second store opener.
- **Tests.** The C# fixture test is RED first against a script that prints `{}`.

### N3 — The report oracle in the S4 scenario

- **Goal.** Close the part of E7.S4 that the E7 branch could not build, because E6 was not on it.
- **What changes.** In `BackfillAuthorsCliScenarioTests.cs`, `BackfillTwice_TheSecondRunChangesNoTotals` also runs `--round-authors` (JSON) after each backfill run and asserts the two documents are byte-identical. `ARowWhoseNamedPeerRoundsAreFinal_IsRecomputed` also asserts that the `provisional` count in the JSON is one lower after the run (E7 S4 test list).
- **Tests.** RED first: the first assertion fails if S3's `DELETE` before the insert is removed (the report totals differ). Report the failure message and the pass.

### N4 — The leftovers of epic E1

These need an attended session or a longer sample. They do not block N5–N10. They change what the RESULTS file may claim.

1. **The `/clear` probe.** It exists only in an interactive Claude Code session. Record: the registry `sessionId` after `/clear`; the variable seen by a new Bash child; the variable held by an already-running MCP child (in WSL, `/proc/<pid>/environ` of the child found by parent pid, never by image name); and which `.jsonl` the post-clear turns land in. **Checked so far:** the headless probes for `--resume` and `--no-session-persistence`, and the inheritance probe (E1 RESULTS Q4). **Not checked:** `/clear` itself. The owner answered "ok" about `/clear` on 2026-10-16 (E7 §10 item 4). That is an acceptance, not a measurement. A cleared session's rounds may carry a stale session id. A backfill cannot detect this.
2. **The line-by-line hand read.** E1 did a weaker check: a second script recomputed the window counts for three rounds per side. Nobody read the lines. N9 covers this for the sampled rounds. State in the RESULTS file that E1's check was weaker.
3. **The torn-tail frequency under heavy writing.** E1 sampled 0 torn last lines in 4 107 reads (Windows) and 5 272 (WSL), with 2 and 7 growth steps seen. That is "never seen", not "cannot happen". The constant `CallerTranscript.TornTailCountsAsPartial` stays `false` (parent plan §16 D22). Sample again while a large session writes, or keep the statement as it is.

### N5 — O1: before anything is written

Full text: E7 §6 O1. In short, record in a working folder, per side, before any writing run:

1. **The release.** The first `mcp-v*` tag that contains `42eb3a95a` (`git merge-base --is-ancestor 42eb3a95a mcp-v<x>`), and `coai-mcp --version` per side. On WSL, install the released `linux-x64` asset. No build happens in WSL.
2. **N1 exits 0** on each side.
3. **The rehearsal on a copy.** A quiescent copy of `coai.db`, `-wal`, `-shm` and `sessions/` in a new scratch folder, `COAI_DATA_DIR` pointed at it. Run `coai-mcp --backfill-authors --since 2026-09-30T00:00:00Z` (a writing run) and run it again. The second run must show every `written` count 0. This is a rehearsal of the released binary's backup and write path. It does not replace the backup of O2 or O3.
4. **The shaper-regime check.** `git log --since=2026-09-30 -L` over `DiffExclusions.Default` (`src_mcp/runners/Context/ContextAssembler.cs:7-30`) and `DiffShaper.DefaultMaxBytes` (`src_mcp/core/Context/DiffShaper.cs:28`). If either changed inside the window, mark each earlier round with a `regime` mark in the RESULTS file and keep it out of any per-100-line figure that reads `shown` or `elided`.
5. **The predictions**, written into the draft RESULTS file BEFORE any run: per side, the rounds selected; the share found `observed` or `partial`; the counts per unrecoverable reason (including `size: base not an ancestor`); the rounds that end `partial: read budget`; the share of corrective rounds whose delta ends `unavailable`, per reason; the run time. The E1 coverage counts are observations: 399 of 405 Windows code rounds and 516 of 516 in WSL had a transcript.
6. **The sample rule**, written before the run, so the judge cannot get a convenient ten (E7 §6 O1).
7. **The dry runs**, both sides, the WSL one from a script file. Compare each summary with its prediction. Explain any miss in writing. Do not change the prediction.
8. **The `before` report**: `coai-mcp --round-authors --since 2026-09-30T00:00:00Z` (JSON), both sides, kept for N10.

### N6 — A fix story for a defect that J1 finds (only if needed)

If N9 finds any disagreement, stop and report no number. The round becomes a fixture. The test is RED first. The fix bumps E3's `ReaderVersion` or E4's `RoundAttribution.Version`. The story says whether the bump **corrects** or **refines**. A correcting bump sets `BackfillDecision.CorrectedBelow` to the defective version, so the next run replaces every row of that version whatever its strength. N7 and N8 run again, each with its own backup, and N9 checks all ten rounds of that side again.

### N7 — O2: back up and backfill Windows, twice (owner approval first)

Precondition: §4 rules 1–3 and 8; N1 exit 0; the rehearsal passed; the dry run of this binary over this store exited 0 the same day; no round running on the side. Run `coai-mcp --backfill-authors --since 2026-09-30T00:00:00Z`. Record the summary, its `backupCheck` and the backup folder. Run `--round-authors` and keep the JSON. Run the mode again (it backs up first). Record the summary. Run `--round-authors` again and compare the two JSON documents by SHA-256. If `deferred` is above 0, run again. A session deferred on two consecutive runs is held by a live server (the claim is an OS lock). The operator waits or asks for the window to close. If it still holds, record the session ids and treat those rounds as an unfillable stratum for N9.

**Pass.** Every writing run's `backupCheck` passed before it wrote. The second run reports every `written` count 0 and `healed` 0, or only heals it can name. The two `after` JSON documents are byte-identical. The `provisional` count after is the count before minus the first run's `recomputedProvisional`, and `kept.peerStillRunning` explains the rest. Each backup folder opens read-only with the counts its summary printed.

### N8 — O3: back up and backfill WSL, twice (owner approval first)

N7's sequence. Every command is in a script file written on the Windows side. Each script writes to a new folder `~/coai-backfill/<utc>/`. The second run's `--round-authors` JSON, copied read-only to the Windows working folder with its SHA-256 recorded, is `wsl.json`. N10 uses that file. N10 generates no fresh one.

### N9 — J1: hand-check ten rounds per side

A fresh Opus judge (`model: opus`) that wrote and reviewed none of the code of epics E3–E7, or the operator. The judge gets the sample, `--log` output, the N2 dump, read access to the transcripts and the repositories. The judge does not get the reader's source. The judge writes its own token-counting helper and imports no product code. Per round, check: the state and its reason; per agent the requested and observed model, effort, association and the write that earned it, window and launches; tokens once per `message.id`; the ambiguous totals; the size against `git diff --numstat -z <rounds.base_ref>..<head>` by hand (the recorded base, never a merge-base taken today); and that the findings and resolutions read the same as in the backup. **Pass: 10 of 10 per side agree on every field.** Record `round_key` prefixes and fields only. No transcript text, no path.

### N10 — O4: the answer

Inputs: the second-run `--round-authors` JSON of N7 and the `wsl.json` of N8, verbatim, with their SHA-256. Run `--round-authors --since 2026-09-30T00:00:00Z --split 2026-10-13T00:00:00Z --add <wsl.json> --table` for `--cohort first` and `--cohort corrective` (never pooled), with and without `--with-before-open`. Write `research/RESULTS_who_wrote_the_code_under_review.md` in this order: header (association only, conditions), the hand-check, predictions against observations (with `provisional` and `size coverage unknown` before and after, per cell and side), the tables, the naive date split of the parent plan §1 re-derived beside them, and what the file does not settle. The repository is public. Name rounds by `round_key` prefix. Anonymise repositories outside this family and their branches. Commit `wsl.json` only if it holds no branch name and no path. Print no backup path. Run the private-data scan before any push. Then promote this plan with `/promote-plan`, and add the run's own paragraph to `research/module_server.md`.

## 7. Not in this plan, by decision (kept so the promoted plans can point here)

| Item | Why it stays out | Source |
|---|---|---|
| A per-window schema step (the window states and totals projected to the database) | The owner said "not now" on 2026-10-18 (question 12). The report states the roll-up limit in its header. | E6 stories §4 item 9, §5 |
| Authors for feature rounds and for the merge | Left for later by the parent plan §7 ("at merge: not recorded"). | Parent plan §7 |
| A transcript reader for codex and gemini callers | A later plan behind the same `ObservedSession` record. Those rounds read `unobserved: no reader for <vendor>`. | Parent plan §3 |
| `peer running` for a still-running peer round | A running round has no caller on disk, so the state is unreachable in production. The grown-peer-set rule carries the repair. Making it reachable needs an owner decision. | E5 deviation 8; E7 §11 deviation 12 |
| A connection-string builder for every store opener | `RoundsDb.cs:132`, `RoundCounts.cs:98`, `AuthorPeers.cs:78` and others interpolate the path. A `;` in a data directory breaks them all. This is not an E7 defect. The owner has not answered. | E7 §11.3 |
| The residual limits of the E3 write-path guard | A custom autofs map, a network file system inside a POSIX tree, and a junction followed under a local drive. The code states them. | E3 §5.1 |

## 8. Test plan

| Test | Proves | Story |
|---|---|---|
| Version helper over three process lines | exit 1 names the older path; exit 0 when all match | N1 |
| Dump of a seeded S4 scenario round equals the seeded rows | the judge reads what the store holds | N2 |
| `BackfillTwice…` with two `--round-authors` documents | a second run changes no report byte | N3 |
| `ARowWhoseNamedPeerRoundsAreFinal…` with the `provisional` count | the report and the backfill agree on what a repair changes | N3 |
| Rehearsal summaries on a copy | the released binary backs up, writes and re-runs to zero | N5 |
| Dry-run and writing-run counts per side | the predictions held, or each miss is explained | N5, N7, N8 |
| Ten rounds per side, every field | the product's numbers equal the judge's | N9 |

Run C# tests from the executable, never `dotnet test`: `dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4`, then `node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*BackfillAuthorsCliScenario*"`. Run script tests with `node --test "scripts/**/*.test.mjs"`.

## 9. Owner decisions this plan needs

1. Approval of the Windows run (N7), before it starts.
2. Approval of the WSL run (N8), before it starts.
3. The release date: N5 waits for a release that contains E5, E6 and E7.
4. Whether the `;`-in-path question of §7 becomes its own plan.

## Definition of Done

- [ ] N1 and N2 are committed with their tests green; N3 is committed with its RED run reported.
- [ ] A release carrying E5, E6 and E7 exists. Its tag and `coai-mcp --version` per side are in the RESULTS file.
- [ ] N1 exited 0 on each side before that side's first write.
- [ ] The rehearsal on a copy passed on each side. The shaper-regime decision is written. The predictions and the sample rule were written before the first run.
- [ ] Each live run had the owner's written approval, a dry run of the same binary over the same store the same day, and a verified backup made by the run itself before it wrote.
- [ ] The second run on each side wrote nothing, and the two `after` report documents are byte-identical.
- [ ] Nothing was deleted on either side. No backup was removed by a session.
- [ ] Ten rounds per side were checked by a judge who wrote none of the code, and 10 of 10 agreed on every field, before any number was published.
- [ ] `research/RESULTS_who_wrote_the_code_under_review.md` exists in the order of N10, with association only in its header. Every figure traces to a kept output.
- [ ] The N4 leftovers are done, or the RESULTS file states each as not done.
- [ ] The private-data scan of the added lines is clean.
- [ ] `node .agents/conventions/tools/plan-lifecycle.mjs` is clean, and this plan is promoted with its deviations and its open tail.
