# PLAN — does the gate still work, and does it hold under five windows?

> Status: **plan only, nothing measured yet.** Scope: no production code — this is a measurement of
> `mcp 0.17.0` using `src_bench/CoaiBench`, and any product change it provokes gets its own plan.
>
> Related: [../src_bench/README.md](../src_bench/README.md) ·
> [../research/module_server.md](../research/module_server.md) ·
> [../research/RESULTS_commands_campaign.md](../research/RESULTS_commands_campaign.md)

## The goal, and why now

Six code rounds died today with `Access to the path is denied`, and the fixes went in fast: a scratch
name per write, readers that no longer forbid writing, a turn shared by both, an in-place write under
that turn, and a final record that is required rather than best-effort. Every one of them has a test.
None of them has been seen carrying a real round on a real machine with real vendors.

So: **run the thing, and say whether it is worse than it was.** Two questions, in this order.

1. Does a plan round and a code round still work, end to end, on the shipped binary?
2. Do five of them at once — five windows, one data directory, one GPU — still each produce an answer
   whose findings can be resolved?

The second is the one the fixes were for. The first is what makes the second mean anything.

## What "not worse" is measured against

Not a feeling. Both cases ran through the gate earlier today on **0.15.0**, and those numbers are
recorded:

| case | stage | on 0.15.0 |
|---|---|---|
| `split-once` | code | `proceed`, 12 findings, 9 reviewers, 957k in / 9.9k out, 4m19s |
| `rounds-collapse` | plan | `good_enough`, 15 findings, 3 reviewers, 38.9k in / 4.6k out |

Same plan text, same commits, same vendors. A comparison on identical input, not on vibes.

## Epics

### Epic 1 — the sequential baseline

Two cases, plan and code, one arm of three vendors, twice each. This is the "does it work" half and
the "not worse" half at once.

- `--arm codex,gemini,local --repeat 2 --stages both`, cases `rounds-collapse` and `split-once`.
- Definition of done: every run produces a verdict; no run reports `NOTHING RAN`; the session file on
  disk for each finished round says `done` with a non-empty `pending` — which is the state `resolve`
  needs and the exact thing that was silently missing this afternoon.

### Epic 2 — five windows

The same two cases, five lanes, one SHARED data directory — because the interference is the subject.

- `--parallel 5` over the same corpus and arm.
- Definition of done: five answers, none of them `NOTHING RAN`, no `Access to the path is denied`
  anywhere in any server's stderr, and every finished round resolvable on disk.
- Recorded either way: if it still fails, the failure is the result and it gets its own plan.

### Epic 3 — the judgement and the table

- `judge --judge claude-fable-5-1` over both run files: Fable reads each finding with the file it
  names and says whether it was worth having.
- One table per the operator's ask: **time, findings, useful findings, tokens** — per arm and per
  run, medians not means, failures named rather than averaged.
- Definition of done: a report in `research/` with the tables, the before/after against 0.15.0, and
  what the campaign got wrong about itself.

## What would make this a failure worth stopping for

- Any round that answers with findings while its session says `running` — the defect fixed this
  afternoon, alive again.
- A round killed by a file operation under five lanes.
- Findings materially fewer or worse than the 0.15.0 baseline on identical input: the store fixes
  were not supposed to touch what reviewers see, and if they did, that is the finding.

## Test plan

The bench is the instrument and it has its own tests (25). What is verified HERE, after each epic, is
the state on disk rather than the harness's own summary: for every finished round, `status: done` and
a `pending` list whose length matches the findings the answer carried.

## Definition of Done

- [ ] Epic 1 run, recorded, compared against the 0.15.0 numbers on identical input.
- [ ] Epic 2 run at five lanes on one data directory, with the stderr of every server kept.
- [ ] Every finished round resolvable on disk — checked, not assumed.
- [ ] Fable's judgement over both run files.
- [ ] The report in `research/`, and this plan promoted.
