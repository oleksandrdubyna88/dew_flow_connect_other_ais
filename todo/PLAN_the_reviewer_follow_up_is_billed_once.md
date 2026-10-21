# PLAN — an antigravity reviewer that was asked again is billed once

> Status: **open; step 1 run 2026-10-21, outcome INCONCLUSIVE: the follow-up path is not reached on agy 1.4.4, and a cumulative
> follow-up report stays unmeasured.** Nothing is implemented. Scope: `src_mcp/runners/Reviewers/ReviewerExecutor.cs`
> (the second-launch usage sum), the spending ledger. The measurement is recorded in
> [RESULTS_the_round_counter_tail.md](../research/RESULTS_the_round_counter_tail.md) (M1).
>
> Related docs: [module_runners.md](../research/module_runners.md),
> [PLAN_the_consultant_works_on_every_vendor.md](../research/PLAN_the_consultant_works_on_every_vendor.md) (where it was found).

## The boundary with PLAN_every_round_is_counted

| Item | Which plan builds it | What the other one's part is |
|---|---|---|
| The SUM of a first launch's and its follow-up's usage | **this plan** | [PLAN_every_round_is_counted.md](../research/PLAN_every_round_is_counted.md) 3.1 carries whatever sum this plan produces onto the server line, and labels the line `attempt: followUp` |
| `attempt`, round id, cached/reasoning on the server line | that plan (3.1, 4.1) | — |
| One ledger line per launch — the follow-up its own line (`attempt: followUp`), each line carrying what ITS launch reported, linked by `turn` | that plan (3.3, landed 2026-10-10: `ReviewerOutcome.Launches`, `ReviewAttempt.Launches`, `LaunchLines.Of`) | if the measurement here shows a follow-up reports CUMULATIVELY, this plan changes the follow-up PART to the difference together with the sum — `LaunchLines.Of` writes the parts only while they add up to the turn's usage exactly, so changing one without the other folds the turn back into one line rather than double-writing it. 3.3 deliberately does not take the difference (its decision 3): it is unmeasured |

Disjoint; either may land first.

| Item | Which plan builds it | What the other one's part is |
|---|---|---|
| Step 2 below (`OfTwoLaunches` behind a reviewer-side `UsageIsCumulative`, the follow-up part changed to the difference together with the sum) | [PLAN_the_round_counter_tail.md](PLAN_the_round_counter_tail.md), **E2.S1 (local) and E2.S3 (Team server)**, when the path fires again (see step 1's outcome) | this plan owns the QUESTION (step 1) and the second defect's description; that plan's E2 builds the answer, so its commits tick the matching boxes here. E2 is **deferred** by the owner's decision of 2026-10-21 until a reviewer follow-up can be elicited again |

## The suspicion (not yet measured)

When a reviewer's shell command is auto-denied, issue #504 continues the SAME antigravity conversation
(`AntigravityRuntime.FollowUp`, `src_mcp/runners/Reviewers/AntigravityRuntime.cs:84`), and the executor adds
the two launches' usage: `first.Usage.Add(second.Usage)` (`ReviewerExecutor.cs:619`, `:625`, `:639`).

antigravity reports usage CUMULATIVELY across a conversation — measured for consultations: turn 1 reported
14 138 input tokens and turn 2 reported 30 843, turn 1 plus turn 2 (`AntigravityConsultant.cs:30-40`). If a
follow-up launch of a reviewer reports the same way, the sum counts the first launch twice, and every
spending figure for a #504 round is too high.

This was found while designing the consultant fix, which handles it for consultations
(`ConsultationUsage.OfTwoLaunches`). The reviewer path was deliberately left alone there.

## A second defect on the same path

`AntigravityStream.WasDenied` (`AntigravityRuntime.cs`) matches the literal `"denied_actions":[{` and the word
`auto-denied` ANYWHERE in the transcript, stdout included — and stdout carries the model's own tool
parameters, so a reviewer that greps for "auto-denied" triggers a follow-up it did not need. The consultant
path reads the same facts precisely since 2026-10-02 (`AntigravityStream.DeniedActions(stdout, stderr)`:
the JSON parsed, the sentence read from stderr only). Move the reviewer onto it here; it was left alone
there so the reviewer stayed byte-identical.

## Build order

1. **Measure**: one denied reviewer launch and its follow-up on the real CLI; record both `usage`
   blocks. Prediction: the second includes the first.
   **Run 2026-10-21 (M1 of [PLAN_the_round_counter_tail.md](PLAN_the_round_counter_tail.md), details in
   [RESULTS_the_round_counter_tail.md](../research/RESULTS_the_round_counter_tail.md)): INCONCLUSIVE.** On agy 1.4.4, model
   `gemini-3.8-flash-low`, the current `coai-reader` agent with the `view_file`-only hook, two launches were SUCCESS, not
   denied, with a non-empty answer (input 94 883 and 357 039, output 4 399 and 16 522); the model says `run_command` is
   unavailable and answers in prose. There is no denial, no empty answer and no follow-up, so the follow-up path this plan
   suspects is **not reached in today's product**, and whether a follow-up reports cumulatively stays **unmeasured** (step 2a
   never started; step 2b was not run, by the owner's decision of 2026-10-21). The 2026-10-02 denials came from agy 1.2.15 with
   another agent configuration. The first attempt (7 launches) was refused before any model call because
   `AntigravityRuntime.DefaultModel` `gemini-3.7-flash-high` is not a model agy 1.4.4 offers; that default is corrected in its
   own pull request.
2. If confirmed: reuse `ConsultationUsage.OfTwoLaunches(adapter-declares-cumulative, first, second)` in
   the executor's second-launch arm, with `IReviewerRuntime` declaring cumulative usage the way
   `IConsultantRuntime.UsageIsCumulative` does. **Not started**; built as E2.S1/E2.S3 of
   [PLAN_the_round_counter_tail.md](PLAN_the_round_counter_tail.md) when the path fires again (an agy version or reviewer
   configuration under which a denial with an empty answer happens again) and step 1 can be re-run.
3. If refuted: record it and close this plan.

## Test plan

- A RED test with two cumulative usage reports: the ledger row equals the second, not the sum.
- Non-cumulative runtimes unchanged.

## Definition of Done

- [ ] Measured, with the two usage blocks recorded. *(Run 2026-10-21: inconclusive, the path is not reached on agy 1.4.4; the
      usage blocks of the two undenied launches are in RESULTS_the_round_counter_tail.md. Stays open until a follow-up is elicited.)*
- [ ] Fixed with a test that was observed red, or closed as refuted.
