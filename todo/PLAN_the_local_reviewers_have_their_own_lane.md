# PLAN — the local reviewers have their own lane, so the next one starts when the last one ends

> Status: **plan only, nothing implemented yet, 2026-09-23.** Scope: `src_mcp/runners/Reviewers/BoundedScheduler.cs`,
> `PanelService.OneLocalRowFirstTheRestLast` (becomes `LocalRowsFirst`), `src_mcp/tests/SharedEngineTests.cs`,
> `src_mcp/tests/TheLocalReviewerIsAskedFirstTests.cs`.
>
> **Plan round (2026-09-23, `proceed`, 2 of 3 reviewers):** accepted — every local row now goes FIRST as
> one group (gemini: with the machine slot gone, "the rest last" only lets a hosted reviewer's process
> spawn sit between `local/1` and `local/2` on the engine queue), and this status line's date. Rejected
> with reasons recorded — a semaphore leak on a killed child (the release is in a `finally`, in-process),
> a hung child (bounded by the reviewer's own deadline), `PeakConcurrency` as a load metric (it is test
> instrumentation), a UI state mismatch (states are reported where each wait is entered), heterogeneous
> cards (the key is already per endpoint), and "unbounded" local processes (the bound is written below).
>
> Related docs: [PLAN_the_local_reviewer_starts_first.md](../research/PLAN_the_local_reviewer_starts_first.md)
> (the half this finishes), [PLAN_one_gpu_one_reviewer.md](../research/PLAN_one_gpu_one_reviewer.md),
> [module_server.md](../research/module_server.md).

## The symptom

Issue #155, second half, restated by the operator on 2026-09-23: *"when there are local LLMs they
always have priority. That the first one starts first is right. But as soon as the first local LLM
finishes, the second must start at once, and so on. Maybe a separate queue for the local ones (take
them out of the general one)."*

A round with four local roles today runs `local/1` at once — and `local/2..4` only after the hosted
vendors have drained, although the card is idle the moment `local/1` answers.

## Why it happens (read from the code, `BoundedScheduler.cs:191-293`)

Every reviewer takes three slots, widest first: the machine (`globalCap`, 3), its vendor
(`perProviderCap`, 2), and — only a local one — its engine (`sharedResourceCap`, 1, keyed by
endpoint). `OneLocalRowFirstTheRestLast` (`PanelService.cs:1020`) submits `local/1` first and the
other local rows LAST, so they wait for a MACHINE slot behind every hosted reviewer. When `local/1`
ends it releases a machine slot, and the next waiter on that semaphore is a hosted reviewer, not
`local/2`. The card then sits idle while `local/2` queues for a slot it does not need.

The research plan left exactly this out on purpose ("a different mechanism … needs a measurement
first"). The measurement is the operator's screenshot: four local roles in one round, the three
tail ones waiting behind codex and gemini.

## The change

**A reviewer with a `SharedResource` (a local engine) does not take a machine slot or a vendor slot.
Its only limiter is its engine.** Hosted reviewers keep all three machine slots and their per-vendor
caps, unchanged.

- The engine semaphore is already FIFO over async waiters and already outlives a round, so local
  rows run strictly one after another in submission order, and across rounds in one server.
- `local/1` still leads: nothing ahead of it on the engine.
- The machine cap exists for CLI processes, lock files and vendor 429s. A local reviewer is one
  small self-invocation of `coai-mcp` speaking HTTP to the engine, and at most `engines × cap` of
  them run at once (one, on a one-card machine) — the engine cap already bounds them.
- `PeakConcurrency` keeps meaning what the machine cap bounds: reviewers in the machine lane. The
  engine lane is measured by `PeakPerResource`, which already exists.
- **The bound, stated:** hosted reviewers ≤ `globalCap`; local reviewers ≤ engines × `sharedResourceCap`
  (one per configured card by default). A machine with one card runs at most `globalCap + 1`.
- `OneLocalRowFirstTheRestLast` becomes **`LocalRowsFirst`**: every local row leads, as one group in
  the shuffle's order, and the hosted rows follow in theirs. "The rest go last" existed only so a
  waiting local row would not hold a machine slot; with no machine slot to hold, leaving them last
  would only put the hosted rows' synchronous launch prefix between `local/1` and `local/2` on the
  engine queue (plan round, gemini).

**Not in scope:** the cross-process engine lease (`EngineLease`) between two servers on one machine.
It already hands the card over when the holder ends.

## Test plan (RED first)

| # | Test (`src_mcp/tests/SharedEngineTests.cs`) | RED symptom expected |
|---|---|---|
| 1 | *the next local reviewer starts when the last one ends, not after the hosted ones* — the order `BuildWork` produces (one local, five hosted, two locals); hosted launches block until the test releases them; all three locals must finish while every hosted reviewer is still blocked | times out: `local/2` waits for a machine slot held by a blocked hosted reviewer |
| 2 | *a local reviewer takes no machine slot* — while three locals run one by one, three hosted reviewers are running at once (`PeakConcurrency == 3` with `globalCap: 3`) | peak 2 while `local/1` holds a slot |
| 2b | `TheLocalReviewerIsAskedFirstTests`: *every local row leads, in the shuffle's order* replaces *one local row leads and the rest go last* | the second local row is last |
| 3 | existing: *reviewers on one engine run one at a time*, *two vendors on one engine share it*, *two concurrent rounds share the engine*, *cancelled while waiting still reports* | (guards; green before and after) |

Run: `dotnet build src_mcp/tests -c Debug` then
`./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*SharedEngine*"` for the loop,
and the whole executable before the commit — never `dotnet test`.

## Build order

1. Tests 1-2, watched red for the reason in the table.
2. `BoundedScheduler`: branch the lane on `SharedResource`; engine-only for local rows.
3. `OneLocalRowFirstTheRestLast` → `LocalRowsFirst`, its test updated; the scheduler's remarks.
4. `research/module_server.md` (the scheduler section), and promote this plan.

## Definition of Done

- [ ] Tests 1-2 red for the stated reason, green after; the break-it check done with compiling code.
- [ ] Every existing scheduler and shared-engine test green; the whole `CoaiMcp.Tests.exe` green.
- [ ] `module_server.md` says the local lane exists and why the machine cap does not bound it.
- [ ] This plan promoted to `research/` with what shipped differently.
