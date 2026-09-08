# RESULTS — haiku is not slow, it is verbose; and the input column does not mean the same thing twice

> Measured 2026-09-08 on `coai.remsoft.dev`, from `data/usage.jsonl`: **57 successful reviews**, three
> vendors, four roles, real rounds rather than a bench.
>
> Two findings, and the first one refutes the hypothesis that started the investigation.

## The question

A Team-server round showed `remsoftdev-claude` taking minutes where the other two took seconds. The
usage lines for one round, same roles, suggested an obvious cause:

| role | vendor | seconds | in | out |
|---|---|---|---|---|
| Architecture | claude · haiku | 108.1 | **190 139** | 9 137 |
| Architecture | codex | 38.4 | 42 090 | 1 938 |
| Architecture | antigravity | 56.8 | 63 927 | 20 476 |
| SecurityReliability | claude · haiku | 242.6 | 107 991 | 23 652 |
| SecurityReliability | codex | 41.3 | 42 119 | 1 957 |
| SecurityReliability | antigravity | 11.8 | 42 545 | 772 |

Read down the `in` column and the conclusion writes itself: haiku is handed four times the material,
so of course it takes four times as long. **That conclusion is wrong twice over.**

## Finding 1 — the time is explained by OUTPUT, and haiku generates fast

Over all 57 successful reviews (runs with a real answer, `out > 100`):

| vendor | n | median out | median seconds | **ms per output token** |
|---|---|---|---|---|
| claude · haiku | 15 | **11 950** | 135.1 | **11.67** |
| codex | 24 | 1 948 | 46.7 | **26.16** |
| antigravity | 18 | 1 102 | 12.4 | **11.44** |

**Haiku generates at the same rate as antigravity, and at less than half the cost per token of
codex.** It is not slow. It takes longer because it writes **6× more than codex and 11× more than
antigravity** on the same role, from the same prompt.

The per-token rate is the measurement that settles it: if the input were the cause, haiku's
milliseconds-per-output-token would be inflated along with everything else. It is not — it is the
joint fastest of the three.

The prompts already ask for restraint. `prompts/plan-critique.md` says, in as many words, *"Three
real ones beat twelve padded ones, and an empty findings list is a valid answer."* Haiku answers with
twelve anyway, and **nothing downstream discards them**: `maxPerProvider` is a CONCURRENCY cap — how
many of one vendor's reviewers may run at once — not a cap on findings. Every one of those tokens
reaches the round and is read.

So bounding the answer is a PRODUCT decision, not a defect fix: `maxItems` on the findings array
would cut real findings, and this measurement says nothing about whether finding #9 was worth having.
It is left open deliberately.

## Finding 2 — `in` is not the same quantity for three vendors

The column that produced the wrong hypothesis is not comparable, and the code says why:

| vendor | what `in` counts | where |
|---|---|---|
| claude | `inputTokens + cacheCreationInputTokens + cacheReadInputTokens`, **summed over every model in `modelUsage`** | `ClaudeRuntime.Aggregate` |
| antigravity | `usage.input_tokens` alone — **no cache fields at all** | `AntigravityRuntime.ReadUsage` |
| codex | the MAXIMUM over `input_tokens`/`cache_*` keys, because its stream is cumulative | `UsageParser` |

Two consequences, both visible in the table above:

1. **Claude's figure counts cache READS, and a cache read is re-counted every turn.** An agentic loop
   that re-reads the same prefix six times reports six times the prefix. A trivial `{"findings":[]}`
   review — twenty output tokens — measured **29 513** input on claude against 12 593 on codex, and
   the whole difference is Claude Code's own harness being cached and read back.
2. **Antigravity's figure omits cache entirely**, so it is the same measurement with a term missing.

Nothing here is a billing error — every vendor's number is what that vendor reported. But the three
are placed side by side in the spending view and in `usage.jsonl`, where they read as one quantity,
and this investigation is the evidence that they mislead when they are. Normalising them is its own
change: see [../todo/PLAN_usage_that_compares.md](../todo/PLAN_usage_that_compares.md).

## What was NOT the cause

Checked and eliminated on the live host before the measurement above:

- **No MCP servers** are configured in the claude slot (`mcpServers: []` in its 41 KB `.claude.json`).
- **No user skills, agents or plugins** beyond the built-ins (`~/.claude/` holds only `backups`,
  `cache`, `history.jsonl`, `plugins`, `projects`, `sessions`, `settings.json`).
- **The prompt is identical** for all three vendors — the round composes one text per role and posts
  the same string.

The harness itself is the constant ~28 K, and it is the same for every review that vendor runs.

## Method

`usage.jsonl` on the deployed server, all rows with `outcome == "ok"`, grouped by provider; medians
rather than means because two antigravity runs answered in single-digit seconds with 31 output tokens
and would otherwise dominate. Rows with `out <= 100` are excluded from the per-token rate: a
twenty-token answer is measuring start-up, not generation.
