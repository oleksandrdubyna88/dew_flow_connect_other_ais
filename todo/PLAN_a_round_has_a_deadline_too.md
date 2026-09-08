# PLAN — a round has a deadline, not only a reviewer

> Status: **plan only, nothing implemented yet.** Scope: one setting in the panel and its manifest,
> one bound in `PanelService.RunStageAsync`, and the sentence a round prints when it hits it.
>
> Related docs: [module_server.md](../research/module_server.md),
> [module_runners.md](../research/module_runners.md),
> [RESULTS_reviewer_input_sizes.md](../research/RESULTS_reviewer_input_sizes.md).

## What the operator asked for

> "значет нужно добавить и настройку макс время на раунд" — *so a max-time-per-round setting is
> needed too.*

Said immediately after reporting that the existing limit had not worked. Half of that report was a
real defect and is fixed: `reviewerTimeoutMinutes` bounded each LAUNCH, and a reviewer makes up to
two, so one could take twice its deadline
(*"the deadline bounds the reviewer, not each launch it happens to make"*, 2026-09-08). This plan
is the other half, and it only makes sense now that the per-reviewer number means what it says.

## The symptom that remains after that fix

A reviewer is bounded. A ROUND is not. What a person watches is the round, and it can legitimately
run for a long time while every reviewer inside it is behaving:

    round wall-clock  ≈  ceil(vendors × roles ÷ maxConcurrency) × reviewerTimeout

At today's defaults — 3 vendors, 4 code roles, `maxConcurrency` 3, `reviewerTimeoutMinutes` 10 —
that is **four waves of ten minutes: forty minutes**, with nothing overrunning. Add the Team
server's own queue, where a job waits behind other people's, and the number a person sees grows
again without anything being wrong.

## The trap this plan exists to avoid

**A round bound whose default is below that arithmetic kills healthy rounds.** It is the obvious
mistake and it would look exactly like a bug in the gate: reviewers cancelled mid-answer, findings
lost, and a verdict nobody can explain. The default must be derived, not chosen:

    default round budget  =  reviewerTimeout × ceil(vendors × roles ÷ maxConcurrency) × safety

with the safety margin stated in the setting's own description, so a person lowering it can see
what they are cutting into.

## Open questions to settle before building

1. **What does hitting it DO?** Three candidates, and they are not equivalent:
   - cancel the outstanding reviewers and gate on what answered (the round still produces a verdict);
   - cancel and refuse (`call_human`);
   - warn and let it run.
   The first matches the existing shape — `ReviewerSummary` already reports "7 of 9 answered;
   failed: …" honestly, and a cancelled reviewer is already a first-class outcome (`Abandoned`).
   **Assumption for the build: cancel and gate on what answered**, because a round that produces
   nothing after forty minutes is worse than one that produces most of its findings.
2. **Does it bound the QUEUE too?** A Team-server job waiting behind other people is not this
   machine's work, but it is the person's wall-clock. Assumption: yes, one clock from the moment
   the round opens — that is the number the panel shows and the number they complained about.
3. **Per stage, or one?** The plan stage is one reviewer per vendor and the code stage is four, so
   one number cannot fit both without being far too generous for the plan. Assumption: derive both
   from the same setting via the arithmetic above, which already accounts for the role count.

## Build order

1. RED: the derived default is at least `reviewerTimeout × waves`, for a range of vendor and
   concurrency counts — the guard against the trap, written before the setting exists.
2. RED: a round whose budget expires cancels its outstanding reviewers and still returns a verdict
   built from the ones that answered.
3. RED: the summary sentence names the deadline as the reason, distinctly from a reviewer failing
   on its own.
4. The setting: `roundTimeoutMinutes` in `settingsShape.ts`, the manifest, and `envBlock`; the
   server reads it in `PanelSettings` beside the other budgets.
5. The bound itself in `RunStageAsync`, as a linked `CancellationTokenSource` — the scheduler
   already treats cancellation as a per-reviewer outcome rather than an exception, which is what
   makes this small.
6. Docs: `module_server.md`, the CHANGELOG, and this plan promoted.

## Definition of Done

- [ ] A round that exceeds its budget cancels what is outstanding and still reports a verdict.
- [ ] The summary says the ROUND ran out, not that a reviewer failed.
- [ ] The default is derived from the reviewer timeout, the role count and the concurrency cap, and
      a test asserts it cannot fall below one full wave.
- [ ] The setting's description says what lowering it cuts into.
- [ ] The panel and the server agree on the default — the precondition `envBlock` needs, and the
      one that was silently broken for a day in September.
