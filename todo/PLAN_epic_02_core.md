# PLAN — epic 02: the pure core — findings, sanitizer, counting, rounds

> Status: **plan only, nothing implemented yet.** Epic 2 of 6 under
> [PLAN_connect_other_ais.md](PLAN_connect_other_ais.md) (its Phase 1). Depends on epic 01 (the test
> scaffolding). No process, no network, no filesystem beyond fixtures — everything here is a pure
> function under a unit test, which is the whole point of doing it before any CLI exists.

## Goal

The logic that decides whether a round passes must be trustworthy before anything can produce a
round. Every rule in the master plan's *counting rule* and *round protocol* sections becomes code
here, and every one of them is seen red before it goes green.

---

## Story 2.1 — One `Finding`, from two vendor shapes

*As the round logic, I want every reviewer's output normalised into one `Finding` record, so
counting and dedup never know which vendor produced what.*

Work: the `Finding` record (severity, category, file, line, title, why, fix, providers[]), the
schema JSON shipped as a file (fed to Codex `--output-schema`, pasted into Gemini's prompt), and a
normaliser per vendor shape. Unknown severity/category values are a **named rejection** of that
finding, not a guess and not a crash.

**Test cases**

| # | Test | Expected |
|---|---|---|
| 1 | `CodexShape_Normalises_FieldForField` | fixture → `Finding` with every field carried |
| 2 | `GeminiShape_Normalises_FieldForField` | same guarantee for the other vendor |
| 3 | `UnknownSeverity_RejectsThatFindingByName` | result lists the rejected item + reason; others survive |
| 4 | `MissingFileAndLine_SurvivesAsRepoLevelFinding` | plan-stage findings have no file — must not be dropped |
| 5 | `EmptyFindingsArray_IsAValidCleanReview` | zero findings ≠ failure |
| 6 | `SchemaFile_MatchesTheRecord` | the shipped schema and the C# record agree (round-trip a synthetic max-filled finding) |

## Story 2.2 — The Gemini sanitizer

*As the normaliser, I want the model's JSON extracted from everything Gemini wraps it in, so a
wrapped answer is data, not a "model failure".*

Work: two layers, in order — the `-o json` envelope (the answer is a field inside Gemini's own
object) and the model's habits (fences, preambles). Extraction is the outermost **balanced** `{…}` by
brace counting from the first `{` — first-to-last is explicitly forbidden by the master plan.

**Test cases**

| # | Test | Expected |
|---|---|---|
| 1 | `Envelope_YieldsTheResponseField` | stats and metadata discarded |
| 2 | `CleanJson_PassesThroughUntouched` | idempotent on the easy case |
| 3 | `FencedJson_IsUnwrapped` | ```` ```json … ``` ```` fixture |
| 4 | `PreambleThenFence_IsUnwrapped` | "Here is the review:" + fence |
| 5 | `TrailingProseWithABrace_DoesNotExtendTheExtraction` | the case brace-counting exists for — seen red against a first-to-last implementation |
| 6 | `EmptyResponse_IsANamedParseFailure` | distinct outcome, never an empty findings list |
| 7 | `TwoJsonObjectsInOneAnswer_TakesTheFirstBalancedOne` | deterministic choice, asserted |

## Story 2.3 — Dedup and the counting rule

*As the gate, I want "fewer than N remarks" to mean something, so three verbose reviewers cannot
force a human escalation with nits.*

Work: dedup (same file, lines within ±5, same category, similar title → one finding listing its
providers), then the count: `blocking` + `major` only; a finding rejected-with-reason in an earlier
round and not re-raised with a new argument does not count; gate is `count <= threshold`.

**Test cases**

| # | Test | Expected |
|---|---|---|
| 1 | `SameDefectFromTwoProviders_CountsOnce_ListsBothProviders` | the core dedup |
| 2 | `SameFileDifferentCategory_StaysTwoFindings` | dedup must not over-merge |
| 3 | `LinesSixApart_StaysTwoFindings` | the ±5 boundary, tested at 5 and at 6 |
| 4 | `MinorsAndNits_NeverGate` | 10 nits + 0 majors → pass |
| 5 | `RejectedWithReason_NotReRaised_DoesNotCount` | rule 3 — seen red against a counter without it |
| 6 | `RejectedButReRaisedWithNewArgument_CountsAgain` | the other half of rule 3 |
| 7 | `ThresholdBoundary` | count == threshold passes; threshold+1 revises |

## Story 2.4 — The round state machine and the escalation ladder

*As the server, I want every ordering rule to be a refusal in one pure type, so `review_code` before
a passed plan stage is impossible rather than discouraged.*

Work: session state (opened → plan rounds → plan `proceed` → code rounds → terminal), transitions
only via events; refusals carry the sentence the main AI will see. The ladder: reviewer effort ↑ →
reviewer model ↑ → arbiter model ↑, one step per exhausted stage, `continue | human | escalate`
honoured from config.

**Test cases**

| # | Test | Expected |
|---|---|---|
| 1 | `ReviewCode_BeforePlanProceed_Refuses` | the master plan's central enforcement claim |
| 2 | `Resolve_WithoutAReviewRound_Refuses` | nothing to resolve → said so |
| 3 | `MaxRounds_ReachedWithFindings_YieldsConfiguredOutcome` | each of `continue`/`human`/`escalate`, three tests |
| 4 | `EscalationSteps_FireInLadderOrder` | effort before model before arbiter — seen red against a shuffled ladder |
| 5 | `RoundWithPartialReviewerFailures_StillAdvances_AndSaysSo` | 4 of 6 answered → verdict carries that fact |
| 6 | `ReopeningAnOpenSession_ReturnsTheSameSession` | idempotent `open` for a resumed conversation |

## Definition of Done

- [ ] Every rule named in the master plan's counting and protocol sections has a test here, and each was observed red first.
- [ ] No type in this epic references `Process`, `HttpClient`, or the filesystem (fixtures aside) — verified by an architecture test.
