# PLAN — the suite is deterministic and leaves no temp

> Status: **in progress, 2026-10-21.** E1 (no run sees another) and C2 (E3 S1) are **merged** in #893
> (`e86fe17f8`, 2026-10-20); **E2 (one temp folder per run) is merged** in #897 (`215170c4b`, 2026-10-21); **E3 (product defects) is coded and reviewed** (gate and judge folded), in its pull request; E4
> is coding (its stories document); E5 is open; the §10 CI watch of E1 and C2 is running. Scope: the fake CLI's steering files
> (`src_mcp/tests_fakecli`), the fixed-name temp paths of the C# and extension tests, the flaky cases found in CI and
> locally between 2026-09-26 and 2026-10-19 (product code where the cause is in the product), and the `coai-*` folders
> the suites leave in `%TEMP%`. The owner put this "tail" at the very end of the queue; it was written so the causes
> are on paper while the evidence is fresh. **Plan gate (session `f6808c2f`, verdict proceed), the Opus judge, the
> cadence and risk consults, and the E2 plan round (session `8fd6b7ff`, verdict good_enough) folded** — what was taken,
> what was refuted and why: § *Review record*.
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
2. Every case in §4 is closed: its cause is fixed in the product or in the test, and **what closes it is its
   deterministic RED turned green** (or, for a scheduling race that has no deterministic red, its recorded loop before
   and after). After the fix merges the case is **WATCHED** for 20 CI runs of its leg: a failure inside the watch
   reopens the case with its kept log (D5); a clean watch closes nothing, because it proves nothing (§10, J15).
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
| `APromptFileDoesNotOutliveItsLaunchTests.AFileAlreadyGone_IsNotAFailure` | **4** (macOS) | 4 (3), `main` 10-19 (run 39229540975) | 10-16 – 10-19 | C2 | product | this plan |
| `AUsageTheVendorNeverReportedIsUnknownTests.An_api_call_whose_deadline_struck_…` | **4** (Linux) | 3 (3), `main` | 10-03 – **10-19** (run 39278975228, still active) | C16 | shared state in the stub | this plan |
| `KimiCredentialsLockTests.AShimThatMeetsADisposalsProbe_…` | 2 (Windows) + 3 work-in-progress | `main` 10-18 | 10-16 – 10-18 | C3 | timing | this plan |
| `EndToEndTests.ARoundThatFoundNothing_SaysWhatItSent_…` | 2 (macOS) | 2 (2), `main` | 09-26 | C2 | product | this plan |
| `AcpClientUsageTests.AUsagePromptThatOutlivesTheTurnsDeadline_…` | 1 (Windows, run 39269879269) | dependabot | 10-19 | C5 | timing | this plan |
| `AConversationPlaceTests.CopiesThatOutrunTheRowsBudget_…` | 1 (Windows) | dependabot | 10-15 | C17 | timing | this plan |
| `QuestionFanOutTests.TheHeartbeatIsRewritten_…` | **3** (Windows) | 2 dependabot (2), and #893's branch (run 39325900821 attempt 1, `UnauthorizedAccessException` from the heartbeat's write) | 10-16 – 10-19 (run 39253800395, 2.9 s) | C18 | timing + product | this plan (E5; the product angle E3) |
| `ConsultAntigravityDenialScenarioTests.AHungFollowUp_…` | 1 (Windows) | a branch that does not touch it | 10-16 | C15 | timing | this plan |
| `AReaderIsEndedWithItsTreeTests.TheDescendantsOfALiveServer_…` | 1 (Windows) | `fix/flaky-size-deadline-test` | 10-16 | C4 | shared file, fixed by #849 | — |
| `PidfdLayerTests.AZombie_IsGone_NotSignalled` | 1 (Linux, run 39276863927, PR #868) | a store PR | 10-19 | C21 | timing inside the test | this plan |
| the job `extension · a real editor` — the editor exited before the host ran a scenario (run 39282963558 attempt 1, PR #888) | 1 (Linux; the job is `continue-on-error`, so the run stayed green) | a docs branch (1) | 10-19 | C22 | timing in the editor's own start | this plan |

**Seen only in local whole-suite runs** (no CI failure in the 174 logs): the steering-file collision (C1), the three
`AReviewerThatAsksForSourceIsAskedAgainTests` budgets (C13), `AFeatureIsReviewedEndToEndTests.TheRoundsDeadline_…` (C6),
`AReaderIsEndedWithItsTreeTests.AServerThatNeverAnswers_…` (C4), `ConsultationSweeperTests` (C14),
`SqliteMigratorTests` (C7), the extension's `processLauncher.test.ts` "many lines" test (C9) and
`SessionStoreConcurrencyTests.EightWritersOnOneSession_DoNotDenyEachOther` (C25, 2026-10-20, in E2's full run and once in
another plan's baseline run). **Found by reading, not by a failure:** C10, C11, C12.

The macOS leg is not a required check, so auto-merge passes over its red — which is how C2 and C8 lived for three
weeks: 54 of the 174 failed runs hold one of those two tests on that leg, and 60 hold one of them on any leg.

Not flaky, and therefore not in this plan: `teamServerTab.test.ts` "the Settings sheet is byte-identical …" (two runs on
`docs/promote-every-round-is-counted`, 2026-10-19) failed on two commits of that branch and passed after its commit
`753d7823b` "re-pin the Settings sheet for the plan's new path" — a pinned hash that a moved plan changes, the same on
every run of one commit; and the three `release-please` changelog tests, which check release state, not timing.

## 4. Each case, with its cause

Every `file:line` below was read on `origin/main` `ea9ccd1c8` (2026-10-19) and re-checked on `38ecd4e11` (#887, the same day) for
the files that commit touched — `ConsultantPlaces.cs` among them (C17's lines moved; re-read in full). The review folds of
2026-10-19 re-read every line they cite on `38ecd4e11`. **Class** is SHARED STATE (two runs or two
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
**A third, structural (gate finding D1):** inside one run the file is safe only while EVERY writer is in the serial
`fakecli-env` collection, and nothing checks that today. S2b is a source scan over `src_mcp/tests`: every class that calls
the steering helper (`FakeCliSteering.Write`/`MinimalFile`) must carry `[Collection("fakecli-env")]` — judged on the
concrete class, like S4, so a base class's call counts through each derived class. It is green on the six classes today
and goes red when one of them (or a seventh) leaves the collection; it is watched red once by moving one class out.

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

**Fix.** Read the start time through one helper that catches BOTH things `Process.StartTime` throws for a child that is
gone — `Win32Exception` (macOS, the text above) and `InvalidOperationException` (.NET's own "process has exited" /
"no process is associated", the Linux and Windows spelling). When the read throws AND the child has ended (`HasEnded`: `WaitForExit` for up to 100 ms, because one `HasExited` read at the moment of exit can still say false — code gate, 2026-10-19), there is
nothing for the orphan sweep to find, so **nothing is recorded**: `IProcessTracker.Record(pid, startedUtc, label)`
(`src_mcp/runners/Processes/ProcessTracking.cs:10`) is not called, and the launch goes on to its normal result. **What
the reader marker then holds** (the Team server's tracker writes `ReaderMarkerDto`, `ReaderMarker.cs:65-70`): it stays
`spawning` (`ReaderPhases.Spawning`, `:33`) — exactly the state it is in today between `Process.Start` and `Record` — and
is cleared by the launch's normal end, the same path a recorded child's marker takes. **That window fails closed, and the
first draft of this paragraph claimed the opposite (corrected at the code gate, 2026-10-19):** a `spawning` marker with no
live child does NOT reconcile as "nothing to kill" — `ReaderMarkerDto.Unidentified` is true for every phase but Running
(`ReaderMarker.cs:76-77`) and `ReaderReconcile.RunAsync` returns `Blocked(ReaderIdentityMissing)`, a slot that needs a
person. A server that crashes inside the window blocks the slot until someone acts; it is the SAME window that exists today
between `MarkReaderExpected` and `Record`, and the launch's own end (`GaugeCapture.ReadAsync`'s `ClearReader`) is what
closes it in every case where the server survives. The brief pins both halves with a tracker stand-in: no `Record`, and the end-of-launch call made.
When the read throws while the child is ALIVE, today's rule stays: kill, wait, rethrow (the remarks at `:300-306` say
why); `ITreeSupervisor.StartedUnidentified(int pid)` (`ITreeSupervisor.cs:21`) is already the verb for a pid with no
start. No assertion changes.

**RED first.** A seam on the launcher for the start-time read (`Func<Process, DateTime>`, default `p => p.StartTime`), shipped as an `internal` constructor overload — the public constructor keeps its one `IProcessTracker?` parameter (code gate, 2026-10-19).
Test 1 (S8), **two arms**: the seam waits for the child to exit, then throws (a) the macOS `Win32Exception` and (b) an
`InvalidOperationException`; `FakeCli emit hello` with a `TrackAs` must end with exit 0 and `hello`, nothing recorded,
on both arms. Today both throw — red on every OS, every run. Test 2 (S9), the guard: the seam throws while the child
(`sleep 20000`) is alive — the exception still reaches the caller and the child is gone. **Confidence: high** on the
class (one message, always an instant child, only macOS; a 4th failure landed on `main` on 2026-10-19, run 39229540975,
`Expected type to be ReviewerOutcome+Ok, but found ReviewerOutcome+NotStarted`); certain once the stack is seen, which
Test 1's message will show.

**Outcome (2026-10-19, branch `feat/suite-c2`, E3 S1).** Built as written. `ProcessLauncher` takes a second constructor argument
`Func<Process, DateTime>? startTimeOf` and reads the start time through `TryStartedAt`: `Win32Exception` or `InvalidOperationException`
AND `HasExited` → nothing recorded, the launch collects the child's result, `Forget` still runs in `RunAsync`'s `finally`; the
same exceptions with the child alive → kill, wait, rethrow, as before (`StartedUnidentified` was NOT needed: that verb belongs to the
ACP transport's supervisor, and `RecordedAsync` already ends a live child itself). RED: `AChildThatExitedBeforeItsStartTimeWasReadTests`
(S8 at the launcher and through `ReviewerExecutor`, two arms each; S9 two arms) — 4 of 6 red on the unfixed helper (`Win32Exception`,
`InvalidOperationException`, `NotStarted`), S9 green; S9 watched red once by dropping `&& process.HasExited`. `EndToEndTests.ARoundThatFoundNothing…`
asserts `all 2` / `all 6 reviewers answered` first. Docs: `module_runners.md` (the launcher row), `module_tests.md` (a section). Open:
the watch of 20 macOS runs (§10). The code gate's record for this story and E1 is under E1's Outcome ("Code gate (2026-10-19)"); it changed the seam to an `internal` constructor and the
"has exited" decision to `HasEnded`.

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
release its probe at that moment. **Where that seam goes (judge, J13):** on the Kimi lock's call site, never inside
`HeldFile.TakeWithin`/`TakeVerified` — a shared primitive used by every lock in the product. Today the home file is
taken in one call, `take(Path.Combine(root, HomeHoldName), HomeWait)` (`KimiCredentialsLock.cs:209`, `take` being
`Inheritable` → `HeldFile.TakeVerified(path, within, inheritable: true)`, `:175`), and the busy answers live inside
`TakeWithin`'s loop (`HeldFile.cs:173`). The call site splits it: one `HeldFile.TakeVerified` with no wait first; when
that answers `Hold.Busy`, the test-only `WhenTheHomeFileIsBusy?.Invoke(path)` (null in the product, beside
`BeforeTheHomeHold`, `:114`) fires once, then the waiting take runs as today. The product's behaviour is unchanged
(one more open attempt before the same 500 ms wait); `HeldFile` is not touched. The test then proves exactly its
title — the shim met a busy file and waited it out — with no clock in the outcome. A second test pins the other side
with no timer either: a probe that is never released ends in exit 69 after `HomeWait`.

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
  (`research/module_tests.md:4493`); the failure text was not kept. **Fix:** the test's subject is "a server that never
  answers is ended at the deadline, with its tree"; the tree must EXIST before the deadline for that to be tested. The
  test waits for the pid file to name both processes (the fake already writes it whole, `Serving.cs:114-131`) before it
  lets the deadline matter: the launcher's deadline (`deadline.CancelAfter(request.Timeout)`,
  `src_mcp/runners/Processes/ProcessLauncher.cs:401`) would be armed through an injectable `TimeProvider` (D3), and the
  test would advance a manual clock past the deadline only after the pid file is complete. **Gated (judge, J8):**
  `ProcessLauncher` is the one class D3 does NOT touch until this case has a KEPT failure — its deadline, drain grace and
  caller-cancellation separation (`:287`, `:401`, `:437`) is the riskiest seam in the plan, and the evidence for this
  case is a failure whose text nobody kept. So the order is D5 first (the next full-suite failure of this test is kept
  with its message), then the seam WITH its RED in one story; until then the test is not changed. **RED first (when the
  story opens):** a `startDelayMs` on the `serve` verb larger than the deadline makes today's test fail on every run
  with the pid file missing. **Confidence: low** until a failure log is kept (D5 asks every failure in the suite to be
  kept with its full message).

### C5 — an ACP turn's 2 s deadline also pays the agent's cold start (TIMING, high)

**Evidence.** `AcpClientUsageTests.AUsagePromptThatOutlivesTheTurnsDeadline_ReturnsTheAnswerAtTheDeadline_NotAfterTheBound`
(`src_mcp/tests/AcpClientUsageTests.cs:47-58`) failed in run 39269879269 (Windows leg, 2026-10-19, a dependabot npm
bump that touches no C#), the only failure of 11,735 tests, in 2 s 025 ms: `Expected outcome.End to be AcpEnd.Answered …
but found AcpEnd.Deadline … the agent did not answer within the 2s this turn was given`. It also failed in the full local
run of 2026-10-18 (`research/module_tests.md:4493`).

**Cause.** The turn's clock starts right after the child process starts: `deadline.CancelAfter(ask.Deadline)`
(`src_mcp/runners/Acp/AcpClient.cs:119`). The test gives 2 s (`AcpClientUsageTests.cs:53`) and means "the deadline
strikes while `/usage` hangs"; but those 2 s also hold the fake agent's cold start, `initialize`, `session/new`, the
config call and the FIRST prompt. Under the suite's load that took longer than 2 s, so the deadline struck before the
answer and the end was `Deadline` — the product did the right thing for what happened. The test's subject (the usage
bound is clipped to what is left of the turn, `AcpClient.cs:180`, `:189-203`, the bound at `:195`) never got to run.

**Fix.** The turn's deadline and the usage bound read an injectable `TimeProvider` (D3): an optional
`TimeProvider? clock = null` constructor parameter on `AcpClient` itself — the class that owns both timers — defaulting
to `TimeProvider.System`, so the product is unchanged; the shape is `BoundedScheduler.cs:96-102`'s. It lands in the
SAME story as this RED (D3: no all-at-once seam). The test uses a manual clock: it waits until the agent has RECEIVED the second
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
test threads and several agent sessions on one machine. The margin is wall-clock — **and one thing IS shared**, which
the judge found (J4): `PanelService._sweptUtc` (`src_mcp/src/Server/PanelService.cs:1407`) is a STATIC throttle — the
product walks the temp folder at most once per ten minutes per process (`:1424`). `PanelService.ForgetTheLastSweep()`
(`:1440-1444`) resets it, and seven call sites do so from classes that are in NO collection:
`AScratchSweepRunsOnTheConsultationBeatTests.cs:57`, `:112`, `:141`, `:150`, `:167`, `VibeHomesTests.cs:154` and
`TempDirsAreSwept.cs:286`. Each reset means the NEXT `BuildWork` anywhere in the process walks the real `%TEMP%` —
≈0.7–1 s at today's 95,285 entries (§5), ≈3 s at the 2026-09-13 size — and this round's `BuildWork` runs INSIDE its 8 s
deadline (`RoundEngine.cs:285-286` arms it before the round is assembled). That is the most likely reason the recorded
failure saw the deadline strike during setup, and it is why this case fails locally (a big temp) and not in CI (a fresh
runner's temp is nearly empty).

**Fix.** The test's subject is arithmetic plus "the conversation is not cut": (1) a pure test pins
`RoundDeadline.For(… ReviewerTimeout = 2 s, followUps: 3)` = 8 s and `followUps: 0` = 2 s — none exists today (the
callers in `SecurityCoverageTests.cs:72` and `SecurityPresetTests.cs:203` pass zero follow-ups); (2) the conversation
cap and the per-turn timeout read `TurnLoop`'s own injected clock (C13's seam — the `Stopwatch` at `TurnLoop.cs:41-45`
goes), and the end-to-end test advances a manual clock 700 ms per turn instead of sleeping; the round clock at
`RoundEngine.cs:286` is NOT given a seam (D3: the clock is never threaded through `PanelService`/`RoundEngine`) — the
sweep-walk inside it is removed instead: (3) the seven `ForgetTheLastSweep` callers move into a serial collection (S4's
scan grows a second rule: any caller of `ForgetTheLastSweep` must be in a `DisableParallelization` collection), and
under D2 the walk is of the run's own folder. A one-turn budget then cuts the conversation at turn 3 on every run, and
the right budget never does. **RED first:** the same test with `FAKECLI_SLEEP_MS=1900` fails on every run today — it
shows that fixed sleeps against a fixed deadline decide the outcome; and for the shared half, S4's grown scan names the
three classes today. **Confidence: medium** (down from medium-high, on the judge's point) — the recorded message shows
the round clock striking before the first launch, which both the setup-inside-the-deadline path and the sweep reset
explain; keeping the next failure's `answer` JSON (D5) and the sweep's warning line decides between them. This case
moves here from `PLAN_question_consultant_tails.md` §6 (§7).

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
clock: fire the per-file timer BEFORE the collection timer, both past 30 s — red on every OS today. **Who adds which
clock (judge, J14 — written on both sides, the sibling's §Boundary says the same):** the per-file timer is
`SourceResolver`'s (`SourceResolver.cs:200`) and gets its `TimeProvider? clock = null` here, in C13's story (E4), because
C13's resolver test needs it first; the collection timer at `SecuritySources.cs:32` (`CollectionBudget`, `:14`) is added
by **the sibling plan**, with its own RED (S21), on the same manual clock — the sibling's step 1 is the first test that
needs it, and D3's rule is that a seam lands with the RED that needs it. Start: the sibling may start as soon as E1 is
merged (J7); its step 1 is deterministic on the system clock (a per-file read that is made to time out before the
collection budget) and needs no seam, so only its step 3 waits for C13's resolver clock.

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
test stays as a smoke test of the pipes — exit 0 and every character arrived — without a wall-clock assertion, **and
without the `ended` helper's 10 s timer** (judge, J5): that timer (`:22-24`, `nothing arrived within 10000 ms`) is what
actually failed locally, so the smoke test waits on `onExit`/`onError` alone and leaves the hang budget to the runner's
own per-file timeout (`run-tests.mjs`), which names the file. A test that measures nothing by the clock has no clock. The
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
a `TimeProvider` — an optional `TimeProvider? clock = null` constructor parameter on EACH of the two classes (the
class that owns the timer, `BoundedScheduler.cs:96-102`'s shape), landed in this case's own story together with S14;
nothing is threaded through `RoundEngine` or `PanelService`, which construct them with the default. The turn tests run through an in-process launcher stand-in that advances the manual clock by a turn's
simulated duration and answers `TimedOut` when the request's timeout is shorter — the file already has an in-process
launcher precedent (`TwoDecidedThenReal`, `:443`). The resolver test's slow read waits on the clock, which is advanced
only after the fast read was served. **RED first:** with the manual clock, removing the cap at `TurnLoop.cs:44` turns the
cap test red on every run; today's versions are the wall-clock ones. **Confidence: high** for the first (fails alone, the
margin is 300 ms), medium-high and medium for the other two (no failure text kept).

### C14 — `ConsultationSweeperTests`: a settings reload read while the file is half written (PRODUCT, low-medium)

`TheLoop_LapsesAnIdleConsultation_AndKeepsDoingSoAfterASettingsReload` (`src_mcp/tests/ConsultLimitsScenarioTests.cs:155-170`)
failed once in 488 ms in a full local run on 2026-10-03 and passed 15 of 15 alone (`PLAN_question_consultant_tails.md`
§6); no CI failure, no message kept. 488 ms rules out the ten-second waits; it is an assertion on the test thread, most
likely `:165` ("the reload happened"). The class has no collection, so its 50 ms beat runs beside the parallel suite.

**Cause, a hypothesis with a mechanism the code shows.** The test writes `settings.json` with `File.WriteAllText`
(`:164`) while the sweeper thread reads `host.Current` every 50 ms. `PanelServiceHost.Current`
(`src_mcp/src/Server/PanelServiceHost.cs:64-84`) compares a stamp and rebuilds; its own comment says "a rebuild that
throws leaves the old stamp in place, so the next call tries again" (`:76-78`). But `SettingsFile.Read`
(`src_mcp/src/Server/SettingsFile.cs:60-94`) turns an `IOException` — a sharing violation while the writer still holds the
file — into an EMPTY settings set, so the rebuild does NOT throw, and the host publishes DEFAULT settings under the new
stamp until the file changes again. In production that is a server that silently runs on defaults after a reload that
met a busy file. The same `catch` takes `JsonException` (`SettingsFile.cs:90-93`), which is what a HALF-written file
reads as. **Fix (the lead's decision on gate C14 and judge J3):** a **tri-state read on the RELOAD path only.**
`SettingsFile.Read` keeps today's signature and behaviour for its callers — `SettingsFile.Layer`'s eight call sites
(`ProbeApiMode.cs:97`, `Program.cs:646`, `:725`, `:2110`, `CadenceReadMode.cs:32`, `ConsultantCheckMode.cs:47`,
`ConsultantsReadMode.cs:221`, `PanelServiceHost.cs:101`) see no change. A new `SettingsFile.TryRead` answers one of
three: `Read(values)`, `Missing`, or **`Busy`** — and `Busy` is ONLY a sharing violation (Windows HResult
`0x80070020` / ERROR_SHARING_VIOLATION, or an `IOException` the open could not take because another process holds the file
for writing); a **permission denial is never `Busy`** — `UnauthorizedAccessException` (Windows deny-read, Linux `chmod 000`)
keeps today's answer, empty settings with a warning (`SettingsFile.cs:90`), so a file locked down by mistake can never pin
stale settings for ever (cadence consult 589e4647, verified); a malformed file and an empty file are `Read` with no
values, exactly what they are today. S10 gains a denial arm that must NOT keep the last good settings. **One existing
test changes by design:** `PanelServiceHostTests.AReloadThatFails_IsTriedAgainOnTheNextCall_RatherThanHiddenBehindTheNewStamp`
(`:113`) asserts `Current` THROWS; under "Current never throws" it asserts the old settings come back and the next call
still retries — the retry it pins is kept, only the throw goes. `PanelServiceHost.Current` (`:64-84`) uses it on the rebuild: on `Busy` it keeps `_current` AND the old
stamp, warns once per stamp, and tries again on the next call; on everything else it behaves as today. **Why malformed
and empty are not kept-last-good:** a person who writes a broken file did an explicit thing, and today's answer to it
(defaults, a warning) is the answer they can see and fix; a torn write — the writer mid-file — already heals by itself,
because the stamp is `(LastWriteTimeUtc, Length)` (`PanelServiceHost.cs:152-160`) and the finished file has another
length, so the next call rebuilds. Only the busy case has no cure today, and only the busy case is cured. **`Current`
never throws to its callers (gate C14):** `Build(first: false)` is inside a `try` whose catch logs the reason and
returns `_current` under the OLD stamp — the comment at `:76-78` already promises the retry, and this makes the promise
true for a throwing rebuild too. **Blast radius:** `PanelServiceHost.Current` (the 20 `.Current` readers in `src_mcp/src`
go through it) on the reload path only; START (`Build(first: true)`, `:60`) is untouched — a busy file at start still
yields defaults with a warning, as today, a server that refuses to start being worse. Nothing in `SettingsFile.Layer`,
`--providers`, the cadence or consultant read modes changes. Also: the test's `Host()` (`:114-125`) passes no consultant-homes folder, so every beat
sweeps the machine's real per-user folders and walks the real temp folder — `ConsultScenarioBase` documents `homes` for
exactly this; the test passes its own. **RED first (Windows, no timing):** hold `settings.json` open for write with
`FileShare.None` and new content flushed; read `host.Current`; release; read again — today the first read (busy)
shows the defaults under the new stamp; after the fix it shows the last good settings and the second read shows the
new file. Two companions that are GREEN today and stay green, pinning the decision: a malformed file on reload gives
defaults, and an empty file on reload gives defaults (as today). **Confidence: low-medium** — the mechanism is real in
the code; that it is THIS failure is not proven.

**Outcome (2026-10-21, branch `feat/suite-e3-s2`, E3 S2).** Built as decided. `SettingsFile.TryRead` answers a closed union (`SettingsRead`: `Values`, `Missing`, `Busy`; `SettingsRead.cs`); `Busy`
is ONLY Windows HResult `0x80070020`/`0x80070021` (one `private static` predicate); a denial, a malformed and an empty file are `Values` with nothing in them, as before. `SettingsFile.TryLayer` adopts first and reads the file ONCE, and
`PanelServiceHost.Current` uses it on the reload path: `Busy` or a throwing rebuild keeps the last good service AND the old stamp, with one warning per stamp; `Current` never throws; START (`Start()`) is untouched. `Read`,
`Layer`'s public signature and its eight callers are unchanged (no diff in those files). The denial arm uses an `internal` constructor of `PanelServiceHost` (and `Layer`/`Read` overloads) taking the read function. RED: the S10 busy
test (`Expected whileHeld to be "gpt-5.6-terra" … but "" has a length of 0` — the defaults under the new stamp) and `CurrentNeverThrows_…` (`NullReferenceException` out of `Current`); green after; the denial, malformed and empty
companions green throughout; four break-its seen red and restored (busy read as empty; a denial read as busy; no catch around the rebuild; `Kept` publishing the new stamp). The sweeper test's `Host()` passes its own
`COAI_CONSULTANT_HOMES`; the class passed 20 of 20 beside a whole-suite run. Tests: 5 → 10 in `PanelServiceHostTests`; `CoaiMcp.Tests` 13 053 → 13 058. Docs: `module_server.md` (the reload row), `module_tests.md` (a section).
**Deviations.** (1) `AReloadThatFails_…` (`:113`) asserts the old settings instead of a throw — by design (G3); its retry assertion is kept. (2) The text above says a denial keeps "empty settings with a warning
(`SettingsFile.cs:90`)": there is NO warning in `Read` today (it returns `[]` silently), and none was added — the denial answer is exactly today's. (3) `HeldFile.Take` was not reused: it reads EVERY non-path `IOException` as a holder, which is
right for a lock that is taken but would call a failing disk "somebody is writing" on a read; the HResult check is the narrower rule this plan decided. (4) The constructor's build is now `Start()` and the shared tail `Assemble(first, configuration)`
(the old `Build(first)` is gone); `StartupNotices`'s doc `cref` followed. (5) The line references of this section were re-pinned to `adb299b5e`. **Not measured:** the sweeper loop on the OLD code (the original failure had no kept message; S10 is the proof). **Suite-only failure
seen once in a whole-suite run on a machine running five suites at once:** `AConversationPlaceTests.CopiesThatOutrunTheRowsBudget_RefuseThePlace_SayingHowManyWereCopied_AndKeepNone` (the 1 s copy budget ran out before the copy: C17's
mechanism); it passes alone (10 of 10) and is not touched by this story.

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
(2026-10-03, `main`), 37138270645 (10-03), 38063638766 (10-10) and **39278975228 (2026-10-19, a docs branch, 1 s 135 ms
— still active)** with a `NullReferenceException` thrown by the TEST's
`Dispose`, not by its body: `NameValueCollection.GetKey ← HttpListenerResponse.FormatHeaders ← … ← HttpListener.Stop ←
ApiEndpointStub.Dispose`. **Cause:** `Dispose` sets the hold and stops the stub in the same breath (`:40-42`); the set
wakes the stub's serving thread, which starts writing the status, the content type and the headers of the held answer
while `HttpListener.Stop()` on the test thread enumerates the same header collection to close the connection. The stub's
`Dispose` (`src_mcp/tests/ApiEndpointStub.cs:218-228`) catches only `ObjectDisposedException` and
`HttpListenerException`. **Fix:** the stub tracks its in-flight handlers (the `HttpListenerContext` each one holds) and
`Dispose` waits for them (bounded, with a sentence when the bound is reached) before it stops the listener; nothing is
swallowed. **When the bound expires (E2 plan round, C16):** the listener must still never be stopped while a handler
is writing headers — so `Dispose` first ABORTS every in-flight context it still tracks (`HttpListenerResponse.Abort()`,
the call `AnsweredAsync`'s own catch already makes at `:183`; a handler that then throws is noted in the journal, as
every failed answer is), waits for the handlers to leave, and only then calls `Stop()`/`Close()`. The order is
abort → drain → stop, never stop while a handler is active; the sentence names the handler that outlived the bound.
**RED first (two arms):** (a) a stub whose handler is released and then sleeps 300 ms before it writes; `Dispose`
called in that window must return only after the handler finished, and must not throw — today it returns at once;
(b) a handler still active PAST the bound (it blocks on a gate the test never opens until after `Dispose` returned):
`Dispose` returns at the bound with its sentence, the handler's write fails on an aborted response and `Dispose` has
not thrown — today `Stop()` races the writer. **Confidence: high** (the stack names both threads' paths).

### C17 — a one-second copy budget that must also cover git starting (TIMING, medium-high)

`AConversationPlaceTests.CopiesThatOutrunTheRowsBudget_RefuseThePlace_SayingHowManyWereCopied_AndKeepNone`
(`src_mcp/tests/AConversationPlaceTests.cs:114-142`) failed once on Windows (run 38817686874, 2026-10-15): the refusal
said `0 of 2 copied`, the test expects `1 of 2` (`:136`). **Cause** (lines re-read on `38ecd4e11`, after #887 rewrote
`ConsultantPlaces.cs` by 204 lines): the budget is 1 s (`:134`), armed by `copying.CancelAfter(budget)` in
`OneShotAsync(folder, sources, budget, ct)` (`src_mcp/src/Server/Consultation/ConsultantPlaces.cs:329`, `:334`), and
since #887 the flow asks git of EVERY root ONCE, before anything is copied (`:390` — "each source asked of git ONCE,
before anything is copied"): a work-tree check and the link checks per root, then the copies in grant order. On a
loaded Windows runner that git phase passed 1 s, and `copying.ThrowIfCancellationRequested()` (`:416`) reported 0 — the
product's own sentence now says which phase it was (`OutOfBudget`, `:449-457`: "the budget ran out while confirming N
candidates with git, before anything was copied"). The slow root's 3 s sleep (`:123-131`) is not the problem. **Fix:**
D3 — the copy's budget is armed through a `TimeProvider? clock = null` constructor parameter on `ConsultantPlaces`
(the class that owns the timer; landed with this RED, S16); the slow root's hook ADVANCES the clock past the budget
instead of sleeping, so the fast copy runs with no clock pressure and the slow one is cut every time.
**Confidence: medium-high** ("0 of 2" proves the fast copy outlived 1 s; its real cost was not logged — and the
refusal's phase sentence now records it for the next time).

### C18 — the question row's heartbeat counted by polling the file every 40 ms (TIMING, medium; and a PRODUCT angle, high — E3)

`QuestionFanOutTests.TheHeartbeatIsRewritten_WhileARowRuns_OnTheConfiguredInterval`
(`src_mcp/tests/QuestionFanOutTests.cs:686-710`) failed twice on Windows: run 38906020752 (2026-10-16): `Expected
beats.Count >= 3 … found 1`, in **13 s** for a test that takes 1.5 s; and run 39253800395 (2026-10-19, a dependabot
npm bump), the same assertion in **2 s 899 ms**. The second run weakens the first reading — "the process was starved"
fits 13 s, not 2.9 s: at 2.9 s the row's 1.5 s delay and the 200 ms beat still fired late enough, or close enough
together, that fewer than three DISTINCT `HeartbeatUtc` values were seen — a 200 ms timer losing two beats under
ordinary load, not a starved process. Both runs agree on the shape: the count depends on how many timer ticks the
scheduler granted inside a fixed delay. The row still settled `Answered` (`:707` passed): not a product defect.
**Fix:** the store's `projected` callback (`QuestionConsultStore`, called on every write) collects distinct
`HeartbeatUtc` values; the scripted row waits on a gate instead of `Wait = 1.5 s` and is released when three distinct
beats were seen, under a 30 s guard that fails with its own sentence. The test then depends only on "the heartbeat is
rewritten". **RED first:** with the heartbeat loop removed the redesigned test fails at the guard; today's version is the
wall-clock one. **Confidence: medium** on the cause (no timeline in the log), high that the redesign removes the clock.

**The product angle (added 2026-10-20; E3 S4).** A THIRD failure of the same test is not the count: run 39325900821
attempt 1 (Windows, 2026-10-19 21:22 UTC, #893's branch `docs/flaky-tests-and-temp-leak`, which does not touch the fan-out),
2 s 206 ms: `System.UnauthorizedAccessException : Access to the path is denied.` at `QuestionFanOut.Current.UpdateAsync` ←
`HeartbeatAsync` (`QuestionFanOut.cs:309`) ← `LaunchAllAsync` (`:280`) ← `RunAsync` (`:97`) ← the test's `await run` (`:706`).
(The two count failures, 38906020752 and 39253800395, fail at `:708` with `found 1`; their `RunAsync` returned, so no
write threw there — they are the timing case above.) Every line below was read on `68bec6f5f`.
**The write path.** `Current.UpdateAsync` (`QuestionFanOut.cs:476-488`) replaces the record under its gate and calls
`store.Write` (`QuestionConsultStore.cs:51-56`) → `AtomicJson.Write` (`Escalations.cs:341-345`): it takes the file's
`SessionTurn` and then `WriteUnderTurn` (`:355-360`) writes `<id>.json.tmp` and `File.Move(temp, path, overwrite: true)` —
**no retry of any kind**, and a fixed temp name. **Who reads it at the same time.** The test's own poll,
`store.Read` → `RecordFiles.Read` (`RecordFiles.cs:43-54`), which takes the same turn and opens the file through
`SharedRead.Text` (`SharedRead.cs:32-39`, `ReadWrite | Delete`); in production also the extension's watcher, which reads
on every change with `vscode.workspace.fs.readFile` (`src_vs_code/src/jsonDirectoryWatcher.ts:9`) and takes no turn at
all. **Why the turn did not keep them apart.** `SessionTurn.Take` gives up after 40 tries of 2–8 ms
(`SessionStore.cs:464-479`) and BOTH sides then go on without it: `AtomicJson.Write` ignores `turn.Held` (`:343-344`) and
so does `RecordFiles.Read` (`:51-53`) — C19's count, met from the other side (*inferred*: under load one side waited past
its 40 tries; the log does not say which). **Why Windows says "access denied".** Measured on this machine on 2026-10-20
(Windows 11 26300, .NET 10.0.12): `File.Move(temp, path, overwrite: true)` over a file another handle holds open is
refused with `UnauthorizedAccessException`, HResult `0x80070005`, **whatever the holder's share mode** — `ReadWrite |
Delete`, `ReadWrite`, `Read`, `Delete` and `None` all gave the same — so `FILE_SHARE_DELETE` does not make the
replace safe (the repository's own remark says the same: `Escalations.cs:317-324`, "hot readers still starved the
writer"). **The precedent has the same hole.** `AtomicFile.Write(path, text, sharingRetry)`
(`src_mcp/runners/Files/AtomicFile.cs:34-63`) exists for exactly this — "retrying the MOVE when the destination is held
open by a reader" — but its loop catches only `IOException` (`:58`), so the refusal it names is never retried; its three
callers with a 500 ms window (`ConsultHealth.cs:227`, `ConsultantsFile.cs:118`, `ConsultCheckState.cs:321`;
`ConsultHealthPaths.SharingRetry`) have the same latent defect. **What a failed beat does today.** `HeartbeatAsync`
catches only `OperationCanceledException` (`QuestionFanOut.cs:312`), so one refused beat faults the heartbeat task, and
`LaunchAllAsync`'s `finally` rethrows it at `await heartbeat` (`:280`): a question whose rows all ANSWERED fails, and
nothing writes its settled record (`store.Write(settled)`, `:100-101`, never runs). One missed beat is otherwise harmless — the sweep needs a
heartbeat two minutes stale AND a dead pid (`QuestionConsultStore.cs:33-34`, `:180-182`).
**Fix (reuse first, three small changes).** (1) `AtomicFile.MoveOver` retries `UnauthorizedAccessException`
as well as `IOException` while inside its window — the measured refusal; a caller with no window still tries once, and a
real permission denial is rethrown when the window ends. (2) `AtomicJson.WriteUnderTurn` stops open-coding the dance and
calls `AtomicFile.Write(path, content, <the same 500 ms window as `ConsultHealthPaths.SharingRetry`, one constant, not a
second literal>)` — `AtomicFile`'s own remark lists `Escalations` among the "older copies still open-coded"; the GUID temp
also ends the fixed `.tmp` name two turn-less writers would share. (3) `HeartbeatAsync` treats a beat whose write still
fails (`IOException` or `UnauthorizedAccessException`) as a MISSED beat: one warning naming the question and the reason,
and the loop goes on; the settle write (`:297`) and the final write (`:101`) keep throwing, because a lost answer must
surface. Not here: the turn's count of tries (C19's plan) and the extension's reader (it already shows a torn or busy file
as nothing and reads again on the next change). **RED first** (S24, S25): (S24, Windows) the destination held open with
`SharedRead.Text`'s exact open; `AtomicFile.Write` with a window and a pause seam that releases the holder on its first
call — today `UnauthorizedAccessException: Access to the path is denied` on the first move and the pause never called;
(S25, every OS) the store's `projected` callback throws `UnauthorizedAccessException("Access to the path is denied.")` on the
first heartbeat-only write and opens the scripted row's gate — today `RunAsync` throws that exception from `HeartbeatAsync`,
the CI sentence; a second arm opens the gate only on the NEXT beat — today no beat follows the failed one and the row's
guard fails with its sentence. **Confidence: high** on the mechanism (the CI stack, the measured refusal type, the missing
retry and the missing catch are all read or measured); medium on which side went without the turn in that run.

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

### C22 — the real-editor job: the editor exited before the extension host ran a scenario (TIMING in the editor's own start, medium)

**Evidence.** The job `extension · a real editor` (`.github/workflows/ci.yml:390-425`, `continue-on-error: true`, so it
never turns a run red) failed on PR #888 (run 39282963558, attempt 1, 2026-10-19 16:07 UTC, `docs/promote-kimi-vibe-linux`,
a docs-only branch) at its step `Run the scenarios in a real editor` (`xvfb-run -a npm run test:host` → `scripts/run-host.mjs`)
and passed on the rerun (attempt 2). **The first reading of the log was wrong, and the log says why.** `run-host.mjs`
launches the editor TWICE (`:100`, `:106-107`): the ordinary scenarios, then a second launch with a missing data root
that runs ONE scenario. The lines `1/1 scenarios passed`, `Extension host test runner exit code: 0` and
`Error: Unexpected SIGPIPE` all belong to the SECOND launch (pid 2759, 16:08:17–16:08:32), which passed; the SIGPIPE
is VS Code's own, printed by a launch that then exited 0, and is not a failure condition anywhere in the harness. The
FIRST launch (pid 2557) printed NOTHING from the extension host — no `ok`, no `FAIL`, no `N/N scenarios passed`
(`src/test/host/scenarios.ts:984-990` prints one of those for every run that reaches `run()`) — and VS Code's main
process logged `CodeWindow: detected unresponsive` at 16:08:17.073, sixteen seconds after it created the window
(16:08:00.794), then `Exit code: 1`. `@vscode/test-electron`'s `runTests` rejects on a non-zero editor exit, so the
harness recorded `the extension host: Test run failed with code 1` and exited 1 (`run-host.mjs:95-96`, `:109-110`,
`:119-122`) — the step's failure condition, working as designed: "a failure to launch is non-zero" (`:22-26`).

**Cause.** The editor's renderer window did not answer VS Code's responsiveness check in time — Electron's hung-renderer
timer, not any budget of ours — and VS Code, running extension tests from the command line, ends the process with
code 1 when a window is unresponsive (there is nobody to answer its "window is not responding" dialog). Why the renderer
was slow is inferred, not shown: the first launch is the FIRST start of a freshly downloaded, freshly unpacked VS Code
1.142.0 (330 MB, `node-compile-cache … inactive`, in-memory shared storage) on a cold runner under `xvfb`; the second
launch, seventeen seconds later on the same warm install, started and ran its scenario in fifteen seconds. Nothing of
ours ran before the window hung — the extension's activation and the scenarios never printed — so no shared state and
no test of ours is involved; the harness' two budgets (`LAUNCH_MS`, the scenario budget) did not fire.

**Fix (E5; harness only, no product code).** (1) **The failure says what happened.** When the editor exits non-zero and
the extension host printed no scenario line, `run-host.mjs`'s sentence is "the editor exited (code N) before the
extension host ran a scenario — VS Code's last main line: `CodeWindow: detected unresponsive`", not `Test run failed with
code 1` — the harness keeps the editor's stdout/stderr tail (it already has it) and quotes the last `[main …]` line. A
scenario failure keeps today's sentence. That is D5 for this job: the next failure is readable without `gh api`.
(2) **The launch budget measures the launch.** `runTests` downloads and unpacks inside the `bounded(LAUNCH_MS)` race
(`:84-94`); the download moves out — `downloadAndUnzipVSCode` first, its path passed as `vscodeExecutablePath` — so
`LAUNCH_MS` bounds only the editor's start and the sentence for a slow download is a different one. (3) **One retry of
the LAUNCH, never of a scenario — and only when the harness can ESTABLISH that the extension host never started a
scenario** (E2 plan round, C22). "Printed no scenario line" is not that proof: a host that started and hung before its
first `ok`/`FAIL` (`scenarios.ts:984-987`) also prints none. So `run()` prints an explicit **host-started** line
(`  host started: N scenarios`, before the loop at `:981`) that is distinct from scenario completion, and the harness
retries the first launch once, on the now-warm install, ONLY when the editor exited non-zero AND that line was never
seen; the log says so ("the editor did not start the first time; started on the second"). An exit after the
host-started line is never retried — a scenario that failed or hung is reported the first time. This is not a longer
timeout — the budgets stay. (4) **A first-launch failure is never silent:** whatever happens next (retry or not), the
harness emits a GitHub annotation `::warning::the editor exited (code N) before the extension host ran a scenario —
VS Code's last [main] line: …` naming the line, so it shows on the run's summary although the job is
`continue-on-error`. The streak step (`host-job-streak.mjs`) counts those annotations per run of `main` (the jobs it
already lists are check runs, whose annotations the same API serves) and prints the count beside the streak, so the
promotion number says how often the editor needed a second start. **RED first:** S23 — `run-host.mjs` pointed at a
stub `vscodeExecutablePath` that exits 1 at once and prints a `[main …]` line and no host-started line: today's
sentence is `Test run failed with code 1`; after (1) it names the exit before the host and quotes the line, after (4)
a `::warning::` line is in the output, and after (3) the stub is launched twice before the sentence; a second arm — a
stub that prints the host-started line and then exits 1 — is launched ONCE. **Confidence:
medium** on the cause (the unresponsive line and the empty host output are certain; "cold first start" is the only
actor on the timeline, but no CPU or disk figure was kept); high that (1) makes the next failure legible, and
measured — not assumed — that (3) removes it: the streak step's count over the next 20 runs of `main`.

*(C23 is unused; C24 — `SupervisedTeardownLinuxTests`, E5 — is recorded only in the Review record until its log is read.)*

### C25 — eight writers of one session: the in-place write meets a writer that went without the turn (PRODUCT, high on the mechanism)

**Evidence.** `SessionStoreConcurrencyTests.EightWritersOnOneSession_DoNotDenyEachOther`
(`src_mcp/tests/SessionStoreConcurrencyTests.cs:51-75`: eight threads, twelve `Save`s each, every exception collected)
failed once in E2's 38-minute full run on a loaded Windows machine (2026-10-20, `research/module_tests.md:4785`):
`IOException: The process cannot access the file …session-<id>.json because it is being used by another process`; alone it
passed 3 of 3 in 4–7 s. It also failed once in the baseline of another plan's whole run
(`todo/PLAN_a_round_records_who_wrote_the_code_and_how_much_E5.md:1086`). No CI failure; the stack was not kept (D5).
Every line below was read on `68bec6f5f`.

**Cause.** `SessionStore.Write` (`src_mcp/src/Server/SessionStore.cs:566-607`) has two roads. With the turn
(`SessionTurn.Take`, `:583`) it writes IN PLACE — `File.WriteAllText(file, json)` (`:592`), which opens the session file
`FileMode.Create`, `FileShare.Read`, with **no retry**. Without the turn — after 40 tries of 2–8 ms (`:464-479`), which
eight writers queued on one turn under load can use up (*inferred*) — it falls back to a per-write temp and
`MoveOverExisting` (`:599-606`), which retries the MOVE ten times (`:619-639`). So two writers can be on the file at
once: one in place, one moving over it. The only operation in the write path that opens `session-<id>.json` BY NAME is
the in-place write (the move's refusal is `UnauthorizedAccessException`, measured under C18; the temp has its own name),
so the message names the in-place write. **Measured** on this machine on 2026-10-20: one thread writing a file in place
with `File.WriteAllText` beside one thread doing temp + `File.Move(…, overwrite: true)` on the same file, four seconds:
the in-place writer was refused **273** times with `IOException` HResult `0x80070020` (ERROR_SHARING_VIOLATION — the
E2 message) and the mover **337** times with `UnauthorizedAccessException` `0x80070005`. The mover's refusals are
retried; the in-place writer's are not, so the first one reaches `Save` (`:551-564`), which throws
`SessionStoreException` — and in production that aborts the round that was reporting its progress
(`LiveRound.Persist`, the test's own remark at `:12-25`). A scanner or indexer that opens the file for a moment is the
same refusal from outside coai, which no turn can prevent. **Not a test defect:** the test's eight writers in one
process are what five servers sharing a data directory do (the remark at `:12-14`), and its assertion is the product's
promise.

**Fix (reuse first).** The in-place write is retried the way the move already is: `MoveOverExisting`'s loop (ten
jittered attempts, the last one allowed to throw, `:619-639`) is extracted into ONE private helper that takes the step,
and both the in-place write (`:592`) and the move (`:602`) call it — the same exceptions (`IOException` or
`UnauthorizedAccessException`), the same bound, no new number. A failed `File.WriteAllText` open writes nothing
(`FileMode.Create` truncates only after the open succeeds), so a retry cannot tear the file. Not here: the turn's count
of tries — `PLAN_a_turn_waits_by_the_clock.md` (C19) makes the fallback road rarer; this fix is still needed after it,
because a scanner's open is not a coai writer. **RED first (S26, Windows):** with a pause seam on the store (an
`internal … { get; init; }` beside `Retired`, `:487`, whose default is today's sleep), the session file held open
`FileAccess.Read, FileShare.Read` (the share a writer's or scanner's handle grants) while the turn is free, so `Save`
takes the in-place road; the pause releases the holder on its first call — today `Save` throws `SessionStoreException`
whose inner `IOException` says the file "is being used by another process", and the pause is never called. A guard arm,
green before and after: a holder never released — `Save` throws `SessionStoreException` naming the branch, and `Load`
still returns the OLD session whole. **Confidence: high** on the mechanism (the two roads are read, the refusal is
measured with the E2 message's exact text); **medium** that it is the E2 failure (its stack was not kept).

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

**The trade-off the judge asked for (J12), decided:** once D2 lands every run has its own `TEMP`, so a steering file
under `Path.GetTempPath()` — today's location — becomes per-run for free, and D1 would not be needed for the two-runs
case. Two placements, two limits: *TEMP-keyed after E2* fails for two runs of the same bin folder only if they also
share a TEMP (they do not, under D2), but is correct only while every launch path keeps `TEMP`/`TMP`/`TMPDIR` on its
allowlist AND E2 has landed — before E2 it is exactly today's defect; *beside the binary* works before E2 and does not
depend on how any launch builds its environment, and fails only for two runs of the SAME output folder at once (one
checkout, one configuration, started twice — not a way this suite is run, and a rebuild between them is refused while
the first holds its binaries). **Decision: beside the binary**, because E1 must land first and alone (§9) and C1 is the
case that already changed a result; D2 then makes the remaining limit smaller still (a stray copy a killed run leaves
is deleted at the next run's start either way). Weighed: deferring C1 until E2 and keeping the file TEMP-keyed — that
leaves the live collision open for the whole of E2 and ties the fake's correctness to an allowlist.

**D2 — Each suite runs inside ONE temp folder of its own.** *(Cadence consult 589e4647, verified: `TempDirsAreSwept` is
BOTH the assembly fixture (`TempDirsAreSwept.cs:5`) and a test class with eight Facts/Theories (`:62-227`), so a disposal
added to it would run after test instances too. The run folder's restore-and-remove lives in a DEDICATED fixture type
with no test methods, `RunTempFixture`; `TempDirsAreSwept` keeps its sweep and its tests. Coordinated with E1's S4
collection scan.)* **Set ONCE, before any test, never toggled during the run
(gate finding D2 / judge J11).** The C# side is a `[ModuleInitializer]` in the test assembly (`RunTemp`, one static
class, the first code of the assembly to run — before any type is touched, including `MemberData` discovery, class
fixtures and static initializers), not the assembly fixture: it records the REAL temp folder in `RunTemp.Real`, makes
`testsToDelete-coai-run-<6 chars>` inside it, and points `TEMP`, `TMP` and `TMPDIR` of the test process at the new one.
The existing `[assembly: AssemblyFixture]` (`TempDirsAreSwept.cs:5`) then only sweeps the REAL folder (through
`RunTemp.Real`, as today); the end — restoring the three variables, removing the run folder and its claim — belongs to the dedicated
`RunTempFixture` and the process-exit handler, which restore and remove and never set. `src_vs_code/scripts/run-tests.mjs` does the same for the `node --test` children
it starts (the variables are set in the parent before the first child, once). `Path.GetTempPath()` and `os.tmpdir()`
read those variables on every call, and every child inherits them — the suite already relies on this in
`AVibeHomesNeverLiveUnderTempTests.cs:15-60`, the ONE class that moves `TEMP` during a run; it is in `fakecli-env`
and restores what it found (`:25`, `:27-32`), which under D2 is the run folder, so it stays as it is. Everything a run
makes in "temp" — its own folders, the product's `coai-answers-`/`-repair-`/`-noworkspace-`, the loose `api-*.prompt`
files, a stray steering file — is then inside one folder that the run removes, that no other run's sweep can see
(C12), and that a glob over "temp" cannot leave (C10).

*The guards that mean the REAL temp (judge J2).* Two assertions say "never under TEMP" and must keep saying it about
the machine's folder, not the run's: `AntigravityReadOnlyTests.cs:118` (`DefaultBase.Should().NotStartWith(Path.GetTempPath())`)
and `KimiCredentialsLockTests.cs:237` (`Root.Should().NotStartWith(Path.GetTempPath()…)`). Under D2 both would compare
a `LocalApplicationData` path against the run folder — still green, but no longer able to catch a root that moved into
the real temp. Both read `RunTemp.Real` instead. The E2 spec includes the scan of every other `Path.GetTempPath()`
assertion in the test trees and says, per site, which temp it means; read on `38ecd4e11` today: `AVibeHomesNeverLiveUnderTempTests.cs:48`
(asserts its OWN move — unchanged), `AVibeRowReviewsInTheRoundsSnapshotTests.cs:192` and `KimiRuntimeTests.cs:229`
(compare an in-process product path with the same call — the run folder on both sides, unchanged), `AcpClientTests.cs:175`,
`:189` (a fixed name that must NOT be created — lands in the run folder if it ever is, unchanged), `SnapshotBudgetTests.cs:114`
(free bytes of the volume — unchanged). A guard test fails when a test source uses `GetTempPath()` in a `NotStartWith`
/`NotContain` assertion without `RunTemp.Real`.

*A killed run leaves ONE folder — claimed (judge J1, the lead's decision).* The run folder carries the family's owner
claim in E5's exact format so E5's sweep can judge it by liveness instead of age: a sibling
`testsToDelete-coai-run-<6>.owner` holding `<host>@<os>:<pid>[:<startUnixSeconds>]` — the `OWNER_TEXT` of the
conventions' reference helper (`dew_flow_conventions · tools/lib/owned.mjs`, on branch `e1/r3-owned` on 2026-10-19,
NOT yet on its `main`): the start only where the OS gives it without spawning a process (`/proc` on Linux; the
two-field form on Windows and macOS), written AFTER the folder exists and the restore is registered. Reuse rule: if
E5's C# helper (`owned` for .NET, E5 S3) is on `main` when E2 starts, `RunTemp` calls it; otherwise `RunTemp` holds the
one claim writer (one static method, the format above, a test that pins the regex) and E5 S3 adopts it — never two
writers. Until E5's sweep lands, the FOLDER is collected by the family janitor (`.agents/conventions/common/test-leftovers.md`)
after an hour — it matches directories only (`-type d` / `-Directory`), so a killed run's `.owner` FILE stays until E5's sweep removes orphan claims (E2 judge J5); after it lands, an age backstop in days stays beside the liveness sweep (§11, the E5 plan's open item). *Loses:* a temp-root parameter through the product (`RosterBuilder`, `RosterAssembly`, `SecurityRoster`,
every runtime's output folder) — a product change for a test concern, the follow-up leftovers-E5's D5 proposed; the
operating system already offers this seam. *Risk, measured in its story:* the folder adds about 25 characters to every
temp path, and some tests sit near a path budget (Kimi's 260, Vibe's 102 — Vibe homes no longer live under temp,
`AVibeHomesNeverLiveUnderTempTests`); the story runs those classes first and keeps the name short. *Two things the story
proves rather than assumes:* that the module initializer ran before every class fixture, static initializer and
`MemberData` that reads the temp path (a test that records `Path.GetTempPath()` from a class fixture AND from a
`MemberData` says so), and that the extension runner removes its folder BEFORE it calls `process.exit` (`run-tests.mjs`
ends the process itself — the same trap leftovers-E5 recorded for `run-host.mjs`, where a `.finally` never ran).
Children reach the folder because `TEMP`, `TMP` and `TMPDIR` are on every launch's allowlist
(`ProcessEnvironment.cs:48-52`).

**D3 — A deadline a test must race is armed through an injectable `TimeProvider`, one class at a time, each landed
WITH its RED (judge J8).** No all-at-once seam story. The seam is an optional `TimeProvider? clock = null` constructor
parameter on the class that OWNS each timer — `BoundedScheduler.cs:96-102`'s existing precedent (`_clock = clock ??
TimeProvider.System`) — and it lands in the same story as the first RED that needs it: `AcpClient` (`:119`, `:195`) with
C5/S12; `ConsultantPlaces` (`:334`) with C17/S16; `TurnLoop` (`:41-45`, `:134-138`) and `SourceResolver` (`:200`) with
C13/S14, which C6/S13 then reuses. **Never threaded through `PanelService` or `RoundEngine`** — they construct these
classes with the default, and the round clock at `RoundEngine.cs:286` gets no seam (C6 removes the walk inside it
instead). `SecuritySources.cs:32` is the sibling plan's (C8, J14). **`ProcessLauncher` (`:401`) is left out** until C4
has a kept failure — its three separations (execution deadline from drain grace, caller cancellation from timeout:
`:287`, `:401`, `:437`) are the riskiest seam here and the only evidence is a failure nobody kept. A test that means
"the deadline strikes while X" drives a manual clock and advances it when X has happened. **The seam's contract is
the cornerstone, not the parameter:** on each class every elapsed-budget calculation, delay and cancellation reads the
SAME clock (a `Stopwatch` left beside an injected timer is the race again); `CreateLinkedTokenSource` + `CancelAfter`
has no `TimeProvider` overload, so the timer becomes `new CancellationTokenSource(delay, clock)` linked in, and
`WaitAsync(timeout, clock)` replaces `WaitAsync(timeout)`; and the tests get ONE manual clock with timers, monotonic
timestamps and disposal — the suite's `FixedClock` (`src_mcp/tests/FixedClock.cs:16`) and `MovableClock`
(`TheRunsThemselvesTests.cs:209`) override only the wall time. The shape to copy, by path:
`wsl_care · src_daemon/tests/WslCare.TestSupport/ManualTimeProvider.cs` (timers `:36`, timestamps `:58-60`); the
manual clock is the first story of E4 and is the one piece landed without a product change. *Loses:* a single seam
story across six classes (a refactor with no RED of its own, which is what made the first cut a cornerstone for the
wrong reason); longer timeouts (they move the race, they do not remove it, and every second is paid on every run);
fixed sleeps in the fakes as the thing that decides the outcome.

**D4 — A round removes its own always-empty folders (PRODUCT — built in E3, judge J9).** `coai-repair-` and
`coai-noworkspace-` are removed when the round ends, by the PATH the round recorded when it made them, never by a name
glob; `coai-answers-` keeps the six-hour sweep. **The plumbing, named:** the frame paths never leave `RosterBuilder` —
`RosterFrame` (`RosterAssembly.cs:13-24`) is built in `RosterBuilder.FrameFor` (`:274-290`: `repairDir` at `:282`, the
`coai-noworkspace-` launch folder through `RosterFrame.Directories` at `:283` → `RosterAssembly.cs:42`) and used only
inside the builder — but the frame already carries the round's **`RoundScratch`** (`RosterFrame.Scratch`, `:24`),
reserved once per `BuildWork` (`RosterBuilder.cs:107`), handed out in `RoundWork.Scratch`, and DISPOSED by the round at
its real end: `using var scratchOfTheRound = roundWork.Scratch;` (`RoundEngine.cs:360`) — after every reviewer and
repair of the round, on success, failure and cancellation alike, because it is a `using`. `RoundScratch.Dispose`
(`RoundScratch.cs:113-170`) already deletes its own folder with `GitScratch.DeleteEvenIfReadOnly`. So D4 is: the two
folders are REGISTERED with the scratch when they are made (`FrameFor` for both of the ordinary round's;
`SecurityRoster.Append(RoundWork ordinary, …)` (`SecurityRoster.cs:30`) registers its own `:154` folder with
`ordinary.Scratch`), and `Dispose` removes them **only when they are EMPTY** — a non-recursive `Directory.Delete`
(cadence consult 589e4647, verified): the launcher lists and confirms descendants only for a request that ends its own
child (`ProcessLauncher.cs:125`/`:668`), so an ordinary reviewer's grandchild can outlive the round inside that folder, and
on Unix removing a live process's working folder succeeds silently. A folder that is not empty, or cannot be removed,
is warned about and left to the six-hour sweep (which needs the folder old, so a live grandchild is not raced), and
**a failed removal never fails the round.** The always-empty case is the measured one (8,844 of 8,844). **Policy,
stated (cadence consult 589e4647, turn 2):** an EMPTY folder can still be a surviving descendant's working folder. A
round that has ENDED may remove it anyway: a descendant still running after its round is a leak the round's end should
have stopped, and anything it writes later is not wanted. On Linux the removal succeeds and that descendant's next
relative write fails; on Windows the removal fails while the folder is in use, and the folder is left to the sweep. S7's
grandchild arm pins exactly this: the grandchild enters the empty folder, closes its pipes, signals ready and waits; the
round ends and disposes; the round's outcome is unchanged on both platforms, and the folder is gone (Linux) or left (Windows). A `BuildWork` called with no round around it
(`SubmissionOrderTests.cs:116-118`, a hundred times) never disposes its scratch; under D2 that is the test's folder,
and D4 does not claim more than rounds. *Built (E3 S3, 2026-10-21):* see E3's S3 Outcome — the line numbers above are re-pinned to the code as built. *Loses:* leaving it to D2 (D2 helps only the tests; a person's machine makes
two of these per round for ever); a `finally` in `RoundEngine` (there is none today, and the scratch's `using` IS the
round's end — a second end would be the reuse rule's defect).

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
| **The owner claim on the run folder — ONE format, ONE writer** (judge J1, the lead's decision) | **shared contract**: the sibling `<folder>.owner` holds `<host>@<os>:<pid>[:<startUnixSeconds>]` — `OWNER_TEXT` of `dew_flow_conventions · tools/lib/owned.mjs` (branch `e1/r3-owned`, 2026-10-19; not on its `main` yet) | **Landing order:** whichever lands FIRST on `main` owns the C# claim writer. If leftovers-E5 S3 (the C# helper) is merged when E2 starts, `RunTemp` calls it. If E2 lands first, `RunTemp` holds the writer (one static method + the regex test) and E5 S3 adopts it instead of writing a second one. **Open item for E5's owner** (the E5 plan lives on `docs/test-leftovers-e5-stories`, draft PR #850, not on this branch's base, so it is amended there, not here): add to its S3 the sentence "the run folder's claim is written by the same helper; if `RunTemp` exists on `main`, S3 moves its writer into the helper and `RunTemp` calls it", and to its S2 that `testsToDelete-coai-run-*` is a name its sweep judges by liveness like any other |
| Renaming every test folder to `testsToDelete-coai-…`, the claim marker, the guard | leftovers-E5 | this plan renames nothing; D6 only makes seven sites remove what they made |
| C12 — the ten-minute age sweep can reach a live run | leftovers-E5 (owner liveness replaces age) | D2 removes the exposure until leftovers-E5 lands |
| C8 — the security lane's two equal deadlines | [PLAN_two_equal_deadlines_race_on_macos.md](PLAN_two_equal_deadlines_race_on_macos.md) | this plan orders it (§9), adds the Windows evidence and the step-3 correction (C8), and shares E4's manual clock. **Clock seams (J14, both sides):** `SourceResolver.cs:200` gets its clock HERE (C13); `SecuritySources.cs:32` gets its clock THERE, with S21. The sibling's step 1 needs no seam and may start as soon as E1 is merged |
| `CadenceStoreTests.ManyClosesAtOnce_AllLand` — `SessionTurn.Take` counts tries | [PLAN_a_turn_waits_by_the_clock.md](PLAN_a_turn_waits_by_the_clock.md) | this plan orders it (§9) and adds the CI evidence (§3) |
| C6, C13, C14 — the three deadline tests of `PLAN_question_consultant_tails.md` §6 | **this plan** — moved here on 2026-10-19 | tails §6 now points here; its DoD line goes with it |
| D4 — a round removes its two empty folders | **this plan** (E3, product) | not E2: E2 is test infrastructure only (J9) |
| C18's product angle and C25 — the roads that go WITHOUT the turn retry the refusal Windows actually gives (`AtomicFile`'s move, `AtomicJson`, the heartbeat, `SessionStore`'s in-place write) | **this plan** (E3 S4, S5) | [PLAN_a_turn_waits_by_the_clock.md](PLAN_a_turn_waits_by_the_clock.md) keeps `SessionTurn.Take` (tries → a clock) and touches none of these four. **Open item for that plan's owner:** its boundary line "Disjoint: that plan builds nothing in `SessionStore`" becomes "…nothing in `SessionTurn`; it changes `SessionStore.Write`'s in-place write (C25)" |

Disjoint: nothing else in this plan is built by another plan, and nothing in those plans' other sections is built here.
Order (J7): E1 (C1) first and alone — it is the case that already changed a result across runs; **C2 (E3 S1) and the
sibling's C8 step 1 start at once, beside E1** — neither needs a clock seam (C2's seam is a `Func<Process, DateTime>`
on the launcher; C8's step 1 is deterministic on the system clock) and both are the largest sources of macOS red;
`PLAN_a_turn_waits_by_the_clock.md` (C19) may run at any time.

## 8. Scenarios — each with the RED it must show first

Every RED is watched failing against today's code with the message below (or, where it says so, the reason no
deterministic red exists), then watched passing; both observations go in the PR (`.agents/conventions/common/testing.md`).

| # | Scenario (test name) | Case | RED today — the message that proves the cause |
|---|---|---|---|
| S1 | `ASteeringFileAnotherRunLeftInTheUsersTemp_NeverSteersThisRunsFake` — a decoy in a PRIVATE temp, the fake started with `TEMP`/`TMP`/`TMPDIR` pointed at it | C1 | the fake answers `decoy` and exits 7 |
| S2 | `ASteeringFileBesideTheBinary_SteersTheFake` | C1 | does not compile / the fake ignores the file (today it reads only temp) |
| S2b | `EveryWriterOfTheSteeringFile_IsInTheSerialCollection` (a source scan over the helper's callers, judged on the concrete class) | C1 (gate D1) | green today on the six classes; watched red by moving one class out of `fakecli-env` |
| S3 | *dropped at the E1 code gate (judge, item 4 under E1's Outcome):* the decoy repair test wrote a non-empty folder into the machine's shared temp and its assertion was true by construction. C10's repair test is the positive-control test of E1's Outcome instead | C10 | — |
| S4 | `EveryClassThatSetsTheProcessEnvironment_IsInTheSerialCollection` (a source scan; its second rule: every caller of `PanelService.ForgetTheLastSweep` is in a serial collection) | C11, C6 | names `ARequestFaultIsNotAnOldBinaryTests.cs` and `VibeRuntimeTests.cs`; the second rule names `AScratchSweepRunsOnTheConsultationBeatTests.cs`, `VibeHomesTests.cs`, `TempDirsAreSwept.cs` |
| S5 | `ATestRun_KeepsItsTempInsideItsOwnFolder` — the run folder exists, `Path.GetTempPath()` is inside it from a class fixture AND from a `MemberData`, `RunTemp.Real` is the machine's folder, the folder's `.owner` matches E5's regex, a product round's `coai-answers-` lands in it | D2 | `Path.GetTempPath()` is the machine's temp folder |
| S5b | `TheNeverUnderTempGuards_CompareAgainstTheRealTemp` — a source scan: a `NotStartWith`/`NotContain` on `GetTempPath()` without `RunTemp.Real` | D2 (J2) | names `AntigravityReadOnlyTests.cs:118` and `KimiCredentialsLockTests.cs:237` |
| S6 | `AFullRunLeavesNothingOutsideItsFolder` (a measurement, §10) | D2, D6 | today: ≈3,700 new `coai-*` folders and the loose prompt files after one `src_mcp` run |
| S7 | `ARoundRemovesItsEmptyRepairAndWorkspaceFolders` — and `AFolderThatCannotBeRemoved_IsLeftToTheSweep_AndTheRoundStillEnds`, `AFolderThatIsNotEmpty_IsKeptAndWarnedAbout_AndTheRoundStillEnds` (a file planted in the repair folder before the round ends: the folder and its file are still there, one warning names it, the round's outcome is unchanged), and the grandchild arm of D4 | D4 (E3) | both folders still exist after the round |
| S8 | `AChildThatExitedBeforeItsStartTimeWasRead_IsNotReportedAsNotStarted` — two arms: the seam throws the macOS `Win32Exception`, then an `InvalidOperationException`, after the child exited; the tracker stand-in saw no `Record` and saw the end-of-launch call | C2 | the launch throws `Win32Exception` "Unable to retrieve the specified information about the process or thread" (arm a) / `InvalidOperationException` (arm b) |
| S9 | `AChildStillAliveWhoseStartTimeCannotBeRead_IsKilled_AndTheErrorReachesTheCaller` | C2 | green today — the guard that the fix keeps story 6.1a's rule; watched red by breaking it once |
| S10 | `ASettingsFileHeldOpenByItsWriter_KeepsTheLastGoodSettings` (Windows), with two green companions: a malformed file on reload gives defaults, an empty file on reload gives defaults; and `CurrentNeverThrows_WhenTheRebuildThrows` | C14 | the busy read shows the defaults, published under the new stamp; the rebuild's exception reaches the caller |
| S11 | `AStubDisposedWhileItsHandlerWrites_WaitsForTheHandler_AndDoesNotThrow`; and `AHandlerStillActivePastTheBound_IsAbortedBeforeTheListenerStops_AndDisposeDoesNotThrow` | C16 | `Dispose` returns before the handler ends; (b) `Stop()` races the writer — the CI `NullReferenceException` from `FormatHeaders`, or `Dispose` returns with the handler still writing |
| S12 | the AcpClient usage test on a manual clock + a fake agent with `startDelayMs` longer than the deadline | C5 | `found AcpEnd.Deadline` (the CI message) |
| S13 | `RoundDeadline.For` pinned (8 s with three follow-ups, 2 s with none) + the end-to-end test on a manual clock, 700 ms per turn | C6 | with a one-turn budget put back: the conversation is cut at turn 3, every run |
| S14 | the three turn tests and the resolver test on a manual clock | C13 | with the cap at `TurnLoop.cs:44` removed: no terminal `TimedOut` |
| S15 | `AServerThatNeverAnswers_…` on the launcher's manual clock + `startDelayMs` past the deadline — **gated on a kept failure (D5); the launcher gets no seam before that** | C4 | the pid file is missing (today's test) |
| S16 | the copy budget on `ConsultantPlaces`' own manual clock; the slow root advances it | C17 | `0 of 2 copied` when the fast copy is slowed past the budget in today's test |
| S17 | the probe released by the shim's first busy answer (the seam on the Kimi call site, `KimiCredentialsLock.cs:209`); and a probe never released ends in exit 69 | C3 | no deterministic red for the race; the loop of §10 before (failures) and after (none) |
| S18 | the heartbeat seen through the store's `projected` callback, the row released after three beats | C18 | with the heartbeat loop removed: fails at the 30 s guard with its sentence |
| S19 | the follow-up launch's timeout asserted directly | C15 | with `RetryLadder.Lesser(left, next)` replaced by the whole budget: launch 2's timeout is 8 s |
| S20 | `replayingFan` CPU bound at 40,000 values; the real-child smoke test waits on `onExit`/`onError` only — no `ended` 10 s timer | C9 | with `held = [...held, value]` put back: CPU time in seconds (the smoke test stays green) |
| S21 | the security lane's per-file timer fired BEFORE the collection timer on a manual clock (`SecuritySources`' clock is the sibling's, `SourceResolver`'s is C13's) | C8 | `not served: src/B.cs — timed out: git did not answer within 30 s` — built by its own plan |
| S22 | `AZombie_IsGone_NotSignalled` with the kill after the parent reads `comm == sleep`; the red arm through `bash … sleep 2; exec sleep 600` | C21 | `the outer sleep never reaps, so the killed child should have stayed a zombie` (the CI sentence), every run |
| S23 | `run-host.mjs` against a stub `vscodeExecutablePath` that exits 1 at once after a `[main …]` line and no host-started line: the sentence names "the editor exited before the extension host ran a scenario" and quotes the line; a `::warning::` line carries the same; the stub is launched twice before the sentence. Second arm: a stub that prints the host-started line, then exits 1 — launched once, no retry | C22 | `the extension host: Test run failed with code 1`, one launch, no `::warning::` |
| S24 | `AMoveOverAFileAReaderHolds_IsRetriedWithinTheWindow_AndLands` (Windows) — the destination held with `SharedRead.Text`'s open (`ReadWrite \| Delete`), released by the pause seam's first call; and two guards, green before and after: a holder never released throws `UnauthorizedAccessException` when the window ends and leaves no `*.tmp`; a zero window tries once and never pauses | C18 (product, E3 S4) | `UnauthorizedAccessException: Access to the path is denied` on the first move; the pause was called 0 times |
| S25 | `AHeartbeatThatCannotBeWritten_IsSaid_AndTheQuestionStillSettles` (every OS) — the store's `projected` throws `UnauthorizedAccessException` on the first heartbeat-only write and opens the row's gate: `RunAsync` returns the row `Answered`, one warning names the question; second arm `ABeatAfterAFailedOne_IsStillWritten` — the gate opens only on the NEXT beat | C18 (product, E3 S4) | (a) `System.UnauthorizedAccessException : Access to the path is denied.` thrown by `RunAsync` from `HeartbeatAsync` — the CI sentence of run 39325900821; (b) no beat after the failed one: the row's guard fails with its sentence |
| S26 | `AnInPlaceSaveThatMeetsABusySessionFile_IsRetried_AndLands` (Windows) — the session file held `Read`/`FileShare.Read` with the turn free, released by the store's pause seam; guard `ASessionFileHeldThroughEveryAttempt_FailsTheSaveNamingIt_AndKeepsTheOldSession`, green before and after | C25 (E3 S5) | `SessionStoreException` with inner `IOException` "…session-….json because it is being used by another process" (the E2 sentence); the pause was called 0 times |

## 9. Build order — the shared-state fixes first

1. **E1 — C1, then C10 and C11 — and, AT ONCE beside it (J7): C2 (E3 S1) and the sibling's C8 step 1.** C1 is the case
   that already changed a result across two runs; C10 and C11 are the same class and small. C2's seam is a
   `Func<Process, DateTime>` on the launcher and C8's step 1 is deterministic on the system clock — neither waits for a
   clock seam, and together they are the two largest sources of macOS red (17 + 43 runs), so neither waits for E1 either.
   Nothing else in this plan is MEASURED (the loops, the two-runs proof, the census) while two runs can still see each other.
2. **E2 — D2, then D6 (test infrastructure only, J9).** The per-run folder makes every later measurement clean (a
   run's leftovers are countable as one folder) and ends C12's exposure. D6's seven sites are one-line fixes. No
   product code in this epic.
3. **E3 — C2 (already started), C14, then D4; C18's product angle and C25 beside them.** C2 removes the largest single
   source of macOS red after C8 (17 runs plus C2's three siblings); C14 is a product defect with a deterministic red; D4 is
   the product change that moved here from E2 — it waits for E1's C10 rewrite (the repair test must assert on the launch,
   or D4 turns it red). C18's product angle (a refused heartbeat write ends a question whose rows answered) and C25 (an
   in-place session write refused with no retry) were read on 2026-10-20; each touches its own files and has a
   deterministic red, so neither waits for the others.
4. **E4 — the manual clock, then C5, C17, C13 (each landing its own class's clock seam WITH its RED), C6 on C13's
   seam; C4 gated.** No all-at-once seam story (D3). The first story is the manual clock alone (no product change);
   each next one adds `TimeProvider? clock = null` to ONE class and moves that class's test. C4's story opens only when
   a failure of `AServerThatNeverAnswers_…` has been kept (D5). [PLAN_two_equal_deadlines_race_on_macos.md](PLAN_two_equal_deadlines_race_on_macos.md)
   (C8) started beside E1; its step 3 waits for C13's `SourceResolver` clock and adds `SecuritySources`' own;
   [PLAN_a_turn_waits_by_the_clock.md](PLAN_a_turn_waits_by_the_clock.md) (C19) needs no seam and may go at any time.
5. **E5 — the tests that wait for an event: C16, C3, C18, C15, C9, C21, C22, then C7's measurement.** Independent of each
   other and of the clock seam, so E5 starts right after E1 and runs beside E2–E4 (§ Epics); C7 waits for a kept failure (D5) or ends with a recorded measurement of its slow step.

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
- **CI:** after each epic merges, the census script of §3 re-run over the next 20 runs of each leg — **as a watch, not
  as the proof** (judge J15): a case that fails in ~1 % of runs is absent from 20 runs 82 % of the time with NO fix, so
  absence proves nothing. **The proof that a case is closed is its deterministic RED turned green** (§8), plus the
  recorded loop for the cases that have no deterministic red (C3). The 20-run watch only says whether something ELSE
  is still wrong; a failure inside it reopens the case with its kept log (D5).
- Every suite green on Windows, Linux and the macOS CI leg before and after each epic; **no test removed or skipped,
  and the test count grows by exactly the new scenarios**, named per epic in its PR (the count before, the count after,
  and the list of added test names that makes up the difference).

## 11. Growth

- **The per-run folder (D2)** — one per suite run, holding what that run makes (≈3,700 product folders and their files
  for a whole `src_mcp` run; tens for a filtered run). **Retired by the run itself** at the end. **Interrupted:** a killed
  run leaves exactly one `testsToDelete-coai-run-…` folder with its `.owner` claim; leftovers-E5's sweep collects it when
  its owner is gone, and the family janitor (`.agents/conventions/common/test-leftovers.md`) after an hour until
  leftovers-E5 lands. **The caveat (E2 plan round, #8):** once leftovers-E5's liveness sweep REPLACES the janitor, a
  killed run on Windows or macOS can leak its folder for ever — the claim there is the two-field form
  (`<host>@<os>:<pid>`, no start time, D2), so a reused pid makes the dead owner look alive, and a claim whose identity
  cannot be established keeps its folder (a leak, never a deletion) — until the claim carries a start time on those
  platforms too. **Decision: an AGE backstop stays beside the liveness sweep** — measured in DAYS, not the janitor's
  hour (a run never lasts a day; a leaked folder does), so it can never reach a live run and still ends every leak.
  **Owner: the E5 plan** — this is one more sentence for the open item in §7's claim row (E5's S2: "`testsToDelete-coai-run-*`
  is judged by liveness like any other, and a folder older than N days is removed whatever its claim says"); this plan
  adds nothing to the sweep.
- **D4** removes growth: two folders per real round that today wait six hours.
- **C18's product fix** moves `AtomicJson` onto `AtomicFile`'s GUID temp, which `AtomicFile` removes when the move fails.
  **Interrupted:** a process killed between the temp write and the move leaves one `<record>.json.<guid>.tmp` per kill,
  where today it leaves one `<record>.json.tmp` that the next write reuses. Neither name is read as a record (the stores
  match `<32 hex>.json`, `RecordFiles.cs:21-28`; the extension skips names not ending `.json`, `jsonDirectory.ts:64-68`).
  Nothing sweeps them today; the bound is the number of kills inside a millisecond window (open item for the lead, E3 S4).
- **The steering files (D1)** — one file per test project output folder, deleted by the writer; a killed run's file is
  deleted by the next run's assembly start.
- Nothing else here writes anything that persists.

## Definition of Done

- [x] No test reads or writes a fixed name in the machine's shared temp folder; the steering files live beside the
      fake's binary; S1, S2, S4 RED then GREEN; S2b green and watched red once (S3 was dropped at the E1 code gate —
      E1's Outcome, judge item 4; the C10 repair test with its positive control stands in). *Done in #893.*
- [ ] Two whole `src_mcp` runs from two worktrees at once are both green.
- [ ] Each suite runs inside its own temp folder, set once by a `[ModuleInitializer]` and never toggled, with E5's
      owner claim beside it, and removes it; the two "never under TEMP" guards compare against `RunTemp.Real`; the
      measurement of §10 shows the real temp folder unchanged by a whole run (`src_mcp` and extension); S5, S5b, S6 RED
      then GREEN.
- [ ] A round removes its empty `coai-repair-` and `coai-noworkspace-` folders through its `RoundScratch` (D4, E3);
      a failed removal never fails the round; S7 RED then GREEN.
- [ ] C2 (both exception arms), C14 (busy only; malformed and empty as today; `Current` never throws), C16 fixed with
      their deterministic REDs (S8, S10, S11) and S9 kept green.
- [ ] C18's product angle (a move over a held file is retried on the refusal Windows gives; `AtomicJson` writes through
      `AtomicFile`; a failed beat is said and skipped, never the end of a question) and C25 (the in-place session write
      retried like the move) fixed with their deterministic REDs (S24, S25, S26), their guards green throughout.
- [ ] Each deadline a test must race is armed through that class's own optional `TimeProvider`, landed with its RED;
      nothing threaded through `PanelService`/`RoundEngine`; `ProcessLauncher` untouched until C4 has a kept failure;
      the product's default behaviour is unchanged; S12–S14, S16 RED then GREEN (S15 when its gate opens).
- [ ] C3, C9, C15, C18, C21 wait for an event or measure CPU instead of wall time; their loops recorded before and
      after. **C22 is excepted from the loop** (E2 plan round, #9): its evidence is S23 red → green (both arms) plus the
      streak step's count of first-launch `::warning::` annotations over the next 20 runs of `main`; the harness
      names an editor that exited before the host ran, retries the launch only on the host-started signal, and never
      fails silently.
- [ ] C7 either fixed from a kept failure or closed with the recorded measurement of its slow step.
- [ ] C8 and C19 built by their own plans in the order of §9 (C8 step 1 beside E1; `SecuritySources`' clock is the
      sibling's, `SourceResolver`'s is C13's); C6, C13, C14 closed here and removed from
      `PLAN_question_consultant_tails.md` §6.
- [ ] Every case of §3 closed by its RED turned green (or its recorded loop), then watched over the next 20 CI runs
      of its leg — the watch reopens, it never closes.
- [ ] No assertion weakened, no test skipped, no timeout lengthened without the measured cost that justifies it — said
      per case in its PR.
- [ ] `research/module_tests.md` records D1, D2, D3 and the per-run folder; the "OPEN shared-state investigation" note
      (`research/module_tests.md:4493`) is closed with a link here; `shared/temp-sweep.json`'s `why` names D2.
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
when it is not. D3 (E4) touches four classes on the product's launch paths, one per story; a `Stopwatch` left beside the
injected timer, or a `CancelAfter` on a linked token source that still uses the system timer, is the same race again
while the tests on the manual clock go green — and that mistake is per class, which is why the contract is written once
and checked in every story. *Weighed and not changed:* E2 before E1 (one consultant's view — D2 isolates runs only while
every launch keeps the `TEMP` allowlist, and C1 already changed a result, so D1 goes first); "E1 and E2 are grab-bags"
(each has one goal — two runs cannot meet; one folder per run — and every case in it serves that goal). *Changed by the
reviews (2026-10-19):* E2 is test infrastructure only and D4 is E3's (J9); E4 has no all-at-once seam story and leaves
`ProcessLauncher` alone until C4 has a kept failure (J8); C2 and the sibling's C8 start beside E1 (J7).

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

**Done when.** S1 red (`decoy`, exit 7) then green; S2 green; C10's repair test red on today's code then green (S3, the
decoy test, was dropped at the code gate — see the Outcome below, judge item 4); S4 red naming the two classes then
green; the two-runs proof of §10 green on both runs; no test reads or writes `fakecli-minimal.json` under
`Path.GetTempPath()`.

**Size.** Four stories for Sonnet coders. S1 the helper, the two readers, S1/S2 and the assembly-start delete (first).
S2 the six scenario classes onto the helper and the S2b structural scan (after S1; all six are in `fakecli-env`, so one
coder). S3 C10's sites and S3 (parallel with S1). S4 C11, the S4 guard and its second rule (the seven
`ForgetTheLastSweep` callers into a serial collection, C6) (parallel with S1). No cornerstone.

**Outcome (2026-10-19, branch `docs/flaky-tests-and-temp-leak`; the tests are in `research/module_tests.md`, section "The suite is deterministic — epic E1").**
Built in four commits, each red first: (1) C1/D1 with S1 and S2, the six writers moved onto the helper and the assembly-start delete;
(2) S2b; (3) C10; (4) C11 and S4 with its second rule. Reds seen: S1 `Did not expect result.StdOut "decoy" to contain "decoy"` and exit 7 where 64 was
expected; S2 `Expected result.StdOut "" to contain "steered"`; C10 `Expected Directory.EnumerateFileSystemEntries(dir) to be empty … {"…\coai-repair-decoy-e1red\x.txt"}` and the
two document tests failing with `IOException` / `UnauthorizedAccessException` when their fixed names were taken; S4 named the three classes of rule 2 and three of rule 1.
Break-its done and restored: the two readers back on the temp folder (S1, S2 and the whole Kimi flow red), a class out of `fakecli-env` (S2b), a scratch file spelling the
file's name (S2b), the repair launched in the review's folder (the C10 test), `[Collection]` off two classes (S4). A full `CoaiMcp.Tests.exe` run: 11,883 tests, 11,766 passed,
117 skipped, 0 failed; **the two-runs proof of §10 done**: two whole runs from two worktrees of this branch started three seconds apart, both 11,883 / 0 failed
(17 min 26 s and 17 min 23 s). Deviations from this plan, each decided on what the code showed:
- **S4's first rule names THREE classes, not two.** `TheVaultsRequestsAreShapedForTheKeyTests` (`TheVaultKeyTravelsOnStdinTests.cs`) sets a uniquely named variable
  (`COAI_TEST_INHERITED_7F3A`) in no collection — harmless by its name, the same shape by the rule. It joined `fakecli-env` with the two named here.
- **S1 does not move the test process's `TEMP`.** The child is started with `TEMP`/`TMP`/`TMPDIR` pointed at a private folder through its request, so the suite's own variables are
  never toggled (D2's "set once, never toggled" holds from E1 on), and nothing is written in the machine's temp folder.
- **The repair test needed `CodeWorkspace = "worktree"`.** In the default `none` mode the review launch runs in an empty folder too, so "the repair folder was empty at launch"
  could not tell a repair that carried the workspace from one that did not — the first break-it stayed green. The round is now a worktree round with a positive control (a review
  launch saw a non-empty folder), and the break-it goes red. The test also needed a REPAIR to exist: the first answers are prose and the repair answers are clean.
- **The shared constant lives in the fake's own assembly** (`CoaiMcp.FakeCli.MinimalSteeringFile`, public, read by `MinimalSteering.Load` and `AcpAgent.SteeredScript`) and the tests reach it
  through `FakeCliSteering.MinimalFile`: one definition where the file is read. The scan for other `fakecli-minimal.json` spellings allows exactly the helper and the two test files that pin it.
- **A nested class counts for its outer class** in the two scans (`TestSourceIndex`): the first parser judged private `Canary` helpers inside placed classes on their own and named them.
- **C1's commit includes the six writers' move** (not a separate story): between the helper and the move the suite would have been red.
**Code gate (2026-10-19), epic 1 (E1 + C2). Verdict: proceed.** The fixes below were made in the same branch, each red first where it changes behaviour.
Accepted (gate numbers #0, #1, #2, #3, #8, #9, #10, #11, #13; #14 in part): #0/#2 one `HasExited` read decided "the child has gone" — now `HasEnded` (`WaitForExit` 100 ms; "no process
is associated" counts as ended), red by a seam that kills the child 10 ms after the read fails; #1 the start-time seam is an `internal` constructor overload, the public constructor
is unchanged; #3 the test helper `IsGone` catches `ArgumentException`, `InvalidOperationException` and `Win32Exception` and compares the start time captured at launch (pid reuse);
#8 every limit of the structural scans is listed in `module_tests.md`; #9 a steering file that cannot be deleted warns and never blocks the assembly (no `Lazy` caching an exception;
a writer's lease deletes best-effort); #10 `TestSourceIndex` builds a name-to-class lookup once per list (no quadratic lineage); #11 `EveryWriterOfTheSteeringFileTests` reads each file once; #13 the dictionary
`ReadBack` returns is read-only and its caller (`QuestionRowLookupScenarioTests`) builds a new one; #14 was replaced by the judge's concrete rule — a static class
other than `FakeCliSteering` that writes the steering file or sets the environment fails, and `FakeCliSteering.Reset(` joins the environment rule's pattern (`using static` and aliases are a
stated limit).
Rejected, with the reason: #4 the `when` precedence is correct (`e is A or B && cond` is `(e is (A or B)) && cond`); #5 and #6 the catch in `RecordedAsync` rethrows after ending the child, so
nothing is swallowed; #7 the scans defend against accidents, not a determined bypass (said in the limits); #12 a sentinel start time would be wrong data on the tracker's disk; #14 as worded
(see above).
The judge's eight: (1) the safety claim was WRONG and is corrected in the code remarks, `module_runners.md` and this plan — a `spawning` marker with no live child fails CLOSED
(`ReaderMarkerDto.Unidentified` is true for every phase but Running, `ReaderReconcile.RunAsync` returns `Blocked(ReaderIdentityMissing)`), cleared by `GaugeCapture.ReadAsync`'s `ClearReader`
at the launch's end, and a server crash inside that window blocks the slot until a person acts — the same window that exists today between `MarkReaderExpected` and `Record`;
(2) `GaugeCaptureTests.ALaunchThatReturnedWithoutRecord_ClearsTheMarker_StoresTheGauge_AndBlocksNoSlot` pins the launch-returned-without-Record path — a guard, green from the start, watched
red by commenting out `ClearReader`; the S8 assertion message now says the marker is cleared by `ReadAsync` because `SlotReaderTracker.Forget` does nothing; (3) the scans see static helpers (above);
(4) the S3 decoy test is dropped — it wrote a non-empty folder into the machine's shared temp and its assertion was true by construction; (5) `FakeCliSteering`'s lease and `DeleteALeftover`
are best-effort; (6) the C10 positive control read every launch since the service was built (the plan round, the `--version` probe) and stayed green in a mode where every real review ran
in an empty folder — it records only from just before `ReviewCodeAsync`, counts only launches that carry a prompt, and a test asserts it goes red in `none` mode; `SeenRepairs` never throws for a
missing folder; (7) the internal constructor (above); (8) a leftover that cannot be deleted must not block the assembly. After the gate: `CoaiMcp.Tests.exe` 11,901 tests, 11,784 passed, 117 skipped,
0 failed; `CoaiServer.Tests.exe` 614, 613 passed, 1 skipped. (A first whole run showed one failure of `AKimiRowRunsThroughTheServiceOnWsl2Tests` — two whole runs of the SAME output folder had been
started by mistake and shared the steering file, the limit stated above; the clean run is the number.)
**Not done in E1, and said:** the fixed names that are only PATHS and never created (`ConsultationStoreTests`, the `Kimi*` classes and the rest) were left alone, as C10 says; the CI watch of §10 starts when this merges.

### E2 — One temp folder per run — cornerstone area: the per-run temp folder (test infrastructure only)

**Goal.** A whole `src_mcp` run and a whole extension run leave the machine's temp folder as they found it; everything a
run makes sits in ONE folder the run removes, so no other run's sweep (C12) and no glob over "temp" (C10) can see it.
**No product code in this epic** (J9) — D4 moved to E3.

**Scope.** D2: a `[ModuleInitializer]` in the C# test assembly (`RunTemp`) records the real temp folder in
`RunTemp.Real`, makes `testsToDelete-coai-run-<6>` and points `TEMP`/`TMP`/`TMPDIR` of the test process at it, ONCE,
before any type is touched; `TempDirsAreSwept` (also an assembly fixture) only sweeps the real folder (`RunTemp.Real`);
the DEDICATED `RunTempFixture` (no test methods) and a process-exit handler, both calling one idempotent `RunTemp.EndOfRun`, restore the
three variables and remove the run folder and its claim at the end — neither ever sets, and `TempDirsAreSwept` neither restores nor removes; the folder carries E5's owner claim
(`<folder>.owner`, `<host>@<os>:<pid>[:<start>]`, §7's one-writer rule). `src_vs_code/scripts/run-tests.mjs` does the
same for its `node --test` children and removes the folder BEFORE `process.exit`. The two "never under TEMP" guards
(`AntigravityReadOnlyTests.cs:118`, `KimiCredentialsLockTests.cs:237`) compare against `RunTemp.Real`; the S5b scan
keeps it so; the spec lists every other `GetTempPath()` assertion with its verdict (D2). D6: the seven leaking sites
remove what they made. The S6 measurement before and after. Closes C12's exposure until leftovers-E5 lands.

**The cornerstone area** is D2's initializer, fixture and runner: the order proof (the module initializer ran before
every class fixture, static initializer and `MemberData` that reads the temp path — a `Path.GetTempPath()` recorded
from a class fixture AND from a `MemberData` says so), the never-toggled proof (no test source sets `TEMP`/`TMP`/`TMPDIR`
except `AVibeHomesNeverLiveUnderTempTests`, which is in `fakecli-env` and restores), the inheritance proof (a product
round's `coai-answers-` lands inside the folder through the allowlist, `ProcessEnvironment.cs:48-52`), the real-temp
guards (J2), the claim's format and its order (folder → restore registered → claim), the restore on every exit path, the
path budget (Kimi 260, Vibe 102 — those classes run first), and the one claimed folder a killed run leaves. Fable
writes this spec; Sonnet codes; Opus reviews.

**Depends on.** E1 (the same fixture). Not on E3/D4.

**Done when.** S5 red (`Path.GetTempPath()` is the machine's folder) then green; S5b red (names the two guards) then
green; S6 shows the real temp folder's `coai-*` and loose `api-*`/`local-*` counts unchanged by a whole `src_mcp` run
and a whole `npm test`, and the run's folder gone; every suite green on Windows, Linux and macOS, no test removed or
skipped and the count grown by exactly E2's new scenarios (§10); `shared/temp-sweep.json`'s `why` names D2.

**Size.** Four stories. S1 the module initializer, the fixture's restore/remove, the claim writer (or the call to E5's
helper, §7), S5, S5b and the proofs (the cornerstone, Fable spec; first). S2 the extension runner and its exit-path
proof (parallel with S1; the same spec covers it). S3 D6's seven sites (parallel). S4 the S6 measurement before and
after, recorded in `research/module_tests.md` (a scout gathers, the caller records).

**Outcome (2026-10-20, branch `feat/suite-e2-run-temp`; the tests, their reds, the break-its and the S6 numbers are in `research/module_tests.md`, section "The suite is deterministic — epic E2").**
Built in seven commits, each red first (the code gate and judge of 2026-10-20 then added six more, see "The E2 code gate and judge" at the end): (1) D2 in C# — `RunTemp` module initializer, `RunTempFolder`, `RunTempFixture`, `OwnerClaim`, S5a–S5i, S5b-scan; (2) D6's six C# sites;
(3) the extension runner and X1–X3; (4) D6's four extension files; (5) two Vibe test classes that the 29 extra characters pushed over the home budget; (6) the owner-claim judge's
host/OS/refused-lookup rules (the lead's extra input of 2026-10-20); (7) docs. Break-its (a)–(g) of the spec were all done and restored. S6: a whole `CoaiMcp.Tests.exe` and a
whole `npm test` on a private stand-in for the machine's temp folder leave nothing of their own (the extension leaves only node's own `node-compile-cache` — one stable, reused folder, measured); `--list-tests`, `--help`
and an empty filter leave no run folder and no `.owner`. Deviations, each decided on what the code showed:
- **`RunTempFolder.Create` takes one `RunTempSeams` record**, not `(realRoot, setVariable, getVariable)`: it also needs the register-restore, claim-writer, deleter and warning seams for S5f and for the failed-delete test.
- **`OwnerClaim` also holds `Format`, `ForThisProcess`, `Judge` and `ProcessIsAlive`**, and is `public`. `Judge` and `ProcessIsAlive` are the lead's extra rules (same host and OS only; `ArgumentException`/`InvalidOperationException` dead, `Win32Exception` alive), verified against the benchmark's and the RAG repository's own liveness code. A live pid always keeps; `Verdict` has no start parameter (comparing a start is E5's, after the gate's G13/G17).
- **`<os>` is Node's spelling** (`win32`/`linux`/`darwin`) and `OwnerClaim.Verdict` stays in the test tree until E5 S3 adopts both (the lead's decisions).
- **`EveryClassThatSetsTheProcessEnvironmentTests` was extended**, not only reused: the initializer is a static class that sets `TEMP`/`TMP`/`TMPDIR`, which the rule 1 static arm named at once (`found at least one item {"RunTemp.cs: RunTemp"}`). `TestSourceIndex.StaticOffenders` gained a set overload; `RunTemp` is the one other allowed writer; a synthetic case keeps any other static helper an offender.
- **D6 found more sites than the plan named:** besides `coai-plan-said-` and `coai-check-model-`, four more `PanelServiceTests` folders (`coai-record-`, `coai-cwd-`, `coai-deal-`, `coai-deal-plan-`) were never removed. All six use `TempDir.For` now; `TheLeakingTestSitesRemoveWhatTheyMadeTests` runs each. The extension's four files are proven by `chatTestsLeaveNoTemp.test.mjs` (a child per file on a private temp).
- **The path budget cost two more fixes than the spec foresaw:** 17 `VibeLaunchControlTests` failed at once (that class now builds its runtime with `VibePathBudget.None`), and a run with a 16-character-longer temp showed four more — `VibePathBudgetTests` rooted its fixed-length homes at the temp folder (now at the drive root) and the unmeasured-Vibe plan round of `AVibeReviewerRunsThroughAFakeVibeTests` sat 6 characters under the limit (now `VibePathBudget.Using(None)`). Kimi's classes needed nothing. Nothing was shortened.
- **X3 only has teeth because the runner makes its folder BEFORE the "no compiled tests" exit** (the spec's `main()` shape); the early exit is then a `return 1` inside the `try/finally`, not a `process.exit`.
- **The X2/X3 driver drops `NODE_TEST_CONTEXT`** from the runner's environment, or the runner's own `node --test` treats itself as nested and skips every file.
- **The S6 proof uses a private stand-in for the machine's temp**, because other sessions' suites write the real folder at the same time and a before/after count of it cannot be attributed (it is also recorded: 1,000 new `coai-*` from them in 18 minutes, none of the run's kind).
- **Two tests failed once each in a full run and pass alone** (listed, not called flakes, in `module_tests.md`): `AStreamsUsageIsNeverZeroTests.A_stream_over_the_answer_cap_is_unknown` and `AReviewerThatAsksForSourceIsAskedAgainTests.AConversationThatOutlivesItsCap_…` — the second is C13's family (E5).
**Open tail:** E5's Node sweeper must pass `shared/owner-claim-vectors.json`, use the same claim rules (same host and OS; only `ESRCH` dead, `EPERM` alive) and keep an age backstop in days; the pid-reuse leak on Windows/macOS and the janitor's one-hour rule are stated limits; the Windows CI leg shows first whether any other Vibe home under temp is now near its limit; `CoaiServer.Tests` and `run-host.mjs` are untouched.

### E3 — Product defects the tests found

**Goal.** Four product defects that show up today as "flaky" tests are fixed with deterministic REDs — C2, C14, C18's
product angle, C25 — and a round stops leaving two empty folders per round (D4); no assertion changes.

**Scope.** C2: one helper reads `process.StartTime` (`ProcessLauncher.cs:311`) and catches `Win32Exception` AND
`InvalidOperationException`; when the read throws AND the child has exited, nothing is recorded (`Record` not called;
the reader marker stays `spawning` and is cleared by the launch's normal end) and the launch goes on; when it throws
while the child is alive, today's rule stays (kill, wait, rethrow); the `ARoundThatFoundNothing` test asserts "all
reviewers answered" first. C14: a tri-state `SettingsFile.TryRead` used by `PanelServiceHost.Current` on the RELOAD path
only — `Busy` keeps the last good settings and the old stamp; malformed and empty behave as today; `Layer`'s eight
callers unchanged; `Current` never throws; the sweeper test passes its own homes. D4: the two empty folders register with
the round's `RoundScratch` (`RosterBuilder.cs:107`, `SecurityRoster.cs:30`/`:155`) and are removed by its `Dispose` at
`RoundEngine.cs:360` — **non-recursive, only when EMPTY** (§6 D4, cadence consult 589e4647): a folder that is not empty,
or cannot be removed, is warned about and left to the six-hour sweep; best effort, never failing the round. C18's
product angle (§4 C18): `AtomicFile.MoveOver` (`AtomicFile.cs:49-63`) also retries `UnauthorizedAccessException` inside
its window; `AtomicJson.WriteUnderTurn` (`Escalations.cs:355-360`) writes through `AtomicFile.Write` with one shared
500 ms window; `QuestionFanOut.HeartbeatAsync` (`:302-317`) says a failed beat and goes on. C25 (§4 C25):
`SessionStore.Write`'s in-place write (`SessionStore.cs:592`) is retried by the loop `MoveOverExisting` already has
(`:619-639`), extracted once. Scenarios S7, S8, S9, S10, S24, S25, S26.

**Depends on.** C2 (S1) on nothing — it starts beside E1 (J7); C14 on E1 (the measurement rule); D4 on E1's C10 rewrite
(the repair test asserts on the launch, or D4 turns it red); C18's product angle and C25 on nothing but E1 (each has its
own files and a deterministic red). None of them waits for `PLAN_a_turn_waits_by_the_clock.md` (§7).

**Done when.** S8 red on both arms then green on every OS; S9 green, watched red once by breaking the guard; S10 red
(defaults under the new stamp on the busy read) then green on Windows, its two companions green throughout; S7 red
(both folders still exist) then green, with the non-empty (kept, warned), the cannot-remove and the grandchild arms;
S24 red (`UnauthorizedAccessException` on the first move, no pause) then green on Windows, its two guards green
throughout; S25 red on both arms (the CI sentence; no beat after the failed one) then green on every OS; S26 red (the
E2 sentence, no pause) then green on Windows, its guard green throughout; the four C2 tests not seen in the next 20
macOS runs, and `TheHeartbeatIsRewritten_…`'s exception form and `EightWritersOnOneSession_…` not seen in the next 20
Windows runs (watches, §10).

**S1 (C2) status: MERGED in #893 (`e86fe17f8`, 2026-10-20); the 20-run macOS watch is running** — see C2's Outcome.

**S4 (C18's product side) Outcome (2026-10-20, branch `feat/suite-e3-product`; the tests, their reds and the break-its are in `research/module_tests.md`, section "The suite is deterministic — E3 S4").**
Built as the lead's decisions, the E3 plan round and the risk consult (the last three sections of this plan) say. `AtomicFile.MoveOver` retries `IOException or UnauthorizedAccessException` inside
`AtomicFile.SharingRetry` (500 ms, one constant; `ConsultHealthPaths.SharingRetry` points at it), measured on the monotonic clock; `AtomicJson.WriteUnderTurn` writes through `AtomicFile.Write`; the heartbeat's missed beat is said at
Warning and skipped (`QuestionFanOut.BeatAsync`); S27 is in `AtomicFile`. Deviations, each decided on what the code showed:
- **The seam is an `internal sealed record AtomicSeams`** (`Clock`, `Pause`, `IsAlive`, `AfterTemp`) on an `internal` `Write` overload, not a lone `Action<int> pause` parameter: the monotonic window, the orphan rule's age and liveness, and "a writer paused after its temp file" each needed a seam.
- **S25 injects the failure at the store's write road** (`QuestionConsultStore.FileWriter`, an `internal` init seam) on every OS, plus one Windows arm with the real hold, because a share-mode hold is Windows' alone and the catch must be proved on every leg. Added arms: the sweep keeps a question whose beats are refused while its pid is alive; a refused FINAL write still throws.
- **`ProcessLiveness` moved** from `CoaiMcp.Server` to `runners/Processes` (public); nothing was copied. `PanelService` already imported the namespace.
- **`MovableClock`** (test helper) gained a hand-moved monotonic reading (`GetTimestamp`, `TimestampFrequency`) so a window is movable without a sleep.
- **S27's name check is exact:** `<record>.<pid>.<guid:N>.tmp`; a name that merely starts with the record's, or has no pid, is never touched.
- **The orphan rule runs on every `AtomicFile.Write`**, so every caller of `AtomicFile` and `AtomicJson` (including S5's session write) gets it; it enumerates `<record>.*.tmp` in the record's folder only.
- **Not done here:** `SessionStore` is S5; the extension's "interrupted" after two minutes without a beat is unchanged and now named in `module_extension.md`.

**S5 (C25) Outcome (2026-10-21, same branch; tests, reds and the 20-run numbers in `research/module_tests.md`, section "The suite is deterministic — E3 S5").**
Built as the plan round and the risk consult say, not as the first brief: `SessionStore.Write` has ONE road, `SessionTurn.Take` then `FileWriter(file, json)`, whose default is `AtomicFile.Write(file, json, AtomicFile.SharingRetry)`; `MoveOverExisting` and `TryDelete` are removed (no second retry loop); the turn stays held through the move; `SessionTurn.Take` is untouched. Deviations:
- **The seam is `SessionStore.FileWriter`** (`internal` init), not a `BusyPause` property: the pause lives in `AtomicSeams`, so the store needs only the write road. `AtomicSeams` gained `WriteText` so S26 can fail the temp write half way.
- **S26 has four arms and a guard** (the temp write fails; the move fails; the busy file lands after release; the turn is held through the move; held-through fails and keeps the old bytes), not the brief's two.
- **The §10 watch was run:** 15 of 20 runs of `SessionStoreConcurrencyTests` failed before, 0 of 20 after, each beside a loaded suite. The 20 Windows CI runs of the plan's watch are still to come.
**S2 (C14) status (2026-10-21): built on `feat/suite-e3-s2` (CI green), merged into `feat/suite-e3-product` for the epic's one code gate** — see C14's Outcome.
**S3 (D4, S7) status (2026-10-21): built on `feat/suite-e3-s3` (CI green), merged into `feat/suite-e3-product` for the epic's one code gate** — see S3's Outcome below.

**S3 Outcome (2026-10-21, branch `feat/suite-e3-s3`; the tests, their reds and the break-its are in `research/module_tests.md`, section "The suite is deterministic — E3 S3 (D4)"; the round scratch is in `research/module_server.md`, "The two folders that are always empty").** Built as written in §6 D4:
`RoundScratch.Owns(folder)` registers a folder on a registry that `With(...)` carries (one `ConcurrentQueue` through the constructor); `RoundScratch.None` takes nothing. `RosterBuilder.FrameFor` registers
`repairDir` and, through `RosterFrame.OwnTheWorkspace`, the `coai-noworkspace-` folder (never the worktree path); `SecurityRoster.Append` carries `ordinary.Scratch` in its `Decided` record to `Build`, which
registers the lane's folder. `Dispose` removes each by its recorded path with a NON-recursive `Directory.Delete`, after the scratch's own folder and its homes; `IOException` / `UnauthorizedAccessException`
say one sentence ("… was not removed (…); the six-hour scratch sweep takes it") and leave the folder; a missing folder is nothing; nothing is thrown. `coai-answers-` and `PanelService.cs:1381` / `shared/temp-sweep.json` are unchanged.
**Deviations.** (1) The brief's break-it "drop the `With` carry → the main test red" is not reachable at round level: a round's scratch goes through `With` only when a runtime asks for a snapshot (Vibe, Kimi), and the S7 round has none. That break-it turns
red the unit test `TheRegistry_IsCarriedBy_With_…`; the round-level main test stays green. (2) The "cannot be removed" arm holds a Windows directory handle (`FolderHolds.Undeletable`; it needs a real access right — an attributes-only open takes no part in sharing,
which a first attempt found: the removal succeeded) and is skipped on Unix at round level; its Unix twin is the scratch's own unit test, where the folder's PARENT is made read-only (skipped when the process is privileged). (3) The grandchild is a real `FakeCli cwd-hold` child
(a new verb, handled before the vendor mode) started by the test's launcher hook in the repair folder: it says `ready <pid>` on its pipe (the event), closes all three pipes and waits; the test kills it by PID. Windows leaves the folder with one warning; the Unix assertion
(folder gone) is written but was NOT run here — the Linux and macOS CI legs are its first run. (4) `PanelServiceTests` became `partial` so the S7 round arms share its service, git fixture and the C10 recorder (`SeenRepairs` gained a `BeforeARepair` hook; the C10 round is now `ARepairedCodeRoundAsync`, with the
worktree-mode assertion kept in `RepairLaunchesOfACodeRoundAsync`, so the positive control still fails in `none` mode). (5) One extra test beside S7: `TheEmptyFoldersOfTheOrdinaryRoundAndOfTheLane_AreAllRemovedWithTheScratchTheRosterWasGiven` (`SecurityLaneRoundTests`) proves the lane's folder is registered.

**Size.** Five stories: S1 C2 (the launcher helper, S8 both arms, S9, the EndToEnd assertion; the brief pins the
tracker stand-in's two observations and that the Team server's reader marker is cleared by the launch's normal end) —
first, beside E1; S2 C14 (`TryRead`, the host's catch, S10 and its companions, the homes fix) — parallel; S3 D4 and S7
(after E1 S3; the brief names `RoundScratch` as the end); S4 C18's product angle (`AtomicFile`'s retry and its pause seam,
`AtomicJson` onto `AtomicFile`, the heartbeat's missed beat, S24, S25) — parallel; S5 C25 (the store's pause seam, the
extracted retry, S26) — parallel. The five touch disjoint files (S4's `AtomicFile` has seven other callers, three of them with a
retry window, which is why S4's brief lists them). Product code: Opus brief, Sonnet code, Opus review. No cornerstone. **Risk:** § *Review
record*, "E3's risk".

### E4 — Deadlines on an injectable clock — cornerstone area: the clock contract

**Goal.** Every deadline a test must race is armed through a `TimeProvider`; the product's default behaviour is unchanged;
the tests that race a deadline advance a manual clock when the thing they mean has happened, and no fixed sleep decides
an outcome.

**Scope.** D3, one class at a time, each WITH its RED (J8): `AcpClient` (`:119`, `:195`) with C5; `ConsultantPlaces`
(`:334`) with C17; `TurnLoop` (`:41-45` — the `Stopwatch` goes — and `:134-138`) and `SourceResolver` (`:200`) with
C13; then C6 on `TurnLoop`'s seam (with the pure `RoundDeadline.For` test and the sweep-reset half, S4's second rule).
Each seam is an optional `TimeProvider? clock = null` constructor parameter on the class that owns the timer
(`BoundedScheduler.cs:96-102`); nothing is threaded through `PanelService` or `RoundEngine`; `RoundEngine.cs:286` gets
no seam. **`ProcessLauncher` is left out** until C4 has a kept failure (D5) — then C4's story lands the launcher's seam
with S15. One manual clock with timers, monotonic timestamps and disposal, shaped on
`wsl_care · src_daemon/tests/WslCare.TestSupport/ManualTimeProvider.cs` (the suite's `FixedClock`/`MovableClock` stay
for wall-time-only tests). Scenarios S12, S13, S14, S16 (S15 gated). `PLAN_two_equal_deadlines_race_on_macos.md`
(C8, S21) reuses the manual clock and C13's `SourceResolver` seam and adds `SecuritySources`' own.

**The cornerstone area** is the seam's CONTRACT, applied per class: (1) every elapsed calculation, delay and
cancellation in that class reads the same clock — the spec lists every `Stopwatch`, `DateTime.UtcNow`, `Task.Delay`,
`CancelAfter` and `WaitAsync` on the four classes and which clock each reads after; (2) `CreateLinkedTokenSource` +
`CancelAfter` has no `TimeProvider` overload, so the timer becomes `new CancellationTokenSource(delay, clock)` linked in,
and `WaitAsync(timeout, clock)` replaces `WaitAsync(timeout)`; (3) the default is `TimeProvider.System` and the whole
suite is green with the seam in and the test not yet moved — proved inside each story, before its test moves; (4) the
manual clock fires timers in order and never runs a callback under its own lock; (5) what the launcher's three
separations must keep, written down now for the gated C4 story. Fable writes this spec; Sonnet codes; Opus reviews.

**Depends on.** E1 (the measurement rule). Not on E2, and not on E3's S1 any more (the launcher is not touched here).

**Done when.** S12 red (`found AcpEnd.Deadline`), S16 red (`0 of 2 copied`), S14 red (no terminal `TimedOut` with the
cap removed), S13 red (cut at turn 3 with a one-turn budget) — each then green, each with the suite green on its seam
alone first; the loops of §10 recorded per case; C5, C6, C13, C17 not seen in the next 20 CI runs of their legs (a
watch, §10); no timeout lengthened; `ProcessLauncher` unchanged unless C4's gate opened.

**Size.** Five stories. S1 the manual clock alone (the cornerstone's shared piece, Fable spec; first, small). Then S2 C5
(`AcpClient`'s clock + S12), S3 C17 (`ConsultantPlaces`' clock + S16), S4 C13 (`TurnLoop` + `SourceResolver` clocks + S14)
— parallel after S1, different classes and test files; S5 C6 (after S4, on `TurnLoop`'s seam, + S13). C4 is a sixth
story only when its gate opens. C8's own plan's step 3 starts after S4 merges.

### E5 — Tests that wait for an event

**Goal.** Eight tests and one CI job stop measuring the scheduler: each waits for the event it means, or measures the
cost it means (CPU), and the clock leaves the outcome.

**Scope.** C16 (`ApiEndpointStub.Dispose` waits for in-flight handlers, bounded, with a sentence), C3 (the probe released
at the shim's first busy answer through `WhenTheHomeFileIsBusy` on the Kimi call site, `KimiCredentialsLock.cs:209` —
never inside `HeldFile`; the shim's take staying INHERITABLE), C18 (beats seen through the store's `projected` callback,
the row released after three), C15 (launch 2's recorded timeout asserted directly), C9 (`replayingFan`'s CPU bound; the
real-child smoke test keeps exit 0 and every character and waits on `onExit`/`onError` with no 10 s timer), C21 (the kill
only after the parent's `comm` reads `sleep`), C22 (`run-host.mjs`: the sentence names an editor that exited before the
host ran a scenario and quotes VS Code's last main line; the download out of the launch budget; one retry of the LAUNCH
only, said in the log and counted by the streak step), C7 (a kept failure per D5, else the slow step sized from a
measurement). Scenarios S11, S17, S18, S19, S20, S22, S23.

**Depends on.** E1 only. Nothing here needs the clock seam, so E5 may run BESIDE E2–E4; its number is 5 because its
stories are the smallest value each.

**Done when.** S11, S18, S19, S20, S22, S23 red with their §8 sentence then green; S17's 100-run loop before and after
recorded; C7 fixed from a kept stack or closed with the recorded measurement; C3, C9, C15, C16, C18, C21 not seen in
the next 20 CI runs of their legs and C22 not seen in the streak step's next 20 runs of `main` (a watch, §10); C9's,
C15's and C22's PRs each say why the new assertion is at least as strong as the old.

**Size.** Eight stories, all parallel, one coder each, different files. C7 starts as a scout (collect the failure or
measure the step) and becomes a coder story only if a cause is shown. No cornerstone.

### Order, and what runs beside what

E1 first; **E3's S1 (C2) and the sibling's C8 step 1 start at once beside it** (J7); E2 → E3's rest → E4 in series for
their first stories; E5 starts after E1 and runs beside the rest. `PLAN_two_equal_deadlines_race_on_macos.md` (C8)
step 3 goes after E4's S4 (C13's `SourceResolver` clock); `PLAN_a_turn_waits_by_the_clock.md` (C19) needs no seam and
may go at any time, calling `HeldFile.TakeWithin` rather than writing a second wait. A case is reported closed when its
RED is green (or its loop recorded), and WATCHED for 20 CI runs of its leg after the merge — the watch reopens, it does
not close (§10, J15).

## Review record (2026-10-19)

The plan gate (coai session `f6808c2f`, verdict **proceed**) raised three findings; the lead accepted all three. An
independent Opus judge raised fifteen. Every claim below was re-read against `origin/main` `38ecd4e11` before it was
folded; where the code said otherwise, it is written here instead of in the plan.

### The gate — 3 findings, 3 accepted and folded

| # | Finding | Folded as |
|---|---|---|
| G1 | D1: inside one run every writer of the steering file must stay in the serial `fakecli-env` collection — a structural test that fails when one is outside | C1 RED (third test), S2b, E1 S2 |
| G2 | D2: TEMP/TMP/TMPDIR set ONCE before any test, never toggled — a `[ModuleInitializer]`, the assembly fixture only removes and restores | D2 rewritten; S5's `MemberData` arm; E2's never-toggled proof. Checked: `AVibeHomesNeverLiveUnderTempTests` is the one class that moves TEMP (`:15-60`), in `fakecli-env`, restoring what it found — it stays |
| G3 | C14: `PanelServiceHost.Current` must never throw to its callers | C14 fix: `Build(first: false)` under a `try` that keeps `_current` and the old stamp; S10's `CurrentNeverThrows` companion |

### The judge — 15 findings

| # | Finding | Decision | What the code said |
|---|---|---|---|
| J1 | BLOCKING — D2's run folder vs leftovers-E5's owner claim and sweep | **Folded.** The run folder writes E5's claim in E5's exact format; one writer, landing order in §7 of this plan; the E5 amendment recorded as an open item for E5's owner (its plan is on `docs/test-leftovers-e5-stories`, PR #850, not on this branch's base — not edited here) | The format is `<host>@<os>:<pid>[:<start>]` — `OWNER_TEXT` in `dew_flow_conventions · tools/lib/owned.mjs`, which on 2026-10-19 exists only on branches `e1/r3-*`, not on that repository's `main`; the pinned submodule's `test-leftovers.md` has no owner section yet. The E5 plan itself says it is "blocked on the E1 release of the conventions". So "reuse E5's helper if it exists on main" cannot be true when E2 starts unless E5 S3 lands first — hence the two-way landing rule |
| J2 | BLOCKING — the two "never under TEMP" guards must compare against the REAL temp | **Folded.** `RunTemp.Real`; both guards read it; S5b; the E2 scan of every other `GetTempPath()` assertion, with its verdicts in D2 | Confirmed: `AntigravityReadOnly.DefaultBase` (`AntigravityReadOnly.cs:48`) and `KimiCredentialsLock.Root` (`KimiCredentialsLock.cs:87-91`) are `LocalApplicationData` paths; under D2 both guards would compare against the run folder — green, but blind to a root that moved into the real temp. Six other `GetTempPath()` assertion sites found and judged (D2) |
| J3 | C14 — tri-state on the RELOAD path only; `Layer`'s callers keep today's behaviour; only busy keeps last-good; malformed and empty as today | **Folded** as the lead decided, with the blast radius | `SettingsFile.Layer` has exactly 8 call sites (listed in C14); the catch at `SettingsFile.cs:90-93`; the length-stamp that heals torn reads is `PanelServiceHost.Stamp`, `PanelServiceHost.cs:152-157` (the judge's `:152-157` is that file, not `SettingsFile.cs`). The first draft's "malformed keeps the previous settings" is withdrawn |
| J4 | Add the shared-state case: `ForgetTheLastSweep()` outside any collection resets the static `_sweptUtc`, so the next `BuildWork` walks the real %TEMP% inside a round deadline; C6 to medium | **Folded.** C6 cause, confidence medium, fix (3), S4's second rule | Confirmed: `_sweptUtc` `PanelService.cs:1407`, throttle `:1424`, reset `:1440-1444`; seven callers (`AScratchSweepRunsOnTheConsultationBeatTests.cs:57`, `:112`, `:141`, `:150`, `:167`, `VibeHomesTests.cs:154`, `TempDirsAreSwept.cs:286`), none in a collection |
| J5 | C9 — the smoke test also needs an event-only wait; the 10 s `ended()` timer is what failed | **Folded.** S20 | Confirmed: `ended` `processLauncher.test.ts:22-34`, `nothing arrived within ${ms} ms` |
| J6 | C2 — the helper covers `Win32Exception` AND `InvalidOperationException`; say what the tracker/reader marker records; S8 tests both | **Folded.** C2 fix and RED (two arms), S8 | `IProcessTracker.Record(pid, startedUtc, label)` (`ProcessTracking.cs:10`) — nothing is recorded for an exited child; the reader marker (`ReaderMarkerDto`, `ReaderMarker.cs:65-70`) stays `spawning` until the launch's normal end clears it; `ITreeSupervisor.StartedUnidentified(int pid)` (`:21`) is the existing verb for a live pid with no start |
| J7 | Build order — C8 (step 1, no clock seam) and C2 (E3 S1) start at once beside E1 | **Folded.** §7 order, §9 item 1, E3 and Order | — |
| J8 | E4 — no all-at-once seam; `TimeProvider? clock = null` per owning class, landed WITH its RED; never through `PanelService`/`RoundEngine`; `ProcessLauncher` left out until C4 has a kept failure; `BoundedScheduler.cs:96-102` precedent | **Folded.** D3, C4 gated, C5/C13/C17 fixes, E4 rewritten (five stories, the manual clock first) | Confirmed: `BoundedScheduler.cs:96` `TimeProvider? clock = null`, `:102` `_clock = clock ?? TimeProvider.System` |
| J9 | D4 moves to E3 (product): best effort, never fails the round; name the plumbing. (The judge's "recursive, never follows a link" was superseded by the cadence consult below: NON-recursive, only an EMPTY folder; a non-empty one is kept, warned about and left to the six-hour sweep — §6 D4) | **Folded.** D4 rewritten, E2 test-infra only, E3 S3, §7 row | The plumbing exists: `RosterFrame.Scratch` (`RosterAssembly.cs:24`) is a `RoundScratch` reserved per `BuildWork` (`RosterBuilder.cs:107`), carried in `RoundWork.Scratch`, and disposed by the round at `RoundEngine.cs:358` (`using var scratchOfTheRound = roundWork.Scratch`); `RoundScratch.Dispose` (`:94-124`) already deletes with `GitScratch.DeleteEvenIfReadOnly`; `SecurityRoster.Append(RoundWork ordinary, …)` (`:30`) reaches the same scratch for its `:154` folder. The frame paths do not leave `RosterBuilder`; the scratch does |
| J10 | Refresh stale evidence: #887 changed `ConsultantPlaces.cs`; add runs 39229540975 (C2 on `main`), 39253800395 (C18 2.9 s — weaken "starved"), 39278975228 (C16 active) | **Folded.** C17 lines (`:329`, `:334`, `:390`, `:416`, `:449-457`), §3 rows, C2/C16/C18 evidence | All three runs read: 39229540975 `main` macOS 09:22 UTC `Expected … Ok, but found … NotStarted` (the `APromptFile…` sibling — C2's 4th); 39253800395 Windows 12:51 UTC `beats.Count … >= 3` in 2 s 899 ms; 39278975228 Linux 15:36 UTC `NullReferenceException … FormatHeaders` in 1 s 135 ms. #887's stat: `ConsultantPlaces.cs` 204 lines; the test file `AConversationPlaceTests.cs` was not touched, but its budget line is `:134`, not `:132` |
| J11 | = G2 | Folded with G2 | — |
| J12 | D1 trade-off — decide "beside the binary" vs "TEMP-keyed after E2"; state the two-runs-of-one-bin limit | **Folded.** D1's new paragraph: beside the binary, with both limits stated | — |
| J13 | C3 — where the busy-answer seam goes: not inside `HeldFile.TakeWithin`/`TakeVerified`; prefer the Kimi lock's call site | **Folded.** C3 fix: the call site at `KimiCredentialsLock.cs:209` splits the take; `HeldFile` untouched | Confirmed: `take(…, HomeWait)` `:209`; `Inheritable` → `HeldFile.TakeVerified(path, within, inheritable: true)` `:175`; the busy loop is `HeldFile.cs:173` |
| J14 | Who adds the clock to `SecuritySources.cs:32` — the sibling plan or this plan's E4; consistent on both sides | **Folded, both sides.** `SourceResolver.cs:200` here (C13); `SecuritySources.cs:32` in the sibling, with S21; the sibling's §Boundary rows 1–2 and its "Disjoint" line say the same | Confirmed: `CollectionBudget` `SecuritySources.cs:14`, `CancelAfter` `:32` |
| J15 | Accuracy — "absent from 20 CI runs" proves nothing for ~1 % failures; fix moved lines `module_tests.md:4493` and D6's siblings `:502/:532/:557` | **Half folded, half refuted.** §10 and the DoD now say the REDs are the proof and the 20 runs a watch (0.99²⁰ ≈ 0.82); `module_tests.md:4475` → `:4493` in three places — correct. **Refuted:** the D6 sibling lines. On `38ecd4e11`, `PanelServiceTests.cs` has `coai-plan-said-` at `:352`, `coai-plan-rules-` at `:480`, `coai-plan-partial-` at `:518`, `coai-plan-none-` at `:542` — the plan's `:480/:518/:542` are right; `:502/:532/:557` match nothing in the file | `grep -n "coai-plan-" src_mcp/tests/PanelServiceTests.cs` on `38ecd4e11` |

### The cadence consult for epics 1-3 (589e4647) — 3 findings, all verified and folded
- **E2's cleanup on a class that is also a test class** — true (`TempDirsAreSwept.cs:5` is the assembly fixture AND holds
  eight Facts/Theories). Folded into D2: a dedicated `RunTempFixture` with no test methods.
- **C14 leaves a permission denial unmapped, and contradicts a test** — true (`SettingsFile.cs:90` maps
  `UnauthorizedAccessException` to empty settings; `PanelServiceHostTests.cs:113` asserts `Current` throws). Folded into
  C14: a denial is never `Busy`, it keeps today's answer; the `:113` assertion changes by design, its retry kept; S10 gains
  a denial arm.
- **D4 can remove a live grandchild's working folder** — true (`ProcessLauncher.cs:586-590`: descendants are listed and
  confirmed only for a request that ends its own child). Folded into D4: remove only an EMPTY folder, non-recursive; a
  non-empty one is left to the six-hour sweep; S7 gains a grandchild arm.

### The risk consult for epic 2 (299c4b94, 2026-10-20) — 4 findings, verified, binding on E2
- **E2 is built on `RunTempFixture`, a dedicated fixture with NO test methods** — E1's text above ("its assembly-start step lives
  in `TempDirsAreSwept` that E2 extends") is superseded for E2. The module initializer ALSO registers an idempotent cleanup at
  normal process exit, so a run that never builds the fixture — `--list-tests`, `--help`, a filter that matches nothing — leaves
  no run folder and no `.owner` file. Those three runs are scenarios of E2, each asserting both are gone.
- **The two-field owner claim on Windows and macOS (no start time) can make a dead run look alive after its pid is reused.**
  That is a LEAK, never a deletion; a claim whose identity cannot be established keeps its folder. The family janitor's
  one-hour age rule is safe only while no run is in flight. Both limits are stated in D2 and in `module_tests.md`; a
  deterministic test feeds the sweeper a live pid with an older recorded start and expects the folder kept.
- **Initializer ordering: no early `GetTempPath()` capture was found**; S5 proves it twice — a static field captured in the test
  assembly, and the temp path a real confined child is given.
- **The extension gets its OWN per-invocation folder**, shared by its `node --test` descendants and never the C# run's; proven
  with a child and a grandchild, cleanup on success and on failure, and two runners at once (removing one leaves the other's
  folder). Interoperability with E5's real Node sweeper is a deferred integration proof until E5's helper exists.
- **Case C24 (added 2026-10-20, from another session's PR #876):** `SupervisedTeardownLinuxTests.AGrandchildBornAfterTheFirstListing_IsFoundByTheReListing`
  failed once on Linux with the late grandchild's `/proc` state still `R` (expected gone). Timing, belongs to E5; root cause to
  be read from its log before a fix.
- **Case C25 (added 2026-10-20, seen in E2's own full run):** `SessionStoreConcurrencyTests.EightWritersOnOneSession_DoNotDenyEachOther`
  failed once with an `IOException` (the session file in use) during a 38-minute full run on a loaded Windows machine, and
  passed 3 of 3 alone (4–7 s). The test asserts that concurrent writers of ONE session never deny each other, so a sharing
  violation under load is a candidate PRODUCT defect (the store's write path), not a test defect. Not touched by E2. Belongs to
  E3 (product) once its cause is read: the store's open mode and retry, against the test's eight writers. **Read on
  2026-10-20:** §4 C25 (the in-place write has no retry; measured), built as E3 S5.
- **Also seen once in E2's runs, passing alone, already in the census:** `AReviewerThatAsksForSourceIsAskedAgainTests` (C13, E5),
  `AStreamsUsageIsNeverZeroTests.A_stream_over_the_answer_cap_is_unknown` (exit 69 vs 70 under load — to be read with C13's
  family), `AReaderIsEndedWithItsTreeTests` (C4), `ConsultationSweeperTests` (C14), and the real-editor job (C22; a different
  scenario, "a deletion the mirror carries…", on 2026-10-20).

### The E2 plan round (coai session `8fd6b7ff`, 2026-10-20, verdict good_enough) — 13 findings: 11 accepted, 2 rejected

Every accepted finding was re-read against `origin/main` `e86fe17f8` (#893) before it was folded. Numbers are the round's.

| # | Finding | Decision | Folded as / the lead's reason |
|---|---|---|---|
| 0, 4 | E3's scope and the J9 row still said D4 removes the folders "recursive, never following a link", contradicting §6 D4 (non-recursive, only when EMPTY) | **Accepted** | E3 scope and J9 row corrected; S7 gains `AFolderThatIsNotEmpty_IsKeptAndWarnedAbout_AndTheRoundStillEnds`; E3's done-when names the three arms |
| 1 | C16: say what happens when the bounded handler drain expires — the listener must never stop while a handler writes headers | **Accepted** | C16 fix: abort the in-flight contexts first (`HttpListenerResponse.Abort()`, the call `ApiEndpointStub.cs:183` already makes), drain, then `Stop()`; S11's second arm — a handler still active past the bound. Verified: `Dispose` (`ApiEndpointStub.cs:219-229`) tracks nothing today |
| 2, 12 | C22: retry only when the harness can ESTABLISH the host never started a scenario (an explicit signal, not "no scenario line"); and never fail silently — a `::warning::` annotation naming VS Code's last `[main]` line, counted by `host-job-streak.mjs` | **Accepted** | C22 fix (3) and (4), S23's two arms, the DoD line. Verified: `scenarios.ts:976-990` prints only `ok`/`FAIL`/`N/N scenarios passed` — a host that hangs before its first scenario prints the same nothing as one that never started; `host-job-streak.mjs:266-277` reads job conclusions only today |
| 3 | The status line still said nothing is implemented | **Accepted** | Status line dated 2026-10-20: E1 and C2 merged in #893 (`e86fe17f8`), E2 in progress, E3–E5 open, the §10 watch running |
| 5 | S3 (the decoy repair test) was dropped by the judge at the E1 code gate but is still in §8, E1's done-when and the DoD | **Accepted** | Removed from all three, each pointing at E1's Outcome (judge item 4) |
| 6 | §2 goal 2 made "20 clean CI runs" part of what closes a case | **Accepted** | Goal 2: closed by the deterministic RED turned green (or its recorded loop), then WATCHED for 20 runs; the watch can reopen a case, never close it |
| 7 | §10 "the same test count" would also be true of a suite that lost one test and gained one | **Accepted** | §10 and E2's done-when: no test removed or skipped; the count grows by exactly the new scenarios, named per epic |
| 8 | §11: after leftovers-E5's sweep replaces the janitor, a killed Windows/macOS run can leak its folder (two-field claim, pid reuse) until the claim carries a start time, unless an age backstop stays beside the liveness sweep | **Accepted** | §11 states the caveat and decides: KEEP an age backstop in DAYS beside the liveness sweep; owner: the E5 plan's open item (§7 claim row); D2 points there |
| 9 | The DoD put C22 under "loops recorded before and after", which it cannot have | **Accepted** | C22 excepted: its evidence is S23 red → green plus the streak step's annotation count over 20 runs of `main` |
| 10 | D2's `[ModuleInitializer]` setting `TEMP`/`TMP`/`TMPDIR` through `Environment.SetEnvironmentVariable` would not reach `Path.GetTempPath()` or child processes on Windows | **Rejected** | On Windows .NET writes the process's NATIVE environment block (`SetEnvironmentVariableW`), and `Path.GetTempPath()` reads `TMP`/`TEMP` at call time; children are started from that block. `AVibeHomesNeverLiveUnderTempTests.cs:43-48` already depends on exactly this and asserts it (`"the fixture really moved TEMP"`), and E1's S1 started a child on the moved variables; S5 proves it again from a class fixture, a `MemberData`, a static field and a real confined child |
| 11 | With D2 in place the steering file could be TEMP-keyed instead of beside the binary | **Rejected** | The trade-off was weighed and decided in J12 (D1): beside the binary works before E2 and does not depend on any launch's allowlist; E1 shipped it in #893 |

### One finding of our own, from reading the C22 log

The report that reached this plan said the real-editor job "failed although the scenario passed" (`1/1 scenarios
passed`, exit 0, `Unexpected SIGPIPE`). The job log says otherwise: those three lines are the SECOND launch's (the
missing-root launch, one scenario, passed); the FIRST launch printed no scenario line at all, VS Code logged
`CodeWindow: detected unresponsive` and exited 1, and the harness reported exactly that (`Test run failed with code 1`).
The job's pass/fail rule worked; what was missing is a sentence that tells the two apart — C22.

### The E2 code gate and judge (coai session `8fd6b7ff`, 2026-10-20) — verdict `proceed` from both; the accepted findings folded into the branch

Each accepted finding that changes behaviour was red first and has its break-it watched red (`research/module_tests.md`, "Code gate and judge (2026-10-20) — what changed after review"). Numbers are the gate's (`G…`) and the Opus judge's (`J…`).

| # | Finding | Decision | Folded as |
|---|---|---|---|
| J1 (major) | The claim's host: C# `Environment.MachineName` (`BUILD01`) vs Node `os.hostname()` (`build01`) — a Node sweeper would judge every C# claim "another host" and keep it for ever | **Accepted** | `Dns.GetHostName()`; a live test asks Node for its own spelling (red `"BUILD01" differs near "BUI"`); the reader compares the host without regard to case |
| G0/G1 | Two writers of one format with no check that they agree | **Accepted** | `shared/owner-claim-vectors.json`, read by a C# test and a Node test; break-it: change one side → only that side red. **E5's reader must pass the same vectors** |
| J2 | The Linux start field was computed two ways (about a second apart) | **Accepted** | one formula both sides (`/proc/self/stat` field 22 ÷ 100 + `btime`), pinned by the vectors; a start from any other source needs a ±2 s tolerance |
| G13/G17 | `Verdict`'s `liveStartUnixSeconds` accepted and ignored | **Accepted** | parameter removed; a live pid always keeps; comparing a start is E5's, with its own precision rule |
| G2 = J4 | `CreateDirectory` succeeds on an existing folder: two runs could share one | **Accepted** | exclusive create with a bounded redraw, through a seam (C#); redraw on `EEXIST` (Node). Limit stated: check-then-create is two calls |
| J3 | The `.owner` was removed even when the folder could not be | **Accepted** | claim deleted only once the folder is gone, both sides |
| G18 | Claim deleted with a hard-coded `File.Delete` | **Accepted** | its own seam; a failure names the file |
| G8 | Trimming the temp path leaves `""` for `/` and `C:` for `C:\` | **Accepted** | `RunTemp.Trimmed` keeps a root |
| G14, G15 | The creation and claim warnings did not say the consequence or what to do | **Accepted** | `CreationWarning` (path, "WITHOUT isolation", make TEMP writable or point TMP/TMPDIR elsewhere), `ClaimWarning` ("only the janitor's age rule, after one hour") |
| J5 | The janitor matches directories only: a killed run's `.owner` file stays | **Accepted (text)** | `RunTemp` remark and D2 corrected; E5's open tail gains "remove orphan `.owner` files whose folder is gone" |
| J6 | S5e allowed a trailing newline on Linux | **Accepted** | strict read (`IsWellFormed`, no `\n`/`\r`, digits only); break-it: the writer appends a line feed |
| J7 | The never-toggled proof was not built | **Accepted** | `NoTestMovesTempButTheRunTempInitializerTests`; limits and the three by-name exemptions stated |
| G3 | `node-compile-cache` in the machine temp | **Accepted (measured, then text)** | measured: one stable folder, 70 files, the same after one and three runs — node's own reused cache, not growth; the guarantee says "nothing of its OWN" |
| J8 | The done-when's test-count proof was missing | **Accepted** | base `e86fe17f8` and head counted the same way in throwaway worktrees; the delta and the names are in `module_tests.md` |
| 4 | `temp-sweep.json` wording | **Rejected** (the lead's decision) | the `why` carries the one sentence the spec asked for; `neverSwept` is untouched |
| 5 | A cross-repository CI check of the claim format | **Rejected** | one repository's CI cannot run another's; the golden vectors in this repository are the check, and E5's reader must pass them |
| 6 | Plant files for the product's sweep | **Rejected** | it would write into the machine's temp folder, the thing E2 removes, to test a product sweep E2 does not change |
| 7 | An opt-out switch for the run folder | **Rejected** | a switch would let a run silently share the folder; the only degraded mode is the fallback, and it now says so (G14) |
| 9 | Catch `Exception` in `Remove` | **Rejected** | `PathTooLongException` and `DirectoryNotFoundException` are `IOException`s, already caught; an unclassified failure should surface |
| 10–12 | Strip exception messages from the warnings | **Rejected** | the warnings go to the local stderr of a test run and carry paths the person needs to delete a leftover |
| 16 | Batch the four chat-file children into one | **Rejected** | one child per file keeps a leak attributable to its file; the cost is a process start each |

The decisions on 4–7, 10–12 and 16 are the lead's; only 9's reason came with it, and the reasons written for the others are the coder's wording of the decision, not the reviewers'.

**Open tail added by the gate and judge:** E5's reader must pass `shared/owner-claim-vectors.json` and compare the host without regard to case; E5's sweeper removes orphan `.owner` files whose folder is gone (the janitor matches directories only); a start comparison needs its own precision rule and a ±2 s tolerance against any start not computed by the shared formula; the exclusive create narrows but does not close the two-call race (stated).

### E3's risk (2026-10-20, written with stories S4 and S5) — what can go wrong, and what catches it

Every E3 story changes PRODUCT code on a path that writes a person's data or decides what a server runs on. What a
wrong change does, per story, and the test or rule that must catch it before merge:
- **A wrong atomic-write change loses a record or a session write (S4, S5).** A retry that swallows the last failure,
  or retries a step that already wrote part of the file, turns a refused write into a SILENT one: a question's settled
  record, a spent proof, an escalation answer or a session's round trail is gone with no sentence. *Caught by:* each
  retry is bounded and its LAST attempt throws (S24's and S26's never-released guards assert the exception and that the
  old content is still whole); only the heartbeat may skip a beat, and it says so (S25) — the settle and final writes
  keep throwing; `AtomicFile` still removes its temp on failure (S24's guard: no `*.tmp` left). S4 changes a helper with
  seven other callers: the whole suite runs green with the retry widened BEFORE `AtomicJson` moves onto it.
- **A busy-reload change pins stale settings (S2).** If `Busy` is read too widely — a permission denial, a malformed
  or an empty file — the server keeps the last good settings for ever and a person's change never arrives. *Caught
  by:* `Busy` is ONLY a sharing violation (C14); S10's denial arm must NOT keep the last good settings, its malformed
  and empty companions stay green, and the next call after the release must show the new file.
- **D4 deletes a live folder (S3).** A recursive delete, a delete by name glob, or a delete of a folder that is not
  empty can take a surviving grandchild's working folder or another round's files. *Caught by:* removal by the PATH the
  round recorded, non-recursive, only when EMPTY (§6 D4); S7's non-empty arm (kept, warned), cannot-remove arm and
  grandchild arm; a failed removal never fails the round.
- **Common to all five:** a retry or a catch that grows past complexity 4, or a `null` returned for "nothing", is
  refused at the code gate; no assertion is weakened and no timeout lengthened (§10); each story's PR names its RED, its
  break-it and the test count before and after.

### The lead's decisions on the E3 draft (2026-10-20)

The Opus draft of S4 and S5 asked five questions. Each is decided here, on a stated reason, so the coders do not stop:

1. **The orphan `*.tmp` of a killed write (S4, §11): accepted, no sweep now.** `AtomicFile` names its temp file with a
   GUID so two writers never share one; a process killed between the temp write and the move leaves one orphan. Growth
   is one small file per killed write, not per write, and the record folder already holds one file per record. A sweep
   is a separate deletion rule with its own risk (it must never take a live writer's temp); it waits until an orphan is
   actually seen. §11 names the limit.
2. **The boundary line of `PLAN_a_turn_waits_by_the_clock.md`: amended in this change** (its line 62): S5 changes the
   in-place write of `SessionStore.Write`, not `SessionTurn.Take`; the two plans touch different methods of one file.
3. **One "busy" predicate for S2 and S5: no, they stay separate.** S2's `Busy` decides whether a settings READ is kept
   (a sharing or lock violation only — a permission denial must still surface, or a broken ACL would silently pin old
   settings forever). S5's retry decides whether a WRITE is tried again inside a bounded window. Different questions;
   one predicate would make one of them wrong.
4. **S2's denial arm: an internal seam, not a real ACL.** A deny-read ACL is Windows-only, needs cleanup that can fail,
   and tests the OS rather than the mapping. An `internal` read seam (the same shape as the repository's other internal
   test seams, no public API change) feeds `TryRead` an `UnauthorizedAccessException`; the coder adds it without asking
   again.
5. **Stale line references in C14 and D4: re-pinned by the coder of each story** when that story opens the file; the
   briefs carry the current numbers. C24 keeps only its review-record entry until E5 reads its log (it is E5's).

### The E3 plan round (coai session `ac57ee60`, 2026-10-20, verdict proceed) — 2 findings, both accepted; they supersede parts of the text above

Only one of three reviewers read the plan: the Gemini preset received it cut off (`PLAN_INCOMPLETE` — the plan is now
~183 KB), and Kimi returned an empty answer. Codex read it whole. Its two findings are taken, and where this section and
the text above disagree, **this section governs**:

1. **C25 / S5 — a retried IN-PLACE write is not safe; the session write becomes atomic instead.** `File.WriteAllText`
   opens with `FileMode.Create`, so the file is truncated the moment the open succeeds; an I/O error after that loses the
   previous session, and a retry loop only repeats the risk. **S5 is changed:** the turn-held path stops writing in place
   and writes the same way the turn-less path does — a temp file beside the session, then the one shared, bounded move
   retry (`MoveOverExisting`'s loop, extracted once and used by both paths). A failure at any point before the move leaves
   the previous session whole. **S26 gains a fault arm:** a write that fails AFTER the temp file is written (a seam that
   throws at the move) leaves the old session byte-for-byte intact and removes its temp file. The C25 text and the E3
   Scope line that say "the in-place write is retried" are superseded by this item.
2. **The orphan temp files of a killed writer — an owner and a rule (supersedes decision 1 of the lead's decisions).** The
   owner is the writer itself: before `AtomicFile.Write` (and the session store's write) writes its own temp file, it
   removes temp files of THE SAME RECORD (`<record>.<guid>.tmp` beside it) whose last write is older than one hour —
   never another record's, never a fresh one, best effort (a failure to remove is ignored and retried on the next write).
   A live writer's temp lives for milliseconds, so one hour cannot take it. **Bound:** at most one orphan per record per
   killed write, removed on that record's next write after an hour; a record that is never written again keeps at most
   the orphans of the kills that hit it — at today's kill rate (an operator stopping a server mid-write) single digits per
   month. **Scenario S27:** an orphan temp file of the record, two hours old, is gone after the next write; a fresh one
   (one second old) and another record's old one are both kept.

### The risk consult for epic 3 (`0060db13`, 2026-10-20) — 5 points, each verified against the code, binding on E3

1. **The 500 ms retry window is a tolerance, not a guarantee** — verified: `AtomicFile.Write` arms its deadline with
   `DateTime.UtcNow + sharingRetry` (`src_mcp/runners/Files/AtomicFile.cs:40`), wall time. **S4:** the window is measured
   on a monotonic clock (`Stopwatch` / an injected `TimeProvider`), and S24 gains a never-released arm: the destination
   held through the whole window → the caller gets the failure, the ORIGINAL bytes survive, the temp file is removed.
   Retrying `UnauthorizedAccessException` does not bypass a real ACL: a persistent denial still throws, after the window.
2. **S5 — atomic replacement confirmed; the turn stays held through the move.** Verified (for the SERVER's readers only — the extension reads without the turn, see the E3 judge J1): session readers go through
   `ReadShared` (`SessionStore.cs:543`), which opens, reads to the end and disposes; nothing keeps a handle and re-reads by
   offset. S26's arms: a failure while the temp file is written AND a failure at the move each leave the previous session
   byte-for-byte; the next load after a successful write sees it. **And one helper, not two:** the session write uses
   `AtomicFile.Write` with S4's retry (S5 builds on S4, same coder, after it) — `SessionStore.MoveOverExisting`'s loop is
   REMOVED, not extracted, because after S4 it is a second copy of `AtomicFile.MoveOver` (the reuse rule).
3. **S25 must fail the WRITE, not the projection.** Verified: `projected` runs AFTER the file is written
   (`QuestionConsultStore.cs:51-56`), so throwing there tests only the catch. S25 injects the failure at the write (the
   S24 hold seam, never released, on a heartbeat-only write); a guard arm proves the server's sweep keeps a question whose
   heartbeat writes fail while its pid is alive (`Orphaned` needs a stale heartbeat AND a dead pid,
   `QuestionConsultStore.cs:179-182`). The extension's "interrupted" after two minutes without a heartbeat
   (`src_vs_code/src/questionConsults.ts:78`) is a display that recovers on the next published beat — accepted, named
   in `module_extension.md`.
4. **S27's one-hour rule could delete a live temp — corrected.** A writer suspended after closing its temp file but before
   the move (a debugger, a suspended process) can hold a temp older than an hour; age is scope, not proof of death. **The
   rule now needs proof:** the temp file's name carries its writer's pid (`<record>.<pid>.<guid>.tmp`), and a writer
   removes a same-record temp only when it is older than one hour AND that pid is not alive on this machine — the
   product's own `ProcessLiveness.IsAlive` (`src_mcp/src/Server/ProcessLiveness.cs:19`; if `AtomicFile`'s layer cannot
   reference it, it MOVES down to `runners`, it is never copied). A reused pid keeps the file: a leak, never a deletion.
   S27 gains the arm the consultant named: writer A paused after its temp is written, the temp aged past an hour, writer B
   writes the same record → A's temp is kept (A's pid is alive), and A's move then lands.
5. **S2 and RoundScratch are sound within their stated limits** (no change).

### The E3 code gate and judge (coai code gate and Opus judge, 2026-10-21) — verdict `proceed` from both; the accepted findings are folded into the branch

Findings, each with its decision and reason. Tests, reds and break-its are in `research/module_tests.md`, section "The suite is deterministic — E3 code gate and judge".

| Finding | Decision | Reason / what changed |
|---|---|---|
| G0 — `PanelServiceTests` became `partial` (`PanelServiceTests.EmptyFolders.cs`) | **Accepted** | The repo rule forbids `partial` as a split. The S7 tests are their own class, `TheRoundsEmptyFoldersAreRemovedTests`; the service, the git fixture, the fake's answer and the repaired-round recorder are one named unit, `CodeRoundHarness`, which both classes own. 24 tests before, 24 after. |
| G1/G2 (+ judge 3) — the orphan rule deletes when `IsAlive` is false, and `IsAlive` is false for "could not ask" | **Accepted** | `ProcessLiveness.Check` answers `Liveness.Alive` / `Gone` / `Unknown` (`ArgumentException`, `InvalidOperationException` = Gone; `Win32Exception`, `NotSupportedException`, a pid of 0 or less = Unknown); `AtomicFile` deletes on `Gone` only; `IsAlive` is built on `Check`, so the sweeps are unchanged. RED: an aged orphan with an `Unknown` writer was deleted. |
| G3 — a failing heartbeat warns on every beat | **Accepted** | One warning per run of misses, reset by a beat that lands. RED: two failed beats gave two warnings. |
| G8 — `catch (Exception e) when (IsRefusal(e))` | **Accepted** | `catch (IOException)` and `catch (UnauthorizedAccessException)` explicitly, same behaviour, `IsRefusal` removed. No new test: nothing observable changes; the S24 arms still cover it. |
| G11 — `PanelServiceHost.Current` calls `Stamp()` outside any try | **Accepted, with a correction** | `Stamp()` already caught `IOException` (so a file that goes away between `Exists` and `Length`, a `FileNotFoundException`, was already safe) and `UnauthorizedAccessException`. What still threw was a path the platform cannot stamp (`ArgumentException`, `NotSupportedException`, `SecurityException`); RED: a data directory with a NUL in it made `Current` throw `ArgumentException: Null character in path`. The stamp now keeps the OLD one for those too: "cannot tell" is not "the file vanished", and the "no file" stamp would reload to the defaults. |
| J1 — the extension reads `sessions/*.json` without the turn | **Accepted (docs only)** | A move over a held file is refused whatever the share mode, so one extension read held past 500 ms can fail a round's final save. Written in `module_server.md` (the "readers take the same turn" claim now says the SERVER's readers) and `module_extension.md`; point 2 of the risk consult above is corrected: true for the server's readers only. |
| J2 — no S26 test pins the store's default write road | **Accepted** | `TheStoresDefaultWriteRoad_IsTheAtomicOne_SoItSweepsAnOrphanOfItsOwnSession`: `new SessionStore(dir)`, an aged dead-writer temp of its session, `Save`, the temp is gone. Break-it: the default back to `File.WriteAllText` goes red. |
| J4 — `PanelServiceHost` logs the exception as text | **Accepted** | `_log.Warning(e, …)`; `Kept` takes the warning as an action so the exception travels first. RED: the logged event had no `Exception`. |
| J5 — doc drift | **Accepted** | `module_runners.md` said "four now" and listed three; `PLAN_a_turn_waits_by_the_clock.md` rewritten (one atomic road, `MoveOverExisting` deleted, `Take` untouched) with the constraint that `Take`'s budget stays above `AtomicFile.SharingRetry`. |
| An HResult split of `UnauthorizedAccessException` | **Rejected** | The exception is the same for a held file and a real ACL denial, and the window bounds both; telling them apart by HResult would couple the retry to one Windows code and change nothing for the caller. |
| The Kept message wording | **Rejected** | Wording only; the message names the cause and the retry. |
| Throttling the orphan scan | **Rejected** | The scan lists one record's `<record>.*.tmp` in one folder; the cost is negligible and a throttle adds state. |
| An async `AtomicFile` | **Rejected** | Every caller is synchronous and holds a turn; async would spread through all of them for a window of 500 ms. |
| Logging the best-effort orphan and missing-folder catches | **Rejected** | They are courtesies to a folder; a line per write would be noise, and the next write retries. |
| A fast in-place session path | **Rejected** | The in-place write is the defect (it truncates before it succeeds); two roads is what S5 removed. |

### Case C26 (added 2026-10-21, seen on PR #897's Linux Sonar job) — an inheritable lock descriptor caught by another test's fork

`HeldFileInheritableOnLinuxTests.ATakeWhoseStreamCannotBeMade_ClosesItsDescriptor_AndLeavesTheFileFree` failed once on Linux
(run 39473785590, the coverage job): `Expected HeldFile.Probe(LockFile) to be LockProbe.Free … because the descriptor leaked
with its flock … but found LockProbe.Held`, after 7 ms. The product closes the handle on the failure path
(`src_mcp/runners/Files/LinuxHeldFile.cs:52-69`), and the lock file is unique per test (`TempDir.For`), so neither the code
nor the path explains it. **Cause (inferred, not yet shown by a kept trace):** the descriptor is opened INHERITABLE by design
(the shim's tree hold, S-K5), and in the window between its open and its close another test in the same process forked a
child; the child inherited the descriptor and with it the flock, so the probe saw `Held` until that child exited. Shared
state across parallel tests — never a flake. **Belongs to E5** (with C21): the class must not run beside process-spawning
classes (a serial collection), or the probe must wait for inheritors to end; a deterministic RED holds a fork open across the
take's failure. Not caused by E2: it does not depend on where temp lives.
