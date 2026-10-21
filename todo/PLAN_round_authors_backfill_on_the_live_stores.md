# PLAN — round authors: backfill the two live stores, check ten rounds per side by hand, then answer the owner

> Status: **plan only, nothing implemented yet, 2026-10-21.** Scope: the open tail of the promoted [round-authors plan](../research/PLAN_a_round_records_who_wrote_the_code_and_how_much.md) and its [E7 stories](../research/PLAN_a_round_records_who_wrote_the_code_and_how_much_E7.md). The `--backfill-authors` mode itself is built and merged (#865). This plan holds what remains: two scripts, one missing test oracle, the owner-approved runs on the live stores, the hand-check, the `RESULTS_` file, and the small leftovers of epic E1. No new reader, no new attribution rule, no schema step.
>
> Related docs: [module_server.md](../research/module_server.md) (*`--backfill-authors`*, *`--round-authors`*), [module_tests.md](../research/module_tests.md), [RESULTS_caller_transcripts_on_both_sides.md](../research/RESULTS_caller_transcripts_on_both_sides.md), [PLAN_a_session_file_keeps_what_a_newer_server_wrote.md](../research/PLAN_a_session_file_keeps_what_a_newer_server_wrote.md).
>
> **How this plan is built.** Nothing in this plan is built in the promotion PR. Before any code of this plan is built, the plan is split into epics. Fable does the split, after it asks the question consultant. Each epic gets its own coai plan gate and its own code gate. Every live run needs the owner's written approval (§4 rule 1).

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

The promoted [E7 stories](../research/PLAN_a_round_records_who_wrote_the_code_and_how_much_E7.md) carry the same table (*Boundary with the live-backfill plan* there). Items 2–4 go first, because the live runs need them.

| # | Item | Built by | The other plan's part |
|---|---|---|---|
| 1 | S1–S4: the decider, the stored-history rebuild, the narrow writer, the mode, its backup and dry run | **E7 (shipped, #865)** | The live-backfill plan runs the mode and changes none of its code. A defect J1 finds gets a RED-test story in the live-backfill plan (N6). |
| 2 | `scripts/backfill-preflight-servers.ps1` and `.sh` | **the live-backfill plan, N1** | E7 §6 O1 names the script and its exit codes. |
| 3 | `scripts/round-authors-dump.mjs` and its tests | **the live-backfill plan, N2** | E7 §6 J1 names the script. |
| 4 | The `--round-authors` JSON identity in the S4 scenario | **the live-backfill plan, N3** | E7 S4 lists the test. The shipped scenario uses `StoreDump` (E7 §11 deviation 6). |
| 5 | O1–O4 and J1: the release record, rehearsal, dry runs, two live runs, hand-check, the `RESULTS_` file | **the live-backfill plan, N5–N10** | E7 §6 is the design text. The live-backfill plan is the work. |
| 6 | O5: module docs, `.agents/PROJECT.md` entries, the promotion of the parent with its seven stories | **the promotion commit of 2026-10-21** | The live-backfill plan adds the run's own paragraph to `module_server.md`. |
| 7 | E1 leftovers: the attended `/clear` probe, the line-by-line hand read, the torn-tail frequency | **the live-backfill plan, N4** | E1's *Open tail* names them. |

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
4. **Idempotent.** The second run reports zero writes and zero heals. Any non-zero count fails the idempotence check. A heal on a second run is a separate named state; it does not pass. The dump of the authors and size tables (database rows, which hold no read time) is byte-identical. The `--round-authors` JSON is the same after the `readUtc` normalisation (N3).
5. **A weaker result never replaces a stronger one.** The strength order is `observed` > `partial` > `unobserved` > empty. A result from an older reader never replaces a row of a newer reader. `CorrectedBelow` is the only exception, and only a RED-test fix story that proves a version wrong may set it.
6. **A live size is never replaced.** A round that already has a `size_state` keeps it. The backfill never re-measures a size it wrote.
7. **Nothing is deleted.** The mode deletes no backup, no session file and no row it did not write. In WSL no agent deletes anything: no `rm`, no `mv` over an existing file, no cleanup step.
8. **No running server on an older release.** An older `coai-mcp` that saves a session file drops the `authors` member (E5 measured it: 13 221 bytes became 3 246 bytes after one `open`). Every running server of the side must be on the release before the first write. N1 checks this on the RUNNING process, not on the file on disk. `<path> --version` reports the binary on disk. An old server that is still running after an upgrade replaced its executable would pass that check, and would strip `authors` on its next session-file save. So every running `coai-mcp` process must have a start time LATER than the last-write time of the installed binary. A process that started before the install is a failure, even when its path reports the new version. If a start time cannot be read, N1 fails closed (exit 1).
9. **WSL runs from script files.** Every WSL command runs from a script file written on the Windows side and started as `wsl.exe -e bash <script>`. No value goes through `wsl.exe`'s command line. Every output goes to a new folder.
10. **No process is killed by image name.** If a session holds a claim, the operator waits for it or asks for the window to close.

## 5. Growth

| Surface | Projected size | Who retires it | If interrupted |
|---|---|---|---|
| `<dataDir>/backfill-backups/<utc>/` (one `coai.db` copy and the session files per WRITING run, a refused run included) | Windows: 72 MB database + 28 MB session files per run (parent plan §14); two runs plus one per J1 fix: about 150–360 MB. WSL: the same arithmetic over the WSL store, measured by the dry run before the first write | The operator, by hand, after the `RESULTS_` file is committed. The mode never deletes | A crashed run leaves its folder. A folder without `check.json` is a run killed mid-copy. The next run makes its own folder |
| Working outputs of N5–N8 (summaries, JSON, the judge sheet) | under 1 MB per side | The operator. Only the `RESULTS_` file is committed. It cites the other outputs by SHA-256 and by a named private location outside the repository (N10) | none |
| `RESULTS_who_wrote_the_code_under_review.md` | about 30 KB | none, it is a record | none |

## 6. Stories and build order

N1–N3 are code or script work. They need no approval and no live store. N4 is attended. N5–N9 touch live data. N9a decides whether a disagreement is a product defect.

```
N1 ─┐
N2 ─┤
N3 ─┼─► (release carrying E5, E6, E7) ─► N5 ─► N7 (Windows) ─► N8 (WSL) ─► N9 hand-check ─► N9a adjudication ─┬─► N10 answer
N4 ─┘                                          ▲                                                              │  no confirmed defect
                                               └──────────────── N6 fix story ◄───────────────────────────────┘
                                                                       only on a confirmed product defect
```

N6 is a loop-back. It starts only when N9a confirms a product defect. After the fix lands, the run goes back to N7 (then N8, N9 and N9a again). When N9a finds no confirmed defect, the run goes to N10. N4 is attended: it is done before N5, or the operator records each open N4 item as not done and the RESULTS file states it (N10).

### N1 — The servers pre-flight script

- **Goal.** One read-only script says whether every RUNNING `coai-mcp` of a side is on the release. It checks the running process, not only the file on disk. It never stops a process that it did not start itself.
- **Files.** `scripts/backfill-preflight-servers.ps1` (Windows) and `scripts/backfill-preflight-servers.sh` (WSL, run from a script file); a `node --test` file for the parts that parse the version output and compare times (`scripts/lib/`).
- **Behaviour.**
  1. The script lists each `coai-mcp` process by process id, executable path and start time. It takes the expected version as an argument.
  2. It asks each distinct path `--version`. It exits 1 and names the path when any path is not the expected version.
  3. **Start-time rule.** `--version` reports the binary on disk. A server that was started before an upgrade replaced its executable still runs the old code, whatever its path now reports. So the script also reads the last-write time of the installed binary. Every running `coai-mcp` process must have a start time LATER than that last-write time. A process that started before the install is a failure, even when its path reports the new version. On Linux, a process whose `/proc/<pid>/exe` link ends in ` (deleted)` is the same failure. Both times are compared in UTC.
  4. **Fail closed.** If a start time or a last-write time cannot be read, the script exits 1 and names the process id. It never treats "unknown" as "fine".
  5. **Bounded `--version`.** Every `--version` call has a per-process timeout of 5 seconds, in both scripts. On timeout the script kills ONLY the child it started, by that child's process id. It never kills by image name and never touches a server it did not start. A timeout is a verification failure: exit 1, with the path named.
  6. Exit 0 only when every process passes steps 2 to 5. Exit 65 when the expected-version argument is missing.
- **LF only for `.sh`.** The `.sh` scripts are LF-only. Build step (do not edit `.gitattributes` in the promotion PR): `.gitattributes` must carry `scripts/*.sh text eol=lf`. Checked on 2026-10-21: the file already has the wider line `*.sh text eol=lf` (line 3), which covers these scripts. The build step adds the narrower `scripts/*.sh text eol=lf` line too, so the rule survives if the wider line is ever narrowed. The first command in each `.sh` is a guard that refuses to run when the file contains a carriage return (for example `grep -q $'\r' "$0" && { echo "CR in script" >&2; exit 1; }`, written on ONE line). A CRLF copy then exits non-zero before any other command runs.
- **Why not in the mode.** A cross-platform process walk is new scope. `ProcessLiveness` (`src_mcp/src/Server/ProcessLiveness.cs:17`) answers by process id only (E7 §6 O1).
- **Tests.** The version-parsing and time-comparing helpers run under `node --test "scripts/**/*.test.mjs"` (the CI step *Probe script tests*). The scripts themselves run against fake binaries (the `.ps1` test where `pwsh` exists, the `.sh` test on Linux).
  - A fixture with three process lines (two current, one older) gives exit 1 and names the older path.
  - A fixture with a process whose path reports the new version but whose start time is BEFORE the binary's last-write time gives exit 1.
  - A fixture whose start time cannot be read gives exit 1 (fail closed).
  - A hung binary (a fake that sleeps 30 seconds on `--version`) gives exit 1 in about 5 seconds, names the path, and leaves no child of the script running. A decoy process with the same image name, started by the test, is still alive afterwards.
  - A CRLF copy of each `.sh` exits non-zero and runs nothing after the guard.
  - Break-its: the comparison `>=` instead of equality goes red; the start-time rule removed goes red; the timeout removed goes red (the hung-binary test hangs and the test timeout fails it).

### N2 — The round dump for the judge

- **Goal.** The judge reads one round's stored authors without writing a query during the run.
- **Files.** `scripts/round-authors-dump.mjs`; its `node --test` file; one C# test in `src_mcp/tests` that dumps a round of the S4 scenario store and compares it with the rows the test seeded.
- **Behaviour.** One round, chosen by `round_key` prefix, gives JSON: its `rounds.authors_*` columns, its `round_authors` rows, and the `Authors` member of the session file's round. N2 commits to ONE extraction mechanism: the Python 3 `sqlite3` module, as `scripts/export-code-rounds.py` already uses, on BOTH Windows and WSL. The connection is opened read-only with a `file:...?mode=ro` URI (`export-code-rounds.py:74-75` is the pattern). There is no "JSON export" branch. Reason: Node 20 in WSL has no `node:sqlite`, and a second path (an export step on one side, a direct read on the other) would give two code paths and two output shapes. One mechanism gives one code path and the same output shape on both sides. The `.mjs` script does only the session-file part and the output; it starts the Python reader as a child (`py -3` on Windows, `python3` in WSL).
- **Invocation contract.**
  - Arguments: `node scripts/round-authors-dump.mjs <dataDir> <round_key prefix> <name.json>`. The prefix must match exactly one round.
  - Output file: `<name.json>` is a file name only. The file is written into the folder named by `COAI_EXPORT_DIR` (as `export-code-rounds.py` does), through a temp file and a rename. The script never writes into the data directory and never overwrites a store file. The JSON holds no path and no transcript text.
  - Exit codes (the same as `export-code-rounds.py`): `0` the file was written; `65` a bad argument or a refusal (wrong argument count, a prefix that matches no round or more than one round, an output named like a store file, `COAI_EXPORT_DIR` not an existing folder); `74` a read failed (a SQLite error or an I/O error). One line goes to stderr.
- **Reuse first.** Read `scripts/export-code-rounds.py` and `scripts/lib/caller-transcripts.mjs` before writing the reader. Do not write a second store opener. If the read-only opener and its refusals are private inside `export-code-rounds.py`, extract them into one shared module under `scripts/lib/` and let both scripts call it.
- **Tests.** The C# fixture test is RED first against a script that prints `{}`. A `node --test` case runs the script against a seeded store and checks the three exit codes (0, 65, 74).

### N3 — The report oracle in the S4 scenario

- **Goal.** Close the part of E7.S4 that the E7 branch could not build, because E6 was not on it.
- **What changes.** In `BackfillAuthorsCliScenarioTests.cs`, `BackfillTwice_TheSecondRunChangesNoTotals` also runs `--round-authors` (JSON) after each backfill run and asserts the two documents are the same after the `readUtc` normalisation: in each document, every `sources[*].readUtc` value (the clock time of the read, the only field that differs per run) is replaced by one fixed text, and everything else is compared exactly. (`readUtc` is set from the clock in `RoundAuthorsMode.cs:138` and printed from `RoundAuthorsMerge.cs:15`; no other per-run field exists.) `ARowWhoseNamedPeerRoundsAreFinal_IsRecomputed` also asserts that the `provisional` count in the JSON is one lower after the run (E7 S4 test list).
- **Tests.** RED first: the first assertion fails if S3's `DELETE` before the insert is removed (the report totals differ). Report the failure message and the pass.

### N4 — The leftovers of epic E1

These need an attended session or a longer sample. They need no live write. N5 starts after N4 is done, or after the operator records each open item as not done. They change what the RESULTS file may claim.

1. **The `/clear` probe.** It exists only in an interactive Claude Code session. Record: the registry `sessionId` after `/clear`; the variable seen by a new Bash child; the variable held by an already-running MCP child (in WSL, `/proc/<pid>/environ` of the child found by parent pid, never by image name); and which `.jsonl` the post-clear turns land in. **Checked so far:** the headless probes for `--resume` and `--no-session-persistence`, and the inheritance probe (E1 RESULTS Q4). **Not checked:** `/clear` itself. The owner answered "ok" about `/clear` on 2026-10-16 (E7 §10 item 4). That is an acceptance, not a measurement. A cleared session's rounds may carry a stale session id. A backfill cannot detect this.
2. **The line-by-line hand read.** E1 did a weaker check: a second script recomputed the window counts for three rounds per side. Nobody read the lines. N9 covers this for the sampled rounds. State in the RESULTS file that E1's check was weaker.
3. **The torn-tail frequency under heavy writing.** E1 sampled 0 torn last lines in 4 107 reads (Windows) and 5 272 (WSL), with 2 and 7 growth steps seen. That is "never seen", not "cannot happen". The constant `CallerTranscript.TornTailCountsAsPartial` stays `false` (parent plan §16 D22). Sample again while a large session writes, or keep the statement as it is.

### N5 — O1: before anything is written

Full text: E7 §6 O1. In short, record in a working folder, per side, before any writing run:

1. **The release.** The first `mcp-v*` tag that contains `42eb3a95a` (`git merge-base --is-ancestor 42eb3a95a mcp-v<x>`), and `coai-mcp --version` per side. On WSL, install the released `linux-x64` asset. No build happens in WSL.
2. **N1 exits 0** on each side (it checks the running processes and their start times, not only `--version`).
3. **The rehearsal on a copy.** A quiescent copy of `coai.db`, `-wal`, `-shm` and `sessions/` in a new scratch folder. The rehearsal is a rehearsal of the released binary's backup and write path. It does not replace the backup of O2 or O3. It has five parts, in this order. The writing run (part d) starts only after parts a, b and c passed.
   - **a. Prove the data-directory redirection, by reading the code.** The released binary reads its data directory from the environment variable `COAI_DATA_DIR`. The read is `ResolveDataDir` in `src_mcp/src/Server/PanelSettings.cs:892`. Use that exact name. Three traps come from the same function. (1) An empty or whitespace `COAI_DATA_DIR` makes the binary fall back to `DefaultDataDir` (`PanelSettings.cs:894`, defined at `:693`), and that is the LIVE store. The rehearsal script refuses to start when the variable is empty. (2) `COAI_DATA_SIDE` (`PanelSettings.cs:898`, joined at `:930`) moves the store to `<root>/<side>`. The script unsets it, or puts the copy at `<root>/<side>`. (3) A named folder that does not exist is exit 74 for a writer mode (`Program.cs:330-334`), which is the safe answer.
   - **b. Prove it by a read-only one-shot.** Run `coai-mcp --backfill-authors --dry-run --since 2026-09-30T00:00:00Z` with `COAI_DATA_DIR` set to the scratch copy. Assert that the summary reports the copy's known contents. The sentinel is the copy's round count, which the operator counts first, read-only, with the Python `sqlite3` reader of N2 over the copy. A summary that reports another count means the binary read another store: stop.
   - **c. Prove the live store did not move.** Before part b and after part b, record the last-write time of each live store file (`coai.db`, `-wal`, `-shm`) and the SQLite `PRAGMA data_version` of the live `coai.db` (read through a read-only connection). Assert that all of them are equal. Any difference stops the rehearsal and is reported.
   - **d. The writing rehearsal.** Run `coai-mcp --backfill-authors --since 2026-09-30T00:00:00Z` (a writing run) on the copy, and run it again. The second run reports zero writes and zero heals. Any non-zero count fails the idempotence check. A heal on a second run is a separate named state; it does not pass. Repeat the check of part c after the writing run.
   - **e. The deferred path.** On a second, new scratch copy (never by restoring over the first one), hold one session's claim: for example start a `coai-mcp` server on the scratch directory with that session open. Run the mode. Assert that the summary shows `deferred.busy` above 0 (the summary counts rounds and names no session) and the other three `deferred` fields at 0, that it exits 75, that it writes nothing for the held session (the operator knows which one it is; its file and its rows are byte-identical before and after), and that the database still passes `integrity_check`. Then release the claim and run the mode again. Assert that this run fills the session and that a third run reports zero writes and zero heals.
4. **The shaper-regime check.** `git log --since=2026-09-30 -L` over `DiffExclusions.Default` (`src_mcp/runners/Context/ContextAssembler.cs:7-30`) and `DiffShaper.DefaultMaxBytes` (`src_mcp/core/Context/DiffShaper.cs:28`). If either changed inside the window, mark each earlier round with a `regime` mark in the RESULTS file and keep it out of any per-100-line figure that reads `shown` or `elided`.
5. **The predictions**, written into the draft RESULTS file BEFORE any run: per side, the rounds selected; the share found `observed` or `partial`; the counts per unrecoverable reason (including `size: base not an ancestor`); the rounds that end `partial: read budget`; the share of corrective rounds whose delta ends `unavailable`, per reason; the run time. The E1 coverage counts are observations: 399 of 405 Windows code rounds and 516 of 516 in WSL had a transcript.
6. **The sample rule**, written before the run, so the judge cannot get a convenient ten (E7 §6 O1).
7. **The dry runs**, both sides, the WSL one from a script file. Compare each summary with its prediction. Explain any miss in writing. Do not change the prediction.
8. **The `before` report**: `coai-mcp --round-authors --since 2026-09-30T00:00:00Z` (JSON), both sides, kept for N10.

### N6 — A fix story for a defect that J1 finds (only if needed)

N6 starts only when N9a has confirmed that the PRODUCT is wrong. A disagreement that N9a resolves as a judge error does not start N6. When N6 starts, stop and report no number. The round becomes a fixture. The test is RED first. The fix bumps E3's `ReaderVersion` or E4's `RoundAttribution.Version`. The story says whether the bump **corrects** or **refines**. A correcting bump sets `BackfillDecision.CorrectedBelow` to the defective version, so the next run replaces every row of that version whatever its strength. The run then goes back to N7: N7 and N8 run again, each with its own backup, and N9 and N9a check all ten rounds of that side again.

### N7 — O2: back up and backfill Windows, twice (owner approval first)

Precondition: §4 rules 1–3 and 8; N1 exit 0 (running processes started after the install); the rehearsal passed; the dry run of this binary over this store exited 0 the same day; no round running on the side. Run `coai-mcp --backfill-authors --since 2026-09-30T00:00:00Z`. Record the summary, its `backupCheck` and the backup folder. Run `--round-authors` and keep the JSON. Run the mode again (it backs up first). Record the summary. Run `--round-authors` again. Compare the two JSON documents after the `readUtc` normalisation (N3), by SHA-256 of the normalised text; record the SHA-256 of both raw files too (they differ, because `readUtc` differs).

If the exit code is 75, `deferred` is above 0. Record the four `deferred` counts exactly as the summary prints them (`busy`, `unreadable`, `changedUnderUs`, `peersUnknown`) and its `next` sentence, then run again. The summary counts rounds per reason. It names no session and no round (`BackfillSummary.cs:13`, `:27-42`), so no run can say which session is held. Read each reason on its own:

- `busy`: the session claim (an OS lock) or the database write lock was not free (`BackfillWriter.cs:93-102`; one count covers both). A running server or a running round can cause it. It is the only reason that can mean a live-server lock, and even then the count does not say which of the two locks it was.
- `unreadable`: the session file did not load (`BackfillDecision.cs:74-76`). No lock explains it.
- `changedUnderUs`: a live server wrote the round between the read and the write (the fingerprint changed). The next run reads the new state.
- `peersUnknown`: the lookup of the peer rounds did not complete, and the round's state names a running peer (`BackfillDecision.cs:127`).

Do not call a session "held by a live server" from a count, from two runs in a row, or from the total of `deferred`. A `busy` count that stays above 0 on consecutive runs, while N1 shows a running server on this store, supports "a live lock" for the rounds counted, and nothing more. The operator waits or asks for the window to close, then runs again. If a count still stays above 0, record the four counts of every run in the RESULTS file, and treat the rounds counted as an unfillable stratum for N9, counted by reason and not named. Do not record session ids that the tool did not print.

**Pass.** Every writing run's `backupCheck` passed before it wrote. The second run reports zero writes and zero heals. Any non-zero count fails the idempotence check. A heal on a second run is a separate named state; it does not pass. The two `after` JSON documents are the same after the `readUtc` normalisation (N3). The `provisional` count after is the count before minus the first run's `recomputedProvisional`, and `kept.peerStillRunning` explains the rest. Each backup folder opens read-only with the counts its summary printed.

### N8 — O3: back up and backfill WSL, twice (owner approval first)

N7's sequence. Every command is in a script file written on the Windows side. Each script writes to a new folder `~/coai-backfill/<utc>/`. The second run's `--round-authors` JSON (default selection) is the idempotence check, as in N7. After the second run, and before anything else writes the WSL store, make one more export per selection that N10 uses. Each export has the same flags as its N10 report: `--since 2026-09-30T00:00:00Z --split 2026-10-13T00:00:00Z`, no `--until`, and the cohort and `--with-before-open` of that selection. `--add` refuses a report whose selection differs in any field (`RoundAuthorsMerge.cs:159-177`: stage, since, until, split, cohort, kinds, with-before-open, without-elided, with-uncovered), so one export cannot serve two selections. The four exports:

1. `wsl-first.json`: `--cohort first`.
2. `wsl-first-before-open.json`: `--cohort first --with-before-open`.
3. `wsl-corrective.json`: `--cohort corrective`.
4. `wsl-corrective-before-open.json`: `--cohort corrective --with-before-open`.

Copy each read-only to a private working folder outside the repository, and record its SHA-256. N10 uses these files. N10 generates no fresh one. No file is committed (N10).

### N9 — J1: hand-check ten rounds per side

The judge is a fresh Opus judge that wrote and reviewed none of the code of epics E3–E7, or the operator, only if the operator wrote and reviewed none of the code of E3–E7. The judge runs through the `claude` CLI (Claude Code), by the model alias `opus`. It never runs through agy or codex. The RESULTS file records the CLI and the model the judge ran on (the CLI name and version, the alias, and the model id that the judge's own transcript shows, because an alias can resolve to a fallback model). The judge gets the sample, `--log` output, the N2 dump, read access to the transcripts and the repositories. The judge does not get the reader's source. The judge writes its own token-counting helper and imports no product code. Per round, check: the state and its reason; per agent the requested and observed model, effort, association and the write that earned it, window and launches; tokens once per `message.id`; the ambiguous totals; the size against `git diff --numstat -z <rounds.base_ref>..<head>` by hand (the recorded base, never a merge-base taken today); and that the findings and resolutions read the same as in the backup. Every field where the judge and the product disagree goes to N9a. **Pass: after N9a, 10 of 10 rounds per side agree with the judge on every field, or each disagreement was resolved by N9a as a judge error and is recorded.** A disagreement that N9a confirms as a product defect is not a pass: it starts N6. Record `round_key` prefixes and fields only. No transcript text, no path.

### N9a — Adjudication of every disagreement

- **Goal.** A disagreement between the judge and the product is not yet a defect. One judge can be wrong. N9a decides who is wrong before any code changes.
- **Behaviour.** Each field where the judge and the product disagree gets a second, independent determination. It is one of: a second fresh judge (the same rules as N9: `claude` CLI, alias `opus`, wrote and reviewed none of the code of E3–E7), which gets the same inputs but neither the first judge's answer nor the product's value for that field; or the operator, who walks the transcript line by line beside the judge's helper output.
- **Outcomes.**
  1. The second determination agrees with the product. The judge was wrong. Record the disagreement and its resolution in the RESULTS file. The run continues. There is no version bump and no `CorrectedBelow`.
  2. The second determination agrees with the judge. The product is confirmed wrong. N6 starts.
  3. The second determination agrees with neither. The field is unresolved. Stop, ask the question consultant, and do not go to N10 until the field is resolved.
- **Record.** `round_key` prefix, field name, the three values, and which determination was used. No transcript text, no path.

### N10 — O4: the answer

Inputs: the second-run `--round-authors` JSON of N7 and the four WSL exports of N8, verbatim, with their SHA-256. Run `--round-authors --since 2026-09-30T00:00:00Z --split 2026-10-13T00:00:00Z --add <export> --table` once per selection, and give each run the WSL export of the same selection: `--cohort first` with `wsl-first.json`, `--cohort first --with-before-open` with `wsl-first-before-open.json`, `--cohort corrective` with `wsl-corrective.json`, `--cohort corrective --with-before-open` with `wsl-corrective-before-open.json`. The cohorts are never pooled. An `--add` of a file from another selection is refused with exit 65; do not work around it. Write `research/RESULTS_who_wrote_the_code_under_review.md` in this order: header (association only, conditions), the hand-check, predictions against observations (with `provisional` and `size coverage unknown` before and after, per cell and side), the tables, the naive date split of the parent plan §1 re-derived beside them, and what the file does not settle. The repository is public. Name rounds by `round_key` prefix. Anonymise repositories outside this family and their branches. **The four WSL exports, and any raw `--round-authors` export (the Windows one included), are NEVER committed.** The RESULTS file cites each of them by its SHA-256 and by a named private location outside the repository (a label such as "the private evidence folder", not a path). Only the RESULTS file's own anonymised figures are committed. Print no backup path. Run the private-data scan before any push. Then promote this plan with `/promote-plan`, and add the run's own paragraph to `research/module_server.md`.

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
| A process that reports the new version but started before the binary's last-write time | exit 1: a running old server is a failure whatever its path reports | N1 |
| A process whose start time cannot be read | exit 1: the script fails closed | N1 |
| A hung fake binary on `--version` (`.ps1` and `.sh`) | exit 1 in about 5 seconds; only the script's own child is killed, by process id; a same-name decoy survives | N1 |
| A CRLF copy of each `.sh` | the first-line guard refuses; nothing after it runs | N1 |
| Dump script exit codes 0, 65, 74 over a seeded store, through Python `sqlite3` opened `mode=ro` | one extraction path, one output shape, no write to the store | N2 |
| Dump of a seeded S4 scenario round equals the seeded rows | the judge reads what the store holds | N2 |
| `BackfillTwice…` with two `--round-authors` documents | a second run changes nothing in the report, after the `readUtc` normalisation (N3) | N3 |
| `ARowWhoseNamedPeerRoundsAreFinal…` with the `provisional` count | the report and the backfill agree on what a repair changes | N3 |
| Rehearsal part b: the dry run on the scratch copy reports the copy's round count | `COAI_DATA_DIR` redirection works; the binary read the copy | N5 |
| Rehearsal part c: live store file last-write times and `data_version`, before and after | the live store was not touched | N5 |
| Rehearsal part d: summaries on a copy | the released binary backs up, writes and re-runs to zero writes and zero heals | N5 |
| Rehearsal part e: one session held by a live server on a scratch copy | the summary shows `deferred.busy` above 0 (exit 75), nothing is written for the held session, the database stays sound; after release the next run fills it | N5 |
| Dry-run and writing-run counts per side | the predictions held, or each miss is explained | N5, N7, N8 |
| Second run of the mode on each live side | zero writes and zero heals; any non-zero count fails | N7, N8 |
| Ten rounds per side, every field | the product's numbers equal the judge's, run through the `claude` CLI on alias `opus` | N9 |
| A second determination for each disagreeing field | a judge error is recorded and the run continues; a confirmed product defect starts N6 | N9a |

Run C# tests from the executable, never `dotnet test`: `dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4`, then `node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*BackfillAuthorsCliScenario*"`. Run script tests with `node --test "scripts/**/*.test.mjs"`.

## 9. Owner decisions this plan needs

1. Approval of the Windows run (N7), before it starts.
2. Approval of the WSL run (N8), before it starts.
3. The release date: N5 waits for a release that contains E5, E6 and E7.
4. Whether the `;`-in-path question of §7 becomes its own plan.

## Definition of Done

- [ ] Before any code of this plan was built, the plan was split into epics (Fable, after the question consultant), and each epic had its own coai plan gate and code gate.
- [ ] N1 and N2 are committed with their tests green; N3 is committed with its RED run reported.
- [ ] N1 checks the running process: every running `coai-mcp` started after the installed binary's last-write time, and an unreadable start time exits 1. Every `--version` call has a 5-second timeout that kills only the script's own child by process id. The `.sh` scripts are LF-only (`scripts/*.sh text eol=lf` in `.gitattributes`, and a first-line CR guard), and a test covers the hung binary, the stale process and the CRLF copy.
- [ ] N2 uses the Python 3 `sqlite3` module, read-only, on both sides, with the invocation contract (arguments, output file, exit codes 0, 65, 74) tested.
- [ ] A release carrying E5, E6 and E7 exists. Its tag and `coai-mcp --version` per side are in the RESULTS file.
- [ ] N1 exited 0 on each side before that side's first write.
- [ ] The rehearsal on a copy passed on each side, in parts a to e: the redirection variable `COAI_DATA_DIR` was proved by code and by a read-only one-shot that reported the copy's round count; the live store's last-write times and `data_version` did not change; the writing rehearsal and its second run passed; the deferred-session path was exercised. The shaper-regime decision is written. The predictions and the sample rule were written before the first run.
- [ ] Each live run had the owner's written approval, a dry run of the same binary over the same store the same day, and a verified backup made by the run itself before it wrote.
- [ ] The second run on each side reported zero writes and zero heals. Any non-zero count fails the idempotence check. A heal on a second run is a separate named state; it does not pass. The two `after` report documents are the same after the `readUtc` normalisation (N3).
- [ ] Nothing was deleted on either side. No backup was removed by a session.
- [ ] Ten rounds per side were checked by a fresh Opus judge that wrote and reviewed none of the code of epics E3–E7 (or the operator, only if the operator wrote and reviewed none of the code of E3–E7), run through the `claude` CLI on alias `opus` and never through agy or codex. The RESULTS file records the CLI and the model. Every disagreement went through N9a; N6 started only on a confirmed product defect; 10 of 10 rounds agreed on every field after N9a, before any number was published.
- [ ] `research/RESULTS_who_wrote_the_code_under_review.md` exists in the order of N10, with association only in its header. Every figure traces to a kept output. The four WSL exports (one per N10 selection, each added only to the Windows report of the same selection) and every raw `--round-authors` export were NOT committed. The RESULTS file cites each of them by its SHA-256 and by a named private location outside the repository.
- [ ] The N4 leftovers are done, or the RESULTS file states each as not done.
- [ ] The private-data scan of the added lines is clean.
- [ ] `node .agents/conventions/tools/plan-lifecycle.mjs` is clean, and this plan is promoted with its deviations and its open tail.
