# PLAN — the usage columns mean the same thing for every vendor

> Status: **plan only, nothing implemented yet.** Scope: `Usage`, the three adapters' `ReadUsage`,
> `UsageLedger`, and the spending view that renders them.
>
> Related: [RESULTS_haiku_is_not_slow_it_is_verbose.md](../research/RESULTS_haiku_is_not_slow_it_is_verbose.md)
> — the measurement that found this, and the wrong conclusion it produced first.

## The symptom

`usage.jsonl` and the spending view put three vendors' token counts in one column, and the column is
three different quantities:

| vendor | what `TokensIn` counts | where |
|---|---|---|
| claude | `inputTokens + cacheCreationInputTokens + cacheReadInputTokens`, summed over every model in `modelUsage` | `src_mcp/runners/Reviewers/ClaudeRuntime.cs:111` |
| antigravity | `usage.input_tokens` alone — no cache term | `src_mcp/runners/Reviewers/AntigravityRuntime.cs:106` |
| codex | the MAXIMUM over `input_tokens` and the `cache_*` keys, because its stream is cumulative | `src_mcp/core/Findings/UsageParser.cs:37` |

The shape they all fill is `src_mcp/core/Findings/UsageParser.cs:8`
(`Usage(long TokensIn, long TokensOut, double? CostUsd)`), and the row that reaches disk is written at
`src_mcp/runners/Reviewers/UsageLedger.cs:69` into the file named at
`src_mcp/runners/Reviewers/UsageLedger.cs:52`.

Measured 2026-09-08: an identical trivial review — twenty output tokens — reported **29 513** input
for claude against **12 593** for codex and **14 673** for antigravity. The whole gap is Claude Code's
own harness, cached and read back, counted once per turn.

This is not a billing error: each number is what its vendor reported. It is a comparison error, and
it has already cost one wrong diagnosis — the difference was read as "haiku is handed four times the
material", which the per-output-token measurement then refuted.

## What must be true when this is done

1. Two vendors' rows can be compared without knowing which adapter produced them.
2. Cache reads are visible rather than silently folded in — a cached prefix re-read six times is a
   real cost and a real fact, but it is not the same fact as six times the prompt.
3. Nothing that was billed disappears: the totals a company is charged for stay recoverable.
4. `usage.jsonl` stays readable by whatever already reads it, or the change says how it migrates.

## Open questions to settle before building

- **Widen `Usage`, or normalise at the edge?** `Usage(TokensIn, TokensOut, CostUsd)` could gain
  `CacheRead` and `CacheWrite`, which is honest and touches the ledger, the DTOs and the view. The
  cheaper alternative — subtract cache at the adapter — throws information away and cannot be undone
  by a reader later.
- **Does the agy envelope carry cache fields at all?** `AntigravityRuntime.ReadUsage` reads only
  `input_tokens`; whether the CLI reports more has not been checked against a captured envelope. If it
  does not, consistency means recording the absence rather than inventing a zero.
- **What does the spending view actually want to show?** Money is the column people read, and
  `CostUsd` is already reported by claude alone. A per-vendor token column may be the wrong shape for
  the question it is being asked.

## Build order

1. **Capture one real envelope per vendor** into `src_mcp/tests/` fixtures — from the deployed
   server's own runs, not hand-written. Nothing below can be decided honestly without them, and the
   open question about agy's cache fields is answered by looking rather than by reasoning.
2. **Decide the shape** on that evidence: widen `Usage` (`UsageParser.cs:8`) with cache terms, or
   normalise at the adapter. Record the decision and its reason before writing either.
3. **The three adapters**, one at a time, each with its fixture asserting the decomposition it
   produces: `ClaudeRuntime.cs:111`, `AntigravityRuntime.cs:106`, `UsageParser.cs:37`.
4. **The ledger** (`UsageLedger.cs:69`) and whatever reads `usage.jsonl`, including the migration
   story for rows already written in the old shape.
5. **The spending view** last, because what it should SHOW is the open question above and the
   answer may be "money, not tokens".

## Test plan

- A captured real envelope per vendor as a fixture, asserting the decomposition each adapter produces.
- A test that the same logical review, given three vendors' envelopes, yields comparable input counts.
- A ledger round-trip over the new shape.

## Definition of Done

- [ ] One documented meaning per column, asserted per adapter against a real captured envelope.
- [ ] Cache is visible rather than folded in silently.
- [ ] `research/module_runners.md` records the decision.
