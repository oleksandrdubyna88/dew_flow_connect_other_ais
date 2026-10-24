# RESULTS — do vendors find the same defects? Thirty days of real rounds

> Status: **measured, 2026-10-21.** Data: [data/vendor-overlap-2026-10-21/](data/vendor-overlap-2026-10-21/)
> (scripts, outputs, judged clusters as finding ids only). Source: both live rounds stores of one
> machine (Windows and WSL), rounds started 2026-09-21 → 2026-10-20.
>
> Related: [RESULTS_vendor_overlap_2026-09-06.md](RESULTS_vendor_overlap_2026-09-06.md) (bench, 5–9 %),
> [RESULTS_five_plans_five_models.md](RESULTS_five_plans_five_models.md) (reading, 55–76 %),
> [RESULTS_prompt_measurement.md](RESULTS_prompt_measurement.md) (run-to-run control).

## The question

The two earlier measurements disagree by an order of magnitude: the bench said a vendor shares
**5–9 %** of its findings with another, the five-plans reading said **55–76 %**. Both were small
and both were campaigns. This one asks the production log: in real rounds where two or more vendor
families reviewed the same input, how often does a finding have a twin from another vendor?

## Data

| | count |
|---|---|
| findings in window (both stores) | 17,791 |
| rounds | 2,328 |
| rounds with ≥2 vendor families returning findings | **1,137** |
| findings in those rounds | 13,518 |

Vendor families: OpenAI (codex), Google (antigravity/gemini + a Gemini Flash chat preset),
Mistral (vibe), Moonshot (kimi), Qwen, Anthropic (claude), and `local` (a model on this machine).

## 1. What the product itself records

The `providers` column (the gate's own dedup) lists more than one vendor on **36 of 13,518**
findings (0.3 %). This is the strict title-Jaccard rule the five-plans record already showed blind
to rewording — it is a floor, not a measurement.

## 2. Automatic pass (all 1,137 rounds)

TF-IDF cosine on title + why, a match when cosine ≥ 0.30, or ≥ 0.12 with the same file and lines
within 10. Share of findings with a match from ANOTHER vendor family in the same round:

| stage | shared | by family |
|---|---|---|
| CodeReview | 29.3 % (2,442 / 8,344) | google 31 %, openai 33 %, mistral 28 %, local 25 % |
| PlanReview | 20.6 % (1,037 / 5,028) | google 20 %, openai 23 %, mistral 17 %, local 19 % |
| DocumentReview | 34.2 % (50 / 146) | — |

Pairwise (A's findings matched by vendor B, same round): **18.0 % across vendors**, **45.4 %
between two instances of the SAME vendor** (claude/claude-2, qwen/qwen-2; only 416 findings — weak).

## 3. The calibration: 30 rounds judged by reading

Thirty multi-vendor rounds (15 code, 15 plan, 6–28 findings each, 359 findings) were drawn at random
and grouped into "same concrete defect" clusters by three Opus judges, ten rounds each.

| | judged | automatic on the same rounds |
|---|---|---|
| findings with a twin from another vendor — all | **21.2 %** (76 / 359) | 28.1 % |
| — CodeReview | **17.4 %** (36 / 207) | 31.4 % |
| — PlanReview | **26.3 %** (40 / 152) | 23.7 % |
| distinct defects raised by 2+ vendors — code | **9.9 %** (17 / 172) | |
| distinct defects raised by 2+ vendors — plan | **14.1 %** (18 / 128) | |
| finding with a twin from the SAME model, other role | 6.1 % (22 / 359) | |

Matcher on cross-vendor PAIRS: recall 27 / 29, precision 27 / 70. It finds nearly every true twin
and over-counts on code (same file, different defect). Applying the per-stage judged/auto ratio to
the full month gives roughly **16 % shared on code and 23 % on plans**.

## 4. Agreement is worth more

Caller acceptance (`resolve`) of a finding:

| | alone | shared with another vendor |
|---|---|---|
| judged sample | 50.5 % (143 / 283) | **65.3 %** (49 / 75) |
| automatic, CodeReview | 34.8 % | **48.0 %** |
| automatic, PlanReview | 59.7 % | **68.5 %** |

## What this says

1. **The claim holds in its strong form for DEFECTS, in a weaker form for findings.** Of the
   distinct defects in a multi-vendor round, **86–90 % were raised by one vendor only.** Of the
   findings a vendor writes, about **one in five** has a twin elsewhere. Not 5 %, not 60 %.
2. **The two older numbers bracket this one for a reason.** The bench clustered two planted cases
   across seven arms and gave 5–9 %; the five-plans reading clustered across five
   whole plans with five models each (more chances to meet) and gave 55–76 %. A live round has two to
   three vendors and one input.
3. **"A unique prism" is not proven by overlap alone.** The run-to-run control in
   [RESULTS_prompt_measurement.md](RESULTS_prompt_measurement.md) showed one model, one prompt, one
   plan, three runs sharing 0–3 findings. Part of the low overlap is sampling noise, not the vendor's
   view. The same-vendor pairs here (45 % automatic, n = 416) lean the other way, but are too few to
   decide it.
4. **Roles of one vendor overlap less than vendors do** (6 % against 21 %). A role is a stronger
   divider than a vendor on the same round — consistent with
   [RESULTS_focused_vs_universal.md](RESULTS_focused_vs_universal.md).
5. **The gate still sees almost none of the agreement it has** — 0.3 % recorded against ~21 % by
   reading. The plan-round dedup defect from 2026-09-02 is still the reason, and it now hides the
   one signal that measurably predicts acceptance.

## External work pointing the same way, and against

- AACR-Bench (arXiv 2601.19494, 2026, Table 8): six models on the same PRs; of 1,505 ground-truth
  comments 68.2 % found by exactly one model, 7.1 % by two, 0.7 % by three, 23.9 % by none. Of the
  comments ANY model found, 90 % were found by one only. Caveat: the ground truth was built partly
  FROM these models' verified outputs, which favours low overlap. Best single-model recall is 47 %
  (10 % in agent mode).
- SWR-Bench (arXiv 2509.01494): shared correct findings across different LLMs (36) barely exceed
  those shared across five runs of ONE model (27) — the noise caveat of point 3.
- Song, "When Does a Second Model Help?" (arXiv 2610.01471, preprint): one same-model + one
  cross-model review find 56.7 % of planted errors against 42.7 % for two same-model reviews; a
  third model adds +1.3 points.
- Kim et al., "Correlated Errors in LLMs" (ICML 2025): when two models are both wrong they agree 60 %
  of the time, more so for stronger models — vendors are less independent than hoped.
- Greptile, "Models are worse at reviewing their own code" (2026-07-21 vendor post): each model finds
  2–3 points more P0/P1 bugs in the other vendor's PRs; recall 50–62 %. The page text does not say
  which bar is which, so only the direction and the gap are certain.

## Limits

- One machine, one operator, mostly two to three vendors per round; `kimi` and `anthropic` are thin.
- "Shared" means within one round. The same defect raised by vendor A in round 1 and vendor B in
  round 2 is not counted.
- The judges are one vendor's model. Their grouping was not double-judged.
- **Overlap is not recall.** The defect universe here is the union of what the reviewers found.
  "Found by one vendor only" says nothing about defects every vendor missed — that needs a ground
  truth this log does not have.
- No ground truth: acceptance by the caller is the closest proxy for "worth having".
