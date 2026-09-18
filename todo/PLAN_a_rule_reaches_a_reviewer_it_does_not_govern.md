# PLAN — a shared rule reaches a reviewer whose document it does not govern

> Status: **plan only, nothing implemented yet, 2026-09-18.** Scope: the rule-selection half of the
> review gate — `.agents/conventions/common/planning-docs.md` and whatever picks the three rules a
> round is shown. No production behaviour of the extension is touched by the investigation itself.
>
> **This is an INVESTIGATION plan, and it deliberately stops short of a fix.** The operator asked for
> it to be written down to be researched later. What follows is the symptom as observed, what has
> been verified, what has NOT, and the cheapest order in which to find out — not a conclusion.
>
> Related: [PLAN_the_rules_a_round_shows_are_drawn_at_random.md](../research/PLAN_the_rules_a_round_shows_are_drawn_at_random.md),
> [PLAN_shared_rules_reach_reviewers.md](../research/PLAN_shared_rules_reach_reviewers.md),
> [module_tests.md](../research/module_tests.md).

## The symptom

A gate reviewer has repeatedly filed a finding about a **plan-promotion procedure** — the `git mv`
from `todo/` to `research/`, the status line, the README table — against documents that **do not
contain one**. Three rounds running, by the observation that prompted this plan.

It has the two marks of a finding worth investigating rather than rejecting one more time:

1. **It is confidently specific.** It names the procedure, cites
   `.agents/conventions/common/planning-docs.md`, and proposes a concrete fix.
2. **It is about something not in the document.** On 2026-09-18 it was raised against a review SCOPE
   — text passed to `review_code` describing what a code change was for, which is not a file, has no
   status line, and is governed by no lifecycle rule. It was rejected with that reason, and the
   rejection was correct, and it will presumably come back.

## What has been VERIFIED

- **`planning-docs.md` is one of the shared rules**, in `.agents/conventions/common/` alongside
  nineteen others.
- **It contains the procedure.** The `git mv`, the `IMPLEMENTED <date>` status line, the two-way link
  fixing, the `todo/README.md` table — all of it.
- **Shared rules reach reviewers.** That is the whole subject of
  [PLAN_shared_rules_reach_reviewers.md](../research/PLAN_shared_rules_reach_reviewers.md).
- **A round is shown THREE rules**, selected rather than shown whole —
  [PLAN_the_rules_a_round_shows_are_drawn_at_random.md](../research/PLAN_the_rules_a_round_shows_are_drawn_at_random.md)
  records that the selection is now *"a pure function … with no cache, no stored state and no random
  source"*, and that **62 KB of the budget goes to three rules before anything else is selected**.

## What has NOT been verified, and must be before anything is built

| claim | why it is not yet established |
|---|---|
| that the reviewer was `local` all three times | the 2026-09-18 occurrence was **gemini**, not `local`. The recollection that named `local` may be wrong, and a plan built on the wrong provider would look for the fault in the wrong place. **Read the rounds log** before assuming. |
| that `planning-docs.md` was actually among the three rules shown in those rounds | this is the whole hypothesis and it is currently an inference from *the finding quotes the rule*. The selection is a pure function, so it can be asked directly. |
| that the finding correlates with the rule being shown | the honest test: rounds where it was shown against rounds where it was not. If the finding appears in both, the rule is not the cause. |
| that this is not simply a reviewer being thorough | a rule a reviewer is HANDED is one it is expected to apply. A finding that a document does not follow a rule it was given is not a malfunction; it may only be a rule given to the wrong document. |

## The hypothesis, stated so it can be refuted

**A reviewer is handed `planning-docs.md` among its three rules, for a round whose document is not a
plan in `todo/` — and dutifully applies it.** The reviewer is behaving correctly; the SELECTION is
what does not know the difference between a plan, a promoted record, a review scope and a
specification.

If this is right, the interesting question is not *"why does this reviewer hallucinate"* but
**"should rule selection depend on what KIND of document a round is about?"** — and that is a design
question with a real cost on both sides:

- **Selecting by kind** makes the three rules more relevant and makes the gate cleverer about its own
  inputs — at the price of a classifier that can be wrong, and of rules never being shown to rounds
  that might have benefited from them unexpectedly.
- **Leaving it** keeps the selection a pure function of the round, which is exactly what the earlier
  plan fought to achieve, and accepts a recurring false finding as the cost.

**It is not obvious which is right**, and that is the honest state of it. A cheap middle exists and
should be measured before either: the rule's own text could say what it governs, so a reviewer handed
it against a review scope has grounds to pass over it rather than to apply it.

## The investigation, in order

1. **Read the rounds log** for the three occurrences. Which provider, which role, which rules were
   shown. The reader is the log page itself — *Show review rounds*, described in
   [module_extension.md](../research/module_extension.md), reading the store through
   `roundsDbRead.ts`; findings are persisted, so this is a query rather than a re-run. (There is no
   `module_rounds.md`; the rounds surface is documented inside `module_extension.md` and
   `module_server.md`.)
2. **Ask the selector directly** which three rules a round of each kind is shown. It is a pure
   function; call it for a plan round, a code round and a document round on the same input.
3. **Correlate.** Does the finding appear only in rounds where `planning-docs.md` was among the three?
   If it appears without it, the hypothesis is dead and the answer is elsewhere — most likely the
   role's own prompt.
4. **Only then** decide between the three options above, and write a plan for the one chosen.

## Test plan for the investigation itself

There is nothing to test until step 4 chooses something. What steps 1–3 produce is a **measurement
table** — provider, role, rules shown, finding present — and that table is the deliverable. It goes
in this document, under a heading dated when it was taken.

## Definition of Done

- [ ] The three occurrences are identified in the rounds log by provider, role and date.
- [ ] The rules shown in each are recorded, from the selector rather than from recollection.
- [ ] The correlation is stated either way, including *"it appears without the rule"* if that is what
      the data says.
- [ ] The design question is answered by the operator, not assumed by whoever runs the investigation.
- [ ] If a fix follows, it is its own plan; this one is promoted with the measurement in it.

## What this plan does NOT do

- It does not change rule selection.
- It does not edit `planning-docs.md`.
- It does not treat the reviewer as faulty. A reviewer given a rule and applying it is a reviewer
  working; if the wrong rule arrived, the fault is upstream of the reviewer.
