# PLAN — a role that was skipped is named where the AI can read it

> Status: **IMPLEMENTED, 2026-09-10** (PRs #182 and #183). Kind: **bug** — the open tail of
> [PLAN_conventions_is_its_own_role.md](PLAN_conventions_is_its_own_role.md).
> Scope as built: `src_mcp/core/Rounds/SessionState.cs`, `src_mcp/src/Server/PanelService.cs`,
> `src_mcp/runners/Reviewers/BoundedScheduler.cs`.
>
> ### What shipped, and the two open questions it answered
>
> `SkippedRole(Role, Reason)` rides on the round's own state as `NotAsked`, so the sentence reaches
> the AI that called the gate rather than only the server's log.
>
> **A skipped role is NOT a failed reviewer, and it does not read like one.** That was the plan's
> first open question. The reason a caller receives is a plain statement — *this repository has no
> written rules to judge against* — with no failure vocabulary anywhere near it.
>
> **Every omitted role carries its OWN reason**, and that is the code round's doing rather than the
> plan's. The first shape mapped one constant over the difference between the scheduled roles and
> the kept ones. It works today and it lies tomorrow: the day a second filter drops a role for some
> other cause, every caller is told *no written rules* with complete confidence. `RolesNotAsked` now
> sits beside `RolesWithRulesInMind`, pairs each dropped role with the rule that dropped it, and is
> still DERIVED from the difference — so what a caller reads and what the round actually ran cannot
> disagree, which a hand-written list could.
>
> **The panel was left alone.** The plan's second open question asked whether *Active rounds* should
> show it too. It does not: the sidebar is present tense and shows what is running, and a role that
> was never asked for has nothing running to show.
>
> Covered end to end by `ConventionsPassTests` — a repository with no rules, the reviewers dropped,
> the reason on the reply, and `RolesNotAsked` empty when there ARE rules.
>
> Related docs: [PLAN_conventions_is_its_own_role.md](../research/PLAN_conventions_is_its_own_role.md)
> — this is its open tail.

## The symptom

A code round in a repository with no written rules drops its **Conventions** reviewers, because a
conventions pass with nothing to judge against would invent a standard. That is right. What is
missing is who hears about it: the server writes a warning to its own log, and the AI that called
the gate is handed a round with fewer reviewers and no sentence saying why.

A round that reviewed less than it was asked to must say so, to the caller and not only to a log
file nobody has open. Otherwise "3 of 4 roles answered" reads as a failure, or worse, is not noticed.

## What already exists to reuse

0.31.5 shipped exactly this shape for a reviewer that could not run — a card that says so with the
reason, a log line, and the reason travelling into what the calling AI receives. This wants the same
treatment for a role dropped before any reviewer was launched, which is a different moment in the
round's life and probably a different place in the code.

## Open questions

- Is a skipped ROLE the same thing to the caller as a reviewer that failed, or does it need its own
  wording? "No written rules, so nothing to check them against" is not a failure and should not read
  like one.
- Does the panel's Active rounds list need to show it, or is the AI's answer enough?

## Definition of Done

- [ ] A round that skips Conventions says so in what it hands back, with the reason.
- [ ] It does not read as a failure.
- [ ] A test covers a repository with no rules end to end.
