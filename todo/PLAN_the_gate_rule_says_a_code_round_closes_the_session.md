# PLAN — the shared gate rule says a code round closes the session, and where the second one is

> Status: **plan only, nothing implemented yet, 2026-09-24.** Scope: `common/coai-review-gate.md` in the
> conventions repository (the snippet, v5 → v6), and the pin cascade that carries it to its six
> consumers. Extracted from [PLAN_a_failed_round_can_be_retried.md](../research/PLAN_a_failed_round_can_be_retried.md)
> (its S3c) when that plan shipped.
>
> Related docs: [module_server.md](../research/module_server.md).

## The symptom

Issue #490's other half. The server now has the door — `review_code(again: true)` reopens a finished
code review for new commits, the `Done` refusal names it, and every gate order this server sends ends
with it (`GateCommands.AnotherCodeRound`). But the RULE an agent reads before any of that, the shared
`coai-review-gate.md`, still describes the code stage as "same `resolve` duty, same loop" — a loop that
does not exist for a one-round stage — and never says that a resolved code round closes the session.
An agent that reads only the rule still concludes "one code round, ever", which is exactly how the
#490 worker refused its operator's checkpoint round.

## The change

- Step 5 of the rule: "A resolved code round CLOSES the session. For a checkpoint mid-epic, a final
  round after one, or a retry after a crash: commit the new work and call `review_code` with
  `again: true`. It is refused, saying why, when nothing reviewable was committed since the last code
  round, while that round's findings await `resolve`, or while a person is asked."
- Step 5 also: "`review_code` reviews COMMITTED changes only — commit first; an empty diff is refused,
  never passed."
- Snippet version 5 → 6, and every generated copy (`src_vs_code/src/generated/gateRule`) regenerated.

## Build order

1. The conventions PR (rule text + snippet version), with the conventions repository's own checks.
2. The pin cascade to the six consumers, in the order the cascade script uses; this repository's
   `gate-snippet-check` and the extension's snippet parity move together with the pin.

## Test plan

| Test | RED before |
|---|---|
| conventions: the rule's snapshot / body hash for `coai-review-gate.md` updated deliberately | the hash pin fails on the edit, as intended |
| this repo: `gate-snippet-check.mjs` reports v6 at `.agents/conventions/common/coai-review-gate.md` | reports v5 |
| extension: the generated gate rule carries "again: true" | it does not |

## Definition of Done

- [ ] The rule says a code round closes the session, names `again: true`, and says COMMITTED only.
- [ ] Snippet v6 everywhere; the cascade landed in all six consumers.
- [ ] This plan promoted to `research/`.
