# RESULTS — four API reviewer models through the feature gate: calibrated first, then measured

> Status: **measurement complete, 2026-09-27.** Protocol: every review is the PRODUCT path — `review_feature` over MCP stdio against
> `coai-mcp` built from this branch (`feat/feature-review-e3-dialects`), one `api` vendor enabled and ticked for
> features per run, its own `COAI_DATA_DIR`, `COAI_FEATURE_MIN_EPICS=1`; the product's pack builder (hybrid outline +
> member hunks, 56 KB hunk reserve, 8 KB per member), its `SourceResolver` in up to 3 follow-up turns, its
> `feature-review.md` prompt, its parser and repair, and its ledger's per-turn tokens, cached tokens, seconds and cost.
> One prompt for every model; only transport settings differ per vendor, and phase 1 is what decided them.
> **The 20-minute limit (operator, 2026-09-27):** a reviewer's answer must arrive within 20 minutes for the WHOLE
> review — every turn of one reviewer's conversation, from launch to its final answer. Every run after the limit
> carries it as the product's conversation cap (`COAI_FEATURE_API_REVIEW_MINUTES`); a model that cannot answer
> validly within it on a calibration task after a fair attempt at bounding its reasoning is excluded from phase 2.
> Content: the 7 seeded trial tasks at their variant heads — cs2 / C#, rs3 / Rust, js3 / JavaScript, ts2 / TypeScript,
> py3 / Python, tsx2 / TSX, php1 / PHP (the repositories behind them are named only in the operator's raw data, never here).
> Raw data — packs, prompts, answers, ledgers, keys — stays on the operator's machine (`runs.jsonl`, `runs/<id>/`, `assess.jsonl`, `results.json`).
>
> Related: [RESULTS_feature_pack_trial.md](RESULTS_feature_pack_trial.md) (the packs and the old harness this replaces),
> [PLAN_feature_review.md](../todo/PLAN_feature_review.md) §4.10, §6 S0.5, S1.2 (ii)/(iii), §9.

## Why the first attempt was invalid, and what this one changes

The first attempt (2026-09-26, `scratchpad/models-trial`) ran the four models over the OLD trial harness's packs through
`coai-mcp --ask-api` only, and every failure it recorded was ours, not the models':

- **GLM-5.3** answered `{"` and stopped — on the Alibaba route `max_completion_tokens` bounds reasoning PLUS the answer,
  and at the harness's 16,384 every GLM call spent exactly 16,384 tokens on reasoning (`completion_tokens_details.reasoning_tokens`
  19–56K when re-run at 65,536, where 4 of 4 calls finished with `stop`). The product's own default is 8,192.
- **Qwen3.8-max** failed "not a review" twice per cell: the same ceiling, seen from the other side — the answer cut mid-string at
  ~16,382 tokens, or a `reasoning_content` and NO `content` at all, which the shim reported as "no message content" and the harness
  as a schema failure. The shim never read `finish_reason`, so a `length` cut looked like a bad model.
- **Grok-4.7** cached only 1,152 tokens on 14 of 17 follow-up turns although every follow-up's prefix was byte-identical: xAI routes
  a request to the replica holding the cache only with a conversation or cache key, and none was sent.
- The **packs** came from the trial harness, not the product's builder (no per-member hunk cap, no hunk reserve), and the harness's
  source serving refused files whose PATH contained the word "token" and could not resolve a qualified symbol (`Class.method`).
- The **assessment** (pass 5's rubric, inherited from pass 3/4) let a finding count as supported when the core problem existed and
  only the consequence was exaggerated.

This measurement removes each of those on our side FIRST (phase 1, one cause per iteration, RED test before every product change),
then measures the product path as a person would run it (phase 2), and judges the findings under a stricter rubric.

## Phase 1 — calibration: causes on our side, per model

Calibration tasks: js3 (mid-size: 5 changed files, outline 3 KB, a real plan) and ts2 (the largest: 86 changed files,
outline 54 KB, plan cut at 64 KB, 88 member hunks cut by the product's reserve). Every iteration is one product run
(`review_feature` over MCP stdio, one `api` row) with a recording pass-through in front of the vendor, so the raw answer,
`finish_reason`, the usage details and the request headers sent are on disk; one cause on our side is fixed per iteration,
RED test first, then GREEN, then a revert check. The per-iteration log with every number is the operator's `phase1.md`.

### grok-4.7 — calibrated after 3 iterations

| it | task | transport | valid | turns | cached per turn | ledger $ / vendor's own $ | s | what it proved |
|---|---|---|---|---|---|---|---|---|
| 01 | js3 | `openai`, 8,192, no effort | yes, 4 findings (both seeds) | 3 | 1,152 / 1,152 / 1,152 | 0.170 / **0.507** | 773 | reasoning tokens (57,917) unbilled — xAI reports them OUTSIDE `completion_tokens`; the byte-identical prefix never cached — no routing header |
| 02 | js3 | `xai` (+ `x-grok-conv-id`), 8,192 | yes, 6 findings (both seeds) | 3 | 1,152 / **34,944** / 34,944 | 0.658 / 0.658 | 1,214 | the header routes the conversation to its cache server; the ledger equals the vendor's bill to the sixth decimal |
| 03 | ts2 | `xai`, 8,192 | yes, 8 findings (S1 found) | 4 | 1,152 / 77,568 / 77,568 / 77,568 | 1.078 / 1.078 | 1,543 | the largest task: every turn under the 30-min deadline; turn 1's 77.6K-token prompt served from cache on each of the three follow-ups |

**Against the 20-minute whole-review limit** the operator set after this calibration (every turn of one reviewer's
conversation, launch to final answer): it01 12.9 min ✓, it02 **20.2 min** (14 s over), it03 **25.7 min** (over). The
operator's later ruling (after glm's calibration): grok must ALSO fit, always, with a margin — the largest task roughly
≤ 15 minutes — by lowering its effort one documented level from what it ran at.

**The field's effect, verified before it was used.** xAI's reasoning guide: `reasoning_effort` `low` / `medium` / `high`
(the default) / `xhigh` on grok-4.7, and "reasoning cannot be disabled". The 948-token probe prompt does not discriminate
(122–359 reasoning tokens at every level, three repeats each — a trivial diff gets a trivial think); js3's real turn-1
prompt (34,652 tokens, the recorded prompt of it02) does: **2,448 / 12,108 / 25,917** reasoning tokens and **36 / 153 /
333 s** at `low` / `medium` / `high` — medium/low 4.95× the reasoning tokens, high/medium 2.14× — and `high` reproducing the
calibration's vendor-default turns on the same prompt (15–39K, 4–10 min): the field is honoured and `high` is the default. So one
level down is `medium`, and it05 (js3) / it06 (ts2) re-ran both calibration tasks at `medium` with everything else as
calibrated — see *grok-4.7 at medium* below.

Causes on our side, fixed in the same change set (details under *Defects*), grok: xAI's reasoning tokens unbilled (#25); no cache
routing key (#27); and, from the first trial's Alibaba failures, `finish_reason` never read (#23), a failed call's cost lost
at the process boundary (#24), an all-rejected answer parsed as a clean review (#26), every api row inheriting the local
engine's 8,192 ceiling with no per-family floor or vendor fields (#28). Settled transport for grok-4.7: dialect `xai` —
no `temperature`/`seed`/`frequency_penalty` (the last is refused with 400), `max_completion_tokens` 8,192 (on xAI it bounds
the answer only: 39K of reasoning ran under it), strict `json_schema`, no effort field, the conversation key in
`x-grok-conv-id` — 3 follow-ups, 30-minute turn timeout. Behaviour: 4–10 minutes and 15–39K reasoning tokens per turn,
whatever the prompt size; the cache is cold on turn 1, and every follow-up is served turn 1's prompt from cache — a
CONSTANT cached count (34,944 on js3, 77,568 on ts2), not the growing previous prompt. Consistent with xAI's guide — it
matches "how many messages at the beginning match a previous request exactly", and the product sends one user message
whose text grows by a tail each turn — but that cause is a hypothesis, not measured here: a base message plus a tail
message per turn would be the test, and is a product follow-up. The comparison runs on the product as it is.

### grok-4.7 at `medium` — re-calibrated under the 20-minute rule (2 more iterations)

| it | task | transport | valid | turns | reasoning per turn | cached per turn | $ | min | what it proved |
|---|---|---|---|---|---|---|---|---|---|
| 05 | js3 | `xai`, `medium`, 8,192, 20-min cap | yes, 4 findings (both seeds) | 3 | 19,039 / 13,623 / 3,041 | 1,152 / 34,944 / 1,152 | 0.423 | **9.0** | the same task at the default took 20.2 min and $0.66; both seeds still found; turn 3 missed the cache despite the routing key |
| 06 | ts2 | the same | yes, 6 findings (S1) | 4 | 11,433 / 8,983 / 14,021 / 11,211 | 1,152 / 77,568 ×3 | 0.665 | **12.2** | the largest task over all four turns, 7.8 min inside the cap (25.7 min and $1.08 at the default); turns of 2.4–3.6 min against 4–10 |
| 07 | ts2 | the same, concurrent with it08 | yes, 8 findings (S1) | 4 | 9,974 / 6,318 / 12,174 / 9,256 | 1,152 / 77,568 ×3 | 0.613 | **10.4** | the consultant's check, rule declared first: a run above ~15 min would have triggered a `low` calibration |
| 08 | ts2 | the same, concurrent with it07 | yes, 6 findings (S1) | 4 | 10,840 / 7,626 / 14,413 / 15,608 | 1,152 / 77,568 ×3 | 0.651 | **12.3** | under the line too; two concurrent reviews no slower than one alone |

Settled transport for grok-4.7: `xai`, **`reasoning_effort: medium`**, 8,192 (answer-only on xAI), 3 follow-ups, the
20-minute cap — viable under the limit with the operator's margin, and provisional (the grok consultation): two fits are
not "always", the same prompt at the same setting took 153 s on the probe and 271 s in it05 (1.77× elapsed, 1.57× the
reasoning tokens — two observations, not a distribution), so ts2 was repeated twice more at `medium` under phase-2
concurrency (it07, it08) with the rule declared first: a run above ~15 minutes triggers a full-review calibration at
`low` (2.4K reasoning, 36 s on the js3 prompt; not measured on a review). They took 10.4 and 12.3 minutes — three ts2
reviews at `medium` at 10.4–12.3 min, the slowest turn 222 s — so `medium` is frozen as three fits, not "always". Unassessed finding counts decreased from
6 → 4 (js3) and 8 → 6 (ts2) in single before/after runs with the same planted seeds found; these establish neither a
quality loss nor its attribution to effort — fewer findings could be fewer false positives, and only the blinded
assessment with repeats could tell.

### qwen3.8-max — calibrated after 6 iterations (the last four under the 20-minute limit)

The Alibaba Model Studio compatible-mode route, dialect `dashscope` (`json_object` — the schema is accepted but not enforced
in thinking mode; `max_tokens` floored at 65,536 because there the ceiling bounds reasoning plus the answer; no sampling
fields; no cache key — the implicit cache is content-addressed from a 1,024-token common prefix). Thinking stayed ON in
every iteration; `enable_thinking: false` was never sent.

| it | task | transport | valid | turns | reasoning / out tokens | cached (turn 2) | $ | min | what it proved |
|---|---|---|---|---|---|---|---|---|---|
| 01 | js3 | vendor-default depth, 30-min turn | no answer | — | unknown | — | — | 30+ | the default thinking runs far past any practical deadline |
| 02 | js3 | the same, 60-min turn | no answer; stopped | — | unknown | — | — | 105+ (connection still open) | the generation never ended; stopped under the new limit |
| 03 | js3 | `reasoning_effort: high`, 20-min cap | no answer | — | unknown | — | — | 20 (cap) | `high` does not fit the limit on the mid-size task |
| 04 | js3 | `reasoning_effort: medium`, 20-min cap | yes, 5 findings (S1) | 2 | 10,288 / 14,601 | 33,792 of 40,422 | 0.178 | **5.4** | the effort field is the lever: one prompt, no answer at `high`, a whole two-turn review at `medium` |
| 05 | ts2 | `reasoning_effort: medium`, 20-min cap | yes, 7 findings | 2 | 17,764 / 24,019 | 80,896 of 86,490 | 0.334 | **7.7** | the largest task fits with room; the implicit cache holds turn 1's whole prompt |
| 06 | ts2 | the same, repeated on the final row (`max_completion_tokens`) | yes, 5 findings | 3 | 32,163 / 38,748 | 80,896 (turn 1: 0 — the 45-minute-old identical prompt was gone from the cache) | 0.458 | **11.5** | the consultant's frozen repeat: one more source turn (5.1 min, 15.4K reasoning — near the 16,384 budget) is the whole difference; still 8.5 minutes inside the cap |

That the field is honoured on this route was verified before it was used: the product's probe sent one 948-token prompt
under a 2,048-token cap and got 655 / 418 / 1,987 / 2,048-cut output tokens at `low` / `medium` / `high` / no field. What
the values MEAN on this model, from the vendor's API reference (found through the consultation): `low` is documented to
map to a 4,096-token thinking budget, `medium` to 16,384, `xhigh` to 262,144 with `high` and `max` mapped onto it, and
"when neither is set, the default thinking_budget (131072) and default reasoning_effort (xhigh) are used". So it01/it02
ran the default tier (a 131,072 budget) and it03 `high` → `xhigh`; none answered, and the tokens they generated are
unknown — whether the request's `max_tokens: 65536` bounded the thinking on this model is not documented (the documented
total ceiling is `max_completion_tokens`), and the recorder of that day kept no request body for an unanswered call.
Every `medium` turn's reasoning (4.5–10.3K) sat inside its 16K budget. Settled transport for qwen3.8-max: `dashscope`,
`reasoning_effort: medium` (documented to map to a 16,384-token thinking budget; thinking on), 3 follow-ups, the
20-minute cap — **viable under the limit**, not "best": `low` and a `thinking_budget` were not needed and are not
measured on a review, and what `medium` costs against the default in quality cannot be said, because the default never
answered. All four successful raw answers were audited field by field against the feature schema: every finding
carries its seven fields, none empty, under `json_object`.

**The ceiling's name matters on this route — measured after the consultation asked.** Two small calls through the product
shim, one prompt, a 64-token ceiling on both: `max_completion_tokens: 64` → 64 completion tokens, `finish_reason: length`
(the documented total, reasoning plus answer); `max_tokens: 64` → **2,410** completion tokens, 2,344 of them reasoning,
200 characters of answer — `max_tokens` bounds the ANSWER only on qwen3.8-max. So the `dashscope` row's floor of 65,536
bounded none of qwen's thinking in the three failed iterations, and the row's ceiling field became `max_completion_tokens`
(RED first: the row test pinned the old name); DeepSeek documents `max_tokens` as the sum already, so for it the change is
one of name if the compatible layer takes the field — checked on its next iteration and on GLM's first, and measured on
DeepSeek itself with the same 64-token probe: there BOTH names cut at 64 completion tokens (64 reasoning, no content,
`finish_reason: length`) — a change of name only for that model, as its reference says.

### deepseek-v4-pro — calibrated after 3 iterations

The same `dashscope` row; thinking ON (hybrid, the vendor's default); `reasoning_effort: high` sent explicitly — the
documented default, and the vendor states `low` and `medium` "produce the same behavior as `high`" on this model, so
effort is no lever here and none was needed.

| it | task | valid | turns | reasoning / out tokens | cached (turns 2+) | $ | min | what it showed |
|---|---|---|---|---|---|---|---|---|
| 01 | js3 | yes — **0 findings** | 3 | 17,105 / 18,474 | 32,768 | 0.202 | **5.6** | fits the cap with room; an empty review of a task where every other model found the planted seed |
| 02 | ts2 | yes — 1 blocking | 4 | 25,630 / 28,568 | 75,776 (turn 1: 2,048 — a warm fragment from qwen's run 20 minutes earlier) | 0.448 | **8.8** | the largest task through all four turns inside the cap |
| 03 | js3 | yes — 1 minor (not a seed) | 3 | 12,134 / 13,690 | 32,768 (turn 1: 2,048) | 0.180 | **4.3** | the consultant's re-run on the final row: `max_completion_tokens` accepted for this model; js3 empty of the seeds twice, with the seed lines in every prompt |

Settled transport: `dashscope` (`max_completion_tokens`, floor 65,536), `reasoning_effort: high`, 3 follow-ups, the
20-minute cap. 1.4–8.1K reasoning tokens a turn, 38–156 s a turn: the fastest of the Alibaba models and, on js3, the
emptiest (0 and 1 findings, neither a seed, on two runs whose prompts carried both planted lines from turn 1) — a phase-2
question, not a calibration one.

### glm-5.3 — calibrated after 2 iterations

The same `dashscope` row; thinking ON — the vendor says this model "supports only the thinking mode, which cannot be
disabled", takes `reasoning_effort` `low`/`high`/`max`, and supports `json_object` only. `high` sent; it fits, so no other
value was needed or measured. The first trial's fear (every call thinking to a 16,384 ceiling) did not recur under the
65,536 total ceiling.

| it | task | valid | turns / calls | reasoning / out tokens | cached (turns 2+) | $ | min | what it showed |
|---|---|---|---|---|---|---|---|---|
| 01 | js3 | yes — 8 findings (both seeds) | 4 / 5 | 22,213 / 30,246 | 32,512 | 0.240 | **6.8** | the fifth call is the product's repair of a first answer that was JSON but not the schema (bare strings in `findings`, an invented key) — a schema-following failure under `json_object`, one review of two; billed and counted inside the review's time and cost, and reported apart in phase 2 as the repair rate |
| 02 | ts2 | yes — 6 findings (S1) | 3 / 3 | 30,868 / 37,840 | 72,704 (turn 1: 2,048) | 0.342 | **8.3** | the cold turn on the largest task thinks 17K tokens in 4.2 minutes — the heaviest single turn of the Alibaba models, and the review ends with 11.7 minutes to spare |

Settled transport: `dashscope`, `reasoning_effort: high`, 3 follow-ups, the 20-minute cap. The seed recall of the four
models on the two calibration tasks is a calibration-run fact only (js3 and ts2 are reported apart in phase 2): GLM found
both js3 seeds and ts2's S1, grok both js3 seeds and ts2's S1, qwen js3's S1, DeepSeek none.

### Every phase-1 iteration (from `runs.jsonl`)

| run | transport | valid | turns | calls | finish | in / out / cached (ledger) | reasoning (wire) | s | cost $ | outcome / failure |
|---|---|---|---|---|---|---|---|---|---|---|
| p1-grok-4.7-js3-it01 | openai · max 8192 · effort none | yes | 3 | 3 | stop/stop/stop | 75,127 / 4,112 / 3,456 | 57,917 | 773.4 | 0.1697 | ok |
| p1-grok-4.7-js3-it02 | xai · max 8192 · effort none | yes | 3 | 3 | stop/stop/stop | 122,121 / 86,671 / 71,040 | 81,929 | 1,213.6 | 0.6577 | ok |
| p1-grok-4.7-ts2-it03 | xai · max 8192 · effort none | yes | 4 | 4 | stop/stop/stop/stop | 385,471 / 109,661 / 233,856 | 101,840 | 1,543.2 | 1.0781 | ok |
| p1-grok-4.7-js3-it04 | xai · max 64 · effort none | NO | 1 | 1 | length | 35,005 / 26,433 / 1,152 | 26,369 | 382.6 | 0.2269 | call 1: finish_reason=length (out=64, reasoning=26369, content=259 chars) / [08:37:42 WRN] coai-mcp#12180 reviewer grok/FeatureReview FAILED after 378.6s: exit  |
| p1-qwen3.8-max-js3-it01 | dashscope · max 8192 · effort none | NO | 1 | 0 |  | 0 / 0 / 0 | — | 1,792.0 | — | [09:09:35 WRN] coai-mcp#36248 reviewer qwen38max/FeatureReview FAILED after 1790.1s: exit 69: [coai-mcp] the API at http://127.0.0.1:56981/v1 did not finish in  |
| p1-qwen3.8-max-js3-it02 | dashscope · max 8192 · effort none | NO | 1 | 1 | None | 0 / 0 / 0 | — | 6,960.0 | — | no vendor answer within the 60-min turn deadline (exit 69); stopped under the 20-min whole-review limit with the vendor connection still open at 105 min |
| p1-qwen3.8-max-js3-it04 | dashscope · max 8192 · effort medium | yes | 2 | 2 | stop/stop | 74,674 / 14,601 / 33,792 | 10,288 | 323.0 | 0.1778 | ok |
| p1-qwen3.8-max-ts2-it05 | dashscope · max 8192 · effort medium | yes | 2 | 2 | stop/stop | 167,654 / 24,019 / 82,944 | 17,764 | 463.7 | 0.3343 | ok |
| p1-deepseek-v4-pro-js3-it01 | dashscope · max 8192 · effort high | yes | 3 | 3 | stop/stop/stop | 107,221 / 18,474 / 65,536 | 17,105 | 337.4 | 0.2018 | ok |
| p1-deepseek-v4-pro-ts2-it02 | dashscope · max 8192 · effort high | yes | 4 | 4 | stop/stop/stop/stop | 339,650 / 28,568 / 229,376 | 25,630 | 530.0 | 0.4477 | ok |
| p1-glm-5.3-js3-it01 | dashscope · max 8192 · effort high | yes | 4 | 5 | stop/stop/stop/stop/stop | 177,874 / 31,553 / 130,048 | 22,213 | 409.4 | 0.2396 | ok |
| p1-deepseek-v4-pro-js3-it03 | dashscope · max 8192 · effort high | yes | 3 | 3 | stop/stop/stop | 109,728 / 13,711 / 67,584 | 12,134 | 257.9 | 0.1805 | ok |
| p1-glm-5.3-ts2-it02 | dashscope · max 8192 · effort high | yes | 3 | 3 | stop/stop/stop | 245,213 / 37,840 / 147,456 | 30,868 | 499.5 | 0.3417 | ok |
| p1-qwen3.8-max-js3-it03 | dashscope · max 8192 · effort high | NO | 1 | 0 |  | 0 / 0 / 0 | — | 1,192.1 | — | [11:35:48 WRN] coai-mcp#33260 reviewer qwen38max/FeatureReview FAILED after 1190.1s: exit 69: [coai-mcp] the API at http://127.0.0.1:53864/v1 did not finish in  |
| p1-qwen3.8-max-ts2-it06 | dashscope · max 8192 · effort medium | yes | 3 | 3 | stop/stop/stop | 254,142 / 38,748 / 161,792 | 32,163 | 692.9 | 0.4576 | ok |
| p1-grok-4.7-js3-it05 | xai · max 8192 · effort medium | yes | 3 | 3 | stop/stop/stop | 120,739 / 39,577 / 37,248 | 35,703 | 544.6 | 0.4231 | ok |
| p1-grok-4.7-ts2-it06 | xai · max 8192 · effort medium | yes | 4 | 4 | stop/stop/stop/stop | 353,777 / 51,404 / 233,856 | 45,648 | 737.9 | 0.6652 | ok |
| p1-grok-4.7-ts2-it07 | xai · max 8192 · effort medium | yes | 4 | 4 | stop/stop/stop/stop | 352,376 / 43,143 / 233,856 | 37,722 | 625.9 | 0.6128 | ok |
| p1-grok-4.7-ts2-it08 | xai · max 8192 · effort medium | yes | 4 | 4 | stop/stop/stop/stop | 340,168 / 53,564 / 233,856 | 48,487 | 740.2 | 0.6509 | ok |

### The consultant's advice per model

One consultation per model through the product's own `consult` (the operator's consultant for this caller kind: Codex,
gpt-6-astra), after the model's calibration and before phase 2; the tree was not edited while a turn ran; every piece of
advice was checked against the code or a run before anything changed.

### grok-4.7 (consultation 9ad2bf34)

| Advice | Checked how | Outcome |
|---|---|---|
| xAI documents `reasoning_effort` `low/medium/high/xhigh`, default `high`; try `xhigh` | the model page confirms the four values and the default | not adopted: the frozen rule is the vendor's default depth for every model, and tuning on js3/ts2 would spend their hold-out status; recorded as an untested, costlier setting |
| "reasoning ran past 8,192" does not show whether the ceiling bounds the answer or is ignored — replay with `max_completion_tokens: 64` | done as iteration 04 on the product path | the ceiling bounds the ANSWER only: `finish_reason: length`, 64 completion tokens, 26,369 reasoning tokens under a 64-token ceiling; the shim exited 70 "cut at the token limit", no fragment kept, the reviewer failed, the ledger carried the $0.2269 xAI itself billed — every phase-1 fix seen end to end on a real vendor |
| no demonstrated reason to change the single user message, `stream:false` or the strict schema; disclose that Chat Completions carries no encrypted reasoning across turns | xAI's own example is user-only; every answer parsed | adopted as a disclosure: this measures grok inside this product (stateless resend of the prefix), not its ceiling as an agent |
| the cache key is per (provider, role, base prompt), not per round: identical repeats reuse it; 32-hex is fine; do not add `prompt_cache_key` (a Responses-API field) | `ConversationKey.cs` read; the Alibaba route's implicit cache is content-addressed with no key at all | kept by design (a re-run of the same review is the same conversation; a D23 retry wants the cache); the consequence is measured — turn-1 cached tokens are recorded per run and reported in the phase-2 table. The consultant's qualification stands: recording the hits exposes a warmth difference between repeats, it does not prove the two vendors' cache advantages equal |
| "the whole previous prompt is cached" is wrong: the constant count is turn 1's prefix | the tap: 77,568 cached on turns 2, 3 and 4 against prompts of 92,661 / 103,044 / 112,143 | corrected in the log and here; the one-message layout (a user message that grows by a tail) is CONSISTENT with xAI's message-prefix matching as the cause — a hypothesis, not measured (a base + tail two-message layout would be the test, and is a product follow-up) |
| freeze the selection rule before tuning any other model | — | frozen: vendor-default reasoning depth for every model, ceiling raised only so the answer is never cut, no quality tuning on the calibration tasks |

Closed `solved`: the discriminating check was run and settled the ceiling question; the wording was corrected; the rule was
written down before the next model.

### qwen3.8-max (consultation 2b121c3d)

| Advice | Checked how | Outcome |
|---|---|---|
| `high` did not lower the default tier: the vendor maps the model's default to `xhigh`, `high` → `xhigh`, `medium` → a 16,384-token thinking budget | the vendor's chat-completions API reference | confirmed: `low` 4,096 · `medium` 16,384 · `xhigh` 262,144 (the default; `high`/`max` map to it); `reasoning_effort` and `thinking_budget` are mutually exclusive. Every `medium` turn's reasoning (4.5–10.3K) sat under 16,384; the three default/`high` runs were 262K-budget generations that no 20-minute deadline can hold |
| "parsed" is not schema compliance — the parser blanks a missing `why`/`fix`; audit the raw answers | the four successful raw answers, field by field | every finding carries all seven fields, none empty; root keys exactly `findings, notes, sourceRequests` — compliant under `json_object` |
| 65,536 is headroom with a per-call exposure (~$0.39 of output at the ceiling); `CeilingFor` is `Math.Max` | `ApiDialect.cs` read | correct; kept for phase 2 |
| an ESTABLISHED socket proves neither generation nor billing; the recorder holds upstream work after the shim gives up | the tap's design (deliberate: to record the answer) | wording softened to "no answer"; the phase-2 in-flight wait cut to 5 minutes |
| fairness needs wording, not matched effort labels; report js3/ts2 apart from the untouched tasks | — | adopted verbatim (see *Phase 2*); the per-task table separates the calibration tasks |
| spend the spare iteration on a frozen `medium` repeat of ts2 (reproducibility, first-turn warmth) rather than a `low` run | — | scheduled as it06 |
| (turn 2) a 262K thinking allowance does not make the failed runs 262K generations — the request carried a 65,536 ceiling; and the reference gives a 131,072 default budget when neither field is set | the API reference again; the recorder's files | confirmed: "when neither is set, the default thinking_budget (131072) and default reasoning_effort (xhigh) are used"; whether our `max_tokens` bounded the thinking on qwen3.8-max is undocumented (`max_completion_tokens` is the documented total ceiling), and the recorder wrote no request body for an unanswered call — so it01/it02 are recorded "default tier, 131,072 budget, no answer, generated tokens unknown" and it03 "`high` → `xhigh`, no answer, unknown"; the recorder now writes the request before forwarding |
| (turn 2) the recorder is part of the measurement: a retained upstream call occupies an endpoint slot after the product cancelled; require zero outstanding requests before it06 and each phase-2 run | the recorder's forwarding thread (a daemon holding `urlopen` until the vendor answers) | fixed: the upstream socket timeout is the run's cap plus the diagnostic wait, so a finished run leaves no connection open; the phase-2 runner starts on an endpoint only after the previous run on it returned |
| (turn 2) call `medium` "documented to map to a 16,384-token thinking budget", and close as "viable under the limit", not "best" | — | adopted |
| (turn 3) a socket timeout is per operation, not a whole-request deadline; a dripping response could outlive it — add an absolute deadline that closes the upstream connection, checked with a local dripping upstream | done: the recorder now closes the vendor socket from a timer at the deadline; a local upstream dripping a byte a second for 12 s under a 4 s deadline was closed at 5.0 s with nothing in flight | fixed and proven; each run records that zero upstream connections remain open locally |
| (turn 3) "connections closed" is not "nothing outstanding at the vendor"; the $0.39 output bound is conditional unless `max_tokens` is shown to bound qwen's total generation | wording; and two small calls at a 64-token ceiling through the product shim, one with `max_completion_tokens` and one with `max_tokens`, reading `finish_reason` and the usage | wording adopted; measured: `max_tokens` bounds the answer only on qwen3.8-max (2,344 reasoning tokens under a 64-token ceiling), `max_completion_tokens` the total — the row's field changed (see *Phase 1*) |

### deepseek-v4-pro (consultation 65388e08)

| Advice | Checked how | Outcome |
|---|---|---|
| the empty js3 review is a model miss under this product's context, unless the seeds' evidence never reached the prompts or a finding was dropped between turns — compare each turn's raw findings with the terminal ones and find the seed lines in the actual prompts | the six prompt files of the run and the three raw answers | both planted lines were in every prompt from turn 1 (the pack's hunks); every turn's raw findings were empty; nothing was dropped — recorded as a model miss with the evidence in hand |
| freeze explicit `high` (the documented default; low/medium map to it on this model); high-versus-omitted cannot show whether the field is honoured; do not tune on js3's seeds | the vendor's reference | adopted; no effort experiment on this model |
| probe `max_completion_tokens` against `max_tokens` on DeepSeek itself at a 64-token ceiling, then re-run js3 once on the frozen configuration before phase 2 | the two probes and iteration 03 | both names cut at 64 completion tokens (64 reasoning, no content, `finish_reason: length`) on deepseek-v4-pro — a change of name only there; iteration 03 on the final row: valid, 4.3 min, 1 finding, no seed — js3 empty of the seeds twice |
| report seed recall apart from evidence availability (withheld evidence is a product limitation; available-but-unrequested evidence tests the reviewer); balance run order for cache warmth; keep the calibration tasks separate | — | adopted in the per-seed table and the run order |

Closed `solved` (it had lapsed idle before the last report could be posted as a turn; the outcome and the note were recorded).

### glm-5.3 (consultation 862436cd)

| Advice | Checked how | Outcome |
|---|---|---|
| keep `high`; a `low` run is not required — fairness is one frozen rule and one deadline, not matched effort labels; say why `high` was the first setting and do not call it a vendor default unless verified | — | adopted: `high` was the first level tried and kept because it fit; the module and the log say so and claim no default |
| count repairs in elapsed time and cost; reconcile it01's 409 s against its five calls; report "reviews needing repair / attempted" apart | the run record: launch-to-final 407.4 s against the five calls' 406.5 s; all five in the $0.240 | confirmed (the product's overhead under a second); the per-model table gains "runs with extra calls / extra calls" — the count proves an extra call, not its cause (the consultant's second-turn correction of the label) |
| "GLM habit" is a hypothesis — an ordinary schema-following failure under `json_object`, prompt influence unresolved; check the recorded first requests carry the schema and replay both raw first answers | the two turn-1 request bodies (the schema text from character 6,004, `json_object`, `high`, 65,536) and the two raw first answers | it01's first answer: `findings, surveyOfRemaining, notes, sourceRequests`, findings mixing objects and strings — refused and repaired; it02's: exactly the three keys, every finding an object — parsed. One of two; reworded everywhere as a schema-following failure, cause not established |
| the documented glm-5.3 capabilities agree; whether effort and a thinking budget may coexist is not on the page — `EffortExcludesThinkingBudget: false` is unverified, not contradicted | the vendor's GLM page | recorded on the capability record's definition ("documented to exclude"; false where nothing is documented) and on the module |
| a concrete module defect: model-specific capabilities applied family-wide — `Resolve("dashscope", "glm-5.2")` returned glm-5.3's no-switch declaration and excluded `medium`, both documented for glm-5.2 | the vendor's GLM page (glm-5.2: `enable_thinking`, efforts `none … max`; glm-5.3: thinking cannot be disabled, `low/high/max`) | fixed: each calibrated module is bound to the exact model it was measured on; any other model on the row runs generically with nothing declared; thinking OFF on a generic row is refused with "no calibrated module spells a thinking switch" (the product spells only what a measured module declares) |
| (turn 2) the other entrance: `Resolve("glm", "glm-5.2")` still returned glm-5.3's capabilities — a named module bypassed the model restriction | by construction (the test asserted it) | fixed: a named module for a model it was not measured on is set aside to the generic module over its own row (the measured transport kept — xAI's routing header for a grok-4.6 row), and `providers` says so in a note, so the downgrade is never silent; each module names its `MeasuredModel` |
| (turn 2) label the metric "runs with extra calls / extra calls" unless repair events are recorded — calls exceeding turns proves extra calls, not their cause | — | adopted |
| (turn 3) an EMPTY model still bypassed the scoping: `Resolve("glm", "")` declared glm-5.3's capabilities while the endpoint may pick another model | by construction | fixed: an empty model is a mismatch too — the row runs generically and `providers` says "a row that names no model (the endpoint picks one) runs on the same row with nothing declared; name glm-5.3 … for calibrated settings" |

### grok-4.7, the twenty-minute rule (consultation 68dcfdce)

| Advice | Checked how | Outcome |
|---|---|---|
| the weak point is overstated timing confidence, not `medium`: 271/153 is 1.77× elapsed and 19,039/12,108 is 1.57× tokens — two observations, not a distribution, so "expected at 10–17 minutes" is unsupported; repeat ts2 twice at `medium` under phase-2 concurrency, pre-declaring that a run above ~15 minutes triggers a `low` calibration; a deadline bounds the waiting, not the completion | the arithmetic; then it07 and it08 (ts2 at `medium`, two runs on the endpoint at once, the rule declared before they ran) | wording corrected everywhere; the repeats took 10.4 and 12.3 minutes, neither over the line, so `medium` is frozen as "three fits, not always" |
| the comparison is defensible as frozen configurations under a common deadline if the same margin criterion applies to every model, every phase-2 timeout stays in the denominator and no setting moves during measurement; "the Alibaba models were not tuned down" is wrong (Qwen is Alibaba too) — say "DeepSeek and GLM"; "each level roughly doubling" is wrong (medium/low 4.95×, high/medium 2.14×) | — | all three adopted verbatim |
| replace "what medium costs in quality" with: unassessed finding counts decreased 6→4 and 8→6 in single before/after runs with the same seeds found; that establishes neither a quality loss nor its attribution to effort (fewer findings could be fewer false positives; blinded assessment with repeats is what could tell) | — | adopted verbatim in the results document and the log |

Closed `solved`. The verification report could not be posted as a second turn: the product caps consult calls at 10 per
caller session and this session's tenth was the turn above — the outcome and the note carry what the turn would have
(the two repeats' times and the rule's outcome).

## Phase 2 — the measurement

7 tasks × 3 repeats × 4 models = 84 planned runs; 84 done. Same prompt, same pack per task,
at most 4 concurrent runs, 2 per endpoint. Assessment: blinded (no model, no run id on a finding), strict rubric —
`supported` only when trigger, mechanism AND consequence are all correct; `partial` when partly right; else `refuted` or
`unresolved`; value high/medium/low/none; grounded; severity fair/overstated/understated; seed hit (which of the 14).

**Fairness, stated plainly (the qwen consultation's wording, adopted; the grok rule added 2026-09-27):** we compare frozen
product configurations under a common 20-minute deadline, and EVERY model's reasoning effort is tuned to that deadline, not to
quality: qwen3.8-max runs `medium` (a 16,384-token thinking budget) after default-tier timeouts; grok-4.7 runs `medium` — one
documented level below its `high` default — after two over-limit calibration reviews at the default (the operator's later rule
that it too must fit, with margin), the field's effect verified on a real prompt first (2.4K / 12.1K / 25.9K reasoning tokens at
low / medium / high); deepseek-v4-pro and glm-5.3 run `high` because it fit. Tuning opportunity is still not matched — DeepSeek
and GLM were not tuned down, grok and qwen were — and what any model loses at its lower level is not measured: unassessed
finding counts decreased from 6→4 (js3) and 8→6 (ts2) in single before/after grok runs with the same planted seeds found, which
establishes neither a quality loss nor its attribution to effort (fewer findings could be fewer false positives; the blinded
assessment and the repeats are what could tell). The same margin criterion applies to every model, every phase-2 timeout stays
in the denominator, and no setting moves during measurement. The two calibration tasks (js3, ts2) already influenced the
settings and are reported apart from the five untouched tasks in the per-task table.

### Per model

| model | runs | attempts (failed) | valid % (final) | findings/run | seeds hit mean (range) | distinct seeds (cross-epic) | supported % (strict) | supported+partial % | high/run | overstated % | min p50 / p90 | turns mean | runs with extra calls / extra calls | tokens in / out / cached per run | cache % | turn-1 cached (warm runs) | reasoning/run | cost/run $ | cost/seed $ | total $ |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| grok-4.7 | 21 | 29 (8) | 100.0 | 4.67 | 1.52 (1–2) | 12 (7) | 67.3 | 87.8 | 2.33 | 15.1 | 9.6 / 15.4 | 3.14 | 0 / 0 | 215,285 / 45,485 / 108,184 | 50.3 | 1,536 (0) | 41,597 | 0.5412 | 0.3552 | 11.37 |
| qwen3.8-max | 21 | 21 (0) | 100.0 | 5.33 | 0.67 (0–2) | 6 (4) | 22.3 | 42.9 | 0.57 | 20.8 | 9.4 / 13.3 | 2.71 | 0 / 0 | 188,046 / 30,808 / 115,614 | 61.5 | 1,950 (0) | 24,343 | 0.3586 | 0.5379 | 7.53 |
| deepseek-v4-pro | 21 | 21 (1) | 95.2 | 1.76 | 0.57 (0–2) | 4 (2) | 43.2 | 62.2 | 0.43 | 43.5 | 5.5 / 8.7 | 2.76 | 1 / 1 | 185,212 / 20,675 / 117,614 | 63.5 | 1,950 (0) | 18,377 | 0.2850 | 0.4987 | 5.98 |
| glm-5.3 | 21 | 21 (0) | 100.0 | 4.43 | 1 (0–2) | 9 (5) | 45.2 | 82.8 | 1 | 23.4 | 5.6 / 15.5 | 2.71 | 1 / 1 | 175,115 / 32,595 / 103,046 | 58.8 | 1,109 (0) | 27,339 | 0.2711 | 0.2711 | 5.69 |

Seeds: 14 planted defects (2 per task, 8 cross-epic). "seeds hit" counts distinct seeds a run's findings identify (trigger +
mechanism, judged blind); "supported %" is over every judged finding of the model. Tokens are the product ledger's per-turn
sums (prompt tokens include the cached subset); reasoning tokens are read off the wire (`completion_tokens_details`).
Cost is the ledger's (S3.7's lookup at the list prices in `models.py`); Alibaba models are Token-Plan credits — a list-price equivalent.
"turn-1 cached" is the mean cached-token count of each run's FIRST call, with the number of runs whose first call was warm
(> 4,096 cached): a repeat of a task can find the previous repeat's prefix in the vendor's cache — recorded, not hidden; the
repeats are spread so the three of one task never run back to back.

### Failed attempts (on the record, apart from the comparison)

Every phase-2 attempt that did not end in a valid review. A cell whose attempt failed on a vendor transient was re-run as
attempt 2 from the fixed binary (§9.29); the comparison above uses each cell's FINAL attempt, and a cell whose final attempt
failed stays in the denominator as an invalid run.

| run | model | task | attempt | re-run | calls | min | cost $ | failure |
|---|---|---|---|---|---|---|---|---|
| p2-grok-4.7-rs3-r1 | grok-4.7 | rs3 | 1 | yes (valid) | 1 | 2.4 | — | call 1: HTTP 500 "Auth context expired." / [14:51:03 WRN] coai-mcp#17640 reviewer grok/FeatureReview FAILED after 138.6s: exit 70: [coai-mcp] the API  |
| p2-grok-4.7-py3-r1 | grok-4.7 | py3 | 1 | yes (valid) | 1 | 1.3 | — | call 1: HTTP 500 "Auth context expired." / [15:02:29 WRN] coai-mcp#42740 reviewer grok/FeatureReview FAILED after 74.6s: exit 70: [coai-mcp] the API a |
| p2-grok-4.7-rs3-r2 | grok-4.7 | rs3 | 1 | yes (valid) | 2 | 9.5 | 0.2362 | call 2: HTTP 500 "Auth context expired." / [15:30:24 WRN] coai-mcp#10108 reviewer grok/FeatureReview FAILED after 566.5s: exit 70: [coai-mcp] the API  |
| p2-grok-4.7-tsx2-r2 | grok-4.7 | tsx2 | 1 | yes (valid) | 4 | 9.5 | 0.4952 | call 4: HTTP 500 "Auth context expired." / [15:49:32 WRN] coai-mcp#44144 reviewer grok/FeatureReview FAILED after 565.0s: exit 70: [coai-mcp] the API  |
| p2-grok-4.7-php1-r2 | grok-4.7 | php1 | 1 | yes (valid) | 2 | 9.4 | 0.2479 | call 2: HTTP 500 "Auth context expired." / [15:50:33 WRN] coai-mcp#48032 reviewer grok/FeatureReview FAILED after 559.3s: exit 70: [coai-mcp] the API  |
| p2-grok-4.7-cs2-r3 | grok-4.7 | cs2 | 1 | yes (valid) | 2 | 6.8 | 0.2001 | call 2: HTTP 500 "Auth context expired." / [15:56:18 WRN] coai-mcp#45232 reviewer grok/FeatureReview FAILED after 402.1s: exit 70: [coai-mcp] the API  |
| p2-grok-4.7-js3-r3 | grok-4.7 | js3 | 1 | yes (valid) | 1 | 3.7 | — | call 1: HTTP 500 "Auth context expired." / [16:00:02 WRN] coai-mcp#16236 reviewer grok/FeatureReview FAILED after 220.9s: exit 70: [coai-mcp] the API  |
| p2-grok-4.7-ts2-r3 | grok-4.7 | ts2 | 1 | yes (valid) | 2 | 7.2 | 0.2186 | call 2: HTTP 500 "Auth context expired." / [16:07:14 WRN] coai-mcp#44860 reviewer grok/FeatureReview FAILED after 427.9s: exit 70: [coai-mcp] the API  |
| p2-deepseek-v4-pro-php1-r2 | deepseek-v4-pro | php1 | 1 | no — final | 3 | 8.7 | 0.2859 | [17:18:53 WRN] coai-mcp#8532 reviewer deepseekv4pro/FeatureReview FAILED after 517.9s: unparseable: the answer was not the schema's JSON after one rep |

### Where each planted seed's evidence was

Read off the product's own turn-1 prompt per task: `pack` — the seeded line is in the outline/hunks every reviewer is sent;
`on request` — in the repository at head, servable through `sourceRequests`, not in the pack (a test of the reviewer's source
selection); `withheld` — a credential-shaped file the product never sends or serves (a product limitation, not a reviewer miss). A credential-shaped
file whose hunks ARE in the pack is labelled `pack` with that noted: the product refuses to SERVE it on request, not to show its diff.

| seed | task | cross-epic | evidence |
|---|---|---|---|
| cs2-S1 | cs2 | yes | pack |
| cs2-S2 | cs2 | no | pack |
| rs3-S1 | rs3 | yes | pack |
| rs3-S2 | rs3 | no | pack |
| js3-S1 | js3 | yes | pack |
| js3-S2 | js3 | no | pack (a removal: its file's hunks are in the pack) |
| ts2-S1 | ts2 | yes | pack |
| ts2-S2 | ts2 | no | pack; the file is credential-shaped, so it is never served on request |
| py3-S1 | py3 | yes | pack |
| py3-S2 | py3 | yes | on request |
| tsx2-S1 | tsx2 | yes | on request |
| tsx2-S2 | tsx2 | no | on request |
| php1-S1 | php1 | yes | pack |
| php1-S2 | php1 | no | on request |

### Per language and task

js3 and ts2 are the CALIBRATION tasks: every model's settings were chosen on them, so they are not held out; the other five are untouched.

| task | language | model | valid | findings per run | seeds hit per run | turns | seconds | cost $ |
|---|---|---|---|---|---|---|---|---|
| cs2 | C# | grok-4.7 | 3/3 | 4/3/3 | 2/2/2 | 2/2/2 | 468.0/623.5/420.3 | 0.355/0.370/0.374 |
| cs2 | C# | qwen3.8-max | 3/3 | 5/4/4 | 1/2/2 | 2/2/2 | 354.1/413.4/464.0 | 0.233/0.251/0.264 |
| cs2 | C# | deepseek-v4-pro | 3/3 | 2/2/3 | 2/2/2 | 2/2/2 | 288.7/301.4/301.7 | 0.201/0.198/0.202 |
| cs2 | C# | glm-5.3 | 3/3 | 3/4/5 | 2/2/2 | 2/2/3 | 224.4/278.7/355.3 | 0.150/0.167/0.215 |
| rs3 | Rust | grok-4.7 | 3/3 | 6/3/2 | 2/2/2 | 3/4/3 | 712.9/548.7/591.9 | 0.530/0.526/0.417 |
| rs3 | Rust | qwen3.8-max | 3/3 | 3/5/5 | 2/2/2 | 4/3/3 | 730.6/569.8/582.5 | 0.390/0.323/0.326 |
| rs3 | Rust | deepseek-v4-pro | 3/3 | 2/2/2 | 2/2/2 | 2/2/2 | 316.8/282.7/269.6 | 0.217/0.210/0.208 |
| rs3 | Rust | glm-5.3 | 3/3 | 4/3/4 | 2/2/2 | 1/1/2 | 219.2/202.5/421.5 | 0.140/0.137/0.231 |
| js3 (calibration) | JavaScript | grok-4.7 | 3/3 | 4/5/5 | 1/2/2 | 3/3/3 | 604.5/711.5/469.6 | 0.428/0.508/0.383 |
| js3 (calibration) | JavaScript | qwen3.8-max | 3/3 | 3/5/5 | 1/1/0 | 3/2/4 | 402.1/431.3/796.6 | 0.245/0.205/0.385 |
| js3 (calibration) | JavaScript | deepseek-v4-pro | 3/3 | 0/1/1 | 0/0/0 | 2/4/4 | 227.9/395.8/384.1 | 0.145/0.234/0.231 |
| js3 (calibration) | JavaScript | glm-5.3 | 3/3 | 7/5/5 | 2/1/1 | 3/3/3 | 334.0/364.2/254.1 | 0.187/0.210/0.162 |
| ts2 (calibration) | TypeScript | grok-4.7 | 3/3 | 4/7/8 | 1/2/2 | 4/3/4 | 560.6/578.1/974.8 | 0.560/0.523/0.877 |
| ts2 (calibration) | TypeScript | qwen3.8-max | 3/3 | 6/8/7 | 0/0/0 | 4/3/3 | 1,036.3/627.7/809.6 | 0.607/0.402/0.481 |
| ts2 (calibration) | TypeScript | deepseek-v4-pro | 3/3 | 3/1/4 | 0/0/0 | 4/3/2 | 584.5/327.9/306.7 | 0.453/0.305/0.286 |
| ts2 (calibration) | TypeScript | glm-5.3 | 3/3 | 7/6/6 | 0/1/0 | 4/3/4 | 551.3/372.6/584.3 | 0.388/0.278/0.408 |
| py3 | Python | grok-4.7 | 3/3 | 8/7/6 | 1/1/1 | 2/2/2 | 526.4/481.6/485.7 | 0.643/0.458/0.437 |
| py3 | Python | qwen3.8-max | 3/3 | 6/7/6 | 1/0/0 | 3/2/2 | 688.2/715.9/561.1 | 0.516/0.427/0.400 |
| py3 | Python | deepseek-v4-pro | 3/3 | 2/2/2 | 0/0/0 | 2/3/3 | 302.5/384.9/262.2 | 0.324/0.376/0.340 |
| py3 | Python | glm-5.3 | 3/3 | 7/7/0 | 0/0/0 | 3/3/1 | 329.3/427.1/154.4 | 0.289/0.318/0.168 |
| tsx2 | TSX | grok-4.7 | 3/3 | 4/5/3 | 1/1/1 | 4/4/4 | 478.9/685.0/354.4 | 0.670/0.630/0.521 |
| tsx2 | TSX | qwen3.8-max | 3/3 | 6/6/8 | 0/0/0 | 4/3/3 | 662.3/404.5/556.5 | 0.537/0.343/0.400 |
| tsx2 | TSX | deepseek-v4-pro | 3/3 | 1/1/0 | 0/0/0 | 4/4/4 | 424.3/436.9/430.2 | 0.403/0.440/0.393 |
| tsx2 | TSX | glm-5.3 | 3/3 | 3/3/5 | 1/1/1 | 3/2/4 | 183.9/180.7/325.9 | 0.235/0.190/0.307 |
| php1 | PHP | grok-4.7 | 3/3 | 3/4/4 | 1/1/2 | 4/4/4 | 923.9/785.0/1,052.4 | 0.709/0.678/0.769 |
| php1 | PHP | qwen3.8-max | 3/3 | 4/4/5 | 0/0/0 | 1/2/2 | 254.3/545.2/509.2 | 0.202/0.306/0.288 |
| php1 | PHP | deepseek-v4-pro | 2/3 | 4/0/2 | 0/0/0 | 2/2/3 | 347.0/520.4/591.8 | 0.217/0.286/0.315 |
| php1 | PHP | glm-5.3 | 3/3 | 4/2/3 | 1/0/0 | 3/4/3 | 1,134.0/928.2/1,086.2 | 0.523/0.461/0.530 |

### Run-to-run variance

Three repeats per (model, task) cell, the same prompt and pack each time; the comparison uses each cell's FINAL
attempt (grok's eight vendor-500 cells are their attempt 2; the failed attempts are listed apart above). What moved
between repeats, and what did not:

| model | cells whose three repeats hit the SAME number of seeds | supported/partial issue clusters seen in ≥ 2 of the 3 repeats | review time, mean coefficient of variation per cell | slowest review | reviews over the ~15-min margin | cost per review, min–max |
|---|---|---|---|---|---|---|
| grok-4.7 | 4 of 7 | 52 % | 0.16 | 17.5 min (php1) | 3 of 21 (php1 15.4 and 17.5, ts2 16.2) | $0.35–0.88 |
| qwen3.8-max | 4 of 7 | 38 % | 0.19 | 17.3 min (ts2) | 1 of 21 | $0.20–0.61 |
| deepseek-v4-pro | **7 of 7** | 49 % | 0.14 | 9.9 min | 0 of 21 | $0.14–0.45 |
| glm-5.3 | 4 of 7 | 44 % | 0.23 | **18.9 min** (php1) | 3 of 21 (php1 15.5, 18.1, 18.9) | $0.14–0.53 |

- **Seed recall is the stable signal; the rest of a review is not.** Every model hit both seeds of cs2 and rs3 on
  (nearly) every repeat — those two tasks do not separate the models. On the other five tasks the per-cell counts
  wander by one seed between repeats (grok js3 1/2/2, ts2 1/2/2, php1 1/1/2; glm js3 2/1/1, ts2 0/1/0), so a single run
  is a poor estimate of a model: the per-seed table below counts how many of the three repeats found each seed.
- **Beyond the seeds, a repeat is a different review.** Only 38–52 % of the issue clusters a model raised (supported or
  partial) reappeared in a second repeat of the same cell. A reviewer ticked for a feature round should be read as a
  sampler of real issues, not a checklist that returns the same list each time — which is also the argument for more
  than one reviewer.
- **deepseek-v4-pro is the steadiest because it says the least**: 7 of 7 cells identical in seed count, but 1.76
  findings a run and none of the 10 seeds outside cs2/rs3 in any repeat.
- **Time.** Within-cell variation is modest (CV 0.14–0.23); the task, not the repeat, decides the time. php1 is the
  slow task for both grok (13.1–17.5 min) and glm (15.5–18.9 min, 1.1 minutes from the cap on its slowest repeat):
  glm's php1 repeats spent 68–85K reasoning tokens against 9–37K on every other task. None of the 84 final attempts was
  cut by the 20-minute cap; seven crossed the ~15-minute margin the operator asked grok to keep.
- **Cache warmth did not differ between repeats**: no run's first call was warm (turn-1 cached ≤ 4,096 tokens on all 84
  final attempts, the "warm runs" column above) — the repeats were spread in time and every vendor's first turn was
  effectively cold, so the cache advantage is only the follow-up turns' (turn 1's prefix re-served on turns 2+).

Seed by seed — how many of the three repeats found it (final attempts), with where its evidence was:

| seed | cross-epic | evidence | grok-4.7 | qwen3.8-max | deepseek-v4-pro | glm-5.3 |
|---|---|---|---|---|---|---|
| cs2-S1 | yes | pack | 3 | 2 | 3 | 3 |
| cs2-S2 | no | pack | 3 | 3 | 3 | 3 |
| rs3-S1 | yes | pack | 3 | 3 | 3 | 3 |
| rs3-S2 | no | pack | 3 | 3 | 3 | 3 |
| js3-S1 (calibration) | yes | pack | 3 | 2 | 0 | 3 |
| js3-S2 (calibration) | no | pack (a removal) | 2 | 0 | 0 | 1 |
| ts2-S1 (calibration) | yes | pack | 3 | 0 | 0 | 1 |
| ts2-S2 (calibration) | no | pack (credential-shaped file) | 2 | 0 | 0 | 0 |
| py3-S1 | yes | pack | 3 | 1 | 0 | 0 |
| py3-S2 | yes | on request | 0 | 0 | 0 | 0 |
| tsx2-S1 | yes | on request | 3 | 0 | 0 | 3 |
| tsx2-S2 | no | on request | 0 | 0 | 0 | 0 |
| php1-S1 | yes | pack | 1 | 0 | 0 | 0 |
| php1-S2 | no | on request | 3 | 0 | 0 | 1 |

Of the ten seeds outside cs2/rs3, grok found 20 of 30 possible (seed × repeat), glm 9, qwen 3, DeepSeek 0. The
"on request" seeds are the test of a reviewer's source selection: grok and glm each reached two of the four
(tsx2-S1 every time), qwen and DeepSeek none; py3-S2 and tsx2-S2 were found by nobody.

**A correction to the evidence column, made while writing this.** The harness labelled any seed in a credential-shaped
file "withheld" BEFORE looking at the prompt, so ts2-S2 was reported as evidence the product never sends. The turn-1
prompt carries its hunk (the edited function, lines 126–135); what the product withholds is SERVING such a
file on request, not its diff. The label now reads the pack first; grok's two ts2-S2 hits are genuine.

**Where it split by task.** Calibration tasks (js3, ts2) against the five untouched ones, seeds a run: grok 1.67 / 1.47,
glm 0.83 / 1.07, qwen 0.33 / 0.80, DeepSeek 0.00 / 0.80 — the calibration tasks did not flatter any model's recall
(DeepSeek's untouched-task hits are all cs2/rs3). Strict-supported share by task is where the models part most: grok
13 of 14 findings on js3 and 10 of 11 on rs3 but 8 of 21 on py3; qwen never above 5 of 13 on any task (2 of 13 on
js3, 3 of 21 on ts2).

### Assessment: agreement between assessors

**The assessor.** One blinded assessor judged all 340 findings of the 84 final attempts (every finding of every finished phase-2 run
was exported; no failed attempt carried a finding): Codex
(`codex exec -m gpt-6-astra`, read-only sandbox, MCP servers off, the output schema enforced), 16 batches of at most 24
findings per task, each row carrying no model, no run id and a fresh 8-hex id, the rows shuffled within each export.
Every batch parsed on the first try (0 retries, 0 assessment failures); 65–139 s a batch. The assessor is an OpenAI
model, a vendor none of the four compared models belongs to. The strict rubric: `supported` only when the finding's
trigger, mechanism AND consequence are all correct at the code.

One ordering fact, recorded rather than hidden: findings were exported in two passes (260 when the grok re-runs had
ended, 80 more when the Alibaba runs had), so the second pass — mostly repeat-3 runs and the grok re-runs — sits in the
last batch of each task. The rows stay blinded; the position is the only thing that correlates with the run.

**The hand check.** 20 findings drawn stratified before looking at any verdict — per model, 2 the assessor called
`supported`, 1 `partial`, 2 `refuted` (fixed seed, `handcheck/sample.json` in the operator's data) — each judged by
reading the code at the variant head through git, the finding's own file and the callers or docs it cites, then
compared with the assessor's row.

| measure | agreement |
|---|---|
| seed identified (`seed_hit`, 5 seed hits and 15 non-hits in the sample) | **20 of 20** |
| verdict, exact (supported / partial / refuted / unresolved) | **11 of 20** |
| verdict, coarse (supported or partial, against refuted or unresolved) | **15 of 20** |

The nine exact disagreements, and which way they lean:

- **Assessor stricter (4):** a nit about a measurement script importing compiled output (assessor `refuted`: the script
  header already directs people to the npm entry that compiles; mine `supported`, low value), a hard-coded author in a
  one-developer export script (the same pair), a removed client-side timeout ceiling (assessor `refuted`: the edit form
  deliberately leaves enforcement to the service; mine `partial`), and a retention-owner finding conditional on a fix
  (assessor `refuted`: at the seeded head the directories it worries about do not exist; mine `partial`). On all four
  the assessor's reading is defensible, and all four are low-value findings either way.
- **Assessor more lenient (4):** a missing HTTP route called Blocking — the assessor's own note says the handler
  deliberately serves only `/health` and the integration is documented as deferred, yet it answered `supported`, value
  high (mine `refuted`: intended and documented — the rubric's own refutation clause); an environment read via
  `$_SERVER` (assessor `supported`, mine `partial`: the consequence on other SAPIs is unverified and the sibling platform
  variables are read the same way); a last-wins dictionary over variant files (`supported` against `partial`: the file
  that wins is the default variant, so "unintended" is not shown); an unset-knobs infrastructure finding (`supported`
  against `partial`: the comments it calls misleading do say the values are left unset).
- **Assessor stricter on a seed hit (1):** ts2-S2, the wrapped-throttle classification — assessor `partial` (frequency
  of wrapping unproven, the worker a stub), mine `supported`; both credit the seed.

**What this means for the tables.** The seed columns rest on the part the check found solid (20 of 20). The
`supported %` column is noisier — one strict reading in two disagrees on the exact grade — but the disagreements run
BOTH ways (four stricter, four more lenient) and sit mostly on low-value findings, so they blur the percentages rather
than tilt them toward a model. The model ordering the tables show is wide enough to survive that noise: on strict
`supported` grok-4.7 67 %, glm-5.3 45 %, deepseek-v4-pro 43 %, qwen3.8-max 22 %; on `supported + partial` 88 / 83 / 62 /
43 %. The glm/DeepSeek gap on strict `supported` alone (45 against 43 %) is inside that noise and is not read as a
difference. A second model assessor was not run; the check above is one further reader's judgement of 20 findings (the
agent that wrote this section, reading the code — not the operator), not a second full pass.

## Context: the earlier Codex arm F (a DIFFERENT protocol)

Not comparable row for row: arm F ran the OLD harness's packs (no per-member hunk cap, no hunk reserve, its own source
serving that refused files for the word "token" and could not resolve qualified names), `codex exec -m gpt-6-astra` with its
own 21K-token system prompt, and a LENIENT assessment (pass 4). Its numbers, for orientation: 7 cells, 7 valid, 16 calls,
21 findings — 20 supported / 1 refuted under the lenient rubric, value 7 high / 11 medium / 2 low / 1 none, severity 14 fair /
7 overstated; **7 of 14 seeds hit, 5 of 8 cross-epic**; turns used 1/4/1/1 (1/2/3/4); CLI wall turn 1 p50 14 s; tokens
1,324,612 in / 8,226 out / 101,504 cached; cost unknown (a subscription CLI).

## Recommendation

The comparison, final attempt per cell (84 reviews, 21 per model, 7 tasks × 3 repeats), under the fairness terms
stated above — every model's effort was tuned to the 20-minute deadline, not to quality (grok-4.7 and qwen3.8-max run
`medium`, deepseek-v4-pro and glm-5.3 `high`), and the calibration tasks are reported apart in the per-task table.

| | grok-4.7 | glm-5.3 | qwen3.8-max | deepseek-v4-pro |
|---|---|---|---|---|
| valid (final) · attempts failed | 21/21 · **8 of 29** (xAI HTTP 500) | 21/21 · 0 | 21/21 · 0 | **20/21** (1 unparseable after its repair) · 1 |
| seeds hit per run, mean (range) | **1.52** (1–2) | 1.00 (0–2) | 0.67 (0–2) | 0.57 (0–2) |
| distinct seeds of 14 (cross-epic of 8) | **12 (7)** | 9 (5) | 6 (4) | 4 (2) |
| seeds outside the two easy tasks, of 30 | **20** | 9 | 3 | 0 |
| strict supported % · supported + partial % | **67 · 88** | 45 · 83 | 22 · 43 | 43 · 62 |
| refuted findings per run | 0.52 | 0.43 | **2.81** | 0.52 |
| high-value findings per run | **2.33** | 1.00 | 0.57 | 0.43 |
| severity overstated % | **15** | 23 | 21 | 44 |
| review minutes p50 / p90 (max) | 9.6 / 15.4 (17.5) | 5.6 / 15.5 (**18.9**) | 9.4 / 13.3 (17.3) | **5.5 / 8.7** (9.9) |
| cost per run · per seed hit | $0.54 · $0.36 | **$0.27 · $0.27** | $0.36 · $0.54 | $0.29 · $0.50 |
| cache % of prompt tokens (turn 1 always cold) | 50 | 59 | 62 | 64 |

(Alibaba costs are Token-Plan credits at list price — an equivalent, not the bill; glm-5.3 is priced from a proxy row.
grok's cost is xAI's own bill to the cent. The $1.40 grok's eight failed attempts cost is in no per-model figure, only in the spend below;
DeepSeek's one failed review ($0.29) is that cell's final attempt, so it stays in DeepSeek's figures.)

**Recommendation: tick grok-4.7 and glm-5.3 as the feature reviewers; do not tick qwen3.8-max or deepseek-v4-pro.**

- **grok-4.7 — the strongest reviewer, and the costliest and slowest to run.** It leads every quality column at once:
  1.52 seeds a run, 12 of 14 distinct seeds, 20 of the 30 hard seed-repeats (the others' best is 9), two thirds of its
  findings strictly supported and 2.33 high-value findings a run — and it leads on the five untouched tasks too
  (1.47 seeds a run), so the lead is not a calibration artefact. The trade-offs are real: the highest cost per run
  ($0.54, twice glm's), the slowest median (9.6 min), three reviews of 21 past the operator's ~15-minute margin
  (php1 15.4 and 17.5, ts2 16.2 — inside the cap, not "always with margin"), and xAI's transient 500 on **8 of 29
  attempts**. That last one is fixed on our side (§9.29: the turn is retried inside the cap), but the retry has so far
  run only against a stub — the eight re-runs met no 500 — so a live retry is unobserved, and a retried turn spends the
  same cap. Its cost per seed ($0.36) is second-lowest: the price buys findings, not padding.
- **glm-5.3 — the cheapest, the second-best reviewer, and a second vendor.** Half grok's price per run and the lowest
  cost per seed ($0.27), 83 % of its findings supported or partly so, 9 distinct seeds including two found only by it
  and grok (tsx2-S1 every repeat, the other on-request seed once), 21 of 21 valid. What it gives up: many of its findings
  are partly right (1.67 partial a run, the most of any model), strict support is 45 %, and it misses seeds grok finds
  (ts2-S1 found once in three repeats, py3-S1 never). Its time is task-dependent: a 5.6-minute median but php1 took 15.5–18.9 minutes on
  all three repeats (68–85K reasoning tokens) — 1.1 minutes from the cap. If a feature of that shape recurs, glm is the
  reviewer that could be cut. Its one repair (an extra call) in 21 reviews is counted in its time and cost. Pairing it
  with grok puts a second vendor behind the gate: on the day xAI answered 500s, the Alibaba route answered 24 of 24.
- **qwen3.8-max — not recommended.** Reliable (21 of 21) and mid-priced ($0.36), but the assessment refuted more than
  half its findings (2.81 a run; strict support 22 %), so each review hands the person roughly three wrong claims to
  disprove for one right one; 0.67 seeds a run and the highest cost per seed ($0.54). It was also tuned down to
  `medium` (its default tier never answered inside the deadline), and what the higher tier would do cannot be said from
  this measurement — the recommendation is about the configuration that fits the deadline.
- **deepseek-v4-pro — not recommended as a reviewer.** The fastest (5.5 / 8.7 minutes, never above 10) and the most
  repeatable, but it says little and misses what matters: 1.76 findings a run, every one of its 12 seed hits on the two
  tasks every model solved, none of the ten harder seeds in any of 30 chances, 44 % of its supported-or-partial
  findings overstated in severity, and one review lost to an unparseable answer after its repair. Its speed does not
  buy coverage.

**Trade-offs stated plainly.** Ticking grok alone would give the best reviews at the highest cost and one vendor's
transients; ticking glm alone would halve the cost and keep a 100 % valid rate at the price of roughly a third fewer
seeds and more partly-right findings. Two reviewers also cover a real weakness of every single model: only 38–52 % of a
model's issues reappear in a second repeat of the same review, so a second, different reviewer adds findings, not
duplicates. Neither ticked model is fast: plan on ~10 minutes a feature round with both running, up to the 20-minute
cap on a large or reasoning-heavy feature. Unmeasured and therefore not claimed: grok at `low` or `xhigh`, qwen at its
default tier, any model on a feature larger than ts2, and whether a two-message layout would let xAI cache more than
turn 1's prefix.

**Spend.** Phase 1: $6.87 over 19 calibration iterations; the effort and ceiling probes: $0.53; phase 2: $31.97 over 93
attempts, of which $1.68 on the nine failed ones — **$39.4 in all**: $17.76 billed by xAI and $21.6 of Alibaba Token-Plan
credits at list price. The assessment ran on a Codex subscription and is not metered here.

## Defects found on our side (also recorded in PLAN_feature_review.md §9)

Each was seen RED with the real symptom before the fix and GREEN after; each has a revert check (behaviour reverted with the
API kept, the test red again). Numbers continue PLAN_feature_review.md §9.

23. **The api shim never read `finish_reason`.** A completion the vendor cut at the token ceiling (`finish_reason: "length"`)
    was taken for the answer when any content arrived — `{"` and `{"findings":[]}` alike — and reported as "returned no
    message content" when none did, hiding the 16,382 reasoning tokens that explained it. Now: `LocalAsk.ReadAnswer` reads the
    finish reason and `completion_tokens_details.reasoning_tokens`; `--ask-api` exits 70 with "cut at the token limit
    (max_completion_tokens N): T tokens generated (R reasoning tokens), C characters of content arrived" and does NOT write the
    fragment; an empty content names its reasoning tokens. Tests: `AskApiModeTests.AnAnswerCutAtTheTokenLimit_IsAFailure_EvenWhenTheFragmentParses`,
    `AReasoningOnlyAnswer_Exits70_AndStillDeclaresItsTokensOnStdout`.
24. **A failed api call's cost stopped at stdout.** `--ask-api` printed no usage on exit 70, and `ReviewerExecutor.LaunchAsync`
    answered `Usage.None` for every non-zero exit before asking the adapter — so a reasoning-only answer (53,092 prompt tokens,
    $0.20) was written down as free, on turn 1 and on turn 2 alike. Now: the usage line goes out BEFORE the content is judged,
    the launch reads usage through the adapter first, `NonZeroExit` carries it, and `UsageLedger`/`LiveRound` count it. Tests:
    `ReviewerExecutorTests.ANonZeroExit_KeepsTheUsageTheVendorReported`, `ApiShimScenarioTests.AReasoningOnlyAnswer_IsAFailedReviewer_WhoseTokensAndCostReachTheLedger`
    (the real binary, shim → executor → ledger), `AReviewerThatAsksForSourceIsAskedAgainTests.AFailedSecondTurn_ThatWasBilled_KeepsBothTurnsSpend`,
    `LedgerAndEvidenceTests.AFailedProcess_ThatReportedUsage_IsRecordedWithIt`. The fake CLI gained an `emit-exit` verb.
25. **xAI's reasoning tokens were not billed.** xAI reports them OUTSIDE `completion_tokens` (grok-4.7: prompt 19,681 · completion
    1,557 · reasoning 27,728 · total 48,966) and prices them as output; the ledger read `completion_tokens` and put the call at
    $0.047 against the vendor's own $0.213. Now: output = `total_tokens − prompt_tokens` when that exceeds `completion_tokens`
    (the Alibaba route, which counts reasoning inside the completion, is unchanged by the rule). Test:
    `AskApiModeTests.ReasoningTokensReportedOutsideTheCompletion_AreBilledAsOutput`.
26. **A review whose EVERY finding was rejected in normalisation was a clean pass.** The Alibaba route does not enforce a strict
    `json_schema`; an answer of N findings with an invented severity parsed to `Success` with an empty list and N rejections —
    a `proceed` nobody gave. Now `ReviewParser` answers `Malformed` naming every rejection (the repair's job); one survivor is
    still a review. Tests: `ReviewParserTests.EveryFindingRejected_IsMalformed_NotAnEmptyReview`, `OneSurvivorAmongRejections_IsStillAReview`;
    `ADocumentFindingFitsTheWireTests.AnInventedCategory_IsStillRejectedByName` moved to the new contract.
27. **No cache routing for xAI.** Every follow-up turn resends a byte-identical prefix (D25) and xAI cached 1,152 tokens of it
    on every turn — its cache entries are per server and a conversation reaches its server only through the `x-grok-conv-id`
    header, which nothing sent. Now: `ApiDialect.CacheKeyHeader` (data, per row), `ConversationKey.Of(provider, role, base prompt)`
    set by `RosterBuilder` on every launch of one reviewer (turns and repairs), `--conversation` on the shim's argv, the header on
    the request. Tests: `ApiDialectsTests.TheXaiRow_IsWrittenFromTheMeasuredAnswers`, `AskApiModeTests.TheXaiDialect_SendsTheConversationKey_InTheRoutingHeader`
    (+ the two negatives), `ApiRuntimeTests.TheConversationKey_TravelsOnArgv_WhenTheRosterSetOne`,
    `AnApiReviewerIsPricedAndKeyedTests.A_round_gives_each_reviewer_one_conversation_key_for_every_launch_of_it`.
28. **Every api row inherited the LOCAL engine's token ceiling** (`COAI_LOCAL_MAX_TOKENS`, 8,192) and no dialect could say what
    its family needs, nor send a vendor field no standard name spells. On the Alibaba route the ceiling bounds reasoning PLUS the
    answer, so 8,192 (and the first trial's 16,384) cut every GLM-5.3 and Qwen3.8-max answer before a character of it was
    written. Now: `ApiDialect.MaxTokensFloor` (a configured ceiling below it is raised to it, `CeilingFor`) and
    `ApiDialect.ExtraBody` (vendor fields sent verbatim at the top level — DashScope's `enable_thinking` / `thinking_budget`),
    both data in `shared/api-dialects.json`. Tests: `ApiDialectsTests.ARowsFloor_RaisesACeilingBelowIt_AndLeavesAHigherOneAlone`,
    `ARowsExtraBodyFields_AreSentVerbatim_AtTheTopLevel`, the refusals by name.

Observed, not fixed here (recorded for a follow-up): `SourceResolver` cannot resolve a member of a JavaScript object literal
(`claudeAdapter.encode` — the outline lists only the top-level `claudeAdapter`), so a reviewer asking for it by name is refused;
the server reads `COAI_CREDS_KEY` from the process environment only, while every other setting is layered from `settings.json`.

29. **A vendor's transient 500 was a lost review.** Phase 2, 2026-09-27: xAI answered 8 of 20 grok-4.7 reviews' calls with
    HTTP 500 `{"code":"internal","error":"Auth context expired."}` — on first calls and later ones, 74–262 s into the
    generation, with the same conversation key, body and concurrency as the calls that succeeded before and after (the
    tap records rule out the key, the shape and our concurrency; the Alibaba models were 24 of 24 valid in the window).
    `AskApiMode` reported any 500 as a failed request (exit 70) and the scheduler's ladder retries only what
    `RateLimit.Hit` recognises (429/503 by status, or an observed phrase), so the review was lost. RED: a 500 with that
    body expected 75, found 70, on every row. Fix: `ApiClassification` treats a 5xx whose own error field says "auth
    context expired" as a transient (exit 75, "HTTP 500 Internal Server Error from …: {body}"), `RateLimit.Phrases`
    gains the observed phrase, and the ladder retries the turn inside the 20-minute cap (it wraps each turn's launch in
    `TurnLoop`, which trims every turn to what is left of the cap). Any other 500 stays a failed request. The eight
    cells were re-run as attempt 2 from the fixed binary; the failed attempts stay on the record. **What the re-runs
    ran:** the product at `5d73ead7` (every later commit on the branch before the fix touches only this results document)
    plus the fix as an uncommitted diff — the records carry `product_dirty_files: 7` and HEAD shas `98acfa74` / `547bfc09`,
    both docs-only descendants of `5d73ead7` — built with `dotnet build -o` into a separate copy (`calib/rerun-bin/coai-mcp.exe`,
    sha256 `4173c4777bbafed2b8b81a50097772c22a078acf3393c86ed86530f160ca7799`, built 16:31 UTC after the last edit of the three
    source files at 16:30 UTC), so the in-flight Alibaba runs on the worktree's binary were untouched. The diff was committed
    unchanged after the runner ended (`8612f076`), with every suite green: CoaiMcp.Tests 6,179 passed / 6 skipped, CoaiServer.Tests
    334, CoaiBugs.Tests 317 / 2 skipped, CoaiBench.Tests 117, the extension's lint and 4,643 tests. All eight re-runs were valid and
    none met a 500, so the ladder's retry of this answer is proven by the tests, not yet by a live call.
