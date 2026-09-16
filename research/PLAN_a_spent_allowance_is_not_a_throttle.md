# PLAN — a spent allowance is not a throttle

> Status: **IMPLEMENTED, 2026-09-16.** Scope: `RateLimit` in
> `src_mcp/runners/Reviewers/ReviewerExecutor.cs` — the vocabulary that decides whether waiting can
> help, and which line of a vendor's output is read as the reason.
>
> Issue: [#165](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/165).
> Related docs: [module_runners.md](../research/module_runners.md).

## The symptom, and it is measured

> *"It thinks for a very long time when the allowance has run out; this should be worked out in a
> couple of seconds, not five minutes."*

Not an impression. From this installation's own logs, `logs/2026-09-09/coai-mcp-15-45-08-59996.log`:

```
[19:02:41 WRN] reviewer codex/Conventions         FAILED after 204.4s: rate limited (after 5 attempts)
[19:02:51 WRN] reviewer codex/Architecture        FAILED after 212.6s: rate limited (after 5 attempts)
[19:02:57 WRN] reviewer codex/SecurityReliability FAILED after 209.9s: rate limited (after 5 attempts)
[19:06:42 WRN] reviewer codex/UxDxPerformance     FAILED after 240.8s: rate limited (after 5 attempts)
[19:06:42 INF] round 1 CodeReview proceed: 8 of 12 reviewers answered … over 456.5s
[19:18:29 WRN] reviewer codex/PlanCritique        FAILED after 213.3s: rate limited (after 5 attempts)
[19:18:29 INF] round 1 PlanReview good_enough: 2 of 3 reviewers answered … over 213.4s
```

**Five attempts each.** The whole ladder — 5 s, 30 s, 60 s, 120 s (`RetryLadder.Default`) — climbed
four times in one code round and once in a plan round, for three and a half to four minutes per
reviewer. The plan round is the starker number: 213 of its 213 seconds were one reviewer waiting, and
it finished with two of three.

And here is what it was waiting for, from the same line:

```
{"type":"error","message":"You've hit your usage limit. Upgrade to Pro (https://chatgpt.com/explore/pro),
 visit https://chatgpt.com/codex/settings/usage to purc…
```

**A limit you fix with money cannot clear while you wait.** No arrangement of the ladder makes that
sentence come out differently two minutes later.

## Why it happens

`RateLimit.Hopeless` was the whole decision, and **before this change** it was two words:

```csharp
// BEFORE this change. What ships now is `Spent.Any(...)` over three observed phrases — see below.
public static bool Hopeless(string reason) =>
    Contains(reason, "daily") || Contains(reason, "exhausted");
```

Those were the right two words for the case they were written for — gemini's *"You have exhausted
your daily quota on this model"*, which the remark above them records as having cost a round 157
seconds instead of 19. The measured codex sentence contained **neither**. So `Hit` classified it as a
rate limit (it says *usage limit*, which is in `Phrases`), `Hopeless` said waiting might help, and
`RunWithLadderAsync` climbed all four rungs.

> Every code excerpt in this document is labelled BEFORE or AFTER, and symbols are named rather than
> line numbers, because a promoted plan is read as a description of the system as it is. Its first
> version quoted the old `Hopeless` in the present tense with a line number that had already moved.
> (CodeRabbit, on the pull request.)

## What separates the two, in the evidence we actually have

| Answer | Vendor | Waiting helps? | What says so |
|---|---|---|---|
| `You have exhausted your daily quota on this model` | gemini | no | *daily*, *exhausted* |
| `You've hit your usage limit. Upgrade to Pro … to purchase…` | codex | **no** | *hit your usage limit* |
| `503 UNAVAILABLE: This model is currently experiencing high demand` | gemini | yes | server load, clears in seconds |
| `429 Too Many Requests` | any | yes | a per-minute throttle |

The distinction is not *how big the number is*, it is **what kind of thing ran out**: a plan's
allowance, which is bought back or waits for a window measured in hours, against a momentary refusal
that clears while you stand there.

So `Hopeless` gains the allowance vocabulary — **and every phrase in it has been observed from a real
vendor answer:**

```csharp
private static readonly string[] Spent = ["daily", "exhausted", "hit your usage limit"];
```

**The first draft of this list had five more words in it, and the plan round was right to take them
away.** They were `weekly limit`, `upgrade`, `purchase`, `resets at` and `try again at` — none
observed, all reasoned from what a vendor *might* say, which is the exact guess this file's own
remarks record being burned by when `429` was matched as a bare substring against a Cloudflare ray
id. Two of them were actively dangerous:

- **`upgrade` and `purchase` as bare words.** *"Rate limit reached; upgrade to the paid plan for
  higher throughput"* is a plausible footer on a genuinely transient 429, and *"please upgrade your
  client library"* is not about limits at all. Either would have made a recoverable refusal
  unretryable. A **negative** test pins that case.
- **`try again at`.** At 15:44:30 a vendor saying *"try again at 15:45"* is thirty seconds away —
  inside the first rung. Classifying every stated clock time as hopeless would refuse to wait out
  exactly the case the ladder exists for. Doing it properly means parsing the time against the
  deadline, which is the open tail below, and no sample of the string has been seen.

What is left is the one sentence that was measured, and the two that were already there.

## The second half: which line is read

**Before this change** `RateLimit.Reason` answered with `lines.FirstOrDefault(Marked)` — the FIRST
line that mentions a limit — and `RunWithLadderAsync` was then given only that line. A vendor that
prints *"Rate limit reached"* on one line and *"Upgrade to Pro"* on another therefore had its terminal
fact read as a transient one, and the ladder ran again.

That is not the measured case (codex puts it all on one line), so it is a hole rather than an
observed defect. Five reviewers asked for the mechanism to be named rather than described, and they
were right — *prefer* is not an implementation. It is:

```csharp
var marked = lines.Where(Marked).ToArray();
return marked.FirstOrDefault(Hopeless) ?? marked.FirstOrDefault() ?? string.Empty;
```

Two passes over the marked lines, the terminal one first. The person is shown the more useful
sentence for the same reason the scheduler reads it.

**Only MARKED lines are considered, and a reviewer asked for every line to be scanned instead.**
Declined, and the reason is the finding directly above: each phrase left in `Spent` is itself matched
by `Phrases` — `quota` covers *daily* and *exhausted* in every observed sample, `usage limit` covers
the third — so a hopeless line that is not marked cannot occur for any string this product has seen.
Scanning unmarked lines is what would let that `upgrade your client library` footer reach the
decision.

## What this does NOT do

- **It does not parse a stated wait and compare it to the deadline.** *"retry after 20 seconds"*
  against *"retry after 3 hours"*, and *"try again at 15:45"* against the clock, are the right general
  rule and the obvious next step — but no sample of any of them has been observed from a vendor this
  product runs. Recorded as the open tail, and it is the reason `try again at` is not in the
  vocabulary.
- **It does not touch the ladder, its jitter, or its settings.** The rungs are right for the case they
  serve.
- **It does not change `Hit`.** A spent allowance is still a rate limit; what changes is whether it is
  waited on.

## Build order

1. **RED**, in `GateReportingTests.cs` beside the two that already pin this vocabulary: the measured
   codex sentence is hopeless; a transient 429 carrying an incidental *upgrade to the paid plan*
   footer is **not**; *try again in 1.2s* is not; and the two existing cases still answer as they did.
2. **RED** for the line choice: an output whose first marked line is transient and whose second says
   the allowance is spent is read as hopeless, and the reason SHOWN names the second.
3. The `Spent` list and the two-pass `Reason`.
4. **RED** at the scheduler, over a real process: a reviewer answered the measured sentence is
   launched **once** — counted in a file by the fake CLI, the way
   `AHopelessLimit_IsNotRetriedAtAll_HoweverLongTheLadder` already counts — **and no progress note
   ever says *rate limited on attempt***, because one launch is not the same as no wait and the
   operator's complaint was about the wait.
5. **Teeth**: remove each new phrase in turn and watch the matching case go red; revert `Reason`'s two
   passes and watch the two-line case go red.
6. Build and run the MTP executable — never `dotnet test`, which aborts here for want of a VSTest host:

   ```bash
   dotnet build dew_flow_connect_other_ais.slnx -c Debug
   ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe
   ```

   then `node .agents/conventions/tools/plan-lifecycle.mjs`, and check its exit code rather than
   skimming its output.

## Test plan

- New cases in `GateReportingTests.cs` (the vocabulary, the negative footer case, the line choice) and
  one in `BoundedSchedulerTests.cs` (the launch count and the absence of a wait).
- Unchanged and must stay green: `ADailyQuota_IsNotRetried_BecauseWaitingCannotClearIt`,
  `ATransientOverload_IsStillWorthOneRetry`, `RetryLadderTests`, `RetryLadderSettingsTests`.
- The whole C# suite, since `RateLimit` is read by the scheduler, the gate's reporting and the round's
  own summary.
- `ScenarioCoverageTests` is keyed by TOOL and this change adds none, so its catalogue does not move.
  The flow is driven end to end where it can be: the scheduler test launches a real process and counts
  the launches in a file.

## Definition of Done

- [x] A RED test observed failing with the measured sentence before the vocabulary existed.
- [x] Every new phrase proved by removing it and watching its case go red.
- [x] A transient 429 with an incidental upgrade footer is still retried — the negative case.
- [x] A reviewer meeting the measured sentence launches once AND announces no wait.
- [x] The whole `CoaiMcp.Tests.exe` suite green, reported by its own output.
- [x] `research/module_runners.md` records the vocabulary and the measurement behind it, and
      `research/module_tests.md` names the flow and what it does not cover.
- [x] Promoted to `research/` with `IMPLEMENTED <date>` and its deviations; both READMEs updated.

## What the plan round changed

Ten of twelve findings taken, and the round's work was almost entirely **subtraction**: five of the
eight phrases in the first draft were guesses, two of them capable of making a recoverable refusal
unretryable, and they are gone. What is left is the one sentence that was measured plus the two that
were already there. It also refused to accept *prefer the hopeless line* as a design; it is written as
code now.

Declined, with reasons recorded in the round: scanning UNMARKED lines for a spent phrase, which is
unnecessary once the vocabulary is only observed phrases and is the very thing the false-positive
findings warn against; and a claim that the plan's filename and status line break the planning
convention, which they do not — `plan-lifecycle.mjs` checks exactly those three things and was run
clean before the round opened.

## Open tail

Parsing a stated wait — *retry after 20 seconds*, *try again at 15:45* — and comparing it against what
is left of the reviewer's deadline. That is the general form of this rule and it would subsume the
vocabulary; it needs a sample of the string from a vendor this product actually runs, which nothing
has produced yet.

## What shipped, and what the code round has yet to say

| File | |
|---|---|
| `ReviewerExecutor.cs` | `Spent` — three observed phrases — and a two-pass `Reason` that prefers the terminal line |
| `GateReportingTests.cs` | the measured sentence; two NEGATIVE cases (a throttle with a sales footer, a throttle naming how long to wait); both halves of the line choice |
| `BoundedSchedulerTests.cs` | one launch over a real process, counted in a file, AND no progress note announcing a wait |
| `research/module_runners.md` | the vocabulary, the measurement behind it, and why five phrases were taken out |

## Evidence

RED first, with both real symptoms: *"Expected RateLimit.Hopeless(SpentAllowance) to be True … but
found False"*, and *"Expected RateLimit.Reason(result) \"429 Too Many Requests\" to contain
\"exhausted your daily quota\""*.

Every guard then proved by breaking it:

| Sabotage | What went red |
|---|---|
| `hit your usage limit` removed from `Spent` | `AnAllowanceThatIsSpent_IsNotWaitedOn` **and** the scheduler's `ASpentAllowance_IsNotWaitedOn_AndNoWaitIsAnnounced` |
| `Reason`'s two passes reverted to one | `WhenAVendorSaysBoth_TheTerminalLineIsTheOneThatDecides` |

Whole C# suite: **2134 tests, 2132 pass, 0 fail, 2 skipped**, run as the MTP executable.

## Deviations from the plan as reviewed

- None of substance. The plan was revised BEFORE any code was written, which is where the round's
  work went: five speculative phrases removed and the `Reason` mechanism written as code rather than
  described.
- One detail the plan did not know and the code found: `Phrases`' own docblock already recorded that
  codex says *"You've hit your usage limit"*. The HIT list knew the sentence and the HOPELESS list did
  not — the two were written at different times from the same evidence, and only one of them was
  updated. That is now said in `module_runners.md`, because it is the shape of the mistake rather
  than the mistake itself.
