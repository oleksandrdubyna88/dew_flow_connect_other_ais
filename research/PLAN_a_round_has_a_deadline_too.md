# PLAN — a round has a deadline, not only a reviewer

> Status: **IMPLEMENTED, 2026-09-08.** A round is bounded by a deadline derived from its own shape —
> `RoundBudget.For`, floored at one full wave and capped at eight hours — with
> `COAI_ROUND_TIMEOUT_MINUTES` overriding it and zero meaning derive. Reaching it cancels the
> outstanding reviewers and gates on what answered, and the summary says the ROUND ran out.
>
> Related docs: [module_server.md](module_server.md), [module_runners.md](module_runners.md),
> [RESULTS_reviewer_input_sizes.md](RESULTS_reviewer_input_sizes.md).
>
> **Deviations, and this plan gained more from its two gate rounds than any other today:**
>
> 1. **The floor was applied to the wrong thing, and the code round caught it.** The first build
>    floored EVERY budget at one reviewer timeout — including a number a person had typed — so an
>    explicit five minutes silently became ten while the panel warned that reviewers would be cut
>    off. A setting ignored and a warning that lied about the same number. The floor belongs to the
>    DERIVATION, inside `RoundBudget`, and an explicit value is now honoured exactly.
> 2. **Two bounds nobody planned for.** A derivation with no ceiling builds a round nobody is waiting
>    for (an hour per reviewer at a concurrency of one is twenty hours), so it caps at eight. And a
>    `CancellationTokenSource` takes an int of milliseconds, so `COAI_ROUND_TIMEOUT_MINUTES=80000`
>    would have thrown before a reviewer started — a config value crashing the round it was meant to
>    bound. `Expressible` clamps any budget to what a timer can hold.
> 3. **The clock had to start earlier than planned.** It began when the reviewers did, so a round
>    that spent minutes resolving a sha and mounting a worktree could exceed its stated limit by
>    however long its setup took. It starts with the STAGE, and the timer gets what is left.
> 4. **Attribution is read, not inferred.** The timer has its own `CancellationTokenSource` linked
>    with the caller's, because a single linked source would have had to guess between a deadline and
>    a person cancelling at the same moment. Where both fired it resolves toward the person.
> 5. **The panel shows the derivation.** "Zero means derive" was a hidden dependency — raise the
>    reviewer timeout and the round doubles with nothing on screen saying so. It reads `at most`,
>    because the server derives from the reviewers a round ACTUALLY schedules: fewer when a
>    repository wrote no rules down, fewer again when the lenses are dealt.
> 6. **Open question 2 was answered by building it:** the deadline does bound the Team server's
>    queue, because the clock is wall-clock from the moment the stage opens and a job waiting on a
>    server is time the person spends waiting too.

## What the operator asked for

> So a max-time-per-round setting is needed too.

*(translated from the operator's Russian; this repository's documentation is English.)*

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

## Where it goes, verified

| Seam | Where |
|---|---|
| The stage that would carry the bound | `src_mcp/src/Server/PanelService.cs:470` (`RunStageAsync`) |
| Its two callers, plan and code | `PanelService.cs:310` and `PanelService.cs:345` |
| The reviewer budget it must clear | `src_vs_code/src/settingsShape.ts:171` (`reviewerTimeoutMinutes: 10`) |
| The concurrency cap in the arithmetic | `src_vs_code/src/settingsShape.ts:169` (`maxConcurrency: 3`) |
| The setting's own type and reader | `settingsShape.ts:62` and `settingsShape.ts:245` |
| The list a new key must join | `settingsShape.ts:198` |
| The server's side of the budget | `src_mcp/src/Server/PanelSettings.cs` (`RoleGates`, beside the other budgets) |

## What this adds that GROWS, and who retires it

Almost nothing, and saying so is the point of the rule rather than a formality:

| Surface | Size | Who retires it |
|---|---|---|
| One `CancellationTokenSource` per ROUND, linked to the caller's | one object, a few dozen bytes | the `using` that creates it, when the stage returns — the same lifetime the stage already has |
| One integer setting in `settings.json` | one key, written only when it differs from the default | the panel, when the control returns to its default (`envBlock` removes it) |
| Round records already written by `SessionStore` | unchanged — a cancelled round writes the same one row a finished round does | the existing session store and its sweep |

Nothing new is appended, cached or spawned. If that stops being true — for example if a cancelled
round were to keep its partial reviewer answers on disk for diagnosis — that is the thing to name
here before it is written.

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
3. **How does the reason reach the person?** Cancelling the outstanding reviewers makes each of
   them `Abandoned`, and `ReviewerSummaryFactory.Describe` has no case for that — it falls through
   to `"unknown"`, which is the least useful word available for the one thing this feature exists to
   explain. `ReviewerSummary.From` also takes no round-deadline input, so the summary cannot tell
   "the round ran out" from "a reviewer failed on its own". **Assumption: the deadline is passed
   into the summary explicitly** and rendered as its own sentence, rather than inferred from a count
   of abandoned reviewers — inferring it would be wrong the moment a round is cancelled by a person.
   Raised on this plan's code round, before any of it was built, which is the cheapest moment.

4. **Per stage, or one?** The plan stage is one reviewer per vendor and the code stage is four, so
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

- [x] A round that exceeds its budget cancels what is outstanding and still reports a verdict.
- [x] The summary says the ROUND ran out, not that a reviewer failed.
- [x] The default is derived from the reviewer timeout, the role count and the concurrency cap, and
      a test asserts it cannot fall below one full wave — over six shapes, including nonsense inputs.
- [x] The setting's description says what lowering it cuts into, in the help ARTICLE as well as the
      tooltip, because a repository guard requires it there.
- [x] The panel and the server agree on the default: zero on both sides, meaning derive. There is no
      number to keep in step, which is how this one avoids the failure of September's gate defaults.
- [x] Two bounds the plan did not foresee: a ceiling on the derivation, and a clamp to what a
      `CancellationTokenSource` can express.
