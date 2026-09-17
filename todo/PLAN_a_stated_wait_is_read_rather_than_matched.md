# PLAN — a stated wait is read rather than matched

> Status: **plan only, nothing implemented yet, 2026-09-17 — BLOCKED. The trigger to unblock it is
> exactly two verbatim vendor samples in the table under *The gate*, and nothing else; no date, no
> decision and no review releases it.** Scope: `src_mcp/runners/Reviewers/ReviewerExecutor.cs` —
> `Spent`, `Hopeless` and `Reason`.
>
> Related docs: [module_runners.md](../research/module_runners.md),
> [PLAN_a_spent_allowance_is_not_a_throttle.md](../research/PLAN_a_spent_allowance_is_not_a_throttle.md)
> — this is that plan's open tail, recorded there under *Open tail*.
>
> **Citations verified at `252813d3`**, each against the symbol named beside it.

## Where this came from

Issue #165 made the scheduler tell a SPENT allowance from a per-minute throttle, because waiting out the
first is pure loss. It ships a three-word vocabulary — `internal static readonly string[] Spent` at
[ReviewerExecutor.cs:158](../src_mcp/runners/Reviewers/ReviewerExecutor.cs#L158) — every word read off a
real vendor answer:

```csharp
internal static readonly string[] Spent = ["daily", "exhausted", "hit your usage limit"];
```

read by `Hopeless` ([ReviewerExecutor.cs:174](../src_mcp/runners/Reviewers/ReviewerExecutor.cs#L174))
over the line `Reason` ([ReviewerExecutor.cs:195](../src_mcp/runners/Reviewers/ReviewerExecutor.cs#L195))
picks.

The measured cost of not having it: a Gemini round took **157 seconds instead of 19**, and on 2026-09-09
four codex reviewers of one code round climbed the whole backoff ladder at 204.4, 212.6, 209.9 and 240.8
seconds because the answer contained neither word the list then knew.

The tail that plan recorded:

> Parsing a stated wait — *retry after 20 seconds*, *try again at 15:45* — and comparing it against what
> is left of the reviewer's deadline. That is the general form of this rule and it would subsume the
> vocabulary.

## Why the general form is better

A vocabulary answers "is this kind of limit hopeless". A stated wait answers the question actually being
asked: **is waiting worth it before this reviewer's deadline**. That subsumes the first — a window
measured in hours is hopeless against a deadline measured in minutes without anyone deciding that
*daily* means hopeless — and it also catches the case the vocabulary cannot: a throttle whose stated
wait is longer than the time left, which today is waited out and lost anyway.

## The boundary with the plan this came from

| Item | Which plan builds it | The other plan's part | Order |
|---|---|---|---|
| The `Spent` vocabulary, `Hopeless`, the two-pass `Reason` | [PLAN_a_spent_allowance_is_not_a_throttle.md](../research/PLAN_a_spent_allowance_is_not_a_throttle.md) | this plan keeps all three | shipped first |
| Reading a STATED wait and comparing it to the remaining deadline | **this plan** | recorded it as an open tail | blocked, see below |
| Deciding an answer that states no wait at all | the parent's vocabulary, unchanged | this plan falls back to it | frozen |

**Disjoint**: the vocabulary is not replaced by this plan and is not deleted by it. It becomes the
answer for the case where nothing was stated, which is most of them.

## Why this is BLOCKED, and must stay blocked

The plan it came from is explicit, and this is the most valuable thing issue #165 produced:

> **Nothing goes in this list that has not been read off a real vendor answer.** The first draft also had
> *weekly limit*, *upgrade*, *purchase*, *resets at* and *try again at* — all reasoned from what a vendor
> MIGHT say. Two of them were worse than useless: "rate limit reached; upgrade to the paid plan" is a
> sales footer on a throttle that clears, and refusing to wait it out would lose a reviewer that would
> have answered.

`try again at` was in that rejected draft. Writing a parser for a stated wait **without a captured
sample would repeat exactly the mistake that plan's round caught** — and this time the failure mode is
worse, because a mis-parsed duration is a number rather than a match, and a wrong number looks
authoritative.

**So the trigger for this plan is a sample, not a schedule.** It is opened now so the idea is not lost.

## The gate: captured samples

**Nothing below step 0 of the build order may begin until this table has two rows.**

| Vendor | The line, verbatim | Where it was seen | Its timezone, if it states one | Date |
|---|---|---|---|---|
| *(none yet)* | | | | |

**At least two samples from vendors this product actually runs**, verbatim. One sample is a format; two
are a pattern. The timezone column is not decoration — see the next section for why a sample that does
not record it is only half a sample.

## An absolute time is not a duration, and that is where this gets dangerous

A relative wait (*retry after 20 seconds*) is unambiguous. An absolute one (*try again at 15:45*) is
three unknowns wearing one coat, and the code round raised it twice independently:

- **Whose clock?** A vendor stating UTC, read on a host at UTC+3, is three hours out. Read as local it
  can look already elapsed — the scheduler retries at once into the same refusal — or hours away, and
  the reviewer misses its deadline for nothing.
- **Which day?** *15:45* received at 16:00 means tomorrow. Received at 15:30 it means fifteen minutes.
  Nothing in the string says which, and a parser that assumes today silently produces a negative wait.
- **Already past, or never valid?** A stale or unparseable time must be *nothing*, never zero.

So the rules the reader must carry, and each is a named test:

1. **The clock is INJECTED**, never `DateTime.Now` inside the reader. A test that cannot set "now"
   cannot test any of the above.
2. **An absolute time without a stated offset is REFUSED** — it answers *nothing* and the vocabulary
   decides, exactly as today. Guessing the vendor's timezone is the same class of guess that this
   plan's parent forbids for phrases.
3. **A resolved wait that is negative or beyond a sane ceiling is refused**, not clamped.
4. **The sample table records the timezone** each vendor states. If a vendor states none, that IS the
   finding: rule 2 applies to it permanently and it never reaches the parser.

## What would ship, once unblocked

1. A pure reader: line → a duration, or nothing. An absolute time is resolved to a duration **against
   the injected clock** and only when it carries an offset.
2. The comparison against the reviewer's remaining deadline, which is where the decision is actually
   made — not against a constant.
3. `Hopeless` keeps its vocabulary as the fallback for answers that state no wait at all.

## Build order

0. **The gate.** Two verbatim samples in the table above, with their timezones. Nothing below starts
   until this is done; if the samples never arrive, this plan is never built, and that is a correct
   outcome rather than a stalled one.
1. **RED — `ASalesFooterOnAThrottleStatesNoWait`**: *"rate limit reached; upgrade to the paid plan"*
   must read as **nothing**, so the vocabulary decides. This is the parent's own trap and it goes first.
2. **RED — `AnAbsoluteTimeWithNoOffsetIsRefused`** and
   **`AnAbsoluteTimeResolvesAgainstTheInjectedClock`** (including the midnight rollover and a UTC line
   read on a UTC+3 host).
3. The pure reader, with the clock injected. Steps 1–2 go green.
4. **RED — `AStatedWaitLongerThanTheDeadlineDoesNotWait`**, then the deadline comparison.
5. The fallback wiring: no wait stated → `Hopeless` and the vocabulary, byte-identical to today.

## Test plan

- Every captured sample, and the rejected draft's traps by name — `ASalesFooterOnAThrottleStatesNoWait`
  is the one that proves the reader refuses more readily than it guesses.
- The timezone and rollover cases from rule set above: no offset, UTC read off-UTC, 15:45 received at
  16:00, and a resolved time already past.
- A stated wait shorter than the remaining deadline waits; a longer one does not; the boundary is pinned.
- The existing `ReviewerExecutor` suite, unchanged — today's behaviour is the no-wait-stated path.

## Definition of Done

- [ ] **The gate was passed before any code**: the sample table has at least two verbatim lines, each
      with its timezone recorded. Until then this plan is BLOCKED and the checklist below is not started.
- [ ] The clock is injected; no test depends on the wall clock.
- [ ] An absolute time without an offset is refused, and a named test says so.
- [ ] An answer stating no wait behaves exactly as it does today.
- [ ] The sales-footer trap is a named test.
- [ ] The boundary table above is mirrored in
      [PLAN_a_spent_allowance_is_not_a_throttle.md](../research/PLAN_a_spent_allowance_is_not_a_throttle.md).
