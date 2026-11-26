# Epic e4-adapters-coder-hardening, story s1-measure-first, task t1 — the .NET commands, measured inside the check unit

Recorded 2026-11-26 on the WSL2 box of E2 (14 threads, 15 GiB RAM, systemd user manager, real disk `/dev/sdd`). Harness: `src_mcp/tests/ADotnetStepsAreMeasuredTests.cs` on `src_mcp/tests/MeasureBed.cs`; run as
`COAI_RECORD_DOTNET=/home/jinx/coai-t3/e4/run-dotnet ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests --filter-class '*ADotnetStepsAreMeasuredTests'` (skipped unless the variable names an absent folder). Subject: this repository at `436943f3`
(E2 merged into the E4 plan branch) for the clone; scratch projects written by the test for everything else. No model, no paid call. SDK inside the unit: **10.0.114** (`global.json` asks 10.0.100, `latestFeature`); the machine also has 11.0.100, which a folder
with no `global.json` above it gets (see "Surprises", 3).

## What ran, and what was not changed

Every row is a command of one `systemd-run --user` unit made by the product's own `CheckBoundary` from a `BoundaryJob` (`MeasureBed.StepAsync` calls `CheckBoundary.RunAsync` after `AdmitAsync` — the preflight, all rows held; the properties of the host-network and
of the private-network unit are the first lines of each `RESULT.txt`). **No property of the unit was changed, none was added** (SPEC rule J1). The `cwd` column is the working folder: the product starts a step at the worktree root; a project folder is reached by
`/bin/sh -c 'cd DIR && exec "$@"'` inside the same unit (a working folder for a product step is s3). The environment is E2's list plus the four .NET switches the product's `CheckRunner` sets (`NO_COLOR`, `DOTNET_NOLOGO`, `DOTNET_CLI_TELEMETRY_OPTOUT`, `MSBUILDDISABLENODEREUSE`).
The unit's `HOME`, `NUGET_PACKAGES` and the caches are the job's own folder, which starts empty: **every restore here was a cold one** (the stand-in of the real home has no `.nuget`, so no read-only fallback folder was bound; E2 run E had one).

The stand-in of the real home is a folder of the harness: for the .NET runs it holds nothing (0 entries before and after — .NET needs nothing from the home), so for these runs "the same bytes and times" is true and says little; the node results (`RESULTS_s1_node.md`) are the ones where the stand-in holds a copy of node.

## Predictions (SPEC §A3, written before the run) against what was seen

| Proposal of the SPEC | Seen |
|---|---|
| `dotnet restore {unit}` on the host network | holds: 9.1 s (11.2 s in an earlier recording) for this repository's whole solution, cold, 988 MB into the job's `NUGET_PACKAGES` |
| `dotnet build {unit} -c Debug --no-restore -m:{jobs}` with NO network ("none when s1 measured it so") | **holds**: exit 0 with only `lo` (M1) |
| `dotnet test {unit} --no-build -c Debug`, "in the form s1 measured for the runner" | holds for VSTest only; **MTP needs `--project`/`--solution` and a `global.json` that opts in** — see the table below |
| the MTP console is read like this repository's | the summary block is the same shape as this repository's executable (`total / failed / succeeded / skipped / duration`), the lines above it are not |

## M1 — restore on the host network, then build and test with only a loopback device

| Row | Command (cwd) | Network | Exit | Time |
|---|---|---|---|---|
| r01 | `dotnet restore dew_flow_connect_other_ais.slnx` (`.`) | host | 0 | 9.1 s |
| r02 | `dotnet build dew_flow_connect_other_ais.slnx -c Debug --no-restore -m:4` (`.`) | **none** | **0** | 38.8 s (39.7 s and 75.9 s in two earlier recordings; the box was shared with other streams' suites — the time is not a property of the command) |
| r03 | the same build WITHOUT `--no-restore` (`.`) — a control | none | 0 | 9.7 s |
| s1 | `dotnet restore` of a VSTest project, an MTP project, an MTP project without the runner setting | host | 0 | 3.0 s, 2.8 s, 1.3 s |
| s2 | `dotnet build -c Debug --no-restore` of the same three | none | 0, 0, 0 | 3.9 s, 5.8 s, 7.2 s |
| s1 bad | `dotnet restore` of a project that references a package that does not exist | host | 1 | 1.5 s |

- **M1 answer: `dotnet build --no-restore` passes with a loopback device only, after a restore on the host network, on this repository.** `NU1900` (the audit of the package sources) belongs to the restore; the build step of a detected .NET project has no
  need of the network. This is a different answer from E2's, where the file's build command is `dotnet build` WITH a restore (E2 run A: 30 × `NU1900`, 8 s).
- **The control (r03) is NOT a repeat of E2's run A**: a build with a restore, on a private network, passed here in 9.7 s (13.6 s earlier) because the host-network restore before it had made the restore a no-op (no audit when nothing is restored). A file project whose build command
  restores still needs the host network when its restore has not run before; this was not re-measured, and E2's number stands for it.
- The restore error is `error NU1101: Unable to find package … No packages exist with this id in source(s): /usr/lib/dotnet/library-packs, nuget.org`, one line per package, then `Failed to restore <project> (in N ms).` (fixture `dotnet-restore-error.txt`).
- `git status` after the build: only `bin/` and `obj/` folders, all ignored (`!!`); nothing git would add. After the restore of the scratch projects, `obj/` of the project with a failed restore was also ignored.

## M2 — `dotnet test` in three cases (private network, after `--no-build` of a built project)

Each scratch project has 4 tests: `PassesAlways` and `OtherTests.AlsoPasses` pass, `FailsOnTheValue` fails by an assertion, `FailsByThrowing` by an exception.

| Case | Command (cwd) | Exit | Console |
|---|---|---|---|
| VSTest project, in its folder | `dotnet test --no-build -c Debug` (`vstest`) | **1** | per failure: `  Failed <FQN> [N ms]`, `  Error Message:` + text, `  Stack Trace:` + `at <member>() in <path>:line N`; last line `Failed!  - Failed:     2, Passed:     2, Skipped:     0, Total:     4, Duration: 51 ms - Scratch.Vs.dll (net10.0)`; **stderr** has `[xUnit.net 00:00:00.24]     <FQN> [FAIL]` lines (fixture `dotnet-vstest-failed.txt`) |
| VSTest project, by path from the root | `dotnet test vstest/Scratch.Vs.csproj --no-build -c Debug` (`.`) | 1 | the same, minus the `VSTest version` banner, plus the warning `VSTEST_ARTIFACTS_DIRECTORY environment variable was not found, falling back to a user-private temporary directory` (a cwd outside the project's own folder) |
| VSTest, zero tests | `… --filter "FullyQualifiedName~NoSuchTestAnywhere"` | **0** | `No test matches the given testcase filter \`…\` in <dll>` and NO summary line — **exit 0, so for VSTest "no test ran" is a text, not a code** |
| VSTest, passes | `… --filter FullyQualifiedName~OtherTests` | 0 | `Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 9 ms - <dll>` |
| MTP, `global.json` names the runner | `dotnet test --no-build -c Debug` (`mtp-global`) | **2** | per failure: `failed <FQN> (Nms)`, `  from <dll> (net10.0|x64)`, the message (`Assert.Equal() Failure: …`, or `Xunit.MicrosoftTestingPlatform.XunitException: <exception>`), `    at <member>() in <path>:N` (no `line`); then `<dll> (net10.0\|x64) failed with 2 error(s) (447ms)`, `Exit code: 2`, `Test run summary: Failed!` + `  total: 4 / failed: 2 / succeeded: 2 / skipped: 0 / duration: 668ms`, `Test run completed with non-success exit code: 2 (see: https://aka.ms/testingplatform/exitcodes)` (fixture `dotnet-mtp-global-failed.txt`) |
| MTP, zero tests | `… --filter-class Scratch.NoSuchClass` | **8** | `<dll> … Zero tests ran (254ms)`, `Exit code: 8`, `Test run summary: Zero tests ran`, `  error: 1`, the counts all 0 (fixture `dotnet-mtp-global-zero-tests.txt`) |
| MTP without the setting | `dotnet test --no-build -c Debug` (`mtp-plain`) | **1** | one line: `…Microsoft.Testing.Platform.MSBuild.targets(395,5): error : Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK and later. If you use dotnet test, you should opt-in to the new dotnet test experience. For more information, see https://aka.ms/dotnet-test-mtp-error [<project>]` (fixture `dotnet-mtp-plain-error.txt`) |
| MTP without the setting, the executable run directly | `./mtp-plain/bin/Debug/net10.0/Scratch.Mtp` | **2** | the MTP console of the line above, with `Test run summary: Failed! - <dll> (net10.0\|x64)` on ONE line with the dll |

The rooted recording (`global.json` with the MTP runner at the REPOSITORY ROOT, as this repository has; projects in `app/` (xunit.v3) and `vs/` (VSTest)):

| Row | Command (cwd) | Exit | Result |
|---|---|---|---|
| x3 | `dotnet test --project app/Scratch.Mtp.csproj --no-build -c Debug` (`.`) | **2** | the MTP console of a failed run (fixture `dotnet-mtp-rooted-project-failed.txt`) |
| x3 | the same with `--filter-class Scratch.NoSuchClass` | **8** | `Zero tests ran` |
| x3 | the same with `--output Detailed --filter-class Scratch.OtherTests` | 0 | passed names are printed (see M10) |
| x3 | `dotnet test --no-build -c Debug` at the root, no project there | 1 | `Specify a project or solution file. The current working directory does not contain a project or solution file.` |
| x3 | `dotnet test app/Scratch.Mtp.csproj --no-build -c Debug` (positional) | **1** | `Specifying a project for 'dotnet test' should be via '--project'.` — **the form `dotnet test {unit}` of the SPEC's table does not exist in MTP mode** |
| x4 | `dotnet test --project vs/Scratch.Vs.csproj …`, and in `vs/` `dotnet test --no-build -c Debug` | **1** | `global.json defines test runner to be Microsoft.Testing.Platform. All projects must use that test runner. The following test projects are using VSTest test runner: Scratch.Vs.csproj` (fixture `dotnet-vstest-under-an-mtp-global-json.txt`) |

Not the same by accident: in the first scratch recording `dotnet test --project mtp-global/Scratch.Mtp.csproj` run from the scratch repository's root (no `global.json` there) died with `MSBUILD : error MSB1001: Unknown switch. … Switch: --project`, and the full command line shows
`sdk/11.0.100/MSBuild`: the SDK chosen, and with it the MTP opt-in, is decided by the `global.json` found from the WORKING FOLDER upward, not from the project's folder. The fixture `dotnet-mtp-global-failed-from-root.txt` is that error.

## M3 — this repository's test executable, and the same for the three `dotnet test` cases (M10)

The executable of this repository is an MTP executable of xunit.v3 (`./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests`), run in the unit with a loopback device only.

| Row | Argv | Exit | Time | Seen |
|---|---|---|---|---|
| r10 | `--filter-class CoaiMcp.Tests.AFailureDigestTests --filter-class CoaiMcp.Tests.AToolchainFileTests` | 0 | 4.9 s (2.1 s in the first recording) | **total: 69**, 69 succeeded: two `--filter-class` are an OR; a class name is the full name |
| r12 | `--filter-class '*.AFailureDigestTests'` | 0 | 2.4 s | a wildcard works |
| r13 | `--filter-class CoaiMcp.Tests.NoSuchClassAnywhere` | **8** | 2.0 s | `Test run summary: Zero tests ran - <dll> (net10.0\|x64)`, `total: 0` |
| r16 | `--minimum-expected-tests 1000 --filter-class CoaiMcp.Tests.AFailureDigestTests` | **9** | 2.4 s | `Test run summary: Minimum expected tests policy violation, tests ran 18, minimum expected 1000 - <dll>` |
| r14 | `--list-tests --filter-class …` | 0 | 1.7 s | one `  <FQN>` line per test |

- **`testFor` of this repository (M3):** `./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests --filter-class <full class name of each file>` runs the tests of the named test files in 2 to 5 seconds, against 8 m 14 s for the suite. The "no test ran" code is **8** (the SPEC's "the exit code … the file holds for the adapter's runners").
  A test FILE holds one class in this repository by convention (`A<Name>Tests.cs`); the class name is the stem of the file, and `CoaiMcp.Tests.` is its namespace. A file that holds two classes is not covered by this form (not measured).
- **The summary line (M10), this executable:** `Test run summary: Passed! - <dll> (net10.0|x64)` (or `Failed!`, `Zero tests ran`, `Minimum expected tests policy violation, …`), then on the next lines `  total: N`, `  failed: N`, `  succeeded: N`, `  skipped: N`, `  duration: …`. With `--output Detailed` each test that ran
  is a line `passed <FQN>[(theory arguments)] (Nms)` — **69 lines for 69 tests, no duplicate** (r11); without it no passed test is named (r10). A theory case is a line with its arguments, so a name is not a method name. A failed test is `failed <FQN>(args) (Nms)` + the message + the stack (the recorded fixture `mtp-three-failed.txt` of E2).
- **The summary line and the passed names for `dotnet test`:**
  - VSTest: the last line is `Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 9 ms - Scratch.Vs.dll (net10.0)` (`Failed!` with the same fields when something failed); passed names only with `--logger "console;verbosity=detailed"`, which prints
    `  Passed <FQN> [N ms]` lines after a noisy block (fixture `dotnet-vstest-detailed.txt`). No summary line at all for a run in which no test matched.
  - MTP in `dotnet test`: `Test run summary: Failed!` (no dll on that line, unlike the executable run directly) and the same five count lines; passed names with `--output Detailed`: `passed <FQN> (Nms)` followed by an indented `  from <dll> (net10.0|x64)` line for every test, passed or failed (fixture `dotnet-mtp-global-detailed.txt`).
- The TRX logger of VSTest (`--logger "trx;LogFileName=/tmp/scratch.trx" --results-directory /tmp/trx`) wrote its files into the unit's own `/tmp`: a report file is possible with no new bind, but decision 3 of the SPEC (read the console) stands, and no reader of it was built.

- **`dotnet test --project` on THIS repository (r17), new in the last recording:** `dotnet test --project src_mcp/tests/CoaiMcp.Tests.csproj --no-build -c Debug --filter-class CoaiMcp.Tests.AFailureDigestTests` in the unit: **exit 0, 2.7 s, 18 tests passed**, the MTP console of the table above (fixture `dotnet-test-project-form-this-repository.txt`). The instructions of this repository (`.agents/PROJECT.md`) say `dotnet test` never works here ("no VSTest host; it aborts"): inside the unit, with SDK 10.0.114 and the root `global.json` opting in to MTP, it does work for this project with a class filter. Not measured: the whole suite through `dotnet test` (8 minutes), and the other three test projects.

## Surprises (what the plan did not predict)

1. **MTP's `dotnet test` has no positional project.** The SPEC's default `dotnet test {unit} --no-build -c Debug` works for VSTest and is refused for MTP; MTP wants `--project <path>` or `--solution <path>`, and works only when `global.json` at the working folder or above opts in. A detected unit with MTP test
   projects and no `global.json` runner setting is not "refused by a guess": `dotnet test` itself refuses it (exit 1, the one-line error above), which is what the detector's refusal must say first. Running the test executable directly (the form this repository's own file uses) works with no setting, and exits 2 on a failed test.
2. **A VSTest project under a `global.json` that opts in to MTP fails in `dotnet test`** with a clear sentence (exit 1) — a unit with both kinds of project is a refusal for the detector, as the SPEC says for "two runners", and the cause can be read from the text.
3. **The SDK follows the working folder.** In a folder with no `global.json` above it the unit ran SDK 11.0.100, not 10.0.114: the answer of `dotnet --version` is a fact of the working folder, and the MTP/VSTest decision with it.
4. **VSTest says "no test matched" with exit 0.** Only MTP (and this repository's executable) has a code for it (8). "Zero tests are not green" must read the text for VSTest.
5. The restore of a whole solution on an empty NuGet folder took 9 to 11 s, so a cold restore is not the cost; the cost is the build (40 to 76 s), which varies with the other work on the box.

## What this does not settle

One SDK (10.0.114), one machine, xunit 2.9.3 with `Microsoft.NET.Test.Sdk` 17.14.1 and `xunit.runner.visualstudio` 3.1.4 for VSTest, xunit.v3 4.0.2 for MTP. NUnit, MSTest and `Microsoft.Testing.Platform` runners other than xunit's were not run; their consoles are the platform's for MTP and
their own for VSTest. No warm-cache `NUGET_FALLBACK_PACKAGES` run (E2 has one); no solution with several test projects; F# and VB projects; a `dotnet test` with a loggers or a coverage extension.

## Files

Recorded consoles: `src_mcp/tests/fixtures/toolchain/dotnet-*.txt` (checkout path and the user name replaced, see the scan of story s2 t6). Raw logs and `RESULT.txt` of each of the three beds (`repo`, `scratch`, `rooted`): `/home/jinx/coai-t3/e4/run-dotnet/` (not committed).
