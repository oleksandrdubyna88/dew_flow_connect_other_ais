# PLAN — a role that was skipped is named where the AI can read it

> Status: **plan only, nothing implemented yet.** Scope: `src_mcp/src/Server/PanelService.cs` and
> whatever assembles the answer a round hands back.
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
