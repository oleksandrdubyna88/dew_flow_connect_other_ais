# RESULTS — which MODEL to review CODE with: one real commit, three roles per model

> Status: **partial, 2026-09-01.** Eleven of fourteen cells completed; three were lost to an
> exhausted vendor quota and are named below rather than omitted.
>
> The plan half, which carries the method and the variance discussion, is
> [RESULTS_model_comparison.md](RESULTS_model_comparison.md).

## Method

**One real commit**: `939175d` — the CLI update button and the coloured section headers — reviewed
against the scope it was written to, `4b49cfc..main`, 19 files and 772 insertions.

**One cell = one vendor, one model, all three code roles, one round**, threshold 99 so nothing gates
and every finding is reported.

**No planted defects.** It is a real change, so there is no ground truth to score recall against.
What is measured here is how much each model reports, how long it takes and what it costs. Whether
the findings are TRUE is a judgement, made by reading them against code written the same hour — a
weaker instrument than the plan half, and labelled as such wherever it is used.

## Two false starts, both worth recording

**The first two attempts measured the conventions prompt and collected a truthful zero.** Round 1 of
every code role is the conventions pass by default; this commit obeys the project's written rules, so
`0 findings` was the correct answer to a question nobody was asking. A comparison of MODELS needs the
question they differ on, so round 1 was pinned to each role's universal prompt.

The second attempt failed for a duller reason: the harness patch that was supposed to pin those
prompts never applied — a backslash eaten by shell heredoc handling — so the run measured the
conventions pass again and looked identical. Two campaign runs lost to a quoting bug, which is why
the harness is now a file rather than a heredoc.

## The table

| model | findings | wall | tokens in/out | ~$ per run |
|---|---|---|---|---|
| Gemini 3.7 Flash (High) | **11** | 294 s | 289k / 42k | $0.38 |
| Gemini 3.1 Pro | **11** | 574 s | 296k / 47k | $1.16 |
| GPT-5.6-Terra | 8 | 168 s | 214k / 10k | $0.55 |
| GPT-5.6-Sol | 8 | 135 s | 208k / 9k | $1.44 |
| GPT-5.6-Luna | 8 | 165 s | 211k / 10k | $0.05 |
| GPT-5.6-Luna *(second run)* | 8 | 135 s | 209k / 9k | $0.05 |
| GPT-5.5 | 7 | 106 s | 205k / 8k | $1.27 |
| GPT-5.4 | 7 | 208 s | 207k / 12k | $0.70 |
| GPT-5.4-Mini | 7 | **478 s** | 206k / 21k | $0.25 |
| Gemini 3.7 Flash (Low) | 6 | 65 s | 275k / 8k | $0.24 |
| Claude Sonnet 4.6 | 3 | 630 s | — | — |

**Claude Sonnet 4.6's row is not a score.** One of its three reviewers answered; the other two were
rate limited. Three findings from one reviewer is not comparable with eleven from three, and it is
left in only so the row is not silently missing.

**Lost with no row at all:** Claude Opus 4.6, GPT-OSS 120B, and Sonnet's second run.

They were retried after a cooldown and failed identically, in 33 seconds each. The log says why, and
it is not the transient throttle this first read as: `Individual quota reached` — the antigravity
ACCOUNT's quota, exhausted after roughly fifty runs in an hour across both halves of the campaign.
Minutes do not fix that, so these three stay missing until the quota window resets. Saying they
"will be re-run" would have been the easier sentence and the wrong one.

**The gate behaved correctly while this happened, which is worth its own line.** With zero of one
reviewer answering, the plan round returned `call_human` with *"no reviewer answered — nothing was
reviewed"*, and `review_code` then refused because no plan round had reached `proceed`. A quota
outage produced a stop and an explanation rather than a green review of nothing.

## What the completed cells show

- **Finding COUNT is not quality, and this half cannot separate them.** The two eleven-finding models
  are also the two slowest and among the most expensive. Reading their output, the extra findings are
  mostly real but minor — placeholder text, a cache window, an aria-label — while the seven-finding
  codex runs concentrate on the same handful of substantive points. A count column is what the
  campaign can measure; it is not what a gate is for.
- **GPT-5.6-Luna is the value outlier again**, and this time it repeats: 8 findings twice, 135–165 s,
  at five cents against Sol's $1.44 for the same count. Whatever separates Sol from Luna, it did not
  show up on this diff.
- **A code round is 200k+ input tokens** whatever the model — the diff, the scope and the rules
  dominate, and the model choice moves the output tokens rather than the input. That is what makes
  the price column swing 30× while the token column barely moves.
- **Gemini 3.7 Flash at LOW effort is the cheap fast option here too**: 6 findings in 65 seconds,
  four times faster than the same model at HIGH for roughly half the findings.

## Recommendation, held loosely

For the code stage the plan half's recommendation stands unchanged — **two vendors, Sonnet 4.6 and
GPT-5.5** — but this half does not confirm it, because Sonnet could not complete a round. What this
half does say is that **GPT-5.6-Luna deserves a place in the rotation**: it repeated its result, it
is the cheapest thing measured by an order of magnitude, and it found as much as models costing
twenty times more.

The honest summary of the code half is that it measures throughput, not judgement. A campaign that
could measure judgement needs a diff with planted defects, the way the plan half has — and that is
the obvious next measurement rather than a conclusion to draw from this one.
