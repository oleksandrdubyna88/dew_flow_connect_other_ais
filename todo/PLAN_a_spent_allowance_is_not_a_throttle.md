# PLAN — a spent allowance is not a throttle

> Status: **plan only, nothing implemented yet, 2026-09-16.** Scope: `RateLimit` in
> `src_mcp/runners/Reviewers/ReviewerExecutor.cs` — the vocabulary that decides whether waiting can
> help, and which line of a vendor's output is read as the reason.
>
> Issue: [#165](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/165).
> Related docs: [module_runners.md](../research/module_runners.md).

## The symptom, and it is measured

> «оно сильно долго думает, когда аут фу лимит. нужно определеть это за пару сек, а не 5 мин» —
> *it thinks for a very long time when the allowance has run out; this should be worked out in a
> couple of seconds, not five minutes.*

Not an impression. From this installation's own logs, `logs/2026-09-09/coai-mcp-15-45-08-59996.log`:

```
[19:02:41 WRN] reviewer codex/Conventions        FAILED after 204.4s: rate limited (after 5 attempts)
[19:02:51 WRN] reviewer codex/Architecture       FAILED after 212.6s: rate limited (after 5 attempts)
[19:02:57 WRN] reviewer codex/SecurityReliability FAILED after 209.9s: rate limited (after 5 attempts)
[19:06:42 WRN] reviewer codex/UxDxPerformance    FAILED after 240.8s: rate limited (after 5 attempts)
[19:06:42 INF] round 1 CodeReview proceed: 8 of 12 reviewers answered … over 456.5s
[19:18:29 WRN] reviewer codex/PlanCritique       FAILED after 213.3s: rate limited (after 5 attempts)
[19:18:29 INF] round 1 PlanReview good_enough: 2 of 3 reviewers answered … over 213.4s
```

**Five attempts each.** The whole ladder — 5 s, 30 s, 60 s, 120 s (`RetryLadder.Default`) — climbed
four times in one code round and once in a plan round, for **three and a half to four minutes per
reviewer**. The plan round is the starker number: 213 of its 213 seconds were one reviewer waiting,
and it finished with two of three.

And here is what it was waiting for, from the same line:

```
{"type":"error","message":"You've hit your usage limit. Upgrade to Pro (https://chatgpt.com/explore/pro),
 visit https://chatgpt.com/codex/settings/usage to purc…
```

**A limit you fix with money cannot clear while you wait.** No arrangement of the ladder makes that
sentence come out differently two minutes later.

## Why it happens

`RateLimit.Hopeless` (`ReviewerExecutor.cs:153`) is the whole decision, and it is two words:

```csharp
public static bool Hopeless(string reason) =>
    Contains(reason, "daily") || Contains(reason, "exhausted");
```

Those were the right two words for the case they were written for — gemini's *"You have exhausted
your daily quota on this model"*, which the remark above them records as having cost a round 157
seconds instead of 19. The measured codex sentence contains **neither**. So `Hit` classifies it as a
rate limit (it says *usage limit*, which is in `Phrases`), `Hopeless` says waiting might help, and
`RunWithLadderAsync` climbs all four rungs.

## What separates the two, in the evidence we actually have

| Answer | Vendor | Waiting helps? | What says so |
|---|---|---|---|
| `You have exhausted your daily quota on this model` | gemini | no | *daily*, *exhausted* |
| `You've hit your usage limit. Upgrade to Pro … to purchase…` | codex | **no** | *hit your usage limit*, *upgrade*, *purchase* |
| `503 UNAVAILABLE: This model is currently experiencing high demand` | gemini | yes | server load, clears in seconds |
| `429 Too Many Requests` | any | yes | a per-minute throttle |

The distinction is not *how big the number is*, it is **what kind of thing ran out**: a plan's
allowance, which is bought back or waits for a window measured in hours, against a momentary refusal
that clears while you stand there.

So `Hopeless` gains the allowance vocabulary:

```csharp
private static readonly string[] Spent =
    ["daily", "exhausted", "hit your usage limit", "weekly limit", "upgrade", "purchase", "resets at", "try again at"];
```

**`try again in` is deliberately NOT in that list, and the distinction is the whole point.** *"Please
try again in 1.2s"* is what a per-minute throttle says and is exactly what one rung of the ladder is
for; *"try again at 3:45pm"* names a clock and is further away than the ladder's whole 215 seconds.
One preposition apart, opposite answers — so the test asserts both, beside each other, or the next
person to touch this will collapse them.

## The second half: which line is read

`RateLimit.Reason` (`ReviewerExecutor.cs:161`) answers with `lines.FirstOrDefault(Marked)` — the
FIRST line that mentions a limit — and `Hopeless` is then given only that line
(`BoundedScheduler.cs:423`). A vendor that prints *"Rate limit reached"* on one line and *"Upgrade to
Pro"* on another therefore has its terminal fact read as a transient one, and the ladder runs again.

That is not the measured case (codex puts it all on one line), so it is a hole rather than a
observed defect — and it is one line to close: prefer a marked line that is **hopeless** over one
that merely marks a limit. The person is shown the more useful sentence for the same reason.

## What this does NOT do

- **It does not parse a stated wait and compare it to the deadline.** *"retry after 20 seconds"*
  against *"retry after 3 hours"* is the right general rule and it is the obvious next step — but no
  sample of either has been observed from any vendor this product runs, and a rule written against an
  imagined string is the guess this file's own remarks already record being burned by (`429` matched
  as a bare substring against a Cloudflare ray id). Recorded as the open tail.
- **It does not touch the ladder, its jitter, or its settings.** The rungs are right for the case
  they serve.
- **It does not change `Hit`.** A spent allowance is still a rate limit; what changes is whether it is
  waited on.

## Build order

1. **RED**, in `GateReportingTests.cs` beside the two that already pin this vocabulary: the measured
   codex sentence is hopeless; *try again in 1.2s* is not; *try again at 3:45pm* is; and the two
   existing cases still answer as they did.
2. **RED** for the line choice: an output whose first marked line is transient and whose second says
   the allowance is spent is read as hopeless, and the reason shown names the second.
3. The `Spent` list and the `Reason` preference.
4. **RED** at the scheduler: a reviewer answered the measured sentence is run **once**, not five
   times — `BoundedSchedulerTests.cs:150` already has the shape for a hopeless limit.
5. **Teeth**: remove each new phrase in turn and watch the matching case go red; revert `Reason`'s
   preference and watch the two-line case go red.
6. Build and run the MTP executable — never `dotnet test`, which aborts here for want of a VSTest host:

   ```bash
   dotnet build dew_flow_connect_other_ais.slnx -c Debug
   ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe
   ```

   then `node .agents/conventions/tools/plan-lifecycle.mjs` and check its exit code.

## Test plan

- New cases in `GateReportingTests.cs` (the vocabulary and the line choice) and one in
  `BoundedSchedulerTests.cs` (the attempt count).
- Unchanged and must stay green: `ADailyQuota_IsNotRetried_BecauseWaitingCannotClearIt`,
  `ATransientOverload_IsStillWorthOneRetry`, `RetryLadderTests`, `RetryLadderSettingsTests`.
- The whole C# suite, since `RateLimit` is read by the scheduler, the gate's reporting and the
  round's own summary.

## Definition of Done

- [ ] A RED test observed failing with the measured sentence before the vocabulary existed.
- [ ] Every new phrase proved by removing it and watching its case go red.
- [ ] *try again in* and *try again at* are asserted beside each other, opposite answers.
- [ ] A reviewer meeting the measured sentence launches once.
- [ ] The whole `CoaiMcp.Tests.exe` suite green, reported by its own output.
- [ ] `research/module_runners.md` records the vocabulary and the measurement behind it.
- [ ] Promoted to `research/` with `IMPLEMENTED <date>` and its deviations; both READMEs updated.
