# PLAN — E4 stories: deadlines on an injectable clock (cornerstone area: the clock contract)

> Status: **plan only, nothing implemented yet, 2026-10-21 — stories and the cornerstone spec by Fable.** Epic E4 of
> [PLAN_the_suite_is_deterministic_and_leaves_no_temp.md](PLAN_the_suite_is_deterministic_and_leaves_no_temp.md)
> (its `### E4`). Branch `feat/suite-e4-clock`, from E3's head `aab069ca5` (which carries E2); every line number below
> was read at that commit. Scope: `src_mcp` only — four product classes get an optional `TimeProvider` each
> (`runners/Acp/AcpClient.cs`, `src/Server/Consultation/ConsultantPlaces.cs`, `runners/Reviewers/TurnLoop.cs`,
> `runners/Feature/SourceResolver.cs`), one shared test clock, five test files, `research/module_tests.md`,
> `module_runners.md`, `module_server.md`. **Cornerstone: E4.S1** — the manual clock and the clock contract (§5); the
> parent's ledger line becomes `Cornerstones: 1 of 2 — E4.S1` when this document is committed.
>
> Related docs: [module_tests.md](../research/module_tests.md), [module_runners.md](../research/module_runners.md),
> [PLAN_two_equal_deadlines_race_on_macos.md](PLAN_two_equal_deadlines_race_on_macos.md) (C8, S21 — reuses the clock
> of S1 and the `SourceResolver` seam of S4), [PLAN_question_consultant_tails.md](PLAN_question_consultant_tails.md) §6
> (C6 and C13 moved from there).

## 0. How to read this

The parent plan is 204 KB and one gate reviewer received it cut off (`PLAN_INCOMPLETE` at 183 KB). This document is
written so a reviewer who reads ONLY it can judge E4: the goal (§1), the lessons from E1–E3 that bind this epic (§2),
the four cases with their evidence and cause re-verified against the code (§3), decision D3 (§4), the cornerstone — the
clock contract and the manual clock (§5), the scenarios with the RED each must show first (§6), the boundaries with the
other plans (§7), the stories (§8), the order (§9), the test plan (§10), what in the parent turned out stale (§11), the
questions for the lead (§12) and the Definition of Done (§13). Where this document and the parent disagree on a line
number or a mechanism, this document is the fresher reading and §11 says why.

## 1. Goal

Four tests fail in CI or in a loaded local run because a deadline they must race also pays something the test did not
mean — a process cold start, a git handshake, a temp-folder walk. Each one passes alone. The parent's rule: a test that
passes alone and fails in the suite is a defect, never a flake, and never a reason to lengthen a timeout.

When E4 is done:

1. Every deadline one of these tests must race is armed through an injectable `TimeProvider` on the class that OWNS the
   timer. The product constructs those classes with the default (`TimeProvider.System`) and behaves exactly as today.
2. A test that means "the deadline strikes while X" drives a manual clock and advances it when X has happened. No fixed
   sleep decides an outcome; no timeout is lengthened; no assertion is weakened.
3. The four classes obey ONE contract (§5): on each class, every budget that decides the outcome reads the same clock —
   a `Stopwatch` left beside an injected timer is the race again, with the tests green.
4. The suite has ONE manual clock with timers, monotonic timestamps and disposal, shared by E4, by the sibling plan
   (C8/S21) and by every later test that races a deadline.

## 2. The lessons from E1–E3 that bind E4

- **The machine is loaded by parallel suites.** Eight test threads, several agent sessions and a second worktree's run
  share one machine. A process cold start was measured at up to ten seconds under that load. A test that waits on wall
  time is exactly the defect class this epic exists to remove — so an E4 test never sleeps to make a deadline bite, and
  never asserts an elapsed wall time as its proof.
- **No timeout is lengthened.** A longer timeout moves the race; it does not remove it, and every second is paid on
  every run. A story that finds itself reaching for a bigger number has the wrong design.
- **Every red is seen before the fix.** Each scenario's RED is watched failing against today's code with the message in
  §6, then watched passing; both observations go in the PR. A test written green has no teeth; if the fix lands first,
  the fix is reverted once to show the red.
- **No all-at-once seam story** (the judge's J8, parent §Review record). A refactor with no RED of its own is what made
  the first cut of this epic a cornerstone for the wrong reason. Each seam lands in the story of the first RED that
  needs it.
- **The seam alone must leave the suite green** before the test moves (§5.3). E3 proved its widened retry on the whole
  suite before `AtomicJson` moved onto it; E4 does the same per class.
- **Test counts are named per story**: the count before, the count after, and the list of added test names that makes
  the difference. No test removed, none skipped.
- **The reuse rule.** E3 found a second copy of a move loop and deleted it instead of extracting it. E4 inherits a
  test helper (`MovableClock`) that already does half of what the manual clock does; §5.4 decides what happens to it so
  there are never two clocks for one job.

## 3. The cases E4 closes — evidence and cause, re-read at `aab069ca5`

### C5 — an ACP turn's 2 s deadline also pays the agent's cold start (TIMING, high)

**Evidence.** `AcpClientUsageTests.AUsagePromptThatOutlivesTheTurnsDeadline_ReturnsTheAnswerAtTheDeadline_NotAfterTheBound`
(`src_mcp/tests/AcpClientUsageTests.cs:47-58`) failed in CI run 39269879269 (Windows, 2026-10-19, a dependabot npm bump
that touches no C#), the only failure of 11,735 tests, after 2 s 025 ms:
`Expected outcome.End to be AcpEnd.Answered … but found AcpEnd.Deadline … the agent did not answer within the 2s this turn was given`.
It also failed in the full local run of 2026-10-18.

**Cause.** The turn's clock starts right after the agent process starts: `deadline.CancelAfter(ask.Deadline)`
(`src_mcp/runners/Acp/AcpClient.cs:119`, on a token source linked to the caller's at `:114`). The test gives 2 s
(`AcpClientUsageTests.cs:53`) and means "the deadline strikes while `/usage` hangs" (`usageHang` at `:50`); but those 2 s
also hold the fake agent's cold start, `initialize`, `session/new`, `set_config_option` and the FIRST prompt
(`AcpClient.cs:143-181`). Under load that took longer than 2 s, the deadline struck before the first answer, and the end
was `Deadline` — the product did the right thing for what happened. The test's subject — the usage bound at `:195`
(`WaitAsync(usageBound)`, 60 s in the test) is clipped by what is left of the turn, because the deadline token flows into
`AcpSession.CallAsync` (`AcpSession.cs:165`, `answer.Task.WaitAsync(ct)`) — never got to run.

**Fix (S2).** `AcpClient` gets the seam (§5.1 table 1); the test waits until the turn's deadline AND the usage bound are
both armed on the manual clock (the answer is in, `/usage` is pending), then advances 2 s. **Confidence: high** — the
end was `Deadline` at 2.025 s on a change that touches no C#, and the clock placement is visible.

### C17 — a one-second copy budget that must also cover git starting (TIMING, medium-high)

**Evidence.** `AConversationPlaceTests.CopiesThatOutrunTheRowsBudget_RefuseThePlace_SayingHowManyWereCopied_AndKeepNone`
(`src_mcp/tests/AConversationPlaceTests.cs:113-139`) failed once in CI (run 38817686874, Windows, 2026-10-15): the
refusal said `0 of 2 copied`; the test expects `1 of 2` (`:136`).

**Cause.** The budget is 1 s (`:134`), armed by `copying.CancelAfter(budget)` in `OneShotAsync`
(`src_mcp/src/Server/Consultation/ConsultantPlaces.cs:333-334`). Since #887 the flow asks git of EVERY root ONCE
before anything is copied (`GrantedAsync`, `:394-409` → `RepositoryFacts.AskAsync`, `:413`; "each source asked of git
ONCE, before anything is copied", `:389-392`), then copies in grant order (`CopiedAsync`, `:355-384`; `tally.Copying =
true` only at `:371`). On a loaded Windows runner the git phase alone passed 1 s; `copying.ThrowIfCancellationRequested()`
(`:416`) ended it, and the sentence said 0. The product's own sentence now names the phase (`Progress`, `:454-457`):
before the first copy it says *"the budget ran out while confirming 2 candidates with git, before anything was copied"*
— so the CI text "0 of 2" cannot recur as such, but the cause can. The slow root's `Thread.Sleep(3 s)` in the test
(`:124-130`, inside the Maker's `BeforeCopyOf`) is not the problem; the fast root's git phase is.

**Fix (S3).** `ConsultantPlaces` gets the seam (§5.1 table 2); the slow root's hook ADVANCES the clock past the budget
instead of sleeping, so the git phase and the fast copy run with no clock pressure and the slow copy is cut every time.
**Confidence: medium-high** ("0 of 2" proves the pre-copy phase outlived 1 s; its real cost was not logged).

### C13 — `AReviewerThatAsksForSourceIsAskedAgainTests`: three turn budgets that also pay a process start (TIMING, high)

All three in `src_mcp/tests/AReviewerThatAsksForSourceIsAskedAgainTests.cs`; none failed in CI, all failed locally.

- `AConversationThatOutlivesItsCap_IsOneTerminalTimeout_WithEveryTurnsUsageKept` (`:462-477`): each turn may take
  700 ms (`:467`), the fake sleeps 400 ms (`:466`), so a cold `FakeCli` start plus exit has 300 ms. A slower start kills
  turn 1 and `EarlierTurns` is empty — the assertion at `:475` fails with
  `Expected outcome.EarlierTurns not to be empty because the turns that answered before the cap are kept.` It **fails
  alone** too ("2 of 5 runs alone" on `main`, `research/PLAN_codex_tier_floor.md:171`) — no peer involved: the cost is the
  process start against a 300 ms margin.
- `AConversationCapOnTheWork_EndsTheConversation_BeforeTheDerivedOneWould` (`:484-497`): the same mechanism with a 900 ms
  cap on the work (`:490`) and a 300 ms sleep (`:488`); about 600 ms of margin.
- `AResolverThatTimesOut_RefusesThatRequest_AndTheTurnGoesOnWithTheRest` (`:523-537`): the resolver's per-file deadline
  is 300 ms (`:527`) for the deliberately slow `Big.cs` (`SlowShow`, 5 s, `:526`, `:540-545`) AND for the real
  `git show` of `Cart.cs`; one git start over 300 ms turns `Cart.cs` into "not served … timed out" and `:535` fails.

**Cause, in the product.** `TurnLoop` measures the conversation on a `Stopwatch` (`src_mcp/runners/Reviewers/TurnLoop.cs:45`,
read at `:74`, `:81`, `:84`); each turn's own timer is the LAUNCHER's `CancelAfter(request.Timeout)`
(`ProcessLauncher.cs:481-482`), bounded by what is left of the cap (`TurnLoop.cs:129-138`, arithmetic only).
`SourceResolver` arms each file's read with `deadline.CancelAfter(_readDeadline)` (`src_mcp/runners/Feature/SourceResolver.cs:199-200`),
and requests are served one after another in order (`ServeAsync`, `:96-104`), so `Big.cs` (first at `:528`) is refused
before `Cart.cs` is even asked for.

**Fix (S4).** `TurnLoop` and `SourceResolver` each get the seam (§5.1 tables 3 and 4). The two turn tests run through an
in-process launcher stand-in that advances the manual clock by a turn's simulated duration and answers `TimedOut` when
the request's timeout is shorter (the file already has an in-process launcher precedent, `TwoDecidedThenReal`,
`:443-456`). The resolver test's slow read waits on the clock; the test advances it while the slow read is pending, and
the fast read then runs with no clock pressure. **Confidence: high** for the first (fails alone, 300 ms margin),
medium-high and medium for the other two (no failure text kept).

### C6 — the round-deadline end-to-end test: a whole round under 8 s of wall clock (TIMING, medium)

**Evidence.** `AFeatureIsReviewedEndToEndTests.TheRoundsDeadline_IsScaledByTheFollowUps_SoAConversationIsNotCutByAOneTurnBudget`
(`src_mcp/tests/AFeatureIsReviewedEndToEndTests.cs:446-458`) fails "about 1 run in 3 on a clean `origin/main`"
(`PLAN_question_consultant_tails.md` §6) and failed in the full local run of 2026-10-18; never in CI. The one message on
record: the test ran 12–20 s and reported "not started … the round reached its limit" BEFORE the reviewer launched —
the round's deadline struck during the round's own setup.

**Cause.** The round's deadline is `ReviewerTimeout × (1 + followUps)` = 2 s × 4 = 8 s (`RoundDeadline.For`,
`src_mcp/src/Server/Rounds/RoundDeadline.cs:11`, `:26-27`), armed BEFORE the round is assembled
(`RoundEngine.cs:287-289`: `new CancellationTokenSource(budget)` at `:288`), so sha resolution, the worktree, the diff and
the outline are inside it. Each turn is a real `FakeCli` launch that sleeps 700 ms (`FAKECLI_SLEEP_MS`, test `:451`);
the test's own remark says the round takes 3–4 s idle — a 2× margin against a loaded machine, on wall clock. The
shared-state half the judge found (J4) — `PanelService._sweptUtc` (`PanelService.cs:1408`) reset by
`ForgetTheLastSweep()` (`:1441-1445`) from classes outside any collection, so a stranger's `BuildWork` walked the real
`%TEMP%` inside this round's 8 s — **is already closed by E1**: the seven callers sit in the serial `fakecli-env`
collection (`AScratchSweepRunsOnTheConsultationBeatTests.cs:63, 118, 147, 156, 173`, `VibeHomesTests.cs:160`,
`TempDirsAreSwept.cs:311`) and the scan's second rule fails on any new one
(`EveryClassThatSetsTheProcessEnvironmentTests.cs:34-35`, `:123`, `:146-156`). Under E2 (on this branch) the walk is of
the run's own folder. What is left is the wall-clock margin itself.

**Fix (S5) — redesigned, see §11 item 3.** The parent's S13 says the end-to-end test "advances a manual clock 700 ms per
turn". It cannot: `PanelService` constructs the scheduler with no clock (`PanelService.cs:72-77`), `RoundEngine` arms the
round clock on the system (`:288`), and D3 forbids threading a clock through either. So S5 pins the test's subject in
three parts: (1) a pure test of `RoundDeadline.For` (the arithmetic — 4× with three follow-ups, 1× with none); (2) the
"conversation is not cut by a one-turn budget" claim at the scheduler level on the manual clock, through S4's
`TurnLoop` seam (`new BoundedScheduler(clock: manual)`), where a one-turn budget cuts at turn 3 on every run and the
right budget never does; (3) the end-to-end test keeps its three assertions (`proceed`, four prompts, "all 1 reviewers
answered") and loses the 700 ms sleep — it then pins the WIRING (`RoundEngine.cs:287` hands `stage.FollowUps` to `For`;
the fake is asked four times) and no longer depends on wall time. **Confidence: medium** — the recorded message shows the
round clock striking before the first launch, which the setup-inside-the-deadline path explains; a kept failure (D5)
decides whether anything else is in it.

### C4 — `AReaderIsEndedWithItsTreeTests.AServerThatNeverAnswers_IsEndedAtTheDeadline_WithItsTree` (TIMING risk, low; GATED)

`src_mcp/tests/AReaderIsEndedWithItsTreeTests.cs:107-123`: `Timeout = TimeSpan.FromSeconds(2)` (`:115`); the fake
server must start a second cold `FakeCli` (the grandchild) and write both pids before the deadline kills the tree;
nothing waits for the pids first. One of the four deadline tests that failed in the full local run of 2026-10-18 and
passed alone; the failure text was not kept. **Gated (judge J8, parent D5):** `ProcessLauncher` is the one class D3 does
NOT touch until this case has a KEPT failure — its three separations (§5.5) are the riskiest seam in the plan and the
evidence is a failure nobody kept. S6 opens only then; until then the test and the launcher are not changed.

## 4. Decision D3 — restated for this epic

**A deadline a test must race is armed through an injectable `TimeProvider`, one class at a time, each landed WITH its
RED.** The seam is an optional `TimeProvider? clock = null` constructor parameter on the class that OWNS each timer,
with `_clock = clock ?? TimeProvider.System` — the precedent is `BoundedScheduler`
(`src_mcp/runners/Reviewers/BoundedScheduler.cs:96`, `:102`). Four classes, one per story: `AcpClient` (S2, C5),
`ConsultantPlaces` (S3, C17), `TurnLoop` and `SourceResolver` (S4, C13); C6 (S5) reuses `TurnLoop`'s. **Never threaded
through `PanelService` or `RoundEngine`** — they construct these classes with the default (`PanelService.cs:72-77`,
`AskAcpMode.cs:191`, `FeatureStage.cs:237`, `:244`, `QuestionFanOut.cs:238`), and the round clock at
`RoundEngine.cs:288` gets no seam. `SecuritySources.cs:32` is the sibling plan's (§7). **`ProcessLauncher`
(`:481-482`) is left out** until C4 has a kept failure. The seam's CONTRACT (§5) is the cornerstone, not the parameter.
*Loses:* a single seam story across the classes (no RED of its own); longer timeouts; fixed sleeps in the fakes as what
decides an outcome.

## 5. The cornerstone — the clock contract and the manual clock (E4.S1, spec by Fable)

A cornerstone is where a mistake compounds and the next step does not catch it. Here the mistake is per class and
invisible: a `Stopwatch` left beside an injected timer, or a `CancelAfter` on a linked token source that still uses the
system timer, is the same race again while the test on the manual clock goes green. So the contract is written ONCE,
here, and checked in every story against the tables below — the "after" column is the reviewer's checklist.

### 5.1 The inventory — every clock read in the four classes, and which clock each reads afterwards

The rule, per class: **every budget that decides the outcome reads the class's one `_clock`** — elapsed calculations
through `GetTimestamp()`/`GetElapsedTime()`, delays through `Task.Delay(…, _clock, ct)`, cancellations through
`new CancellationTokenSource(delay, _clock)`, waits through `WaitAsync(timeout, _clock)`, wall stamps through
`GetUtcNow()`. **One stated exception, with its reason:** a teardown grace that bounds the REAL world after the outcome
is decided — the drain of a killed process's pipe — stays on the system clock. It never races the deadline (it starts
after the deadline has decided), and on a manual clock that nobody advances it would turn a 2 s bound on a real pipe into
a hang. Each such site is named in the tables with "system — teardown". (Confirmed as the rule or overruled by the lead:
§12 Q1.)

**Table 1 — `AcpClient` (`src_mcp/runners/Acp/AcpClient.cs`, 276 lines; 3 timed sites, 2 move, 1 stays; no `Stopwatch`, no `DateTime.UtcNow`, no `Timer`/`PeriodicTimer`).**

| Line | Today | What it decides | After S2 |
|---|---|---|---|
| `:114` + `:119` | `using var deadline = CreateLinkedTokenSource(ct);` … `deadline.CancelAfter(ask.Deadline);` | the turn's deadline (`AcpEnd.Deadline`, `:253`) | `using var turnTimer = new CancellationTokenSource(ask.Deadline, _clock); using var deadline = CreateLinkedTokenSource(ct, turnTimer.Token);` at `:114`; `:119` goes. The timer now starts a few microseconds earlier (before `new AcpSession`, `:115`) — no `await` lies between, so nothing can observe it. `MaxDeadline` (`:50`) keeps refusing what the timer cannot hold. |
| `:195` | `….WaitAsync(usageBound)` | the `/usage` bound | `….WaitAsync(usageBound, _clock)` |
| `:233` | `reading.WaitAsync(AcpTransport.DrainGrace)` in `SettledAsync` | the drain grace after the tree is dead | **system — teardown.** The tree was killed at `:129` (`transport.EndAsync`); this bounds a pipe a survivor may hold. |

Callees that arm a timer (none moves; all are teardown or token-only):
`AcpSession.CallAsync` `:165` `answer.Task.WaitAsync(ct)` — token only, so it reads the turn timer through the token;
`AcpSession.NotifyAsync` `:175` `new CancellationTokenSource(AcpTransport.GraceWait)` — system, teardown (`session/cancel`
after the token fired); `AcpSession.WaitUntilQuietAsync` `:184-199` (`DateTime.UtcNow` loop `:186-187`, `Task.Delay(50)`
`:197`) — system, teardown; `AcpSession.WatchExitAsync` `:265` `Task.Delay(ExitGrace, ct)` — system, the agent's last
output after its exit (300 ms, `:54`); `AcpTransport.EndAsync` `:234` `_draining.CancelAfter(DrainGrace)` — system,
teardown; `AcpTransport.WriteLineAsync` `:197-201` `WaitAsync(ct)` — token only. Constants: `GraceWait` 1.5 s
(`AcpTransport.cs:33`), `DrainGrace` 2 s (`:36`).

Seam shape: the primary constructor (`:31`) gains `TimeProvider? clock = null` last; the two chained constructors
(`:52-55`, `:58-61`) pass nothing. `AskAcpMode.cs:191` is unchanged (default). `CA1502: 4` holds: the `??` is one branch
in a field initializer.

**Table 2 — `ConsultantPlaces` (`src_mcp/src/Server/Consultation/ConsultantPlaces.cs`, 458 lines; 2 sites, both move).**

| Line | Today | What it decides | After S3 |
|---|---|---|---|
| `:333-334` | `using var copying = CreateLinkedTokenSource(ct); copying.CancelAfter(budget);` | the one-shot budget (`OutOfBudget`, `:449-451`) | `using var budgetTimer = new CancellationTokenSource(budget, _clock); using var copying = CreateLinkedTokenSource(ct, budgetTimer.Token);` — armed at the same point, before `CopiedAsync` (`:337`). `catch (OperationCanceledException) when (!ct.IsCancellationRequested)` (`:343`) is unchanged: the budget's cancellation still reads as the budget's. |
| `:289` | `ConversationFolder.Begin(folder, store, DateTime.UtcNow, ceiling)` in `Stamping.Begin` | the folder's wall stamp (read by the home sweep) | `_clock.GetUtcNow().UtcDateTime` — `Stamping` (`:287`) takes the clock. A wall stamp, not a deadline, but the same clock by the rule; a test can pin it. |

Callees (none moves): `TakingTheLock` `:121-122` → `HeldFile.TakeWithin(path, LockWait)` (`HeldFile.cs:169-173`, a
`DateTime.UtcNow` loop, 10 s) — a shared helper of other callers, the subject of C3 (E5) and of
`PLAN_a_turn_waits_by_the_clock.md`; no E4 test races it, and it is already a test seam. `Maker` `:130` →
`ReviewSnapshots.BuildOnceAsync` (`ReviewSnapshots.cs:171` `Stopwatch`, judged against `MaxElapsed` 60 s at `:214`,
`:279`; `SnapshotOptions.cs:23`) — the per-copy ceiling of D6, not the budget C17 races; a seam on `ReviewSnapshots`
would land without a RED, which D3 forbids. `VibeHomeTurns.cs:27` `Gate.WaitAsync(ct)` — token only.
`ConversationFolder.cs:119` parses a ceiling — arithmetic.

Seam shape: the primary constructor (`:115`) gains `TimeProvider? clock = null` last. The test helper `Places()`
(`AConversationPlaceTests.cs:47`) gains the same optional parameter.

**Table 3 — `TurnLoop` (`src_mcp/runners/Reviewers/TurnLoop.cs`, 159 lines; 1 clock, 5 reads, all move).**

| Line | Today | What it decides | After S4 |
|---|---|---|---|
| `:45` | `var clock = Stopwatch.StartNew();` | the conversation's elapsed, against the cap (`:43-44`) | `var started = _clock.GetTimestamp();` |
| `:72`, `:74` | `Stopwatch clock` parameter; `var left = cap - clock.Elapsed;` | what is left of the cap; `TimedOut` with `Usage.None` at `:75-79` | `long started` parameter; `var left = cap - _clock.GetElapsedTime(started);` |
| `:81`, `:84` | `var started = clock.Elapsed;` … `clock.Elapsed - started` | a turn's elapsed (the ledger's `TurnUsage`) | `var turnStart = _clock.GetTimestamp();` … `_clock.GetElapsedTime(turnStart)` |
| `:129-138` | `Within`/`Bounded` | arithmetic on `left` — no timer here | unchanged. The per-turn TIMER is the launcher's (`ProcessLauncher.cs:481-482`, gated) or the test's stand-in. |

Its one constructor site is `BoundedScheduler.cs:462` (`new TurnLoop(runTurn, onProgress)`), which becomes
`new TurnLoop(runTurn, onProgress, _clock)` — the scheduler already owns a clock (`:96`, `:102`), and a loop inside a
scheduler on clock X is on clock X; this is the one-clock rule, not threading (`PanelService.cs:72-77` still constructs
the scheduler with the default). Named and NOT moved in E4 — no E4 RED races them, and D3 forbids a seam without one:
`BoundedScheduler` `:290`, `:365` (`Stopwatch queued`, a report), `:461` (`Stopwatch watch`, the reported elapsed),
`:616` + `:623` + `:625` (`Stopwatch watch` judged against the ladder's budget — an elapsed-budget calculation on a
`Stopwatch` beside `_clock` at `:674`: the pattern this contract names; §12 Q4), `:685` `Task.Delay(wait, ct)` (the
ladder's wait); `ReviewerExecutor` `:598` (`Stopwatch spent`, the ledger), `:824` (`new CancellationTokenSource(AbandonBudget)`),
`:520` (`DateTime.UtcNow` in a file name).

**Table 4 — `SourceResolver` (`src_mcp/runners/Feature/SourceResolver.cs`, 384 lines; 1 site, moves).**

| Line | Today | What it decides | After S4 |
|---|---|---|---|
| `:199-200` | `using var deadline = CreateLinkedTokenSource(ct); deadline.CancelAfter(_readDeadline);` | one file's read deadline (`TimedOutReason`, the sentence at `:229`) | `using var readTimer = new CancellationTokenSource(_readDeadline, _clock); using var deadline = CreateLinkedTokenSource(ct, readTimer.Token);` — `:206` and `:208-211` unchanged: `deadline.IsCancellationRequested && !ct.IsCancellationRequested` still names OUR deadline. |

Callee: `CommittedFile.ReadAtAsync` → `GitHistory.RunAsync` (`GitHistory.cs:320`, `ProcessRequest { Timeout = Budget }`,
30 s at `:49`) → the launcher's own timer (gated). The resolver's token cancels that wait, as today. Seam shape: the
primary constructor (`:42`) gains `TimeProvider? clock = null` after `readDeadline`; `_clock` beside `_readDeadline`
(`:44`). The three product construction sites (`FeatureStage.cs:237`, `:244`, `QuestionFanOut.cs:238`) are unchanged.
`SecuritySources.cs:31-32` (the same `CreateLinkedTokenSource` + `CancelAfter(CollectionBudget)` shape) is the sibling's
and copies this table's row when it lands (S21).

**Counts.** Four classes: 7 timed sites in the classes themselves — `AcpClient` 3 (2 move, 1 teardown stays),
`ConsultantPlaces` 2 (both move), `TurnLoop` 1 `Stopwatch` with 5 reads (all move), `SourceResolver` 1 (moves).
`Stopwatch`: 1 (`TurnLoop`). `DateTime.UtcNow`: 1 (`ConsultantPlaces:289`). `CancelAfter`: 3 (`:119`, `:334`, `:200`).
`WaitAsync` with a timeout: 2 (`:195`, `:233`). `Task.Delay`, `Timer`, `PeriodicTimer`: 0 in the four classes.
Callees that arm a timer: 10 named above (6 ACP teardown/token sites, 3 under `ConsultantPlaces`, 1 under
`SourceResolver`); none moves in E4.

### 5.2 The linked-token rule

`CreateLinkedTokenSource` + `CancelAfter` has no `TimeProvider` overload. So on every site above the timer becomes its
own source, `new CancellationTokenSource(delay, _clock)`, and the linked source links the caller's token AND the
timer's token: `CreateLinkedTokenSource(ct, timer.Token)`. Both sources are `using`. The "which one fired" reads stay as
they are — `ct.IsCancellationRequested` tells the caller's cancellation from the deadline's (`AcpClient.cs:125`,
`ConsultantPlaces.cs:343`, `SourceResolver.cs:206`, `:208`). `WaitAsync(timeout)` becomes `WaitAsync(timeout, _clock)`.
A `Task.Delay` on a budget (none in the four classes today) would become `Task.Delay(delay, _clock, ct)`. All three
overloads exist in .NET 8+ and are driven by `TimeProvider.CreateTimer`, which is the one method the manual clock
overrides for timers.

### 5.3 The default, and the proof that the seam alone changes nothing

The default is `TimeProvider.System`; with it, `new CancellationTokenSource(delay, TimeProvider.System)` and
`CancelAfter(delay)` arm the same system timer, and `GetTimestamp()`/`GetElapsedTime()` are what `Stopwatch` reads. So
each story has an inner step before its test moves: **the seam lands first, alone, and the whole `src_mcp` suite is run
green on it** (the command in §10) — proof that the product's default behaviour is unchanged. Only then does the story's
test move onto the manual clock and its RED is shown. The PR records both runs.

### 5.4 The manual clock — `ManualClock`, one shared test-support class

Shaped on `wsl_care · src_daemon/tests/WslCare.TestSupport/ManualTimeProvider.cs` (read on 2026-10-21: timers `:36-46`,
timestamps `:58-71`, `Advance` and the firing loop `:73-116`, the timer `:125-233`, `JumpWallClock` `:236-242`), with
two simplifications and two additions.

**File:** `src_mcp/tests/ManualClock.cs` (`internal sealed class ManualClock(DateTimeOffset start) : TimeProvider`),
beside `FixedClock.cs` and `SteppingClock.cs`.

**Contract.**

1. **Two readings, moved together by `Advance(TimeSpan by)`:** `GetUtcNow()` (the wall clock, starting at `start`) and
   `GetTimestamp()` with `TimestampFrequency = TimeSpan.TicksPerSecond` (the monotonic reading, starting at one day so no
   due-time arithmetic crosses zero). `LocalTimeZone` is UTC. Unlike `wsl_care`'s, there is no `SteppedTimestamps` flag:
   a hand-moved clock whose monotonic reading is real is the defect class this epic removes, and E3's `MovableClock`
   already moves both (`TheRunsThemselvesTests.cs:217-220`).
2. **`JumpWallClock(TimeSpan by)`** moves the wall clock alone — a host that slept, a clock stepped forward; what a
   two-clock rule is tested with (E3's `AtomicFile` window is monotonic, `AtomicFile.cs:71`, `:80`, `:84`; its orphan age
   is wall, `:117`).
3. **Timers are always manual** (no `DrivesTimers` flag — same reason as 1): `CreateTimer` returns a timer whose due time
   is on the monotonic reading. A timer fires only inside `Advance`, when the reading reaches its due time; it never fires
   on the real clock.
4. **Timers fire in order, earliest due first; ties in arming order.** One `Advance` fires every timer whose due time it
   reaches, repeating while any is due — so a timer a callback arms with a due time already reached fires in the SAME
   advance, after the one that armed it. A periodic timer is due again a period later and fires again while that is still
   reached.
5. **A callback never runs under the clock's lock.** The due timer is picked and marked running under the lock; the
   callback runs outside it, on the advancing thread. A callback may make, change or dispose a timer. Consequence, stated
   for the tests: `Advance` may run the product's continuation inline on the test's thread up to its next `await` (a
   cancellation token's registrations run synchronously on the cancelling thread) — so a test never calls `Advance`
   while holding a lock, and asserts after awaiting the product's task, never inside the advance.
6. **Disposal.** `ITimer.Dispose` retires the timer; `DisposeAsync` completes only once a callback already picked to fire
   has returned (a disposal between the pick and the call still waits — `wsl_care`'s retro finding). `Change` after
   disposal schedules nothing and returns `false`, which `PeriodicTimer` reads as "disposed". A due time or period below
   −1 ms or above 0xFFFFFFFE ms is refused with `ArgumentOutOfRangeException`, as the system timer refuses it — so a
   product value the system would refuse is refused on the manual clock too (`AcpClient.MaxDeadline` keeps its meaning).
7. **`ActiveTimers`** (how many are scheduled) and **`Task WhenTimersAsync(int atLeast)`** — completes when at least that
   many timers are scheduled, at once if they already are. This is the EVENT a test waits on before advancing: "the
   product has armed its deadline". It replaces polling a log file or a `Thread.Sleep`. (An addition over `wsl_care`,
   which offers the count only.)
8. **`RecordingWait` is not carried over** — it is `wsl_care`'s follower's injected wait; here a stand-in waits with
   `Task.Delay(delay, clock, ct)` (S4's `SlowShow`).
9. **Thread-safe**: one lock over `_now`, `_monotonic` and the timer list; every public member takes it.

**What happens to the other test clocks (the reuse rule).**

| Clock | Where | Job | Decision |
|---|---|---|---|
| `MovableClock` | `TheRunsThemselvesTests.cs:209-221`; used in 7 files (`AMoveOverAHeldFileIsRetriedTests`, `ASessionSaveMeetsABusyFileTests`, `AnIdleServerReadsNothingTests:162`, `AuthorPeersTests:184`, `RoundAuthorsStepTests` ×3, `TheRunsThemselvesTests`, `TheSendsThemselvesTests:27`) | a hand-moved wall + monotonic clock, no timers | **Subsumed by `ManualClock`** — same constructor (`DateTimeOffset start`), same `Advance`, same two readings; `ManualClock` adds timers, `JumpWallClock` and the event. S1 moves the nine sites (a rename) and deletes `MovableClock`. Two hand-moved clocks would be two clocks for one job; the only difference (no timers) is a missing feature, not a different job. |
| `FixedClock` | `FixedClock.cs:12` | an instant that cannot move — read-only by design (a test that needs two moments makes two clocks) | stays — a different job |
| `SteppingClock` | `SteppingClock.cs:7` | moves one step per READ, with no hand — a budget that must run out mid-read | stays — a different job |
| `FakeClock` | `CollectRunTests.cs:409-412` | an instant that cannot move | **a duplicate of `FixedClock`** (reuse finding). S1 replaces its one use with `FixedClock` and deletes it — three lines, the same job; §12 Q7 if the lead prefers to leave it. |
| `ReadClock` | `RoundAuthorsStepTests.cs:396-411` | moves per read only while a flag is set | stays — a job of its own, private to one test |

**`ManualClockTests`** (`src_mcp/tests/ManualClockTests.cs`) — each fact with the RED it shows first. The RED is seen
against a stub shaped like today's `MovableClock` (no `CreateTimer` override): a hand-moved clock whose timers are real.

| # | Fact | RED on the `MovableClock`-shaped stub |
|---|---|---|
| M1 | `new CancellationTokenSource(2 s, clock)`: not cancelled after `Advance(1999 ms)`, cancelled after one more `Advance(1 ms)` | `Expected source.IsCancellationRequested to be True because the clock was advanced past the delay, but found False.` |
| M2 | `Task.Delay(700 ms, clock, ct)` completes on `Advance(700 ms)` and not on `Advance(699 ms)` | the delay is still pending after the advance |
| M3 | `task.WaitAsync(1 s, clock)` throws `TimeoutException` on `Advance(1 s)`, not before | no `TimeoutException` after the advance |
| M4 | timers due at 1 s and 2 s both fire inside one `Advance(5 s)`, in that order | neither fires |
| M5 | a callback that arms a timer due inside the same advance: it fires in the same advance, after the one that armed it | the second never fires |
| M6 | the callback runs outside the lock: a callback blocked on a gate does not stop another thread's `GetUtcNow()` / `ActiveTimers` from returning | guard — green from the first run; broken on purpose once by firing under the lock (the second thread's read blocks until the gate opens; asserted with a bounded wait) |
| M7 | `DisposeAsync` during a running callback completes only after the callback returns | guard — green first; broken once by completing the idle task before the callback ends |
| M8 | `Change` after `Dispose` returns `false` and schedules nothing; `PeriodicTimer(period, clock)` ends its `WaitForNextTickAsync` on disposal | `Change` returns `true` / the tick still arrives |
| M9 | `GetElapsedTime(stamp)` moves with `Advance` and not with `JumpWallClock`; `GetUtcNow()` moves with both | `JumpWallClock` moves the elapsed too (or does not exist) |
| M10 | a due time of `uint.MaxValue` ms is refused with `ArgumentOutOfRangeException`, as `TimeProvider.System.CreateTimer` refuses it (both clocks asserted) | the manual clock accepts it |
| M11 | `WhenTimersAsync(1)` is pending while nothing is armed and completes when the first timer is armed; `ActiveTimers` counts scheduled timers and drops when one fires or is disposed | the member does not exist / the count stays |
| M12 | a periodic timer with a 100 ms period fires three times inside one `Advance(300 ms)` and once more on `Advance(100 ms)` | nothing fires |

**Break-its for S1** (change the clock, run `ManualClockTests`, see red, restore): B1 fire inside the lock (M6 red);
B2 skip `Begin()` on pick (M7 red); B3 order timers by arming instead of due time (M4 red); B4 fire a timer on
`CreateTimer` when already due instead of on `Advance` (M1's "not cancelled after 1999 ms" arm red); B5 move the
monotonic reading in `JumpWallClock` (M9 red).

### 5.5 What the launcher's three separations must keep — written now for the gated C4 story (S6)

`ProcessLauncher.RunToCompletionAsync` (`src_mcp/runners/Processes/ProcessLauncher.cs:473-528`) keeps three things
apart, and a seam that merges any two of them breaks a product guarantee while the tests stay green:

1. **The execution deadline is its own source, linked to the caller's token** (`:481-482`:
   `CreateLinkedTokenSource(ct)` + `CancelAfter(request.Timeout)`), read by the exit wait (`:509` →
   `UntilExitOrAnswerAsync`, `:626-641`, whose `stop` at `:628` is linked to it) and by the stdin write (`:499`). Under
   the seam it becomes §5.2's shape: `new CancellationTokenSource(request.Timeout, _clock)` linked in. The `Timeout`
   default is 10 min (`:25`).
2. **The drain OUTLIVES the deadline** — `draining` is a source of its own (`:487`), armed with `DrainGrace` (`:518`,
   default 5 s at `:66-68`) only AFTER the child is confirmed ended (`:510-512`), so the output of a child that was just
   killed is kept as the evidence of why. Under the seam the drain grace STAYS on the system clock — the teardown rule of
   §5.1: it bounds a real pipe after the outcome is decided, and a manual clock nobody advances would hang it. S15's
   guard pins it: with the manual deadline fired, `result.TimedOut` is true AND the lines the child wrote before the kill
   are in `result.StdOut`.
3. **Caller cancellation and timeout are told apart after the fact** — `ResultOf(…, ct.IsCancellationRequested, …)`
   (`:527`): the same linked token, two meanings. A manual deadline that fires must read as `TimedOut: true` with the
   caller's token untouched; a caller cancellation must not read as a timeout. S15 asserts both arms.

Two more real-world timers in the launcher stay on the system clock, named so nobody moves them: `EndedProbe`
(`:209`, a synchronous `WaitForExit(100 ms)` at `:393` — a probe of a real process) and `TreeWatch`'s
`PeriodicTimer(step)` (`TreeWatch.cs:103`, `ProcessTree.ListingStep` 200 ms, `ProcessTree.cs:46` — the cadence at
which a REAL child's tree is listed). S6's RED (when the gate opens): a `startDelayMs` on the fake's `serve` verb
(`tests_fakecli/Serving.cs:29-30`) larger than the deadline makes today's test fail on every run with the pid file
missing; its fix waits for the pid file to name both processes (the fake writes it whole) and only then advances the
manual clock past 2 s.

## 6. Scenarios — each with the RED it must show first

Every RED is watched failing against today's code with the message below, then watched passing; both go in the PR.

| # | Scenario (test name) | Case | RED today — the message that proves the cause | Story |
|---|---|---|---|---|
| S12 | `AUsagePromptThatOutlivesTheTurnsDeadline_ReturnsTheAnswerAtTheDeadline_NotAfterTheBound` on the manual clock: the test waits for TWO timers on the clock (the turn's deadline and the usage bound — the answer is in, `/usage` is pending), then advances 2 s | C5 | with a new `startDelayMs` script field in the fake agent set past the deadline: `Expected outcome.End to be AcpEnd.Answered because the deadline struck while /usage was asked, and /usage never changes the end: the agent did not answer within the 2s this turn was given, but found AcpEnd.Deadline.` — the CI sentence, every run | S2 |
| S16 | `CopiesThatOutrunTheRowsBudget_RefuseThePlace_SayingHowManyWereCopied_AndKeepNone` on `ConsultantPlaces`' own clock; the slow root's hook advances it past the budget | C17 | with a launcher stand-in that delays every `git` answer by 1.1 s (the git phase outlives the budget, as on the runner): `Expected place.Refusal "copying the granted folders took longer than the row's budget (1 s): the budget ran out while confirming 2 candidates with git, before anything was copied — …" to contain "1 of 2" because the sentence says how far the copying got.` — every run | S3 |
| S14 | the two turn tests and the resolver test on the manual clock, through an in-process launcher stand-in / a clock-waiting `SlowShow` | C13 | (a) cap test with `FAKECLI_SLEEP_MS=800` against the 700 ms turn: `Expected outcome.EarlierTurns not to be empty because the turns that answered before the cap are kept.` (b) work-cap test with `FAKECLI_SLEEP_MS=950`: `EarlierTurns.Count` is 0, outside `[1, 3]`. (c) resolver test with `SlowShow` applied to `Cart.cs` as well: `Expected tail … to contain "### src/Cart.cs lines 28-35" because the turn goes on with what was served`. Each every run. **Break-it after the move:** the cap check at `TurnLoop.cs:74-79` removed → (a) ends in the stand-in's own failure after its 20-launch ceiling, not `TimedOut` | S4 |
| S13 | `RoundDeadlineTests` (pure): `For(followUps: 3)` = 4 × `For(followUps: 0)` for a 2 s reviewer timeout, one vendor, one role; `AOneTurnBudget_CutsAFourTurnConversation_AtTurnThree_AndTheScaledBudgetNever` at scheduler level on the manual clock (700 ms per turn, three follow-ups: cap 2 s → `TimedOut` with exactly 2 earlier turns, the third never launched; cap 8 s → `Ok` with `Turns == 4`); the end-to-end test keeps `proceed` / four prompts / "all 1 reviewers answered" with NO sleep | C6 | (1) no `RoundDeadline.For` test exists (red by absence: the new test asserts 8 s and 2 s); (2) the scheduler-level test with a one-turn budget: `Expected outcome to be ReviewerOutcome.TimedOut because a conversation of four 700 ms turns does not fit one 2 s turn, but found ReviewerOutcome.Ok` when the cap is computed without the follow-ups (break-it at `TurnLoop.cs:43`); (3) today's end-to-end test with `FAKECLI_SLEEP_MS=1900`: `Expected … "proceed" … the round waited for the whole conversation` fails with "not started … the round reached its limit" — fixed sleeps against a fixed deadline decide the outcome | S5 |
| S15 | `AServerThatNeverAnswers_IsEndedAtTheDeadline_WithItsTree` on the launcher's manual clock + `startDelayMs` on the `serve` verb past the deadline; the test waits for both pids in the file, then advances — **gated on a kept failure (D5); the launcher gets no seam before that** | C4 | the pid file is missing / names one process (today's test, with `startDelayMs` past 2 s) | S6 (gated) |

## 7. Boundaries with other plans (both sides)

| Item | Built by | The other plan's part |
|---|---|---|
| The manual clock (`ManualClock`, §5.4) | **this epic** (S1) | `PLAN_two_equal_deadlines_race_on_macos.md` (C8) step 3 and S21 reuse it: "the per-file timer fired BEFORE the collection timer on a manual clock"; `PLAN_a_turn_waits_by_the_clock.md` (C19) may use it for `SessionTurn.Take`'s deadline |
| `SourceResolver`'s clock (`SourceResolver.cs:199-200`) | **this epic** (S4, C13) | the sibling's step 3 waits for S4 to merge, then adds `SecuritySources`' own clock at `SecuritySources.cs:31-32` with S21 (judge J14, both sides agree: that plan's boundary rows 1–2) |
| `SecuritySources.cs:32` (`CollectionBudget`, `:14`) | the sibling plan | this epic never touches it |
| `ProcessLauncher.cs:481-482` (the execution deadline) | this epic's S6, **only when C4 has a kept failure** | E3 S1 (C2) changed the launcher's start-time read, not its timers; §5.5 is written so S6's coder keeps the three separations |
| `HeldFile.TakeWithin`'s wall-clock loop (`HeldFile.cs:169-173`) | not this epic | C3 (E5) seams the Kimi call site; `PLAN_a_turn_waits_by_the_clock.md` calls `TakeWithin` rather than writing a second wait |
| `BoundedScheduler`'s ladder `Stopwatch` and `Task.Delay` (`:616-625`, `:685`) beside `_clock` | not this epic (§12 Q4) | the open tail: the first RED that races the ladder lands it, on the scheduler's existing `_clock` |
| C6, C13 — two of the three deadline tests of `PLAN_question_consultant_tails.md` §6 | **this epic** (S4, S5) | tails §6 already points at the parent; when S5 lands, the parent's DoD line "C6, C13, C14 closed here and removed from tails §6" is honoured by a one-line note there |
| The round clock at `RoundEngine.cs:288` and the scheduler at `PanelService.cs:72-77` | nobody — no seam (D3) | S5 removes the end-to-end test's dependence on them instead (§3 C6) |

## 8. The stories

Conventions every story follows: tests first, RED on the assertion against a stub that compiles (never a compile
error); build with `dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4`; run one class with
`node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*<Class>*"`,
then the whole executable; **never `dotnet test`**; break-its change PRODUCTION code (or the clock, in S1), run the named
test, see the real symptom, restore, see green — both runs in the summary; `CA1502: 4` holds per function; no
`partial` as a split; `null` is never "nothing".

### S1 — the manual clock alone (CORNERSTONE; no product change)

- **Goal.** `ManualClock` as §5.4 specifies, `ManualClockTests` M1–M12 green with their reds seen, `MovableClock`'s nine
  sites moved and the class deleted, `FakeClock` folded into `FixedClock` (unless Q7 says no), and the contract (§5.1
  tables, §5.2, §5.3, the teardown rule) written into `research/module_tests.md` as the checklist every later story is
  reviewed against.
- **Files.** New: `src_mcp/tests/ManualClock.cs`, `src_mcp/tests/ManualClockTests.cs`. Changed: `TheRunsThemselvesTests.cs`
  (`:203-221` removed; its own use moved), `AMoveOverAHeldFileIsRetriedTests.cs`, `ASessionSaveMeetsABusyFileTests.cs`,
  `AnIdleServerReadsNothingTests.cs:162`, `AuthorPeersTests.cs:184`, `RoundAuthorsStepTests.cs` (3 sites),
  `TheSendsThemselvesTests.cs:27`, `CollectRunTests.cs:409-412` (`FakeClock` → `FixedClock`). Docs:
  `research/module_tests.md` (new section "The suite is deterministic — E4 S1: the clock contract and the manual clock").
- **RED.** M1's: `Expected source.IsCancellationRequested to be True because the clock was advanced past the delay, but found False.` — seen with `ManualClock` stubbed as a renamed `MovableClock` (no timers). Then each of M2–M5, M8–M12 red
  the same way, M6 and M7 green as guards and broken once (B1, B2).
- **Break-its.** B1–B5 of §5.4.
- **Done when.** M1–M12 green; B1–B5 each red then green; the whole `src_mcp` suite green with `MovableClock` gone
  (E3's `AtomicFile` window tests — `AMoveOverAHeldFileIsRetriedTests`, `ASessionSaveMeetsABusyFileTests` — pass on
  `ManualClock` unchanged, which proves the subsumption); test count before and after named (+12, −0);
  `module_tests.md` carries the contract.
- **Roles.** Fable: this spec (§5) and the review of the clock against it. Sonnet: codes. Opus: reviews the diff against
  §5.4's nine contract points and the reuse table. `model: fable` on the story.

### S2 — C5: `AcpClient`'s clock + S12

- **Goal.** `AcpClient` reads its one `_clock` for the turn's deadline and the usage bound (§5.1 table 1); the C5 test
  no longer pays the cold start; no fixed sleep.
- **Files.** `src_mcp/runners/Acp/AcpClient.cs` (`:31` the parameter; `:114`, `:119`, `:195` as table 1; `:233` stays and
  gets a one-line remark naming the teardown rule). `src_mcp/tests_fakecli/AcpAgent.cs`: a new script field
  `"startDelayMs": N` — "on start, before anything is read" (documented in the header `:15-52`; a `Thread.Sleep` before
  the stdin loop). `src_mcp/tests/FakeAcpAgent.cs`: a `StartDelay(ms)` field helper beside `Models` (`:64`) and
  `UsageReply` (`:67`). `src_mcp/tests/AcpClientUsageTests.cs`: `ClientFor` (`:18-19`) gains `TimeProvider? clock`; the
  C5 test (`:47-58`) rewritten; the sibling test (`:34-45`, the usage bound alone, 1 s) moved the same way (§12 Q5).
  Docs: `research/module_runners.md` (the ACP client's clock), `research/module_tests.md` ("E4 S2").
- **Order inside the story.** (1) The RED on today's code: `startDelayMs: 2500` in the fake, deadline 2 s → the CI
  sentence, every run. (2) The seam alone, default clock, whole suite green (§5.3). (3) The test on the manual clock:
  `var turn = client.AskAsync(agent.Ask(deadline: 2 s), ct); await clock.WhenTimersAsync(2).WaitAsync(30 s guard, ct); clock.Advance(2 s); var outcome = await turn;`
  — the second timer is the usage bound at `:195`, armed only after the first prompt was answered; the 30 s is a guard
  that a correct run never reaches, not a decision. Assertions: `End == Answered`, `Tokens.Captured == false`,
  `clock.GetUtcNow() - start == 2 s` (the turn ended when the clock had moved exactly the deadline, with the 60 s bound
  never reached) in place of the wall-time bound at `:57` (§12 Q3). The `startDelayMs` stays in the fake's script for
  this test (any value — it no longer matters), which is the point.
- **RED (S12).** `Expected outcome.End to be AcpEnd.Answered because … but found AcpEnd.Deadline.`
- **Break-its.** B6: `UsageAsync`'s catch (`:199-202`) rethrows `OperationCanceledException` → the turn ends `Deadline`
  → S12 red with the same sentence (the test observes its subject). B7: `:119` `CancelAfter(ask.Deadline)` put back on
  the linked source (the system timer) with the clock timer removed → the test's wait for the second timer sees only
  one and the guard ends it: `TimeoutException` after 30 s — "the turn's deadline is not on the clock". B8: `:195`
  without the clock → the moved sibling test (`:34-45`, 1 s bound advanced on the clock) never sees its `TimeoutException`
  and ends by the guard.
- **Done when.** S12 red then green; B6–B8 each red then green; the suite green on the seam alone first; the §10 loop
  of `AcpClientUsageTests` recorded (20 runs beside a whole-suite run, before: the failures; after: 0 of 20); C5 not seen
  in the next 20 Windows CI runs (a watch); test count +0 (two tests rewritten) named.
- **Roles.** Opus brief (table 1 is the brief's checklist), Sonnet code, Opus review; Fable reads the diff against
  §5.1–5.3.

### S3 — C17: `ConsultantPlaces`' clock + S16

- **Goal.** `ConsultantPlaces` reads its one `_clock` for the one-shot budget and the folder stamp (§5.1 table 2); the
  C17 test's git phase and fast copy run under no clock pressure; the slow copy is cut on every run.
- **Files.** `src_mcp/src/Server/Consultation/ConsultantPlaces.cs` (`:115` the parameter; `:287-292` `Stamping` takes
  the clock; `:333-334` as table 2). `src_mcp/tests/AConversationPlaceTests.cs` (`Places()` `:47` gains the clock; the
  test `:113-139`: the hook at `:124-130` becomes `clock.Advance(budget + 1 ms)` for the slow root, no `Thread.Sleep`;
  a `StampedAt` arm is optional). Docs: `research/module_server.md` (the places' clock), `module_tests.md` ("E4 S3").
- **Order inside the story.** (1) The RED on today's code: a launcher stand-in (shape: `SlowShow`) that delays every
  `git` answer by 1.1 s — the phase sentence instead of "1 of 2", every run. (2) The seam alone, whole suite green. (3)
  The test on the manual clock: the stand-in gone (real git, no pressure), the slow root's hook advances the clock; the
  budget timer fires inside the hook (a callback on the test's clock, on the product's thread — §5.4 point 5), the copy
  meets its cancelled token and `OutOfBudget` says `1 of 2 copied`; `Directory.EnumerateDirectories(tree)` empty;
  `budget.Held == 0` — the assertions at `:136-138` unchanged.
- **RED (S16).** `Expected place.Refusal "… the budget ran out while confirming 2 candidates with git, before anything was copied …" to contain "1 of 2" because the sentence says how far the copying got.`
- **Break-its.** B9: `:334` `CancelAfter(budget)` put back on the linked source → the hook's advance cancels nothing, the
  slow copy completes, no refusal: `Expected place.Refusal "" to contain "1 of 2"`. B10: the stamp's `DateTime.UtcNow`
  put back at `:289` → the optional `StampedAt` arm (the folder's stamp equals the clock's instant) red.
- **Done when.** S16 red then green; B9 (and B10 if the arm is written) red then green; suite green on the seam alone
  first; the §10 loop recorded; C17 not seen in the next 20 Windows CI runs (a watch); test count named.
- **Roles.** Opus brief, Sonnet code, Opus review; Fable reads the diff against table 2.

### S4 — C13: `TurnLoop` + `SourceResolver` clocks + S14

- **Goal.** `TurnLoop`'s cap and per-turn elapsed and `SourceResolver`'s read deadline read their own `_clock`
  (§5.1 tables 3 and 4); `BoundedScheduler` hands its `_clock` to the loop; the three C13 tests pay no process start.
- **Files.** `src_mcp/runners/Reviewers/TurnLoop.cs` (`:27-29` the parameter; `:45`, `:72`, `:74`, `:81`, `:84` as table
  3). `src_mcp/runners/Reviewers/BoundedScheduler.cs:462` (`_clock` passed). `src_mcp/runners/Feature/SourceResolver.cs`
  (`:42` the parameter after `readDeadline`; `:44` `_clock`; `:199-200` as table 4). `src_mcp/tests/AReviewerThatAsksForSourceIsAskedAgainTests.cs`:
  a new in-process stand-in `ClockedLauncher(ManualClock clock, TimeSpan turn, int ceiling = 20)` beside
  `TwoDecidedThenReal` (`:443-456`) — `RunAsync` advances the clock by `turn`, answers the scripted `FAKECLI_STDOUT`
  text as exit 0 when `turn <= request.Timeout`, else `TimedOut: true`; after `ceiling` launches it answers a non-zero
  exit naming the count (so a loop with no cap ends red, never hangs); the two turn tests (`:462-477`, `:484-497`) use
  `new BoundedScheduler(clock: clock)` and an executor on the stand-in, no `FAKECLI_SLEEP_MS`; `SlowShow` (`:540-545`)
  waits with `await Task.Delay(delay, clock, ct)` and exposes `Task Asked` (completed when `git show` of `Big.cs` was
  asked); the resolver test (`:523-537`) builds the resolver with the clock, starts the turn, awaits `slow.Asked`,
  advances 301 ms, awaits the turn. Docs: `research/module_runners.md` (the loop's and the resolver's clocks),
  `module_tests.md` ("E4 S4").
- **Order inside the story.** (1) The three REDs on today's code (S14 a–c). (2) Both seams alone, whole suite green
  (the three product construction sites of the resolver and `PanelService`'s scheduler untouched). (3) The tests moved.
  With exact durations the counts become deterministic: cap test — 400 ms turns under a 1.4 s cap give three answered
  turns and a fourth never launched (`EarlierTurns.Count == 3`); work-cap test — 300 ms turns under 900 ms give three
  (`== 3`); the parent's `BeInRange(1, 3)` may become `Be(3)` (§12 Q8, stronger, never weaker).
- **RED (S14).** (a) `Expected outcome.EarlierTurns not to be empty because the turns that answered before the cap are kept.`; (b) `EarlierTurns.Count` 0 outside `[1, 3]`; (c) `Expected tail … to contain "### src/Cart.cs lines 28-35" because the turn goes on with what was served`.
- **Break-its.** B11: the cap check at `TurnLoop.cs:74-79` removed → (a) ends in the stand-in's ceiling failure:
  `Expected outcome to be of type ReviewerOutcome.TimedOut because a conversation past 700 ms × 2 is ended by the cap, but found ReviewerOutcome.NonZeroExit`. B12: `:45` back on `Stopwatch` (the clock parameter ignored) → the stand-in's
  advances move nothing the loop reads; the cap never strikes → the same ceiling failure. B13: `:200` back on
  `CancelAfter` → the resolver's deadline is real: the test's advance cuts nothing, `SlowShow` waits on the clock for
  ever, and the real 300 ms strikes first — (c) passes by accident, so the resolver arm ALSO asserts `slow.Cut` (the
  stand-in observed its token cancelled by the advance, not by time: a `TaskCompletionSource` set when the delay's
  `OperationCanceledException` arrives while `clock.ActiveTimers` still counts the read timer) — red: `Expected slow.Cut to be True because the resolver's deadline struck on the clock, but found False`. B14: `BoundedScheduler.cs:462` without `_clock` → (a) and (b) end in the ceiling failure.
- **Done when.** S14 a–c red then green; B11–B14 each red then green; suite green on the seams alone first; the §10 loop
  recorded for each of the three; C13 not seen in the next 20 CI runs (a watch); test count named; the sibling plan told
  that S4 merged (its step 3 may start).
- **Roles.** Opus brief, Sonnet code, Opus review; Fable reads the diff against tables 3 and 4 and the `BoundedScheduler`
  rule (one clock per scheduler, nothing else moved).

### S5 — C6: the round deadline pinned without wall time + S13 (after S4)

- **Goal.** The end-to-end test stops deciding its outcome on wall time; the subject it stood for — the round's deadline
  scales with the follow-ups and a conversation is not cut by a one-turn budget — is pinned deterministically.
- **Files.** New `src_mcp/tests/RoundDeadlineTests.cs` (pure: `PanelSettings { ReviewerTimeout = 2 s }`, one enabled
  vendor, one role, `Stage.Feature`; `For(followUps: 3)` and `For(followUps: 0)`, the ratio 4 and the exact values as
  `RoundBudget.For` derives them — the test states both numbers; `RoundDeadline.cs:9-30`). A scheduler-level test in
  `AReviewerThatAsksForSourceIsAskedAgainTests.cs` (S4's `ClockedLauncher`, 700 ms turns, `Endless(followUps: 3)`:
  timeout 2 s → `Ok` with `Turns == 4`; `ConversationCap = 2 s` → `TimedOut`, `EarlierTurns.Count == 2`, the stand-in
  launched three times). `AFeatureIsReviewedEndToEndTests.cs:446-458`: `:451` (`FAKECLI_SLEEP_MS=700`) removed; the
  remark `:441-445` rewritten to say what the test now pins (the wiring at `RoundEngine.cs:287`); `:455-457` unchanged.
  Docs: `module_tests.md` ("E4 S5"); a one-line note in `PLAN_question_consultant_tails.md` §6 that C6 and C13 are closed.
- **RED (S13).** (1) the pure test: red by absence, then its two numbers; (2) the scheduler-level test with the follow-ups
  dropped from the cap (`TurnLoop.cs:43` → `Timeout * 1`): `Expected outcome to be of type ReviewerOutcome.Ok because an 8 s cap holds four 700 ms turns, but found ReviewerOutcome.TimedOut`; (3) today's end-to-end test with
  `FAKECLI_SLEEP_MS=1900`: "not started … the round reached its limit" — the recorded failure, every run.
- **Break-its.** B15: `RoundDeadline.cs:11` `settings.ReviewerTimeout * 1` → the pure test red (`Expected 8s but found 2s`
  in `RoundBudget`'s terms). B16: `RoundEngine.cs:287` `stage.FollowUps` → `0` → the pure test cannot see it; the
  end-to-end test without sleep still passes (four 0 ms turns fit 2 s) — **named as the limit**: the engine's
  pass-through is a one-token wiring with no deterministic observer short of a seam D3 forbids; it is covered by the
  code gate's reading, and §12 Q2 asks whether that is enough.
- **Done when.** S13 (1)–(3) red then green; B15 red then green; B16's limit stated in the PR; the end-to-end test's §10
  loop recorded (before: the failures with the sleep; after: 0 of 20); C6 not seen in the next 20 CI runs (a watch);
  test count +2 named.
- **Roles.** Opus brief, Sonnet code, Opus review.

### S6 — C4: the launcher's clock + S15 (GATED on a kept failure, D5)

Opens only when a failure of `AServerThatNeverAnswers_…` has been kept with its full message. Then: the seam on
`ProcessLauncher` as §5.5 (the deadline on the clock; the drain, the probe and the tree watch on the system clock; the
caller/timeout distinction asserted both ways), `startDelayMs` on the `serve` verb, the test waiting for both pids
before it advances. Fable reviews the diff against §5.5; Opus briefs; Sonnet codes. Until then: nothing.

## 9. Build order — and what runs beside what

S1 first and alone (the clock is what S2–S4 inject). Then **S2, S3, S4 in parallel** — four different product classes,
four different test files, no shared line. S5 after S4 (it uses the `TurnLoop` seam and S4's stand-in). S6 only when
its gate opens. The sibling plan's step 3 starts after S4 merges. Each story is its own branch off `feat/suite-e4-clock`
and merges into it for the epic's one code gate (E3's shape); the epic's PR to `main` carries the test counts of every
story.

## 10. Test plan

```bash
dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4
node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*ManualClockTests"
node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*AcpClientUsageTests"
node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*AConversationPlaceTests"
node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*AReviewerThatAsksForSourceIsAskedAgainTests"
node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*AFeatureIsReviewedEndToEndTests"
node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe   # the whole suite
```

- **Per scenario:** RED with the §6 message, then GREEN; the break-its of §8 each red then green. All in the PR.
- **Per story, the seam-alone run:** the whole suite green with the seam in and the test not yet moved (§5.3).
- **Per timing case, the loop:** the case's class 20 times in a row while a whole-suite run goes on beside it, before the
  fix (record how many failed) and after (0 of 20). A run that still fails after is a reopened case, not a rerun.
- **CI:** after the epic merges, the parent's census over the next 20 runs of each leg — a watch, not the proof (a case
  that fails in ~1 % of runs is absent from 20 runs 82 % of the time with no fix). The proof is each RED turned green.
- **The counts:** every suite green on Windows, Linux and macOS before and after; no test removed or skipped; the count
  grows by exactly the new scenarios, named per story.

## 11. What in the parent turned out stale (re-read at `aab069ca5`)

1. **C4's launcher lines** `ProcessLauncher.cs:287`, `:401`, `:437` are now `:481-482` (the deadline), `:487`/`:518`
   (the drain) and `:527` (the caller/timeout read) — E3 S1 moved the file.
2. **C6's fix item (3) is already done by E1.** The seven `ForgetTheLastSweep` callers sit in `fakecli-env`, and the
   scan's second rule exists (`EveryClassThatSetsTheProcessEnvironmentTests.cs:34-35`, `:123`, `:146-156`). The sites
   moved: `AScratchSweepRunsOnTheConsultationBeatTests.cs:63, 118, 147, 156, 173`, `VibeHomesTests.cs:160`,
   `TempDirsAreSwept.cs:311`; `_sweptUtc` is at `PanelService.cs:1408`, the reset at `:1441-1445`.
3. **S13 as written cannot be built under D3.** "The end-to-end test advances a manual clock 700 ms per turn" needs the
   round's `TurnLoop` on the manual clock; the scheduler is constructed with no clock in `PanelService.cs:72-77` and D3
   forbids threading one through `PanelService`/`RoundEngine`. S5 redesigns it (§3 C6, §8 S5); §12 Q2.
4. **`startDelayMs` does not exist** in the fake ACP agent's script (`tests_fakecli/AcpAgent.cs:15-52` lists every field)
   nor on the `serve` verb (`Serving.cs:29-30`). S2 adds the first; S6 the second, when it opens.
5. **"`TurnLoop`'s per-turn bound at `:134-138` reads the clock"** — `:129-138` is arithmetic on `left`; the only clock
   read in `TurnLoop` is the `Stopwatch` (`:45`, read at `:74`, `:81`, `:84`). The per-turn TIMER is the launcher's
   (`:481-482`) or the stand-in's.
6. **C13's quoted message** "No turn finished before the cap" is a paraphrase; the assertion's reason at `:475` is "the
   turns that answered before the cap are kept", and the FluentAssertions sentence is the one in §6 S14 (a).
7. **C17's RED** "`0 of 2 copied` when the fast copy is slowed past the budget" cannot be produced since #887: a budget
   that runs out before the first copy says the PHASE sentence (`ConsultantPlaces.cs:454-457`), never "0 of 2". The RED
   in §6 S16 is the sentence the product writes today.
8. **The resolver test's order.** The parent says the slow read's clock "is advanced only after the fast read was
   served"; requests are served one after another in order (`ServeAsync`, `:96-104`) and `Big.cs` is FIRST (`:528`), so
   the fast read runs AFTER the slow one is refused. The advance comes while the slow read is pending (§8 S4).
9. **D3's note on `MovableClock`** ("overrides only the wall time") is out of date since E3 S4: it moves the monotonic
   reading too (`TheRunsThemselvesTests.cs:217-220`) — which is why `ManualClock` subsumes it (§5.4).
10. **The wall-time bounds inside the C5 tests** (`AcpClientUsageTests.cs:44`, `:57`: `asked.Elapsed < 10 s` / `< 12 s`)
    are themselves the defect class — a cold start measured at ten seconds plus the teardown's graces reaches 12 s. The
    parent says ":55-57 stay as they are"; S2 replaces `:57` with the clock-side proof (§12 Q3).
11. **`FakeClock`** (`CollectRunTests.cs:409-412`) duplicates `FixedClock` — a reuse-rule finding, folded by S1 (§12 Q7).
12. **The parent's E4 "Depends on: E1"** holds; note that this branch descends from E3's head and therefore carries E2
    (`RunTemp*.cs`), which is not on `main` yet — the epic's PR is stacked on E2's and E3's.

## 12. Questions for the lead

- **Q1 — the teardown exception to contract (1).** §5.1 keeps every teardown grace (`AcpClient.cs:233` and the ACP
  session's/transport's graces; the launcher's drain in S6) on the SYSTEM clock, because they bound a real pipe after the
  outcome is decided and a manual clock nobody advances would hang them. Confirm this as the rule, or ask for the
  literal reading (every wait on `_clock`) — in which case S2's test must also advance the clock through the teardown,
  and a surviving grandchild would hang a test instead of ending it after 2 s.
- **Q2 — S13's redesign.** Pin the round-deadline subject with the pure `For` test + a scheduler-level manual-clock test
  + the end-to-end test without its sleep (recommended; D3 intact), or add an `internal` scheduler seam on
  `PanelService` so the end-to-end test can run the round's `TurnLoop` on the manual clock (a seam through
  `PanelService`, which D3 forbids today). The pass-through at `RoundEngine.cs:287` has no deterministic observer
  without that seam (B16's stated limit).
- **Q3 — the wall-time bounds at `AcpClientUsageTests.cs:44` and `:57`.** Replace with clock-side assertions (the clock
  moved exactly the deadline / exactly the bound) — recommended — or keep them as 60 s guards.
- **Q4 — `BoundedScheduler`'s ladder `Stopwatch` (`:616-625`) and `Task.Delay` (`:685`) beside its `_clock`.** The
  pattern the contract names, in the class that will hand its clock to `TurnLoop`. Leave it for the first RED that
  races the ladder (recommended under D3), or fold a 429-ladder test on the manual clock into S4.
- **Q5 — the sibling test** `AUsagePromptThatNeverAnswers_IsGivenUpOnAtItsBound_AndTheAnswerStands` (`:34-45`) has the
  same wall-clock shape and no case number. S2 moves it with the C5 test (recommended) — say if it should stay.
- **Q6 — the cornerstone ledger.** This document marks E4.S1 `model: fable`; the parent's line `Cornerstones: 0 of 2`
  (in `## Epics`, outside E4's section) becomes `1 of 2 — E4.S1` — E2 was built without a stories document, so its
  slot stays unmarked. Confirm, since this task changes only E4's section and the status line.
- **Q7 — `FakeClock` → `FixedClock`** in S1 (three lines, the reuse rule), or leave it.
- **Q8 — stronger counts.** With deterministic durations S4 can pin `EarlierTurns.Count == 3` where the parent has
  `NotBeEmpty` / `BeInRange(1, 3)`. Stronger, never weaker; say if the looser form is wanted for some reason.

### The lead's answers (2026-10-21)

- **Q1 — yes.** Graces that bound a real pipe AFTER the outcome is decided (`AcpClient.cs:233`, the ACP session and transport graces, the launcher's drain) stay on the SYSTEM clock: a manual clock nobody advances would hang a teardown. Contract (1) names this exception explicitly.
- **Q2 — the redesign.** S13 is the scheduler-level manual-clock test plus the pure `RoundDeadlineTests` and the end-to-end test without its sleep; no seam through `PanelService` or `RoundEngine` (D3 stands). B16's limit (the `RoundEngine.cs:287` pass-through has no deterministic observer) is recorded as a limit.
- **Q3 — yes.** The wall-time bounds at `AcpClientUsageTests.cs:44`/`:57` become clock-side assertions in S2; they are the defect class E4 removes.
- **Q4 — leave it for a RED.** `BoundedScheduler`'s ladder `Stopwatch` and `Task.Delay` beside `_clock` are named, not moved, until a failure is kept (D5).
- **Q5 — yes.** S2 also moves the sibling test `AUsagePromptThatNeverAnswers…` (`:34-45`).
- **Q6 — yes.** The parent's ledger line becomes "Cornerstones: 2 of 2 named — the per-run temp folder (E2, built), the clock contract (E4.S1)".
- **Q7 — yes.** S1 folds `FakeClock` (`CollectRunTests.cs:409`) into `FixedClock`.
- **Q8 — yes, where the manual clock makes it exact.** Pin `EarlierTurns.Count == 3` in S4/S5 when the clock decides the cut; keep the weaker bound only where a real process still races, and say why there.

## 13. Definition of Done

- [ ] `ManualClock` exists as §5.4 specifies; M1–M12 green with their reds seen; B1–B5 red then green; `MovableClock`
      gone, its nine sites moved; E3's `AtomicFile` window tests unchanged and green on it (S1).
- [ ] Each of `AcpClient`, `ConsultantPlaces`, `TurnLoop`, `SourceResolver` reads its one `_clock` exactly as §5.1's
      "after" column says; every "stays" site has its remark; nothing threaded through `PanelService`/`RoundEngine`;
      `ProcessLauncher` untouched unless C4's gate opened; `SecuritySources` untouched.
- [ ] Each seam landed alone first with the whole suite green on the default clock (§5.3), recorded per story.
- [ ] S12, S16, S14 (a–c), S13 (1–3) RED with their §6 messages, then GREEN; every break-it B6–B16 red then green;
      B16's limit stated.
- [ ] No timeout lengthened; no assertion weakened (the moved wall-time bounds are replaced by clock-side proofs, said
      per test); no test removed or skipped; counts named per story.
- [ ] The §10 loops recorded for C5, C13, C17, C6 (before and after); the 20-run CI watch started per leg.
- [ ] `research/module_tests.md` carries the contract and one section per story; `module_runners.md` and
      `module_server.md` name the clocks; the parent's E4 section and `todo/README.md` updated; the sibling plan told
      when S4 merges.
- [ ] `node .agents/conventions/tools/plan-lifecycle.mjs` clean; promotion of this document with the parent's.
