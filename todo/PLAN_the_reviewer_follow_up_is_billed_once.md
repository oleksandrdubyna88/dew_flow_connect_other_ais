# PLAN — an antigravity reviewer that was asked again is billed once

> Status: **plan only, nothing implemented yet, 2026-10-02.** Scope: `src_mcp/runners/Reviewers/ReviewerExecutor.cs`
> (the second-launch usage sum), the spending ledger.
>
> Related docs: [module_runners.md](../research/module_runners.md),
> [PLAN_the_consultant_works_on_every_vendor.md](../research/PLAN_the_consultant_works_on_every_vendor.md) (where it was found).

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
2. If confirmed: reuse `ConsultationUsage.OfTwoLaunches(adapter-declares-cumulative, first, second)` in
   the executor's second-launch arm, with `IReviewerRuntime` declaring cumulative usage the way
   `IConsultantRuntime.UsageIsCumulative` does.
3. If refuted: record it and close this plan.

## Test plan

- A RED test with two cumulative usage reports: the ledger row equals the second, not the sum.
- Non-cumulative runtimes unchanged.

## Definition of Done

- [ ] Measured, with the two usage blocks recorded.
- [ ] Fixed with a test that was observed red, or closed as refuted.
