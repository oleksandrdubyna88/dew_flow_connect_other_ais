# PLAN — E4 stories: deadlines on an injectable clock (cornerstone area: the clock contract)

> Status: **S1–S5 built, the one docs pass done, the code gate pending (2026-10-22 update of the 2026-10-21 build); S6 (C4) gated on a kept failure (D5); not promoted. E4.S1 (the cornerstone: `ManualClock`, `ClockHandshake`, the structural scan) and S2–S5 are built on this branch, see the "status" sections of §8; the `research/` docs (`module_tests.md`, `module_runners.md`, `module_server.md`) were written in the §9 pass. Stories and the cornerstone spec by Fable.** Epic E4 of
> [PLAN_the_suite_is_deterministic_and_leaves_no_temp.md](PLAN_the_suite_is_deterministic_and_leaves_no_temp.md)
> (its `### E4`). Branch `feat/suite-e4-clock`, from E3's head `aab069ca5` (which carries E2); every line number below
> was read at that commit. Scope: `src_mcp` only — four product classes get an optional `TimeProvider` each
> (`runners/Acp/AcpClient.cs`, `src/Server/Consultation/ConsultantPlaces.cs`, `runners/Reviewers/TurnLoop.cs`,
> `runners/Feature/SourceResolver.cs`), one shared test clock, five test files, `research/module_tests.md`,
> `module_runners.md`, `module_server.md`. **Cornerstone: E4.S1** — the manual clock and the clock contract (§5); the
> parent's ledger line reads `Cornerstones: 2 of 2 named — the per-run temp folder (E2, built), the clock contract (E4.S1)`
> (Q6 — this line and Q6 say the same). **Folded on 2026-10-21, every claim re-read at `d9cdccf0e` (no `src_mcp` line
> moved since `aab069ca5`):** the E4 plan round (coai session `67791431`, verdict proceed) — §14; the risk consult for
> epic 4 (`d4afeda2`) — §15; the cadence consult for epics 4-5 (`15f733c7`), whose text lives in the parent under E5 —
> §16. Where §14–§16 and the sections above disagree, §14–§16 govern.
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
questions for the lead (§12), the Definition of Done (§13), and the three review folds of 2026-10-21 — the plan round
(§14), the risk consult (§15), the pointer to the cadence consult in the parent (§16). Where this document and the parent
disagree on a line number or a mechanism, this document is the fresher reading and §11 says why.

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

**Fix (S3).** `ConsultantPlaces` gets the seam (§5.1 table 2); the slow root's hook SIGNALS the test and waits for its
release instead of sleeping, and the TEST advances the clock past the budget (§5.4 point 6 — never the hook, which runs
on the product's thread), so the git phase and the fast copy run with no clock pressure and the slow copy is cut every time.
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
in-process launcher stand-in that signals each launch to the test — which advances the manual clock by the turn's
simulated duration, bounded by the request's timeout, and releases it — and answers `TimedOut` when the request's timeout
is shorter (the file already has an in-process launcher precedent, `TwoDecidedThenReal`, `:443-456`; the stand-in never
advances the clock itself, §5.4 point 6). The resolver test's slow read waits on the clock; the test advances it while the slow read is pending, and
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
`GetUtcNow()`. **One stated exception, with its reason:** a teardown grace that bounds REAL PIPE PROGRESS — the drain
of a killed process's pipe, the agent's last output after its exit — stays on the system clock. It never decides the
outcome the test races (the deadline has already fired, or the agent has already exited, when it starts); what it bounds
is how long a real pipe may still deliver bytes — and some of those bytes CAN still change what is kept
(`AcpSession.WatchExitAsync`'s 300 ms grace lets a buffered answer through before the pending requests are broken, §15
point 5). On a manual clock that nobody advances it would turn a 2 s bound on a real pipe into a hang. Each such site is
named in the tables with "system — teardown". The first draft's wording "after the outcome is decided" was corrected by
the risk consult (§15): it is "a grace that bounds real pipe progress". (Confirmed as the rule by the lead: §12 Q1.)

**Table 1 — `AcpClient` (`src_mcp/runners/Acp/AcpClient.cs`, 276 lines; 3 timed sites, 2 move, 1 stays; no `Stopwatch`, no `DateTime.UtcNow`, no `Timer`/`PeriodicTimer`).**

| Line | Today | What it decides | After S2 |
|---|---|---|---|
| `:114` + `:119` | `using var deadline = CreateLinkedTokenSource(ct);` … `deadline.CancelAfter(ask.Deadline);` | the turn's deadline (`AcpEnd.Deadline`, `:253`) | `using var turnTimer = new CancellationTokenSource(ask.Deadline, _clock); using var deadline = CreateLinkedTokenSource(ct, turnTimer.Token);` at `:114`; `:119` goes. The timer now starts a few microseconds earlier (before `new AcpSession`, `:115`); a preemption between the two CAN observe that, so S2 claims nothing about it — it asserts only that the turn's end is decided by the timer (§15, point 2). `MaxDeadline` (`:50`) keeps refusing what the timer cannot hold, and `:104-105` keeps refusing a deadline of zero or less before any start. |
| `:195` | `….WaitAsync(usageBound)` | the `/usage` bound | `….WaitAsync(usageBound, _clock)` |
| `:233` | `reading.WaitAsync(AcpTransport.DrainGrace)` in `SettledAsync` | the drain grace after the tree is dead | **system — teardown.** The tree was killed at `:129` (`transport.EndAsync`); this bounds a pipe a survivor may hold. |

Callees that arm a timer (none moves; all are teardown or token-only):
`AcpSession.CallAsync` `:165` `answer.Task.WaitAsync(ct)` — token only, so it reads the turn timer through the token;
`AcpSession.NotifyAsync` `:175` `new CancellationTokenSource(AcpTransport.GraceWait)` — system, teardown (`session/cancel`
after the token fired); `AcpSession.WaitUntilQuietAsync` `:184-199` (`DateTime.UtcNow` loop `:186-187`, `Task.Delay(50)`
`:197`) — system, teardown; `AcpSession.WatchExitAsync` `:260-273` `Task.Delay(ExitGrace, ct)` at `:265` — system,
teardown: the agent's last output after its exit (300 ms, `:54`) — a grace that bounds real pipe progress, and a buffered
answer that lands inside it is still handled by the pump before `Break` at `:272`, so it is NOT "after the outcome is
decided" (§15, point 5); `AcpTransport.EndAsync` `:234` `_draining.CancelAfter(DrainGrace)` — system,
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
`ReviewSnapshots.BuildOnceAsync` (`src_mcp/runners/Snapshots/ReviewSnapshots.cs:171` `Stopwatch`, judged against
`MaxElapsed` 60 s at `:214` and `:279`; `SnapshotOptions.cs:23` `DefaultMaxElapsed`) — **an UNSEAMED HOST SAFETY
CEILING, not a test-controllable budget** (plan round §14, finding 1). It is D6's per-copy ceiling: it protects a
person's machine from one copy that never ends, it reads the system clock, and it STAYS there in E4. No E4 test drives
it — C17's test races the row's 1 s budget, which fires long before 60 s, and the slow copy is cut by the budget's
cancelled token (`:343`), never by this ceiling. Why it is not seamed: a seam on `ReviewSnapshots` would land without
a RED (D3), and a safety ceiling on a manual clock that nobody advances would let a test turn a real runaway copy
into a hang. It is listed under "callees, none moves" in the counts below so nobody moves it by accident.
`VibeHomeTurns.cs:27` `Gate.WaitAsync(ct)` — token only.
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

**Disposal order (risk consult, §15 point 2).** The timer source is declared FIRST and the linked source SECOND —
`using var timer = new CancellationTokenSource(delay, _clock); using var linked = CreateLinkedTokenSource(ct, timer.Token);`
— so the `using`s dispose the LINK first and the timer after it: a timer that fires between the two disposals meets a
disposed link, which ignores it, rather than a live link holding a disposed timer. Every "after" cell of §5.1 is written
in that order; a reviewer checks the ORDER, not only the shape.

**Zero budgets (risk consult, §15 point 2) — a stated behaviour difference, pinned by a test.**
`new CancellationTokenSource(TimeSpan.Zero, clock)` cancels SYNCHRONOUSLY in the constructor (the state is set; no timer
is made), while today's `CancelAfter(TimeSpan.Zero)` SCHEDULES a timer that fires a moment later on the timer thread. So
after the seam a zero budget is already cancelled when the product's first `ThrowIfCancellationRequested` runs, where
today it may or may not be. **Checked at `d9cdccf0e`: no product path hands a zero budget to any of the four classes.**
`AcpClient` refuses `ask.Deadline <= TimeSpan.Zero` before any start (`AcpClient.cs:104-105`); the row budget and the
reviewer timeout come through `PanelSettings.IntVar`, which admits only values `> 0` (`PanelSettings.cs:1704-1705`;
`QuestionConsultSettings.cs:149`, `PanelSettings.cs:1009`), so `OneShotAsync`'s two callers (`QuestionRowLaunch.cs:242`,
`ConsultantCheck.cs:232` — the latter capped by `ConsultantCheck.Budget`, `:35`) never pass zero; the resolver's three
product sites pass no `readDeadline` and get `SourceBudget.ReadDeadline` (30 s, `SourceBudget.cs:57`). A zero budget is a
TEST's value only. S3 and S4 each pin what it does on the manual clock — the budget is out before anything is copied /
the read is refused as timed out with the same sentence a budget that ran out gives — so the synchronous cancellation is a
contract point, not a surprise.

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
5. **A callback never runs under the clock's lock, and always on the advancing thread, in due order.** The due timer
   is picked and marked running under the lock; the callback runs outside it, synchronously, on the thread that called
   `Advance`; the next due timer is picked only when it has returned. No thread-pool dispatch — a callback posted
   elsewhere would race the product's completion, the timer's disposal and the test's assertions, and the order of two
   timers due in one advance would be the pool's, not the clock's. A callback may make, change or dispose a timer.
   Consequence, stated for the tests: `Advance` runs the product's continuation inline on the test's thread up to its
   next `await` (a cancellation token's registrations run synchronously on the cancelling thread) — so a test never
   calls `Advance` while holding a lock, and asserts after awaiting the product's task, never inside the advance.
6. **`Advance` is the TEST DRIVER's call — never a product thread's** (plan round §14, finding 0). A product hook —
   S3's `BeforeCopyOf`, S4's launcher stand-in — runs on the product's thread, under the product's own locks and
   between its `await`s; an `Advance` from there would run the product's timer callbacks inline on that thread: a
   cancellation registration cancelling the very copy that is calling the hook (re-entrancy), or a callback waiting for
   a lock the hook holds (a deadlock). So a hook only SIGNALS that the product has reached the point the test means (a
   `TaskCompletionSource`, set once) and then WAITS for a release the test controls; the test awaits the signal,
   advances the clock, and releases the hook. The release is in a `finally` of the test, and the hook's wait is bounded
   by the test's guard, so a failing test never leaves a product thread parked. `ManualClock` ENFORCES the rule: an
   `Advance` entered while another `Advance` is running — nested from a callback, or from a second thread at the same
   time — throws `InvalidOperationException` naming the thread that holds the advance. S1's `ClockHandshake` (point 11)
   is the one helper every hook uses for the signal-and-wait, so there is one shape and not three.
7. **Disposal** (risk consult, §15 point 1). `ITimer.Dispose()` RETIRES THE TIMER AT ONCE and returns — even when it is
   called from inside that timer's own callback. It must: `CancellationTokenSource` disposes its timer INSIDE the timer
   callback, before it runs the token's registrations, so a `Dispose` that waited for "the running callback" would wait
   for itself. Only `DisposeAsync()` waits: it completes once a callback already picked to fire has returned (a
   disposal between the pick and the call still waits — `wsl_care`'s retro finding), and at once when called from
   inside its own callback. `Change` after disposal schedules nothing and returns `false`, which `PeriodicTimer` reads
   as "disposed". A due time or period below −1 ms or above 0xFFFFFFFE ms is refused with
   `ArgumentOutOfRangeException`, as the system timer refuses it — so a product value the system would refuse is refused
   on the manual clock too (`AcpClient.MaxDeadline` keeps its meaning).
8. **`ActiveTimers`** (how many are scheduled) and **`Task WhenTimersAsync(int atLeast)`** — completes when at least that
   many timers are scheduled, at once if they already are. This is the EVENT a test waits on before advancing: "the
   product has armed its deadline". It replaces polling a log file or a `Thread.Sleep`. (An addition over `wsl_care`,
   which offers the count only.) A timer that fired or was disposed LEAVES the count (M11) — and because a
   `CancellationTokenSource` disposes its timer before its registrations run, no registration can ever observe its own
   timer still counted; a test that means "the deadline struck on the clock" asserts the TOKEN's state after the
   advance, never the count inside a callback (§15, point 3 — the reason S4's first `slow.Cut` was wrong).
9. **`RecordingWait` is not carried over** — it is `wsl_care`'s follower's injected wait; here a stand-in waits with
   `Task.Delay(delay, clock, ct)` (S4's `SlowShow`).
10. **Thread-safe**: one lock over `_now`, `_monotonic` and the timer list; every public member takes it. `Advance`
    additionally holds an "advancing" mark (point 6), set under the lock and cleared in a `finally`.
11. **`ClockHandshake`** (`src_mcp/tests/ClockHandshake.cs`, S1 — the small handshake helper of §14 finding 0): the hook's
    side is `Signal()` (completes `Task Reached`, once) then `await WaitForReleaseAsync(ct)`; the test's side is
    `await handshake.Reached; clock.Advance(…); handshake.Release();` with `Release()` also called from the test's
    `finally` (idempotent). One helper, three users: S3's slow-root hook, S4's `ClockedLauncher` and `SlowShow`.

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
| M7 | `DisposeAsync` during a running callback completes only after the callback returns; `Dispose()` during the same running callback returns AT ONCE | guard — green first; broken once by completing the idle task before the callback ends (B2), and once by making `Dispose()` wait (B17: M13 hangs under its guard) |
| M8 | `Change` after `Dispose` returns `false` and schedules nothing; `PeriodicTimer(period, clock)` ends its `WaitForNextTickAsync` on disposal | `Change` returns `true` / the tick still arrives |
| M9 | `GetElapsedTime(stamp)` moves with `Advance` and not with `JumpWallClock`; `GetUtcNow()` moves with both | `JumpWallClock` moves the elapsed too (or does not exist) |
| M10 | a due time of `uint.MaxValue` ms is refused with `ArgumentOutOfRangeException`, as `TimeProvider.System.CreateTimer` refuses it (both clocks asserted) | the manual clock accepts it |
| M11 | `WhenTimersAsync(1)` is pending while nothing is armed and completes when the first timer is armed; `ActiveTimers` counts scheduled timers and drops when one fires or is disposed | the member does not exist / the count stays |
| M12 | a periodic timer with a 100 ms period fires three times inside one `Advance(300 ms)` and once more on `Advance(100 ms)` | nothing fires |
| M13 (§15) | `new CancellationTokenSource(2 s, clock)` whose token is LINKED into a second source with a caller's token (§5.2's shape), advanced to its deadline: `Advance` RETURNS, and the linked token is cancelled — the source's `Dispose` of its own timer from inside the timer's callback must not wait for that callback | `Advance` never returns (a deadlock; the arm runs under the test's bounded guard and fails with its sentence) |
| M14 (§15) | a timer disposed between its selection and its callback: the callback does not run and `Dispose()` returned at once; a `DisposeAsync()` started on a second thread while a callback runs completes only after it returns | the callback runs on a disposed timer / `DisposeAsync` completes early |
| M15 (§14) | `Advance` from inside a callback (nested) throws `InvalidOperationException` naming the advancing thread; `Advance` from a second thread while one is running throws the same; the first advance still fires every timer it reached | the nested advance runs (re-entrancy) |
| M16 (§15) | zero: `new CancellationTokenSource(TimeSpan.Zero, clock)` is cancelled before any `Advance`; a timer made with a due time of zero fires on `Advance(TimeSpan.Zero)` and not before (today's `CancelAfter(0)` shape) | the zero-delay source is pending until an advance / the zero timer fires on `CreateTimer` |
| M17 (§15) | on §5.2's two-source shape — caller-first: the caller's token cancelled, the link cancelled with `ct.IsCancellationRequested` true and the timer still counted; deadline-first: the deadline fired by `Advance`, the link cancelled with `ct.IsCancellationRequested` false; dispose-before-deadline: both sources disposed, then the advance past the deadline fires nothing (`ActiveTimers == 0`, the link untouched) | the disposed timer still fires / the caller's cancellation reads as a deadline |

**Break-its for S1** (change the clock, run `ManualClockTests`, see red, restore): B1 fire inside the lock (M6 red);
B2 skip `Begin()` on pick (M7 red); B3 order timers by arming instead of due time (M4 red); B4 fire a timer on
`CreateTimer` when already due instead of on `Advance` (M1's "not cancelled after 1999 ms" arm red, M16's timer arm
red); B5 move the monotonic reading in `JumpWallClock` (M9 red); B17 make `Dispose()` wait for a running callback (M13
hangs under its guard, M7's second arm red); B18 drop the "advancing" mark (M15 red: the nested advance runs); B19
fire callbacks through `ThreadPool.QueueUserWorkItem` (M4's order arm red under repetition, M6's "on the advancing
thread" assertion red).

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
   §5.1: it bounds real pipe progress after the deadline has fired (`TimedOut` is decided; what the pipe still delivers
   is the evidence kept beside it), and a manual clock nobody advances would hang it. S15's
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
| S16 | `CopiesThatOutrunTheRowsBudget_RefuseThePlace_SayingHowManyWereCopied_AndKeepNone` on `ConsultantPlaces`' own clock; the slow root's hook signals and waits (`ClockHandshake`), the test advances the clock past the budget and releases it | C17 | with a launcher stand-in that delays every `git` answer by 1.1 s (the git phase outlives the budget, as on the runner): `Expected place.Refusal "copying the granted folders took longer than the row's budget (1 s): the budget ran out while confirming 2 candidates with git, before anything was copied — …" to contain "1 of 2" because the sentence says how far the copying got.` — every run | S3 |
| S14 | the two turn tests and the resolver test on the manual clock, through an in-process launcher stand-in / a clock-waiting `SlowShow` | C13 | (a) cap test with `FAKECLI_SLEEP_MS=800` against the 700 ms turn: `Expected outcome.EarlierTurns not to be empty because the turns that answered before the cap are kept.` (b) work-cap test with `FAKECLI_SLEEP_MS=950`: `EarlierTurns.Count` is 0, outside `[1, 3]`. (c) resolver test with `SlowShow` applied to `Cart.cs` as well: `Expected tail … to contain "### src/Cart.cs lines 28-35" because the turn goes on with what was served`. Each every run. **Break-it after the move:** the cap check at `TurnLoop.cs:74-79` removed → (a) ends in the stand-in's own failure after its 20-launch ceiling, not `TimedOut` | S4 |
| S13 | `RoundDeadlineTests` (pure): `For(followUps: 3)` = 4 × `For(followUps: 0)` for a 2 s reviewer timeout, one vendor, one role; `AOneTurnBudget_CutsAFourTurnConversation_AtTurnThree_AndTheScaledBudgetNever` at scheduler level on the manual clock (700 ms per turn, three follow-ups: cap 2 s → `TimedOut` with exactly 2 earlier turns — the THIRD turn IS launched with the 600 ms left and ends `TimedOut`, because `TurnLoop.cs:74-80` launches whenever anything is left (§15, point 4); cap 8 s → `Ok` with `Turns == 4`); the end-to-end test keeps `proceed` / four prompts / "all 1 reviewers answered" with NO sleep, plus the wiring arm of §8 S5 (the warning line at `RoundDeadline.cs:28` names `80`, the scaled minutes) | C6 | (1) no `RoundDeadline.For` test exists (red by absence: the new test asserts 8 s and 2 s); (2) the scheduler-level test with a one-turn budget: `Expected outcome to be ReviewerOutcome.TimedOut because a conversation of four 700 ms turns does not fit one 2 s turn, but found ReviewerOutcome.Ok` when the cap is computed without the follow-ups (break-it at `TurnLoop.cs:43`); (3) today's end-to-end test with `FAKECLI_SLEEP_MS=1900`: `Expected … "proceed" … the round waited for the whole conversation` fails with "not started … the round reached its limit" — fixed sleeps against a fixed deadline decide the outcome | S5 |
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

- **Goal.** `ManualClock` as §5.4 specifies, `ManualClockTests` M1–M17 green with their reds seen, `ClockHandshake`
  (§5.4 point 11), `MovableClock`'s nine sites moved and the class deleted, `FakeClock` folded into `FixedClock` (Q7:
  yes), the STRUCTURAL SCAN below with its companion, and the contract (§5.1 tables, §5.2, §5.3, the teardown rule)
  written into `research/module_tests.md` — in the final docs pass of §9 — as the checklist every later story is
  reviewed against.
- **The structural scan (plan round §14, finding 3).** `EveryBudgetInAClockedClassReadsItsClockTests`
  (`src_mcp/tests/EveryBudgetInAClockedClassReadsItsClockTests.cs`), the shape of
  `EveryClassThatSetsTheProcessEnvironmentTests` (`:31-35` the patterns, `:38-48` the offenders, each rule with the
  companion that the scan still finds a known instance): over a NAMED SET of product source files — the four classes
  `runners/Acp/AcpClient.cs`, `src/Server/Consultation/ConsultantPlaces.cs`, `runners/Reviewers/TurnLoop.cs`,
  `runners/Feature/SourceResolver.cs`, and `runners/Reviewers/BoundedScheduler.cs` (the class that hands its clock to the
  loop) — it flags every `Stopwatch`, `.CancelAfter(`, `DateTime.UtcNow`, `Task.Delay(` whose argument list carries no
  `TimeProvider`, and `.WaitAsync(` with a timeout and no `TimeProvider`; **multi-line aware** (an argument list is read
  to its closing parenthesis across lines, so `WaitAsync(\n  usageBound)` is not missed). Sanctioned sites are an
  allow-list of `(file, snippet)` pairs, each with its reason in the test source, and the COMPANION fails when an
  allowed snippet is no longer found — so a site that moves onto the clock must also leave the list, and a stale list
  is red, not silent. The allow-list on day one: `AcpClient.cs` `reading.WaitAsync(AcpTransport.DrainGrace)` (`:233`,
  teardown, §5.1); `BoundedScheduler.cs` `:290` and `:365` (`Stopwatch queued`, reports), `:461` (`Stopwatch watch`,
  the reported elapsed), **`:616` (the ladder's `Stopwatch`, judged against the ladder's budget at `:623`/`:625`) and
  `:685` (`Task.Delay(wait, ct)`, the ladder's wait) — the DEFERRED sites of Q4, listed with their reason ("named, not
  moved, until a 429-ladder failure is kept — D5"), so the scan PINS them rather than ignoring them**. The set of
  scanned files GROWS per story and that is each seam story's structural RED: S1 lands the scanner with
  `BoundedScheduler` only (green on its five allowed sites; the companion finds all five); S2 adds `AcpClient.cs` (red
  on `:119` and `:195` before the seam, green after, `:233` allowed); S3 adds `ConsultantPlaces.cs` (red on `:289`,
  `:334`); S4 adds `TurnLoop.cs` (red on `:45`) and `SourceResolver.cs` (red on `:200`). The scan is NOT over
  `ReviewSnapshots.cs`, `ProcessLauncher.cs` or `ReviewerExecutor.cs` — §5.1 names their sites as "callees, none
  moves"; a later story that seams one of them adds it to the set with its own RED.
- **Files.** New: `src_mcp/tests/ManualClock.cs`, `src_mcp/tests/ManualClockTests.cs`, `src_mcp/tests/ClockHandshake.cs`,
  `src_mcp/tests/EveryBudgetInAClockedClassReadsItsClockTests.cs`. Changed: `TheRunsThemselvesTests.cs`
  (`:203-221` removed; its own use moved), `AMoveOverAHeldFileIsRetriedTests.cs`, `ASessionSaveMeetsABusyFileTests.cs`,
  `AnIdleServerReadsNothingTests.cs:162`, `AuthorPeersTests.cs:184`, `RoundAuthorsStepTests.cs` (3 sites),
  `TheSendsThemselvesTests.cs:27`, `CollectRunTests.cs:409-412` (`FakeClock` → `FixedClock`). Docs (final pass, §9):
  `research/module_tests.md` (new section "The suite is deterministic — E4 S1: the clock contract and the manual clock").
- **RED.** M1's: `Expected source.IsCancellationRequested to be True because the clock was advanced past the delay, but found False.` — seen with `ManualClock` stubbed as a renamed `MovableClock` (no timers). Then each of M2–M5, M8–M13,
  M15–M17 red the same way (M13 and M15 red by absence of the rule: the stub's `Advance` has no mark and the stub's
  timers are real), M6, M7 and M14 green as guards and broken once (B1, B2, B17). The scan: red by absence, then
  green on `BoundedScheduler`'s five allowed sites; the companion watched red once by removing `:616` from the
  allow-list AND from the file (a stale list), restored.
- **Break-its.** B1–B5, B17–B19 of §5.4.
- **Done when.** M1–M17 green; B1–B5, B17–B19 each red then green; the scan and its companion green on `BoundedScheduler`;
  the whole `src_mcp` suite green with `MovableClock` gone (E3's `AtomicFile` window tests —
  `AMoveOverAHeldFileIsRetriedTests`, `ASessionSaveMeetsABusyFileTests` — pass on `ManualClock` unchanged, which proves
  the subsumption); test count before and after named (+19, −0: M1–M17, the scan, its companion); the contract section
  for `module_tests.md` written and held for the docs pass.
- **Roles.** Fable: this spec (§5) and the review of the clock against it. Sonnet: codes. Opus: reviews the diff against
  §5.4's eleven contract points, the scan's allow-list and the reuse table. `model: fable` on the story.

### S1 — status: built 2026-10-21 (Sonnet; the spec above governed, deviations listed)

**Built, no product class changed.** New: `src_mcp/tests/ManualClock.cs`, `ManualClockTests.cs` (18 tests: M1–M17 plus the split-out "DisposeAsync from inside the timer's own callback"), `ClockHandshake.cs`,
`ClockHandshakeTests.cs` (3), `EveryBudgetInAClockedClassReadsItsClockTests.cs` (6). Changed: `MovableClock`'s nine sites (seven files: `AMoveOverAHeldFileIsRetriedTests`, `ASessionSaveMeetsABusyFileTests`,
`AnIdleServerReadsNothingTests`, `AuthorPeersTests`, `RoundAuthorsStepTests` ×3, `TheRunsThemselvesTests` field, `TheSendsThemselvesTests` field) renamed to `ManualClock`, the class deleted from
`TheRunsThemselvesTests.cs`; `FakeClock` in `CollectRunTests.cs` replaced by `FixedClock` and deleted. E3's `AtomicFile` window tests pass on `ManualClock` unchanged. The contract is written in
`research/module_tests.md` ("The suite is deterministic — E4 S1"); the S2–S5 sections and the other two modules follow in the final docs pass (§9).

**Deviations from the spec, each with its reason.**

1. **18 + 3 + 6 tests, not +19.** M14 is two tests (the pick/dispose arm and `DisposeAsync` from inside the timer's own callback, which is point 7's "at once" and needed its own deterministic arm); `ClockHandshake`
   has its three-test class (a new helper ships with tests); the scan has four self-tests of the scanner on synthetic source (the precedent's shape) beside the scan and its companion.
2. **`ManualClock.AfterPick`** (an `internal Action`, default no-op) — not in §5.4. M14's "a timer disposed between its selection and its callback never runs it" has no deterministic way to be reached from
   outside (the pick and the call are consecutive statements on the advancing thread); the hook is that window. Broken once (B21: the disposed-after-pick check removed → red).
3. **B4's limit.** §5.4 says B4 turns "M1's not-cancelled-after-1999 ms arm" red; a timer due in 2 s is not due at creation, so B4 as worded cannot. B4 is the zero-due form (a timer fired on `CreateTimer` when due now) and is red in M16's timer arm and M5.
4. **Two arms were already green on the stub**, said rather than hidden: M10 (the stub's timers ARE the system's, which refuses the range) and the zero-delay arm of M16 / the `Change`-after-`Dispose` arm of M8 (the framework
   behaves so on its own). M10 was then proved by a break-it of its own (B20, the range check removed), as M6/M7/M14 are guards broken once (B1, B2/B17, B21).
5. **The scan is also over `DateTimeOffset.UtcNow` and `.Now`**, not only `DateTime.UtcNow` (a superset; green on `BoundedScheduler`); a `WaitAsync` whose sole argument is a token (`ct`, `x.Token`) is not a budget.
   Sanctioned sites are matched on the statement around the site (the joined-line source has no line numbers), so the five allow-list snippets are statements, not `:290`-style lines.
6. **`ClockHandshake` is single-shot** as specified ("completes `Reached`, once"); S4's `ClockedLauncher` is reached on every launch, so it takes one handshake per launch (a factory of handshakes), not one reused. Said here so the S4 brief does not assume a re-armable one.
7. **Build trap recorded** in `module_tests.md`: a break-it restore by `copyFileSync` keeps the old mtime and the next build ran the previous broken binary; restore by writing the file.

**Reds and break-its** are in `module_tests.md` (the table per test); in short: M1's red is the spec's sentence verbatim; M6/M7/M14 were guards broken by B1, B2/B17, B21; B1–B5, B17–B21 each red then green; the scan was seen red with
the allow-list emptied (5 offenders), with the ladder's entry removed (1 offender: the 616 `Stopwatch`), with a scanner that finds nothing, and the companion red with the ladder's `Stopwatch` moved onto `_clock` (restored).

### S2 — C5: `AcpClient`'s clock + S12

- **Goal.** `AcpClient` reads its one `_clock` for the turn's deadline and the usage bound (§5.1 table 1); the C5 test
  no longer pays the cold start; no fixed sleep.
- **Files.** `src_mcp/runners/Acp/AcpClient.cs` (`:31` the parameter; `:114`, `:119`, `:195` as table 1; `:233` stays and
  gets a one-line remark naming the teardown rule). `src_mcp/tests_fakecli/AcpAgent.cs`: a new script field
  `"startDelayMs": N` — "on start, before anything is read" (documented in the header `:15-52`; a `Thread.Sleep` before
  the stdin loop). `src_mcp/tests/FakeAcpAgent.cs`: a `StartDelay(ms)` field helper beside `Models` (`:64`) and
  `UsageReply` (`:67`). `src_mcp/tests/AcpClientUsageTests.cs`: `ClientFor` (`:18-19`) gains `TimeProvider? clock`; the
  C5 test (`:47-58`) rewritten; the sibling test (`:34-45`, the usage bound alone, 1 s) moved the same way (§12 Q5).
  `src_mcp/tests/AcpClientTests.cs:660-671` — **C23** (`TheCancelReachesAnAgentThatIsStillListening`, the parent's §4
  C23; moved here from E5 by the fold of 2026-10-21, §16): the same class, the same seam, no new product line — its 5 s
  deadline "counts the agent's own start too" (its own remark, `:664-665`), which is C5's mechanism exactly. Docs (final
  pass, §9): `research/module_runners.md` (the ACP client's clock), `research/module_tests.md` ("E4 S2").
- **Order inside the story.** (1) The RED on today's code: `startDelayMs: 2500` in the fake, deadline 2 s → the CI
  sentence, every run; and for C23, `startDelayMs: 5500` against its 5 s → `Expected agent.Received("session/cancel") to
  contain a single item, but the collection is empty` (the run-39325900821 sentence), every run. (2) The seam alone,
  default clock, whole suite green (§5.3); the S1 scan gains `AcpClient.cs` — red on `:119`/`:195` before the seam,
  green after. (3) The test on the manual clock:
  `var turn = client.AskAsync(agent.Ask(deadline: 2 s), ct); await clock.WhenTimersAsync(2).WaitAsync(30 s guard, ct); clock.Advance(2 s); var outcome = await turn;`
  — the second timer is the usage bound at `:195`, armed only after the first prompt was answered; the 30 s is a guard
  that a correct run never reaches, not a decision. Assertions: `End == Answered`, `Tokens.Captured == false`,
  `clock.GetUtcNow() - start == 2 s` (the turn ended when the clock had moved exactly the deadline, with the 60 s bound
  never reached) in place of the wall-time bound at `:57` (§12 Q3). The `startDelayMs` stays in the fake's script for
  this test (any value — it no longer matters), which is the point. **After the last advance nothing is advanced for the
  teardown** (§15, point 5): `transport.EndAsync` and `SettledAsync` run on the system clock, so the assertions that
  follow are real-time-bounded by `GraceWait` + `DrainGrace` (3.5 s), and the test asserts them on every arm — the
  received answer is KEPT (`outcome.Answer` holds the first prompt's text; `AnswerChars` set) and the agent is gone
  (`Gone(outcome.AgentPid)`). C23 on the clock: `WhenTimersAsync(1)` (the turn timer) AND the fake has received
  `session/prompt` (the fake's log, as `agent.Received` reads it today — an event, polled under the guard), then
  `Advance(5 s)`: `End == Deadline`, `session/cancel` received once, the agent gone — the unresponsive-peer arm the risk
  consult asked for, since the `AwaitCancel` script answers nothing until the cancel. (4) Two arms the risk consult names
  (§15, point 2), both on the clock and never advanced: **caller-first** — the caller's token cancelled after the first
  answer: the end is what `Failed(e, ct.IsCancellationRequested, …)` at `:125` gives a caller's cancellation, never
  `AcpEnd.Deadline`, and the teardown is still bounded; **dispose-before-deadline** — the turn ends by its own answer
  (no `/usage` hang), both sources are disposed by the `using`s, and `clock.ActiveTimers == 0` after the turn: nothing
  of the turn is left armed.
- **RED (S12).** `Expected outcome.End to be AcpEnd.Answered because … but found AcpEnd.Deadline.`
- **Break-its.** B6: `UsageAsync`'s catch (`:199-202`) rethrows `OperationCanceledException` → the turn ends `Deadline`
  → S12 red with the same sentence (the test observes its subject). B7: `:119` `CancelAfter(ask.Deadline)` put back on
  the linked source (the system timer) with the clock timer removed → the test's wait for the second timer sees only
  one and the guard ends it: `TimeoutException` after 30 s — "the turn's deadline is not on the clock". B8: `:195`
  without the clock → the moved sibling test (`:34-45`, 1 s bound advanced on the clock) never sees its `TimeoutException`
  and ends by the guard.
- **Done when.** S12 red then green; C23's RED (`startDelayMs` past 5 s) red then green; B6–B8 each red then green; the
  suite green on the seam alone first; the scan red on `AcpClient.cs` then green; the §10 loop of `AcpClientUsageTests`
  and of `AcpClientTests.TheCancelReaches…` recorded (20 runs beside a whole-suite run, before: the failures; after: 0 of
  20); C5 and C23 not seen in the next 20 Windows CI runs (a watch); test count +2 (the caller-first and
  dispose-before-deadline arms; three tests rewritten) named.
- **Roles.** Opus brief (table 1 is the brief's checklist), Sonnet code, Opus review; Fable reads the diff against
  §5.1–5.3.

### S2 — status: built 2026-10-21 (Sonnet; §5.1 table 1 and §15 governed, deviations listed)

**Built.** Product: `AcpClient` takes `TimeProvider? clock = null` (last parameter of the primary constructor, field `_clock`, default `TimeProvider.System`); `TurnAsync` arms `new CancellationTokenSource(ask.Deadline, _clock)` FIRST and the link second (`CreateLinkedTokenSource(ct, turnTimer.Token)`), the old `CancelAfter` is gone; `/usage` is `.WaitAsync(usageBound, _clock)`; `SettledAsync` stays on the system clock with a remark naming the teardown rule. Test support: `tests_fakecli/AcpAgent.cs` script field `startDelayMs`; `FakeAcpAgent.StartDelay(ms)` and `WhenReceivedAsync(method, ct)`; new `ClockedAcpTurn` (the test-side driver: starts the turn, runs the test's drive under a 30 s guard, cancels the caller in a `finally`). Tests: S12 and the sibling usage test moved onto `ManualClock`; C23 moved; new `AcpClientClockTests` (3). The scan's set gained `AcpClient.cs` (one line) and its allow-list gained the `DrainGrace` site.

**Seam alone.** With only the constructor parameter and the `_clock` field in (no site moved, no test moved), the whole suite: 13140 tests, 13018 passed, 120 skipped, 2 failed — `AConversationPlaceTests.CopiesThatOutrunTheRowsBudget…` (C17, S3's open case: `0 of 2 copied`) and `QuestionOutlineTimingTests` (61–68 s against a 60 s bound on this machine; it fails alone too, it times the outline of this checkout, and does not touch `AcpClient`). Neither is in S2's area; both fail the same way before the seam (S3's case is red by design). `AcpClient`'s own classes were green.

**Reds (watched on today's code, with the fake's `startDelayMs`).** S12 (`startDelayMs` 2500, deadline 2 s): `Expected outcome.End to be AcpEnd.Answered {value: 0} because the deadline struck while /usage was asked, and /usage never changes the end: the agent did not answer within the 2s this turn was given, but found AcpEnd.Deadline {value: 3}.` C23 (`startDelayMs` 5500, deadline 5 s): `Expected agent.Received("session/cancel") to contain a single item, but the collection is empty.` Scan with `AcpClient.cs` added, before the seam moved its sites: `AcpClient.cs: CancelAfter .CancelAfter(ask.Deadline) in …; AcpClient.cs: WaitAsync .WaitAsync(usageBound) in …` (the two sites), green after.

**Break-its (product changed, test run, restored by writing the file).** B6 (`UsageAsync` stops swallowing `OperationCanceledException`): S12 red with the same sentence as above. B7 (`CancelAfter` back on the linked source, no clock timer): S12 ends by the guard — `the product never reached what the test waits for within 30s on the manual clock — a deadline or bound that is not on the clock never arms its timer there`. B8 (`.WaitAsync(usageBound)` without the clock): the moved sibling test ends by the same guard sentence. Extra: B9 (the timer source not `using`): `Expected clock.ActiveTimers to be 0 because the turn's deadline timer is disposed with the turn, but found 1`; B10 (`Failed(e, false, …)`): `Expected outcome.End to be AcpEnd.Cancelled … but found AcpEnd.Deadline`; B11 (the zero-deadline refusal removed): `Expected outcome.End to be AcpEnd.NotStarted, but found AcpEnd.Protocol`; B12 (the drain grace moved onto the clock): the scan's companion is red — `reading.WaitAsync(AcpTransport.DrainGrace)` matches 0 sites, not one (the stale allow-list).

**Arms of §8 S2 / §15.** Caller-first (`ACallerWhoCancelsFirst…`): `Cancelled`, the clock unmoved, `session/cancel` once, agent gone, `ActiveTimers == 0`. Dispose-before-deadline (`ATurnThatEndsByItsOwnAnswer…`): `ActiveTimers == 0` after the turn and after a one-hour advance. Zero (`ADeadlineOfZero…`): `NotStarted`, no log, no timer. After the last advance every arm reaches the teardown on the system clock: the received answer is kept (`outcome.Answer == "OK"` in S12) and the agent is gone (`Gone(outcome.AgentPid)`).

**Loop (§10).** `AcpClientUsageTests` (12) + C23, 20 runs in a row: 0 of 20 failed. The "before" figure was not collected as a loop: the red runs above fail on every run (the sentence, every run), which is stronger than a rate.

**Whole suite after the move.** 13145 tests, 13023 passed, 120 skipped, 2 failed, both only in the full run: `QuestionConsultScenarioTests.AQuestionIsAnsweredByARealChild_FencedAndRecorded` (a real fake-CLI child; status `failed`) and `AReviewerThatAsksForSourceIsAskedAgainTests.AConversationThatOutlivesItsCap…` (C13, S4's open case). Both pass when their class runs alone (6 of 6, 16 of 16) — suite-only failures, listed, not called flakes; the second is the case S4 closes; the first is not in S2's area and is reported to the lead. The log's disk-full line in the runner's output is the machine's (D: has 61 GB free; it is the runner's own log, noted).

**Deviations, each with its reason.**
1. **+3 tests, not +2.** The plan counted the caller-first and dispose-before-deadline arms; the zero arm (a stated contract point of §5.2, §15 point 2) is the third. Three tests were rewritten (S12, the sibling, C23). The scan and its companion are unchanged in number. Total of the executable: 13145 (the seam-alone run 13140 + the 3 new tests + 2 rows of `NoSourceFileCarriesAControlByteTests`, a per-file theory that gains one row for each of the two new source files, `AcpClientClockTests.cs` and `ClockedAcpTurn.cs`).
2. **`FakeAcpAgent.Entries()` reads the log shared** (`FileShare.ReadWrite | Delete`). `WhenReceivedAsync` polls the log while the agent appends; `File.ReadAllLines` shares read only, so the agent's next append failed, the agent died and the turn ended in a protocol error — the symptom was the caller-first test ending by its guard. A helper bug of the new poll, found by the test; fixed in the helper, no product line.
3. **The `startDelayMs` of S12's tests** stays in the script (2500 ms against the 2 s deadline) — the point of the test. The sibling test has none: its bound (1 s) is advanced on the clock.
4. **No `Stopwatch` in the tests any more**: the wall-time bounds at the old `:44`/`:57` are replaced by `clock.GetUtcNow() - start == bound` (the turn ended when the clock had moved exactly the bound / the deadline).

**For the final docs pass (§9) — what `module_tests.md` and `module_runners.md` must say.** `module_tests.md`, section "E4 S2": the seam-alone result; the three reds; B6–B12; `ClockedAcpTurn` (test drives, product never advances; the 30 s guard is a bound a correct run never reaches); `startDelayMs`; `WhenReceivedAsync` and the shared log read; the scan's `AcpClient.cs` entry and its `DrainGrace` exception. `module_runners.md`, the ACP client: one `_clock` (deadline timer source declared before the link; `/usage` bound on the clock; the three graces — `SettledAsync`, `AcpTransport.EndAsync`, `AcpSession` — stay on the system clock because they bound a real pipe after the end); a caller's cancellation still reads `Cancelled`, not `Deadline`.

### S3 — C17: `ConsultantPlaces`' clock + S16

- **Goal.** `ConsultantPlaces` reads its one `_clock` for the one-shot budget and the folder stamp (§5.1 table 2); the
  C17 test's git phase and fast copy run under no clock pressure; the slow copy is cut on every run.
- **Files.** `src_mcp/src/Server/Consultation/ConsultantPlaces.cs` (`:115` the parameter; `:287-292` `Stamping` takes
  the clock; `:333-334` as table 2). `src_mcp/tests/AConversationPlaceTests.cs` (`Places()` `:47` gains the clock; the
  test `:113-139`: the hook at `:124-130` becomes a `ClockHandshake` signal-and-wait for the slow root, no
  `Thread.Sleep` and NO `Advance` on the product's thread (§5.4 point 6; plan round §14, finding 0); a `StampedAt` arm is
  optional; a zero-budget arm). Docs (final pass, §9): `research/module_server.md` (the places' clock),
  `module_tests.md` ("E4 S3").
- **Order inside the story.** (1) The RED on today's code: a launcher stand-in (shape: `SlowShow`) that delays every
  `git` answer by 1.1 s — the phase sentence instead of "1 of 2", every run. (2) The seam alone, whole suite green; the
  S1 scan gains `ConsultantPlaces.cs` — red on `:289`/`:334` before the seam, green after. (3) The test on the manual
  clock — **the handshake, not an advance from the hook:** the stand-in gone (real git, no pressure); the slow root's
  `BeforeCopyOf` hook, on the product's thread, calls `handshake.Signal()` and awaits `WaitForReleaseAsync` (it blocks
  the slow copy, as the sleep did, but on an event); the test, having started `OneShotAsync`, awaits
  `handshake.Reached` under the 30 s guard, calls `clock.Advance(budget + 1 ms)` on ITS OWN thread — the budget timer
  fires there, outside the clock's lock, and cancels the copy's token — and then `handshake.Release()` (also in the
  test's `finally`); the released copy meets its cancelled token and `OutOfBudget` says `1 of 2 copied`;
  `Directory.EnumerateDirectories(tree)` empty; `budget.Held == 0` — the assertions at `:136-138` unchanged, the last
  one being the risk consult's "a cancelled copy releases its reserved bytes" (§15, point 5), already on this test.
  (4) The zero-budget arm `AZeroBudget_IsOutBeforeAnythingIsCopied` (§5.2): `OneShotAsync(folder, [fast],
  TimeSpan.Zero, ct)` on the manual clock, never advanced — the refusal is the phase sentence ("before anything was
  copied"), nothing under `tree`, `budget.Held == 0`. A guard, not a RED: today a scheduled zero timer fires within a
  millisecond and the arm is green by luck; after the seam it is green by the constructor's synchronous cancellation
  (M16), which is the contract point it pins. Whether git is even asked under a pre-cancelled token is
  `RepositoryFacts.AskAsync`'s business — the arm asserts the outcome only.
- **RED (S16).** `Expected place.Refusal "… the budget ran out while confirming 2 candidates with git, before anything was copied …" to contain "1 of 2" because the sentence says how far the copying got.`
- **Break-its.** B9: `:334` `CancelAfter(budget)` put back on the linked source → the hook's advance cancels nothing, the
  slow copy completes, no refusal: `Expected place.Refusal "" to contain "1 of 2"`. B10: the stamp's `DateTime.UtcNow`
  put back at `:289` → the optional `StampedAt` arm (the folder's stamp equals the clock's instant) red.
- **Done when.** S16 red then green; B9 (and B10 if the arm is written) red then green; the zero-budget arm green
  before and after (said as a guard); suite green on the seam alone first; the scan red on `ConsultantPlaces.cs` then
  green; the §10 loop recorded; C17 not seen in the next 20 Windows CI runs (a watch); test count named (+1, the zero
  arm; +1 more if `StampedAt` is written).
- **Roles.** Opus brief, Sonnet code, Opus review; Fable reads the diff against table 2.

### S3 — status: built 2026-10-21 (Sonnet; the spec above governed, deviations listed)

**Built.** `ConsultantPlaces` takes `TimeProvider? clock = null` last (default `TimeProvider.System`); `OneShotAsync` arms the row's budget as
`using var budgetTimer = new CancellationTokenSource(budget, _clock); using var copying = CreateLinkedTokenSource(ct, budgetTimer.Token);` (timer first,
link second — the link is disposed first); `Stamping` takes the clock and stamps `clock.GetUtcNow().UtcDateTime`. `ReviewSnapshots.BuildOnceAsync`'s
60 s `Stopwatch` is untouched (the host safety ceiling). The scan's set gained `ConsultantPlaces.cs` (one line, plus its constant). Tests:
`AConversationPlaceTests` 10 -> 12 (`AZeroBudget_IsOutBeforeAnythingIsCopied_OnAClockNobodyAdvances`, `AConversationFoldersStamp_IsTheClocksInstant`; the C17 test
rewritten); the scan class stays 6 tests (its set grew, its companion still finds all five `BoundedScheduler` sites).

**Order and evidence.**

1. **The seam alone** (constructor parameter + field, nothing reads it): whole `CoaiMcp.Tests` suite, 13 140 tests, 4 failed, 13 016 passed, 120 skipped. The 4: C17 itself
   (`...CopiesThatOutrunTheRowsBudget...`, what this story removes: `0 of 2 copied`, also red alone 1 run of 2), and three that are load failures of a 51-minute run with three
   suites on the machine — `TheServerAnswersInitializeAtOnceTests.Initialize_IsAnswered_WhileTheVaultReadAndTheProbeAreStillRunning` (11 s stdio round-trip timeout),
   `AChildThatExitedBeforeItsStartTimeWasReadTests...Win32Exception` and `RepairRunsInsideTheBudgetTests.ARepairGetsWhatIsLEFTOfTheDeadline_NotAWholeSecondOne` — each re-run alone
   (3, 10, 5 tests): green. Nothing else changed, so the seam alone changes nothing.
2. **The scan** with `ConsultantPlaces.cs` added, before the move: red, naming exactly the two sites — `.CancelAfter(budget)` in `copying.CancelAfter(budget); try {...` and `DateTime.UtcNow` in
   `public void Begin() => ConversationFolder.Begin(folder, store, DateTime.UtcNow, ceiling)`; green after the move.
3. **S16 RED** on the unmoved product (stand-in `SlowGit`: the real launcher with every `git` answer delayed 1.1 s, the 1 s budget): `Expected place.Refusal "copying the granted folders took longer than the row's budget
   (1 s): the budget ran out while reading the granted folders, before anything was copied - ..." to contain "1 of 2" because the sentence says how far the copying got.` Then the product moved and the test
   rewritten to the handshake (no stand-in, real git, no clock pressure): green, 5 of 5 runs and 20 of 20 runs of the class alone.
4. **Break-its** (restore by WRITING the file, then rebuild — the S1 trap): **B9** `CancelAfter(budget)` back on the linked source: S16 red 3 of 3, `Expected place.Refusal "" to contain "1 of 2" because the sentence says
   how far the copying got.` (the budget armed on the system timer; the advance cancels nothing; the slow copy completes); the zero-budget arm stays green under B9 (a scheduled zero timer fires within a millisecond: the guard, as
   the spec said); the scan red on the `CancelAfter` site. **B10** `DateTime.UtcNow` back in `Stamping.Begin`: `AConversationFoldersStamp_IsTheClocksInstant` red — `Expected ... StartedUtc to be <2031-03-04 05:06:07>
   because the stamp reads the places' clock, which no wall time has moved, but found <2026-10-21 17:00:28.685387>` — and the scan red on the `DateTime.UtcNow` site.

**The final run, recorded.** After the move and the new tests, the whole suite: 13 142 tests, 4 failed, 13 018 passed, 120 skipped (1 h 06 m, three suites on the machine). The 4 — none uses `ConsultantPlaces`:
`TheSizingIsConcurrentAndBoundedTests.ASizingThatOutlastsItsDeadline_ReturnsGitFailedScopes_WithinIt`, `AReviewerThatAsksForSourceIsAskedAgainTests.AResolverThatTimesOut_RefusesThatRequest_AndTheTurnGoesOnWithTheRest` and
`...ATurnThatTimesOut_IsOneTerminalFailure_AndTheSlotIsReleased` (C13, S4's), `AFeatureIsReviewedEndToEndTests.TheRoundsDeadline_IsScaledByTheFollowUps_...` (C6, S5's: `codex/FeatureReview: timeout`). Each method re-run alone: green.
A re-run of the whole C13 class also showed a fifth test red once (`AConversationCapOnTheWork_EndsTheConversation_BeforeTheDerivedOneWould`, `EarlierTurns.Count` 0), green alone. All are the cold-start/budget races that S4 and S5 remove.

**Deviations from the spec, each with its reason.**

1. **"0 of 2 copied" CAN occur** (§3 C17 and §6 S16 say it cannot since #887). The unmoved test failed on this machine with exactly `... 0 of 2 copied` (in the full suite and alone): the git phase was
   over and `tally.Copying` was true, but the FAST copy itself had not finished inside 1 s. So the cause is wider than the git phase: any real work before the slow copy shares the 1 s. The S16 RED the stand-in shows is
   the phase sentence the spec names, with one difference: git's FIRST answer is the one cut, so the sentence says "while reading the granted folders", not "while confirming 2 candidates with git" (`Candidates` is 0 when the first ask is cut).
2. **The test's budget is 5 minutes, not 1 s.** With a 1 s budget B9 was not reliably red: the system timer (1 s of wall time) fires during the test and cuts the released copy, so the old shape passes by luck on a slow run
   (seen: B9 green once of two). A budget the system timer cannot reach inside the test's 30 s guard makes the advance the ONLY thing that can cut the copy; the refusal sentence then reads "(300 s)". The arithmetic is unchanged.
3. **The test waits on `Task.WhenAny(meeting.Reached, placing)`** and fails with its own sentence ("the place ended first, so something else times the budget") rather than a `TaskCanceledException` after the 30 s guard — the message
   B9 produced on the first try, replaced because it named nothing.
4. **The released-bytes arm is the S16 test's own last assertion** (`budget.Held == 0`, §15 point 5) plus the same assertion in the zero-budget arm — no separate test. S16 also asserts `clock.ActiveTimers == 0` after the refusal (the
   budget's timer left the clock when it fired).
5. **The stamp arm is written** (the "optional" `StampedAt` arm): it is what B10 breaks. `Places()` in the test gains `TimeProvider? clock = null`.

**What the module docs must say (held for the final docs pass, §9).** `module_server.md`: `ConsultantPlaces` reads one injected clock — the one-shot budget (timer source on the clock, linked second) and the conversation folder's
stamp; `ReviewSnapshots`' 60 s ceiling stays on the system clock by design (a host safety ceiling, no seam); a zero budget is out synchronously. `module_tests.md` ("E4 S3"): the handshake shape of the C17 test (hook signals and waits,
the test advances), the 5-minute budget and why (deviation 2), the scan's set now holds `BoundedScheduler` and `ConsultantPlaces`, and the reds/break-its above.

### S4 — C13: `TurnLoop` + `SourceResolver` clocks + S14

- **Goal.** `TurnLoop`'s cap and per-turn elapsed and `SourceResolver`'s read deadline read their own `_clock`
  (§5.1 tables 3 and 4); `BoundedScheduler` hands its `_clock` to the loop; the three C13 tests pay no process start.
- **Files.** `src_mcp/runners/Reviewers/TurnLoop.cs` (`:27-29` the parameter; `:45`, `:72`, `:74`, `:81`, `:84` as table
  3). `src_mcp/runners/Reviewers/BoundedScheduler.cs:462` (`_clock` passed). `src_mcp/runners/Feature/SourceResolver.cs`
  (`:42` the parameter after `readDeadline`; `:44` `_clock`; `:199-200` as table 4). `src_mcp/tests/AReviewerThatAsksForSourceIsAskedAgainTests.cs`:
  a new in-process stand-in `ClockedLauncher(ManualClock clock, ClockHandshake handshake, TimeSpan turn, int ceiling = 20)`
  beside `TwoDecidedThenReal` (`:443-456`) — **it never advances the clock itself** (§5.4 point 6): on each `RunAsync`
  it records the request's `Timeout`, signals the handshake and waits for the release; the TEST's driver loop does
  `await handshake.Reached; clock.Advance(min(turn, request.Timeout)); handshake.Release();` (a timed-out launch costs
  what the launcher would have let it run, not the whole turn), and the released stand-in answers the scripted
  `FAKECLI_STDOUT` text as exit 0 when `turn <= request.Timeout`, else `TimedOut: true`; after `ceiling` launches it
  answers a non-zero exit naming the count (so a loop with no cap ends red, never hangs). The two turn tests
  (`:462-477`, `:484-497`) use `new BoundedScheduler(clock: clock)` and an executor on the stand-in, no
  `FAKECLI_SLEEP_MS`; `SlowShow` (`:540-545`) waits with `await Task.Delay(delay, clock, ct)` and records in a
  `TaskCompletionSource<bool> TokenCancelled` whether `ct.IsCancellationRequested` was true when the delay threw; the
  resolver test (`:523-537`) builds the resolver with the clock, starts the turn, awaits `clock.WhenTimersAsync(2)`,
  advances 301 ms, awaits the turn. A zero-deadline arm for the resolver. Docs (final pass, §9):
  `research/module_runners.md` (the loop's and the resolver's clocks), `module_tests.md` ("E4 S4").
- **Order inside the story.** (1) The three REDs on today's code (S14 a–c). (2) Both seams alone, whole suite green
  (the three product construction sites of the resolver and `PanelService`'s scheduler untouched); the S1 scan gains
  `TurnLoop.cs` and `SourceResolver.cs` — red on `:45` and `:200` before the seams, green after. (3) The tests moved.
  With exact durations the counts become deterministic — **rechecked against `TurnLoop.cs:74-80` (risk consult, §15
  point 4): a turn is launched whenever ANYTHING is left (`left <= TimeSpan.Zero` is the only no-launch case), bounded
  to what is left (`:135-138`).** Cap test — 400 ms turns, a 700 ms timeout and one follow-up (cap 1.4 s): three
  answered turns (1.2 s), then a FOURTH launch with the 200 ms remainder, which the stand-in answers `TimedOut` →
  `EarlierTurns.Count == 3`, FOUR launches, the outcome `TimedOut` with the fourth launch's usage (not `Usage.None`).
  Work-cap test — 300 ms turns under a 900 ms `ConversationCap`: three answered turns use exactly 900 ms, `left == 0`,
  no fourth launch → `EarlierTurns.Count == 3`, THREE launches, `TimedOut` with `Usage.None` (`:78`). Both counts are
  asserted exactly, and the launch count too; the parent's `BeInRange(1, 3)` becomes `Be(3)` (§12 Q8). (4) The
  resolver's zero arm `AZeroReadDeadline_RefusesTheRead_AsTimedOut_AndTheTurnGoesOn`: `readDeadline: TimeSpan.Zero` on
  the manual clock, never advanced — `Big.cs` "not served … timed out", the turn goes on (§5.2); a guard, green by luck
  today, green by contract after.
- **RED (S14).** (a) `Expected outcome.EarlierTurns not to be empty because the turns that answered before the cap are kept.`; (b) `EarlierTurns.Count` 0 outside `[1, 3]`; (c) `Expected tail … to contain "### src/Cart.cs lines 28-35" because the turn goes on with what was served`.
- **Break-its.** B11: the cap check at `TurnLoop.cs:74-79` removed → (a) ends in the stand-in's ceiling failure:
  `Expected outcome to be of type ReviewerOutcome.TimedOut because a conversation past 700 ms × 2 is ended by the cap, but found ReviewerOutcome.NonZeroExit`. B12: `:45` back on `Stopwatch` (the clock parameter ignored) → the stand-in's
  advances move nothing the loop reads; the cap never strikes → the same ceiling failure. B13: `:200` back on
  `CancelAfter` → the resolver's deadline is real, so (c) could pass by accident; **the resolver arm's proof is therefore
  the risk consult's (§15, point 3), not the first draft's `slow.Cut`** — that observer could never be true: a fired
  timer leaves `ActiveTimers` (M11) and the `CancellationTokenSource` disposes its timer BEFORE its registrations run,
  so "cancelled while the read timer is still counted" is a contradiction. The proof as written: the test awaits
  `clock.WhenTimersAsync(2)` — BOTH the read deadline (300 ms) and `SlowShow`'s delay (5 s) are armed on the clock —
  advances 301 ms (only the deadline is reached), asserts `await slow.TokenCancelled` is `true` (the slow operation's
  token was cancelled, and by the advance, since no real time passed), and only then awaits the turn: `Cart.cs` served.
  Under B13 the read deadline is not on the clock, `WhenTimersAsync(2)` sees only `SlowShow`'s timer, and the 30 s guard
  ends the test — red: `TimeoutException` — "the resolver's deadline is not on the clock". B14:
  `BoundedScheduler.cs:462` without `_clock` → (a) and (b) end in the ceiling failure.
- **Done when.** S14 a–c red then green; B11–B14 each red then green; the resolver's zero arm green as a guard; suite
  green on the seams alone first; the scan red on both files then green; the §10 loop recorded for each of the three;
  C13 not seen in the next 20 CI runs (a watch); test count named (+1, the zero arm); the sibling plan told that S4
  merged (its step 3 may start).
- **Roles.** Opus brief, Sonnet code, Opus review; Fable reads the diff against tables 3 and 4 and the `BoundedScheduler`
  rule (one clock per scheduler, nothing else moved).

### S4 — status: built 2026-10-21 (Sonnet; the spec above governed, deviations listed)

**Built.** Product: `TurnLoop` (`TimeProvider? clock = null` last; the one `Stopwatch` and its five reads are `_clock.GetTimestamp()` / `GetElapsedTime(...)`; `using System.Diagnostics` removed), `BoundedScheduler.cs:462` passes its `_clock`, `SourceResolver` (`clock` after `readDeadline`; `readTimer` declared first, `deadline` linked second). Nothing threaded through `PanelService`/`RoundEngine`; the three resolver construction sites and `PanelService`'s scheduler are untouched. Tests: `AReviewerThatAsksForSourceIsAskedAgainTests` (the in-process `ClockedLauncher` with one `ClockHandshake` per launch and a 20-launch ceiling, a driver loop on the test's thread, `SlowShow` waiting with `Task.Delay(delay, clock, ct)` and a `TokenCancelled` task, the zero-deadline arm); the scan's set gained `TurnLoop.cs` and `SourceResolver.cs` (one line each, plus two path constants).

**Order, with what was seen.**

1. *Seams alone, whole suite on the default clock.* 13140 tests, 13016 passed, 120 skipped, 4 failed — all four are timing cases that the suite-load makes lose (three other coders ran whole suites on the same machine): `AConversationPlaceTests.CopiesThatOutrunTheRowsBudget...` (C17, S3's), `ConsultAntigravityDenialScenarioTests.AHungFollowUp...` (a 11 s wall bound, found 12.3 s), `AFeatureIsReviewedEndToEndTests.TheRoundsDeadline_IsScaledByTheFollowUps...` (C6, S5's) and `AConversationThatOutlivesItsCap...` (C13, S4's own RED subject). Listed, not called flaky: the last three were re-run alone (`--filter-method`) and passed; the first passed alone by method and failed by class (S3's case, untouched by S4). None touches `TurnLoop` or `SourceResolver` except C13, which is the case being fixed.
2. *The scan.* With the two files added and no seam: red on exactly the three sites — `TurnLoop.cs` `var clock = Stopwatch.StartNew()`, `TurnLoop.cs` the `Stopwatch clock` parameter, `SourceResolver.cs` `.CancelAfter(_readDeadline)` — green (6 of 6) with the seams.
3. *REDs on today's code (the tests moved onto the clock, the seams stashed):* (a) `Expected outcome.EarlierTurns not to be empty because the turns that answered before the cap are kept.` (b) `Expected outcome.EarlierTurns.Count to be between 1 and 3 because ... but found 0.` — these were seen on the OLD tests with `FAKECLI_SLEEP_MS` 800 / 950 (the brief's way to force the failure with a real process: every run); (c) `Expected tail ... to contain "### src/Cart.cs lines 28-35" because the turn goes on with what was served.` (old test, `Cart.cs` slowed as well: both reads "timed out: git did not answer within 0.3 s"). Then all three green on the manual clock.
4. *Counts, exact.* Cap test: 4 launches, timeouts `700, 700, 600, 200 ms`, `EarlierTurns.Count == 3` each 400 ms, `TimedOut` with `Usage.Unknown`, the clock moved exactly 1400 ms. Work-cap test: 3 launches, `EarlierTurns.Count == 3`, `TimedOut` with `Usage.None`, clock 900 ms.

**Break-its (each red, restored, rebuilt, the diff re-checked identical to the seams).**

- B11 (`left <= TimeSpan.Zero` made unreachable): the work-cap test red — `Expected launcher.Count to be 3 because nothing was left of the 900 ms for a fourth launch, but found 4.` See deviation 2.
- B12 (`:45` and the `left` read back on `Stopwatch`): both turn tests red on the ceiling — `launcher.Count ... found 21` and `Expected type to be ...TimedOut ... but found ...NonZeroExit`.
- B13 (resolver back on `CancelAfter`): the resolver test red — `TimeoutException: gave up after 30 s waiting for the read deadline AND the slow read's delay to be armed on the clock`.
- B14 (`BoundedScheduler.cs:462` without `_clock`): both turn tests red the same way as B12 (`found 21`, `NonZeroExit`).

**Loop.** `AReviewerThatAsksForSourceIsAskedAgainTests` 20 times in a row: before the fix (seam present, old tests moved back by the REDs' form) the class failed in 13 of 20 runs (C13's cap test 12 times, plus the work-cap test, the resolver test and one unrelated repair-timeout test under the load of three parallel suites); after: 0 of 20.

**Deviations.**

1. **Test count +1** (the zero-deadline arm), as written. The scan's two self-test counts are unchanged (6).
2. **B11 does not turn the cap test red** — the brief and §8 expected both turn tests to. With the check removed, the cap test's fourth launch is bounded to 200 ms and the stand-in ends it `TimedOut` anyway, so the outcome is the same; the work-cap test (exactly 900 ms spent, `left == 0`) is the arm that sees the check, through the launch count. The §8 sentence "(a) ends in the stand-in's ceiling failure" holds for B12 and B14, not B11.
3. **The `ClockedLauncher` driver reads arrivals from a channel**, not one reused handshake: `ClockHandshake` is single-shot (S1 deviation 6), so each launch makes its own and posts it; the driver (the test's thread) waits for the launch, advances `min(turn, timeout)` and releases it. A `ReleaseAll()` in the test's `finally` frees every parked launch.
4. **A bounded `WaitingForAsync` helper** names the thing a guard waited for, so B13 reads as a sentence and not a bare timeout.
5. **The wall-time claim in the resolver test.** The slow read's `TokenCancelled` is asserted `true` BEFORE the turn is awaited, as §15 point 3 says; `clock.GetUtcNow() - Epoch == 301 ms` is asserted at the end: nobody but the test moved the clock.

**What the module docs must say (held for the docs pass of §9).** `module_runners.md`: `TurnLoop` reads one `TimeProvider` (the scheduler's — a loop inside a scheduler on clock X is on clock X); its per-turn timer is the launcher's; a turn is launched whenever anything is left, bounded to what is left, so `timeout × (1 + follow-ups)` of exact turns gives a final launch bounded to the remainder, unless the remainder is exactly zero. `SourceResolver`'s read deadline is its own timer source on its clock, linked after the timer; a zero deadline is refused as timed out before any time passes. `module_tests.md` ("E4 S4"): the `ClockedLauncher` shape (signal, wait, driver advances `min(turn, timeout)`, 20-launch ceiling), the resolver proof (two timers armed, advance through the deadline only, token cancelled, then the outcome), B11–B14 with their results and deviation 2, the scan's set now of three product classes beyond `BoundedScheduler`.

### S5 — C6: the round deadline pinned without wall time + S13 (after S4)

- **Goal.** The end-to-end test stops deciding its outcome on wall time; the subject it stood for — the round's deadline
  scales with the follow-ups and a conversation is not cut by a one-turn budget — is pinned deterministically.
- **Files.** New `src_mcp/tests/RoundDeadlineTests.cs` (pure: `PanelSettings { ReviewerTimeout = 2 s }`, one enabled
  vendor, one role, `Stage.Feature`; `For(followUps: 3)` and `For(followUps: 0)`, the ratio 4 and the exact values as
  `RoundBudget.For` derives them — the test states both numbers; `RoundDeadline.cs:9-30`). A scheduler-level test in
  `AReviewerThatAsksForSourceIsAskedAgainTests.cs` (S4's `ClockedLauncher`, 700 ms turns, `Endless(followUps: 3)`:
  timeout 2 s → `Ok` with `Turns == 4`; `ConversationCap = 2 s` → `TimedOut`, `EarlierTurns.Count == 2`, the stand-in
  launched three times — the third with the 600 ms left, as `TurnLoop.cs:74-80` launches whenever anything is left;
  §15 point 4). `AFeatureIsReviewedEndToEndTests.cs:446-458`: `:451` (`FAKECLI_SLEEP_MS=700`) removed; the remark
  `:441-445` rewritten to say what the test now pins and what it still pays (below); `:455-457` unchanged; **a wiring
  arm** (next bullet). Docs (final pass, §9): `module_tests.md` ("E4 S5"); a one-line note in
  `PLAN_question_consultant_tails.md` §6 that C6 and C13 are closed.
- **The wiring at `RoundEngine.cs:287` — narrowed, and then a seam-free observable found (plan round §14, finding 7).**
  The finding asked S5 to say that `RoundEngine` handing `stage.FollowUps` to `RoundDeadline.For` stays unverified
  (B16's limit) unless a seam-free observable exists. Checked at `d9cdccf0e`: the budget is a local (`:287-289`), never
  written to the round's record or the ledger — but `RoundDeadline.For` WARNS when the whole is shorter than one
  reviewer's own scaled budget (`RoundDeadline.cs:28`: "the round limit of {Limit:0} minute(s) is shorter than one
  reviewer's own {Reviewer:0}"), through the `Serilog.ILogger` the engine was given (`RoundEngine.cs:25`, `:42`), which
  the end-to-end harness already passes (`AFeatureIsReviewedEndToEndTests.cs:244`, `Logger.None` today), and five test
  files already capture log events with a sink (`EndToEndTests.cs`, `GateReportingTests.cs`, …). So the arm
  `TheRoundsDeadline_IsScaledByTheFollowUps_TheEngineHandsThemToTheDerivation`: `RoundTimeout = 1 min` (`PanelSettings.cs:290`,
  an explicit whole), `ReviewerTimeout = 20 min`, three follow-ups, a capturing sink in place of `Logger.None` → the
  round runs as today under its 1-minute cap (a fake answers in seconds) and the captured warning says `… one reviewer's
  own 80` — 20 × (1 + 3), in minutes, the format's `0` decimals keeping it an integer. No seam; the engine's pass-through
  is observed. B16 is thereby a REAL break-it, not a stated limit: `stage.FollowUps` → `0` at `:287` makes the line say
  `20` — red: `Expected warning to contain "own 80" … but found "own 20"`. What stays unverified is nothing of the
  wiring; what the arm does NOT prove is the engine's behaviour under a derived whole (no warning fires then), which the
  pure test covers.
- **What the end-to-end test still pays, said (cadence consult 15f733c7, point 1; §16).** S5 removes the fake's 700 ms
  sleep, but the 2 s reviewer timeout and the REAL round timer stay: `RoundEngine.cs:288` arms `new
  CancellationTokenSource(budget)` on the system clock BEFORE `WarmRemoteRolesAsync` (`:300`), the sha (`:306`), the
  worktree, the diff and the outline — so the test's four real `FakeCli` launches plus that setup must fit 8 s of wall
  time. It is a REAL-PROCESS SMOKE TEST of the wiring (proceed, four prompts, all reviewers answered), separated from
  the two DETERMINISTIC budget tests of this story (the pure test and the scheduler-level test on the clock), and its
  remaining deadline exposure is stated in its remark: on a machine where the setup plus four cold starts exceed 8 s it
  fails with "the round reached its limit" — a kept failure (D5) of it reopens C6 against the SETUP COST on that machine,
  never against the arithmetic, which the deterministic tests hold. No timeout is lengthened for it.
- **RED (S13).** (1) the pure test: red by absence, then its two numbers; (2) the scheduler-level test with the follow-ups
  dropped from the cap (`TurnLoop.cs:43` → `Timeout * 1`): `Expected outcome to be of type ReviewerOutcome.Ok because an 8 s cap holds four 700 ms turns, but found ReviewerOutcome.TimedOut`; (3) today's end-to-end test with
  `FAKECLI_SLEEP_MS=1900`: "not started … the round reached its limit" — the recorded failure, every run; (4) the wiring
  arm: red by absence (no sink today), then green with `own 80`.
- **Break-its.** B15: `RoundDeadline.cs:11` `settings.ReviewerTimeout * 1` → the pure test red (`Expected 8s but found 2s`
  in `RoundBudget`'s terms). B16: `RoundEngine.cs:287` `stage.FollowUps` → `0` → the pure test cannot see it and the
  end-to-end test without sleep still passes (four 0 ms turns fit 2 s) — but the wiring arm is red (`own 20`). §12 Q2's
  "no deterministic observer" is withdrawn; the arm is the observer.
- **Done when.** S13 (1)–(4) red then green; B15 and B16 red then green; the end-to-end test's remark states its
  remaining exposure; its §10 loop recorded (before: the failures with the sleep; after: 0 of 20 — a failure after is a
  reopened C6 against the setup cost, with its message kept); C6 not seen in the next 20 CI runs (a watch); test count +3
  named (the pure test, the scheduler-level test, the wiring arm).
- **Roles.** Opus brief, Sonnet code, Opus review.

### S5 — status: built 2026-10-21 (Sonnet; the spec above governed, deviations listed)

**Built.** Tests only; no product line changed (each break-it was restored and the diff of `src_mcp/runners`, `src_mcp/src` re-checked empty). `RoundDeadlineTests` (new, 1 test): `RoundDeadline.For` with one enabled vendor, one role, `ReviewerTimeout = 2 s`, `Stage.FeatureReview` — `followUps: 3` is exactly `8 s`, `followUps: 0` exactly `2 s`, the ratio `4`. `AReviewerThatAsksForSourceIsAskedAgainTests.AConversationIsCutAtTimeoutTimesItsTurns_NotAtOneTurnsTimeout` (new): two conversations on two manual clocks through S4's `ClockedLauncher`, 700 ms turns, `Endless` — arm one `Ok`, `Turns == 4`, 4 launches, the clock moved exactly 2800 ms; arm two with `ConversationCap = 2 s`: `TimedOut`, `EarlierTurns.Count == 2`, 3 launches with timeouts `2 s, 1.3 s, 600 ms`, the clock moved exactly 2 s. `AFeatureIsReviewedEndToEndTests`: `FAKECLI_SLEEP_MS=700` removed, the remark rewritten (it is now a "real-process SMOKE TEST of the wiring", says what it pins and what it still pays: the 2 s reviewer timeout and the real round timer, armed before setup, so setup plus four cold launches must fit the derived 8 s), and the wiring arm `TheRoundsDeadline_IsScaledByTheFollowUps_TheEngineHandsThemToTheDerivation` (`RoundTimeout = 1 min`, `ReviewerTimeout = 20 min`, three follow-ups, the suite's existing `ListSink` in place of `Logger.None` — no second sink written). `Service(...)` gained two optional parameters, `roundTimeout` and `log`. No timeout was lengthened.

**RED (S13), seen on today's code.**

1. *The pure test:* red by absence (the class did not exist; nothing to run). Its real symptom is B15 below.
2. *The scheduler-level test* with `TurnLoop.cs:48` (the cap, `Timeout * (1 + FollowUps)`) dropped to `Timeout * 1`: `Expected type to be CoaiMcp.Runners.Reviewers.ReviewerOutcome+Ok because an 8 s cap (a 2 s timeout with three follow-ups) holds four 700 ms turns, but found CoaiMcp.Runners.Reviewers.ReviewerOutcome+TimedOut.`
3. *Today's end-to-end test with `FAKECLI_SLEEP_MS=1900`:* red in 20 of 20 runs. Two messages alternate, both from `Expected answer.GetProperty("verdict").GetString() to be "proceed" … "verdict": "revise"`: `"reviewers": "0 of 1 reviewers answered; failed: codex/FeatureReview: timeout"` (8.8 s) and `"… failed: codex/FeatureReview: cancelled; the round reached its 0 minute limit and the reviewers still running were cancelled"` (9.6 s).
4. *The wiring arm:* red by absence (no sink, no `RoundTimeout` parameter), then green; its real symptom is B16.

**Break-its (each red, product restored, rebuilt, green).**

- B15 (`RoundDeadline.cs:11` `ReviewerTimeout * 1`): the pure test red — `Expected 8s because a 2 s reviewer asked again three times may take 2 s x (1 + 3), but found 2s.`; the wiring arm red too (`own 20`).
- B16 (`RoundEngine.cs:287` `stage.FollowUps` → `0`): the wiring arm red — `Expected warnings {"the round limit of 1 minute(s) is shorter than one reviewer's own 20; reviewers may be cancelled"} to have an item matching w.Contains("own 80") because the 20-minute reviewer asked three more times is allowed 20 x (1 + 3) = 80 minutes, which the 1-minute round is shorter than.` The pure test stays green (it cannot see the engine), as the spec said.

**Full suite.** `CoaiMcp.Tests.exe`, Windows Debug, 25 min 41 s on a loaded machine: 13152 total, 13032 passed, 120 skipped, 0 failed (S4 reported 13140: +12 are the other merged stories' tests and S5's +3 among them).

**Loop (§10).** The end-to-end smoke test plus the wiring arm (`--filter-method "*TheRoundsDeadline_IsScaledByTheFollowUps*"`), 20 runs in a row: before (sleep 1900) 20 of 20 failed; after, 0 of 20 failed. The scheduler-level test alone 20 runs, 0 failed; the pure test alone 20 runs, 0 failed. The scan (`EveryBudgetInAClockedClassReadsItsClockTests`) 6 of 6 green: S5 touches no clocked class.

**Deviations.**

1. **`Stage.Feature` is `Stage.FeatureReview`** in the code; the spec's name is shorthand.
2. **`Endless` gained `bool stopsAtTheFollowUps = false`.** The spec's `Endless(followUps: 3)` never stops on its own, so "`Ok` with `Turns == 4`" needed a continuation that stops when its follow-ups are spent (as the feature stage's does); the default keeps S4's two tests unchanged.
3. **Arm two's timeouts are `2 s, 1.3 s, 600 ms`**, not `2 s, 2 s, 600 ms`: each launch is bounded by what is left of the cap (`TurnLoop.Bounded`), so the second is bounded to the 1.3 s left. The third launch's 600 ms and the three launches are as the spec says.
4. **B16 also turns the SMOKE test red on a real machine**, not only the wiring arm. The spec predicted "the end-to-end test without sleep still passes (four 0 ms turns fit 2 s)"; seen: with `stage.FollowUps` → `0` the derived whole is 2 s, and the setup (worktree, diff, outline) plus the cold launches did not fit it on this machine (`"verdict": "revise"`). The wiring arm is still the deterministic observer; the smoke test is simply also sensitive to the whole being 2 s instead of 8 s — wall-time exposure of the kind its remark states.
5. **Test count +3**, as written (one pure test, one scheduler-level test with two arms, one wiring arm).

### S6 — C4: the launcher's clock + S15 (GATED on a kept failure, D5)

Opens only when a failure of `AServerThatNeverAnswers_…` has been kept with its full message. **The gate names its
artifact and its owner (plan round §14, finding 4):** the artifact is the test's FULL failure message — the
FluentAssertions sentence with the `because`, the test's duration, the OS and the run (CI run id and attempt, or the
local whole-suite run's date and the command) — appended to **§3 C4 of THIS document** by whoever sees it: the lead of
the session in which it fails, whether a CI watch, a §10 loop or a whole-suite run beside other work. Until then the
C4 test (`AReaderIsEndedWithItsTreeTests.cs:107-123`) **stays in the suite as a known intermittent — no skip, no
quarantine, no longer timeout**; a run it fails is reported with its message, and that message is the thing that opens
S6. Then: the seam on `ProcessLauncher` as §5.5 (the deadline on the clock; the drain, the probe and the tree watch on
the system clock; the caller/timeout distinction asserted both ways), `startDelayMs` on the `serve` verb, the test
waiting for both pids before it advances; the S1 scan gains `ProcessLauncher.cs` with its teardown sites allowed. Fable
reviews the diff against §5.5; Opus briefs; Sonnet codes. Until then: nothing.

## 9. Build order — and what runs beside what

S1 first and alone (the clock is what S2–S4 inject). Then **S2, S3, S4 in parallel for CODE** — four different product
classes, five different test files, no shared product line (plan round §14, finding 5) — with ONE shared file they all
touch: the S1 scan's set of scanned files, where each story adds its own entry (one line each; a merge of three one-line
additions to one list, resolved by whoever merges last). S5 after S4 (it uses the `TurnLoop` seam and S4's stand-in).
S6 only when its gate opens. The sibling plan's step 3 starts after S4 merges. Each story is its own branch off
`feat/suite-e4-clock` and merges into it for the epic's one code gate (E3's shape); the epic's PR to `main` carries the
test counts of every story.

**The `research/` docs land in ONE final docs pass on the epic branch, not per story** (finding 5): three parallel
stories editing `module_tests.md`, `module_runners.md` and `module_server.md` would conflict on every merge. Each story
PR carries its test counts, its reds and its break-its in its summary and holds its doc paragraphs in the PR body; after
S5 merges, one docs pass writes them in this order: (1) `research/module_tests.md` — the contract section of S1 first
(§5.1 tables, §5.2, §5.3, the teardown rule, the scan and its allow-list), then one section per story in story order
S2, S3, S4, S5; (2) `research/module_runners.md` — the ACP client's clock (S2), the loop's and the resolver's clocks
(S4) and the `BoundedScheduler` one-clock rule; (3) `research/module_server.md` — the places' clock (S3); (4) the
Mermaid diagrams of those three re-generated where a clock appears; (5) the parent's E4 section and `todo/README.md`;
(6) the one-line note in `PLAN_question_consultant_tails.md` §6. The docs pass is the last commit before the epic's
code gate, so the gate reviews code and docs together.

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
  races the ladder (recommended under D3), or fold a 429-ladder test on the manual clock into S4. *(Since the plan
  round, §14 finding 3: whichever way, the S1 scan lists both sites as NAMED exceptions with this reason, so they are
  pinned, not ignored — moving them later means removing them from the allow-list, and the companion keeps the list
  honest.)*
- **Q5 — the sibling test** `AUsagePromptThatNeverAnswers_IsGivenUpOnAtItsBound_AndTheAnswerStands` (`:34-45`) has the
  same wall-clock shape and no case number. S2 moves it with the C5 test (recommended) — say if it should stay.
- **Q6 — the cornerstone ledger.** This document marks E4.S1 `model: fable`; the parent's ledger line (in `## Epics`,
  outside E4's section) and this document's status line must say the SAME thing (plan round §14, finding 6):
  `Cornerstones: 2 of 2 named — the per-run temp folder (E2, built), the clock contract (E4.S1)`. *(The first draft of
  this question proposed `1 of 2 — E4.S1`, leaving E2's slot unmarked because E2 had no stories document; the lead's
  answer below named both, and the parent's line already reads so at `d9cdccf0e`.)*
- **Q7 — `FakeClock` → `FixedClock`** in S1 (three lines, the reuse rule), or leave it.
- **Q8 — stronger counts.** With deterministic durations S4 can pin `EarlierTurns.Count == 3` where the parent has
  `NotBeEmpty` / `BeInRange(1, 3)`. Stronger, never weaker; say if the looser form is wanted for some reason.

### The lead's answers (2026-10-21)

- **Q1 — yes.** Graces that bound a real pipe AFTER the outcome is decided (`AcpClient.cs:233`, the ACP session and transport graces, the launcher's drain) stay on the SYSTEM clock: a manual clock nobody advances would hang a teardown. Contract (1) names this exception explicitly.
- **Q2 — the redesign.** S13 is the scheduler-level manual-clock test plus the pure `RoundDeadlineTests` and the end-to-end test without its sleep; no seam through `PanelService` or `RoundEngine` (D3 stands). B16's limit (the `RoundEngine.cs:287` pass-through has no deterministic observer) is recorded as a limit. *(Superseded in part by the plan round's finding 7, §14: a seam-free observer WAS found — the warning line at `RoundDeadline.cs:28` through the engine's injected logger — so S5 gains the wiring arm and B16 is a real break-it; D3 still stands, nothing is threaded.)*
- **Q3 — yes.** The wall-time bounds at `AcpClientUsageTests.cs:44`/`:57` become clock-side assertions in S2; they are the defect class E4 removes.
- **Q4 — leave it for a RED.** `BoundedScheduler`'s ladder `Stopwatch` and `Task.Delay` beside `_clock` are named, not moved, until a failure is kept (D5).
- **Q5 — yes.** S2 also moves the sibling test `AUsagePromptThatNeverAnswers…` (`:34-45`).
- **Q6 — yes.** The parent's ledger line becomes "Cornerstones: 2 of 2 named — the per-run temp folder (E2, built), the clock contract (E4.S1)".
- **Q7 — yes.** S1 folds `FakeClock` (`CollectRunTests.cs:409`) into `FixedClock`.
- **Q8 — yes, where the manual clock makes it exact.** Pin `EarlierTurns.Count == 3` in S4/S5 when the clock decides the cut; keep the weaker bound only where a real process still races, and say why there.

## 13. Definition of Done

- [x] (S1, done 2026-10-21) `ManualClock` exists as §5.4 specifies (eleven points: callbacks synchronous on the advancing thread in due
      order; `Advance` never from a product thread and a nested or concurrent `Advance` refused; `Dispose()` immediate,
      `DisposeAsync()` the only wait); M1–M17 green with their reds seen; B1–B5, B17–B19 red then green;
      `ClockHandshake` is the one signal-and-wait helper every hook uses; `MovableClock` gone, its nine sites moved;
      E3's `AtomicFile` window tests unchanged and green on it (S1).
- [x] (done 2026-10-21: S1 landed it, S2–S4 added their classes red first) The structural scan `EveryBudgetInAClockedClassReadsItsClockTests` and its companion exist (S1), with
      `BoundedScheduler`'s five sites — the ladder's `Stopwatch` and `Task.Delay` among them, with Q4's reason — as
      named exceptions; each seam story added its class to the scanned set red first, then green.
- [x] Each of `AcpClient`, `ConsultantPlaces`, `TurnLoop`, `SourceResolver` reads its one `_clock` exactly as §5.1's
      "after" column says — the timer source declared first and the link second (§5.2), every "stays" site with its
      remark and `ReviewSnapshots`' `MaxElapsed` named as the unseamed host safety ceiling; nothing threaded through
      `PanelService`/`RoundEngine`; `ProcessLauncher` untouched unless C4's gate opened; `SecuritySources` untouched.
- [x] Each seam landed alone first with the whole suite green on the default clock (§5.3), recorded per story.
- [x] S12, C23's RED, S16, S14 (a–c), S13 (1–4) RED with their §6/§8 messages, then GREEN; every break-it B6–B16 red
      then green (B16 through the wiring arm); the zero-budget arms of S3 and S4 and the caller-first /
      dispose-before-deadline arms of S2 green and said as guards; the resolver arm's proof is the two-timers-then-token
      one, never a count read inside a callback.
- [x] S4's and S5's turn counts rechecked against `TurnLoop.cs:74-80` (a turn is launched whenever anything is left):
      the cap test asserts three earlier turns AND four launches; the work-cap test three and three; the S13
      scheduler-level test two earlier turns and three launches.
- [x] No timeout lengthened; no assertion weakened (the moved wall-time bounds are replaced by clock-side proofs, said
      per test); no test removed or skipped; counts named per story; the end-to-end test's remaining wall-clock
      exposure (the real round timer at `RoundEngine.cs:288` and four real launches) stated in its remark; no product
      hook ever calls `Advance`.
      **Qualifier (C17):** the test passes the row budget as `OneShotAsync`'s INPUT, and it is now 5 minutes where it was 1 s (S3 deviation 2). No product timeout, guard or bound was lengthened, and the test never waits for that budget — the manual clock's advance is the only thing that cuts the copy and the 30 s guard is unchanged; it was raised because at 1 s the system timer could cut the released copy on a slow run and B9 passed by luck. It is a changed test constant that makes the deadline wholly the clock's, not a longer wait masking a race; the plan's wording "no timeout lengthened" is met for every product timeout and stretched for this one input, and said so here.
- [x] After the last manual advance of every S2 arm, the teardown is reached on the system clock with the received
      answer kept and the agent gone; a caller's cancellation never reads as `Deadline`; a cancelled copy's bytes are
      given back (`budget.Held == 0`).
- [ ] The §10 loops recorded for C5, C23, C13, C17, C6 (before and after); the 20-run CI watch started per leg; the
      C4 test kept in the suite as a known intermittent, its next failure's full message appended to §3 C4 by the lead
      who sees it. **Open:** the loops are recorded per story in §8 (S2's "before" is the every-run RED, S3's has no "before" loop), but the 20-run CI watch has not started and C4/S6 stays gated on a kept failure.
- [x] (docs pass, 2026-10-22) The `research/` docs written in ONE final docs pass on the epic branch in §9's order: `module_tests.md` (the
      contract, then one section per story), `module_runners.md` and `module_server.md` (the clocks), the diagrams,
      the parent's E4 section and `todo/README.md`, the tails note.
- [ ] The sibling plan (`PLAN_two_equal_deadlines_race_on_macos.md`) told that S4 merged — not yet: S4 is on the epic branch only, it reaches `main` with the epic's PR.
- [ ] `node .agents/conventions/tools/plan-lifecycle.mjs` clean — **run 2026-10-22: `plan-lifecycle: clean.` (exit 0); the box stays open for the promotion half** — and promotion of this document with the parent's (waits for the code gate and the CI watch).

## 14. The E4 plan round (coai session `67791431`, 2026-10-21, verdict proceed) — 8 findings: 7 accepted, 1 rejected

Every accepted finding was re-read against the code at `d9cdccf0e` before it was folded (`git diff --stat aab069ca5
d9cdccf0e -- src_mcp` is empty, so every line number in this document still holds). Numbers are the round's.

| # | Finding | Decision | Folded as / the reason |
|---|---|---|---|
| 0 | An `Advance` from inside a product hook runs the clock's callbacks inline on the product's thread — re-entrancy (a registration cancelling the copy that is calling the hook) or a deadlock (a callback waiting for a lock the hook holds). S3's slow-root hook and S4's `ClockedLauncher` were both written that way | **Accepted** | §5.4 point 6: `Advance` is the test driver's call, never a product thread's; a hook only SIGNALS readiness and WAITS for a test-controlled release, released in the test's `finally`; `ManualClock` throws on a nested or concurrent `Advance` (M15, B18); callbacks stay synchronous, outside the lock, on the advancing thread, in due order — no thread-pool dispatch (point 5, B19). S3 §8 (3) and S4 §8 (the stand-in's driver loop) rewritten to the handshake; `ClockHandshake` added to S1 (point 11). Verified: `AConversationPlaceTests.cs:124-130`'s hook runs inside `ReviewSnapshots`' copy on the product's thread; `TwoDecidedThenReal.RunAsync` (`:449-455`) is called from `ReviewerExecutor` on the scheduler's thread |
| 1 | §5.1 Table 2 should say what `ReviewSnapshots.BuildOnceAsync`'s `Stopwatch` against `MaxElapsed` IS, not only that it stays | **Accepted** | Table 2's callee row names it explicitly as an UNSEAMED HOST SAFETY CEILING, not a test-controllable budget, with the reason (D6's per-copy ceiling; a seam would land without a RED; a safety ceiling on a manual clock could turn a runaway copy into a hang). Verified: `runners/Snapshots/ReviewSnapshots.cs:171`, `:214`, `:279`; `SnapshotOptions.cs:23` `DefaultMaxElapsed` 60 s |
| 2 | The ledger edit (move or restate the cornerstone ledger beside this document's stories) | **Rejected** | The convention keeps the cornerstone ledger in the parent's `## Epics` line and updates it when a cornerstone gets its document — one line, in a section no other open branch changes, so no branch conflicts on it. This document's status line and Q6 point at that line and say the same words (finding 6); they do not carry a second ledger |
| 3 | S1 gains a STRUCTURAL scan over the product classes for `Stopwatch`, `CancelAfter`, `DateTime.UtcNow`, a bare `WaitAsync(timeout)` and `Task.Delay(` outside named sanctioned sites, multi-line aware, with the companion that it still matches a known sanctioned site; the deferred `BoundedScheduler` ladder `Stopwatch` listed as a named exception with its reason (Q4) | **Accepted** | §8 S1 "The structural scan": `EveryBudgetInAClockedClassReadsItsClockTests`, the shape of `EveryClassThatSetsTheProcessEnvironmentTests` (`:31-48`); the scanned set grows per story (each seam story's structural RED), `BoundedScheduler` in from day one with `:290`, `:365`, `:461`, `:616` (the ladder, Q4's reason) and `:685` allowed; the companion fails on a stale allow-list. Verified the five sites at `d9cdccf0e` |
| 4 | S6/C4: the gate names its artifact and its owner | **Accepted** | §8 S6: the artifact is the full failure message (sentence, duration, OS, run or local command) appended to §3 C4 of this document by the lead of the session that sees it; the C4 test stays in the suite as a known intermittent until then — no skip, no quarantine, no longer timeout. Verified: `AReaderIsEndedWithItsTreeTests.cs:107-123`, `Timeout = 2 s` at `:115`, unchanged |
| 5 | §9: S2/S3/S4 run in parallel for CODE; their `research/` doc edits land in one final docs pass on the epic branch, in a stated order | **Accepted** | §9 second paragraph: the three stories hold their doc paragraphs in their PR bodies; one docs pass after S5, in the order `module_tests.md` (contract, then S2–S5) → `module_runners.md` → `module_server.md` → diagrams → the parent's E4 section and `todo/README.md` → the tails note; the one shared file in code is the scan's set, one line per story |
| 6 | One target ledger line: the status line and Q6 must say the same — `Cornerstones: 2 of 2 named — the per-run temp folder (E2, built), the clock contract (E4.S1)` | **Accepted** | The status line (which said `1 of 2 — E4.S1`) and Q6 (which said `0 of 2` → `1 of 2`) both now carry the one line; the parent's `## Epics` line already read so at `d9cdccf0e` and is unchanged |
| 7 | S5 does not verify that `RoundEngine` passes `stage.FollowUps` into the round deadline: narrow S5's guarantee and the DoD to say the wiring stays unverified (B16), unless a seam-free observable exists | **Accepted — and the observable exists** | Checked: `RoundDeadline.For` warns at `RoundDeadline.cs:28` with one reviewer's SCALED minutes (`{Reviewer:0}`) whenever an explicit `RoundTimeout` (`PanelSettings.cs:290`) is shorter than it; the engine's logger is injected (`RoundEngine.cs:25`, `:42`) and the end-to-end harness passes one (`AFeatureIsReviewedEndToEndTests.cs:244`), as five other test files do with a capturing sink. §8 S5 gains the wiring arm (`RoundTimeout = 1 min`, `ReviewerTimeout = 20 min`, three follow-ups → the warning says `own 80`); B16 becomes a real break-it (`FollowUps` → `0` says `own 20`). Q2's "no deterministic observer" is withdrawn |

**Commands from the round:** none (an empty list; nothing to apply).

## 15. The risk consult for epic 4 (`d4afeda2`, 2026-10-21) — 5 points, each verified against the code, binding on E4

1. **`ITimer.Dispose()` must retire the timer at once, even from inside its own callback; only `DisposeAsync()` waits
   for a running callback.** Verified by the mechanism it names: `CancellationTokenSource` disposes its timer INSIDE the
   timer's callback before running the token's registrations, so a `Dispose()` that waited for the running callback
   would wait for itself — every `new CancellationTokenSource(delay, clock)` of §5.1 would deadlock the first `Advance`
   that reaches a deadline. §5.4 point 7 rewritten; M7's second arm, M13 (a timed source LINKED to a caller's token,
   advanced to its deadline: `Advance` returns, the linked token is cancelled) and M14 (disposal between the timer's
   selection and its callback; `DisposeAsync` on a second thread during a callback) added; B17 breaks it.
2. **Linked-source order, zero budgets, and no "nothing can observe the earlier arming".** §5.2 now states the order —
   the timer source declared first, the linked source second, so the LINK is disposed first — and every "after" cell is
   in it. Zero budgets: `new CancellationTokenSource(TimeSpan.Zero, clock)` cancels SYNCHRONOUSLY, `CancelAfter(0)`
   SCHEDULES; checked for the places and the resolver at `d9cdccf0e` — no product path hands a zero (`AcpClient.cs:104-105`
   refuses `<= 0`; `PanelSettings.IntVar` admits only `> 0`, `PanelSettings.cs:1704-1705`; the resolver's product sites
   take the 30 s default) — so it is a test value only, pinned by M16 and by the zero arms of S3 and S4 as a contract
   point. Caller-first, deadline-first and dispose-before-deadline are M17 on the clock and S2's two arms on the
   product. Table 1's "no `await` lies between, so nothing can observe it" is dropped: a preemption can observe the
   earlier arming, and S2 claims only that the timer decides the end.
3. **S4's `slow.Cut` contradicted M11** — a fired timer leaves `ActiveTimers`, and the source disposes its timer before
   its registrations run, so "the delay's cancellation arrives while the read timer is still counted" can never be
   true. The resolver arm's proof is replaced (§8 S4, B13): both the read deadline and the slow delay armed
   (`WhenTimersAsync(2)`), advance only through the deadline (301 ms of a 5 s delay), assert the slow operation's token
   is cancelled (`SlowShow.TokenCancelled`), then await the outcome; with the resolver reverted to `CancelAfter` the
   second timer never appears on the clock and the guard ends the test — the proof fails, as required. §5.4 point 8
   states the rule for every later test.
4. **`TurnLoop.TurnAsync` launches a turn whenever ANY budget is left** (`TurnLoop.cs:74-80`: `left <= TimeSpan.Zero` is
   the only no-launch case; `Bounded` at `:135-138` trims the request to `left`). Verified by the lead and re-read here.
   Every scenario rechecked: the cap test (400 ms turns, 1.4 s) launches a FOURTH turn with the 200 ms remainder, which
   ends `TimedOut` — three earlier turns, four launches, the fourth's usage on the outcome; the work-cap test (300 ms
   turns, 900 ms) uses exactly 900 ms, `left == 0`, no fourth launch, `Usage.None`; the S13 scheduler-level test (700 ms
   turns, 2 s) launches a third turn with 600 ms, which ends `TimedOut` — two earlier turns, three launches (§6 S13 said
   "the third never launched"; corrected). Launch counts are asserted beside the turn counts, and the stand-in's advance
   is `min(turn, request.Timeout)`.
5. **The teardown classification stands, with the wording fixed.** `AcpSession.WatchExitAsync` (`:260-273`) waits
   `ExitGrace` 300 ms AFTER the agent's exit and BEFORE it breaks the pending requests (`:272`), so a buffered answer
   that lands inside the grace is still handled — it is "a grace that bounds real pipe progress" on the system clock, not
   "after the outcome is decided". §5.1's exception and the Table 1 callee row say so; §5.5 point 2 likewise. Added to
   S2 and S3, asserted on every arm: after the last manual advance an unresponsive ACP peer (the `AwaitCancel` script)
   still reaches a bounded teardown (`GraceWait` + `DrainGrace`, 3.5 s of real time) with the received answer kept and the
   agent gone; a caller's cancellation stays distinguishable from a timeout (`AcpClient.cs:125`, never `Deadline`); a
   cancelled copy releases its reserved bytes (`budget.Held == 0`, `AConversationPlaceTests.cs:138`).

## 16. The cadence consult for epics 4-5 (`15f733c7`, 2026-10-21) — where it lives, and its two E4 items

The consult's four points are folded in the PARENT, under `### E5` ("The cadence consult for epics 4-5 (15f733c7,
2026-10-21)"), because three of them restructure E5. Two things from it land in THIS document:

- **Point 1 — C6's remaining deadline exposure** is stated in §8 S5: the end-to-end test keeps the 2 s reviewer timeout
  and the real round timer (`RoundEngine.cs:288`, armed before setup), so it is a real-process smoke test with a stated
  exposure, separated from S5's two deterministic budget tests. (C15's half of the same point is E5's, in the parent.)
- **Point 3/4 — C23 is E4's, not E5's.** The ACP-cancel case (`AcpClientTests.TheCancelReachesAnAgentThatIsStillListening`,
  `:660-671`, run 39325900821 attempt 1) is C5's mechanism — a 5 s turn deadline that "counts the agent's own start too"
  (its own remark, `:664-665`) — on the class S2 seams; it is moved onto the clock in S2 with no further product change
  (§8 S2). The parent's E5 story list says so and excludes it; E5 then needs no `ManualClock` at all, which is what lets
  E5 start beside E4.
