# PLAN — E5 stories: tests that wait for an event (the last epic)

> Status: **plan only, nothing implemented yet, 2026-10-22.** Epic E5 — the last — of
> [PLAN_the_suite_is_deterministic_and_leaves_no_temp.md](PLAN_the_suite_is_deterministic_and_leaves_no_temp.md) (its
> `### E5`, including the "E5 intake" paragraph the E4 judge round added and the cadence consult for epics 4-5, `15f733c7`).
> Branch `docs/suite-e5-stories`, cut from `feat/suite-e4-clock` at `78d91db55` (E1–E4; E4 is PR #916, not yet merged —
> E5's code branches are based on it). Every `file:line` below was read at that commit. Scope: twenty cases — the nine
> stories the parent planned (C22, C21/C24/C26, C16, C3, C18's timing half, C15, C9, C27, C7) and the eleven cases the E4
> judge round filed (C28–C38) — over `src_mcp/tests`, `src_mcp/tests_fakecli`, `src_vs_code/scripts`, `src_vs_code/src/test`,
> and three product files, each named in its story: `runners/Files/LinuxHeldFile.cs` (C26), `runners/Kimi/KimiCredentialsLock.cs`
> (C3, a test seam), `runners/Reviewers/ReviewerExecutor.cs` (C35, a clock seam under D3). **No cornerstone** (§4, "Why no
> cornerstone"); the parent's ledger line stays `Cornerstones: 2 of 2 named — the per-run temp folder (E2, built), the clock
> contract (E4.S1)`. Stories by Fable. **Folded 2026-10-22, each item verified against the code at `78d91db55`:** the E5
> plan round (coai session `51b4e0fd`, verdict proceed, 6 findings, all accepted) — §10; the risk consults for S2
> (`fdd9146f`, `LinuxHeldFile`) and S8 (`c463d7c3`, the executor's clock) — §11. Where §10–§11 and the sections above
> disagree, §10–§11 govern; the sections were edited in place to match. The gate's operator command "split into 4-5
> epics" is a size heuristic; E5 is epic 5 of a FIXED five-epic plan, so no epic is added — the thirteen stories are
> grouped into four BUILD GROUPS, each closed by a checkpoint code round (§5).
>
> Related docs: [PLAN_the_suite_is_deterministic_and_leaves_no_temp_E4.md](PLAN_the_suite_is_deterministic_and_leaves_no_temp_E4.md)
> (the clock contract §5, `ManualClock`, `ClockHandshake`, the structural scan — reused by S8), [module_tests.md](../research/module_tests.md),
> [module_runners.md](../research/module_runners.md), [PLAN_two_equal_deadlines_race_on_macos.md](PLAN_two_equal_deadlines_race_on_macos.md)
> (C8 — the owner of C38 if its kept message names the collection deadline), [PLAN_question_consultant_tails.md](PLAN_question_consultant_tails.md).

## 0. How to read this

The parent is 243 KB; a gate reviewer reads ONLY this document. It carries: the goal (§1), the lessons from E1–E4 that bind
this epic (§2), every case E5 owns with its evidence, its cause marked VERIFIED or INFERRED, the fix, the RED it must show
first and its break-its (§3), the stories with their parallel groups (§4), the order (§5), the test plan (§6), the risks
(§7), the questions for the lead with the assumption each story proceeds on (§8), and the Definition of Done (§9).

Two words are used exactly. **VERIFIED** means the cause is shown by a kept message read against the code, or by a
deterministic reproduction. **INFERRED** means the shape matches a known mechanism but no kept message or reproduction
shows it yet; a case marked INFERRED begins with a DIAGNOSIS step (§2, lesson 4) and a stop rule, and no fix lands on it
before the step decides.

## 1. Goal

Twenty tests and one CI job stop measuring the scheduler. Each one either waits for the event it means (a beat seen, a
handler finished, a parent that has become `sleep`, a pid file written whole), measures the cost it means (CPU, not wall
time), asserts the number it claims (a recorded timeout, a count under a cap), or — where the cause is only inferred — is
fixed from a kept failure and not from a guess. When E5 is done:

1. Every case in §3 is closed by its deterministic RED turned green, or — for the three scheduling races that have no
   deterministic red (C3, and the `R` arm of C24 and C37 if their loops never reproduce) — by its recorded loop before and
   after; then WATCHED for 20 CI runs of its leg (a watch, not the proof — parent §10, J15).
2. No timeout is lengthened, no assertion weakened, no test skipped or removed. Where a wall-clock bound goes, what replaces
   it is said per test and is at least as strong (it names the number the title claims).
3. The whole `src_mcp` suite and the whole extension suite are green on Windows, Linux and macOS with every E5 story in,
   the test count grown by exactly the named scenarios.
4. The parent's five-epic plan is complete: its feature gate is run over the whole feature (§9), and the parent, the E4
   document and this one are promoted together.

## 2. The lessons from E1–E4 that bind E5

1. **A test that passes alone and fails in the suite is a defect — never a flake, never a reason to lengthen a timeout.**
   The parent's rule, unchanged. A story that finds itself reaching for a bigger number has the wrong design (E4 §2).
2. **The machine is loaded by parallel suites, and a failure seen only under that load is still a defect.** E4's S3 ran its
   seam-alone proof inside a 51-minute window with THREE suites on one machine; S4's 20-run loop of
   `AReviewerThatAsksForSourceIsAskedAgainTests` failed 13 of 20 under three parallel suites and 0 of 20 alone. Those are the
   "load-only" failures: nine of E5's eleven intake cases (C30–C38) were seen exactly there. The lesson is twofold. (a) The
   rule still applies — a clock that decides an outcome is removed from the outcome, never widened. (b) The DIAGNOSIS must
   name its load: a loop is run under ONE named load (§6, "the load window"), its result is recorded WITH the load, and a
   case that reproduces only under three suites and never under one is written down as such — it is still fixed where a
   clock decides it, and still not fixed by a number.
3. **No timeout lengthened; a changed test INPUT is said as such.** E4 S3 raised the C17 test's budget from 1 s to 5 minutes
   because at 1 s the system timer could cut a released copy by luck; the DoD recorded it as "a changed test constant that
   makes the deadline wholly the clock's, not a longer wait masking a race", qualified in the PR. E5 does the same: a bound
   that goes is replaced by a claim, and the PR says per test what replaced it and why it is at least as strong.
4. **Diagnosis first for an inferred cause.** C24's first reading ("wait for the late child to be born") was refused by the
   cadence consult because the fake ALREADY waits for that pid before it signals (`tests_fakecli/SupervisionVerbs.cs:204`)
   — a fix with no kept log would have changed nothing. So: **no fix lands on a case whose cause is only inferred until
   either a kept failure (D5 — the full message, duration, OS, run id or local command, attached to the case) or a
   deterministic RED reproduces the cause.** Six cases here start that way (C30 in part, C32, C34, C37, C38, and C24's `R`
   arm); C7 and C27 were already so marked by the parent. Each has a stop rule in §3.
5. **Every red is seen before the fix** — watched failing with the message in §3, then passing; both in the PR. Where the
   product is right and only the TEST is redesigned (C18, C31, C33), the proof the new test has teeth is its break-it watched
   red; the story says so instead of pretending a RED on today's code exists.
6. **The reuse rule, with E4's precedent.** E3 found and deleted a second move loop; E4 subsumed `MovableClock` into
   `ManualClock` and folded `FakeClock` into `FixedClock`. E5 inherits three duplicates and resolves each before building on
   it: two zombie-making shells (`PidfdLayerTests.cs:477`, `AZombieIsNotAliveTests.cs:28`) and a second `/proc` parser
   (`AZombieIsNotAliveTests.cs:61`, against `ProcStat`'s own rule "a second parser is never written", `ProcStat.cs:41-42`)
   — S2; a private `ClockedLauncher` inside one test file (`AReviewerThatAsksForSourceIsAskedAgainTests.cs:618`) that two
   more files now need — S8; two `minimalEnv()` copies in the extension's Windows-install tests
   (`kimiWindowsInstallRuns.test.ts:69`, `vibeWindowsInstallRuns.test.ts:103`) — S12.
7. **The clock contract applies to any class that gains a clock, and the scan enforces it.** E5 seams one more class
   (`ReviewerExecutor`, C35) under D3 — one class, landed WITH its RED. The E4 structural scan
   (`EveryBudgetInAClockedClassReadsItsClockTests.cs:35`, the `Clocked` set) gets the file red first; its companion
   (`:180`) refuses an unclassified clock constructor, so the entry cannot be forgotten. A product hook never calls
   `Advance`; `ClockHandshake` is the one signal-and-wait helper (E4 §5.4 points 6 and 11); and the graces that bound a REAL
   pipe stay on the system clock (E4 Q1 — `EndedProbe`, `ProcessLauncher.cs:209`, is one of them, which decides C34's shape).
8. **A guard is not the measurement.** E4's Q3 replaced `asked.Elapsed < 10 s` with a clock-side proof and kept a 30 s
   guard only to name a hang. E5 does the same wherever a wall bound was the proof (C9, C15, C31, C33, C36): the claim moves
   onto an event or a count, and a guard — the suite's usual one — only names a hang with its own sentence.
9. **Counts per story; docs in ONE final pass.** Each PR names the count before, the count after and the added test names;
   the `research/` docs are written in one pass on the epic branch after the last story merges (E4 §9, plan round finding 5),
   so parallel stories never conflict on `module_tests.md`.
10. **The cadence consult for epics 4-5 (`15f733c7`) binds E5 in three places,** each folded where it lands: point 1 — C15's
    deterministic budget test is separated from its real-process smoke test, whose remaining deadline exposure is STATED
    (§3 C15); point 2 — C16's `Dispose` is close-admission → abort → drain with a bound → stop, and the handler contract is
    cancellation-aware (§3 C16); points 3 and 4 — C27 is E5's with diagnosis first, C24 and C26 open on their gates, C23 is
    E4's. Point 4's sentence "no E5 story needs `ManualClock`" was true of the original list; the intake (C30, C31, C35)
    reuses S4's in-process launcher and so DOES use it — E5 therefore builds on E4's branch, which it already does.

## 3. The cases — evidence, cause, fix, RED, break-its

Grouped as the stories group them (§4). "The CI sentence" is the exact text a kept log shows; a RED's expected message is
written in FluentAssertions' shape where the test is C#, and the coder pastes the observed text into the PR.

### 3.1 C22 — the real-editor job: the editor exited before the extension host ran a scenario (TIMING in the editor's own start) — S1

**Evidence.** The job `extension · a real editor` (`.github/workflows/ci.yml:390-425`; `continue-on-error: true` at `:394`,
`xvfb-run -a npm run test:host` at `:418-420`) failed on PR #888 (run 39282963558 attempt 1, 2026-10-19, a docs-only
branch) and passed on the rerun. The parent's §4 C22 reads the log: the FIRST launch printed nothing from the extension
host — no `ok`, no `FAIL`, no `N/N scenarios passed` (`src/test/host/scenarios.ts:981-990`) — and VS Code's main process
logged `CodeWindow: detected unresponsive` sixteen seconds after the window was created, then `Exit code: 1`; the SECOND
launch (`run-host.mjs:106-107`) started and ran its scenario in fifteen seconds. `runTests` rejects on a non-zero editor
exit, so `run-host.mjs:95-96` recorded `the extension host: Test run failed with code 1` and `:119-122` exited 1.

**Cause — VERIFIED on the mechanism, INFERRED on the trigger.** The renderer did not answer Electron's hung-renderer check
on a cold first start of a freshly unpacked VS Code under `xvfb`; nothing of ours ran. Why the renderer was slow is
inferred (no CPU or disk figure was kept); the harness's two budgets (`LAUNCH_MS` 10 min at `:36`, the scenario budget) did
not fire.

**Fix (harness only — `run-host.mjs`, `scenarios.ts`, `host-job-streak.mjs`; no product code).** Exactly the parent's four
items, restated so the coder needs nothing else: (1) **the sentence names what happened** — when the editor exits non-zero
and the host printed no scenario line, the failure reads `the editor exited (code N) before the extension host ran a
scenario — VS Code's last [main] line: "…"`, quoting the last `[main …]` line of the editor's output the harness already
holds; a scenario failure keeps today's sentence; (2) **the launch budget measures the launch** — `downloadAndUnzipVSCode`
runs first, outside `bounded(LAUNCH_MS)`, and its path is passed as `vscodeExecutablePath`; a slow download has its own
sentence; (3) **one retry of the LAUNCH, never of a scenario, only on proof the host never started** — `run()` prints an
explicit `  host started: N scenarios` line before its loop (`scenarios.ts:981`), distinct from any scenario line; the
harness retries the first launch ONCE, only when the editor exited non-zero AND that line was never seen, and says so
(`the editor did not start the first time; started on the second`); an exit after the host-started line is never
retried; (4) **never silent** — a `::warning::the editor exited (code N) before the extension host ran a scenario — VS
Code's last [main] line: …` annotation is emitted whatever happens next, and `host-job-streak.mjs` counts those annotations
per run of `main` beside the streak it already prints, so the promotion number says how often the editor needed a second
start. Not a longer timeout: the budgets stay.

**RED (S23, two arms), in `src_vs_code/scripts/run-host.test.mjs`.** The harness is driven against a stub
`vscodeExecutablePath` (a `node` script) — so the test needs the launch extracted into a function the test can call with a
stub path and a captured output, which is part of (2). Arm (a): the stub prints `[main 2026-10-22T10:00:00.000Z] CodeWindow:
detected unresponsive` and exits 1 at once, no host-started line. Today: the sentence is `the extension host: Test run
failed with code 1`, the stub was launched ONCE, and no `::warning::` line is in the output — three assertions, three reds:
`expected the sentence to name an editor that exited before the host ran a scenario, got "the extension host: Test run
failed with code 1"`, `expected 2 launches, got 1`, `expected a ::warning:: line`. After: the sentence quotes the `[main …]`
line, the stub is launched twice, the warning is present. Arm (b): the stub prints `  host started: 3 scenarios` then exits
1 — launched ONCE, today's sentence kept. Green before and after (a guard); broken once by retrying regardless of the line
(arm (b) red: `expected 1 launch, got 2`).

**Break-its.** B-E5-1: quote the first `[main …]` line instead of the last → arm (a) red on the quoted text. B-E5-2: the
download left inside `bounded(LAUNCH_MS)` → a unit test of the extracted launch with a stub downloader that rejects after
the (shortened, test-only) budget names the download's sentence, not the launch's. B-E5-3: the streak step counting
annotations of every job rather than this job's → its unit test (a fixture of two jobs' check runs) red.

**Confidence.** Medium on the trigger, high that (1) makes the next failure legible and that (3)'s effect is MEASURED, not
assumed: the count over the next 20 runs of `main`. C22 is excepted from the §6 loop (E2 plan round #9): its evidence is S23
red → green plus that count.

### 3.2 C21 — a zombie made by a shell that may still be a shell (TIMING inside the test) — S2

**Evidence.** `PidfdLayerTests.AZombie_IsGone_NotSignalled` (`src_mcp/tests/PidfdLayerTests.cs:473-496`) failed once on
Linux (run 39276863927, PR #868, a store change) after 10 s 025 ms with the CI sentence `the outer sleep never reaps, so the
killed child should have stayed a zombie` (`BecomesZombieAsync`, `:498-512`, `Assert.Fail` at `:511`).

**Cause — VERIFIED on the window, INFERRED that the shell closed it.** The script `/bin/sh -c "sleep 600 & echo $!; exec
sleep 600"` (`:477`) prints the background pid and the test kills it at once (`:481-485`). The design needs the parent to be
the outer `sleep`, which never reaps — true only AFTER `exec`. Between `echo` and `exec` the parent is the shell; a shell
that collects a dead background child there (bash does) leaves no zombie, the pid is GONE, and the poll runs its 10 s out.
Nothing outside the test is involved (the host is never a subreaper, `AskAcpSubreaperTests.cs:73-79`).

**Fix.** The kill waits (bounded by the test's patience, with its own sentence) until `/proc/<parent>/stat`'s `comm` reads
`sleep` through `ProcStat.ReadFor` (`runners/Processes/ProcStat.cs:68-82`, `Comm` at `:17`) — then kills. The zombie-making
moves into ONE test-support helper, `src_mcp/tests/ZombieMaker.cs`, shared with C29 (lesson 6): it starts the shell, reads
the child's pid, waits for the parent's `comm`, kills, and polls for `Z` through `ProcStat.ReadFor` only; a `Gone` answer
fails with `the killed child's /proc entry is gone — it was reaped before it could be observed as a zombie (its parent was
still a shell?)`, and `Unreadable` with its reason.

**RED (S22).** The arm `bash -c "sleep 600 & echo $!; sleep 2; exec sleep 600"` (bash reaps the killed child while it waits
for the foreground `sleep 2`): today's test fails on every run with the CI sentence after 10 s; with the wait for `comm ==
sleep` the same arm is green. The arm stays in the suite as the guard.

**Break-its.** B-E5-4: the `comm` wait removed → the bash arm red with the CI sentence. B-E5-5: the helper polls `Z` with a
raw `File.ReadAllText` → the bash arm red with C29's exception (below) — the one parser rule, pinned.

### 3.3 C29 — `AZombieIsNotAliveTests` reads `/proc/<pid>/stat` after the entry was reaped — S2

**Evidence.** `AZombieIsNotAliveTests.AKilledButUnreapedProcess_IsNotAlive` (`src_mcp/tests/AZombieIsNotAliveTests.cs:24-53`)
failed on Linux in run 39655713274 (2026-10-21, 30 ms): `System.IO.DirectoryNotFoundException : Could not find a part of the
path '/proc/3825/stat'.` at `BecomesZombieAsync` (`:55-70`, the raw `File.ReadAllTextAsync` at `:60`) ← `:45`.

**Cause — VERIFIED.** The same script as C21 (`:28`), the same immediate kill (`:40-43`), the same window — and a poll that
reads the file RAW (`:60`) and parses the state itself (`:61`, the second parser), so the reaped entry throws instead of
reading as `Gone`. The 30 ms says the child was gone at the first poll: reaped by the shell before `exec`, C21's mechanism
with a different symptom.

**Fix.** The shared `ZombieMaker` (C21): the wait for the parent's `comm`, the poll through `ProcStat.ReadFor`, the second
parser deleted.

**RED.** The bash arm of C21 applied to THIS test: today `System.IO.DirectoryNotFoundException : Could not find a part of
the path '/proc/<pid>/stat'.` — the CI sentence — on every run; green with the helper.

**Break-its.** B-E5-4 and B-E5-5 cover it (one helper, two users).

### 3.4 C24 — the late grandchild: a pid file read while EMPTY (new, VERIFIED), and the `R` arm (still gated) — S2

**Evidence, two messages.** `SupervisedTeardownLinuxTests.AGrandchildBornAfterTheFirstListing_IsFoundByTheReListing`
(`src_mcp/tests/SupervisedTeardownLinuxTests.cs:136-147`):

- 2026-10-20, PR #876's Linux coverage job: the late grandchild's state was `R` where `gone` was expected (`:145`) —
  reported second-hand, full text not kept.
- **2026-10-22, run 39760284371 (Linux, 256 ms), NEW message:** `Expected exit to be 0 because fake-cli: FormatException:
  The input string '' was not in a correct format.` `, but found 97 (difference of 97).` at `SupervisedAsync` (`:112`,
  `exit.Should().Be(0, error)`) ← `:140`.

**What exit 97 means.** It is the fake CLI's "a managed exception escaped" exit: `tests_fakecli/Program.cs:34-46` installs
an `UnhandledException` handler that writes ONE line to stderr — `fake-cli: {type}: {message}` — flushes, and
`Environment.Exit(97)` (`:45`), so the exception's name and message survive where the runtime's own dump would scroll off.
The line here says a `FormatException` on an EMPTY string: `int.Parse("")`.

**Cause of the new message — VERIFIED by reading the line against the code.** `supervise-tree late` (`SupervisionVerbs.cs:97-127`)
prints its report at `:115-124`, whose `states` field is `StatesOf(dir)` (`:130-133`): every `*.pid` file in the folder,
`int.Parse(File.ReadAllText(file))` at `:133`. `late.pid` is written by the agent `late-spawn` (`:77-88`): it waits for the
trigger, starts the `setsid` sleeper, and only then `WritePid` (`:84`) — which is `File.WriteAllText` (`:43`): the file is
CREATED, then written. Between those two steps the file exists and is empty. `Listed` (`:196-206`), called by the teardown
before its first signal (`SupervisedTeardown.cs:32-34`), writes the trigger (`:203`) and waits only for `File.Exists(late.pid)`
(`:204`); the moment it returns, `Ended(members, calls)` SIGKILLs every listed member (`:34`, `:59-60`) — `late-spawn` among
them. Under load the agent is preempted between `open` and `write`, `Listed` sees the empty file, the kill lands, and the
file stays empty for ever; `StatesOf` then parses `''`. The 256 ms duration fits (no waiting anywhere). The test's own
`PidIn` (`:93-105`) waits for a PARSEABLE pid (`:96`) and would have been fine — the fake's wait at `:204` is the weaker
one. So the parent's reading stands and sharpens: the fake does wait for the pid file, but for its EXISTENCE, not its content.

**Fix for the new message (deterministic, lands now).** (1) `SupervisionVerbs.WritePid` writes whole or not at all through
`Serving.WritePidFile` (`tests_fakecli/Serving.cs:114-131`, the sibling-temp-then-move writer #849 added for C4's sharing
violation — reuse, not a second writer). (2) `Listed`'s wait becomes "the file holds a pid" (exists AND parses), one fake-side
helper `PidWritten(file)` used at `:204` and at `:180`, `:184`, `:187` for the other modes. (3) `StatesOf` never throws on
a pid file: an unparseable one reads as `unreadable: '<content>'` in the report, so a torn file is a named state rather
than exit 97. (4) The test's `PidIn` (`:101`) fails with a sentence after its patience instead of a blind `int.Parse`:
`late.pid never held a pid within 30 s (content: '')`. (5) **D5 made concrete for the `R` arm:** every `StateOf(...)` and
`survivors` assertion of the class passes `report.ToString()` as its reason, so the NEXT `R` failure carries the whole
report — the re-listing's survivors, every pid's state — in the kept message.

**RED for the new message.** A structural test, `EveryPidFileTheFakeWrites_GoesThroughWritePidFile`, over
`tests_fakecli/*.cs`: a `File.WriteAllText(`/`File.WriteAllLines(` whose target is a `.pid` path or a `pidFile` identifier
outside `Serving.WritePidFile` is a site — today it names `SupervisionVerbs.cs:43`; green after (1). A second rule in the
same test: a `File.Exists(` wait on a `.pid` path without a parse (today `:204`) — green after (2). A unit test of (3) over
a folder holding an empty `x.pid` is green by construction after (3) (the report reads `unreadable: ''`); its break-it is
(3) reverted (the exception). The kept CI message is the behavioural evidence (D5); no behavioural RED is written for a
microsecond window a test cannot hold open deterministically — said so, per lesson 4.

**The `R` arm — still GATED.** The new message does not explain `R` (a grandchild ALIVE after the teardown): the three
candidates of the parent's §4 C24 stand — the re-listing not finding a pid that exists, the kill not reaching it, or
`StateOf` reading `/proc` before the kernel retired the entry (`SupervisedTeardown.cs:36` confirms until
`ProcessTree.ConfirmationBound`, `:47`, on the system clock; `:54` steps by `Task.Delay(ConfirmationStep)`). **Stop rule:**
with (5) in, the next `R` failure's message (CI or the S13 loop) names the step; the RED is then written for THAT step and
a fix proposed in a follow-up story; until then nothing else in the test changes. If the S13 loop (20 runs of the class under
the load window) shows no `R`, the arm is recorded "not reproduced; watched" and closes with the 20-run CI watch.

**Break-its.** B-E5-6: `WritePid` restored to `File.WriteAllText` → the scan red naming `:43`. B-E5-7: (3) reverted → the
unit test red with `FormatException`.

**Confidence.** High on the torn write (the message names the exact parse and the code names the exact window); low on the
`R` cause.

### 3.5 C26 — an inheritable lock descriptor caught by another test's fork (SHARED STATE across parallel tests) — S2

**Evidence.** `HeldFileInheritableOnLinuxTests.ATakeWhoseStreamCannotBeMade_ClosesItsDescriptor_AndLeavesTheFileFree`
(`src_mcp/tests/HeldFileInheritableOnLinuxTests.cs:117-125`) failed once on Linux (run 39473785590, PR #897's coverage job,
7 ms): `Expected HeldFile.Probe(LockFile) to be LockProbe.Free because the descriptor leaked with its flock: nobody else can
take the file until this process exits, but found LockProbe.Held.` (`:124`). The class is in no collection (`:21`); the lock
file is unique per test (`:25`, `:29`).

**Cause — VERIFIED as a mechanism (the RED below reproduces it on every run), INFERRED as this failure.** `LinuxHeldFile.TakeInheritable`
(`runners/Files/LinuxHeldFile.cs:40-49`) opens the file WITHOUT `O_CLOEXEC` on purpose (`:22`, `:42`), takes `flock(LOCK_EX|LOCK_NB)`
(`:48`), and only then builds the stream (`:52-65`); when the stream cannot be made the handle is disposed (`:61`) and the
flock goes with it. In the window between `:42` and `:61` the descriptor is inheritable in the TEST HOST — a process that
runs up to eight test threads (`xunit.runner.json:3`), many of which start children. Any `Process.Start` on another thread
in that window forks a child that inherits the descriptor and, with it, the flock (a flock belongs to the open file
description; the child's copy keeps it alive after the parent closes). The probe then reads `Held` until that child exits.
7 ms is consistent with the probe running while a sibling test's child was starting.

**Why the parent's two options are not the fix.** "The class moves into a serial collection" cannot help: a collection
serialises its OWN members; the other seven threads keep running other collections' tests, and it is THEIR forks that
inherit. "The probe waits for inheritors to end" is a timer deciding an outcome — the thing this plan removes.

**What `O_CLOEXEC` does and does not do (plan round findings 1+3, risk consult `fdd9146f` — §10, §11).** Close-on-exec
closes at EXEC, not at fork: a child another thread forks while the descriptor is open holds a copy of it — and with it
the flock, which belongs to the shared open file description — until that child execs. So `O_CLOEXEC` alone narrows the
window to fork→exec; it does not close it. And clearing `FD_CLOEXEC` after a successful take makes the descriptor
inheritable by EVERY concurrent fork from then on — which is the product's intent for the one child the shim starts next,
and harmless for the others only because of process isolation (below). **The fix, with both halves:** (1) open WITH
`O_CLOEXEC` (`:42`), take the flock (`:48`), build the stream (`:57`); (2) on success clear `FD_CLOEXEC` with
`fcntl(fd, F_SETFD, 0)` WITH CHECKED error handling — an `fcntl` failure is `Hold.Unusable` naming the errno, after the
descriptor is unlocked and closed; (3) on the ABORTED-acquisition path (the stream cannot be made, `:59-63`) explicitly
`flock(fd, LOCK_UN)` BEFORE the close — the unlock acts on the open file description, so a pre-exec child that inherited a
copy no longer holds the lock; (4) NEVER unlock on the normal successful disposal (`Hold.Taken`'s stream dispose): that
would release the lock Kimi inherited, which is the whole point of the primitive. Two more `LibraryImport`s beside `:83-90`
(`fcntl`, and `flock` is already there). The product's behaviour for every caller is unchanged — a taken hold is inherited
exactly as before (L_N1b, `:192-206`, is the guard) — and the failure path no longer leaves a lock anywhere.

**Why production is not exposed to the "every concurrent fork" half — verified.** The inheritable take is made by the
SEPARATE `--ask-acp` shim process, not by the server: `AskAcpMode.cs:163` takes the hold (`profile.Hold(ask, ask.Deadline)`
→ `KimiCredentialsLock.HoldTree`, `KimiCredentialsLock.cs:172`), keeps it in a `using` (`:189`) and starts the agent through
`AcpClient.AskAsync` at `:191` with "nothing awaited between" (`:35`, `:152`); the class's own remark says what for
(`KimiCredentialsLock.cs:46-51`: "the SHIM's: taken inheritable immediately before the agent starts, so `kimi` inherits it
and the sign-in stays held while EITHER the shim or the agent lives"). The server never owns these descriptors, so its
sibling reviewers and git children cannot inherit them. The test host is the one process that holds the primitive beside
seven forking threads — which is why only a test ever saw C26.

**Is the 7 ms failure a pre-exec child?** PLAUSIBLE, NOT ESTABLISHED. The stream-factory RED below covers a different
window: `.NET`'s `Process.Start` on Unix waits for the child's exec before it returns, so a child started INSIDE the seam has
exec'd — and closed a CLOEXEC copy — before the probe runs; it reproduces the no-CLOEXEC leak (today's code), not the
pre-exec hold. The pre-exec window is reproduced by an isolated helper (below). If a reproducing CI run is ever kept, the
diagnosis tool is `strace -ff -tt -yy -e trace=process,openat,flock,fcntl,close` on the test host: it names the holder.

**REDs (four arms, Linux).** (a) `AForkDuringTheTakesFailure_DoesNotCarryTheLockAway` — the stream factory seam (`:121`)
starts a child (`FakeCli sleep 30000`, kept for the test's `Dispose` to kill) and then throws; the probe must read `Free`.
Today (no `O_CLOEXEC`): `Expected HeldFile.Probe(LockFile) to be LockProbe.Free because a descriptor opened inheritable and
then closed must not survive in a child the host forked meanwhile, but found LockProbe.Held.` — every run. After: `Free`
("failure after a completed exec releases"). (b) `AFailureWithAPreExecChild_StillLeavesTheFileFree` — an isolated helper
(a small native shim or the fake CLI's `hold-file` family extended with a `fork-and-wait` verb under a pipe barrier)
forks AFTER the flock and holds the child BEFORE exec on the barrier, then the stream factory throws: with `LOCK_UN` on the
abort path the probe reads `Free`; without it `Held`. Today: `Held`. (c) `AnFcntlThatFails_IsUnusable_AndLeavesNoLock` — the
`fcntl` seam made to fail: `Hold.Unusable` naming the errno, the file `Free` after. Today: the member does not exist (red
by compilation). (d) the guard, green before and after: `L_N1b_TheHold_SurvivesItsOpenersSigkill_WhileTheGrandchildLives`
(`:192`) plus `L_N1a_ASecondInheritableTake_IsBusy_TheLockIsExclusive` (`:181`) — successful inheritance still excludes a
second taker after the opener dies, and the file is released only after the last inheritor exits (`:205`).

**Break-its.** B-E5-8: `FD_CLOEXEC` never cleared → L_N1b red (`the hold did not survive its opener's SIGKILL while the
grandchild lived — the descriptor was not inherited (O_CLOEXEC) (S-K5)`). B-E5-9: `O_CLOEXEC` dropped from the open again →
arm (a) red. B-E5-9a: `LOCK_UN` dropped from the abort path → arm (b) red. B-E5-9b: `LOCK_UN` added to the SUCCESSFUL
disposal → L_N1b red (the lock is gone the moment the opener's stream is disposed).

**Confidence.** High on both mechanisms (arms (a) and (b) show them); medium that either is the CI failure (no trace of
which sibling forked, nor whether it had exec'd).

### 3.6 C16 — an HTTP stub stopped while its handler still writes headers (SHARED STATE inside the stub) — S3

**Evidence.** `AUsageTheVendorNeverReportedIsUnknownTests.An_api_call_whose_deadline_struck_before_the_answer_is_recorded_as_unknown`
(`src_mcp/tests/AUsageTheVendorNeverReportedIsUnknownTests.cs:107`) failed on Linux in runs 37134428327, 37138270645,
38063638766 and 39278975228 (2026-10-19, 1 s 135 ms) with a `NullReferenceException` thrown by the TEST's `Dispose`:
`NameValueCollection.GetKey ← HttpListenerResponse.FormatHeaders ← … ← HttpListener.Stop ← ApiEndpointStub.Dispose`.

**Cause — VERIFIED (the stack names both threads).** The test's `Dispose` (`:40-51`) sets the hold (`:42`) and disposes the
stub (`:43`) in one breath; the set wakes the handler blocked at `:109-113`, which returns its answer; `AnsweredAsync`
(`tests/ApiEndpointStub.cs:127-189`) starts writing the status, content type and headers (`:158-163`) while
`HttpListener.Stop()` (`:223`) on the test thread enumerates the same header collection. `Dispose` (`:219-229`) tracks
nothing and catches only `ObjectDisposedException`/`HttpListenerException` (`:226`). The serving loop handles ONE request
at a time (`:123`, `await AnsweredAsync` inline) and `Answers(seen)` is called SYNCHRONOUSLY on that thread (`:157`) — the
cadence consult's point 2, which is why "wait for the handler AND return at the bound AND the gate opens after" could not
all hold.

**Fix (the consult's rewrite, verbatim in shape; plan round finding 5 on the order).** `Dispose` (1) CLOSES ADMISSION —
the accept loop stops taking contexts, and a context taken but not yet handled is aborted; (2) ABORTS every in-flight
response it tracks (`HttpListenerResponse.Abort()`, the call `:183` already makes) AND cancels the handlers' `Disposing`
token — both SYNCHRONOUSLY, before any wait, so a handler blocked on the token or on its response is released at once and a
stuck one does not cost the whole bound on every teardown; (3) only then DRAINS the tracked handler task with a bound (2 s, a
constant with its reason — reached only by a handler that ignores both its token and its aborted response) and, when the
bound expires, journals `dispose: a handler outlived the 2 s bound and was aborted: <Method> <Path>`; (4) only then
`Stop()`/`Close()`. The order is cancel-and-abort → drain → stop; never a wait before the cancel. The handler contract becomes cancellation-aware: `Answers` receives the stub's `Disposing` token (an
overload beside today's `Func<Seen, Answer>` so the fifty callers compile unchanged; a reader of `IsDisposed` is the
simpler form), and a late callback — one that returns from `Answers` after `Dispose` returned — can no longer touch the
response: its context is untracked and aborted, its write throws into the handler's own catch (`:178-188`), which `Abort()`s
an already-aborted response (idempotent) and journals it; the listener is never stopped while a header is being written.
Nothing is swallowed: a handler that threw is in the journal, as every failed answer is today (`:180`).

**RED (S11, two arms), in a new `ApiEndpointStubTests.cs`.** (a) `AStubDisposedWhileItsHandlerWrites_WaitsForTheHandler_AndDoesNotThrow`:
a handler released by a gate, then sleeping 300 ms (a real sleep INSIDE the handler, which is the subject — the only sleep
in E5 that stays, because it stands for the writer's own progress) before it writes; `Dispose` is called in that window.
Today: `Dispose` returns at once — `Expected finished to be True because Dispose waits for the handler it still tracks,
but found False.`; after: it returns only after the handler finished, and does not throw. (b) `AHandlerStillActivePastTheBound_IsAbortedBeforeTheListenerStops_AndDisposeDoesNotThrow`:
the handler blocks in `Answers` on a gate the test opens only AFTER `Dispose` returned; `Dispose` must return AT its bound,
without throwing, with its journal sentence; then the gate opens, the late handler's write fails on the aborted response and
is journaled; nothing reaches the listener. Today: nothing is tracked and no sentence exists — `Expected stub.Journal() to
contain "outlived" because the bound names the handler it gave up on, but found "POST /v1/chat/completions -> …".`; the
race itself (the CI `NullReferenceException`) is reproduced by the §6 loop, not by the arm.

**Break-its.** B-E5-10: step (2) skipped (drain without abort) → arm (b)'s "the late write fails on an aborted response"
assertion red (the write succeeds into a stopped listener or throws the CI exception). B-E5-11: `Stop()` moved before the
drain → arm (a) red. B-E5-12: the bound's sentence dropped → arm (b)'s journal assertion red.

**Exposure stated in the PR.** The stub stays single-threaded (one request at a time); C27's diagnosis (§3.10) reads its
journal, so S13's C27 follow-up, if it touches the stub, lands after S3.

### 3.7 C3 — a Kimi shim that meets a disposal's probe: a 250 ms stand-in for an instant, inside a 500 ms wait (TIMING) — S4

**Evidence.** `KimiCredentialsLockTests.AShimThatMeetsADisposalsProbe_WaitsItOut_AndTakesItsHold`
(`src_mcp/tests/KimiCredentialsLockTests.cs:315-329`) failed on Windows in runs 38853192407 (704 ms) and 39151073693
(`main`, 622 ms): `the child ended (exit 69) without taking its lock: … coai-tree.lock is held by another process, and nothing
but a disposal's probe ever takes it` (`ShimChild.StartAsync`, `tests/ShimChild.cs:20-34`, the sentence at `:27`).

**Cause — VERIFIED on the arithmetic, INFERRED that no scan intervened.** The product waits `HomeWait` = 500 ms
(`runners/Kimi/KimiCredentialsLock.cs:77`, used at `:209` through `HeldFile.TakeWithin`, `runners/Files/HeldFile.cs:167-180`)
for a probe that in production holds the file for one open. The test's stand-in `MeetADisposalsProbe`
(`tests_fakecli/TreeHoldVerb.cs:55-59`) holds it for 250 ms and releases from `Task.Delay(250).ContinueWith` (`:58`) on the
thread pool of a freshly started child; 622 and 704 ms are 500 ms of waiting plus a cold child. The margin is scheduling.

**Fix — on the Kimi call site, never inside `HeldFile` (J13).** `WithHome` (`:206-215`) today makes ONE waiting take at
`:209`. It becomes: one `take(path, TimeSpan.Zero)` first; when that answers `Hold.Busy`, the test-only
`WhenTheHomeFileIsBusy?.Invoke(path)` (an `AsyncLocal` beside `BeforeTheHomeHold`, `:114`; null in the product) fires once,
then the waiting take runs as today. The product's behaviour is unchanged (one more open attempt before the same 500 ms
wait); `HeldFile` is not touched; the shim's take stays INHERITABLE (`:175`). `TreeHoldVerb` releases its probe FROM that
seam — `WhenTheHomeFileIsBusy = _ => probe.Stream.Dispose()` — so the test proves its title (the shim met a busy file and
waited it out) with no clock in the outcome; the `Task.Delay(250)` goes. A second mode, `tree-probed-forever`, never
releases: exit 69 after `HomeWait`, the other side pinned without a timer in the TEST (the product's 500 ms is the subject
there).

**RED (S17).** For the race: none deterministic; the before/after proof is the §6 loop (100 runs, the class under the load
window; expect failures at roughly the CI rate before, 0 after). For the seam: a host-side unit test over
`TakeTreeThrough` (`:181-199`) with a `take` stand-in that answers `Busy` once then `Taken`: `WhenTheHomeFileIsBusy` is
invoked exactly once with the home path — today the member does not exist (red by compilation; with a no-op stub,
`Expected busyCalls to be 1 because the seam fires on the first busy answer, but found 0.`); a guard arm: a take that is
`Taken` at once never invokes it.

**Break-its.** B-E5-13: the seam fired BEFORE the no-wait take (always) → the guard arm red. B-E5-14: the verb's release
put back on `Task.Delay(250)` → the loop regresses (recorded, not a unit red).

### 3.8 C18 (timing half) — the heartbeat counted by polling the file every 40 ms (TIMING) — S5

**Evidence.** `QuestionFanOutTests.TheHeartbeatIsRewritten_WhileARowRuns_OnTheConfiguredInterval`
(`src_mcp/tests/QuestionFanOutTests.cs:690-710`) failed on Windows in runs 38906020752 (13 s, `found 1`) and 39253800395
(2.9 s, `found 1`): `Expected beats.Count to be greater than or equal to 3 … but found 1`. (The third failure, the
`UnauthorizedAccessException`, is the product angle E3 S4 fixed.)

**Cause — VERIFIED.** The row is a scripted `Wait = 1500 ms` (`:694`, `ScriptedAnswer.Wait`, `tests/ScriptedLauncher.cs:117`),
the beat is `Task.Delay(every, stop)` every 200 ms (`src/Server/QuestionConsult/QuestionFanOut.cs:302-309`, `:309`), and
the test counts distinct `HeartbeatUtc` values by polling the store every 40 ms (`:700-707`). How many distinct values it
sees is how many timer ticks the scheduler granted inside a fixed delay.

**Fix.** The store's `projected` callback (`QuestionConsultStore.cs:25`, invoked on every write at `:58` and `:126`) collects
distinct `HeartbeatUtc` values; the scripted row waits on a GATE instead of `Wait = 1.5 s` (`ScriptedAnswer` gains a
`Task? Gate` beside `Wait`, `:117`; `ScriptedLauncher` awaits it when set), and the test opens the gate when three distinct
beats were seen — under the suite's 30 s guard, which fails with `the row was never released: 1 distinct heartbeat(s)
seen in 30 s, 3 expected`. The 200 ms interval stays as the product's seam; the test depends only on "the heartbeat is
rewritten while a row runs".

**RED (S18).** The product is right today, so the redesigned test is green on it; its teeth are shown by the break-it: the
heartbeat loop removed (`:302-309` returning at once) → `the row was never released: 1 distinct heartbeat(s) seen in 30 s,
3 expected` after the guard. Both observations in the PR (lesson 5).

**Break-its.** B-E5-15: the loop removed (above). B-E5-16: the gate opened on the FIRST beat → the test's "three distinct"
assertion on the collected set red.

### 3.9 C15 — a whole-turn stopwatch for a launch-level claim (TIMING) — S6

**Evidence.** `ConsultAntigravityDenialScenarioTests.AHungFollowUp_IsKilledByTheLauncher_InsideTheRemainingBudget_AndTheConversationIsKept`
(`src_mcp/tests/ConsultAntigravityDenialScenarioTests.cs:368-397`) failed once on Windows (run 38964817927): `Expected
time to be less than 11s because the follow-up was capped to what the first launch left, but found 12s, 961ms` (`:386`).

**Cause — VERIFIED.** The stopwatch (`:378`) wraps the whole consult — git processes, snapshots, the prompt diff, the
records — and allows 3 s of it beside 8 s of launches (`:386-387`). The claim is about launch 2's TIMEOUT, which the product
computes at `runners/Consultation/ConsultantTurn.cs:107` (`RetryLadder.Lesser(left, next)`, `left` from the `Stopwatch` at
`:89` read at `:101`); `ConsultantTurnTests.cs:90` already pins the cap at the runner's level (strictly less than a minute).

**Fix (the cadence consult's point 1, folded).** (a) The deterministic budget test: the scenario base's launcher wrapper
`TurnLauncher` (`tests/ConsultScenarioBase.cs:41`) records each launch's `ProcessRequest.Timeout`, its result's `TimedOut`
and its elapsed (widened, not a second wrapper); the test asserts launch 2's recorded timeout is at most `8 s − launch 1's
elapsed` and that launch 2 ended `TimedOut` — the number the title claims, where the stopwatch let any follow-up under 3 s
of slack pass. The two wall-clock lines (`:386-387`) go. (b) The REAL-PROCESS SMOKE TEST keeps its 6 s sleep
(`FAKECLI_SLEEP_MS`, `:375`) and its 8 s budget (`:377`) — it proves the whole consult path kills a hung follow-up through
the launcher (two launches `:391`, the record `Interrupted` `:393`, the handle kept `:394`, the ledger's tokens `:395-396`)
— and its remaining exposure is STATED in its remark and in the PR: a first `FakeCli` launch whose cold start exceeds 2 s
under load is itself killed at the 8 s budget, `Offered` then returns null for a terminal launch (`ConsultantTurn.cs:94-98`)
and `Launches().HaveCount(2)` fails — a smoke failure that names the machine's load, not the cap; a kept failure of it is
read against the cold start, never answered with a longer budget.

**RED (S19).** `RetryLadder.Lesser(left, next)` at `:107` replaced by `next` (the whole budget): `Expected launches[1].Timeout
to be less than or equal to 2s because the follow-up gets only what the first launch left of 8s, but found 8s.` — every
run; green with the cap.

**Break-its.** B-E5-17: the wrapper records the FIRST launch's timeout for both → the assertion red the other way (`found
8s` where `≤ 2 s` is expected — the same sentence, so the break-it also pins that the wrapper records per launch:
`launches[0].Timeout == 8 s` is asserted beside it). B-E5-18: `TimedOut` not recorded → the "ended TimedOut" assertion red.

### 3.10 C27 — the streaming answer cap: exit 69 where 70 was expected, under load (cause NOT read) — S13 → S3's area

**Evidence.** `AStreamsUsageIsNeverZeroTests.A_stream_over_the_answer_cap_is_unknown` (`src_mcp/tests/AStreamsUsageIsNeverZeroTests.cs:85-95`)
failed once in E2's full `src_mcp` run on a loaded Windows machine (2026-10-20): the exit was 69 where 70 was expected
(`:93`); it passes alone. No CI failure; no message beyond the two numbers.

**What the numbers say — VERIFIED; the cause INFERRED.** Seventeen 512 KiB chunks (`:88-89`) over `AskApiMode.MaxAnswerBytes`
8 MiB (`src/Api/AskApiMode.cs:50`), each under `MaxStreamLineChars` 1 MiB (`:56`), expecting `Failed` = 70 (`:39`). 69 is
`ApiRuntime.EndedBeforeAnAnswerExit` (`runners/Reviewers/ApiRuntime.cs:39`, `AskApiMode.Unavailable`, `:38`): the stream
ENDED before the eighth-plus megabyte reached the cap. The run is in-process (`AskApiMode.RunAsync` at `:134` with no
handler → a real `HttpClient` to the stub) under `--timeout-seconds 20` (`:119`, `:131`). Candidates: the stub's chunked
writer (`ApiEndpointStub.cs:192-209`) losing the connection under load; the 20 s deadline striking inside 8.5 MiB of
loopback traffic on a starved machine (a C5-shaped deadline on the system clock, through `HttpClient`); the client's own
read bound.

**DIAGNOSIS step (S13), stop rule.** Twenty runs of the class under the load window (§6), the stub's journal (`Noted`,
`:176`/`:180` — it names the exception the handler saw) and the runtime's stderr kept per failure. Decides: (a) the journal
shows the handler's write failing (`-> HttpListenerException` or `IOException`) → a STUB defect: the stream is served from
memory in one write per chunk with no flush-per-chunk back-pressure dependence, or the stub's chunk loop is driven to
completion by an event — a coder story AFTER S3 (same file); (b) the stderr names the deadline (the shim's own "deadline"
sentence) → the test asserts the cap on a stream whose SIZE, not duration, is what is served (the stub serves the 8.5 MiB
from one buffer) and the 20 s is NOT lengthened; (c) 0 of 20 → "not reproduced under one named load; watched", closed by the
20-run CI watch and the E2-run note.

### 3.11 C7 — `SqliteMigratorTests` "database is locked" (cause NOT determined) — S13

**Evidence.** Reported locally; not in any of the 174 failed CI logs; no stack kept.
`TwoMigratorsRacing_TheSecondWaitsAndNeverReRunsAStep` (`src_mcp/tests/SqliteMigratorTests.cs:112-136`, the remark
`:96-108`) is the one test with two connections: step 1 builds three hundred thousand rows (`:118`) so the second opener
must wait; the product sets `journal_mode=WAL` then `busy_timeout=5000` (`storage/SqliteMigrator.cs:68-69`,
`BusyTimeoutMilliseconds` at `:46`, the provider's own `Default Timeout=5` at `:58-59`).

**Candidates (parent §4 C7): INFERRED.** H1 (timing): the slow step outlasts 5 s on a starved machine — the product then
does what it promises and the test's step size decided it; the stack would show the version read or the transaction begin
after ≈5 s. H2 (environment): a scanner's handle on the fresh `-wal`/`-shm` files; the stack would show the error after
milliseconds.

**DIAGNOSIS step (S13), stop rule.** Twenty runs of the class under the load window with the full stack kept, and the slow
step's duration measured idle and under load (a `Stopwatch` around step 1 written to the test output — a measurement, not an
assertion). Decides: H1 → the step is sized from the measurement to stay well under 5 s at the measured worst while still
forcing the wait (the product's 5 s unchanged); H2 → recorded, the test unchanged, the environment named; 0 of 20 and the
step measured under 2 s at worst → closed with the recorded measurement (the parent's DoD allows exactly this).

### 3.12 C9 — the extension's "many lines nobody reads still finishes promptly" (TIMING) — S7

**Evidence.** `src_vs_code/src/test/processLauncher.test.ts:250-269` failed locally at the `ended` helper's 10 s timer
(`:22-34`); in every extension CI log it passes in 0.10–0.19 s. It spawns `node -e` writing 40,000 lines and asserts
`took < 4_000` ms (`:268`).

**Cause — VERIFIED on what is measured, INFERRED that load alone explains the local failure.** Wall time of a fresh `node`
plus 40,000 pipe writes under `node --test` with up to eight files at once (`scripts/run-tests.mjs:84`,
`TEST_CONCURRENCY`) and other sessions. What the test MEANS — "the fan does not rebuild its buffer per line" — is a CPU
cost: the quadratic `held = [...held, value]` cost 64 s at 130,000 values, the append 0.01 s (`src/processLauncher.ts:203-209`,
the push at `:219`); at 40,000 values the quadratic version costs about 6 s of CPU.

**Fix.** `replayingFan` (`:194`, module-private today) is exported for tests; a direct test emits N values with no
subscriber and asserts `process.cpuUsage()`'s delta (user + system) under a threshold. **The sizing, numerically (plan
round finding 6).** The file's own measurement is 64.09 s for 130,000 quadratic emits and 0.01 s for the append (`:203-205`).
Scaled: at N = 40,000 the quadratic fan costs (40/130)² × 64.09 ≈ **6.1 s** of CPU and the append (40/130) × 0.01 ≈
**3 ms**; at N = 60,000, 13.7 s against 5 ms. The threshold is **1,000 ms at N = 40,000**: six times UNDER the quadratic
figure and three hundred times OVER the linear one — an order of magnitude of margin on both sides, so neither a slow
machine (CPU time, not wall time, is what is measured) nor a fast one can cross it. **Isolation:** `node --test` runs each
test file in its own process, so the delta is the file's; inside the file the test first emits 1,000 values and yields
(`await setImmediate`) so the setup's allocation and any pending GC are outside the measured window, then reads
`cpuUsage()` before and after the 40,000; nothing else runs in the file concurrently (its tests are sequential). The
real-child test stays as a
smoke test of the pipes — exit 0 and every character arrived (`:267`) — WITHOUT a wall-clock assertion and WITHOUT the
`ended` helper's 10 s timer (J5): it waits on `onExit`/`onError` alone and leaves the hang budget to the runner's own
per-file timeout, which names the file.

**RED (S20).** `held = [...held, value]` put back for one run: the CPU test red — `the fan spent 6103 ms of CPU on 40000
values; an append costs under 1000 ms` — and the smoke test green (every character still arrives, late).

**Break-its.** B-E5-19: the CPU bound measured in wall time (`Date.now()`) instead of `cpuUsage` → a deliberate
`Atomics.wait` of 1.5 s in the test's own thread turns it red where the CPU form stays green (pinned as a self-test of the
measure). B-E5-20: the smoke test's `ended` timer restored → a scan of the test file for `setTimeout(` inside `ended` red.

### 3.13 C30 — two turn-timeout tests on a real 2 s deadline against a cold `FakeCli` (C13's mechanism) — S8

**Evidence.** `AReviewerThatAsksForSourceIsAskedAgainTests.ATurnThatTimesOut_IsOneTerminalFailure_AndTheSlotIsReleased`
(`src_mcp/tests/AReviewerThatAsksForSourceIsAskedAgainTests.cs:342-359`) and
`ATurnWhoseRepairTimesOut_KeepsTheMalformedAttemptsCost_AsOneTerminalFailure_AndReleasesTheSlot` (`:366-385`) failed in E4
S3's final whole-suite run (13,142 tests, 4 failed); green alone; the messages were not kept.

**Cause — INFERRED (the shape is C13's, which S4 closed for the OTHER tests of the class).** Each runs `Work(timeout: 2 s)`
(`:348`, `:373`) through the REAL `ProcessLauncher` (`_executor`) and a cold `FakeCli`; turn 1 must start, answer and be
read inside the 2 s, and turn 2 sleeps 10 s (`:345`, `:370`) so only the launcher's own timer (`ProcessLauncher.cs:481-482`,
untouched — C4's gate) can end it. A cold start measured at up to ten seconds under load eats turn 1's 2 s.

**Fix.** Both tests move onto S4's in-process `ClockedLauncher` (`:618-680`) with `ManualClock` — exactly the shape of the
class's cap tests at `:485`, `:517`, `:547`, `:557`. The stand-in is EXTRACTED to `src_mcp/tests/ClockedLauncher.cs` (lesson 6;
S8 is the only story that touches it) and WIDENED with a per-launch script (`IReadOnlyList<(TimeSpan Turn, string StdOut,
string OutFile, int Exit)>` in launch order; the one-`turn`-one-`stdout` constructor stays for the existing callers): turn 1
answers in 400 ms with `OneThousandIn`; turn 2 "runs" 10 s against a 2 s request timeout and answers `TimedOut` (the
stand-in's `turn > request.Timeout` rule, `:647-649`); the repair variant scripts turn 2's first launch as junk with
`SevenHundredIn` and the repair as the 10 s one. The REAL `BoundedScheduler` and the second work item stay (the slot's
release is the subject). The 2 s is not lengthened; no process is started.

**DIAGNOSIS first (small).** Before the move, S8 runs the class 20 times under the load window and keeps the messages —
C30's AND C37's (same class, §3.20) — so the inference becomes a kept failure (D5) and the moved tests are checked against
the real sentence.

**RED.** The moved tests are deterministic by construction; the teeth are shown by S4's break-its re-run against them:
`TurnLoop`'s terminal handling of a `TimedOut` turn broken (S4's B11/B12 shape) → `Expected timedOut to be of type
ReviewerOutcome.TimedOut, but found ReviewerOutcome.Ok` / the earlier turn's usage lost. And the per-launch script's own
RED: with the old single-`stdout` stand-in the repair variant cannot express "junk, then a timed-out repair" — the test
does not compile; that is its red by absence.

**Exposure stated in the PR.** The real launcher's deadline on a sleeping child is proved by
`ProcessLauncherTests.AChildThatNeverReadsItsStdinIsStillKilledOnTime` (`tests/ProcessLauncherTests.cs:119-136`, `TimedOut`
at `:136`); nothing in these two tests depended on it beyond that.

### 3.14 C35 — the repair gets what is LEFT: two real 1.5 s launches against a 2 s budget plus a 3.5 s wall bound — S8

**Evidence.** `RepairRunsInsideTheBudgetTests.ARepairGetsWhatIsLEFTOfTheDeadline_NotAWholeSecondOne`
(`src_mcp/tests/RepairRunsInsideTheBudgetTests.cs:35-58`) failed in E4 S3's seam-alone run (green alone, 5 tests) and once
in S4's 20-run loop under three parallel suites ("an unrelated repair-timeout test"). Message not kept.

**Cause — VERIFIED on the shape.** Two real `FakeCli sleep 1500` launches (`:31-32`, `:46-47`) through a real
`ProcessLauncher` (`:28`) against a 2 s budget (`:43`); the OUTCOME is the assertion (`:50-51`), the wall bound `< 3.5 s`
(`:57`) is context — and under load either the first launch overruns its 1.5 s share (the outcome is still `TimedOut`) or
the two cold starts push the whole past 3.5 s.

**The executor's clock — the one seam E5 lands, under D3.** `ReviewerExecutor.RunAsync` (`runners/Reviewers/ReviewerExecutor.cs:593-607`)
starts a `Stopwatch` at `:601`, passes it (`:609`), and computes the repair's remainder from it at `:627`
(`RetryLadder.Remaining(spent.Elapsed, budget)`); `:640`/`:652` read it for the record's elapsed; `:523` stamps a file name
with `DateTime.UtcNow`. With an in-process stand-in that spends no real time, `spent.Elapsed ≈ 0` and the repair would be
handed the WHOLE budget — the property cannot be observed without a clock. So `ReviewerExecutor` gets `TimeProvider? clock
= null` (the `BoundedScheduler.cs:96-102` shape), its `Stopwatch` becomes `_clock.GetTimestamp()`/`GetElapsedTime`, and the
`:523` stamp reads `_clock.GetUtcNow()` (one clock per class — E4 S3 did the same for the places' folder stamp). The E4 scan's
`Clocked` set (`EveryBudgetInAClockedClassReadsItsClockTests.cs:35`) gains `src_mcp/runners/Reviewers/ReviewerExecutor.cs`
RED FIRST (the seam-shape rules, E4 §17 M1), and the companion (`:180`) would refuse the unclassified constructor anyway.
The Opus brief for S8 writes the inventory table for this class in E4 §5.1's form (every clock read; what each reads after;
the `:539` remark's `CancelAfter(request.Timeout)` is the LAUNCHER's, which stays). The seam lands alone first with the
suite green on the default clock (E4 §5.3), then the test moves.

**Three rules from the risk consult `c463d7c3` (§11), each verified.** (1) **One clock instance, passed explicitly to all
three.** The executor keeps its own optional `TimeProvider? clock` (standalone callers construct it alone); a test passes
the SAME `ManualClock` to the scheduler, the executor and the driver. Today's `ConversationAsync`
(`AReviewerThatAsksForSourceIsAskedAgainTests.cs:580-596`) gives the scheduler a clock (`new BoundedScheduler(clock: clock)`,
`:585`) but constructs `new ReviewerExecutor(launcher)` (`:586`) — after the seam that still COMPILES and silently defaults
the executor to system time. A RED must catch exactly that: with the executor on the default clock and the stand-in
spending no real time, the 2 s / 1.5 s arithmetic hands the repair the whole budget — the `found 2s` sentence below — so
the helper takes the clock once and hands it to all three, and the test that would compile with a missing clock is the one
that goes red. (2) **The remainder and BOTH launch-duration records move together.** `:627` (the remainder), `:640`
(`started`) and `:652` (the second part's elapsed) all read `spent`; they all become `_clock.GetElapsedTime(stamp)` reads
of ONE stamp — a record that still read a `Stopwatch` would report a duration of zero on the manual clock while the
remainder moved. (3) **`AbandonBudget` STAYS on the system clock as teardown.** `AbandonAsync` (`:818-835`) arms
`new CancellationTokenSource(AbandonBudget)` (`:827`, 10 s at `:838`) around the adapter's courtesy cleanup — for the
remote runtime a real HTTP cancel (`RemoteRuntime.AbandonAsync`, `runners/Reviewers/RemoteRuntime.cs:252-260`, on a client
whose own timeout is 10 s, `:242`). That is real cleanup I/O after the outcome is decided: a manual clock nobody advances
would park the cleanup for ever. E4's teardown exception (contract (1), the lead's Q1: "a grace that bounds a real pipe
after the outcome is decided") is EXTENDED in wording to "real cleanup I/O", and the scan's `Sanctioned` allow-list
(`EveryBudgetInAClockedClassReadsItsClockTests.cs:60`, the `Allowed(File, Snippet, Reason)` shape) gains ONE narrowly
justified entry — `ReviewerExecutor.cs`, snippet `new CancellationTokenSource(AbandonBudget)`, reason "TEARDOWN — real
cleanup I/O (the adapter's courtesy cancel after a kill); a manual clock would park it" — the only unclocked site left in
the class. Its test: with the manual clock FROZEN, an adapter stand-in whose `AbandonAsync` awaits its token is still
cancelled by the system-time bound (shortened through a test-only `internal` seam on the budget, or asserted through the
stand-in observing `ct.IsCancellationRequested` within the suite's guard), and the ORIGINAL timeout/cancellation outcome of
the launch survives the cleanup (`TimedOut` stays `TimedOut`).

**What the stand-in proves, said plainly.** The scripted `ClockedLauncher` proves budget ALLOCATION — what the executor
hands each launch — not the production launcher's timer ENFORCEMENT. The real `ProcessLauncher` deadline tests
(`tests/ProcessLauncherTests.cs:119-136` and its siblings) stay as the enforcement proof and are named so in the PR.

**Fix for the test.** `ARepairGetsWhatIsLEFT…` runs on `ManualClock` + `ClockedLauncher`: launch 1 is scripted to 1.5 s
(the stand-in advances the clock by it), the repair's recorded `request.Timeout` must be 500 ms and the repair "runs" 1.5 s
against it → `TimedOut`. The wall bound (`:57`) goes; the assertion that replaces it is the exact number
(`Timeouts[1] == 500 ms`), stronger than "under 3.5 s". `AFastFirstLaunch_LeavesTheRepairPlentyOfTime` (`:61-70`) moves the
same way (`Timeouts[1]` near 10 s, the repair `Ok`); the zero-left arm (`NoRepairToRun`, `:582`) is already pure.

**RED.** On the unseamed executor with the stand-in — and, after the seam, on an executor constructed WITHOUT the clock
(rule 1): `Expected launcher.Timeouts[1] to be 500ms because the repair gets what is LEFT of a 2s budget after a 1.5s first
launch, but found 2s.` — every run (the Stopwatch saw no time pass). Green with the seam and the shared clock. The scan's RED:
`ReviewerExecutor.cs: Stopwatch.StartNew( …` named as an unclocked site; after the seam, `new CancellationTokenSource(AbandonBudget)`
named until the allow-list entry is in (red first, then allowed with its reason).

**The tests, listed (consult c463d7c3):** 1.5 s of a 2 s budget → the repair's request is EXACTLY 500 ms; a repair whose
own timeout is already shorter than the remainder stays shorter (`RetryLadder.Lesser`); an exhausted budget starts no
repair (`NoRepairToRun`, `:582`); the launch-duration records reflect the same advances (the first part's and the second
part's elapsed equal what the driver advanced); a timed-out repair keeps the earlier launch's usage and releases the slot
(C30's shape, on this class's seam); the default-clock seam-only run green; the real-process `ProcessLauncher` timeout
tests still green; the frozen-clock abandon test (rule 3) green.

**Break-its.** B-E5-21: the `Stopwatch` left beside `_clock` for the remainder → the scan red AND the test red with `found
2s`. B-E5-22: the stamp at `:523` left on `DateTime.UtcNow` → the scan red (named site). B-E5-23: the stand-in advancing the
clock from its own thread → `ManualClock`'s nested-advance refusal (E4 M15) names it. B-E5-23a: `AbandonBudget` moved onto
`_clock` → the frozen-clock abandon test hangs under its guard with the guard's sentence. B-E5-23b: one duration record left
on a `Stopwatch` → the "same advances" test red (`found 0ms`).

### 3.15 C31 — a wall bound of 3 s around a 300 ms sizing deadline over a 4 s stand-in — S9

**Evidence.** `TheSizingIsConcurrentAndBoundedTests.ASizingThatOutlastsItsDeadline_ReturnsGitFailedScopes_WithinIt`
(`src_mcp/tests/TheSizingIsConcurrentAndBoundedTests.cs:116-129`) failed in E4 S3's final run; green alone; message not kept.
Its sibling `ALauncherThatIgnoresItsToken_CannotHoldTheSizingPastItsDeadline` (`:133-142`) has the same shape (`:141`).

**Cause — VERIFIED on the shape.** The `Stamping` stand-in (`:169-199`) holds each call 4 s (`Task.Delay(hold, ct)`, `:194`)
and records its start and end on its own `Stopwatch` (`:171`, `:197`); the test arms a 300 ms sizing deadline (`:121`) and
asserts the WHOLE returned under 3 s of wall time (`:124`). A continuation delayed 2.7 s by a loaded machine fails it while
the product did exactly what it promised.

**Fix.** The claim "the sizing answers at its deadline, not when its slowest call is done" is an ORDER of two events on
the stand-in's own stopwatch, not a distance from a fixed number: the stand-in gains a `ReturnedAt` mark the test sets when
`Measure` returns (the same `Stopwatch`), and a per-call `Cancelled` flag (the token observed cancelled inside `:194`); the
assertions become `returnedAt < slow.Calls.Min(c => c.End)` — the sizing returned BEFORE its slowest call ended — and every
call `Cancelled` (for the deaf launcher: the wait returned before the call's end, the call not cancelled). Under load both
marks shift together. The 300 ms product deadline stays on its own clock (it is not one of E4's seamed classes and no test
needs it to be — the ORDER is observable without a clock seam).

**RED.** The product is right today, so the redesigned tests are green; the break-it shows the teeth: the sizing's deadline
not armed → `Expected returnedAt to be less than 4.0s (the slowest call's end) because the sizing answers at its deadline,
not when its slowest call is done, but found 4.0s.` Both in the PR.

**Break-its.** B-E5-24: the deadline not armed (above). B-E5-25: the stand-in ignoring its token in the honouring arm →
the `Cancelled` assertion red.

### 3.16 C33 — `initialize` answered while the vault read runs: an 11 s stdio round trip on a starved machine — S10

**Evidence.** `TheServerAnswersInitializeAtOnceTests.Initialize_IsAnswered_WhileTheVaultReadAndTheProbeAreStillRunning`
(`src_mcp/tests/TheServerAnswersInitializeAtOnceTests.cs:44-77`) failed in E4 S3's seam-alone run (51 minutes, three suites):
the stdio round trip timed out at `RoundTrip(…, timeoutSeconds: 10)` (`:58-62`; `tests/StdioServer.cs:47-55`, the
`WaitAsync` at `:51`).

**Cause — VERIFIED on the shape.** A REAL server process starts under load; the fake `creds` on its PATH sleeps `SlowMs` =
20 s (`:23`, `:53`); the claim "initialize is not held by the vault read" is measured as 10 s against 20 s (`:62-63`) — a wall
bound on a cold process start.

**Fix (plan round finding 2: a GATE, not a sleep).** A fixed 20 s sleep with an end marker would fail falsely the day a
slow `initialize` reply takes longer than 20 s — the marker would exist by then. So the vault read blocks on a
TEST-CONTROLLED GATE: the fake, in vendor mode, answers only once a file the test names exists (`FAKECLI_ANSWER_GATE`, the
shape `FAKECLI_RECORD_GATE` already has — `tests_fakecli/Program.cs:57`, `:174`, `:678`: "wait until this file exists"
— reused for the answer; it replaces `FAKECLI_SLEEP_MS` in this test, so there is no 20 s anywhere). The test: (1) waits for
the reply line to `initialize` with the suite's guard (named as a guard: `the server never answered initialize within 60 s`
— a hang's sentence, not the measurement) while the gate is CLOSED — the vault read cannot have finished, by construction;
(2) asserts the reply arrived (the ORDER is now a fact, not a race); (3) RELEASES the gate; (4) verifies the read completes
— `providers` answers (`:69-72`, `timeoutSeconds: 90` stays as the guard it is) and reports the vault's outcome. `:63` goes;
no bound is lengthened — the 10 s was the measurement and the measurement is now the gate's order. The gate is released in
the test's `finally` too, so a failing test never parks the fake.

**RED.** The product is right today; the break-it: the server made to await the vault read before answering `initialize`
(the restart-storm defect, D2 of `PLAN_an_idle_server_is_idle_and_starts_at_once.md`) → the reply never arrives while the
gate is closed: `the server never answered initialize within 60 s (the answer gate was still closed: the server waited for
the vault read)` — the guard's sentence, which the test extends with the gate's state so the hang names its cause.

**Break-its.** B-E5-26: the above. B-E5-27: the gate honoured AFTER the fake's answer instead of before (the fake answers,
then waits) → step (1) passes but step (4)'s "the read had not completed before the release" guard arm (a fake-side marker
written when the gate opens, asserted absent at the reply) red — pinned by a fake-side unit of the gate's placement.

### 3.17 C34 — `AChildThatExitedBeforeItsStartTimeWasReadTests` under Windows load: a `Win32Exception` (C2's family) — S13

**Evidence.** Failed in E4 S3's seam-alone run with a `Win32Exception`; green alone (10 tests); which Theory arm and the
message beyond the type were not kept.

**Cause — INFERRED, with one candidate that the code names.** Four arms (`src_mcp/tests/AChildThatExitedBeforeItsStartTimeWasReadTests.cs:30`,
`:46`, `:60`, `:85`) inject a start-time read that throws AFTER the child exited. The launcher catches the throw only when
`HasEnded(process)` (`runners/Processes/ProcessLauncher.cs:375`), which is `WaitForExit(EndedProbe)` with `EndedProbe` =
100 ms (`:209`, `:393`) — a real-process grace E4 §5.5 names as staying on the system clock. The arm
`AChildThatIsStillEndingWhenTheReadFails_IsGivenAMomentToEnd_…` (`:87-120`) ends the child from a task the test owns —
`EndsTheChildSoon`: `Task.Delay(10 ms)` then `Kill` (`:152-156`) — so that the end lands INSIDE the launcher's 100 ms wait.
On a loaded Windows machine a 10 ms thread-pool continuation can land after 100 ms; `HasEnded` then answers false, the
`Win32Exception` travels, and the arm fails with exactly that type. The other three arms kill or let the child exit BEFORE
the read, and would not show a Win32Exception. So the candidate is: the test's 10 ms deferred kill racing the product's
100 ms probe.

**DIAGNOSIS step (S13), stop rule.** Twenty runs of the class under the load window, the full message kept with the arm
and the spelling. Decides: (a) the "still ending" arm → the race above is VERIFIED, and the honest fix is NOT a seam on
`EndedProbe` (E4 §5.5 and the lead's Q1 keep it on the system clock) and NOT a longer probe: the arm is a REAL-PROCESS
smoke test of a 100 ms window by nature; it keeps its mechanism, asserts `aliveWhenTheReadFailed` FIRST (so a late kill is
told apart from a wrong catch), and its failure sentence names the exposure — `the child was still alive after the
launcher's 100 ms ended-probe: the test's deferred kill landed late (load), not a wrong catch` — with the exposure stated
in its remark and the PR (§8 Q4 asks whether the lead wants the arm kept with that sentence or the probe seamed under C4's
gate now that a failure is kept); (b) another arm → C2's fix (E3 S1) is short of a Windows case: read against
`ProcessLauncher.cs:367-399` and filed as a follow-up with its kept message; (c) 0 of 20 → watched.

### 3.18 C36 — `QuestionOutlineTimingTests`: red ALONE, 61–68 s against a 60 s bound — a size, a load, and a bound that protects the wrong thing — S11

**Evidence.** `QuestionOutlineTimingTests.TheOutlineOfThisCheckout_IsBuiltAtHead_InBoundedTime_AndTheTimeIsRecorded`
(`src_mcp/tests/QuestionOutlineTimingTests.cs:22-43`): 61–68 s against `< 60 s` (`:42`) on the E4 machine (S2's seam-alone
run, with other suites running); `research/module_tests.md:198` records **6.2 s, 1,997 files** on 2026-10-01; **measured
for this document on 2026-10-22, on this machine with other sessions' processes running: `3561 file(s) changed against the
empty tree, 73 outlined, 165031 bytes of section, built in 12671 ms`** (the test's own `[measurement]` line, run through
`low-priority.mjs`, class alone).

**What the test really measures — VERIFIED.** `FeatureOutlineBuilder.BuildAtHeadAsync` (`runners/Feature/FeatureOutlineBuilder.cs:110-121`)
over THIS checkout: every file at HEAD is "changed" against the empty tree (`:118`), so the cost has two parts — a part that
scales with the TREE (listing and sizing 3,561 files through git) and a part the product CAPS (reading and outlining:
`MaxOutlinedFiles` = 400, `core/Feature/FeatureBudget.cs:126`; the section budget stopped this tree at 73 outlined; the
read ceiling, `FeatureOutlineBuilder.cs:56`; the cap sentences, `ReadPlan.cs:76-77`). The product calls exactly this per
question for a `none` row (`src/Server/QuestionConsult/QuestionFanOut.cs:145`), so the number IS a product cost — but it
is 12.7 s here with 1.8× the files of 2026-10-01 and 2× its time, and 61–68 s on a machine running three suites: the
tree explains 2×, the load 5×. It is the one case that fails alone — alone on a loaded machine.

**What the bound protects, decided.** Its remark says it: "an outline that takes a minute would eat a row's five-minute
budget" (`:42`). Two different guarantees hide in that: (i) the product's caps hold on a real tree — the outline never reads
more than `MaxOutlinedFiles` or more than the section budget, whatever the tree's size; (ii) the tree-scaling part is
bounded. (i) is a COUNT, pinned without a clock; (ii) is a cost the product does not cap today, and a wall bound on a
loaded machine is not a measurement of it. **The honest fix:** the test keeps its measurement line (it is the point of the
test) and asserts (i) directly — `outline.Outlined <= FeatureBudget.MaxOutlinedFiles`, `outline.Section.Length <=
FeatureBudget.OutlineBytes` — plus a FIXED-FIXTURE test (a repository the test writes: `MaxOutlinedFiles + 50` small
files) asserting the same counts, which no tree growth can move; the wall bound at `:42` goes, and the PR says why: it
measured the machine. For (ii) the story prints the two parts separately (a per-phase measurement: list+size vs
read+outline) so the next reader knows which part a tree of 10,000 files would move; if the tree-scaling part is a per-file
process launch, that is a product cost worth a follow-up plan, named in the PR — not a longer bound. (§8 Q3.)

**RED.** The fixture test with the file cap broken (`MaxOutlinedFiles` ignored in `ReadPlan.cs:76`) → `Expected
outline.Outlined to be less than or equal to 400, but found 450.` — every run, no clock. The this-checkout test is green
today on the counts; its wall bound's removal is the deviation, said.

**Break-its.** B-E5-28: the cap ignored (above). B-E5-29: the section budget ignored → the bytes assertion red on the
fixture.

### 3.19 C28 — PowerShell's `ModuleAnalysisCache` leaking into `src_vs_code` from a child started without `LOCALAPPDATA` — S12

**Evidence.** The lead's report (E4 judge round): a `ModuleAnalysisCache` file appears inside `src_vs_code` after local
runs; no CI failure, no test message — the symptom is a stray file.

**The test that does it — FOUND, VERIFIED by reading.** Two parse tests spawn a real PowerShell with a THREE-variable
environment and no `cwd`: `src_vs_code/src/test/kimiWindowsInstall.test.ts:55-56` (`env = { PATH, SystemRoot, COAI_PS_TEXT
}`, `spawnSync(SHELL, [...], { env, … })`) and `src_vs_code/src/test/vibeWindowsInstall.test.ts:52-53` (the same shape). With
no `cwd` the child's working folder is the `node --test` worker's — `src_vs_code`, where `npm test` runs — and with no
`LOCALAPPDATA` PowerShell's module-analysis cache has no home and lands in the working folder (the lead's mechanism; the
RED below is also the diagnosis of it). Their siblings do carry it: `kimiWindowsInstallRuns.test.ts:69-80` (`minimalEnv()`,
`LOCALAPPDATA` at `:75`) and `vibeWindowsInstallRuns.test.ts:103-112` (`:109`) — two copies of one helper (lesson 6). The
SHELL probes (`kimiWindowsInstall.test.ts:43-45`, `vibeWindowsInstall.test.ts:40-42`) inherit the full environment and are
not the leak. Class: SHARED STATE (a file written outside the run's temp folder — C10–C12's family).

**Fix.** ONE test-support helper, `src_vs_code/src/test/powerShellEnv.ts`: the minimal PowerShell environment (`PATH`,
`SystemRoot`, `PATHEXT`, `LOCALAPPDATA`, `ProgramFiles`, `ProgramData`, `ALLUSERSPROFILE`, `TEMP`/`TMP` — the run's folder
under D2) PLUS `PSModuleAnalysisCachePath` pointed INTO the run's temp folder (PowerShell 5.1 and 7 both honour it), so the
cache never lands anywhere a run does not remove even if `LOCALAPPDATA` is empty; the four files take it (the two
`minimalEnv()` copies deleted). Every PowerShell spawn in the test tree also passes `cwd: tmpdir()`.

**RED (Windows).** `aPowerShellStartedByATest_leavesNoModuleAnalysisCacheInItsWorkingFolder` (`src/test/powerShellEnv.test.ts`):
spawn `powershell.exe -NoProfile -NonInteractive -Command "Get-Command Get-ChildItem | Out-Null"` with today's three-variable
environment and `cwd` = a fresh temp folder; assert no `ModuleAnalysisCache` under it. Today: `a ModuleAnalysisCache landed
in <cwd>` — if it does on the coder's machine, the mechanism is VERIFIED; if it stays green, the mechanism is something else
and the story STOPS (stop rule) and reports what the stray file's timestamps say. After: green with `powerShellEnv()`. A
scan test, `everyPowerShellSpawnInTheTests_usesThePowerShellEnv`, over `src/test/**/*.ts`: a `spawnSync`/`spawn` whose
first argument names `powershell`/`pwsh` without `env: powerShellEnv(` → today names the two parse tests and the two
`minimalEnv` sites; green after.

**Break-its.** B-E5-30: `PSModuleAnalysisCachePath` dropped from the helper → the RED red again (where the mechanism holds).
B-E5-31: one spawn given a hand-built `env` → the scan red.

### 3.20 C32, C37, C38 — three cases with no kept message: diagnosis only — S13 (C37 inside S8's loop)

**C32 — `QuestionConsultScenarioTests.AQuestionIsAnsweredByARealChild_FencedAndRecorded`** (`src_mcp/tests/QuestionConsultScenarioTests.cs:131-157`,
`fakecli-env`, `:29`). Failed in E4 S2's whole-suite run (status `failed`); 6 of 6 alone. A real fake-CLI child on the
MINIMAL environment reading `fakecli-minimal.json` beside its binary (E1). **INFERRED: nothing yet.** The test already passes
`reply.ToString()` as the reason of its first assertion (`:139`, the shape of `:124`), so the row's `reason` IS in the
message when it fails — it was not copied into E4's status. **Step:** 20 runs under the load window, the full assertion
message kept. **Stop rule:** a reason naming the child's exit/stderr → a coder story on what it names (a cold start against
the row's deadline would be C5's shape through `QuestionFanOut`'s own timer — then a clock seam on THAT class, under D3,
in a follow-up story, not here); 0 of 20 → watched.

**C37 — `ASpentSourceBudget_MakesTheNextTurnFinal_WhateverTheCapSays`** (`AReviewerThatAsksForSourceIsAskedAgainTests.cs:210-222`).
Named in an E4 whole-suite run, message not kept; two real `FakeCli` turns under the class's default 1-minute timeout, so
its exposure is the children's starts and the per-turn steering through process-wide `FAKECLI_TURN*` variables (`:103-110`)
inside `fakecli-env`. **Step:** S8's 20-run loop of the class (it runs anyway for C30) keeps this test's message. **Stop
rule:** the message names a turn's prompt or outfile mismatch → a steering-collision reading (E1's family) and a story; a
timeout → the test moves onto `ClockedLauncher` in S8 with the others; 0 of 20 → watched.

**C38 — `ASourceRequestIsServedOrRefusedTests.Supporting_material_cannot_displace_production_from_all_sixteen_source_slots`**
(`src_mcp/tests/ASourceRequestIsServedOrRefusedTests.cs:130-153`). Named in an E4 whole-suite run, message not kept. It reads
seventeen files through a real git checkout (`:141-144`, `Resolver(head:)` at `:150`) under `SecuritySources.ReadAsync`'s
30 s `CollectionBudget` (`src/Server/SecuritySources.cs:14`, `CancelAfter` at `:32`; `MaxSourceFiles` 16 at `:12`). **Step:**
20 runs under the load window, message kept. **Stop rule:** the message names the collection deadline (`…Source read
deadline reached…` or the collection's sentence) → C8's plan owns it (its S21 on the manual clock) and this epic records the
hand-over; a different message → read and filed; 0 of 20 → watched.

## 4. The stories

Thirteen stories. Each has Goal, Files (the complete set — two stories never share a file), RED(s) with their expected
messages (by reference to §3), break-its, Done-when, Roles. **Parallel groups:** A (C#, `src_mcp`) — S2, S3, S4, S5, S6,
S8, S9, S10, S11, all at once; B (the extension) — S1, S7, S12, all at once and beside A; S13 (the scout's loops) runs
beside both but OWNS the load window (§6) while a loop runs. The only ordering inside the groups: S13's C27 follow-up (if
any) after S3; S8's loop before S8's move.

**Why no cornerstone.** A cornerstone is where a mistake compounds and the next step does not catch it. The two places that
come closest both have a net: S8's `ReviewerExecutor` seam is the E4 contract applied to one more class, and the E4 scan
(with its M1 seam-shape rules and its G2 companion) turns red on a `Stopwatch` left beside the clock or an unclassified
constructor; S2's `LinuxHeldFile` flag order is three lines whose wrong order fails L_N1b on the next Linux run. Everything
else is one test file with its own RED. So: none named; the two places above get an Opus brief with a required table
(S8: the per-class inventory; S2: the descriptor's flag state at each line of the take, success and failure paths).

**Roles, for every story unless it says otherwise:** Opus writes the brief from this document (the files, the RED text, the
break-its, the exposure sentence) and reviews the PR; Sonnet codes; S13 is a Sonnet scout with an Opus reading of each kept
message. Fable: none (no cornerstone).

### S1 — C22, the real-editor harness (group B; its own independently accepted PR)

- **Goal.** §3.1 (1)–(4): the failure names an editor that exited before the host ran, the download leaves the launch budget,
  one retry of the launch on the host-started signal only, a `::warning::` annotation counted by the streak step.
- **Files.** `src_vs_code/scripts/run-host.mjs`, `src_vs_code/src/test/host/scenarios.ts` (the `host started: N scenarios` line
  at `:981`), `src_vs_code/scripts/host-job-streak.mjs`, new `src_vs_code/scripts/run-host.test.mjs` (and a fixture stub
  editor script under `src_vs_code/scripts/fixtures/`). No product code, no C#.
- **RED.** S23, both arms (§3.1). **Break-its.** B-E5-1..3.
- **Done when.** S23 (a) red with the three messages then green; (b) green before and after and broken once; the job's
  budgets unchanged; `host-job-streak.mjs` prints `… first-launch warnings in the last N runs of main: k`; the PR says why
  the new sentence is at least as strong as the old (it names the exit, the line and the retry; the old named a code).
  Accepted on its own evidence — never waits for or blocks another story; its job is `continue-on-error`, so its merge is
  on the harness's reviewers.

### S2 — the Linux process-lifecycle group: C21 → C29 → C24 (the torn pid write; the `R` arm gated) → C26 (group A)

- **Goal.** One zombie-making helper that kills only once the parent is `sleep` and polls `/proc` through `ProcStat`; the
  fake's pid files written whole and waited for by content; a `/proc` state report that never throws; the test class's
  failures carrying the whole report; and the inheritable descriptor that cannot be caught by a fork on a failure path.
- **Files.** `src_mcp/tests/PidfdLayerTests.cs` (`:473-512`), `src_mcp/tests/AZombieIsNotAliveTests.cs` (`:24-70`), new
  `src_mcp/tests/ZombieMaker.cs`; `src_mcp/tests_fakecli/SupervisionVerbs.cs` (`:43`, `:130-133`, `:180-189`, `:204`),
  `src_mcp/tests/SupervisedTeardownLinuxTests.cs` (`:93-105`, the reasons on `:128-130`, `:143-145`, `:157-159`, `:169-173`,
  `:183-186`), new `src_mcp/tests/EveryPidFileTheFakeWritesIsWrittenWholeTests.cs`; `src_mcp/runners/Files/LinuxHeldFile.cs`
  (`:22`, `:23-34`, `:40-49`, `:52-65`, `:83-90`), `src_mcp/tests/HeldFileInheritableOnLinuxTests.cs` (a new Fact beside
  `:117`). `tests_fakecli/Serving.cs` is CALLED, not changed.
- **REDs.** S22 and C29's (§3.2, §3.3 — the bash arm, two sentences); C24's scan and unit (§3.4); C26's four arms (§3.5:
  the post-exec fork, the PRE-exec child on a pipe barrier, the failing `fcntl`, the inheritance guards).
- **Break-its.** B-E5-4..9, B-E5-9a, B-E5-9b.
- **Done when.** The reds seen then green on the Linux leg (the coder runs them in CI on the story branch — these classes
  skip elsewhere); the two shells and the second parser gone; `NoTestHoldsAnInheritableHandleTests` still green; C24's `R`
  arm recorded as GATED with (5) in place and S13's loop result attached; the PR states the `LinuxHeldFile` descriptor
  contract as a table — open `O_CLOEXEC` → flock → stream → clear `FD_CLOEXEC` (checked) on success; `LOCK_UN` → close on
  the aborted path; NO unlock on successful disposal — with the descriptor's and the lock's state at each line, success and
  failure paths, and names `strace -ff -tt -yy -e trace=process,openat,flock,fcntl,close` as the tool for a kept CI run.
- **Roles.** Opus brief with the flag-state table required; Sonnet; Opus review. One coder, in that order (all three read
  `/proc` through `ProcStat` and the fake's supervision verbs).

### S3 — C16, the HTTP stub (group A)

- **Goal.** §3.6: close admission → abort → drain with a bound → stop; the cancellation-aware handler contract; nothing swallowed.
- **Files.** `src_mcp/tests/ApiEndpointStub.cs` (`:102-125`, `:127-189`, `:219-229`), new `src_mcp/tests/ApiEndpointStubTests.cs`.
  The fifty callers compile unchanged (the overload rule). `AUsageTheVendorNeverReportedIsUnknownTests.cs:40-51` is NOT
  changed — the stub's `Dispose` now does the right thing for exactly that `Dispose` order.
- **REDs.** S11 (a) and (b) (§3.6). **Break-its.** B-E5-10..12.
- **Done when.** Both arms red then green; the §6 loop of `AUsageTheVendorNeverReportedIsUnknownTests` recorded before and
  after; every stub-using class green on all three OSes; the PR states the exposure (single-threaded stub) and the handler
  contract in two sentences; a whole-suite run's teardown time for the stub-using classes is not longer than before (the
  synchronous cancel-and-abort means a handler blocked on its token or response never costs the 2 s bound — measured once
  in the PR).

### S4 — C3, the Kimi shim's probe (group A)

- **Goal.** §3.7: the probe released by the shim's first busy answer through `WhenTheHomeFileIsBusy` on the call site;
  `HeldFile` untouched; the inheritable take kept.
- **Files.** `src_mcp/runners/Kimi/KimiCredentialsLock.cs` (`:114` neighbourhood, `:206-215`), `src_mcp/tests_fakecli/TreeHoldVerb.cs`
  (`:33-59`), `src_mcp/tests/KimiCredentialsLockTests.cs` (`:315-329` and two new Facts).
- **REDs.** S17's seam unit (§3.7); the loop for the race. **Break-its.** B-E5-13, B-E5-14.
- **Done when.** The seam unit red then green; the 100-run loop before (the count) and after (0) recorded under the load
  window; `tree-probed-forever` green before and after; `EveryBudgetInAClockedClassReadsItsClockTests` untouched (no clock
  here); the product's default path proven unchanged by the existing `KimiCredentialsLockTests` all green.

### S5 — C18's timing half (group A)

- **Goal.** §3.8: beats seen through `projected`, the row released after three distinct ones.
- **Files.** `src_mcp/tests/QuestionFanOutTests.cs` (`:690-710`), `src_mcp/tests/ScriptedLauncher.cs` (`:117` — `Gate`
  beside `Wait`, and the await in the launcher). No product code (`QuestionFanOut.cs:302-309` is the subject, unchanged).
- **RED.** S18 by break-it (§3.8, lesson 5). **Break-its.** B-E5-15, B-E5-16.
- **Done when.** Green on today's product, red under B-E5-15 with the guard's sentence, green again; the §6 loop recorded;
  no `Task.Delay` or poll left in the test; the PR says the new assertion is stronger (three DISTINCT beats observed at the
  write, not inferred from a poll).

### S6 — C15, the follow-up launch's timeout (group A)

- **Goal.** §3.9 (a) and (b): the recorded timeout asserted; the smoke test kept with its exposure stated.
- **Files.** `src_mcp/tests/ConsultAntigravityDenialScenarioTests.cs` (`:368-397`), `src_mcp/tests/ConsultScenarioBase.cs`
  (`:41`, the `TurnLauncher` record widened), `src_mcp/tests/ConsultantTurnTests.cs` (`:90` cross-referenced, unchanged).
  `ConsultantTurn.cs:107` is the break-it site only.
- **RED.** S19 (§3.9). **Break-its.** B-E5-17, B-E5-18.
- **Done when.** S19 red then green; `:386-387` gone; the smoke test's remark carries the exposure sentence; the §6 loop
  recorded; the PR says why the new assertion is at least as strong (it names `≤ 8 s − elapsed` where the old allowed 3 s of
  slack).

### S7 — C9, the extension's `replayingFan` (group B)

- **Goal.** §3.12: the CPU bound on the fan; the smoke test without a clock.
- **Files.** `src_vs_code/src/processLauncher.ts` (`:194` exported for tests), `src_vs_code/src/test/processLauncher.test.ts`
  (`:22-34`, `:250-269`).
- **RED.** S20 (§3.12). **Break-its.** B-E5-19, B-E5-20.
- **Done when.** S20 red (`held = [...held, value]`) then green; the smoke test keeps exit 0 and every character and has no
  timer; `npm test` green; the PR states the margin numerically (N = 40,000: quadratic ≈ 6.1 s, linear ≈ 3 ms, threshold
  1,000 ms — ×6 below, ×300 above) with the measured CPU figures for both shapes on the coder's machine, and the isolation
  (own process per test file; a warm-up and a yield before the window).

### S8 — the budget tests on the in-process launcher: C30, C35 (+ the `ReviewerExecutor` clock), C37's loop (group A)

- **Goal.** §3.13, §3.14: `ClockedLauncher` extracted and widened; the two turn-timeout tests and the repair-budget tests
  moved onto it with `ManualClock`; `ReviewerExecutor` reads one clock, landed with its RED and its scan entry; the class's
  20-run loop keeps C30's and C37's messages.
- **Files.** `src_mcp/tests/AReviewerThatAsksForSourceIsAskedAgainTests.cs` (`:342-385`; `:609-680` moved out), new
  `src_mcp/tests/ClockedLauncher.cs`, `src_mcp/tests/RepairRunsInsideTheBudgetTests.cs` (`:28`, `:35-70`),
  `src_mcp/runners/Reviewers/ReviewerExecutor.cs` (`:414` constructor, `:523`, `:601`, `:609`, `:627`, `:640`, `:652`),
  `src_mcp/tests/EveryBudgetInAClockedClassReadsItsClockTests.cs` (`:35`, one line).
- **Order inside the story.** (1) the loop (C30/C37 messages kept — D5); (2) `ClockedLauncher` extracted, callers moved,
  suite green; (3) the scan entry RED; (4) the seam alone, suite green on the default clock; (5) C35's test moved, its RED
  seen on the unseamed executor first (the coder reverts the seam once to watch it — lesson 5); (6) C30's tests moved.
- **REDs.** §3.13's by absence and S4's break-its; §3.14's `found 2s` and the scan's named site. **Break-its.** B-E5-21..23.
- **Done when.** The inventory table for `ReviewerExecutor` in the PR (E4 §5.1 form: every clock read, what each reads
  after; the launcher's `CancelAfter` named as "stays, the launcher's"; `AbandonBudget` named as "stays — TEARDOWN, real
  cleanup I/O", with its one allow-list entry and its frozen-clock test); the scan and its companion green with the file in
  `Clocked`; no `Stopwatch`, `DateTime.UtcNow`, bare `Task.Delay` or `WaitAsync(timeout)` left in the class outside that
  entry; the one clock instance passed explicitly to scheduler, executor and driver, and the missing-clock RED seen; the
  2 s and the 1.5 s/2 s budgets are INPUTS on the manual clock, nothing lengthened; the PR says the stand-in proves
  ALLOCATION and names `ProcessLauncherTests.cs:119-136` as the enforcement proof; C37's kept message filed in §3.20 by the
  lead.
- **Roles.** Opus brief with the inventory table required; Sonnet; Opus review.

### S9 — C31, the sizing deadline (group A)

- **Goal.** §3.15: the order of two events on the stand-in's stopwatch replaces the 3 s wall bound, in both tests.
- **Files.** `src_mcp/tests/TheSizingIsConcurrentAndBoundedTests.cs` (`:116-142`, `:169-205`).
- **RED.** By break-it (§3.15). **Break-its.** B-E5-24, B-E5-25.
- **Done when.** Both tests green on today's product, red under B-E5-24 with the named sentence, green again; no wall bound
  left in the file; the §6 loop recorded.

### S10 — C33, the initialize round trip (group A)

- **Goal.** §3.16: the vault read's end marker orders the proof; the reply wait is the suite's guard with a hang's sentence.
- **Files.** `src_mcp/tests/TheServerAnswersInitializeAtOnceTests.cs` (`:44-77`), `src_mcp/tests_fakecli/Program.cs` (vendor
  mode: `FAKECLI_ANSWER_GATE`, the `FAKECLI_RECORD_GATE` wait reused — `:57`, `:174`, `:678`), `src_mcp/tests/StdioServer.cs`
  (`:47-55` — only if a guard overload is needed; the default 30 s at `:47` is not touched).
- **RED.** By break-it (§3.16). **Break-its.** B-E5-26, B-E5-27.
- **Done when.** Green today, red under B-E5-26 with its sentence, green again; `:63` and the 20 s sleep gone; the gate
  released in a `finally`; the §6 loop recorded; the PR says the 10 s was the measurement and the gate's order replaced it
  (no guard lengthened: the suite's 60 s names a hang).

### S11 — C36, the outline measurement (group A)

- **Goal.** §3.18: the caps asserted by count on this checkout and on a fixed fixture; the measurement kept and split by
  phase; the wall bound gone with its reason.
- **Files.** `src_mcp/tests/QuestionOutlineTimingTests.cs` (`:22-43`), new `src_mcp/tests/FeatureOutlineCapsTests.cs` (the
  fixture). `FeatureOutlineBuilder.cs` is read for the phase split (a test-visible timing of its two phases may need an
  `internal` hook — if so, named in the PR as the one product line of this story and reviewed as such).
- **RED.** The fixture's cap RED (§3.18). **Break-its.** B-E5-28, B-E5-29.
- **Done when.** The fixture test red with the cap ignored then green; the this-checkout test green on counts with its
  `[measurement]` line now naming both phases; `:42` gone and the PR's deviation paragraph says what the bound measured
  (the machine) and what now pins the row's budget (the caps); the follow-up on the tree-scaling phase named if it is a
  per-file launch.

### S12 — C28, the PowerShell cache (group B)

- **Goal.** §3.19: one `powerShellEnv()` with `PSModuleAnalysisCachePath` in the run's temp; four files on it; the scan.
- **Files.** new `src_vs_code/src/test/powerShellEnv.ts`, new `src_vs_code/src/test/powerShellEnv.test.ts`,
  `src_vs_code/src/test/kimiWindowsInstall.test.ts` (`:55-56`), `src_vs_code/src/test/vibeWindowsInstall.test.ts` (`:52-53`),
  `src_vs_code/src/test/kimiWindowsInstallRuns.test.ts` (`:69-80`, `:94`, `:130-139`), `src_vs_code/src/test/vibeWindowsInstallRuns.test.ts`
  (`:100`, `:103-112`, `:136-147`).
- **RED.** §3.19's (Windows); the scan naming four sites. **Break-its.** B-E5-30, B-E5-31. **Stop rule.** The RED green on
  today's environment → the story stops and reports (§3.19).
- **Done when.** The RED red then green on a Windows machine; the scan green; a full `npm test` leaves no
  `ModuleAnalysisCache` under `src_vs_code` (checked by `git status --ignored` before and after — the file is not even in
  `.gitignore`, so today it shows as untracked); the two `minimalEnv` copies gone.

### S13 — the diagnosis loops (a scout; group-independent; owns the load window while a loop runs)

- **Goal.** For C27, C7, C34, C32, C38 (and C24's `R` arm once S2's (5) is in): 20 runs of the class under ONE named load,
  every failure's full message kept and filed into §3 by the lead, each stop rule applied. Nothing is fixed in this story.
- **Files.** None changed. Output: one `research/RESULTS_suite_e5_diagnosis_loops.md` (the loads, the counts, the messages
  verbatim) — the only new document; written by the scout, filed by the lead.
- **Done when.** Each of the six has a row: load, runs, failures, the message(s), the stop rule's outcome (a follow-up story
  opened — C27 after S3; C7 sized; C34 per §8 Q4; C32/C38 read and filed or handed to C8's plan — or "not reproduced;
  watched").
- **Roles.** Sonnet scout; Opus reads each message against the code before it is filed.

## 5. Build order — and what runs beside what

1. **The loops first where a story needs a kept message before it moves:** S8's loop (C30, C37) and S13's loops run in the
   load window (§6) as the first thing on the epic branch; S13 continues through the epic. Everything else starts at once.
2. **The parallel groups stand** — group A (S2, S3, S4, S5, S6, S8, S9, S10, S11) and group B (S1, S7, S12) share no file
   (§4 lists each story's complete set; the one near-miss, `tests_fakecli/Program.cs`, is S10's alone — S2 edits
   `SupervisionVerbs.cs`, S4 `TreeHoldVerb.cs`). Each story is its own branch off `feat/suite-e5` (itself off
   `feat/suite-e4-clock`, E4's PR #916 — rebased onto `main` when #916 merges) and merges into it.
   **BUT the merges land in four BUILD GROUPS, each closed by a checkpoint code round (`review_code` with `again: true`)
   before the next group's merges begin.** This is the answer to the gate's operator command "split into 4-5 epics": E5 is
   epic 5 of a FIXED five-epic plan, so no epic is added; the size heuristic is met by reviewing the branch at four
   checkpoints instead of once at the end. The groups, in order:
   - **BG1 — the product-touching C# stories: S2 (`LinuxHeldFile`), S8 (the executor's clock, `ClockedLauncher`), S3 (the
     stub every API test shares).** Highest risk, so first and reviewed first; S8's loop (C30/C37) runs at the start of BG1.
     Checkpoint round 1 over their merged diff.
   - **BG2 — the test-only C# stories: S4, S5, S6, S9, S10, S11.** Coded in parallel with BG1 (different files), merged
     after checkpoint 1 is `proceed`. Checkpoint round 2.
   - **BG3 — the extension: S1, S7, S12.** Coded in parallel with BG1/BG2 (a different tree), merged after checkpoint 2
     (or beside it — the harness story S1 is accepted on its own PR per the parent, and that stands). Checkpoint round 3.
   - **BG4 — the diagnosis follow-ups: S13's results file and whatever stories its stop rules opened** (C27's after S3 is
     in BG1; C7, C34, C32, C38 as decided). Checkpoint round 4 is the epic's final code gate, followed by the five-epic
     feature gate (§9).
3. **S13's loops** run from the start (the load window, §6) and through BG1–BG3; its follow-ups are BG4.
4. **The docs pass** — one commit after the last story, in E4 §9's order: `module_tests.md` (one section per story in
   story order, the E5 lessons, the diagnosis results linked), `module_runners.md` (the executor's clock; `LinuxHeldFile`'s
   flag order; the Kimi call-site seam), the diagrams where a clock or a descriptor appears, the parent's E5 section and
   `todo/README.md`, the tails note (`PLAN_question_consultant_tails.md` §6 — C15's half).
5. **The epic's code gate, then the feature gate of the whole five-epic plan** (§9), then the 20-run CI watch per leg, then
   promotion of the parent, the E4 document and this one together.

## 6. Test plan

```bash
# build — always before a run; the exe is only as fresh as the last build
dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4

# the C# suite — the executable, NEVER `dotnet test` (xUnit v3 on Microsoft Testing Platform: no VSTest testhost)
node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*ApiEndpointStubTests"
node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*KimiCredentialsLockTests"
node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*AReviewerThatAsksForSourceIsAskedAgainTests"
node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*EveryBudgetInAClockedClassReadsItsClockTests"
node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe   # the whole suite

# the extension — the runner caps node --test at 8 files and sets the per-run temp (D2)
cd src_vs_code && npm test
# one extension test file, or the harness's own test
node --test src_vs_code/src/test/processLauncher.test.ts          # through the runner's build: `npm test -- --filter processLauncher` if the runner offers it; else the whole
node --test src_vs_code/scripts/run-host.test.mjs
# the Linux-only classes (S2) run on the Linux CI leg of the story branch; locally they skip — the coder pushes to see them

# the conventions tools' own tests and the plan check
node --test .agents/conventions/tools/
node .agents/conventions/tools/plan-lifecycle.mjs
```

- **Per scenario:** RED with the §3 message, then GREEN; every break-it red then green; all three observations in the PR.
  Where the product is right today (S5, S9, S10), the PR says so and shows the break-it's red instead.
- **Per story, the seam-alone run (S8 only):** the whole suite green on the default clock with the seam in and the tests not
  yet moved, "green except named timing cases, each green alone and filed" — E4's L1 wording, with the names.
- **Per timing case, the loop:** the case's class 20 times in a row (100 for C3 — its CI rate is low) under the load window,
  before the fix (record the count and keep every message) and after (0 of N). A run that still fails after is a reopened
  case, never a rerun.
- **The load window — how to run a load reproduction without three suites colliding.** E4's loops were run with up to
  three suites on one machine, and the results could not say which load produced which failure. E5 names the load: a loop
  runs with EXACTLY ONE whole `src_mcp` run beside it (started through `low-priority.mjs` in another worktree), nothing else
  — before starting, the runner checks that no other `CoaiMcp.Tests.exe`, `node --test` or `dotnet build` of a sibling
  session is alive (`tasklist`/`ps`, named in the results file), and announces the window to the other sessions in the
  usual place; two whole runs from two worktrees at once are used ONLY for the parent's shared-state proof (E1/E2's DoD),
  never during a loop. Each loop's row records: the class, N, the load ("1 whole src_mcp beside", "alone"), the failures and
  their messages verbatim. S13 owns the window while its loops run; S8's and S4's loops take it in turn.
- **CI:** after the epic merges, the parent's census over the next 20 runs of each leg — a watch, not the proof; a failure
  inside the watch reopens the case with its kept log. C22's watch is the streak step's annotation count over the next 20
  runs of `main`.
- **The counts:** every suite green on Windows, Linux and macOS before and after; no test removed or skipped; the count
  grows by exactly the named scenarios, per story.

## 7. Risks

- **C26's three lines are in a product primitive.** A wrong order (clear `FD_CLOEXEC` before the flock, or never) silently
  breaks or over-broadens the tree hold. Caught by: the flag-state table in the brief, L_N1b and the new RED in the same
  class, `L_N1a_ProcLocks_NamesTheKindOfFlockTheInheritableHoldTook` (the kernel's own table). Residual: none known; §8 Q1.
- **S8 seams a class E4 did not inventory.** A missed read stays on the system clock while the moved tests go green — the
  E4 risk, on a fifth class. Caught by: the M1 scan rules and the G2 companion (both already on the branch), the required
  inventory table, and B-E5-21/22.
- **The widened `ClockedLauncher` is used by four tests across two files** (after extraction) — a change for C30 can move
  S4's cap tests. Caught by: the cap tests' own reds (S4's B11–B13) re-run in S8's PR.
- **The stub rewrite touches fifty tests' fixture.** Caught by: the overload rule (callers compile unchanged) and the whole
  suite on three OSes in S3's PR; the single-threaded serving loop is kept as it is.
- **A diagnosis loop that reproduces nothing** leaves a case open on a watch. That is the plan working (lesson 4): the
  result is recorded as "not reproduced under one named load", and the 20-run watch decides; no fix is invented.
- **E4's PR #916 is not merged.** E5's branch stacks on it; if #916 changes under review (its code gate accepted G1–G4
  already), E5 rebases. Nothing in E5 edits a file E4 changed except the scan's `Clocked` line (S8) — one line, by design.
- **The load window asks other sessions to stand down** for the length of a loop (minutes to an hour). Announced; the
  alternative — loops under an unknown load — is what produced E4's unreadable counts.
- **C36's phase split may name a product cost** (a per-file launch in the listing phase). That is a finding, filed as a
  follow-up plan, not a change this epic makes.

## 8. Questions for the lead — each with the assumption the story proceeds on

- **Q1 — C26's fix in `LinuxHeldFile` (open `O_CLOEXEC`, flock, stream, then clear `FD_CLOEXEC`) instead of the parent's
  two options.** A serial collection cannot stop another collection's fork under `maxParallelThreads: 8`, and "the probe
  waits for inheritors" is a timer. *Assumed: yes — three lines in the primitive, with the flag-state table and L_N1b as
  the guards.*
- **Q2 — a fifth clock seam, `ReviewerExecutor` (C35), in E5 under D3.** One class, with its RED, scan entry red first,
  inventory table in the brief. *Assumed: yes.* The alternative — keep two real 1.5 s launches and only drop the 3.5 s
  bound — leaves "the repair gets what is LEFT" observable only by a real clock, i.e. the race.
- **Q3 — C36: the wall bound at `:42` goes; the caps are asserted by count on this checkout and on a fixed fixture; the
  measurement is kept and split by phase.** *Assumed: yes.* If the lead wants a time bound somewhere, the only honest home
  is the FIXTURE (a fixed size), and even there as CPU, not wall — say so and S11 adds it.
- **Q4 — C34's "still ending" arm, once the loop confirms it:** keep it as a real-process smoke test of the launcher's
  100 ms probe with the exposure sentence (recommended — E4 §5.5 and Q1 keep `EndedProbe` on the system clock), or open
  C4's gate for a seam on the probe now that a failure is kept. *Assumed: keep with the sentence.*
- **Q5 — C38's hand-over.** If its kept message names the collection deadline, C8's plan owns it and E5 records only the
  hand-over. *Assumed: yes.*
- **Q6 — the `ClockedLauncher` widening** (a per-launch script beside the one-turn constructor) rather than a second
  stand-in. *Assumed: widen — the reuse rule's step 2.1.*
- **Q7 — the load window.** May S13 (and S4's/S8's loops) reserve the machine for the length of a loop, announced, with the
  check that no sibling suite is alive? *Assumed: yes.*
- **Q8 — the feature gate.** The parent's five-epic plan ends with a feature gate (`review_feature`) over the whole feature
  after E5's code gate — run by the session that lands E5's docs pass, with the parent + E4 + E5 documents as the plan
  text. *Assumed: yes; the lead says if a narrower scope (E5 alone) is wanted.*
- **Q9 — C7's closure.** If the loop shows 0 of 20 and the slow step measures under 2 s at worst, close with the recorded
  measurement (the parent's DoD allows it). *Assumed: yes.*

## 9. Definition of Done

- [ ] **Every case of §3 closed** by its deterministic RED turned green (S1, S2, S3, S4's seam unit, S6, S7, S8, S11, S12)
      or its break-it watched red then green where the product was already right (S5, S9, S10), or its recorded loop
      before/after where no deterministic red exists (C3; C24's `R` arm and C37 if their loops never reproduce) — and
      every INFERRED case either VERIFIED by a kept message or recorded "not reproduced under one named load; watched"
      (S13's results file), with no fix landed on an inference.
- [ ] **The parent's DoD items E5 owns, closed:** C3, C9, C15, C18, C21 wait for an event or measure CPU, their loops
      recorded; C22 by S23 red → green plus the streak step's annotation count (its exception from the loop kept); C7 fixed
      from a kept failure or closed with the recorded measurement; C24 and C26 fixed from their gates — C24's torn write
      from the kept exit-97 message, C26 from the fork RED — never "fixed" without them; C27 from a kept journal or closed
      with its loop; C16 with S11 both arms; C28–C38 as §3 says; C15's and C16's PRs state the exposure and the contract;
      C9's, C15's and C22's PRs say why the new assertion is at least as strong as the old.
- [ ] **No timeout lengthened, no assertion weakened, no test removed or skipped** — said per case in its PR; every wall
      bound that went names what replaced it (an event, a count, a recorded number); the one real sleep kept (S3's arm (a),
      the writer's own progress) is named as such.
- [ ] **The fifth clock seam** (`ReviewerExecutor`) obeys the E4 contract: the scan and its companion green with the file
      in `Clocked`, the inventory table in the PR, the seam-alone run recorded, no product hook calls `Advance`.
- [ ] **The reuse rule's three duplicates gone** (two zombie shells and the second `/proc` parser; the private
      `ClockedLauncher`; the two `minimalEnv` copies), each replaced by one shared piece with its own test.
- [ ] **The suites green on Windows, Linux and macOS** before and after each story; the count grows by exactly the named
      scenarios; the Linux-only classes seen green on the Linux leg of each story branch.
- [ ] **The loops recorded per timing case** (before and after, under the named load) and the diagnosis results file in
      `research/`; the load window honoured (no loop ran beside an unknown load).
- [ ] **The docs pass** done in one commit after the last story (§5 item 4): `module_tests.md`, `module_runners.md`, the
      diagrams, the parent's E5 section, `todo/README.md`, the tails note.
- [ ] **Four checkpoint code rounds `proceed`** (`review_code again: true` after BG1, BG2, BG3 and BG4 — §5), each one's
      findings folded here (a section like E4's §18), the fourth being the epic's final code gate; then **the feature gate
      of the whole five-epic plan** run and folded (§8 Q8), then **the 20-run CI watch per leg started** — a failure inside
      it reopens its case with its kept log.
- [ ] **The plan round's six findings and the two risk consults honoured as built (§10, §11):** `LinuxHeldFile` with checked
      `fcntl`, `LOCK_UN` on the aborted path only, the pre-exec-child arm; `PidWritten` tolerant of a transient empty file;
      the stub's synchronous cancel-and-abort before the drain; C9's margin stated numerically with isolation; the
      executor's one explicit clock across scheduler/executor/driver with the missing-clock RED, both duration records on
      it, `AbandonBudget` on the system clock with its allow-list entry and frozen-clock test; C33 on a gate, no sleep.
- [ ] `node .agents/conventions/tools/plan-lifecycle.mjs` clean; promotion of the parent, the E4 document and this one
      together per `.agents/conventions/common/planning-docs.md`, the parent's "OPEN shared-state investigation" note closed
      with a link, and the `Cornerstones: 2 of 2 named` line unchanged.

## 10. The E5 plan round (coai session `51b4e0fd`, 2026-10-22, verdict proceed) — 6 findings, all accepted

Every finding was re-read against the code at `78d91db55` before it was folded; the sections above were edited in place
and this table says where. Numbers are the round's.

| # | Finding | Decision | Folded as / what the code showed |
|---|---|---|---|
| 1, 3 | C26/S2: `O_CLOEXEC` closes at EXEC, not at fork — a child another thread forked holds the descriptor (and the flock on the shared open file description) until it execs; and clearing `FD_CLOEXEC` after a successful take makes the descriptor inheritable by every concurrent fork | **Accepted** (with §11's risk consult `fdd9146f`, which supplies the mechanism) | §3.5 rewritten: open `O_CLOEXEC` → flock → stream → clear `FD_CLOEXEC` with CHECKED `fcntl` on success; `flock(LOCK_UN)` before close on the aborted-acquisition path; NEVER an unlock on successful disposal; production isolation verified (the shim at `AskAcpMode.cs:163`/`:189-191` is the only taker, `KimiCredentialsLock.cs:46-51`); the 7 ms failure marked PLAUSIBLY pre-exec, not established; four RED arms, two new break-its; S2's done-when carries the descriptor-state table and the `strace` tool |
| 2 | §3.16 (C33): a fixed 20 s sleep before the fake vault's end marker can expire before a slow `initialize` reply → a false failure. Use a test-controlled GATE | **Accepted** | §3.16 rewritten: the vault read blocks on `FAKECLI_ANSWER_GATE` (the `FAKECLI_RECORD_GATE` wait shape, `Program.cs:57`/`:174`/`:678`, reused); assert the reply while the gate is closed; release in a `finally`; verify the read completes through `providers`. No 20 s anywhere; the break-it's sentence names the closed gate |
| 4 | §3.4 (C24): `PidWritten` must tolerate a transiently empty or unparseable `late.pid` (retry until its bound, never throw) AND `late-spawn` writes atomically through `Serving.WritePidFile` | **Accepted** | §3.4 fix items (1) and (2): `:84` goes through the atomic writer; `PidWritten` treats `''`/junk as "not yet" on its 20 ms poll until `Patience`, failing only at the bound with what it last read |
| 5 | S3 (`ApiEndpointStub.Dispose`): abort admission and active connections and cancel the handlers' token SYNCHRONOUSLY before waiting on the drain, so a stuck handler does not cost the 2 s bound per teardown | **Accepted** | §3.6 fix step (2) now cancels the `Disposing` token and aborts before any wait; (3) drains only after; S3's done-when measures the stub-using classes' teardown time once in the PR |
| 6 | S7 (`process.cpuUsage() < 1000 ms`): size N and the threshold with an order-of-magnitude margin between the linear and the quadratic shape, isolate from unrelated CPU (a warm-up / GC first), state the margin numerically | **Accepted** | §3.12: from the file's own 64.09 s / 0.01 s at 130,000 (`processLauncher.ts:203-205`) — at N = 40,000 the quadratic fan ≈ 6.1 s, the append ≈ 3 ms; threshold 1,000 ms = ×6 below and ×300 above; own process per test file, 1,000-value warm-up and a yield before the window; S7's done-when asks for both measured figures |

**The operator command on the round ("split into 4-5 epics"):** read as the size heuristic it is; applied as the four build
groups with checkpoint code rounds in §5, not as new epics (E5 is the fixed plan's fifth and last).

## 11. The risk consults (`fdd9146f` for S2 — `LinuxHeldFile`; `c463d7c3` for S8 — the executor's clock), 2026-10-22 — verified, binding

### `fdd9146f` — S2, the inheritable take

1. **Production inheritance exists for ONE child and in ONE process.** Verified: the `--ask-acp` shim takes the two
   inheritable locks (`AskAcpMode.cs:163`, `profile.Hold(ask, ask.Deadline)` → `KimiCredentialsLock.HoldTree`, `:172`) and
   starts Kimi with nothing awaited between (`:35`, `:152`, `AcpClient.AskAsync` at `:191` inside `using (held.Hold)`,
   `:189`), so Kimi keeps the locks if the shim dies (`KimiCredentialsLock.cs:46-51`). The SERVER never owns these
   descriptors; its sibling reviewers and git children cannot inherit them — process isolation, not luck. Folded in §3.5.
2. **The mechanism, in order.** Open `O_CLOEXEC`; take the flock; build the stream; on success clear `FD_CLOEXEC` with
   CHECKED error handling (`fcntl` failure → `Hold.Unusable` naming the errno, after unlock and close); on the ABORTED
   acquisition path `flock(LOCK_UN)` explicitly BEFORE close — the unlock acts on the shared open file description, so a
   pre-exec child that inherited a copy no longer holds the lock; NEVER unlock on normal successful disposal (that would
   release Kimi's inherited lock). Folded in §3.5 as the fix and in S2's descriptor-state table.
3. **The 7 ms C26 failure is PLAUSIBLY a pre-exec child — not established.** The stream-factory RED covers a different
   window (`.NET`'s `Process.Start` waits for the child's exec before returning, so a child started inside the seam has
   exec'd before the probe). Folded: §3.5 arm (b) — an isolated helper forks after the flock and holds the child before
   exec on a pipe barrier, then the stream fails; with `LOCK_UN` the probe reads `Free`, without it `Held`.
4. **Tests before shipping** (all in §3.5 / S2): failure after a completed exec releases (arm a); failure with a pre-exec
   child meets the contract (arm b); an injected `fcntl` failure → `Unusable` and cleanup (arm c); successful inheritance
   still excludes a second taker after the opener dies and releases only after the last inheritor exits (arm d — the
   existing `L_N1a_ASecondInheritableTake_IsBusy…` and `L_N1b…`).
5. **The diagnosis tool for a kept CI run:** `strace -ff -tt -yy -e trace=process,openat,flock,fcntl,close` on the test
   host names the holder. Recorded in §3.5 and S2.

### `c463d7c3` — S8, the executor's clock

1. **One clock instance, explicitly to all three.** Verified: `ConversationAsync` (`AReviewerThatAsksForSourceIsAskedAgainTests.cs:580-596`)
   passes `clock` to the scheduler (`:585`) and nothing to `new ReviewerExecutor(launcher)` (`:586`); after the seam that
   compiles and silently defaults to system time. Folded: the executor keeps its own optional `TimeProvider? clock` for
   standalone callers; tests pass the SAME instance to scheduler, executor and driver; the RED that catches the omission is
   the 500 ms repair request (`found 2s` on a default-clock executor with a time-free stand-in) — §3.14.
2. **The remainder and BOTH launch-duration records move together.** Verified: `:627`, `:640`, `:652` all read `spent`.
   Folded: one stamp, three `GetElapsedTime` reads; break-it B-E5-23b.
3. **`AbandonBudget` STAYS on the system clock as teardown.** Verified: `AbandonAsync` (`ReviewerExecutor.cs:818-835`) arms
   `new CancellationTokenSource(AbandonBudget)` (`:827`; 10 s at `:838`) around the adapter's courtesy cleanup — for the
   remote runtime a real HTTP cancel (`RemoteRuntime.cs:252-260`, client timeout 10 s at `:242`). Folded: E4's teardown
   exception wording extended to "real cleanup I/O"; ONE narrowly justified `Sanctioned` allow-list entry
   (`EveryBudgetInAClockedClassReadsItsClockTests.cs:60`, the `Allowed(File, Snippet, Reason)` shape) for that snippet; the
   frozen-clock test — an adapter awaiting its cleanup token is still cancelled by the system-time bound and the launch's
   original timeout/cancellation outcome survives; break-it B-E5-23a.
4. **The scripted `ClockedLauncher` proves budget ALLOCATION, not production timer enforcement.** Folded: the real
   `ProcessLauncher` deadline tests (`ProcessLauncherTests.cs:119-136`) stay and the PR says so — §3.13, §3.14, S8.
5. **The test list** (1.5 of 2 s → exactly 500 ms; an already-shorter repair stays shorter; an exhausted budget starts no
   repair; the duration records reflect the same advances; a timed-out repair keeps earlier usage and releases the slot;
   the default-clock seam-only run and the real-process timeout tests still pass) — §3.14 "The tests, listed".
