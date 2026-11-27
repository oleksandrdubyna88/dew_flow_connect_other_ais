# E4 s5 t6 — this repository's own toolchain file selects tests (recorded run, 2026-11-27)

Task: `stories/s5-format-selection-rerun/tasks/t6-this-repository-selects`. No model ran. Every commit of the run was made by the test itself
(`ARecordedSelectedRunUnderTheBoundary` in `src_mcp/tests/AThisRepositoryToolchainFileTests.cs`, skipped unless `COAI_RECORD_SELECTED_RUN` names an
absent folder). It clones HEAD, makes a coder worktree of it (`CoderWorktree`), commits in the worktree, and runs `ChecksAtBase.RunCoderJobAsync`, the
product's own road, with `BoundaryJob.ForCoder` (the measured network defaults: restore and build on the host, test on a loopback device only,
`MemoryMax` 8000 MiB). Folder: `/home/jinx/coai-t3/scratch-e4s5t6/` (not `/tmp`); deleted at the end of each run.

**Verdict.** d1, d3, d4 done and tested. d2: red selected run, green selected run, then the FULL suite green inside the unit (exit 0), see "The recorded run".
The first recording found two things: the restore step (below) and 94 tests that failed only inside the unit (below); both are fixed.

## What the file says now

`.coai/toolchain.yaml`, project `src_mcp`: `build` and `test` unchanged; `testFiles: [tests]` (relative to the project: `src_mcp/tests`), and `testFor`:

```
/bin/sh -c 'x=./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests; for s in "$@"; do x="$x --filter-class CoaiMcp.Tests.$s"; done; exec $x' sel {stem}
```

(`{stem}` is an item of its own; the script turns each stem into one `--filter-class`, the argv s1 t1 measured as M3.) `src_vs_code` is not a project of the
file (comment in the file says why). **Found by the first recording, decided by the lead: a `restore` step** (`dotnet restore dew_flow_connect_other_ais.slnx`).

### Why the file has a restore step

A check unit binds the package, npm and dotnet folders read-only in every step but a restore (verify pass 3, M16), and a job folder is new and empty. Without
a restore step the build died after 10.5 s with exit 1: `System.IO.IOException: Read-only file system : '<job>/dotnet-home/.dotnet'` (the SDK's first-use
setup), and it could not fetch a package either. The step fits the owner's decision of 2026-11-26: the packages are restored from the internet by the
coder's own step, on the host network. `build` and `test` are still exactly as before.

## The recorded run (final)

Base = HEAD of the clone = `3d1fb9e6`. The unit is the one of `CheckBoundary` (systemd user unit, `ProtectSystem=strict`, caches read-only outside a restore,
`PrivateNetwork` for the test).

| Run | Step | Argv | Exit | Time | Network |
|---|---|---|---|---|---|
| 1 (the commit adds `ASelectedRunProbeTests`, one failing test) | restore | `dotnet restore dew_flow_connect_other_ais.slnx` | 0 | 4.8 s | host |
| 1 | build | `dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4` | 0 | 57.6 s | host |
| 1 | test, scope **selected**, 1 file | the `testFor` argv above with stem `ASelectedRunProbeTests` | **2** | 3.7 s | none |
| 1 | full test | not run (a red selected run ends the round) | | | |
| 2 (the fix: the class passes) | restore | same | 0 | 4.7 s | host |
| 2 | build | same | 0 | 49.8 s | host |
| 2 | test, scope **selected**, 1 file | same argv | **0** | 2.1 s | none |
| 2 | test, scope **full** | `./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests` | **0** | 11 m 00.2 s | none |

- Run 1 ended `failed` in 1 m 06.7 s: "the test step of src_mcp exited 2; the full test command was not run". The selected log holds
  `failed CoaiMcp.Tests.ASelectedRunProbeTests.TheProbe` and `Test run summary: Failed! … total: 1, failed: 1`; the failure digest names the test (the test asserts it).
  A red selected run cost 1 m 07 s against 12 m for the whole round.
- Run 2 ended `green` in 11 m 57.3 s: 4 steps, every one exit 0. The full step: **total 18651, failed 0, succeeded 17948, skipped 703**, duration 10 m 59.6 s.

### The skips of the full step in the unit (703, listed by reason)

| Count | Reason (the skip's own words) |
|---|---|
| 530 | no systemd user manager here: `systemd-run --user` exited 1 (a unit cannot start a unit). 50 classes, the largest: ACodexCoderPreflightTests 42, AAgyCoderFixTurnsTests 31, ACheckBoundaryPreflightTests 29, AAgyCoderPreflightTests 28, ACoderFailsOverAcrossVendorsTests 27, ACodexCoderFixTurnsTests 25, ACoderJobWithNoBoundaryIsRefusedTests 20, ACoderInstructionStepRunTests 19, ACoderFixTurnsTests 17, then 41 more classes of 15 or fewer (all the tests that start real check units; the host run covers them) |
| 3 | this run has no per-user data folder of the machine (a check unit: an empty HOME), so it made its own inside the run folder, which is under the temp folder: `ACoderWorktreeIsMadeAtTheBaseShaTests.TheDefaultRoot_IsMachineLocal_…`, `AntigravityReadOnlyTests.TheFolder_IsThePersonsOwn_…`, `KimiCredentialsLockTests.TheDefaultRoot_IsOutsideTheHomesFoldersAndTemp_…` (new, this task) |
| 20 + 18 + 15 + 15 + 4 + 4 + 3 + 3 + 2 + 2 + 2 + … | Windows or macOS only (Windows ACLs 20; Vibe refused off Windows 18; the Windows-host arm 15; Kimi on Windows hosts only 15 + 2; held-file refusals and share modes 4 + 2 + 2 + 1 + 1 + 1; MAX_PATH 4; Darwin native calls 4; drive and UNC roots 3; Windows paths 3; SID keys 2 + 1; named mutex 2; pid tag bits 1; mklink junction 2; the like) — 155 in all (the rest of the 703), the same kind as on the host |
| 5 | this process cannot give a folder the setgid bit here (the unit's user namespace) |
| 2 + 2 | `COAI_LIVE_CREDS_CLI` names no creds CLI; `COAI_LEFTOVERS_SELFTEST` is not set |
| 5 | the real codex (2), the real agy (2) and the real CLIs (1) run only when their `COAI_REAL_*` switch is set; the skip says "this is a skip, not a pass" |
| 1 | the real line of a native Linux host runs on the Linux CI legs |

(The reason of each skip is a line in the step log, `logs2/03-src-mcp-test.log`; the count is of the lines `skipped <test>`.)

## The first recordings, and what they found

1. **No restore step** (two runs, build exit 1 at 10.5 s): see "Why the file has a restore step".
2. **The full suite inside the unit failed 94 tests** (with the restore step in: run 2 `failed` in 14 m 06 s; total 18644, failed 94, succeeded 17850, skipped 700;
   the second recording, before the change, gave the same 94 again: total 18645, failed 94, skipped 700, duration 12 m 12 s). Cause: on Linux .NET answers an
   EMPTY string for `Environment.GetFolderPath(LocalApplicationData)` when the folder (`$XDG_DATA_HOME`, else `$HOME/.local/share`) does not exist, and the HOME of
   a check unit is a new empty folder. The product reads that folder for the data folder, the coder worktrees, the agy launch folder, the Kimi locks. By their
   first lines: 63 `agy was not launched: there is no per-user folder to hold its read-only agent and hook ('coai-agy')`, 7 `'coai-mcp' is not rooted`,
   3 `timed out waiting for the runner to start its child`, 3 `KeyNotFoundException`, 5 `InvalidOperationException` where another type was expected, and single
   rooted-path asserts and timing.
3. **The fix (a separate commit, test code only).** `PerUserFolderForTheRun` (a module initializer of the test assembly, beside `RunTemp`): where the machine answers
   empty, the run makes `data` inside its own run folder (removed with it) and points `XDG_DATA_HOME` at it, once, before any test; where the machine has the
   folder nothing is made or set, and the real HOME is never written. Result: 94 failures became 3, and those 3 assert that the machine's own folder lies OUTSIDE
   the temp folder, which cannot hold where the run made its own: they skip, naming that cause (the table above). A test that needs a real systemd user manager
   already detected none and skipped (`SystemdUser.Works`; the 530 above), so no skip was added for it. Tests: `PerUserFolderForTheRunTests` (4); the scan test
   `EveryClassThatSetsTheProcessEnvironmentTests` names the initializer as the second allowed writer.

## Numbers on the host

The whole suite on the host (not in a unit), under the shared lock, on the final branch: **total 18649, failed 0, succeeded 18482, skipped 167**, exit 0, 28 m 18 s
(the box was shared with other coders). The suite in the unit has 2 more tests (18651) and 536 more skips (703), the difference being the tests that start check units
(skipped in the unit) and the three that name the per-user folder.
