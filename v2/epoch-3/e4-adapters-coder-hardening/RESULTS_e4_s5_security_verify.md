# E4 s5 — security verify (2026-11-26)

Verifier: Fable, read-only. Commits `46290f86` (s5) and the merge `1e4e3cbe` of E3 `6ebdbf42`, in `/home/jinx/git/coai-v2-ep3e4`, read with `git show`. The worktree was not touched: no file changed, nothing built or run in it (`git status` empty, `HEAD` `1e4e3cbe`). Line numbers are of `1e4e3cbe`.

The question, after the owner's decision of 2026-11-26 (SPEC §A4: nothing the head changes is refused, the head's own definitions run, every difference is reported): **can the head make coai's own process unsafe, or make the report say something that is not so?**

How it was checked:

- Read in full: `FileArguments.cs`, `TestSelection.cs`, `FlakyRule.cs`, `CoderFormat.cs`; the s5 diffs of `CheckRunner.cs`, `ChecksAtBase.cs`, `StepPlan.cs`, `CheckResult.cs`, `CoderChecks.cs`, `CoderChecksReport.cs`, `CoderFixLoop.cs`, `CoderCommits.cs`, `RoleJobNotices.cs`, `RoleJobSettings.cs`; `RunSummary.cs`, the entry of `FailureDigest.cs` and the platform-runner reader; `ToolchainResolve.FileChanges`, `DefinitionReport.cs`; the boundary's per-step folders; the merge's three conflict files and `CoderInstructions.cs`. SPEC §A4, §D, §E, §F, §R.
- **The real runner was run.** `src_mcp/core` and `src_mcp/runners` of `1e4e3cbe` were exported with `git archive` into the scratchpad and built there. A probe drives the real `CheckRunner.RunAsync` (with `StepPlan`, `TestSelection`, `FlakyRule`, `FailureDigest`) on scratch trees whose test commands are small shell scripts that print what the platform runner prints. No OS boundary is in that run (no coder job), so everything about units is by reading.
- Scratch: `/tmp/claude-1000/-home-jinx-git-dew-flow-conventions/4c91ec34-3816-4f0d-83a8-d95afa710fba/scratchpad/s5x/` — `probe/Program.cs`, `run/r1`, `run/r2`, `run/r3`, `gitfmt/`. A reboot clears it.
- `CoderFormat` and `CoderFixLoop` are in the server assembly and were not run: the formatter turn is judged by reading, plus one git measurement of the status codes it relies on.

## Verdict

**must-change: four items.** Three were measured on the real runner, one is by reading. None of them lets the head out of a unit or reach a secret. Each makes a step start with text the head's output chose, or makes the report say something that is not so.

| # | What | How known |
|---|---|---|
| M1 | The re-run puts file names taken from the test OUTPUT into the argv with no check: the re-run started as `sh t/sel.sh --ignore-exit-code 2`. | measured |
| M2 | A round with an excused failure says "every one exited 0"; and a narrowed re-run turns a step green whose first run exited non-zero for a reason that is no test. | measured |
| M3 | When the selected run says "no test ran" and the full run then fails, the digest for the coder is read from the selected run's log. | measured |
| M4 | A unit that cannot be stopped does not end the job in two new places: the re-run, and the formatter turn. | by reading |

The points of the brief:

- **Secrets in logs, argv, notices.** No new road for a key. The `coder-test-flaky` notice holds the job id and a count only. One gap: the name of an excused test is written as the output had it (S3).
- **The formatter unit.** Its own unit, no network, 5 minutes, a new home and temp, the package folders read-only. A file it adds, a link it makes or a file it was not given puts the tree back; an ignored file is removed by the round's clean. Open: M4, and what the format commit may hold (S2).
- **The re-run and the rule.** The rule itself follows F0 and K13 (exit 0, no failure, a total above zero, the name exactly once). The faults are around it: M1, M2, M4.
- **The selected run.** It cannot hide a failure: the full run always follows a selected run that passed or selected nothing, and a failed selected run ends the round red (measured). The fault is the digest's source (M3).
- **`{file}` and `{stem}`.** Safe where the names come from git (the formatter, the selected run). Not checked where they come from the output (M1).
- **The E3 merge.** The three conflict files hold both sides; the hardened git reads and the clean of M6 are intact.

## Must-change

### M1 — the re-run's argv takes file names from the test output, unchecked

Site: `/home/jinx/git/coai-v2-ep3e4/src_mcp/runners/Toolchain/CheckRunner.cs:198` and `:205`.

```csharp
var narrowedFiles = decision.Tests.Select(test => test.File).Where(file => step.TestFiles.Any(prefix => file.StartsWith(prefix.Trim('/') + "/", …) || …)).Distinct(…).ToList();
…
var argv = narrowed ? TestSelection.Argv(step.TestFor!, narrowedFiles, ".", request.Jobs) : step.Argv;
```

`test.File` is what the failure's stack line said after ` in ` (`TestExecutableFailures.cs`, the pattern ` in (?<file>.+):(?:line )?(?<line>\d+)`): text of the step's output, which the head's code prints. The selected run filters its names through `TestSelection.IsSafe` (`TestSelection.cs:76-81`: a closed pattern, no segment that starts with `-`, a stem of letters, digits and underscores); `TestSelection.Argv` (`:38-59`) does not, and the re-run calls it directly. Only a prefix is asked for.

Measured (`run/r1`; the file states `testFor: [sh, t/sel.sh, "{stem}"]`, `testFiles: [tests]`; the first run prints two failures whose stack lines name `tests/--ignore-exit-code.cs` and `tests/2.cs`, and exits 2):

```
step 1: scope=full  exit=2 passed=True excused=True  argv=[sh | t/full.sh]
step 2: scope=rerun exit=0 passed=True excused=False argv=[sh | t/sel.sh | --ignore-exit-code | 2]
flaky: Ns.T.Flaky file=tests/--ignore-exit-code.cs rerun=narrowed proof=named; Ns.T.Other file=tests/2.cs …
```

No shell is involved, so this is an option for the test tool and not a command. It matters most where the head left the base's `testFor` alone: the command is then the base's, and the head's output still adds options to it. A `..` in such a name also passes (`./tests/../../x` for `{file}`).

Fix, on the one road: `TestSelection.Argv` (or one function both callers use) takes only names that pass `IsSafe`, and for the re-run only names that are paths of the head commit's listing; a failure whose file does not pass means no narrowed re-run. Test: `run/r1` ends with a whole re-run or none, and no argv item that the file did not write.

### M2 — a green round that says "every one exited 0" when one did not; and a narrowed re-run that excuses more than it showed

Sites: `/home/jinx/git/coai-v2-ep3e4/src_mcp/runners/Toolchain/CheckRunner.cs:145`; `/home/jinx/git/coai-v2-ep3e4/src_mcp/core/Toolchain/CheckResult.cs` (`Passed => (Started && !TimedOut && ExitCode == 0) || Excused`); `FlakyRule.cs:127-154`.

1. The note. Measured (`run/r1`, `run/r2`): `state: Green   note: 2 step(s) ran and every one exited 0`, with step 1 at `exit=2` (r1) and `exit=3` (r2). The note is the envelope's `checksNote`. The `flaky` list and `excused: true` on the step are there, so the facts are in the report; the sentence beside them is false.
2. What a narrowed re-run proves. Measured (`run/r2`): the first run names ONE failed test, its counters agree (total 3, failed 1), it also prints `ERROR: coverage for lines (41%) does not meet the threshold (80%)` and exits 3. The narrowed re-run of that one test's file exits 0 and names the test as passed. Result: `Green`, `flaky: Ns.T.Flaky … rerun=narrowed proof=named`. The rule's "complete" means the count of failed tests equals the count of names (`RunSummary.cs`); it does not mean the exit code is explained by them. A narrowed re-run shows that the named tests pass, not that the step would. SPEC §F states the order-dependency limit; this one (another cause of the exit code) is not stated.

Fix: (a) the note of a green result with an excused step says so (`…; 1 step failed and was excused by a re-run, see flaky`). (b) A narrowed re-run turns the step green only when the first run's exit code is the adapter's code for "tests failed" (the platform runner: 2) — a row of `toolchains.json` beside `noTestRan`; for a tool with no such code, only a whole re-run excuses. Or, if the owner keeps the rule as it is, the `flaky` row carries the first run's exit code and the sentence "the first run's exit code was not shown to come from these tests alone". Test: `run/r2` is red, or green with that row.

### M3 — the digest is read from the wrong step after a selected run that selected nothing

Sites: `/home/jinx/git/coai-v2-ep3e4/src_mcp/core/Toolchain/CheckResult.cs:168` (`FailedStep => Steps.FirstOrDefault(step => !step.Passed)`), used by `CoderChecks.cs:271-281` (`DigestOf`); the records are added at `CheckRunner.cs:106-132`.

A selected run that exits with a "no test ran" code is kept in the list as a step that did not pass, and the full run follows. When the full run fails, the first step that did not pass is the selected one.

Measured (`run/r3`: the job changed `tests/New.cs`; the selected run prints `Zero tests ran`, exit 8; the full run fails with six failures, exit 2):

```
state: Failed   note: the test step of . exited 2; the steps after it were not run
  step 2: scope=selected exit=8 passed=False argv=[sh | t/sel.sh | New] log=02-root-test.log
  step 1: scope=full     exit=2 passed=False argv=[sh | t/full.sh]      log=01-root-test.log
FailedStep: step 2, scope=selected, log=02-root-test.log
```

So the coder's fix turn gets the digest of a log that says "Zero tests ran" while the note names the full run. The same list makes a GREEN result hold a step that did not pass.

Fix: a selected run that selected nothing is recorded as such (its own flag, or `Passed` true with `nothingSelected`), and `FailedStep` is the step that ended the round. Test: `run/r3` gives the full run's log.

### M4 — a unit that could not be stopped ends the job for a step of the round, but not for the re-run and not for the formatter turn

By reading; no unit was run.

1. The re-run. `CheckRunner.cs:107` and `:134` ask `Ended(…)` for the selected run and for the first run. The re-run's record is added at `:125` and never asked. A re-run whose unit survived excuses nothing (`FlakyRule.Excused`, `rerun.LeftUnit`), so the round is `failed` and a fix turn may start beside a process that may still write. That is what S17 of pass 2 closed for every other step.
2. The formatter turn. `/home/jinx/git/coai-v2-ep3e4/src_mcp/src/Server/RoleJobs/CoderFormat.cs:57-62`: any state but green — `BoundaryRefused` from `Ended` among them — is `failed`, the worktree is put back, and `CoderFixLoop` goes on to the checks of the round. `git reset` and `git clean` then run beside that process too.

Fix: `TryRerunAsync` returns the survivor to `RunPlanAsync`, which ends as it does at `:134`; `PrepareAsync` hands a `BoundaryRefused` whose step left a unit to the loop as the end of the job (`coder-boundary`). Tests: the two cases with the fake boundary that the S17 test uses.

## Should

- **S1 — the report does not name everything the head did to the toolchain file.** `/home/jinx/git/coai-v2-ep3e4/src_mcp/core/Toolchain/ToolchainResolve.cs:60-80` compares `build`, `test`, `restore`, `format` and `testFor` only where the BASE stated them (`if (!before.Runs) continue`). A command the head ADDS — a `testFor` that prints the proof of the re-run, a `format` whose output coai commits, a `restore` with the host network — a changed `testFiles`, `skip`, `timeouts`, and a project the head adds to the file are not named. `definitionsChanged` is still true (the file itself is counted). List added commands too, with `testFor` and `testFiles` under `changedTestCommands`; and let a `flaky` row say when the command of a narrowed re-run is one the head wrote or changed.
- **S2 — what the format commit may hold.** `CoderFormat.cs:77-88` takes every ` M` entry of a given file. When the formatter or its configuration is the head's, the commit `style: format (coai)` can hold any change of those files under coai's name; a mode change shows as ` M` too (measured, `gitfmt`: made executable ` M`, turned into a link ` T`, new file `??`, ignored file not shown). Say in the `format` note when the formatter's command or configuration is among the changed definitions; refuse a mode change. The formatter turn also runs before the round's clean, on a tree that may hold the coder's ignored files: run the proof of M6 before it as well.
- **S3 — the name of an excused test is the output's text.** `summary.Failures` comes from the header pattern (`failed (?<name>.+) (…)`), up to 4096 characters, not through the redactor. It is written to the envelope (`CoderChecksReport.cs:111`) and to `flaky-excused.txt` (`CoderChecks.cs:198`). The digest's own fields are bounded and redacted. Do the same for the name.
- **S4 — F0 names a cut log, and the "named" proof does not ask.** `FlakyRule.cs:139-142` excuses on the name alone; only the "totals" branch asks `summary.Complete`. In practice a cut log loses its summary line and nothing is excused; ask `Complete` in both.
- **S5 — the units of the formatter turn leave no record.** `CoderFormat.cs:55` passes no `StepFinished`, so no `unit-s<n>.json` is written for its restore (host network) and its format step, and the envelope's `checks` do not list them.
- **S6 — the order of the records.** `CheckRunner.cs:132` puts a record before the last one when that last one is a re-run, also when the re-run belongs to an earlier project. Cosmetic.
- **S7 — two quiet failures.** A fact of the job that cannot be written (`CoderChecks.cs:214-229`) is a warning, so the count of excused tests can be lost and a third excused; the note for a `COAI_FLAKY_RERUNS` that is not 0 or 1 is made and dropped (`CoderChecks.cs`, `FlakyReruns.Read(…).Count`).
- **S8 — the merge dropped the `"title"` line of `shared/toolchain.schema.json`.** Both parents had it. No reader uses it.

## Confirmed safe

1. **`{file}` and `{stem}` from git paths.** The formatter: `{file}` only as an item of its own, replaced by `./<path>` for each file (`FileArguments.cs:33-50`); files with a control character, manifests and lock files are left out; over 200 is no run. The selected run: names pass a closed pattern, no segment starts with `-`, the stem is letters, digits and underscores, over 50 is no run (`TestSelection.cs:20-35`, `:76-81`); measured `run/r3`: `argv=[sh | t/sel.sh | New]`. The file's reader refuses `{file}` inside an item for `format` and any placeholder a key may not hold.
2. **The formatter's unit.** `CoderFormat.cs:127-128`: the step is planned with `DefaultNetwork.None` whatever the project's origin, 5 minutes, through `CheckRunner.RunStepsAsync` and so through the bounded road; `CheckBoundary.RunAsync` empties the job's home and temp before and after every step and binds the package folders read-only unless the step is a restore (`restore: step.Step == CheckStep.Restore`). The restore before it is a unit of its own with the host network, as SPEC §D item 3 says.
3. **What is taken from the formatter.** After it, `git status` (untracked files included): every entry must be a given file with ` M`, `M ` or `MM`; anything else puts the tree back to the turn's commit by `CoderRestore` (`reset --hard`, `clean -ffdx`, a proof) and the state is `refused`; a formatter that fails gives the same reset and `failed`. Measured codes: a link ` T` and a new file `??` are refused. An ignored file is not seen here and is removed by the clean at the start of the round (`CoderChecks.cs`, `ProveEqualToTheCommitAsync`, with the walk for a `.git` below the root), and the round restores again. The commit goes through the one commit road, with the search for the turn's pool key; the round then runs on the format commit.
4. **Selected, then full.** A selected run that passed or selected nothing is always followed by the full step, and only the full step (or its excuse) makes the step pass; a selected run that failed ends the round red without the full run; after one failed full run the job runs the full step only (a file in the job folder). Measured `run/r3`.
5. **The rule.** No re-run for anything but a failed full test step; none for a step that did not start, ran over its time or left a unit; none without a complete summary, with no named failure or with more than 5; none when a failure names no file, names a file the job added or changed, or was excused before; none for a third excused test. An excuse needs the re-run to start, not time out, not leave a unit, exit 0, show a summary with zero failed and a total above zero, and name each test exactly once among the passed — or, for a whole re-run that names no passed test, a complete summary with the first run's total. All or none.
6. **The job's facts.** `full-failed`, `flaky-excused.txt` and `flaky-noticed` are in the job folder, which a check unit sees as an empty read-only cover. The notice is raised once, with the job id, the role and a count. `COAI_FLAKY_RERUNS` is read from coai's own settings.
7. **The merge.** `GitHistory.RunAsync` keeps `--no-replace-objects`, the hardened environment and E3's standard input; the three E4 readers pass `maxChars` by name. `CoderChecks` keeps the clean, the empty `status --ignored` and the `.git` walk, with E3's static `StateAsync`. The schema and `ToolchainTables` hold E4's four keys and E3's `instructions`. The instruction step of E3 is read from the file at the BASE commit and runs once before the coder; the head's file does not reach it.

Not verified: anything inside a real unit; `CoderFormat` and `CoderFixLoop` at run time; the jest, vitest and node:test readers against M1 to M3 (only the platform runner's output was scripted); the repository's tests were not run.

---

# Pass 2 (2026-11-26, commit 23e25947): the fixes of M1–M4 and S1, S3, S4, S5, S8

Read-only, `git show 23e25947`. The worktree at `/home/jinx/git/coai-v2-ep3e4` was not changed, built or tested (`git status` empty, `HEAD` `23e25947`). Line numbers are of `23e25947`.

How it was checked: `src_mcp/core` and `src_mcp/runners` of `23e25947` were exported and built in the scratchpad, and the pass 1 scenarios were run again on the real `CheckRunner`, with new ones. "Is a file of the head" is answered by the scratch tree in that probe; the real read (`git cat-file -e`) was measured apart. `CoderFormat`, `CoderFixLoop` and everything inside a unit are by reading, as in pass 1. The repository's tests were not run. Scratch: `/tmp/claude-1000/-home-jinx-git-dew-flow-conventions/4c91ec34-3816-4f0d-83a8-d95afa710fba/scratchpad/s5y/` — `probe/Program.cs`, `run/`, `git/`, `node/`, `old/`.

## Verdict

**must-change: one item (M5), a change of four values in `shared/toolchains.json`.** M1, M3 and M4 are fixed. M2 is fixed for the note and for the platform runner; for the other four tools the new code is 1, and 1 is not "tests failed" only.

| Pass 1 | State | How known |
|---|---|---|
| M1 names of the output in the argv | fixed | measured: `run/r1`, `r1d`, `r1e` give a whole re-run, `argv=[sh | t/full.sh]` |
| M2 the note | fixed | measured: `1 step(s) passed; the test step of . failed and was excused by a re-run (see flaky)` |
| M2 a narrowed re-run after another exit code | fixed for mtp (`run/r2`: exit 3, whole re-run, red); **not for vstest, jest, vitest, node-test (M5)** | measured |
| M3 the digest's step | fixed | measured: `run/r3` `FailedStep: step 1, scope=full`; `run/r3g` green with `1 selected run(s) selected no test` |
| M4 a unit that cannot be stopped | fixed in both places | by reading: `CheckRunner.cs:141`, `CoderFormat.cs:66`, `CoderFixLoop.FormatEnded` |
| S1 added commands, `testFiles`, `skip`, `timeouts`, added projects | fixed | by reading (`ToolchainResolve.FileChanges`) |
| S3 the excused name | fixed | measured: a name of 249 characters is kept as about 200; a name the redactor changes is never excused (`run/r5`: no re-run, because the digest's name and the summary's name then differ — closed) |
| S4 the named proof asks `Complete` | fixed | the test's body run on the old export: 1 excused; new code: `named == 1 && summary.Complete` |
| S5 the formatter turn's units | fixed | by reading: `StepFinished` writes `unit-s<n>.json` in `format-<turn>`, the rows are put before the round's in `checks` |
| S8 the schema's `title` | fixed | read |

## Must-change

### M5 — `testsFailed: [1]` for vstest, jest, vitest and node-test does not tell a failed test from another failure

Site: `/home/jinx/git/coai-v2-ep3e4/shared/toolchains.json:57` and `:111-113`; read at `CheckRunner.cs:230` (`TestsFailedCodes`) and `FlakyRule.Decide` (`input.TestsFailedCodes.Contains(input.FirstExit)`).

The platform runner has a code of its own for failed tests (2; measured in the s3 passes). The other four tools end with 1 for a failed test and for other things too.

Measured, node:test (node 26.12.1, `node/`):

| Run | Exit |
|---|---|
| one test fails | 1 |
| every test passes, `--experimental-test-coverage --test-coverage-lines=100` not met (`42.86% line coverage does not meet threshold of 100%`) | **1** |
| the test passes, the file throws afterwards | **1** |

Measured, the real runner with jest-shaped output (`run/r2j`: `adapter: node`; the first run names one failed test in `tests/flaky.js`, prints a coverage-threshold line and exits 1; the narrowed re-run passes that test):

```
state: Green   note: 1 step(s) passed; the test step of . failed and was excused by a re-run (see flaky)
  step 1: scope=full  exit=1 excused=True  argv=[sh | t/full.sh]
  step 2: scope=rerun exit=0               argv=[sh | t/sel.sh | ./tests/flaky.js]
flaky: sometimes file=tests/flaky.js rerun=narrowed proof=named firstExit=1
```

That is pass 1's `r2` again, for a node tool. The row now has `firstExit`, but 1 tells a reader nothing. jest, vitest and VSTest were not run here (no package in the local cache); that each ends with 1 for a coverage threshold or a crash as well is from their documentation, not measured.

Fix: `"testsFailed": []` for `vstest`, `jest`, `vitest` and `node-test`, so that only a whole re-run excuses there; keep `[2]` for `mtp`. A row gets a code only with a RESULTS line that measured it as the tool's code for failed tests and for nothing else. Test: `run/r2j` ends with a whole re-run, and red.

## Should

- **S9 — the head-listing read says yes for a folder and for a link.** `ChecksAtBase.cs:111`: `InHead` is `HasPathAsync`, which is `git --no-replace-objects cat-file -e <head>:<path>`. Measured (`git/`): exit 0 for `tests/File.cs`, for the folder `tests/Sub` and for the link `tests/Link`; 128 for a path that is not there. A name of the output that is a folder under a `testFiles` prefix with a plain name (`tests/Sub`) therefore reaches the argv as `./tests/Sub`. It is a path of the head and no option, so no harm follows; the rule's own word is "a file". Use `EntryAtAsync` (the `ls-tree -l -z` read that is there) and take only a blob with mode 100644 or 100755.
- **S10 — the note of the format state when the PREFLIGHT refused.** `CoderFormat.cs:66-69` answers every `BoundaryRefused` with "a unit of the formatter turn could not be stopped". A refused admission is `BoundaryRefused` too. The job's reason (`ran.Note`) is right; the `format.note` beside it is not. By reading.
- **S11 — the test of S3 proves little.** `S3_ThePassedNameOfAnExcusedTest_IsRedactedAndBounded` calls `FlakyRule.SafeName` on a long text and a plain one. It does not show a credential-shaped name, and not that the envelope and `flaky-excused.txt` get the safe name. A change that stops calling `SafeName` in `TryRerunAsync` passes it.
- **S12 — what S1 still does not name**: a changed `adapter` or `memoryMb` of a project. Small.
- **Note — `*.test.js` and `*.spec.ts` never get a selected or a narrowed run**: the stem must be letters, digits and underscores, and the stem of `flaky.test.js` is `flaky.test`. It is closed (the whole step runs). Say so where `testFor` is described for node, or s5 t6's twin for a node repository will read as broken.
- **Open by the coder's own list**: S2 (the format commit; the M6 proof before the formatter turn), S7 (a fact that cannot be written; the dropped note). S6 is gone: a re-run's record now follows its own first run.

## The new tests: would each fail on 1e4e3cbe?

13 cases in `AS5VerifyFixesTests.cs` and one changed line in `AFormatStepTests.cs`. Two bodies were run against the scratch export of the old code; the others were read against the old code and against what pass 1 measured on it. None was run in the rig.

| Test | On 1e4e3cbe | How known |
|---|---|---|
| `M1_TheNarrowedArgv_NeverHoldsAnUnsafeName…` | fails: `[t | --ignore-exit-code | Ok | x | 2 x | ./tests/--ignore-exit-code.cs | …]` where it wants `[t | Ok | ./tests/Ok.cs]` (its `IsSafe` lines do not compile there: the method was private) | body run on the old export |
| `M1_AFileNameOfTheOutput…` (2 cases) | fails: the old code narrows on any name under the prefix, so the re-run's argv differs and `rerun` is `narrowed` | read; pass 1 `r1` |
| `M2_AGreenRoundWithAnExcusedStep_SaysSo…` | fails: the old note is "every one exited 0"; no `firstExit` member | read; pass 1 `r1` |
| `M2_ANarrowedReRun_DoesNotTurnAStepGreen…` | fails: the old code excuses, the job succeeds | read; pass 1 `r2` |
| `M3_ASelectedRunThatSelectedNothing…` | fails: the digest is the selected run's tail, "Zero tests ran" | read; pass 1 `r3` |
| `M3_FailedStep_SkipsANothingSelectedRecord` | does not compile (`NothingSelected`); the old `FailedStep` would answer the selected record | read |
| `S3_ThePassedNameOfAnExcusedTest…` | does not compile (`SafeName`); see S11 | read |
| `S4_TheNamedProof_AlsoAsksForACompleteSummary` | fails: 1 excused where it wants 0 (without the two new init members, which do not compile there) | body run on the old export |
| `M4_AReRunWhoseUnitCannotBeStopped…` | fails: the round is `failed`, a fix turn starts | read |
| `M4_AFormatterUnitThatCannotBeStopped…` | fails: the state is `failed`, the checks go on and pass; no unit record in `format-0` | read |
| `S1_ACommandAndTestFilesTheHeadAdds…` | fails: no line "added the testFor command" | read |
| `S5_TheFormatterTurnsUnits…` | fails: no `format` row in `checks`, no unit record | read |
| `AFormatStepTests` (the `checks` list) | fails: the old list is `restore, build, test` | read |

So 12 of the 13 cases are red on the old code for the behaviour they name, and one (S3) only because a name is missing. Not verified: that the two M4 tests reach the state they describe in the rig (a unit made unstoppable by taking `TimeoutStopSec` out); I read them and did not run them.

## Confirmed safe

1. **M1, the one road.** `TestSelection.Argv` drops every name that fails `IsSafe` whoever calls it; `IsSafe` now also refuses a segment that is empty, `.` or `..` (`TestSelection.cs`). The re-run narrows only when EVERY named file is under a `testFiles` prefix, safe, and in the head (`CheckRunner.cs`, `NarrowableAsync`); else the whole step, or none over 300 seconds. With no `InHead` (a run that is not a coder job) nothing narrows.
2. **The head-listing read is hardened.** `ChecksAtBase` holds `history.ForChecks()`, so the call runs on the main checkout with `--no-replace-objects`, `GIT_NO_LAZY_FETCH=1` and without the names that move git. The head id is forty hex and comes first in `<sha>:<path>`, so the item cannot be an option; the path has passed `IsSafe` (a closed set of characters, no `:` `{` `^` `~`) and `IsRepoRelative` before git is started; an unsafe name never reaches git, because the three checks run in that order. `cat-file -e` ran no configured program in the canary measurement of the s2 verify. At most 5 reads for a step.
3. **M2 for the platform runner.** A first exit code that is not 2 gives a whole re-run, which must itself exit 0 (`run/r2`: red). The `flaky` row carries `firstExit`.
4. **M3.** A selected run that selected nothing is marked and is not the failed step; a green round says how many selected runs selected no test.
5. **M4.** A re-run whose unit survived returns the same `BoundaryRefused` result as any step; the formatter turn hands it to the loop, which adds a round `boundary_refused` with the turn's records and ends `coder-boundary`, without touching the worktree.
6. **The table's reader** takes `testsFailed` as a list of whole numbers and refuses anything else; the tool names of the five readers (`mtp`, `vstest`, `jest`, `vitest`, `node-test`) are the keys of the rows; a tool with no row, or a row of another adapter, gives no code and so only a whole re-run.
