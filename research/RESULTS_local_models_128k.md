# RESULTS — two local models at 128k, plan and code, against the hosted baseline

> Run 2026-09-02 on this machine (Ollama 0.33.2, one 32 GB card), through the real `coai-mcp` over
> stdio, on the SAME subjects the thirteen hosted models were measured on:
> [RESULTS_model_comparison.md](RESULTS_model_comparison.md) for the plan half and
> [RESULTS_model_comparison_code.md](RESULTS_model_comparison_code.md) for the code half.
>
> **8 cells, all completed.** Raw output: `C:/Users/strug/gemma128` and `C:/Users/strug/qwen128`.

## What had to be true first

Both models had failed this baseline earlier the same day, and neither failure was about the model:

1. **A local reviewer never ran at all.** Three places decided what a vendor is; two knew about
   `local` and the third did not, so a local vendor was read as a codex one needing a vault key,
   answered `unavailable`, and was dropped from the round — `0 reviewer(s)`, while `providers`
   reported it healthy. Fixed in mcp 0.11.1.
2. **Thinking ran until the context ran out.** Gemma answered once in 171 s and once spent 1056 s
   filling 64k with 110 000 characters of `reasoning` and returned an empty `content`; Qwen never
   answered at all. `reasoning_effort: "none"` — the one field Ollama's `/v1` route honours, found in
   `dew_flow_rag_qln` three weeks earlier — is the default from mcp 0.11.2.

## The plan half: five of eight, twice, for both

Same 40-line nightly-export plan, same eight planted defects, one round, threshold 99, two runs.
**Scored by reading**, each finding credited to at most ONE defect — the keyword pass was thrown away
in the hosted campaign for crediting a single finding as two defects, and the same discipline applies
here.

| model | run 1 | run 2 | mean | spread | wall | tokens in/out |
|---|---|---|---|---|---|---|
| Claude Opus 4.6 / Sonnet 4.6 | 7 | 7 | **7.0** | 0 | 83–98 s | — |
| GPT-5.5 | 7 | 6 | 6.5 | 1 | 22 / 30 s | — |
| **Qwen3.5 35B-A3B, local 128k** | **5** | **5** | **5.0** | **0** | 40 / 14 s | 1 517 / 1 024 |
| **Gemma4 26B-A4B, local 128k** | 4 | **5** | 4.5 | 1 | 38 / 11 s | 1 572 / 800 |
| Gemini 3.7 Flash (Medium) | 4 | 4 | 4.0 | 0 | 38 / 28 s | — |
| GPT-5.4-Mini | 4 | 3 | 3.5 | 1 | 88 / 52 s | — |

**Both local models find the same five defects** — D1 (delete before load), D2 (password on the
command line), D4 (unbounded retry), D6 (whole ledger in memory), D8 (nothing makes the load
reversible). Gemma's first run is a 4 only because its fifth finding, *"verification occurs after the
destructive step"*, restates D1's risk rather than naming D8; its second run says the same thing as
*"if the rename fails the next night reads the old data"*, which does.

**Both miss the same three**, and they are the three that need the reviewer to imagine the job
running rather than read it: D3 (the table created after the steps that write to it), D5 (the ledger
renamed while the server is still appending), D7 ("fast" with nothing measuring it).

**Qwen's two runs are byte-identical** — same five findings, same order, same 1 517 / 1 024 tokens.
Gemma's are not. At `temperature 0` with a fixed seed that is a property of the model, and for a gate
it is worth something on its own: a verdict that changes between runs is a verdict a developer cannot
trust.

## The code half: nine findings each, both runs, both models

Commit `939175d` over `4b49cfc` — 19 files, 772 insertions — three roles per model, round 1 pinned to
each role's universal prompt.

| model | run 1 | run 2 | wall | tokens in (3 roles) | per reviewer |
|---|---|---|---|---|---|
| Gemma4 26B-A4B, local 128k | 9 | 9 | 86 / 54 s | 76 999 | ~25.7k |
| Qwen3.5 35B-A3B, local 128k | 9 | 9 | 103 / 60 s | 70 418 | ~23.5k |
| Gemini 3.7 Flash (High) / 3.1 Pro | 11 | — | 294 / 574 s | ~290k | — |
| GPT-5.6 family | 8 | 8 | 135–168 s | ~210k | — |

**No planted defects here, so this measures throughput, not judgement** — the same caveat the hosted
code half carries. What it does show is that both local models are in the hosted models' band on
count and FASTER than most of them on wall clock.

**The two models found different things, and both found a real one.** Gemma: a sequential `await` in
a loop, a race in the version cache, zombie processes on a slow CLI check. Qwen: no timeout on the
version fetch, a hang if the CLI hangs, the update button's cache racing with the install. The
missing timeout on the network fetch is genuine and was independently raised by codex in an earlier
round on the same file — two local models and one hosted model converging on it is the strongest
signal available here.

## Context: 128k is headroom, not a requirement

The prompt actually sent per reviewer is **~25k tokens** — measured, not estimated: 76 999 across
three roles for the larger of the two models.

| | tokens |
|---|---|
| plan prompt | ~1 500 |
| code prompt, per reviewer, this diff | **~25 000** |
| code prompt worst case at the shaper's own cap (`DiffShaper.DefaultMaxBytes` = 192 KB) | ~50–60 000 |
| the models' 64k build | 65 536 |
| the models' 128k build | 131 072 |

So 64k already covered this work and 128k covers the shaper's worst case twice over. Above that there
is nothing to put in: the ceiling is this product's own diff budget, not the model. Qwen at 128k is
resident at **26.4 GB** on a 32 GB card — the cost of the headroom is about 1.6 GB over its 64k build,
because both architectures use sliding-window attention and keep a full-length KV on a minority of
layers only.

An earlier note in this repository said the hosted code stage sends ~205k tokens and implied a local
model would need the same. That was wrong and is corrected here: 205k is what **codex** spends
exploring the repository on its own. The local runtime sends one prompt and explores nothing.

## What this changes

**A local reviewer is now a real option for the plan gate.** Five of eight, both runs, in 11–40
seconds and at no per-token cost, is between Gemini 3.7 Flash and GPT-5.5 — and Qwen delivers it
identically twice.

**It is not a replacement for the top of the table.** Opus and Sonnet score 7 of 8 twice. The three
defects both local models miss are the ones that need the plan to be imagined in motion, and that is
the gap a second vendor is bought for.

**The honest recommendation:** a local model as the SECOND or third reviewer, where its cost is zero
and its disagreement with a hosted model is the signal; a hosted model as the first, where the
missed-defect risk is paid for. For a machine with no paid vendor at all, five of eight in under a
minute is a working gate rather than none.
