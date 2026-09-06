# RESULTS — what each vendor found that the others did not

> Status: **measured, 2026-09-06.** Data: [data/bench-2026-09-06/](data/bench-2026-09-06/) (server
> 0.18.0, one repeat, seven arms × two cases). The six campaigns that preceded it, on 0.17.1–0.17.4,
> are in [data/bench-2026-09-05/](data/bench-2026-09-05/).
>
> Judged by `claude-opus-5`, one CLI turn per finding, over the code as reviewed (`git show` at the
> commit, windowed around the cited line). Related: [RESULTS_model_comparison.md](RESULTS_model_comparison.md),
> [module_bench.md](module_bench.md).

## The question the per-arm table cannot answer

A provider whose every finding repeats another's scores exactly as well on hit-rate as one that
found things by itself — and the second is the only thing a second provider is worth paying for.
So the overlap pass clusters findings across every arm and repeat, per case and stage, and asks of
each vendor: what did it write, how many distinct things were those, how many did somebody else
also name, how many did nobody else name, and how many of *those* were worth having.

Matching is deliberately generous — two findings close enough to argue about are counted as one —
because erring the other way inflates "only I found this", which is the number a purchase is made on.

## Who found it alone

| provider | findings written | distinct | also found by another | found by it alone | of those, worth having |
|---|---|---|---|---|---|
| `codex` | 75 | 60 | 5 (8 %) | **55 (92 %)** | **22** |
| `gemini` | 52 | 43 | 4 (9 %) | **39 (91 %)** | **19** |
| `local` | 118 | 59 | 3 (5 %) | **56 (95 %)** | **5** |

**The overlap is 5–9 %.** Three reviewers reading the same diff almost never name the same defect.
Whatever else this measurement says, it settles the first question: a second vendor is not a second
opinion on the same findings, it is a different set of findings. Redundancy is not what is being
bought, and it is not what is being paid for either.

## Where each one earns its place — and where it does not

| provider | plan stage | code stage |
|---|---|---|
| `codex` | 17 / 35 (49 %) | 13 / 40 (33 %) |
| `gemini` | 17 / 28 (61 %) | 9 / 24 (38 %) |
| `local` | 9 / 48 (19 %) | **2 / 70 (3 %)** |

`local` writes the most of anyone — 118 findings, more than codex and gemini together — and two of
its seventy code-stage findings were worth having. On a PLAN it is a fifth useful, which is a real
contribution at no marginal cost. On CODE it is noise with a 3 % signal, and each of those findings
costs a person the time to read and resolve it.

`gemini` is the opposite shape: the fewest findings and the highest hit rate in both stages.

## What this changes

1. **Keep more than one vendor.** At 5–9 % overlap the arms are additive, and the all-three arm's
   twenty findings per code round are twenty different things.
2. **`local` belongs in the plan stage.** Its code-stage output is a 3 % signal that a person pays
   for in reading time. The gate's per-stage vendor selection is the switch that already exists.
3. **The useful findings are not nits**: 42 Major, 14 Blocking, 11 Minor. What the judge kept is
   what would have changed the change.

## What this measurement cost to get twice

The first pass of this judgement was lost — the pass wrote its file once, after the loop, and it
died on its fourteenth run of fourteen when a reviewer cited a line past the end of a file
(`Enumerable.Range` with a negative count). Thirteen judged runs, about a hundred paid CLI turns,
existed only in memory. Both holes are closed: the file is written after every run and a restart
skips what this judge already answered ([PLAN_bench_campaign_after_the_store_fix.md](PLAN_bench_campaign_after_the_store_fix.md)),
and a cited line the file does not have yields no window instead of taking down the process.
