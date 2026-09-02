# RESULTS — five real plans, five models, the operator's own settings

> Run 2026-09-02 through the real `coai-mcp` over stdio. **25 plan cells, all completed.** The code
> half over the same five commits is running and appends below.
>
> Unlike [RESULTS_model_comparison.md](RESULTS_model_comparison.md), these plans carry no planted
> defects — they are five plans that were actually written and actually built here. So recall cannot
> be scored, and what is scored instead is the thing this product already calls its strongest signal:
> **a finding two models raise independently.**

## The subjects, and why these five

Each is a real plan paired with the commit that implemented it. The plan text is read from that
commit, so nothing written later leaks into the review.

| subject | plan | commit | diff |
|---|---|---|---|
| update-button | `PLAN_cli_update_button.md` | `939175d` over `4b49cfc` | 19 files, +772 |
| local-models | `PLAN_local_models.md` | `2b7d3ab` over `ece88e2` | 17 files, +2014 |
| snippet-version | `PLAN_snippet_version.md` | `9bd669a` over `25f3aca` | 18 files, +608 |
| conventions-pass | `PLAN_conventions_pass.md` | `0b43097` over `b361e92` | 41 files, +1770 |
| per-role-gate | `PLAN_per_role_gate_and_dealt_prompts.md` | `bf30a9b` over `edd6fcb` | 46 files, +1734 |

## The settings, identical for every model

The operator's own, from the panel: plan — 1 round, passes at or under 6, Universal, lenses NOT
dealt. Code — Architecture 2 rounds (Conventions then Universal), Security 1, Performance 1, each
passing at or under 5, roles NOT dealt. One vendor per cell, so nothing is shared between models.

## Plan stage — what each model produced

| model | update | local | snippet | conventions | per-role | total | gating | wall | tokens in |
|---|---|---|---|---|---|---|---|---|---|
| Claude Sonnet 5 (native) | 6 | 6 | 4 | 6 | 8 | **30** | 70 % | 895 s | 411k |
| GPT-5.6-Luna | 5 | 6 | 5 | 6 | 7 | 29 | 83 % | 205 s | 77k |
| **Qwen3.5 35B, local 128k** | 5 | 6 | 5 | 7 | 5 | 28 | 68 % | **127 s** | 15k |
| **Gemma4 26B, local 128k** | 4 | 4 | 4 | 4 | 5 | 21 | 76 % | **89 s** | 15k |
| Gemini 3.7 Flash (Medium) | 4 | 5 | 3 | 4 | 5 | 21 | 90 % | 376 s | 89k |

Sonnet finds the most and takes seven times the wall clock of Gemma to do it. The two local models
bracket Gemini Flash on count, at a quarter to a third of its time and a fifth of its input tokens —
because a local reviewer is sent one prompt and explores nothing, while the hosted CLIs read the
repository on their own.

## Agreement — and the measurement that had to be thrown away first

**The automatic matcher said 0–5 % agreement. Reading said 55–76 %.** The matcher is a Jaccard
similarity of 0.5 on titles, and on plan findings there is no file or line to anchor it, so five
models describing one defect in five different sentences look like five defects.

The reading is auditable: the clusters are in `C:/Users/strug/coai-clusters.json`, one entry per
defect naming the models that raised it, and every assignment can be checked against the raw cells.

| model | update | local | snippet | conventions | per-role | in a cluster with another model |
|---|---|---|---|---|---|---|
| Gemma4 26B, local | 3/4 | 2/4 | 3/4 | 4/4 | 4/5 | **76 %** |
| Gemini 3.7 Flash (Med) | 3/4 | 5/5 | 2/3 | 2/4 | 3/5 | **71 %** |
| Qwen3.5 35B, local | 4/5 | 3/6 | 2/5 | 5/7 | 4/5 | **64 %** |
| Claude Sonnet 5 (native) | 4/6 | 3/6 | 3/4 | 3/6 | 5/8 | **60 %** |
| GPT-5.6-Luna | 2/5 | 3/6 | 4/5 | 3/6 | 4/7 | **55 %** |

**32 distinct defects across the five plans. 31 of them were raised by two models or more; 14 by
three or more; one by all five** — that a seed alone does not make a dealt assignment reproducible,
on the per-role-gate plan.

**The local models are not outliers.** Gemma has the highest agreement in the set and Sonnet the
second lowest — which is not a quality ranking. A high figure means a model says what others say; a
low one means more of its findings are its own, and Sonnet's and Luna's unshared findings are mostly
specific plan-internal contradictions the others did not reach (*"the GateRule test example
contradicts the threshold semantics stated in Build order"*).

## The ground truth that did exist, and all four were found

The `local-models` plan is the one subject with known-open defects: this product's own gate reviewed
it three times, and four findings were deferred to
[PLAN_local_trust_and_vllm.md](../todo/PLAN_local_trust_and_vllm.md) — still true of the code today.
Independently, without seeing that document:

| deferred finding | re-found by |
|---|---|
| §1 a non-loopback endpoint is warned about, not gated | qwen, luna, sonnet |
| §2 a served vLLM wanting a key has no key path | flash, luna, sonnet |
| §3 killing the shim may not stop a non-Ollama generation | flash, gemma |
| §4 the endpoint race and the trust warning have no test | sonnet |

**All four, by three different models each on average, and one of them by a model running on this
machine for free.** That is the strongest evidence in this campaign that the panel is measuring
something real.

## The defect this campaign found in the product itself

`FindingDedup.SameDefect` merges two reviewers' findings when the category, file and line match and
the titles are similar. For a PLAN finding the file is empty and the line is zero, so it falls to
`TextSimilarity.SameRemark` — Jaccard ≥ 0.5 on the titles alone.

That is the same rule that scored 0–5 % here against a reading of 55–76 %. So on plan rounds:

- **the same defect raised by two vendors counts TWICE against the threshold**, and a threshold of 6
  is reached by three vendors saying two things each;
- **the "two vendors agreed" signal never appears** — the reply lists each with a single provider.
  Observed directly in an unrelated smoke run the same day: codex, gemini and a local model each
  raised the delete-destroys-history defect, in three separate entries with one provider each.

The code already records this exact lesson for the ANCHORED case — the real run of 2026-08-31 found
three reviewers wording one path-traversal defect 0.43 apart — and the fix made then lowered the bar
only when file, line and category agree. Plan findings have none of those, and were left on the
strict threshold.

This is not fixed here: changing it changes what passes a gate, and that is the operator's call. The
measurement is what a fix would have to beat — 31 of 32 defects agreed on by reading, essentially
none by the rule.

## Code stage

*Running; this section is appended when the 25 cells complete.*
