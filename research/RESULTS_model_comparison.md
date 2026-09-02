# RESULTS — which MODEL to review with: plan stage, thirteen models, two runs each

> Status: **plan half complete, 2026-09-01; a LOCAL model added 2026-09-02.** 26 runs against the
> Release `coai-mcp` in WSL, one vendor and one model per run, driven over stdio, plus the local rows
> below. The code half is in [RESULTS_model_comparison_code.md](RESULTS_model_comparison_code.md).
>
> Related: [RESULTS_prompt_measurement.md](RESULTS_prompt_measurement.md),
> [RESULTS_conventions_prompt.md](RESULTS_conventions_prompt.md).

## Method, stated before the numbers

**One plan, eight planted defects.** A 40-line plan for a nightly export job, written for this
measurement, into which eight checkable defects were deliberately placed. Checkable is the point: it
is not a matter of taste whether a step that `DELETE`s a table before the load that refills it is a
defect.

| id | the defect |
|---|---|
| D1 | `DELETE FROM review_usage` before the load — a mid-load failure leaves finance with nothing |
| D2 | the warehouse password on a `psql` command line |
| D3 | step 5 creates the table that steps 2–4 already write to |
| D4 | retry forever, ten seconds apart, no cap and no backoff |
| D5 | the ledger is renamed while the server is still appending to it |
| D6 | the whole ledger is read into memory, and it grows without bound |
| D7 | "fast" and "queryable by 08:00" with nothing that measures either |
| D8 | nothing makes the load idempotent or reversible |

**One cell = one vendor, one model, one round, threshold 99** — nothing gates, so every finding is
reported rather than stopping the stage. **Every model was run twice.**

**Scored by reading.** A keyword pass found candidates and was then thrown away as a score: it
credited Opus's "infinite retry loop" finding as *both* the password defect and the memory defect,
which is the exact failure this repo's earlier measurement recorded. Every finding is credited to at
most ONE defect, the one it most specifically names.

## The table

Both runs, best and mean, with the wall clock and what the tokens would have cost at published API
rates. (Reviews here run on a subscription; the money column is an order of magnitude, not a bill.)

| model | run 1 | run 2 | mean | spread | sec (1 / 2) | ~$ per run |
|---|---|---|---|---|---|---|
| **GPT-5.5** | 6 | **8** | **7.0** | 2 | 22 / 30 | $0.070 |
| **Claude Opus 4.6** | 7 | 7 | **7.0** | **0** | 83 / 98 | $0.165 |
| **Claude Sonnet 4.6** | 7 | 7 | **7.0** | **0** | 78 / 78 | $0.114 |
| GPT-5.6-Sol | 6 | 7 | 6.5 | 1 | 27 / 30 | $0.105 |
| GPT-5.4 | 5 | 7 | 6.0 | 2 | 27 / 36 | $0.045 |
| GPT-5.6-Luna | 7 | 5 | 6.0 | 2 | 29 / 28 | $0.004 |
| GPT-OSS 120B | 5 | 6 | 5.5 | 1 | 26 / 102 | $0.002 |
| Gemini 3.1 Pro | 6 | 5 | 5.5 | 1 | 53 / 70 | $0.100 |
| Gemini 3.7 Flash (Low) | 6 | 5 | 5.5 | 1 | **11 / 23** | $0.020 |
| Gemini 3.7 Flash (High) | 5 | 5 | 5.0 | 0 | 44 / 32 | $0.050 |
| GPT-5.6-Terra | 5 | 4 | 4.5 | 1 | 21 / 28 | $0.030 |
| Gemini 3.7 Flash (Medium) | 4 | 4 | 4.0 | 0 | 38 / 28 | $0.053 |
| GPT-5.4-Mini | 4 | 3 | 3.5 | 1 | 88 / 52 | $0.036 |
| **Gemma4 26B-A4B, local** (Ollama, 64k) | 4 | **—** | *(4)* | — | 171 / **1056, no answer** | electricity |
| Qwen3.5 35B-A3B, local (Ollama, 64k) | — | — | — | — | 1027, no answer | electricity |

## The local rows, and why one of them is a dash

Two models on this machine were run on the same plan, through the same server, by the same
harness — the local runtime added on 2026-09-02, reaching Ollama's OpenAI-compatible route with the
finding schema as `response_format`, `temperature` 0 and a seed. What it took to get even one number
is most of the result.

**Gemma4 26B-A4B (Q5_K_M, 64k context) — 4 of 8, once.** Run 1 answered in 171 s with four findings,
and every one of them was a planted defect: D1 (the `DELETE` before the load), D2 (the password on
the `psql` command line), D3 (the table created after the steps that use it), D6 (the whole ledger in
memory). It missed the retry loop, the rename under a live writer, the unmeasured "fast", and
idempotency — the four that need the reviewer to imagine the job RUNNING rather than read it. Four
puts it level with Gemini 3.7 Flash at medium effort and above GPT-5.4-Mini, at six to fifteen times
their wall clock.

**Run 2 produced nothing after 1056 seconds.** Not a crash: the engine returned HTTP 200 with an
empty `content`. Reproduced by hand with the identical request, the failure has a shape —
`finish_reason: length`, `completion_tokens` filling the entire context, 110 000 characters in the
model's `reasoning` field and none in `content`. The same request, same seed, same temperature had
answered twenty minutes earlier after 41 000 characters of reasoning. **This model's thinking is
unbounded and not reproducible**, and when it outruns the context there is no answer at all; raising
the context from 32k to 64k moved the cliff and did not remove it (1056 s of thinking against 508 s).

**Qwen3.5 35B-A3B (Q5_K_M, 64k) — no row at all.** 1027 seconds, no content. Two things separate it
from Gemma, both measured directly: it generates at roughly three tokens a second even fully in VRAM,
and it puts its chain of thought INTO `content` rather than a separate field — asked to return
`{"ok":true}` and nothing else, it returned 300 tokens of *"Verify Constraints… Draft Output…
Self-Correction on Markdown"* and hit the length cap before the JSON. A reviewer that cannot finish
one line in under a minute cannot finish a review.

**What cannot be switched off.** Ollama 0.33.2's `/v1` route silently ignores both `think: false`
and `reasoning_effort: low` — verified: the two requests produced byte-identical answers with the
same 6 250 characters of reasoning. Bounding a reasoning model on this route is not possible from the
client; it needs Ollama's native `/api/chat`, which the runtime does not use because vLLM does not
serve it. That is an open item in
[PLAN_local_trust_and_vllm.md](../todo/PLAN_local_trust_and_vllm.md).

**The code half was not attempted.** The hosted models were sent about 205k input tokens for it;
this model's context is 64k, and Ollama truncates a prompt to `num_ctx` without saying so — a review
of the diff's first third reported as a review of the diff. Refusing to run it is the honest result;
detecting that truncation is the other open item in the same plan.

**What the local rows say, read together.** A model on this machine can score in the hosted table's
lower half and cost nothing per token. What it cannot yet do is finish reliably: one of two runs, and
the failed run took six times longer than the successful one. For the plan gate, where the hosted
models answer in under a minute, that is not a trade anybody would take today; for a machine with no
paid vendor at all, it is four real defects found for the price of a busy card.

## What the second run changed — read this before the table above

## What the second run changed — read this before the table above

The second pass was run because two results from the first looked like findings and might have been
noise. It refuted one of them outright and weakened another. Both corrections are worth more than
anything the first pass appeared to show.

**REFUTED: "no codex model finds the build-order defect."** After the first pass, none of the six
codex models had found D3 (the table created after the steps that use it) while four of seven
antigravity models had. It was the sharpest result in the set and the easiest to build a story on —
different vendors, different blind spots. In the second pass **four codex models found D3**
(5.4, 5.5, 5.6-Luna, 5.6-Sol). The pattern was a one-run artefact.

**WEAKENED: "GPT-5.6-Luna matches Opus at a fortieth of the price."** Luna scored 7 then 5; Opus
scored 7 twice. On the mean they are 6.0 against 7.0, and Luna's spread is the widest in the set.
The price difference is real and large; the parity was not.

**And the variance is bigger than the first three controls suggested.** Those three came back ±1,
which made a difference of 2 look meaningful. With every model run twice, three models moved by 2
(GPT-5.5, GPT-5.4, Luna). **Treat anything under a 2-point gap as a tie**, and treat a single run as
evidence of nothing.

## What survives two runs

- **Three models are at the top and they got there differently.** Opus and Sonnet scored 7 twice —
  no spread at all — while GPT-5.5 reached 8 once and 6 once. For a gate, the stable one is worth
  more than the occasionally brilliant one: a review you cannot predict is a review you have to
  double-check.
- **Nobody found all eight, twice.** One run of GPT-5.5 found all eight; no model did it repeatably.
  D7 (unverified promises) and D8 (no rollback) are the two that go missing most, and they are the
  two that read as judgement rather than as a rule being broken.
- **Cost and quality are barely related in this range.** GPT-OSS 120B at $0.002 scores 5.5; Gemini
  3.1 Pro at $0.100 scores 5.5. Opus costs 80× GPT-5.5's cheapest sibling and scores the same as
  GPT-5.5.
- **Reasoning effort did not order the way its name does.** Gemini 3.7 Flash at LOW effort (5.5)
  beat the same model at MEDIUM (4.0) and matched HIGH (5.0), across four runs. This is the one
  result the second pass strengthened rather than weakened, and it has no explanation here.

## The recommendation

**For the plan gate, two vendors: Claude Sonnet 4.6 and GPT-5.5.**

Sonnet for stability — 7 and 7, no spread, and the highest finding count in the set at a third of
Opus's price. GPT-5.5 as the second vendor because it is the other model that reached the top and it
is a genuinely different family; its own instability is the argument for pairing it with a stable
one rather than for dropping it.

**Opus 4.6 only where a missed defect is expensive.** It matches Sonnet's score at 1.5× the price
and 10 % more time. It is not a mistake, it is just not a purchase this measurement can justify.

**For a cheap continuous pass: Gemini 3.7 Flash (Low).** 5.5 of 8, in eleven seconds, at $0.02 —
the fastest thing measured and half the price of its own HIGH setting. Not the gate; the thing you
can afford to run on every push.

**Avoid GPT-5.4-Mini for review.** 3.5 of 8 and the slowest of the small models in one run (88 s).

**Do not switch to one vendor.** The blind-spot story was refuted, but the reason for two vendors
survives it: the top three models each missed different defects on different runs, and the merged
panel is what covers them. What this measurement changes is *which* two, not *whether*.

## Cost of the campaign

26 plan runs, ~0.5 M input and ~0.1 M output tokens, about 20 minutes of wall clock. The two
`gpt-oss-120b` runs are the outlier in the token column (27k then 176k for the same input) — the
same prompt, the same plan, six times the input tokens on the second run. Unexplained, recorded.
