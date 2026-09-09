# PLAN — the shim scenario's wait is tuned to the fastest runner

> Status: **IMPLEMENTED, 2026-09-09.** Scope: `src_mcp/tests/RemoteShimScenarioTests.cs`. No
> production code was touched — this is a test fixing a test.
>
> Related docs: [module_tests.md](module_tests.md),
> [PLAN_a_release_is_visible_before_it_is_whole.md](../todo/PLAN_a_release_is_visible_before_it_is_whole.md)
> — the release this cost.

## The symptom, measured

`mcp-v0.18.13`, 2026-09-08 17:35 UTC. The `win-arm64` leg of the release matrix failed:

```
failed CoaiMcp.Tests.RemoteShimScenarioTests.AShimKilledMidClaim_LeavesEitherNothingOrAWholeClaim_NeverHalf (30s 359ms)
  Xunit.MicrosoftTestingPlatform.XunitException: System.TimeoutException : the condition was still false after 30s
total: 1091, failed: 1, succeeded: 1090
```

Five of six legs passed the same test. The build before it was `0 Warning(s), 0 Error(s)`, so this
is a WAIT that ran out, not a binary that was wrong — and it cost the release a platform: no
`coai-mcp-0.18.13-win-arm64.zip` was ever published, and every Windows ARM install of that version
answers 404.

## What the test is doing, and why 30 s is the wrong number

`RemoteShimScenarioTests.cs:371` launches the REAL `coai-mcp --ask-remote` in its own process and
waits up to 30 seconds for it to create its claim file (or the `.writing` sibling), then kills it
mid-write to prove the claim is whole or absent and never half.

Thirty seconds is generous for a process start — on the runner the test was written on. It is spent
by: a cold .NET start on an ARM runner, a sign-in, an HTTP round trip to a loopback `HttpListener`,
and the first write — immediately after a full Release build of the whole solution, on a machine
whose disk and CPU are already the slowest of the six. The number was chosen once and has never been
measured against the runner it fails on.

**The deeper problem is that the wait is a fixed constant in a scenario about a RACE.** The test
exists to kill the shim at a precise moment; how long the shim needs to reach that moment is a
property of the machine, and a constant cannot be right for six of them.

## The change

Three parts, cheapest first:

1. **Raise the bound and say what it is for.** The wait is not measuring performance — it is waiting
   for a prerequisite before the interesting part begins. A prerequisite deadline should be generous
   (120 s) and should FAIL with the reason: what it was waiting for, how long it waited, and whether
   the process is still alive. The current message names none of the three.
2. **Say whether the child is still running.** `the condition was still false after 30s` and `the
   process died before it got there` are different failures with different cures, and the test
   cannot currently tell them apart — which is why this one has to be diagnosed from a release log.
3. **Separate the phases' budgets**, per the family testing rule: the wait for the process to reach
   the claim, and the assertions after the kill, must not share one token. A slow start currently
   eats the budget of the thing being tested.

## Test plan

This is a test fixing a test, so the honest checks are:

| # | Check | Holds |
|---|---|---|
| 1 | The failing scenario passes on win-arm64 | Verified by a release re-run rather than asserted locally — the machine is the variable |
| 2 | `WaitForAsync`'s failure names what it waited for and whether the child lived | Read the message from a deliberately-failing wait in a unit test |
| 3 | No other test in the file shares a token across phases | A grep-shaped assertion over the file, with a known instance to prove the scan matches |

## Definition of Done

- [x] The prerequisite wait is generous, named, and reports whether the child process is alive.
- [x] A call site that omits the description or the child does not compile.
- [x] The child's stderr is drained, so it cannot block on a pipe nobody reads.
- [x] Tests written, watched fail with the release log's own sentence, and passing; suite green.
- [ ] A re-run of the `win-arm64` leg passes — **the open tail**, and it can only be closed by the
      next `mcp-v*` release, because the machine is the variable.
- [x] `research/module_tests.md` records that a scenario driving a real child process bounds its
      SETUP separately from its assertion.
- [x] The plan completion check ran and the plan is promoted in the same task.

## What shipped differently

**Part 3 was wrong, and the plan round replaced it.** "Separate the phases' budgets" described a
`CancellationToken` this file does not have — the wait is a `Stopwatch` loop and there is no token to
share. What the phases actually needed was for the prerequisite's failure to be distinguishable from
the assertion's, which parts 1 and 2 deliver. Its test — "a grep-shaped assertion that no test shares
a token" — was replaced by something strictly stronger: `WaitForAsync` takes the description and the
child as REQUIRED parameters, so a call site that omits either does not compile. A scan over source
text can match a comment, and this repository has already shipped one test that passed against a
defect for exactly that reason.

**A dead child ends the wait immediately.** The plan only promised to REPORT the child's state at the
deadline. Three reviewers, from three directions, pointed out that a child which crashed five seconds
in still costs the full 120 s and then produces a sentence indistinguishable from a slow machine. The
loop now checks after every poll, reads the condition once more (a child can satisfy it on its way
out), and reports the exit code. `HasExited` and `ExitCode` are guarded: both throw once the handle
is gone, and a test dying with `InvalidOperationException` instead of its own timeout has thrown away
the evidence it exists to produce.

**A latent hang, found while reading the call sites rather than raised by anyone.** Both scenarios set
`RedirectStandardError` and neither drained the pipe. A child that fills a redirected pipe nobody
reads blocks on its next write — for ever, since the parent's next act is to wait for it — and the
symptom is a prerequisite wait running out on one machine and not another, which is the shape of the
failure being fixed. Whether it caused THIS failure cannot be proved after the fact. `StartShim`
drains it, which is also where the new diagnostic gets the child's own words, and its `Dispose` kills
the tree: a wait that throws runs no `finally` of its own, and a leaked `coai-mcp` holding a claim
file is how one failing test makes the next three fail for unrelated reasons.

**The state is reported as an observation.** `The child was still running when that was checked`
rather than `the child is running`, because a process can change state between the check and the
sentence. (codex.)

## What the CODE round changed

**The promotion left its own row behind, and two vendors caught it.** Rebasing onto a moving main
conflicts in `todo/README.md`, and resolving that by keeping BOTH sides restores the row main still
had — so the plan landed in `research/` with its open-work row intact. It had already happened once
undetected: `PLAN_a_progress_note_is_never_a_reason_the_local_half.md` merged to main the same way,
and running the checker here found both.

```
$ git submodule update --init --depth 1 .claude/rules/shared
$ node .claude/rules/shared/tools/plan-lifecycle.mjs
plan-lifecycle: 2 finding(s) — see common/planning-docs.md
  index     todo/README.md still lists PLAN_a_progress_note_is_never_a_reason_the_local_half.md, …
  link      todo/README.md -> PLAN_a_progress_note_is_never_a_reason_the_local_half.md
```

Both rows are gone and it reports `plan-lifecycle: clean.` The CI step exists but the tool lives in
the conventions submodule, which a worktree does not check out — so it says nothing locally unless
asked.

**The optional `limit` was the defect in miniature.** Three findings from codex, and a fourth from
gemini from the opposite side: an optional parameter is exactly how the thirty seconds got there, and
a scenario could pass its own number again with the compiler saying nothing — while a diagnostic test
that omitted it would inherit the two-minute budget and block the suite the day it regressed. There
are two methods now: `WaitForAsync`, which always uses `PrerequisiteWait`, and
`WaitBrieflyForAsync`, whose name says it is for the tests OF the wait.

Four more, each real:

- **The condition is read at least once**, as a `do`, so a budget already spent does not report a
  failure for something nobody looked at. (gemini.)
- **The exit path waits for the stream handlers to finish** before building the message.
  `ErrorDataReceived` is asynchronous, so a child that writes its reason and exits can have that
  reason still queued at the moment the exit is noticed — and the reason is the whole point of
  reporting the exit. `WaitForExit()` with no timeout is documented to wait for them;
  `WaitForExitAsync` does not promise it. (codex.)
- **The stderr buffer is capped at 8 KB of the most recent lines**, kept from the END, because what a
  process said just before it stopped is the part that explains why. (codex.)
- **`RunningShim` is a class, not a record** — it owns a process and a growing buffer, which is a
  stateful service — and its `Dispose` catches `Win32Exception` as well as `InvalidOperationException`,
  because that is what `Kill` throws on Windows. (gemini.) The failure message names the BUDGET as
  well as the elapsed time, so a reader can tell two minutes from a fraction of a second without
  opening the file.

Nineteen findings were rejected with reasons. Seven of them were `local` restating that a rule was
satisfied (*"This is compliant."*, *"It does."*), and four more asked for code that is already there —
`Kill(entireProcessTree: true)`, `BeginErrorReadLine` immediately after `Start`, `HasExited` checked
before `ExitCode`, `Dispose` doing the killing. Two are worth naming because they sounded right: that
stdout is an undrained pipe (it is not redirected at all, so the child inherits the handle and writes
straight through), and that a child exiting instantly loses its stderr to the race —
`AFailedWait_NamesWhatItWaitedForAndWhatTheChildSaid` starts a shim that writes one line and exits in
milliseconds, and asserts the message quotes it.

## Evidence

| reverted | test | failure |
|---|---|---|
| the failure sentence naming none of the three | `AFailedWait_NamesWhatItWaitedForAndWhatTheChildSaid` | `Expected failure "the condition was still false after 0s" to contain "the claim file to appear"` — the release log's own sentence, reproduced |
| the same | `AFailedWait_SaysTheChildWasStillRunning` | `Expected failure "the condition was still false after 0s" to contain "still running"` |

The success path is held by `AWaitReturnsWhenTheConditionComesTrueLate`, which the plan round asked
for: a test that only ever proves the failure path would pass just as well against a wait that gave
up immediately.

Whole suite: **1113 tests, 1112 pass, 0 fail, 1 skipped.** `plan-lifecycle: clean.`
