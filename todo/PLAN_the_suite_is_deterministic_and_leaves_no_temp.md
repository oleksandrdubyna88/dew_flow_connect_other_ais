# PLAN — the suite is deterministic and leaves no temp

> Status: **plan only, 2026-10-19.** Nothing implemented yet. Scope: the fake CLI's steering files
> (`src_mcp/tests_fakecli`), the fixed-name temp paths of the C# and extension tests, the flaky cases found in CI and
> locally between 2026-09-26 and 2026-10-19 (product code where the cause is in the product), and the `coai-*` folders
> the suites leave in `%TEMP%`. The owner put this "tail" at the very end of the queue; it is written now so the causes
> are on paper while the evidence is fresh.
>
> Related docs: [module_tests.md](../research/module_tests.md),
> [PLAN_the_tests_take_their_temp_directories_with_them.md](../research/PLAN_the_tests_take_their_temp_directories_with_them.md)
> (the sweep before a run, 2026-09-23), [PLAN_two_equal_deadlines_race_on_macos.md](PLAN_two_equal_deadlines_race_on_macos.md),
> [PLAN_a_turn_waits_by_the_clock.md](PLAN_a_turn_waits_by_the_clock.md),
> [PLAN_question_consultant_tails.md](PLAN_question_consultant_tails.md) §6. Boundaries: §7.

## 1. The symptom

Three things, all measured on 2026-10-19.

1. **A test run in one worktree changed the result of a test run in another.** Two full `src_mcp` runs from two
   worktrees of this repository ran at the same time on one Windows user. `QuestionRowOnAgyScenarioTests` wrote agy's
   denied stream into `%TEMP%\fakecli-minimal.json`, and the OTHER run's `AVibeReviewerRunsThroughAFakeVibeTests`
   launched the fake CLI, which read that file and answered as agy (its "auto-denied" text where Vibe's review was
   expected). Each run was correct alone. The fake CLI reads ONE
   steering file per Windows user, and the `fakecli-env` collection that keeps its six writers apart works only inside
   one test process.
2. **CI goes red on changes that did not cause it.** In the last 1,500 `ci.yml` runs (2026-09-26 to 2026-10-19) 174
   failed. The same handful of tests failed again and again on branches that did not touch them — dependabot bumps,
   release-please PRs, docs-only PRs and `main` itself (§3). Each one passes on a rerun, so each one costs a rerun and
   teaches people that red is normal.
3. **The temp folder fills up.** `%TEMP%` held **95,285** entries, **23,558** of them `coai-*`; 22,492 of those were
   made the same day (§5). In 2026-09-13 the same folder held 81,986 `coai-*` folders, and the product's scratch sweep
   that walks them made `SubmissionOrderTests` look hung. A throttle fixed the hang, not the pile.

The repository rule decides how each case is read: **a test that passes alone and fails in the suite is a SHARED-STATE
defect, never a flake, and every case gets a cause.** This plan gives each case its cause, with its evidence and how
sure we are, and the smallest fix that removes the cause — never a weaker assertion, never a skip, and never a longer
timeout unless the measured cost of the work says the old one was wrong.

## 2. The goal — what is true when this plan is done

1. No test reads or writes a FIXED name in the machine's shared temp folder. Two runs from two worktrees on one user
   cannot see each other.
2. Every case in §4 is closed: its cause is fixed in the product or in the test, a RED test showed the cause before
   the fix (or, for a scheduling race that has no deterministic red, a recorded loop before and after), and the case
   has not failed in CI for the 20 runs of its leg after the fix merged.
3. A full `src_mcp` run and a full extension run leave nothing in `%TEMP%` that only the product's six-hour sweep would
   remove. The product-named folders the tests make (`coai-answers-`, `coai-repair-`, `coai-noworkspace-`) and the
   loose `api-*.prompt` / `local-*.prompt` files go into ONE per-run folder that the run removes.
4. The product's own rounds stop leaving two always-empty folders per round (`coai-repair-`, and `coai-noworkspace-`
   where it is made).

## 3. The census — what failed in CI, and where it belongs

Read from `gh run list --workflow ci.yml --limit 1500` and `gh run view <id> --log-failed` for all 174 failed runs
(2026-09-26 11:38 UTC to 2026-10-19 15:45 UTC), plus two runs that were still going when the list was taken and failed
later (39269879269 and 39276863927, cases C5 and C21). "Unrelated" branches are `main`, `release-please--*`, `dependabot/*`,
`docs/*`, `chore/*`: a failure there is not work in progress. Tests that failed only on the one branch that was changing
them (for example `StageRulesTests` on rule-budget branches, `HeldFileInheritableOnLinuxTests` on `feat/wsl-kimi-e5*`)
are work in progress and are not in this table unless §4 says otherwise.

| Test | Failed CI runs (leg) | Branches (of them unrelated) | First – last | Case | Class | Built by |
|---|---|---|---|---|---|---|
| `SecuritySourcesCharacterizationTests.The_collection_deadline_…` | **43** (macOS 37, Windows 6) | 21 (12), `main` among them | 10-03 – 10-19 | C8 | timing + product | its own plan |
| `BoundedSchedulerTests.FourOfSixAnswer_TheSummaryNamesWhoFailedAndWhy` | **17** (macOS) | 13 (8) | 09-28 – 10-16 | C2 | product | this plan |
| `TheRoundsNumstatsAreCountedTests.AnUnknownCommit_IsGitFailed_NeverAThrow` | 9 (macOS, Linux) | 7 (6) | 10-15 – 10-16 | C20 | fixed by #840 | — |
| `CadenceStoreTests.ManyClosesAtOnce_AllLand` | 3 (Linux 2, Windows 1) | 3 (3), `main` 10-19 | 09-28 – 10-19 | C19 | product | its own plan |
| `APromptFileDoesNotOutliveItsLaunchTests.AFileAlreadyGone_IsNotAFailure` | 3 (macOS) | 3 (2) | 10-16 – 10-19 | C2 | product | this plan |
| `AUsageTheVendorNeverReportedIsUnknownTests.An_api_call_whose_deadline_struck_…` | 3 (Linux) | 2 (2), `main` | 10-03 – 10-10 | C16 | shared state in the stub | this plan |
| `KimiCredentialsLockTests.AShimThatMeetsADisposalsProbe_…` | 2 (Windows) + 3 work-in-progress | `main` 10-18 | 10-16 – 10-18 | C3 | timing | this plan |
| `EndToEndTests.ARoundThatFoundNothing_SaysWhatItSent_…` | 2 (macOS) | 2 (2), `main` | 09-26 | C2 | product | this plan |
| `AcpClientUsageTests.AUsagePromptThatOutlivesTheTurnsDeadline_…` | 1 (Windows, run 39269879269) | dependabot | 10-19 | C5 | timing | this plan |
| `AConversationPlaceTests.CopiesThatOutrunTheRowsBudget_…` | 1 (Windows) | dependabot | 10-15 | C17 | timing | this plan |
| `QuestionFanOutTests.TheHeartbeatIsRewritten_…` | 1 (Windows) | dependabot | 10-16 | C18 | timing | this plan |
| `ConsultAntigravityDenialScenarioTests.AHungFollowUp_…` | 1 (Windows) | a branch that does not touch it | 10-16 | C15 | timing | this plan |
| `AReaderIsEndedWithItsTreeTests.TheDescendantsOfALiveServer_…` | 1 (Windows) | `fix/flaky-size-deadline-test` | 10-16 | C4 | shared file, fixed by #849 | — |
| `PidfdLayerTests.AZombie_IsGone_NotSignalled` | 1 (Linux, run 39276863927, PR #868) | a store PR | 10-19 | C21 | timing inside the test | this plan |

**Seen only in local whole-suite runs** (no CI failure in the 174 logs): the steering-file collision (C1), the three
`AReviewerThatAsksForSourceIsAskedAgainTests` budgets (C13), `AFeatureIsReviewedEndToEndTests.TheRoundsDeadline_…` (C6),
`AReaderIsEndedWithItsTreeTests.AServerThatNeverAnswers_…` (C4), `ConsultationSweeperTests` (C14),
`SqliteMigratorTests` (C7) and the extension's `processLauncher.test.ts` "many lines" test (C9). **Found by reading, not by
a failure:** C10, C11, C12.

The macOS leg is not a required check, so auto-merge passes over its red — which is how C2 and C8 lived for three
weeks: 54 of the 174 failed runs hold one of those two tests on that leg, and 60 hold one of them on any leg.

Not flaky, and therefore not in this plan: `teamServerTab.test.ts` "the Settings sheet is byte-identical …" (two runs on
`docs/promote-every-round-is-counted`, 2026-10-19) failed on two commits of that branch and passed after its commit
`753d7823b` "re-pin the Settings sheet for the plan's new path" — a pinned hash that a moved plan changes, the same on
every run of one commit; and the three `release-please` changelog tests, which check release state, not timing.

## 4. Each case, with its cause

Every `file:line` below was read on `origin/main` `ea9ccd1c8` (2026-10-19) and re-checked on `38ecd4e11` (#887, the same day) for
the files that commit touched. **Class** is SHARED STATE (two runs or two
tests use one thing), TIMING (a deadline shorter than the measured cost of the work it waits for), or PRODUCT (the
product is wrong and the test is the first to see it). **Confidence** says what the evidence proves and what would raise
it.

### C1 — the fake CLI's steering file is one file per Windows user (SHARED STATE, high)

**Evidence.** The fake CLI answers in vendor mode from a JSON file when its environment carries no `FAKECLI_MODE` and
its argv has a vendor's shape: `MinimalSteering.Load` (`src_mcp/tests_fakecli/Program.cs:500-515`) reads
`Path.Combine(Path.GetTempPath(), "fakecli-minimal.json")` (`:498`, `:507`), and `Program.cs:117-118` then takes EVERY
`FAKECLI_*` value from it. The ACP agent reads the same file for its script (`AcpAgent.SteeredScript`,
`src_mcp/tests_fakecli/AcpAgent.cs:99-112`). Six test classes write it, each through its own copy of
`private static string SteeringFile => Path.Combine(Path.GetTempPath(), "fakecli-minimal.json")`:
`AKimiRowRunsThroughTheServiceOnWsl2Tests.cs:32`, `AskHumanScenarioTests.cs:37`, `AVibeReviewerRunsThroughAFakeVibeTests.cs:33`,
`QuestionConsultScenarioTests.cs:41`, `QuestionRowLookupScenarioTests.cs:35`, `QuestionRowOnAgyScenarioTests.cs:43`.

Inside one run they cannot meet: all six are in `[Collection("fakecli-env")]`, which is
`DisableParallelization = true` (`src_mcp/tests/PanelServiceTests.cs:24`), so xUnit runs that collection alone, after
the parallel ones. Across two runs nothing keeps them apart. `Path.GetTempPath()` is the same folder for every process
of one Windows user (and `/tmp` for every process on a Linux box without `TMPDIR`), so a second run in another worktree —
the normal case here, with several agent sessions on one machine — writes and deletes the file the first run's child is
reading. A run killed before its `DisposeAsync` leaves the file behind, and every later vendor-shaped launch with no
`FAKECLI_MODE`, in any checkout on that user, is steered by it.

**Why the file is in temp at all.** The child that reads it is started on a MINIMAL environment (the question rows,
`research/module_tests.md:198`) or one built from scratch (the Kimi shim, the Vibe launch), so no `FAKECLI_*` variable
reaches it; `TEMP` does. That is also why the change the test-leftovers rollout's epic E5 ("leftovers-E5" below) proposes for these sites
(§7: "the path comes from an environment variable the test sets") cannot work for them: the variable never arrives.

**Fix.** The file moves next to the fake's own binary: `Path.Combine(AppContext.BaseDirectory, "fakecli-minimal.json")`.
The tests start the fake as `FakeCli.dll`/`FakeCli.exe` from the test project's output folder
(`src_mcp/tests/CoaiMcp.Tests.csproj:27-28`), so `AppContext.BaseDirectory` of the child IS the test's own
`AppContext.BaseDirectory` — one folder per checkout and per configuration, which no other worktree shares, and which the
child finds whatever its environment says. One shared helper replaces the six copies: `FakeCliSteering.MinimalFile`
(beside the existing `FakeCliSteering.Reset`, `src_mcp/tests/TheVaultKeyTravelsOnStdinTests.cs:312`, which moves into
its own file), with `Write(pairs)` returning an `IDisposable` that deletes it, and an assembly-start step that deletes a
file a killed run left beside the binary. `AcpAgent.SteeredScript` reads the same path. **Checked:**
`TheServerAnswersInitializeAtOnceTests.cs:160-175` copies the fake into a test folder as `creds` — that copy's
`AppContext.BaseDirectory` is the test folder, so it would NOT see the file — but it steers by its environment
(`FAKECLI_MODE=vendor`, `:52`, `:94`) and `MinimalSteering.Load` returns before it looks for the file when that is set
(`Program.cs:502`). It is the only place a test copies the fake.

The same shape, one file, for the Team server's app-server stand-in: `Serving.AppServer` reads
`Path.Combine(Path.GetTempPath(), "fakecli-appserver.json")` (`src_mcp/tests_fakecli/Serving.cs:22`, `:140`). Its one
writer, `src_server/tests/GaugeReaderCrashHarnessTests.cs:60-61`, hands the server it starts a PRIVATE temp folder, so the
file is one per test and never shared. It STAYS where it is: that is the stronger pattern (beside the binary it would be
shared by every class of the Team server suite that starts an app-server in parallel), and under D2 a stray copy lands
in the run's own folder anyway.

**RED first.** `ASteeringFileAnotherRunLeftInTheUsersTemp_NeverSteersThisRunsFake`: a decoy
`fakecli-minimal.json` that says "exit 7, stdout `decoy`" is written into a PRIVATE folder; the fake is started with
`TEMP`, `TMP` and `TMPDIR` pointed at that folder, a vendor argv (`exec …`) and no `FAKECLI_MODE`. Today it answers
`decoy` and exits 7 on every run, on every OS; after the fix it does not read the folder at all. The test never writes
into the real user temp. Its companion — a file beside the binary DOES steer — keeps the six scenario classes honest.

### C2 — a reviewer that exits before its start time is read is reported "could not be started" (PRODUCT, high)

**Evidence.** `BoundedSchedulerTests.FourOfSixAnswer_TheSummaryNamesWhoFailedAndWhy` (`src_mcp/tests/BoundedSchedulerTests.cs:277-296`)
failed in 17 CI runs, **only on the macOS leg**, from 2026-09-28 to 2026-10-16, on unrelated branches including
release-please and dependabot ones. The message every time:
`codex/Architecture: not started: '…/FakeCli' could not be started: Unable to retrieve the specified information about the
process or thread. It may have exited or may be privileged.` The reviewer named is always one that exits AT ONCE (`emit`
or `stderr-exit`), never the one that sleeps. The same message appears in 18 of the 174 failed logs.

**Cause.** After `Process.Start` the launcher records the child for the orphan sweep:
`_tracker.Record(process.Id, process.StartTime.ToUniversalTime(), request.TrackAs)` (`src_mcp/runners/Processes/ProcessLauncher.cs:311`).
On macOS `Process.StartTime` throws `Win32Exception` with exactly that text when the child has already exited and been
reaped; Windows keeps the handle, so it never throws there. The catch at `:313-317` kills and rethrows, and
`ReviewerExecutor` turns a `Win32Exception` into `ReviewerOutcome.NotStarted("'<exe>' could not be started: …")`
(`src_mcp/runners/Reviewers/ReviewerExecutor.cs:965-970`). Every reviewer is tracked (`TrackAs = "{Provider}/{Role}"`,
`:950`), so a real CLI that answers fast enough is reported as not started on a Mac — this is a product defect the test
found, not a test defect.

**The same cause, two more tests.** `APromptFileDoesNotOutliveItsLaunchTests.AFileAlreadyGone_IsNotAFailure`
(`src_mcp/tests/APromptFileDoesNotOutliveItsLaunchTests.cs:42-51`; macOS runs 38958131709, 39049834919, 39221834929,
2026-10-16 to 10-19: `Expected type to be ReviewerOutcome+Ok, but found ReviewerOutcome+NotStarted`) and
`EndToEndTests.ARoundThatFoundNothing_SaysWhatItSent_AndKeepsWhatItWasTold` (`src_mcp/tests/EndToEndTests.cs:185-235`;
macOS runs 36239446746 and 36268923698, 2026-09-26: `Expected kept to contain 8 item(s) … but found 7`). In the second
one the missing file is a DIFFERENT reviewer each time and the names never collide: one reviewer was "not started" and
kept nothing. On the same day run 36240525010 failed another `EndToEndTests` round with the "Unable to retrieve" text in
its message. Neither assertion prints the reason, which is why they read as two more mysteries; the
`ARoundThatFoundNothing` story also asserts "all reviewers answered" first, so a failure names its cause.

**Fix.** Read the start time through one helper: when the read throws AND `process.HasExited`, there is nothing for the
orphan sweep to find, so nothing is recorded and the launch goes on to its normal result. When the read throws while the
child is ALIVE, today's rule stays: kill, wait, rethrow (the remarks at `:300-306` say why). No assertion changes.

**RED first.** A seam on the launcher for the start-time read (`Func<Process, DateTime>`, default `p => p.StartTime`).
Test 1: the seam waits for the child to exit, then throws the macOS `Win32Exception`; `FakeCli emit hello` with a
`TrackAs` must end with exit 0 and `hello`, nothing recorded. Today it throws — red on every OS, every run. Test 2, the
guard: the seam throws while the child (`sleep 20000`) is alive — the exception still reaches the caller and the child
is gone. **Confidence: high** on the class (one message, always an instant child, only macOS); certain once the stack is
seen, which Test 1's message will show.

### C3 — a Kimi shim that meets a disposal's probe: a 250 ms stand-in for an instant, inside a 500 ms wait (TIMING, medium)

**Evidence.** `KimiCredentialsLockTests.AShimThatMeetsADisposalsProbe_WaitsItOut_AndTakesItsHold`
(`src_mcp/tests/KimiCredentialsLockTests.cs:315`) failed on the Windows leg in run 38853192407 (2026-10-16, branch
`fix/flaky-size-deadline-test`) and run 39151073693 (2026-10-18, `main`), in 704 ms and 622 ms:
`the child ended (exit 69) without taking its lock: … coai-tree.lock is held by another process, and nothing but a
disposal's probe ever takes it`. (Its other failures, 2026-10-16 on `feat/wsl-kimi-e5*`, were a different, finished
cause: the D19 sign-in folder on Linux and macOS before #879.)

**Cause.** The product waits `HomeWait = 500 ms` for the per-home file (`src_mcp/runners/Kimi/KimiCredentialsLock.cs:77`,
used at `:209`) through `HeldFile.TakeWithin` (`src_mcp/runners/Files/HeldFile.cs:167-180`), because a real disposal's
probe holds it "for the length of one open" (`KimiCredentialsLock.cs:73-76`) — microseconds. The test's stand-in,
`MeetADisposalsProbe` (`src_mcp/tests_fakecli/TreeHoldVerb.cs:55-59`), holds it for **250 ms** and lets it go from a
`Task.Delay(250).ContinueWith` on the thread pool of a freshly started child. The margin is 250 ms of scheduling on a
runner running eight test threads and dozens of child processes; 622 and 704 ms are 500 ms of waiting plus a cold child.
The product's 500 ms is right for what it waits for; the test's stand-in is three orders of magnitude longer than the
thing it stands for, and its release depends on a timer.

**Fix.** Release the stand-in by an EVENT, not a timer. The verb already reaches into the shim's take through a test
seam, `KimiCredentialsLock.BeforeTheHomeHold = MeetADisposalsProbe` (`src_mcp/tests_fakecli/TreeHoldVerb.cs:38`), and the
shim must keep its product take (`HoldTree` → the INHERITABLE `HeldFile.TakeVerified`, `KimiCredentialsLock.cs:172-175`) —
the test's subject. A second seam of the same kind, called when the home file's take first answers busy, lets the verb
release its probe at that moment. The test then proves exactly its title — the shim met a
busy file and waited it out — with no clock in the outcome. A second test pins the other side with no timer either: a
probe that is never released ends in exit 69 after `HomeWait`.

**RED first.** None deterministic for the race itself; the event-driven version is deterministic by construction. The
before/after proof is a loop: the test 100 times in a whole-suite load on Windows before (expect a failure in roughly
the CI rate) and after (0). **Confidence: medium** — the 500 ms wait certainly ended with the probe still held; that the
cause is the timer's late release and not an antivirus scan of the new lock file is not proven. The fix removes both.

### C4 — `AReaderIsEndedWithItsTreeTests` (SHARED STATE, fixed; and one TIMING risk, low)

- `TheDescendantsOfALiveServer_AreListedWithTheirStartTimes` (`src_mcp/tests/AReaderIsEndedWithItsTreeTests.cs:163`)
  failed in run 38853192407 (Windows, 2026-10-16) with a sharing violation on the pid file. **Already fixed** by
  `f1bc6ace2` (#849, 2026-10-18): `Serving.WritePidFile` writes a sibling and moves it into place
  (`src_mcp/tests_fakecli/Serving.cs:101-131`). No failure since. Nothing to build; recorded so the case is closed.
- `AServerThatNeverAnswers_IsEndedAtTheDeadline_WithItsTree` (`:108`): no CI failure in 174 failed logs. The risk read
  from the code: the test's `Timeout` is 2 s (`:115`) and the fake server must start a second cold `FakeCli` (the
  grandchild, `Serving.cs:153`) and write both pids before that deadline kills the tree; nothing waits for the pids
  first. A cold start under load is measured up to ten seconds on this machine (`research/module_tests.md:3536`). It is
  one of the four deadline tests that failed in the full local run of 2026-10-18 and passed alone
  (`research/module_tests.md:4475`); the failure text was not kept. **Fix:** the test's subject is "a server that never
  answers is ended at the deadline, with its tree"; the tree must EXIST before the deadline for that to be tested. The
  test waits for the pid file to name both processes (the fake already writes it whole, `Serving.cs:114-131`) before it
  lets the deadline matter: the launcher's deadline (`deadline.CancelAfter(request.Timeout)`,
  `src_mcp/runners/Processes/ProcessLauncher.cs:401`) is armed through an injectable `TimeProvider` (D3), and the test
  advances a manual clock past the deadline only after the pid file is complete. **RED first:** a `startDelayMs` on the `serve` verb larger than the deadline makes
  today's test fail on every run with the pid file missing. **Confidence: low** until a failure log is kept (D5 asks
  every failure in the suite to be kept with its full message).

### C5 — an ACP turn's 2 s deadline also pays the agent's cold start (TIMING, high)

**Evidence.** `AcpClientUsageTests.AUsagePromptThatOutlivesTheTurnsDeadline_ReturnsTheAnswerAtTheDeadline_NotAfterTheBound`
(`src_mcp/tests/AcpClientUsageTests.cs:47-58`) failed in run 39269879269 (Windows leg, 2026-10-19, a dependabot npm
bump that touches no C#), the only failure of 11,735 tests, in 2 s 025 ms: `Expected outcome.End to be AcpEnd.Answered …
but found AcpEnd.Deadline … the agent did not answer within the 2s this turn was given`. It also failed in the full local
run of 2026-10-18 (`research/module_tests.md:4475`).

**Cause.** The turn's clock starts right after the child process starts: `deadline.CancelAfter(ask.Deadline)`
(`src_mcp/runners/Acp/AcpClient.cs:119`). The test gives 2 s (`AcpClientUsageTests.cs:53`) and means "the deadline
strikes while `/usage` hangs"; but those 2 s also hold the fake agent's cold start, `initialize`, `session/new`, the
config call and the FIRST prompt. Under the suite's load that took longer than 2 s, so the deadline struck before the
answer and the end was `Deadline` — the product did the right thing for what happened. The test's subject (the usage
bound is clipped to what is left of the turn, `AcpClient.cs:180`, `:189-203`, the bound at `:195`) never got to run.

**Fix.** The turn's deadline and the usage bound read an injectable `TimeProvider` (D3; default `TimeProvider.System`,
so the product is unchanged). The test uses a manual clock: it waits until the agent has RECEIVED the second
`session/prompt` (the `/usage` one), then advances the clock past the deadline. The assertions at `:55-57` stay as they
are. **RED first:** a `startDelayMs` in the fake agent's script, longer than the deadline, makes today's test fail on
every run with the CI message. **Confidence: high** (the end was `Deadline` at 2.025 s on a change that touches no C#;
the clock placement is visible).

### C6 — the round-deadline end-to-end test: a whole round under 8 s of wall clock (TIMING, medium)

**Evidence.** `AFeatureIsReviewedEndToEndTests.TheRoundsDeadline_IsScaledByTheFollowUps_SoAConversationIsNotCutByAOneTurnBudget`
(`src_mcp/tests/AFeatureIsReviewedEndToEndTests.cs:447-458`) fails "about 1 run in 3 on a clean `origin/main`"
(`todo/PLAN_question_consultant_tails.md` §6) and failed in the full local run of 2026-10-18; never in the 174 failed CI
logs. The one message on record (tails §6): the test ran 12–20 s and reported "not started … the round reached its
limit" BEFORE the reviewer launched — the round's deadline struck during the round's own setup.

**Cause, from the arithmetic.** The round's deadline is `ReviewerTimeout × (1 + followUps)` = 2 s × 4 = 8 s
(`src_mcp/src/Server/Rounds/RoundDeadline.cs:11`), armed BEFORE the round is assembled
(`src_mcp/src/Server/Rounds/RoundEngine.cs:285-286`), so sha resolution, the worktree, the diff and the outline are
inside it. The conversation's cap is the same 8 s on a `Stopwatch` (`src_mcp/runners/Reviewers/TurnLoop.cs:41-45`), and
each turn is a real `FakeCli` launch that sleeps 700 ms (`FAKECLI_SLEEP_MS`, test `:451`) under its own 2 s timeout
(`TurnLoop.cs:134-138`). The test's own remark says the round takes 3–4 s idle: a 2× margin against eight parallel
test threads and several agent sessions on one machine. Nothing is shared; the margin is wall-clock.

**Fix.** The test's subject is arithmetic plus "the conversation is not cut": (1) a pure test pins
`RoundDeadline.For(… ReviewerTimeout = 2 s, followUps: 3)` = 8 s and `followUps: 0` = 2 s — none exists today (the
callers in `SecurityCoverageTests.cs:72` and `SecurityPresetTests.cs:203` pass zero follow-ups); (2) the round clock
(`RoundEngine.cs:286`), the conversation cap (`TurnLoop.cs:45`) and the per-turn timeout read the D3 `TimeProvider`,
and the end-to-end test advances a manual clock 700 ms per turn instead of sleeping. A one-turn budget then cuts the
conversation at turn 3 on every run, and the right budget never does. **RED first:** the same test with
`FAKECLI_SLEEP_MS=1900` fails on every run today — it shows that fixed sleeps against a fixed deadline decide the
outcome. **Confidence: medium-high** — the recorded message shows the round clock striking before the first launch, which is
exactly the setup-inside-the-deadline path above; keeping the next failure's `answer` JSON (D5)
confirms it. This case moves here from `PLAN_question_consultant_tails.md` §6 (§7).

### C7 — `SqliteMigratorTests` "database is locked" (cause NOT yet determined, low)

**Evidence.** Reported locally; not in any of the 174 failed CI logs; no stack kept. Every test of the class uses its
own `coai-migrator-<guid>` folder and `Pooling=False`, so no file is shared between tests. The one test with two
connections is `TwoMigratorsRacing_TheSecondWaitsAndNeverReRunsAStep` (`src_mcp/tests/SqliteMigratorTests.cs:94-136`).
The product sets `journal_mode=WAL` OUTSIDE the transaction, then `busy_timeout=5000`
(`src_mcp/storage/SqliteMigrator.cs:68-69`), then begins an immediate transaction.

**Candidate causes, and the observation that decides.** (H1, timing) the second opener waits at most 5 s
(`BusyTimeoutMilliseconds`, `:46`; the provider's own retry is the same 5 s through `Default Timeout=5`, `:58`) for the
first opener's DELIBERATELY slow step — three hundred thousand rows, slow so that the second one must wait. On a starved
machine that step can outlast 5 s: the product then does what it promises (a wedged writer surfaces as an error), and
the test's step cost decided the outcome. The stack would show the transaction begin or the version read after ≈5 s.
(H2, environment) an antivirus or indexer handle on the fresh `-wal`/`-shm` files; the stack would show the error after
a few milliseconds. Checked and ruled out: a shared file (none) and the pragma order — `journal_mode` runs before
`busy_timeout` (`:68-69`), but Microsoft.Data.Sqlite retries a busy statement until its command timeout
(`SqliteMigrator.cs:50-56`), so the first pragma is not unprotected. **Fix, in order:** keep the next failure's stack
(D5); for H1, the slow step is sized from a measurement (its time on this machine idle and with the suite running) so
that it stays well under 5 s at the measured worst while still forcing the wait — the product's 5 s is not changed.
**Confidence: low** — no failure text exists.

### C8 — the security lane's collection deadline: two equal 30 s timers (TIMING + PRODUCT, high) — owned by its own plan

`SecuritySourcesCharacterizationTests.The_collection_deadline_ends_the_file_it_interrupts_and_every_later_one`
(`src_mcp/tests/SecuritySourcesCharacterizationTests.cs:146-156`) is the largest single cost in the census: **43 failed
CI runs**, 37 on macOS and **6 on Windows** (38813184598, 38840957346, 38855510878, 38894853503, 39161171996,
39254220535), the last on 2026-10-19, including a `main` push (39160755246). Same text every time (`…c/B.cs =>\nnot served:…`
where `…Source read deadline reached…` was expected), 30.2–32.3 s each. The diagnosis of
[PLAN_two_equal_deadlines_race_on_macos.md](PLAN_two_equal_deadlines_race_on_macos.md) still holds on `ea9ccd1c8`, with
lines moved by one by `ef2a39199`: `SecuritySources.cs:14` (`CollectionBudget` 30 s), `:32` (`CancelAfter`), the catch
at `:85` and the post-loop check at `:88`; `SourceBudget.cs:57` (`ReadDeadline` 30 s); `SourceResolver.cs:200` (the
per-file `CancelAfter`), `:206`/`:210` (`Reading.Not(TimedOutReason)`). Its title says macOS; it is not only macOS any more.

That plan builds it; this plan only orders it (§9) and adds one correction to its step 3: injecting "200 ms / 300 ms"
must keep the per-file deadline LONGER than the collection budget in the characterization test, or "git timed out"
becomes the correct answer and the test would pin the wrong sentence. The deterministic RED it needs is the D3 manual
clock: fire the per-file timer BEFORE the collection timer, both past 30 s — red on every OS today.

### C9 — the extension's "a child that writes many lines nobody reads still finishes promptly" (TIMING, medium)

**Evidence.** `src_vs_code/src/test/processLauncher.test.ts:250-269`; it failed locally at the `ended` helper's 10 s
timer (`:22-34`); in every extension CI log it passes in 0.10–0.19 s. It spawns `node -e` writing 40,000 lines (about
1.75 MB) and asserts `took < 4_000` ms (`:268`). The launcher always reads stdout and stderr (`src_vs_code/src/processLauncher.ts:318-324`),
so there is no back-pressure hang; the quadratic fan it guards against was fixed on 2026-09-09 (`held.push`,
`processLauncher.ts:219`).

**Cause.** The test measures wall time of a fresh `node` process plus 40,000 pipe writes, under `node --test` with up to
eight test files at once (`src_vs_code/scripts/run-tests.mjs:62-80`) and other sessions on the machine. Wall time is
the scheduler's, not the launcher's: what the test means — "the fan does not rebuild its buffer per line" — is a CPU
cost. **Fix:** a direct test of `replayingFan` (exported for tests — today it is a module-private function, `src_vs_code/src/processLauncher.ts:194`) that emits 40,000 values with no subscriber and
asserts the CPU time spent (`process.cpuUsage()` delta) is under a bound the quadratic version cannot meet (it costs
about 6 s of CPU at 40,000 values, per the file's own 64 s at 130,000; the append costs milliseconds); the real-child
test stays as a smoke test of the pipes — exit 0 and every character arrived — without a wall-clock assertion. The
guard moves, it does not weaken: the CPU bound catches the quadratic fan at 40,000 values on any machine, where the 4 s
wall bound sat below the regression's own ≈6 s by a margin a busy machine could erase in either direction. **RED
first:** put `held = [...held, value]` back for one run; the CPU test goes red, the smoke test does not. **Confidence:
medium** that load alone explains the local failure (its log was not kept); high that the fix removes the clock from
the outcome.

### C10 — test assertions that read the SHARED temp folder (SHARED STATE, high)

Found by reading every `Path.GetTempPath()` site of the test trees, not by a failure:

| Site | What it shares | How it can change a result |
|---|---|---|
| `src_mcp/tests/PanelServiceTests.cs:264-270` | enumerates EVERY `%TEMP%\coai-repair-*` and asserts the set is non-empty and every one empty | another run's (or a live round's) repair folders count: `NotEmpty` can pass for the wrong reason, and one non-empty folder anywhere on the machine turns it red. On 2026-10-19 the glob walked 8,843 folders |
| `src_mcp/tests/ADocumentIsProvenBeforeItIsReviewedTests.cs:276-293` | `%TEMP%\coai-outside-the-repo` (fixed folder; created, then deleted in `finally`) | two runs: the second `Directory.Delete` throws `DirectoryNotFoundException` |
| same file `:119-130` | `%TEMP%\coai-not-in-the-repo.md` (fixed file) | another run deletes it mid-test and the refusal says "not found" instead of "inside" |
| `src_mcp/tests_fakecli/Serving.cs:22`, `:140` | `%TEMP%\fakecli-appserver.json` | none: the one writer gives its server a private temp folder — kept as it is (C1) |

**Fix.** Each becomes a path inside the test's own temp folder. The repair test asserts on what the repair launch was
GIVEN — its recorded working directory and that it was empty AT LAUNCH (a launcher wrapper records the request, or the
fake records its cwd and the entry count beside its argv; today it records only argv and stdin, `Program.cs:56`,
`:634`) — never on a folder looked up after the round, which D4 removes. The fixed strings that are only PATHS and are
never created (`KimiLaunchChecksTests.cs:16`, `TheReviewTreeRootCanBePointedTests.cs:19` and fifteen more, listed in
the stories document) cannot collide and are left alone. **RED first** for the repair test: a non-empty
`coai-repair-decoy` in the temp folder the test sees turns it red today.

### C11 — process-wide environment written outside the serial collection (SHARED STATE, medium)

`ARequestFaultIsNotAnOldBinaryTests` sets and restores `COAI_DATA_DIR` (`src_mcp/tests/ARequestFaultIsNotAnOldBinaryTests.cs:35`,
`:40`, `:162`) and `VibeRuntimeTests` sets `VIBE_MCP_SERVERS` (`src_mcp/tests/VibeRuntimeTests.cs:76-88`); neither class
is in a collection, so both run in PARALLEL with everything else. `_was` is captured in a field initializer, so a value
another class set in the meantime is "restored" later. The other process-environment writers are all serial: most sit in
`fakecli-env`, twenty of the `COAI_DATA_DIR` ones in `console-out` (eight classes, e.g. `BugsQueryTests.cs:37`) —
both `DisableParallelization = true` (`PanelServiceTests.cs:24`, `BugsQueryTests.cs:23`), like `process-privileges`
(`TokenPrivilege.cs:108`). No failure is tied to these two yet; they are the shape every "passes alone" case looks for.
**Fix:** both classes join `fakecli-env`. **RED first:** a guard test that reads the test sources and fails on any
`Environment.SetEnvironmentVariable(` in a class that is not in a collection defined with `DisableParallelization =
true` — judged on the concrete classes, so `ConsultScenarioBase.cs`'s writes count through its twelve derived classes,
all in `fakecli-env` — red today naming exactly these two.

### C12 — a test run's temp sweep can reach into ANOTHER run that is still going (SHARED STATE, medium)

`TempDirsAreSwept` (`src_mcp/tests/TempDirsAreSwept.cs:58-60`) and the extension's `sweepFirst`
(`src_vs_code/scripts/run-tests.mjs:102-110`) remove every `coai-*` folder older than ten minutes before a run starts.
The ten minutes assume "the longest single run of this suite is under ten minutes" (`TempDirsAreSwept.cs:44-52`); a full
Windows CI run took about 17 and 22 minutes on 2026-10-16 and 2026-10-19, and a local one with other sessions takes
longer. A folder a running suite made early and has not written for ten minutes (a class fixture, a long-lived data
directory) is removed by the next suite that starts on the same machine. The test-leftovers rollout (leftovers-E5, §7) replaces
the age rule with owner liveness; until then D2 below takes this run's folders out of the shared root altogether, so no
other run's sweep can see them.

### C13 — `AReviewerThatAsksForSourceIsAskedAgainTests`: three turn budgets that also pay a process start (TIMING, high)

All three in `src_mcp/tests/AReviewerThatAsksForSourceIsAskedAgainTests.cs`; none failed in CI, all failed locally.

- `AConversationThatOutlivesItsCap_IsOneTerminalTimeout_WithEveryTurnsUsageKept` (`:463-476`) — each turn may take
  700 ms (`:467`) and the fake sleeps 400 ms (`:466`), so a cold `FakeCli` start plus exit has 300 ms; a slower start
  kills turn 1 and `EarlierTurns` is empty (`:475`, "No turn finished before the cap",
  `PLAN_question_consultant_tails.md` §6). It **fails alone** too — "2 of 5 runs alone" on `main`
  (`research/PLAN_codex_tier_floor.md:171`), "1 of 4" (`research/RESULTS_catalog_consultations_2026-10-06.md:43`) — which
  is the evidence that no peer is involved: the cost is the process start against a 300 ms margin.
- `AConversationCapOnTheWork_EndsTheConversation_BeforeTheDerivedOneWould` (`:485`) — the same mechanism with a 900 ms
  cap and a 300 ms sleep; about 600 ms of margin.
- `AResolverThatTimesOut_RefusesThatRequest_AndTheTurnGoesOnWithTheRest` (`:524-536`) — the resolver's per-file deadline
  is 300 ms (`:527`) for the deliberately slow `Big.cs` AND for the real `git show` of `Cart.cs`; one git start over
  300 ms turns `Cart.cs` into "not served … timed out" and `:535` fails.

**Fix.** D3: `TurnLoop`'s cap and per-turn bound (`src_mcp/runners/Reviewers/TurnLoop.cs:41-45`, `:134-138`; it uses a
`Stopwatch` today) and `SourceResolver`'s per-file deadline (`src_mcp/runners/Feature/SourceResolver.cs:197-221`) read
the `TimeProvider`. The turn tests run through an in-process launcher stand-in that advances the manual clock by a turn's
simulated duration and answers `TimedOut` when the request's timeout is shorter — the file already has an in-process
launcher precedent (`TwoDecidedThenReal`, `:443`). The resolver test's slow read waits on the clock, which is advanced
only after the fast read was served. **RED first:** with the manual clock, removing the cap at `TurnLoop.cs:44` turns the
cap test red on every run; today's versions are the wall-clock ones. **Confidence: high** for the first (fails alone, the
margin is 300 ms), medium-high and medium for the other two (no failure text kept).

### C14 — `ConsultationSweeperTests`: a settings reload read while the file is half written (PRODUCT, low-medium)

`TheLoop_LapsesAnIdleConsultation_AndKeepsDoingSoAfterASettingsReload` (`src_mcp/tests/ConsultLimitsScenarioTests.cs:156-170`)
failed once in 488 ms in a full local run on 2026-10-03 and passed 15 of 15 alone (`PLAN_question_consultant_tails.md`
§6); no CI failure, no message kept. 488 ms rules out the ten-second waits; it is an assertion on the test thread, most
likely `:165` ("the reload happened"). The class has no collection, so its 50 ms beat runs beside the parallel suite.

**Cause, a hypothesis with a mechanism the code shows.** The test writes `settings.json` with `File.WriteAllText`
(`:164`) while the sweeper thread reads `host.Current` every 50 ms. `PanelServiceHost.Current`
(`src_mcp/src/Server/PanelServiceHost.cs:64-85`) compares a stamp and rebuilds; its own comment says "a rebuild that
throws leaves the old stamp in place, so the next call tries again" (`:76-78`). But `SettingsFile.Read`
(`src_mcp/src/Server/SettingsFile.cs:84-93`) turns an `IOException` — a sharing violation while the writer still holds the
file — into an EMPTY settings set, so the rebuild does NOT throw, and the host publishes DEFAULT settings under the new
stamp until the file changes again. In production that is a server that silently runs on defaults after a reload that
met a busy file. The same `catch` takes `JsonException` (`SettingsFile.cs:90-93`), which is what a HALF-written file
reads as. **Fix:** `SettingsFile.Read` tells "missing" (empty, as today) from "busy, torn or malformed" (throws), so the
host's own retry rule works. Two behaviours this decides, said out loud because they change production: at START
(`Build(first: true)`, `PanelServiceHost.cs:60`) a busy or malformed file still yields defaults with a warning — a server
that refuses to start is worse; on a RELOAD it keeps the last good settings and the old stamp, warns once per stamp, and
tries again on the next call — so a file left malformed keeps the previous settings until it is fixed, instead of
silently dropping to defaults. Also: the test's `Host()` (`:116-118`) passes no consultant-homes folder, so every beat
sweeps the machine's real per-user folders and walks the real temp folder — `ConsultScenarioBase` documents `homes` for
exactly this; the test passes its own. **RED first (Windows, no timing):** hold `settings.json` open for write with
`FileShare.Read` and new content flushed; read `host.Current`; release; read again — today the second read shows the
defaults. **Confidence: low-medium** — the mechanism is real in the code; that it is THIS failure is not proven.

### C15 — `ConsultAntigravityDenialScenarioTests`: a whole-turn stopwatch for a launch-level claim (TIMING, medium-high)

`AHungFollowUp_IsKilledByTheLauncher_InsideTheRemainingBudget_AndTheConversationIsKept`
(`src_mcp/tests/ConsultAntigravityDenialScenarioTests.cs:368`) failed once on Windows (run 38964817927, 2026-10-16,
`feat/wsl-kimi-vibe`, a branch that does not touch it): `Expected time to be less than 11s because the follow-up was
capped to what the first launch left, but found 12s, 961ms`. **Cause:** the stopwatch (`:378`) wraps the WHOLE consult —
a dozen and more git processes, the snapshots, the prompt diff, the records — and allows 3 s of it beside the 8 s of
launches (`:386`); that run spent about 5 s. The claim is about the follow-up LAUNCH's timeout, and
`ConsultantTurnTests.cs:90` already proves the cap at the runner's level. **Fix:** record each consultant launch's
`ProcessRequest.Timeout` and result through the launcher wrapper the scenario base already uses, and assert launch 2's
timeout is at most the budget minus the first launch's 6 s and that it ended `TimedOut`; the two stopwatch lines go, the
assertions that prove the launcher (not the backstop) killed it stay. This is not a weaker test: the new assertion
names the exact number the title claims, where the stopwatch let any follow-up under 3 s of slack pass. **RED first:** give launch 2 the whole budget
instead of `RetryLadder.Lesser(left, next)` (`src_mcp/runners/Consultation/ConsultantTurn.cs:107`) — the new assertion
fails every run. **Confidence: medium-high** (one failure; 12.96 s was below the 13 s backstop, so the cap worked and
the overhead did not fit the 3 s).


### C16 — an HTTP stub stopped while its handler still writes headers (SHARED STATE inside the stub, high)

`AUsageTheVendorNeverReportedIsUnknownTests.An_api_call_whose_deadline_struck_before_the_answer_is_recorded_as_unknown`
(`src_mcp/tests/AUsageTheVendorNeverReportedIsUnknownTests.cs:107`) failed on the Linux leg in runs 37134428327
(2026-10-03, `main`), 37138270645 (10-03) and 38063638766 (10-10) with a `NullReferenceException` thrown by the TEST's
`Dispose`, not by its body: `NameValueCollection.GetKey ← HttpListenerResponse.FormatHeaders ← … ← HttpListener.Stop ←
ApiEndpointStub.Dispose`. **Cause:** `Dispose` sets the hold and stops the stub in the same breath (`:40-42`); the set
wakes the stub's serving thread, which starts writing the status, the content type and the headers of the held answer
while `HttpListener.Stop()` on the test thread enumerates the same header collection to close the connection. The stub's
`Dispose` (`src_mcp/tests/ApiEndpointStub.cs:218-228`) catches only `ObjectDisposedException` and
`HttpListenerException`. **Fix:** the stub tracks its in-flight handlers and `Dispose` waits for them (bounded, with a
sentence when the bound is reached) before it stops the listener; nothing is swallowed. **RED first:** a stub whose
handler is released and then sleeps 300 ms before it writes; `Dispose` called in that window must return only after the
handler finished, and must not throw — today it returns at once. **Confidence: high** (the stack names both threads'
paths).

### C17 — a one-second copy budget that must also cover git starting (TIMING, medium-high)

`AConversationPlaceTests.CopiesThatOutrunTheRowsBudget_RefuseThePlace_SayingHowManyWereCopied_AndKeepNone`
(`src_mcp/tests/AConversationPlaceTests.cs:114-142`) failed once on Windows (run 38817686874, 2026-10-15): the refusal
said `0 of 2 copied`, the test expects `1 of 2` (`:136`). **Cause:** the budget is 1 s (`:132`), armed by
`copying.CancelAfter(budget)` (`src_mcp/src/Server/Consultation/ConsultantPlaces.cs:298`), and the FAST root's copy runs
git (a work-tree check, `ls-files`, the link checks) before it copies a byte; on a loaded Windows runner that passed 1 s,
and `copying.ThrowIfCancellationRequested()` (`:329`) reported 0. The slow root's 3 s sleep (`:121-127`) is not the
problem. **Fix:** D3 — the copy's budget is armed through the `TimeProvider`; the slow root's hook ADVANCES the clock past
the budget instead of sleeping, so the fast copy runs with no clock pressure and the slow one is cut every time.
**Confidence: medium-high** ("0 of 2" proves the fast copy outlived 1 s; its real cost was not logged).

### C18 — the question row's heartbeat counted by polling the file every 40 ms (TIMING, medium)

`QuestionFanOutTests.TheHeartbeatIsRewritten_WhileARowRuns_OnTheConfiguredInterval`
(`src_mcp/tests/QuestionFanOutTests.cs:686-710`) failed once on Windows (run 38906020752, 2026-10-16): `Expected
beats.Count >= 3 … found 1`, in **13 s** for a test that takes 1.5 s — the process was starved, so the row's 1.5 s delay
and the 200 ms beat fired late and together. The row still settled `Answered` (`:707` passed): not a product defect.
**Fix:** the store's `projected` callback (`QuestionConsultStore`, called on every write) collects distinct
`HeartbeatUtc` values; the scripted row waits on a gate instead of `Wait = 1.5 s` and is released when three distinct
beats were seen, under a 30 s guard that fails with its own sentence. The test then depends only on "the heartbeat is
rewritten". **RED first:** with the heartbeat loop removed the redesigned test fails at the guard; today's version is the
wall-clock one. **Confidence: medium** on the cause (no timeline in the log), high that the redesign removes the clock.

### C19 — `CadenceStoreTests.ManyClosesAtOnce_AllLand`: a turn counted in tries (PRODUCT, high) — owned by its own plan

Failed on Linux 36422964311 (2026-09-28, 373 ms) and 38446522574 (10-13, 206 ms) and on Windows 39218744065
(**2026-10-19, `main`**, 2 s 126 ms): `the cadence record … is being written by another server and its turn could not be
had`. `SessionTurn.Take` still gives up after 40 tries of 2–8 ms (`src_mcp/src/Server/SessionStore.cs:417-432`) and
`CadenceStore` still throws on a missed turn (`src_mcp/src/Server/Cadence/CadenceStore.cs:66-70`); the diagnosis of
[PLAN_a_turn_waits_by_the_clock.md](PLAN_a_turn_waits_by_the_clock.md) holds. Two corrections for that plan: its caller
list is stale (`ConsultationStore` no longer calls `Take` directly; `RecordFiles.cs:51`, `QuestionConsultStore.cs:104` and
`QuestionPhaseStore.cs:87`, `:108` now do), and the clock-based wait it describes already exists —
`HeldFile.TakeWithin` (`src_mcp/runners/Files/HeldFile.cs:167-180`, added in the same commit as that plan) — so the fix is
to call it, not to write a second one (the reuse rule).

### C20 — closed: three that are already fixed or were the intended red of work in progress

`HeldFileInheritableOnLinuxTests.L_N1a_ASecondInheritableTake_IsBusy_TheLockIsExclusive` (five Linux runs on
`feat/wsl-kimi-e5*`, 2026-10-16/17) was the measurement that proved .NET's `FileShare.None | Inheritable` takes a SHARED
`flock` on Unix; `LinuxHeldFile` fixed it and #879 merged it, and it has not failed on `main`. `KimiCredentialsLockTests`'
`TheTreeHold_OutlivesItsOpener_WhileAChildLives` and `TheGateAlone_DoesNotOutliveItsOpener_…` on macOS and Linux
(2026-10-16, `feat/wsl-kimi-e5`) were the D19 refusal before #879 gave Linux a folder key and skipped macOS. And
`TheRoundsNumstatsAreCountedTests.AnUnknownCommit_IsGitFailed_NeverAThrow` (nine runs, 2026-10-15/16) was fixed by #840
(`e32965d78`, merged 2026-10-16 05:42 UTC); no failure since. Recorded so the census has no unexplained row.


### C21 — a zombie made by a shell that may still be a shell (TIMING inside the test, medium)

**Evidence.** `PidfdLayerTests.AZombie_IsGone_NotSignalled` (`src_mcp/tests/PidfdLayerTests.cs:473-496`) landed on `main`
with #879 (`cb9ec8b65`, 2026-10-19) and failed in its first day: run 39276863927 (Linux leg, PR #868
`docs/plan-session-keeps-unknown-fields`, a store change that does not touch it), after 10 s 025 ms: `the outer sleep
never reaps, so the killed child should have stayed a zombie` (`BecomesZombieAsync`, `:498-512`).

**Cause.** The test starts `/bin/sh -c "sleep 600 & echo $!; exec sleep 600"` (`:477`), reads the background child's pid
from the `echo` line and kills that child AT ONCE (`:481-485`). The design needs the child's parent to be the outer
`sleep`, which never reaps — but that is only true AFTER the shell's `exec`. Between the `echo` and the `exec` the
parent is still the shell, and a shell that collects its dead background children in that window (bash does, from its
`SIGCHLD` handler and whenever it waits; whether Ubuntu's `dash` does there is not verified) leaves no zombie: the pid is
GONE, never `Z`, and the poll runs its 10 s to the end — which is the 10 s 025 ms of the log. Nothing outside the test
is involved: the test host is never a subreaper (`AskAcpSubreaperTests.cs:73-79` keeps `Become()` out of it on purpose),
and no code kills another test's process tree by name. **Fix:** the test kills the child only after the PARENT has
become `sleep` — it waits (bounded, with its own sentence) until `/proc/<parent>/stat`'s `comm` reads `sleep` through
`ProcStat.ReadFor`, then kills. **RED first:** `bash -c "sleep 600 & echo $!; sleep 2; exec sleep 600"` — bash reaps the
killed child while it waits for the foreground `sleep 2`, so today's test fails on every run with the CI sentence; with
the wait for `comm == sleep` it is green. **Confidence: medium** — the window is certain from the script; that the CI
shell closed it by reaping is inferred from the 10 s and from the absence of any other actor.

## 5. The temp folder — what is in it, who made it, why it stays

**Measured 2026-10-19, ≈17:30 local, read-only** (counts only; nothing was removed). `%TEMP%` held 95,285 entries;
23,558 start with `coai-`:

| Prefix | Count | Made | Created by | Removed by | Why it stays |
|---|---|---|---|---|---|
| `coai-answers-` | 8,977 | all 2026-10-19 | the PRODUCT, `src_mcp/src/Server/Rounds/RosterBuilder.cs:280` (once per `BuildWork`), `src_mcp/src/Server/SecurityRoster.cs:153` | only the product's sweep, six hours after its last write (`PanelService.cs:1419-1433`) | the round never removes it, on success or failure; the test sweep may not (`neverSwept`) |
| `coai-repair-` | 8,843 | all 2026-10-19 | the PRODUCT, `RosterBuilder.cs:282` | the same sweep | the same; ALWAYS empty (8,844 of 8,844) |
| `coai-noworkspace-` | 4,672 | all 2026-10-19 | the PRODUCT, `RosterAssembly.cs:42` (a fast code row), `SecurityRoster.cs:154` | the same sweep | the same; always empty |
| `coai-chat-store-` | 318 | since 2026-10-11 | TEST `chatStoreFile.test.ts:45` (`home()`), leaked at `:172` and `:474` | nothing | the test removes the sub-folder it named, never the `mkdtemp` parent; `coai-chat-` is in `neverSwept` |
| `coai-chat-ledger-` | 165 | since 10-11 | TEST `chatUsageFile.test.ts:21`, leaked at `:88` | nothing | the same mistake |
| `coai-chat-lock-` | 160 | since 10-11 | TEST `chatStoreLock.test.ts:27`, leaked at `:285` | nothing | the same mistake |
| `coai-chat-fast-` | 159 | since 10-11 | the PRODUCT's `chatFastSettings.ts:44` (one folder per extension process, by design), called with no home by `aChatRunsOnItsRowsFastMode.test.ts:68` | nothing | a test process is an "extension process" that never ends a session |
| `coai-plan-said-` | 196 | since 10-11 | TEST `PanelServiceTests.cs:352` | nothing | its `finally` resets the variable and forgets the folder (its siblings at `:480`, `:518`, `:542` delete theirs); `coai-plan-` is in `neverSwept` |
| `coai-check-model-` | 30 | since 10-18 | TEST `EpicTwoReviewThreadsTests.cs:69` | nothing | no delete; `coai-check-` is in `neverSwept` |
| other | 38 | — | — | — | — |

Beside them, in the temp ROOT with no `coai-` prefix, so no sweep at all ever looks at them: **4,284 `api-*.prompt`**
and **998 `local-*.prompt` / `local-*.json`** files, oldest 2026-10-07 — tests hand the bare temp root to a runtime as
its output folder (`ApiRuntimeTests.cs:28`, `ADeniedCommandIsAskedAgainTests.cs:36`, `EveryPaidLaunchIsWrittenTests.cs:102`,
`LocalReasoningEffortTests.cs`), and the runtime writes its prompt file there (`ApiRuntime.cs:71`, `LocalRuntime.cs:123`).
(Also 12,148 empty `stage-<guid>` folders, newest 2026-08-21 — not made by any code in this tree; and 221
`wsl-care-test-archive-*`, another repository's.)

**Where the three big ones come from.** All 22,492 were made between 11:46 and 17:32 that day, in bursts that match test
runs (6,930 between 11:46 and 12:55 with two runs overlapping; 3,751 between 15:59 and 16:23; 3,595 between 16:29 and
16:47), and the files inside the non-empty `coai-answers-` folders are the fake reviewers' (`local-<Role>-<guid>.prompt`,
`codex-<Role>.json`): **no real round made any of them.** One full `src_mcp` run makes about 3,600–3,750:
`SubmissionOrderTests` alone calls `BuildWork` a hundred times in one loop (`SubmissionOrderTests.cs:116-118`), and every round
of `EndToEndTests`, `PanelServiceTests`, `CadenceGateScenarioTests` and the feature and document scenarios goes through
it. `research/module_tests.md:415-419` estimated "about a thousand per server-suite run" on 2026-09-23; it is now about
3.7 times that. With six hours to wait and this machine's run rate, the steady state is tens of thousands.

**What the walk costs now.** One `readdir` of the 95,285 entries took 124 ms and a `stat` of the 23,558 `coai-*` ones
602 ms (node, one measurement). The product's sweep is throttled to once per ten minutes per process
(`PanelService.cs:1395-1415`, the 2026-09-13 fix), so the cost is no longer a hang; it is ≈0.7–1 s per process start at
this size, ≈3 s at the 2026-09-13 size, and the reason a slow machine stays slow.

**Why the product is not the problem, and still owes one thing.** For a real round the six-hour sweep is right: a
round's answers folder can be written long after it was made. But `coai-repair-` and `coai-noworkspace-` are made
EMPTY so an agentic CLI has nothing to explore (`PanelServiceTests.cs:260-270` asserts that), are never written, and
are never needed after the round — the round can remove them itself.

## 6. Decisions

**D1 — The fake CLI's minimal-environment steering file lives beside its binary** (C1). `FakeCliSteering.MinimalFile` is
`Path.Combine(AppContext.BaseDirectory, "fakecli-minimal.json")`; one helper writes it and deletes it; the fake and the
ACP agent read the same constant. (The app-server file keeps its private-temp pattern, C1.) *Loses:* an environment variable naming the file (the minimal and built environments
never carry it — the reason the file exists); relying on D2 alone (D2 isolates runs too, and `TEMP`/`TMP`/`TMPDIR` are on
every launch's allowlist, `src_mcp/runners/Processes/ProcessEnvironment.cs:48-52` — but then the steering would be
correct only while every future launch path keeps that allowlist; the binary's own folder does not depend on how any
launch builds its environment). **Its limits, stated:** two runs of the SAME output folder at once still share the file
(one checkout, one configuration started twice at once — not a way this suite is run, and a rebuild between them is
refused while the first holds its binaries); and inside one run the file is safe only because every writer is in
`fakecli-env`, which runs alone — the helper being the ONLY writer keeps that true, and a source scan in E1 fails on any
other `fakecli-minimal.json` literal in the test tree. A per-launch steering file was weighed (a sibling repository's fake takes
its script path through each child's own environment) and rejected for the same reason as the variable: the children
here are started on an environment the test does not write.

**D2 — Each suite runs inside ONE temp folder of its own.** Before any test, the C# assembly fixture makes
`testsToDelete-coai-run-<6 chars>` in the real temp folder, remembers the real one (the existing start-of-run sweep
still sweeps the REAL folder), and points `TEMP`, `TMP` and `TMPDIR` of the test process at the new one; at the end it
restores them and removes the folder. `src_vs_code/scripts/run-tests.mjs` does the same for the `node --test` children
it starts. `Path.GetTempPath()` and `os.tmpdir()` read those variables on every call, and every child inherits them —
the suite already relies on this in `AVibeHomesNeverLiveUnderTempTests.cs:15-60`. Everything a run makes in "temp" —
its own folders, the product's `coai-answers-`/`-repair-`/`-noworkspace-`, the loose `api-*.prompt` files, a stray
steering file — is then inside one folder that the run removes, that no other run's sweep can see (C12), and that a
glob over "temp" cannot leave (C10). A run that is killed leaves ONE folder, named by the family's leftovers rule
(`.agents/conventions/common/test-leftovers.md`), for leftovers-E5's sweep or the janitor. *Loses:* a temp-root parameter through
the product (`RosterBuilder`, `RosterAssembly`, `SecurityRoster`, every runtime's output folder) — a product change for a
test concern, the follow-up leftovers-E5's D5 proposed; the operating system already offers this seam. *Risk, measured in its
story:* the folder adds about 25 characters to every temp path, and some tests sit near a path budget (Kimi's 260,
Vibe's 102 — Vibe homes no longer live under temp, `AVibeHomesNeverLiveUnderTempTests`); the story runs those classes
first and keeps the name short. *Two things the story proves rather than assumes:* that xUnit v3 constructs the assembly
fixture before every class fixture and static initializer that reads the temp path, and disposes it after the last
(a test that records `Path.GetTempPath()` from a class fixture says so); and that the extension runner removes its
folder BEFORE it calls `process.exit` (`run-tests.mjs` ends the process itself — the same trap leftovers-E5 recorded for
`run-host.mjs`, where a `.finally` never ran). Children reach the folder because `TEMP`, `TMP` and `TMPDIR` are on every
launch's allowlist (`ProcessEnvironment.cs:48-52`).

**D3 — A deadline a test must race is armed through an injectable `TimeProvider`.** `ProcessLauncher` (`:401`),
`AcpClient` (`:119`, `:195`), `RoundEngine`'s round clock (`:286`), `TurnLoop`'s cap and per-turn bound, the consultant place's copy budget
(`ConsultantPlaces.cs:298`), and `SecuritySources`/`SourceResolver` (C8) take an optional `TimeProvider`, default `TimeProvider.System` — the product
behaves exactly as today. A test that means "the deadline strikes while X" drives a manual clock and advances it when X
has happened. **The seam's contract is the cornerstone, not the parameter:** every elapsed-budget calculation, delay
and cancellation on a path reads the SAME clock (a `Stopwatch` left beside an injected timer is the race again); the
launcher keeps its separation of the execution deadline from the output-drain grace and of caller cancellation from
timeout (`ProcessLauncher.cs:287`, `:401`, `:437`); and the tests get ONE manual clock with timers, monotonic timestamps
and disposal — the suite's `FixedClock` (`src_mcp/tests/FixedClock.cs:16`) and `MovableClock`
(`TheRunsThemselvesTests.cs:209`) override only the wall time. The shape to copy, by path:
`wsl_care · src_daemon/tests/WslCare.TestSupport/ManualTimeProvider.cs` (timers `:36`, timestamps `:58-60`). *Loses:* longer timeouts (they move the race, they do not remove it, and every second is paid on every
run); fixed sleeps in the fakes as the thing that decides the outcome.

**D4 — A round removes its own always-empty folders.** `coai-repair-` and `coai-noworkspace-` are removed when the round
ends — the story names the actual end: `RoundEngine` has no `finally` for it today, and the folders travel in
`RosterFrame` (`RosterAssembly.cs:19`, used at `RosterBuilder.cs:479`) — after every child that used them is gone — reviewers and repairs,
on success, failure and cancellation alike — and by the PATH the round recorded when it made them, never by a name
glob; `coai-answers-` keeps the six-hour sweep. A `BuildWork` called with no round around it (`SubmissionOrderTests.cs:116-118`, a hundred
times) has no end to remove them at; under D2 that is the test's folder, and D4 does not claim more than rounds. *Loses:* leaving it to D2 (D2 helps only the tests; a person's machine makes two of these per round for
ever).

**D5 — A failure in the suite is written down before it is fixed.** Four of the cases here (C4b, C6, C7, C9) were seen
locally and their messages were not kept, which is why their confidence is low. Each story that touches a case starts
from a kept failure — the CI log, or the local runner's full output saved with the PR — or says that none exists.

**D6 — The seven leaking test sites are fixed here, under today's names** (§5: `chatStoreFile.test.ts:172`, `:474`,
`chatStoreLock.test.ts:285`, `chatUsageFile.test.ts:88`, `aChatRunsOnItsRowsFastMode.test.ts:68`,
`PanelServiceTests.cs:352`, `EpicTwoReviewThreadsTests.cs:69`): each removes the folder it made. Renaming every test
folder to `testsToDelete-coai-…` stays leftovers-E5's (§7).

## 7. Boundaries with other plans (both sides)

| Item | Built by | The other plan's part |
|---|---|---|
| C1/D1 — the minimal-environment steering file beside the fake's binary | **this plan** (E1) | leftovers-E5, the test-leftovers rollout's epic (`todo/PLAN_test_leftovers_E5.md`, draft PR #850, not merged) S4 lists "`tests_fakecli` fixed file names → an environment variable the test sets"; that cannot reach a minimal-environment child, so its row is superseded by D1 and its author drops it when E5 is next amended |
| D2 — one per-run temp folder per suite, named `testsToDelete-coai-run-…` | **this plan** (E2) | leftovers-E5's sweep (S2) collects a killed run's folder by owner liveness; leftovers-E5's D5 follow-up ("the product scratch root is a seam") is not needed — D2 takes that slice without a product seam |
| Renaming every test folder to `testsToDelete-coai-…`, the claim marker, the guard | leftovers-E5 | this plan renames nothing; D6 only makes seven sites remove what they made |
| C12 — the ten-minute age sweep can reach a live run | leftovers-E5 (owner liveness replaces age) | D2 removes the exposure until leftovers-E5 lands |
| C8 — the security lane's two equal deadlines | [PLAN_two_equal_deadlines_race_on_macos.md](PLAN_two_equal_deadlines_race_on_macos.md) | this plan orders it (§9), adds the Windows evidence and the step-3 correction (C8), and shares D3's clock |
| `CadenceStoreTests.ManyClosesAtOnce_AllLand` — `SessionTurn.Take` counts tries | [PLAN_a_turn_waits_by_the_clock.md](PLAN_a_turn_waits_by_the_clock.md) | this plan orders it (§9) and adds the CI evidence (§3) |
| C6, C13, C14 — the three deadline tests of `PLAN_question_consultant_tails.md` §6 | **this plan** — moved here on 2026-10-19 | tails §6 now points here; its DoD line goes with it |

Disjoint: nothing else in this plan is built by another plan, and nothing in those plans' other sections is built here.
Order: E1 (C1) first — it is the case that already changed a result across runs; the two owned plans may run beside
this plan's E3–E4 at any time.

## 8. Scenarios — each with the RED it must show first

Every RED is watched failing against today's code with the message below (or, where it says so, the reason no
deterministic red exists), then watched passing; both observations go in the PR (`.agents/conventions/common/testing.md`).

| # | Scenario (test name) | Case | RED today — the message that proves the cause |
|---|---|---|---|
| S1 | `ASteeringFileAnotherRunLeftInTheUsersTemp_NeverSteersThisRunsFake` — a decoy in a PRIVATE temp, the fake started with `TEMP`/`TMP`/`TMPDIR` pointed at it | C1 | the fake answers `decoy` and exits 7 |
| S2 | `ASteeringFileBesideTheBinary_SteersTheFake` | C1 | does not compile / the fake ignores the file (today it reads only temp) |
| S3 | `ARepairTest_SeesOnlyItsOwnRoundsRepairFolder` — a non-empty `coai-repair-decoy` in the temp folder the test sees | C10 | `Expected … to be empty` on the decoy's folder |
| S4 | `EveryClassThatSetsTheProcessEnvironment_IsInTheSerialCollection` (a source scan) | C11 | names `ARequestFaultIsNotAnOldBinaryTests.cs` and `VibeRuntimeTests.cs` |
| S5 | `ATestRun_KeepsItsTempInsideItsOwnFolder` — the fixture's folder exists, `Path.GetTempPath()` is inside it, a product round's `coai-answers-` lands in it | D2 | `Path.GetTempPath()` is the machine's temp folder |
| S6 | `AFullRunLeavesNothingOutsideItsFolder` (a measurement, §10) | D2, D6 | today: ≈3,700 new `coai-*` folders and the loose prompt files after one `src_mcp` run |
| S7 | `ARoundRemovesItsEmptyRepairAndWorkspaceFolders` | D4 | both folders still exist after the round |
| S8 | `AChildThatExitedBeforeItsStartTimeWasRead_IsNotReportedAsNotStarted` (the start-time seam throws the macOS `Win32Exception` after the child exited) | C2 | the launch throws `Win32Exception` "Unable to retrieve the specified information about the process or thread" |
| S9 | `AChildStillAliveWhoseStartTimeCannotBeRead_IsKilled_AndTheErrorReachesTheCaller` | C2 | green today — the guard that the fix keeps story 6.1a's rule; watched red by breaking it once |
| S10 | `ASettingsFileHeldOpenByItsWriter_IsNotReadAsDefaults` (Windows) | C14 | the second `host.Current` shows the defaults, published under the new stamp |
| S11 | `AStubDisposedWhileItsHandlerWrites_WaitsForTheHandler_AndDoesNotThrow` | C16 | `Dispose` returns before the handler ends |
| S12 | the AcpClient usage test on a manual clock + a fake agent with `startDelayMs` longer than the deadline | C5 | `found AcpEnd.Deadline` (the CI message) |
| S13 | `RoundDeadline.For` pinned (8 s with three follow-ups, 2 s with none) + the end-to-end test on a manual clock, 700 ms per turn | C6 | with a one-turn budget put back: the conversation is cut at turn 3, every run |
| S14 | the three turn tests and the resolver test on a manual clock | C13 | with the cap at `TurnLoop.cs:44` removed: no terminal `TimedOut` |
| S15 | `AServerThatNeverAnswers_…` on the launcher's manual clock + `startDelayMs` past the deadline | C4 | the pid file is missing (today's test) |
| S16 | the copy budget on a manual clock; the slow root advances it | C17 | `0 of 2 copied` when the fast copy is slowed past the budget in today's test |
| S17 | the probe released by the shim's first busy answer; and a probe never released ends in exit 69 | C3 | no deterministic red for the race; the loop of §10 before (failures) and after (none) |
| S18 | the heartbeat seen through the store's `projected` callback, the row released after three beats | C18 | with the heartbeat loop removed: fails at the 30 s guard with its sentence |
| S19 | the follow-up launch's timeout asserted directly | C15 | with `RetryLadder.Lesser(left, next)` replaced by the whole budget: launch 2's timeout is 8 s |
| S20 | `replayingFan` CPU bound at 40,000 values | C9 | with `held = [...held, value]` put back: CPU time in seconds |
| S21 | the security lane's per-file timer fired BEFORE the collection timer on a manual clock | C8 | `not served: src/B.cs — timed out: git did not answer within 30 s` — built by its own plan |
| S22 | `AZombie_IsGone_NotSignalled` with the kill after the parent reads `comm == sleep`; the red arm through `bash … sleep 2; exec sleep 600` | C21 | `the outer sleep never reaps, so the killed child should have stayed a zombie` (the CI sentence), every run |

## 9. Build order — the shared-state fixes first

1. **E1 — C1, then C10 and C11.** C1 is the case that already changed a result across two runs; C10 and C11 are the same
   class and small. Nothing else in this plan should be measured while two runs can still see each other.
2. **E2 — D2, D6, then D4.** The per-run folder makes every later measurement clean (a run's leftovers are countable as
   one folder) and ends C12's exposure. D6's seven sites are one-line fixes; D4 is the one product change of the epic.
3. **E3 — C2, then C14.** C2 removes the largest single source of macOS red after C8 (17 runs plus C2's two siblings);
   C14 is a product defect with a deterministic red.
4. **E4 — the D3 clock seam, then C5, C6, C13, C4, C17 on it.** One story lands the seam in every place D3 names with no
   behaviour change (proved by the existing suite), the next ones move each test onto it.
   [PLAN_two_equal_deadlines_race_on_macos.md](PLAN_two_equal_deadlines_race_on_macos.md) (C8) builds on the same seam
   and goes right after E4's first story; [PLAN_a_turn_waits_by_the_clock.md](PLAN_a_turn_waits_by_the_clock.md) (C19)
   needs no seam and may go at any time.
5. **E5 — the tests that wait for an event: C16, C3, C18, C15, C9, C21, then C7's measurement.** Independent of each other
   and of the clock seam, so E5 starts right after E1 and runs beside E2–E4 (§ Epics); C7 waits for a kept failure (D5) or ends with a recorded measurement of its slow step.

## 10. Test plan

```bash
dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4
node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*<Class>"
node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe   # the whole suite
node .agents/conventions/tools/low-priority.mjs ./src_server/tests/bin/Debug/net10.0/CoaiServer.Tests.exe
cd src_vs_code && npm test
```

- **Per scenario:** RED with the §8 message, then GREEN (S9 is the one that is green first and is broken on purpose).
- **Per timing case, the loop:** the case's test 20 times in a row while a whole-suite run goes on beside it, before the
  fix (record how many failed) and after (0 of 20). For C3, 100 times, because its CI rate is low.
- **Per shared-state case, two runs at once:** two whole `src_mcp` runs from two worktrees started within a minute of each
  other, both green, after E1 and E2 (the situation that produced C1).
- **The temp measurement, before and after E2** (read-only counts, by prefix, with the same script as §5): one whole
  `src_mcp` run and one `npm test` on a machine where nothing else runs; after E2 the count of `coai-*` entries and of
  loose `api-*`/`local-*` files in the real temp folder is unchanged by the run, and the run's own folder is gone.
- **CI:** after each epic merges, the census script of §3 re-run over the next 20 runs of each leg; a case is closed when
  its test is absent from them.
- Every suite green on Windows, Linux and the macOS CI leg with the same test count before and after each epic.

## 11. Growth

- **The per-run folder (D2)** — one per suite run, holding what that run makes (≈3,700 product folders and their files
  for a whole `src_mcp` run; tens for a filtered run). **Retired by the run itself** at the end. **Interrupted:** a killed
  run leaves exactly one `testsToDelete-coai-run-…` folder; leftovers-E5's sweep collects it when its owner is gone, and the
  family janitor (`.agents/conventions/common/test-leftovers.md`) after an hour until leftovers-E5 lands.
- **D4** removes growth: two folders per real round that today wait six hours.
- **The steering files (D1)** — one file per test project output folder, deleted by the writer; a killed run's file is
  deleted by the next run's assembly start.
- Nothing else here writes anything that persists.

## Definition of Done

- [ ] No test reads or writes a fixed name in the machine's shared temp folder; the steering files live beside the
      fake's binary; S1–S4 RED then GREEN.
- [ ] Two whole `src_mcp` runs from two worktrees at once are both green.
- [ ] Each suite runs inside its own temp folder and removes it; the measurement of §10 shows the real temp folder
      unchanged by a whole run (`src_mcp` and extension); S5–S7 RED then GREEN.
- [ ] A round removes its empty `coai-repair-` and `coai-noworkspace-` folders (D4).
- [ ] C2, C14, C16 fixed with their deterministic REDs (S8, S10, S11) and S9 kept green.
- [ ] Every deadline a test must race is armed through `TimeProvider`; the product's default behaviour is unchanged (the
      whole suite green before the test moves); S12–S16 RED then GREEN.
- [ ] C3, C9, C15, C18, C21 wait for an event or measure CPU instead of wall time; their loops recorded before and after.
- [ ] C7 either fixed from a kept failure or closed with the recorded measurement of its slow step.
- [ ] C8 and C19 built by their own plans in the order of §9; C6, C13, C14 closed here and removed from
      `PLAN_question_consultant_tails.md` §6.
- [ ] Every case of §3 absent from the next 20 CI runs of its leg.
- [ ] No assertion weakened, no test skipped, no timeout lengthened without the measured cost that justifies it — said
      per case in its PR.
- [ ] `research/module_tests.md` records D1, D2, D3 and the per-run folder; the "OPEN shared-state investigation" note
      (`research/module_tests.md:4475`) is closed with a link here; `shared/temp-sweep.json`'s `why` names D2.
- [ ] Promotion per `.agents/conventions/common/planning-docs.md`, then `node .agents/conventions/tools/plan-lifecycle.mjs`.

## Epics

Five epics, in build order. E = 5, so the cornerstone quota is Q = ⌈2·5/5⌉ = 2. **Split confirmed by Fable,
2026-10-19**, after the question consultant was asked what is important (consult `6bb7f4dbc72341acb4af00ccf9886424`: two
rows answered, both naming the per-run folder and the clock contract). Fable named the two AREAS below; Opus marks the
concrete stories `model: fable` when it writes each epic's stories document
(`todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp_E<n>.md`, written when the epic starts), and that commit raises
the count.

Cornerstones: 0 of 2 — the per-run temp folder (E2), the clock contract (E4)

**Why these two and not E1.** A cornerstone is where a mistake compounds and the next step does not catch it. D1 (E1) is
one path constant, one helper, two readers and a deterministic RED on every OS — a wrong D1 fails S1 at once. D2 (E2) is
a lifecycle: if the assembly fixture runs after a class fixture or a static initializer has read `Path.GetTempPath()`,
half the run lands outside the folder and every later measurement (S6, the two-runs proof, the CI census) reports clean
when it is not. D3 (E4) touches every launch path in the product; a `Stopwatch` left beside the injected timer, or a
`CancelAfter` on a linked token source that still uses the system timer, is the same race again while the tests on the
manual clock go green. *Weighed and not changed:* E2 before E1 (one consultant's view — D2 isolates runs only while every
launch keeps the `TEMP` allowlist, and C1 already changed a result, so D1 goes first); E4 before E3 (C14 needs no retry
loop and C2's seam is small — landing it first is cheaper than rebasing it onto the refactor); "E1 and E2 are grab-bags"
(each has one goal — two runs cannot meet; one folder per run — and every case in it serves that goal).

### E1 — No run sees another

**Goal.** Two full `src_mcp` runs from two worktrees on one user cannot change each other's result. Nothing else in this
plan is measured before this epic is merged (§9).

**Scope.** C1/D1: `FakeCliSteering.MinimalFile` beside the fake's binary; `MinimalSteering.Load`
(`src_mcp/tests_fakecli/Program.cs:507`) and `AcpAgent.SteeredScript` (`AcpAgent.cs:101`) read it; the six
`SteeringFile` copies go; `Write(pairs)` returns an `IDisposable`; the assembly start deletes a file a killed run left; a
source scan fails on any other `fakecli-minimal.json` literal. (The app-server file keeps its private-temp pattern, C1.)
C10: the shared-temp assertions become paths inside the test's own folder; the repair test asserts on what the repair
launch was GIVEN, never on the folder after the round (D4 removes it). C11: the two parallel environment writers join a
serial collection; the S4 guard. Scenarios S1–S4.

**Depends on.** Nothing. Its assembly-start step lives in the one `[assembly: AssemblyFixture]`
(`src_mcp/tests/TempDirsAreSwept.cs:5`) that E2 extends — E1's change lands first, and both stories documents say so.

**Done when.** S1 red (`decoy`, exit 7) then green; S2 green; S3 red on the decoy folder then green; S4 red naming the two
classes then green; the two-runs proof of §10 green on both runs; no test reads or writes `fakecli-minimal.json` under
`Path.GetTempPath()`.

**Size.** Four stories for Sonnet coders. S1 the helper, the two readers, S1/S2 and the assembly-start delete (first).
S2 the six scenario classes onto the helper (after S1; all six are in `fakecli-env`, so one coder). S3 C10's sites and S3
(parallel with S1). S4 C11 and the S4 guard (parallel with S1). No cornerstone.

### E2 — One temp folder per run — cornerstone area: the per-run temp folder

**Goal.** A whole `src_mcp` run and a whole extension run leave the machine's temp folder as they found it; everything a
run makes sits in ONE folder the run removes, so no other run's sweep (C12) and no glob over "temp" (C10) can see it.

**Scope.** D2: the C# assembly fixture makes `testsToDelete-coai-run-<6>` and points `TEMP`/`TMP`/`TMPDIR` of the test
process at it, restores and removes it at the end; `src_vs_code/scripts/run-tests.mjs` does the same for its `node
--test` children and removes the folder BEFORE `process.exit`. D6: the seven leaking sites remove what they made. D4: a
round removes its own `coai-repair-` (`RosterBuilder.cs:282`) and `coai-noworkspace-` (`RosterAssembly.cs:42`,
`SecurityRoster.cs:154`) when it ends — after every child that used them is gone, on success, failure and cancellation,
by the recorded path. The S6 measurement before and after. Closes C12's exposure until leftovers-E5 lands.

**The cornerstone area** is D2's fixture and runner: the order proof (the assembly fixture is constructed before every
class fixture and static initializer that reads the temp path, and disposed after the last — a `Path.GetTempPath()`
recorded from a class fixture says so), the inheritance proof (a product round's `coai-answers-` lands inside the folder
through the allowlist, `ProcessEnvironment.cs:48-52`), the restore on every exit path, the path budget (Kimi 260, Vibe
102 — those classes run first), and the one folder a killed run leaves. Fable writes this spec; Sonnet codes; Opus
reviews.

**Depends on.** E1 (the same fixture; and D4 only after E1's C10 rewrite asserts on the launch, or D4 turns it red).

**Done when.** S5 red (`Path.GetTempPath()` is the machine's folder) then green; S6 shows the real temp folder's `coai-*`
and loose `api-*`/`local-*` counts unchanged by a whole `src_mcp` run and a whole `npm test`, and the run's folder gone;
S7 red (both folders still exist) then green; every suite green on Windows, Linux and macOS with the same test count;
`shared/temp-sweep.json`'s `why` names D2.

**Size.** Five stories. S1 the C# assembly fixture, S5, the two proofs and the path-budget check (the cornerstone, Fable
spec; first). S2 the extension runner and its exit-path proof (parallel with S1; the same spec covers it). S3 D6's seven
sites (parallel). S4 D4 and S7 (product change; after S1; the brief names where a round ends). S5 the S6 measurement
before and after, recorded in `research/module_tests.md` (a scout gathers, the caller records).

### E3 — Product defects the tests found

**Goal.** Two product defects that show up today as "flaky" tests are fixed with deterministic REDs; no assertion changes.

**Scope.** C2: one helper reads `process.StartTime` (`ProcessLauncher.cs:311`); when the read throws AND the child has
exited, nothing is recorded and the launch goes on; when it throws while the child is alive, today's rule stays (kill,
wait, rethrow); the `ARoundThatFoundNothing` test asserts "all reviewers answered" first. C14: `SettingsFile.Read` tells
"missing" from "busy, torn or malformed", with the start and reload behaviours C14 states; the sweeper test passes its
own homes. Scenarios S8, S9, S10.

**Depends on.** E1 (the measurement rule). C2 lands BEFORE E4 touches `ProcessLauncher`, so E4 rebases on it.

**Done when.** S8 red (`Win32Exception` "Unable to retrieve…") then green on every OS; S9 green, watched red once by
breaking the guard; S10 red (defaults under the new stamp) then green on Windows; the three C2 tests absent from the next
20 macOS runs.

**Size.** Two stories, parallel: S1 C2 (the launcher seam, S8, S9, the EndToEnd assertion; the brief pins that an exited
child's later `Forget` logs nothing and the Team server's reader marker path is unaffected); S2 C14 (`SettingsFile.Read`,
S10, the homes fix). Product code: Opus brief, Sonnet code, Opus review. No cornerstone.

### E4 — Deadlines on an injectable clock — cornerstone area: the clock contract

**Goal.** Every deadline a test must race is armed through a `TimeProvider`; the product's default behaviour is unchanged;
the tests that race a deadline advance a manual clock when the thing they mean has happened, and no fixed sleep decides
an outcome.

**Scope.** D3's seam in `ProcessLauncher` (`:401`, keeping the deadline apart from the drain grace `:437` and caller
cancellation apart from timeout), `AcpClient` (`:119`, `:195`), `RoundEngine` (`:286`), `TurnLoop` (`:41-45` — the
`Stopwatch` goes — and `:134-138`), `SourceResolver` (`:200`), `ConsultantPlaces` (`:298`); one manual clock with timers,
monotonic timestamps and disposal, shaped on `wsl_care · src_daemon/tests/WslCare.TestSupport/ManualTimeProvider.cs`
(the suite's `FixedClock`/`MovableClock` stay for wall-time-only tests). Then C5, C6 (with the pure `RoundDeadline.For`
test), C13 (an in-process launcher stand-in, on the pattern of `TwoDecidedThenReal`), C4 (`AServerThatNeverAnswers…`),
C17. Scenarios S12–S16. `PLAN_two_equal_deadlines_race_on_macos.md` (C8, S21) builds on the seam right after the first
story.

**The cornerstone area** is the seam's CONTRACT: (1) every elapsed calculation, delay and cancellation on one path reads
the same clock — the spec lists every `Stopwatch`, `DateTime.UtcNow`, `Task.Delay`, `CancelAfter` and `WaitAsync` on the
six paths and which clock each reads after; (2) `CreateLinkedTokenSource` + `CancelAfter` has no `TimeProvider`
overload, so the timer becomes `new CancellationTokenSource(delay, timeProvider)` linked in, and
`WaitAsync(timeout, timeProvider)` replaces `WaitAsync(timeout)`; (3) the launcher keeps its three separations; (4) the
manual clock fires timers in order and never runs a callback under its own lock; (5) the whole suite is green on the
seam alone before any test moves. Fable writes this spec; Sonnet codes; Opus reviews.

**Depends on.** E1 (the measurement rule) and E3's S1 (same file, lands first). Not on E2.

**Done when.** The seam story: the whole suite green with the same test count and no test moved. Then S12 red
(`found AcpEnd.Deadline`), S13 red (cut at turn 3 with a one-turn budget), S14 red (no terminal `TimedOut` with the cap
removed), S15 red (pid file missing), S16 red (`0 of 2 copied`) — each then green; the loops of §10 recorded per case; C4,
C5, C6, C13, C17 absent from the next 20 CI runs of their legs; no timeout lengthened.

**Size.** Six stories. S1 the manual clock and the seam in all six places, no behaviour change (the cornerstone, Fable
spec; ONE coder — the contract is one thing). Then S2 C5, S3 C6, S4 C13, S5 C4, S6 C17 — parallel after S1, different
test files. C8's own plan starts after S1 merges.

### E5 — Tests that wait for an event

**Goal.** Seven tests stop measuring the scheduler: each waits for the event it means, or measures the cost it means
(CPU), and the clock leaves the outcome.

**Scope.** C16 (`ApiEndpointStub.Dispose` waits for in-flight handlers, bounded, with a sentence), C3 (the probe released
at the shim's first busy answer through a seam beside `BeforeTheHomeHold`, the shim's take staying INHERITABLE), C18
(beats seen through the store's `projected` callback, the row released after three), C15 (launch 2's recorded timeout
asserted directly), C9 (`replayingFan`'s CPU bound; the real-child test keeps exit 0 and every character), C21 (the kill
only after the parent's `comm` reads `sleep`), C7 (a kept failure per D5, else the slow step sized from a measurement).
Scenarios S11, S17, S18, S19, S20, S22.

**Depends on.** E1 only. Nothing here needs the clock seam, so E5 may run BESIDE E2–E4; its number is 5 because its
stories are the smallest value each.

**Done when.** S11, S18, S19, S20, S22 red with their §8 sentence then green; S17's 100-run loop before and after recorded;
C7 fixed from a kept stack or closed with the recorded measurement; C3, C9, C15, C16, C18, C21 absent from the next 20 CI
runs of their legs; C9's and C15's PRs each say why the new assertion is at least as strong as the old.

**Size.** Seven stories, all parallel, one coder each, different files. C7 starts as a scout (collect the failure or
measure the step) and becomes a coder story only if a cause is shown. No cornerstone.

### Order, and what runs beside what

E1 → E2 → E3 → E4 in series for their first stories; E5 starts after E1 and runs beside the rest.
`PLAN_two_equal_deadlines_race_on_macos.md` (C8) goes right after E4's S1; `PLAN_a_turn_waits_by_the_clock.md` (C19)
needs no seam and may go at any time, calling `HeldFile.TakeWithin` rather than writing a second wait. Nothing is
reported closed until its test is absent from 20 CI runs of its leg after the merge (§10).
