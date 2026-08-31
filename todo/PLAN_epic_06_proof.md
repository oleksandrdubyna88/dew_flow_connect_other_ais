# PLAN — epic 06: proof — the loop observed end to end

> Status: **plan only, nothing implemented yet.** Epic 6 of 6 under
> [PLAN_connect_other_ais.md](PLAN_connect_other_ais.md) (its Phase 6). Depends on everything
> before it. This epic produces no feature; it produces **evidence**, and the family's rule applies —
> never report work you have not seen working.

## Goal

Two runs of the whole loop: one repeatable and cheap (fake CLIs, in CI, forever), one real (the
actual vendors, once, recorded). The second exists because the first cannot catch what only a real
model does — and the master plan's history already shows predictions losing to measurements.

---

## Story 6.1 — The scripted end-to-end, in CI

*As CI, I want the full protocol driven over real stdio against fake CLIs with scripted rounds, so
every future change replays the whole story: flawed plan → revisions → gate → code rounds → fixes →
verdict.*

Work: a test harness speaking MCP to the published binary; fake codex/gemini scripted per round
(round 1: 4 majors incl. one duplicate across vendors; round 2: 1 major; code round: findings across
all three roles, one reviewer timing out, one rate-limited); assertions over the final `status` —
counts, dedup, outcomes, verdicts, and the artifact trail.

**Test cases**

| # | Test | Expected |
|---|---|---|
| 1 | `FullLoop_HappyPath_ReachesProceedInTwoPlanRounds` | gate math visible in `status` |
| 2 | `CrossVendorDuplicate_CountedOnce_AttributedToBoth` | dedup proven over the wire |
| 3 | `CodeRound_WithATimeoutAndARateLimit_StillVerdicts` | partial round, named failures, honest verdict |
| 4 | `MaxRoundsExhausted_FiresTheConfiguredOutcome` | one run per `continue`/`human`/`escalate` |
| 5 | `MachineIsClean_AfterTheRun` | no worktrees, no children, no temp files, live checkout untouched |
| 6 | `ArtifactTrail_ReplaysTheWholeStory` | every round's findings + decisions readable afterwards |

## Story 6.2 — The real run, once, recorded

*As the operator, I want one session against real Codex and Gemini on a throwaway feature with a
deliberately flawed plan, so the first honest test of prompts and parsing is ours and not a user's.*

Work: a scratch repo; a plan seeded with three known flaws (a security hole, a missing failure path,
a performance trap); the loop run from a live Claude Code session; the record kept — which flaws
which vendor caught, rounds to gate, parse repairs triggered, wall-clock and spend; findings that
expose weak role prompts fixed in the same task (that is what the run is *for*).

**Test cases** *(observations to record, since the system under test includes the vendors)*

| # | Observation | Pass condition |
|---|---|---|
| 1 | The three seeded flaws | at least two named by at least one vendor — else the role prompts are the finding |
| 2 | Gemini parse path | zero unhandled shapes; any repair attempt logged with its input kept as a new fixture for epic 02 |
| 3 | The gate | reached within max rounds, or the escalation fired exactly as configured |
| 4 | The record | written up and kept with the promoted plan — rounds, catches, misses, cost |
| 5 | Live checkout | untouched throughout, verified by hash as in CI |

## Definition of Done

- [ ] Story 6.1 runs green in CI on every push.
- [ ] Story 6.2's record exists; every new Gemini shape it surfaced is a fixture; prompt fixes it forced are landed.
- [ ] The master plan and all six epics are promoted to `research/` with `IMPLEMENTED <date>` and deviations recorded — the promotion check is part of this epic, not an afterthought.
