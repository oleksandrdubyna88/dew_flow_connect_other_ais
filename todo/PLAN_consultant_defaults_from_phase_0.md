# PLAN — The consultant's defaults come from phase 0's table, not from a guess

> Status: **plan only — the measurement has not been run.** Owned by the OPERATOR, from 2026-09-14.
> Scope: no code unless the table says so; the two settings it can move are the turn cap and which
> vendor answers which caller kind, both already editable in the *Consultant* section of the panel.
>
> Extracted from [PLAN_consultant.md](../research/PLAN_consultant.md) when that plan was promoted on
> 2026-09-13. Every one of its six stories shipped; this is the part of its Definition of Done that
> only a person running real work can close, and it was always scheduled after the build.
>
> Related docs: [module_server.md](../research/module_server.md) (the tool, the caps),
> [module_extension.md](../research/module_extension.md) (where the defaults are edited),
> [module_core.md](../research/module_core.md) (`StuckFindings` — the other half of the same question).

## The question

The tool exists and works. What nobody has measured is whether asking another vendor actually gets a
stuck agent UNSTUCK — and, if it does, what the defaults should be.

This was never a gate on building the tool, and it is not one now: other people report the pattern
working, and the plan said from the first draft that this table decides the DEFAULTS, not whether the
feature exists. What it decides is whether a turn cap of 4 is generous or mean, and which vendor is
worth its tokens for which caller.

## How it is run

The operator puts this into ONE active repository's `CLAUDE.md` — or, now that story 5 shipped, simply
copies the snippet from the ⋯ menu, which carries the same triggers — and records every incident by
hand. Target 10–15 incidents.

| Column | Values |
|---|---|
| Trigger | test red twice · architectural dead end · contradicting requirements |
| Consultant | vendor / model |
| Outcome | **Unstuck** (a non-obvious cause, solved in the next step) · **Echo chamber** (repeated what the agent had tried) · **Hallucination / misdirection** (a non-existent API, led astray) |

**Go / No-Go:** Unstuck strictly above **60 %** → the hypothesis holds. Below **40 %** → changing the
vendor does not cure the dead end; the problem is how the context is put, and the next work is the
prompt rather than the plumbing. Between the two: more incidents.

## What the table is allowed to change

1. **The turn cap.** Ships at 4. `agy` re-sends the whole conversation and is billed for it — 13.9k
   tokens on turn 1, 30.6k on turn 2, no cache (phase 0b) — so if the table shows consultations
   settling in two turns, the default drops to 3 and nobody notices except the quota.
2. **Which vendor answers which caller.** Ships as one row per caller kind in the *Consultant*
   section. A vendor that produces Echo chamber for a caller whose own model it shares is the
   measurable form of the README's thesis, and the row is where that is fixed.
3. **Nothing else.** A result between 40 % and 60 % buys more incidents, not a redesign.

## The other half of the same question

Story 6 shipped `consult_missed`: how often the gate hands back a finding the caller had already
ACCEPTED, counted on every round and said once in the audit, calling nothing. Phase 0 asks whether a
consultation HELPS; that counter asks how often one would have been worth firing automatically. Read
them together — the trigger of phase 2 is worth building only if both say yes, and
[PLAN_consultant.md](../research/PLAN_consultant.md) records what would have to move in the round
machine if they do.

## Definition of Done

- [ ] 10–15 incidents recorded in the table above, with vendor, trigger and outcome.
- [ ] Two live consultations per vendor on a real tree, with timings, tokens, and the filesystem
      invariant's before/after — the last unrun item of the parent plan's DoD.
- [ ] The Unstuck rate is written down here, beside the Go/No-Go it was measured against.
- [ ] The turn cap and the per-caller vendor rows either changed, with the number that moved them, or
      stayed, with the same number saying why.
- [ ] This plan is promoted to `research/` with `IMPLEMENTED <date>`, or CLOSED with the result that
      ended it.

## What this plan still decides, after 2026-09-17

[PLAN_a_finding_that_changes_everything_calls_the_consultant.md](../research/PLAN_a_finding_that_changes_everything_calls_the_consultant.md)
shipped a sixth trigger on 2026-09-17 and **took nothing away from this one**. The boundary:

| Item | Who owns it |
|---|---|
| the turn cap, the calls-per-session cap, which vendor answers which caller kind | **this plan** — the measurement decides them |
| whether an AUTOMATIC consultation ever fires, and on what number | **this plan**. `consult_missed` still measures and calls nothing |
| a PROSE trigger a caller reads and decides on | that plan, shipped |

The distinction is the one `StuckFindings` already states: a machine trigger that fired before
anybody had read the number would be the same guess with a cost attached. What shipped on 2026-09-17
is not that machine — it is an instruction, and a caller can decline it with a reason like any other.
