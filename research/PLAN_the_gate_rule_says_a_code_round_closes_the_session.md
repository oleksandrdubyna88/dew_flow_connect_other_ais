# PLAN — the shared gate rule says a code round closes the session, and where the second one is

> Status: **IMPLEMENTED, 2026-09-24.** The rule change is conventions #52, promoted to `release` at
> a847ab7. The cascade landed in the order the README requires:
>
> 1. mcp #37
> 2. sidecar_rust #45
> 3. benchmark #36
> 4. creds_for_devs #148
> 5. connect_other_ais #502
> 6. rag_qln #43, all three pins
>
> **What shipped differently:**
> - **The marker did not move.** The first draft said "v5 → v6", but the marker is frozen. The
>   extension's version triple moved instead: `SNIPPET_BODY_SHA`, `ARTEFACT_VERSION` 11 and the
>   menu title.
> - **The text is shorter than the draft below.** The first version was +1 038 bytes, which is too
>   much for a nearly full rules budget. It shipped at +379 bytes.
> - **The third refusal names `call_human`, not "a person is asked".** This was the one finding
>   accepted from the document gate (6/6 reviewers, `proceed`).
>
> Scope: `common/coai-review-gate.md` in the conventions repository (the text of step 5), and the
> pin cascade that carries it to its six consumers.
> Extracted from [PLAN_a_failed_round_can_be_retried.md](PLAN_a_failed_round_can_be_retried.md)
> (its S3c) when that plan shipped.
>
> Related docs: [module_server.md](module_server.md).

## The symptom

This is the other half of issue #490. The server already has the way through: `review_code(again: true)`
reopens a finished code review for new commits. The `Done` refusal names it, and every gate order this
server sends ends with it (`GateCommands.AnotherCodeRound`).

The RULE an agent reads before any of that is the shared `coai-review-gate.md`. It still describes the
code stage as "same `resolve` duty, same loop" and never says that a code round resolved at `proceed`
closes the session. An agent that reads only the rule concludes "one code round, ever". That is exactly
how the #490 worker refused its operator's checkpoint round.

## The change

Step 5 of the rule gains the following, kept short because the rules byte budget
(`StageRulesTests.TheRotatedTail`) is nearly full:

- Only committed changes are reviewed. An empty diff is refused, never passed.
- A code round resolved at `proceed` CLOSES the session.
- For a checkpoint, a final round, or a retry after a crash: commit, then call `review_code` with
  `again: true`.
- That call is refused, with a reason, when:
  - nothing new is committed;
  - findings await `resolve`;
  - `call_human` awaits a person.

**What does NOT change: the marker.** An earlier draft of this plan said "snippet v5 → v6". That was
wrong. The gate rule's `coai-snippet v5` marker is frozen against the conventions migration baseline:
`SNIPPET_VERSION` is `frozen: true` in `KNOWN_HALVES`, and `snippetVersion.test.ts` says so.

A text change moves these instead:

| What moves | Old | New |
|---|---|---|
| `SNIPPET_BODY_SHA` | `5e630da7220cede4` | `18ba0fdd9ad6aaa5` |
| `ARTEFACT_VERSION` | 10 | 11 |
| `copyClaudeSnippet` title in `package.json` | `(v10)` | `(v11)` |

The other halves' markers do not move, because their rules did not change.

## Build order

1. The conventions PR with the rule text and `research/rule-bodies.json`, passing the conventions
   repository's own checks. Then promote `release`.
2. The pin cascade to the six consumers, in this order:
   1. mcp
   2. sidecar_rust
   3. benchmark
   4. creds_for_devs
   5. connect_other_ais. This repository's version triple moves together with its pin.
   6. rag_qln, last, with all three of its pins in one PR.

## Test plan

| Test | RED before |
|---|---|
| conventions: `rule-bodies.mjs` body hash for `common.coai-review-gate`, updated deliberately | `DRIFT common.coai-review-gate`, as intended |
| extension: `the snippet text and its version numbers move together` | names sha `18ba0fdd9ad6aaa5` and v11 |
| extension: the generated gate rule carries "again: true" | it does not |
| mcp tests: `StageRulesTests` rules budget holds with the longer rule | no RED — a guard, measured green on Windows (CRLF) at +379 bytes; the first draft was +1 038 and was cut before measuring |

## Definition of Done

- [x] The rule says a code round closes the session, names `again: true`, and says COMMITTED only.
- [x] The version triple has moved in this repository; the cascade landed in all six consumers.
- [x] This plan promoted to `research/`.
