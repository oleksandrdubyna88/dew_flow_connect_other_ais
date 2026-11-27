# E4 s5 t6 — this repository's own toolchain file selects tests (recorded run, 2026-11-27)

Task: `stories/s5-format-selection-rerun/tasks/t6-this-repository-selects`. No model ran. Every commit of the run was made by the test itself
(`ARecordedSelectedRunUnderTheBoundary` in `src_mcp/tests/AThisRepositoryToolchainFileTests.cs`, skipped unless `COAI_RECORD_SELECTED_RUN` names an
absent folder). It clones HEAD, makes a coder worktree of it (`CoderWorktree`), commits in the worktree, and runs `ChecksAtBase.RunCoderJobAsync`, the
product's own road, with `BoundaryJob.ForCoder` (the measured network defaults: restore and build on the host, test on a loopback device only,
`MemoryMax` 8000 MiB). Folder: `/home/jinx/coai-t3/scratch-e4s5t6/` (not `/tmp`); deleted at the end.

**Verdict.** d1, d3, d4: done and tested. d2: the red selected run and the green selected run are recorded and are as the plan says. **The full suite is
NOT green inside the check unit: 94 of 18644 tests fail there** (they pass on the host). d2 is therefore not met; see "What blocks d2".

## What the file says now

`.coai/toolchain.yaml`, project `src_mcp`: `build` and `test` unchanged; new `testFiles: [tests]` (relative to the project: `src_mcp/tests`), new `testFor`:

```
/bin/sh -c 'x=./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests; for s in "$@"; do x="$x --filter-class CoaiMcp.Tests.$s"; done; exec $x' sel {stem}
```

(`{stem}` is an item of its own; the script turns each stem into one `--filter-class`, the argv s1 t1 measured as M3.) `src_vs_code` is not a project of the
file (comment in the file says why). **New, found by this run: a `restore` step** (`dotnet restore dew_flow_connect_other_ais.slnx`), see the first attempt.

## The recorded run

Base = HEAD of the clone = `e9359bd6`. The unit properties are those of `CheckBoundary` (systemd user unit, `ProtectSystem=strict`, caches read-only
outside a restore, `PrivateNetwork` for the test).

| Run | Step | Argv | Exit | Time | Network |
|---|---|---|---|---|---|
| 1 (the commit adds `ASelectedRunProbeTests`, one failing test) | restore | `dotnet restore dew_flow_connect_other_ais.slnx` | 0 | 5.5 s | host |
| 1 | build | `dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4` | 0 | 1 m 05.9 s | host |
| 1 | test, scope **selected**, 1 file | the `testFor` argv above with stem `ASelectedRunProbeTests` | **2** | 3.8 s | none |
| 1 | full test | not run | | | |
| 2 (the fix: the class passes) | restore | same | 0 | 6.0 s | host |
| 2 | build | same | 0 | 1 m 05.3 s | host |
| 2 | test, scope **selected**, 1 file | same argv | **0** | 3.1 s | none |
| 2 | test, scope **full** | `./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests` | **2** | 12 m 51.2 s | none |

- Run 1 ended `failed` in 1 m 15.9 s: "the test step of src_mcp exited 2; the full test command was not run". The selected log holds
  `failed CoaiMcp.Tests.ASelectedRunProbeTests.TheProbe` and `Test run summary: Failed! … total: 1, failed: 1`; the failure digest of that log names the test
  (the test asserts it). A red selected run cost 1 m 16 s against 14 m for the whole round.
- Run 2 ended `failed` in 14 m 06.3 s: selected green, then the full suite: `total: 18644, failed: 94, succeeded: 17850, skipped: 700, duration 12 m 50 s`.

### First attempt: no restore step (exit 1 at 10.5 s, both runs)

With the file as the owner's answer had it (no `restore`), the build died after 10.5 s with exit 1:
`System.IO.IOException: Read-only file system : '<job>/dotnet-home/.dotnet'`. The check unit binds the package, npm and dotnet folders read-only in every
step but a restore (verify pass 3, M16, "interim, the owner decides"), and the job folder is new and empty: the SDK's first-use setup cannot write, and a
build could not fetch a package either. So a coder job of this repository could never build under HEAD's code. (The job of 2026-11-26 in
`RESULTS_coder_proof.md` ran before M16.) A `restore` step runs on the host network with writable folders and fixes it: this is the only file change
beyond `testFiles` and `testFor`, and `build` and `test` are still unchanged. It is a decision for the owner whether to keep it or to change M16.

## What blocks d2: the full suite inside the unit

94 tests fail in the unit that pass on the host. The first lines of their messages, counted:

| Count | Message |
|---|---|
| 55 + 8 | `agy was not launched: there is no per-user folder to hold its read-only agent and hook ('coai-agy')` (and the refusals that carry it) |
| 7 | `'coai-mcp' is not rooted, so it was composed by hand rather than resolved` |
| 3 | `timed out waiting for the runner to start its child` |
| 3 | `KeyNotFoundException` (the same cause, a missing per-user folder) |
| 2 + 3 | `InvalidOperationException` where an `ArgumentException` or `OperationCanceledException` is expected (the agy refusal again) |
| 1 each | `Path.IsPathFullyQualified(root)` of the default coder root; `Path.IsPathRooted(DataDir)`; `'…/coai-mcp' already exists`; a state that stayed `running`; one timing assertion |

The common cause: the unit has no per-user folder (`/run/user` is an empty tmpfs, `HOME` is the job's), and the tests added by E3 (the agy and codex coders,
per-vendor folders) and the data-folder resolver need one. This is a mismatch between the E3-merged suite and the check unit, not a fault of the
selection. It is outside t6 (it needs either the unit to give the tests a per-user folder, or those tests to make their own); nothing of it was changed.

## Numbers on the host

The whole suite on the host (not in a unit), under the shared lock, on the branch after the t6 commits (the committed file, with the restore step):
**total 18643, failed 0, succeeded 18476, skipped 167**, exit 0, 31 m 57 s (the box was shared with other coders). The same suite passes there and fails 94 tests in
the unit, so the 94 are the unit's, not the change's.
