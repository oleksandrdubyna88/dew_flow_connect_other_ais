# PLAN — the question consultant's tails

> Status: **plan only, nothing implemented yet.** Scope: `src_mcp/runners` (the shipped consultant's and
> reviewer's confinement, the api runtime), `src_mcp/src/Store` (question-consult pruning), `src_mcp/tests` (one flaky
> test), `shared/runtime-capabilities.json`.
>
> Related docs: [PLAN_question_consultant.md](PLAN_question_consultant.md) §9 (where these were set aside),
> [RESULTS_question_consultant_capabilities.md](../research/RESULTS_question_consultant_capabilities.md) (the
> measurements every item rests on), [module_runners.md](../research/module_runners.md).

## Goal

The question consultant shipped (stories S1–S5 of its plan) with five things deliberately left out, plus one flaky test
found while building it and one gap the live check found (item 7). Each is small, but none can be done well inside that plan. One of them is a security release in its own
right: the deny list the **existing** consultant and reviewer rely on was measured leaking.

## 1. The shipped consultant and reviewer leave their deny lists — a security release

**Symptom (measured, not inferred).** claude 2.1.258 ignores `--disallowedTools` as a confinement: a canary outside
the working directory was read through PowerShell in 6 of 6 runs. PowerShell is part of the user's tool set, and so are
`Artifact`, `RemoteTrigger` and `SendMessage` (RESULTS §4, the deny-list cells). Only the `--tools` allowlist plus
`--restricted` keeps reads inside the working directories. The question consultant launches through
`ConfinementPlanner` and is therefore unaffected. The two older launches are not:

- the stuck consultant — `--disallowedTools` built from `Denied`, `src_mcp/runners/Consultation/ClaudeConsultant.cs:44`,
  applied at `:68`;
- the confined reviewer — `WriteTools + ReachTools`, `src_mcp/runners/Reviewers/ClaudeRuntime.cs:55`, chosen at `:61`.

**Change.** Both launches ask `ConfinementPlanner` for their fragments, using the same capability table rows the
question rows use, instead of each carrying its own list. Their argv changes, so this is a server release with its
own notes.

**Tests.** A RED test per launch: the planned argv carries `--tools` / `--restricted` and no `--disallowedTools`. Then
a bench re-run of the deny-list cells against both launches, to show the canary is no longer read (the probes module
re-runs one cell; `bench probes rerun`).

## 2. agy `web`

Blocked today, because agy auto-denies `read_url` in headless mode and has no allowlist flag
(`shared/runtime-capabilities.json`, the `antigravity` rows from `:152`). The only flag that lifts the denial is
`--dangerously-skip-permissions`, which lifts every other denial too, so it is refused. **First step: a question, not
code.** Find out whether agy 1.2.x has a per-tool permission setting usable headless (a settings file, a policy). If
it has none, this item is blocked and the plan says so. If it has one: a bench cell, then a capability row citing it.

## 3. Web search for `api` rows

`ApiConsultant` (`src_mcp/runners/Consultation/ApiConsultant.cs:31`) answers `none` only. A web capability for an API
row depends on the vendor (OpenRouter's `:online` suffix / web plugin; xAI live search) and is unmeasured. Measure
one vendor in the bench before any capability row claims it. A row without a measurement stays blocked in the UI,
as today.

## 4. Uncommitted work for api rows' source turns

An api row asking for a file or symbol is served from git at `HEAD` (`src_mcp/src/Server/Stages/FeatureRefs.cs:42`).
A question asked mid-implementation is usually about work not yet committed, so the model reads the version before
it. Options to cost: serve the working tree through the same secret check, or say in the turn that the read is
`HEAD` and the tree is dirty. Choose with the operator.

## 5. SQLite pruning for `question_consults`

`question_consults` and its rows (`src_mcp/src/Store/QuestionConsultTable.cs:29`, read by
`QuestionConsultLog.cs:56`) grow without bound. The JSON records already have a retention. Mirror that retention
in the table, or cap it by count, using the store's existing pruning seam if one exists. Search for it first (the reuse rule).

## 6. The flaky round-deadline test

`AFeatureIsReviewedEndToEndTests.TheRoundsDeadline_IsScaledByTheFollowUps_SoAConversationIsNotCutByAOneTurnBudget`
(`src_mcp/tests/AFeatureIsReviewedEndToEndTests.cs:424`) fails about 1 run in 3 on a clean `origin/main` as well. It runs
for 12–20 s and reports "not started … the round reached its limit" before the reviewer launches. It is
load-sensitive: the deadline is wall-clock. Make the clock injectable for that path, or derive the assertion from
the deadline the test itself sets, then show it holding over 20 consecutive runs.

Two more failed the same way on 2026-10-03, each once in a full local run and never alone (15 of 15 and 5 of 5).
Both are wall-clock waits that a loaded machine overruns:
- `ConsultationSweeperTests.TheLoop_LapsesAnIdleConsultation_AndKeepsDoingSoAfterASettingsReload`
  (`src_mcp/tests/ConsultLimitsScenarioTests.cs:156`) — failed in 488 ms. Its loop now also sweeps question consults
  and escalations on every 50 ms beat, so read the failure message before assuming it is load alone.
- `AReviewerThatAsksForSourceIsAskedAgainTests.AConversationThatOutlivesItsCap_IsOneTerminalTimeout_WithEveryTurnsUsageKept`
  (`src_mcp/tests/AReviewerThatAsksForSourceIsAskedAgainTests.cs:455`) — a 700 ms per-turn fake CLI. No turn finished
  before the cap.

## 7. An api row's cost

Found by the live check
([RESULTS §7](../research/RESULTS_question_consultant_capabilities.md#7-the-live-product-path-check-s5-2026-10-03)):
the Grok row answered with no `costUsd`, while the claude rows reported theirs. The field is nullable, so nothing is
wrong in the reply, but the cost can be known. OpenRouter returns usage, and the price is in the catalog the panel
already reads. Find where the api runtime drops it, and fill it from the vendor's own figure when one exists, never
from an estimate.

## Build order

1 (security, its own release) → 6 (unblocks a trustworthy suite) → 5 → 7 → 4 → 2 and 3 (each starts with a
measurement and may end blocked).

## Test plan

Every item that changes behaviour starts with a RED test, per `common/testing.md`. Items 1, 2 and 3 also need a bench
cell, re-run on its own, whose result is recorded in RESULTS_question_consultant_capabilities.md and cited from the
capability row.

## Definition of Done

- [ ] Neither shipped launch carries `--disallowedTools`; the deny-list canary cells are re-run and recorded.
- [ ] agy `web` is either measured and allowed, or recorded as blocked, with the reason.
- [ ] api `web` is either measured for one vendor and allowed, or still blocked, with the reason.
- [ ] The operator's choice on uncommitted work is recorded and built.
- [ ] `question_consults` has a retention, tested.
- [ ] The round-deadline test passes 20 runs in a row.
- [ ] An api row reports its cost when the vendor states it.
- [ ] module docs updated; this plan promoted when done.

## Boundary with the model catalog (2026-10-04)

| Item | Here | [PLAN_one_model_catalog.md](PLAN_one_model_catalog.md) |
|---|---|---|
| the API runtime for asking | as written above | adds a consultant on an API key (its E2.3) and the shared availability file; this plan keeps agy web and api web |
