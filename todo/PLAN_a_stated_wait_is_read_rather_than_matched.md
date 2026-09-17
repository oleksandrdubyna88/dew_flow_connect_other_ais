# PLAN — a stated wait is read rather than matched

> Status: **plan only, nothing implemented yet, 2026-09-17 — and BLOCKED on a sample, deliberately.**
> Scope: `src_mcp/runners/Reviewers/ReviewerExecutor.cs` — `Spent`, `Hopeless` and `Reason`.
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

**So the trigger for this plan is a sample, not a schedule.** It is opened now so the idea is not lost,
and it must not be built until the table below is filled in.

## The gate: captured samples

| Vendor | The line, verbatim | Where it was seen | Date |
|---|---|---|---|
| *(none yet)* | | | |

**At least two samples from vendors this product actually runs**, verbatim, before any parser is
written. One sample is a format; two are a pattern.

## What would ship, once unblocked

1. A pure reader: line → a duration or an absolute time, or nothing. It must answer *nothing* far more
   readily than it guesses, and *nothing* must fall back to today's behaviour exactly.
2. The comparison against the reviewer's remaining deadline, which is where the decision is actually
   made — not against a constant.
3. `Hopeless` keeps its vocabulary as the fallback for answers that state no wait at all.

## Test plan

- The reader against every captured sample, and against the rejected draft's traps — *"rate limit
  reached; upgrade to the paid plan"* must produce **nothing**, and a test must say so by name.
- A stated wait shorter than the remaining deadline waits; a longer one does not; the boundary case is
  pinned.
- The existing `ReviewerExecutor` suite, unchanged — today's behaviour is the no-wait-stated path.

## Definition of Done

- [ ] The sample table has at least two verbatim lines before a parser exists.
- [ ] An answer stating no wait behaves exactly as it does today.
- [ ] The sales-footer trap is a named test.
- [ ] The boundary table above is mirrored in
      [PLAN_a_spent_allowance_is_not_a_throttle.md](../research/PLAN_a_spent_allowance_is_not_a_throttle.md).
