# RESULTS — the round counter's open tail

> Measurements for [PLAN_the_round_counter_tail.md](../todo/PLAN_the_round_counter_tail.md). Each section is one measurement
> (M1, M2, M3), recorded with the raw blocks redacted (no email, no token, no local path).

## M1 — the agy reviewer follow-up (2026-10-21)

> Epic E1. Harness: [`scripts/probe-agy-review-follow-up.mjs`](../scripts/probe-agy-review-follow-up.mjs) (paid; it launches the
> real `agy` CLI in the shape `AntigravityRuntime.Build` sends a reviewer). Subject: agy **1.4.4**, the current reviewer build
> (agent `coai-reader`, tool `view_file` only, the `PreToolUse` hook that allows nothing else). Scratch folder: under `%TEMP%`.
> The question: when a reviewer is asked again in the same conversation (`AntigravityRuntime.FollowUp`), does the second launch
> report CUMULATIVE usage (so the sum counts the first launch twice)?
>
> **Verdict: inconclusive.** The follow-up path is not reached on agy 1.4.4 with the current read-only agent, so there was no
> follow-up to measure. Cumulative reporting of a reviewer follow-up stays **unmeasured**.

### Attempt 1 — refused before any model call

| Item | Value |
|---|---|
| Model sent | `gemini-3.7-flash-high` (`AntigravityRuntime.DefaultModel`) |
| Launches | 7, all refused |
| Refusal | `invalid model selection ... not recognized` |
| Tokens spent | 0 |

`agy models` on 1.4.4 lists `gemini-4-argon-{low,medium,high}`, `gemini-3.8-flash-{low,medium,high}`, `claude-opus-5-5-*`,
`claude-sonnet-5-5-*` and `gpt-oss-120b-medium`; `gemini-3.7-flash-high` is not among them. The default is corrected in its
own pull request (the owner chose `gemini-3.8-flash-low`); it is not part of this record.

### Attempt 2 — model `gemini-3.8-flash-low` (the owner's live agy reviewer row)

Two launches, then the probe's own cap (400 000 input tokens by the reports' own sums) stopped it.

| Launch | Wall time | Outcome | Denied | Answer | Input | Output | Reasoning | Cached | `view_file` calls |
|---|---|---|---|---|---|---|---|---|---|
| 1 | 65 s | SUCCESS | no | non-empty | 94 883 | 4 399 | 3 417 | 0 | 6 |
| 2 | 327 s | SUCCESS | no | non-empty | 357 039 (summed over 51 agent steps) | 16 522 | 12 346 | 383 516 | 9 |
| Total | | | | | 451 922 | 20 921 | 15 763 | 383 516 | 15 |

Cached tokens (launch 2) are larger than its input: in agy's report cached is not a subset of input, so the two must not be
subtracted from one another.

### What this shows

- On agy 1.4.4 with the current `view_file`-only agent the model never reaches for a shell. It says `run_command` is unavailable
  and answers in prose. So there is **no denial, no empty answer and no follow-up**: the three conditions that
  `ConcludedAsync` (local) and the server's `FollowUpOf` require before any follow-up never held in either launch.
- The follow-up path that E2.S1 would correct is therefore **not reached in today's product**. The 2026-10-02 denials
  ([RESULTS_agy_consult_follow_up.md](RESULTS_agy_consult_follow_up.md)) came from agy **1.2.15** with another agent
  configuration; they are evidence about that setup, not about this one.
- Step 2a (an elicited denial, three launches in one conversation) never started: no launch gave a denial plus a conversation id
  plus an empty answer. Step 2b (cumulative reporting through an explicit same-conversation continuation, apart from denial
  recovery) was **not run, by decision of the owner on 2026-10-21** (option A: record the result and defer E2).
- **Cost note.** One reviewer launch costs 90 k to 360 k input tokens here (launch 1: 95 k, launch 2: 357 k), not the plan's
  "~60 k per launch". The plan's 12-launch cap and its 400 k input-token cap do not fit together: 400 k holds two launches.

### Consequences

- [PLAN_the_reviewer_follow_up_is_billed_once.md](../todo/PLAN_the_reviewer_follow_up_is_billed_once.md) stays open on its one
  question; its step 1 records this outcome.
- E2 of the plan is **deferred** until the path fires again: an agy version or reviewer configuration under which a denial with
  an empty answer happens again. See the plan's E2 section.
