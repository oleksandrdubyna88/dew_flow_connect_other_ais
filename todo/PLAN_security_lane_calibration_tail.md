# PLAN — the security lane's open tail: calibration, the frozen-PR campaign, and the final feature gate

> Status: **plan only, nothing implemented yet, 2026-10-03.** Extracted from
> [the security lane plan](../research/PLAN_a_security_lane_runs_beside_the_gate.md) when its E1–E4 shipped
> (PR #634, merged `5c9fecd2`). Scope: measurement and qualification only — `src_mcp/tests` (explicit hardware
> harnesses), `research/RESULTS_security_lane_*.md`, and the lane's prompt files if a measured change is adopted.
> No product behaviour change is planned here.
>
> Related docs: [module_security_lane.md](../research/module_security_lane.md),
> [RESULTS_security_lane_local_llm_windows.md](../research/RESULTS_security_lane_local_llm_windows.md),
> [RESULTS_security_lane_qwen_windows.md](../research/RESULTS_security_lane_qwen_windows.md).

## The goal

The lane ships and runs: pairs are configured, routed, bounded, answered, counted and recorded. What is NOT
established is whether a local model's security findings are **correct** — the shipped prompts produced
schema-valid answers, but hand-checking found unsupported impacts and incomplete reproduction preconditions.
Until that is measured, the lane's findings are unverified evidence (the product already says so), and no
model may be described as qualified for it.

## What is open (as recorded at the merge of #634)

1. **E5 positive-finding fidelity.** Qwen 0/3 on the feature preflight. Gemma
   (`Gemma4-26B-A4B-Uncensored_vk128:latest`) 3/3 adequate feature replies and all twelve modules answered,
   but every answer was an empty `SECURE`; on the positive fixture the AuthZ prompt reached **2/3** at best
   (v7), v8 and v9 **1/3** each. A three-consecutive-adequate-answers criterion on more than one fixture has
   not been met by any prompt or model.
2. **The frozen 30-PR campaign.** Groups 1–4 (12 PRs) recorded all-Gemma; group 5 adjudicated but an
   incomplete security matrix (one reviewer exited 69 after a 590 s engine wait); **groups 2 and 5 must be
   rerun** under the corrected SQL routing (`98b01d3a`) and the mixed configuration (cloud ordinary roles +
   local security); groups 6–10 (15 PRs) not run. There is no labelled vulnerability population, so recall
   cannot be estimated from it.
3. **The consultant discussion of the module findings** (operator instruction, 2026-10-01): the actual
   consultant calls hit the caller-session cap (10 / 24 h) and produced no advice.
4. **The final `review_feature`**, run LAST over the two review epics (`COAI_FEATURE_MIN_EPICS=2` for that
   invocation). Its earlier attempt answered 0/1 on cloud quota and is not a pass.

## Build order

1. Re-establish the harnesses on current `main` (schema step 18; `COAI_SECURITY_CALIBRATION_ENDPOINT`,
   `COAI_SECURITY_AUDIT_SCOPE`, `COAI_SECURITY_AUDIT_MODEL_CONTEXT_TOKENS`; see `module_tests.md`).
2. Write the prediction for each measurement BEFORE running it (measurement rule 4).
3. Fidelity: a second positive fixture besides the AuthZ/SQL one. The adequacy criterion is WRITTEN into the
   RESULTS document before the run (correct trigger and mechanism, an impact the fixture supports, complete
   reproduction preconditions, no invented values). Decoding is pinned — the local runtime already sends
   temperature 0 and a prompt-derived seed; the run records both. **Exactly three attempts per prompt per model per
   fixture, 3/3 required** — no retries until a streak appears; a changed prompt is a new candidate with its own
   three. Raw answers retained.
3a. **Decision point after step 3:** if no prompt/model qualifies, the campaign is descoped (recorded, not run)
   and the lane stays documented as unverified evidence. If one qualifies with a changed prompt, model or
   configuration, ALL campaign groups (1–10) are rerun under that one configuration, so the dataset is uniform;
   the earlier all-Gemma groups remain historical.
4. Campaign (only if step 3a says so), grouped and inspected before advancing.
5. Consultant discussion of the findings once its cap allows; verify every piece of advice.
6. The final `review_feature`. Preflight first: `providers` shows every enabled vendor healthy, each model on its
   own vendor's CLI per `local.common.vendor-routing`, and no reviewer known to be at its quota (the earlier
   attempt answered 0/1 on cloud quota). Its findings are RESOLVED (accept/reject with reasons) in this plan;
   accepted findings that need product code become follow-up plans and pull requests of their own — this plan
   changes no product behaviour, and promotion waits for resolution, not for those fixes.
7. Promote this plan.

## Test plan

Explicit hardware tests only (`SecurityLaneCalibrationTests`, `SecurityLaneAuditTests`), never in CI.
Each result recorded in `research/RESULTS_security_lane_*.md` with the subject sha, harness, date, model,
pinned variables and what it does not settle. No prompt in Git changes without a measured comparison.

## Growth surfaces

Retained raw requests/answers per run: tens of MB per campaign group in the operator's retained-artifact folder
(on this machine `D:/rsd/_wt/security-lane-tools`; the harnesses take their output directory as a setting), kept
until this plan is promoted, then archived or deleted by the operator. Before a group is rerun, any partial
artifacts of an interrupted run of that group are moved aside, so a result is never assembled from two runs.
Nothing in the repository grows.

## Definition of Done

- [ ] A prompt/model reaches three consecutive adequate positive answers on at least two fixtures, or the
      record says plainly that none did and the lane stays documented as unverified evidence.
- [ ] Campaign groups 2 and 5 rerun; groups 6–10 run or their omission recorded with the reason.
- [ ] The consultant discussion held, or its unavailability recorded.
- [ ] The final `review_feature` run after a healthy-provider preflight; every finding resolved; accepted code
      findings turned into their own plans; reviewer counts recorded.
- [ ] RESULTS documents updated per run; this plan promoted.
