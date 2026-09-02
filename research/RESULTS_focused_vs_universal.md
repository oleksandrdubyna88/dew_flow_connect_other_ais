# RESULTS — a focused lens against the universal prompt, on one real change

> Phase B of the focused-prompt campaign, run 2026-09-02 against the real `coai-mcp` over stdio.
> **32 cells**, 79 minutes, 11.8 M input tokens, **1 failed cell** (named below). Raw output:
> `C:/Users/strug/coai-phaseb/*.json`.
>
> Phase A — which WORDING of each lens, and the shape result that came out of it — is
> [RESULTS_focused_prompts.md](RESULTS_focused_prompts.md). This is the question phase A could not
> ask: does a narrow lens find anything the section's broad prompt does not?

## The subject, and why it has a ground truth

One real change: the local-model feature — [PLAN_local_models.md](PLAN_local_models.md) as the plan,
commit `2b7d3ab` as the code. It was chosen because **four of its defects are known and still
present**. This product's own gate reviewed it three times; four findings were fixed inside the
change and four were deferred to
[PLAN_local_trust_and_vllm.md](../todo/PLAN_local_trust_and_vllm.md), which means they remain true of
the committed code:

1. a non-loopback endpoint is warned about but never acknowledged (SecurityReliability)
2. a served vLLM wanting an API key has no key path at all (SecurityReliability)
3. non-streaming cancellation may not stop a non-Ollama engine (UxDxPerformance)
4. the endpoint race and the trust warning have no test (Architecture)

So a cell can be scored on something real, which phase A could not do.

## Method, and the confound that has to be read first

Per section: one arm running the section's shipped universal prompt, one running its three phase-A
winning lenses. Two runs each, one vendor (codex), threshold pinned so nothing gates and every
finding is reported. Round 1 of the code stage was pinned to the section prompt — **without that the
whole campaign measures the conventions pass**, which is exactly the false start recorded in
[RESULTS_model_comparison_code.md](RESULTS_model_comparison_code.md) and which cost five cells here
before the log said `codex/Architecture[conventions]` out loud.

**A section has three lenses and one universal prompt, so the focused arm ran six rounds per section
and the universal arm ran two.** Every "focused found more" number below is three times the sampling,
and the first table is written per-run for that reason. This is not a flaw to apologise for — "three
narrow prompts or one broad one" is the choice a person actually makes — but it is a different
question from "which prompt is better", and the two are easy to confuse.

## The result that matters: per run, the broad prompt is not behind

| Section | Arm | runs | findings | **per run** | gating | repeated |
|---|---|---|---|---|---|---|
| PlanCritique | universal | 2 | 10 | **5.0** | 80 % | 20 % |
| PlanCritique | focused | 6 | 14 | 2.3 | 100 % | 0 % |
| Architecture | universal | 2 | 5 | 2.5 | 60 % | 0 % |
| Architecture | focused | 6 | 18 | **3.0** | 67 % | 67 % |
| SecurityReliability | universal | 2 | 6 | **3.0** | 83 % | 67 % |
| SecurityReliability | focused | 6 | 15 | 2.5 | 93 % | 86 % |
| UxDxPerformance | universal | 2 | 6 | **3.0** | 50 % | 0 % |
| UxDxPerformance | focused | 6 | 10 | 1.7 | 70 % | 14 % |

**Per round, the universal prompt finds as much or more in three sections of four.** The focused arm
wins on yield only in Architecture. What the focused arm does win consistently is **gating share**
(70–100 % against 50–83 %) and, in the two code sections where it also repeats itself, **stability**:
86 % and 67 % against 67 % and 0 %.

## What each arm found that the other did not

| Section | shared | only universal | only focused |
|---|---|---|---|
| PlanCritique | 1 | 9 | 13 |
| Architecture | 2 | 3 | 14 |
| SecurityReliability | 2 | 4 | 12 |
| UxDxPerformance | 3 | 3 | 7 |

**The overlap is tiny — one to three findings out of twenty or thirty.** That is the strongest result
here and it survives the sampling confound, because it is about WHICH findings, not how many: the two
arms are largely not looking at the same things. A section's broad prompt and its narrow lenses are
not two ways of asking one question.

It also means the arms cannot be ranked by these columns. "Only universal: 9" in PlanCritique is nine
findings three lenses never reached, from a third of the rounds.

## The four known defects, verified by reading rather than by matching

The automatic matcher over-reported, and the correction is the point of this section. Every claimed
hit was read against the finding it claimed:

| Known defect | universal | focused | |
|---|---|---|---|
| 1. non-loopback endpoint never acknowledged | **FOUND** | **FOUND** | both, repeatedly and in the right words |
| 2. a served vLLM has no key path | — | — | the matcher scored "endpoint credentials are exposed through process arguments" as a hit. It is a different defect — leaking a key, not the absence of a way to supply one |
| 3. non-streaming cancellation on a non-Ollama engine | — | — | the matcher scored "very short reviewer timeouts erase the shim's terminal error message". Also real, also not this |
| 4. the endpoint race has no test | — | **FOUND** | `arch-testability` named it twice: *"the endpoint-keyed cache and in-flight race decision is trapped inside a VS Code provider with global configuration and clock dependencies"* |

**Universal 1 of 4, focused 2 of 4, and neither found half.** Two defects that are provably still in
that code were missed by twenty-two reviewer rounds across both arms — which is the most useful
sentence in this document, and the one that would have been hidden by trusting the keyword matcher.

**The one clean win for a lens** is defect 4: `arch-testability-B` reached the untested race, and the
broad Architecture prompt did not, in either run. That is a lens doing exactly what a lens is for.

## Per lens

| Section | Lens | r1 | r2 | gating | repeated |
|---|---|---|---|---|---|
| PlanCritique | plan-data-loss | 0 | 0 | — | — |
| PlanCritique | plan-operability | 3 | 3 | 100 % | 0 % |
| PlanCritique | plan-scope-creep | 3 | 5 | 100 % | 0 % |
| Architecture | arch-coupling | 4 | 4 | 88 % | 75 % |
| Architecture | arch-naming | 2 | 3 | 20 % | 50 % |
| Architecture | arch-testability | 3 | 2 | 80 % | 0 % |
| SecurityReliability | sec-blast-radius | 3 | 2 | 80 % | 33 % |
| SecurityReliability | sec-concurrency | 2 | 2 | 100 % | **100 %** |
| SecurityReliability | sec-supply-chain | 2 | 4 | 100 % | **100 %** |
| UxDxPerformance | perf-first-run | 3 | 3 | 83 % | 33 % |
| UxDxPerformance | perf-wasted-work | 2 | 0 | 50 % | 0 % |
| UxDxPerformance | ux-undo | 2 | 0 | 50 % | 0 % |

**`plan-data-loss` found nothing, twice, and that is correct.** The subject adds a reviewer that
reads a model; it destroys nothing. A narrow lens aimed at a surface the change does not have returns
an empty list, which is the honest answer and the price of narrowness: **pick the wrong lens and the
round costs a full panel and returns nothing.** The broad prompt cannot fail that way.

**`sec-concurrency` and `sec-supply-chain` repeated themselves perfectly** — every finding of run 1
found again in run 2. Against a median self-repeat of 35 % in phase A, that is the strongest
stability seen in either campaign.

## The failed cell, and the protocol working

`ux-undo-B-r2` produced nothing: `review_code` refused with *"no plan round has reached 'proceed' in
this session — the plan gate comes first"*. Its plan round had not advanced, so the code round was
refused rather than run against a stage that never passed. One cell lost to a gate doing its job is
the right trade, and it is left in the table as a zero rather than quietly dropped.

## What this changes

**Both prompts ship, and the universal one stays the default.** Nothing here argues for replacing the
broad question: per round it finds as much, it cannot return nothing through being aimed wrong, and
three of the four sections have it ahead on yield.

**A lens is for when you know what you are worried about.** The evidence for that is narrow and real:
one known defect reached only by `arch-testability`, near-perfect repeatability from the two security
lenses, and consistently higher gating share across all four sections. It is not evidence that lenses
find MORE.

**The honest headline is the overlap.** One to three shared findings out of twenty or thirty means
the arms mostly do not compete — running a lens is buying a different view, not a better one, and the
question "which is better" was the wrong question to have asked.

## What is still not measured

- **One vendor.** Every cell ran codex at one model, as in phase A.
- **One subject.** The four known defects are from one change; a lens that shines on a
  networking-and-trust feature may say nothing about a database migration.
- **Truth only for what was already known.** Findings outside those four were not verified. The arms
  produced roughly fifty findings between them and this document knows which two of four known ones
  they caught, not how many of the fifty are real.
- **Cost.** The focused arm spent three times the rounds. Nothing here says whether the extra view
  was worth three panels; that is the question a person answers with their own budget.
