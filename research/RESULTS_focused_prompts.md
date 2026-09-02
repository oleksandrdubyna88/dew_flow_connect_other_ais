# RESULTS — twelve focused lenses, three shapes each, measured

> Phase A of the focused-prompt campaign, run 2026-09-01/02 in WSL against the real `coai-mcp` over
> stdio. **72 cells** — 12 candidate lenses × 3 wordings × 2 runs — 38.7 M input tokens, 2.1 hours of
> wall clock, **0 failed cells**. Raw output: `~/coai-select/*.json` (findings, cost, seconds) and the
> matching `.log`; scores in `_scores.json`.
>
> Phase B — the winners against the universal prompt, per section, on a real plan and a real PR — is
> **not run yet**; the section at the end says exactly what it is and why it is worth running.
>
> Companion measurements: [RESULTS_model_comparison.md](RESULTS_model_comparison.md) (which model),
> [RESULTS_prompt_measurement.md](RESULTS_prompt_measurement.md) (the universal prompts),
> [RESULTS_conventions_prompt.md](RESULTS_conventions_prompt.md) (the conventions pass).

## What was being decided

Each review section is to offer 5–7 narrow lenses somebody can pick instead of the section's universal
prompt. Twelve were drafted — three per section — and each was written **three times**. The point of
the three was to pick the best wording of each lens.

It turned out to measure something more useful, because the three wordings were not arbitrary. Every
lens was written in the same three shapes, held constant across all twelve:

| | Shape | How it opens |
|---|---|---|
| **A** | a question list | *"Ask, in this order: — What does this plan DELETE, overwrite, truncate or move…"* |
| **B** | a task to enact | *"Somebody runs this at 02:00 and it fails at 02:04. Describe what they find in the morning, and work backwards to the step that made it possible."* |
| **C** | a rule with exceptions | *"The rule: **no step may destroy data that a later step is responsible for restoring.** Exceptions: … Find every violation."* |

So the campaign answers two questions, and the second is the interesting one: which wording of each
lens, and **does the SHAPE of a review prompt change what comes back**.

## How a cell was scored

Every cell ran the real protocol — `open`, `review_plan`, `resolve`, `review_code` — with the
candidate text overriding its section's prompt, against one vendor (codex) at a fixed model, seed and
temperature. Four numbers, none a matter of taste:

| | What it measures | Why it is in the score |
|---|---|---|
| **yield** | findings per run | a lens that finds nothing is not a lens; capped at 8 so none can win by shouting |
| **gating** | share Blocking/Major | a lens that only produces Minors does not move a gate |
| **repeated** | how many of run 1's findings run 2 found again | **the decisive one.** Same prompt, same code, different sampling: a lens whose output does not survive its own second run cannot be handed to somebody |
| **distinct** | share its SIBLING lenses in the same section did not report | a focused prompt earns its slot by seeing what the others miss |

`score = 0.45·repeated + 0.35·distinct + 0.20·min(yield/8, 1)`. Two findings are the same defect when
they name the same file within 12 lines, or when their titles match above 0.6 similarity.

## The measurement that matters: shape beats wording

| Shape | wins | mean score | **mean repeated** | mean distinct | mean findings | mean gating |
|---|---|---|---|---|---|---|
| **A** — a question list | 3/12 | 0.408 | 32 % | 28 % | 6.9 | 79 % |
| **B** — a task to enact | **7/12** | **0.460** | **42 %** | 30 % | 6.9 | 82 % |
| **C** — a rule with exceptions | 2/12 | 0.411 | 33 % | 29 % | 6.6 | 79 % |

**Yield and severity are flat across all three shapes** — 6.6 to 6.9 findings, 79–82 % gating. The
shape does not change how much is found or how serious it is. What it changes is **how reliably the
same thing is found twice**: 42 % against 32 %, a third more of run 1 surviving into run 2.

That is a better prize than it first looks. Run-to-run variance is the dominant noise in this whole
product — the median cell reproduced **35 %** of its own findings — so a shape that raises
repeatability is a shape that makes a gate's verdict mean more. A finding that appears once in two
runs is a finding a developer cannot trust; the threshold is counting those.

**Where it holds, and where it does not.** Honest breakdown by section, mean score:

| Section | A (questions) | B (enact) | C (rule) |
|---|---|---|---|
| PlanCritique | **0.457** | 0.441 | 0.379 |
| Architecture | 0.450 | **0.491** | 0.442 |
| SecurityReliability | 0.381 | 0.454 | **0.459** |
| UxDxPerformance | 0.345 | **0.454** | 0.366 |

The effect is carried by **UxDxPerformance** (0.454 against 0.345 — the largest gap in the table) and
**Architecture**. In SecurityReliability the rule shape ties it, and in PlanCritique the question list
is nominally ahead. So "enact it" is the best default, not a law.

**Why UxDxPerformance is where it shows.** Its shape-B prompts are the ones that ask the reviewer to
be a person: *"Install this on a brand-new machine and use it for the first time. Narrate what you see,
second by second, for the first thirty seconds."* — *"Use it wrongly on purpose. Click the wrong
button, type the wrong value, run it twice, answer the dialog without reading it."* A question list
cannot get at a first-run experience, because the answer is a sequence, not a fact. A concurrency
prompt has the same property (*"Run it twice, starting the second one a millisecond after the first.
Narrate both, interleaved"*) and shape B wins that lens too, by the second-largest margin in
SecurityReliability.

## Every cell

**Wording** column: **←** marks the pick. Numbers are the two runs.

### PlanCritique

| Lens | Wording | findings r1/r2 | gating | repeated | distinct | score |
|---|---|---|---|---|---|---|
| Data loss & recovery | A **←** | 3/3 | 100% | 33% | 83% | 0.517 |
| Data loss & recovery | B | 2/3 | 100% | 0% | 100% | 0.412 |
| Data loss & recovery | C | 3/1 | 100% | 33% | 50% | 0.375 |
| Operability | A | 4/5 | 78% | 0% | 89% | 0.424 |
| Operability | B **←** | 3/3 | 100% | 0% | 100% | 0.425 |
| Operability | C | 4/5 | 100% | 0% | 89% | 0.424 |
| Scope & budget | A | 4/4 | 100% | 25% | 62% | 0.431 |
| Scope & budget | B **←** | 7/6 | 100% | 0% | 92% | 0.486 |
| Scope & budget | C | 4/3 | 100% | 0% | 71% | 0.338 |

### Architecture

| Lens | Wording | findings r1/r2 | gating | repeated | distinct | score |
|---|---|---|---|---|---|---|
| Coupling & knowledge | A | 8/9 | 59% | 38% | 6% | 0.389 |
| Coupling & knowledge | B | 10/6 | 81% | 50% | 12% | 0.469 |
| Coupling & knowledge | C **←** | 8/9 | 82% | 50% | 18% | 0.487 |
| Names & the shape they imply | A **←** | 8/8 | 69% | 50% | 25% | 0.512 |
| Names & the shape they imply | B | 7/6 | 85% | 57% | 0% | 0.420 |
| Names & the shape they imply | C | 9/7 | 56% | 33% | 19% | 0.416 |
| Testability of the seams | A | 8/8 | 88% | 50% | 6% | 0.447 |
| Testability of the seams | B **←** | 7/9 | 75% | **86%** | 0% | **0.586** |
| Testability of the seams | C | 8/6 | 93% | 50% | 7% | 0.425 |

### SecurityReliability

| Lens | Wording | findings r1/r2 | gating | repeated | distinct | score |
|---|---|---|---|---|---|---|
| Blast radius | A **←** | 8/10 | 78% | 50% | 11% | 0.464 |
| Blast radius | B | 8/8 | 69% | 50% | 6% | 0.447 |
| Blast radius | C | 7/9 | 75% | 29% | 12% | 0.372 |
| Two at once | A | 7/7 | 79% | 29% | 7% | 0.329 |
| Two at once | B **←** | 6/9 | 73% | 67% | 13% | 0.534 |
| Two at once | C | 9/7 | 69% | 56% | 19% | 0.516 |
| What this change trusts | A | 9/12 | 62% | 22% | 14% | 0.350 |
| What this change trusts | B | 10/10 | 80% | 40% | 0% | 0.380 |
| What this change trusts | C **←** | 9/9 | 67% | 56% | 11% | 0.489 |

### UxDxPerformance

| Lens | Wording | findings r1/r2 | gating | repeated | distinct | score |
|---|---|---|---|---|---|---|
| The first run and the empty case | A | 6/8 | 93% | 50% | 0% | 0.400 |
| The first run and the empty case | B **←** | 7/8 | 73% | 71% | 7% | 0.532 |
| The first run and the empty case | C | 8/9 | 47% | 38% | 6% | 0.389 |
| Work done twice | A | 7/8 | 67% | 14% | 13% | 0.298 |
| Work done twice | B **←** | 7/6 | 62% | 57% | 0% | 0.420 |
| Work done twice | C | 6/7 | 69% | 33% | 15% | 0.366 |
| What cannot be taken back | A | 7/5 | 83% | 29% | 17% | 0.337 |
| What cannot be taken back | B **←** | 7/10 | 82% | 29% | 24% | 0.411 |
| What cannot be taken back | C | 6/6 | 92% | 17% | 33% | 0.342 |

## The picks — and which of them are real

Two runs cannot separate two prompts that are close. The median cell reproduced 35 % of its own
findings, so anything under about 0.09 of score is inside that noise. Saying which picks are
**decided** and which are **arbitrary** is the difference between a measurement and a leaderboard:

| Section | Lens | Pick | Margin | |
|---|---|---|---|---|
| Architecture | Testability of the seams | **B** | 0.139 | decided |
| UxDxPerformance | The first run and the empty case | **B** | 0.132 | decided |
| SecurityReliability | What this change trusts | **C** | 0.109 | decided |
| PlanCritique | Data loss & recovery | **A** | 0.104 | decided |
| Architecture | Names & the shape they imply | **A** | 0.093 | decided |
| UxDxPerformance | What cannot be taken back | **B** | 0.069 | within noise |
| PlanCritique | Scope & budget | **B** | 0.054 | within noise |
| UxDxPerformance | Work done twice | **B** | 0.053 | within noise |
| SecurityReliability | Two at once | **B** | 0.019 | within noise |
| Architecture | Coupling & knowledge | **C** | 0.018 | within noise |
| SecurityReliability | Blast radius | **A** | 0.017 | within noise |
| PlanCritique | Operability | **B** | 0.001 | within noise |

**Five of twelve are decided by the measurement. Seven are a coin toss**, and pretending otherwise
would be the whole point of running two runs thrown away. For those seven the tie-break is the shape
result above — take **B**, the enacted task — which is a defensible prior rather than a result. Four
of the seven already are B.

## What this does not measure, said plainly

- **One vendor.** Every cell ran codex at one model. A shape effect could be that model's habit rather
  than a property of review prompts. The cheapest strengthening available is re-running the twelve
  winners on a second vendor.
- **No ground truth.** `repeated` and `distinct` are proxies for a question they cannot ask: is the
  finding TRUE. A lens could repeat itself perfectly and be repeatedly wrong. The plan half of the
  model comparison had planted defects and could score correctness; this campaign has none, and the
  honest next measurement is a diff with known defects per section.
- **`distinct` is relative to siblings, not to the universal prompt.** A lens with 0 % distinctness
  said nothing its two sibling lenses did not — but it may still be finding what the universal prompt
  misses, which is the question phase B exists to answer.
- **PlanCritique's numbers are the weakest.** Plan rounds produce 2–6 findings, so one match moves
  `repeated` by 20–50 points, and six of the nine plan cells scored 0 % on it. Its section row in the
  shape table should be read as "no signal", not as "questions win here".

## Phase B — designed, not yet run

The question phase A cannot answer: **does a focused lens find anything the section's universal prompt
does not?** Everything above compares lenses to each other.

The design: per section, one arm running the universal prompt and one running that section's three
winning lenses, over the SAME real subject — the plan and the change of the local-model feature
([PLAN_local_models.md](PLAN_local_models.md), commit `2b7d3ab`) — two runs each, 32 cells. That
subject was chosen because its ground truth is known: the gate ran over it three times, four findings
were fixed in the change and four were deferred to
[PLAN_local_trust_and_vllm.md](../todo/PLAN_local_trust_and_vllm.md) and are therefore **still true of
the committed code**. So phase B can score something phase A could not: how many of four known real
defects each arm finds.

## What ships from phase A

The twelve lenses go into the prompt catalog with the picked wording, five to seven per section as
intended, and the shape finding goes into the authoring guidance for any lens written later:

> **Write a review prompt as a task to perform, not a list of things to check.** "Install this on a
> new machine and narrate the first thirty seconds" and "run it twice, a millisecond apart, and
> narrate both" find as much as a question list does, and find it again on the second run half again
> as often. Where the subject genuinely has no sequence to enact — a data-loss audit of a plan — a
> question list is not worse.
