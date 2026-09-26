# RESULTS — the API vendors probed (S0.5), and a first model trial that measured our plumbing

> Status: **measured 2026-09-26; the model comparison below is SUPERSEDED** by a per-vendor calibration
> and a three-run measurement on the product path (`RESULTS_feature_reviewer_models.md`, in progress).
> This record keeps what the probe established and why the first comparison was withdrawn.
>
> Related docs: [PLAN_feature_review.md](../todo/PLAN_feature_review.md) (S1.2 (ii)/(iii), D25, S3.7, §9.10–12),
> [RESULTS_feature_pack_trial.md](RESULTS_feature_pack_trial.md) (the pack and protocol this trial reused).

Numbers, findings and method only. The prompts and answers are NOT committed: they carry code from the
operator's private repositories, and this repository is public.

## 1. The vault path, measured

- The operator stored `{"grok": …, "qwen": …}` as a CredsForDevs **config** entry and put its access key in
  the panel (`COAI_CREDS_KEY`).
- `coai-mcp --providers` still answered `no COAI_CREDS_KEY configured`. A server run from a shell does not
  get the panel's key. The one VS Code launches does.
- With the key in the environment, the vault read failed with "the `creds` CLI is not installed". The
  CredsForDevs extension ships `creds.exe` in its global storage (`…\remsoftdev.creds-for-devs\bin\`), and
  the folder on PATH holds only `creds-mcp.exe`. This became §9.10 of the plan and is fixed on the epic 3
  branch (`KeyVault.ForThisMachine` looks in the extension's bin after PATH).

## 2. What each key can call (`--probe-api`, `GET /models`)

| key | endpoint | models offered (review-relevant) |
|---|---|---|
| `grok` | `https://api.x.ai/v1` | grok-4.20-0309-(non-)reasoning, grok-4.20-multi-agent-0309, grok-4.3, grok-4.5, grok-4.6, **grok-4.7**, grok-build-0.1 (+ image/video models) |
| `qwen` (Token Plan, Singapore) | `https://token-plan.ap-southeast-1.maas.aliyuncs.com/compatible-mode/v1` | **qwen3.8-max**, qwen3.8-flash, qwen3.7-max, qwen3.7-plus, qwen3.6-flash, **deepseek-v4-pro**, deepseek-v4-flash-0731, deepseek-v4.1-flash, glm-5.2, **glm-5.3**, auto (+ audio/image models) |

## 3. The request matrix (S0.5), synthetic 40-line diff, no repository code

| field | grok-4.7 | qwen3.8-max |
|---|---|---|
| `response_format` json_schema / json_object | 200 / 200 | 200 / 200 |
| temperature, seed | 200, 200 | 200, 200 |
| frequency_penalty | **400** "does not support parameter frequencyPenalty" | 200 |
| reasoning_effort low / medium / high | 200 each | 200 each |
| second turn | 200 | 200 |
| wrong key | **400** "Incorrect API key provided" (not 401 — §9.11) | 401 |
| cached tokens on a 2.3K-token prompt | 1,152 of 2,336 on the first call | 0 (below any caching threshold) |
| wall time, whole matrix (10 requests) | 44 s | **408 s** |

## 4. The metrics the product path returns (`--ask-api`, one tiny review each)

| model | exit | wall | tokensIn | tokensOut | tokensCached |
|---|---|---|---|---|---|
| grok-4.7 | 0 | 11.4 s | 1,593 | 95 | 1,152 |
| qwen3.8-max | 0 | 50.9 s | 111 | 1,510 | 0 |
| deepseek-v4-pro | 0 | 8.9 s | 54 | 518 | 0 |
| glm-5.3 | 0 | 9.3 s | 61 | 809 | 0 |

Tokens and time arrive for every vendor. **Cost does not**: an OpenAI-compatible response carries no price.
That became S3.7 (price per million for input, cached input and output on the row; cost computed per turn).

## 5. Prices, and the lookup coai already had

`src_vs_code/src/modelPrices.ts` (OpenRouter, then LiteLLM) was the right source, with three defects for
these routes (§9.12 and S3.7):
- it asks OpenRouter first, whose number is OpenRouter's resale price, not the vendor's;
- `priceKey` strips `-max` as a reasoning effort, so `qwen3.8-max` found no price;
- it reads no cached rate.

The route-correct list prices (USD per 1M, LiteLLM provider rows, fetched 2026-09-26):

| model | input | cached input | output | row |
|---|---|---|---|---|
| grok-4.7 | 2.00 | 0.50 | 6.00 (every rate doubles from 200K prompt tokens) | `xai/grok-4.7` (OpenRouter: 1.60 / 0.40 / 4.80) |
| qwen3.8-max | 2.00 | 0.25 | 6.00 | `dashscope/qwen3.8-max` |
| deepseek-v4-pro | 2.40 | 0.20 | 4.80 | `dashscope/deepseek-v4-pro` (OpenRouter: 0.35 / 0.03 / 0.70) |
| glm-5.3 | 1.40 | 0.26 | 4.40 | `zai/glm-5.3`, a proxy: no dashscope row |

The three Alibaba models are billed as **Token Plan credits**, so their cost is a list-price equivalent,
not the bill.

## 6. The first model trial — withdrawn

**Setup:**
- Protocol: 4 models × the 7 seeded tasks (C#, Rust, JS, TS, Python, TSX, PHP), with arm F's protocol
  (hybrid pack, seam prompt, severity calibration, up to 3 follow-up turns).
- Transport: the product's `--ask-api` at `--max-tokens 16384`.
- Packs and source serving: from the earlier trial's harness.

| model | valid finals | median wall per cell | tokens in / out / cached | list-price cost, 7 cells |
|---|---|---|---|---|
| grok-4.7 | 7 / 7 | 1,008 s (always 4 turns) | 1,780K / 15K / 263K (14 %) | $3.26 |
| deepseek-v4-pro | 7 / 7 | 174 s (2 turns) | 1,197K / 117K / 690K (57 %) | $1.92 |
| qwen3.8-max | 3 / 7 | 1,201 s | 599K / 129K / 393K (65 %) | $1.29 |
| glm-5.3 | 0 / 7 | — | 907K / 229K / 457K (50 %) | $1.76 |

About $0.98 more was spent on diagnostic re-runs. No assessment was run.

**Why it was withdrawn.** It measured our plumbing, not the models. Two outside critiques and the operator
said so, and the diagnosis confirmed it:

- **The token ceiling was ours.** On Alibaba, `max_completion_tokens` bounds reasoning PLUS the answer.
  - At 16,384, every glm-5.3 call used exactly 16,384 output tokens and wrote `{` or `{"`.
  - At 65,536, four calls finished with `stop`, using 21–58K completion tokens (19–56K of them reasoning),
    and three of the four answers were valid JSON.
  - qwen3.8-max's failures stop at ~16,382 tokens, mid-string, or return reasoning with no content
    (exit 70).
- **Defects the product would hit in real rounds:**
  - the default `--max-tokens` (8,192) fails both;
  - `finish_reason` is never read, so a length cut reads as a bad model;
  - exit 70 prints no usage, so billed tokens go uncounted;
  - strict `json_schema` is not enforced on the Alibaba route, and the shim does not validate the answer
    against the schema itself;
  - grok's reasoning tokens are possibly under-counted (217–1,264 output tokens against 87–454 s per
    turn; unmeasured).
- **Cache.**
  - The resent prefix was byte-identical on all 60 turns (D25 held).
  - Alibaba cached it well: repairs 99–100 %, deepseek follow-ups 94 %, glm about 90 %; turn 1 is cold.
  - xAI mostly did not: 14 of 17 grok follow-ups cached exactly 1,152 tokens. xAI routes cache hits by a
    conversation or cache key the product never sent.
- **Wrong packs and serving, and a lenient score.**
  - The packs and the source serving were the old harness's (no per-member hunk cap, no hunk reserve,
    "token" in a file name refused, qualified names unresolved), not the product's.
  - A single run per cell cannot rank.
  - "Supported" admitted findings with a wrong trigger or consequence.

**What replaced it:**
1. A calibration of each vendor alone, up to ten iterations, fixing one cause on our side at a time, on
   the product path.
2. A consultation per model on its calibration.
3. Then 3 runs × 7 tasks × 4 models with the same prompt and a strict rubric. Results are persisted after
   every run.
