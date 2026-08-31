# module: runners — spawn, isolate, survive

> `src_mcp/runners/CoaiMcp.Runners` — the impure ring around [module_core.md](module_core.md):
> processes, worktrees, the scheduler. It carries data to and from the core and adds no rules.

## Purpose

Turn "run six reviewers" into something that survives a moving checkout, a killed session, a hung
CLI and a rate limit — with every failure mode named.

## Flow

```mermaid
sequenceDiagram
  participant S as server (epic 04)
  participant W as WorktreeManager
  participant C as ContextAssembler
  participant B as BoundedScheduler
  participant E as ReviewerExecutor
  participant V as vendor CLI
  S->>W: ResolveShaAsync(branch) → AddAsync(sha) [one lease per ROUND]
  S->>C: CollectAsync(base, sha) — numstat, per-file diffs, exclusions
  S->>B: RunAllAsync(work[], executor)
  B->>E: per job, under global(3) + per-provider(2) semaphores
  E->>V: launch (read-only, ephemeral); timeout kills the TREE
  V-->>E: -o file (codex) / stdout envelope (gemini)
  E-->>B: Ok | NonZeroExit | TimedOut | Unparseable | RateLimited
  B-->>S: outcomes → ReviewerSummaryFactory → "N of M answered"
  S->>W: lease.DisposeAsync() — the finally
```

## Core entities

| Type | File | Role |
|---|---|---|
| `IProcessLauncher` / `ProcessLauncher` | `Processes/ProcessLauncher.cs` | the ONE process seam; timeout kills the entire tree; `StdIn` carries every long or multi-line input, BOM-less UTF-8, and a child that exits before reading is not an exception |
| `ExecutableResolver` | `Processes/ExecutableResolver.cs` | npm Windows shims: the PATHEXT resolution `Process.Start` does not do |
| `WorktreeManager`, `WorktreeLease` | `Worktrees/WorktreeManager.cs` | one detached tree per round, `coai-wt-` prefix under OUR storage; prune-on-open; disposal = finally; never touches a human's worktree |
| `DiffExclusions`, `ContextAssembler` | `Context/ContextAssembler.cs` | numstat → per-file diffs with `:(exclude,glob)` pathspecs; binary sizes via `cat-file -s` |
| `IReviewerRuntime`: `CodexRuntime`, `DeepseekRuntime`, `GeminiRuntime`, `ClaudeRuntime`, `CustomCodexRuntime` | `Reviewers/ReviewerRuntime.cs`, `ClaudeRuntime.cs`, `CustomRuntime.cs` | THE vendor adapter: `Build` (argv, pure) + `ReadAnswer` + `ReadUsage`, the last two with working defaults. Flags verified against codex 0.147.0 / gemini 0.55.1 / claude 2.1.197; keys ride env, never argv; DeepSeek = Codex config-shifted |
| `ReviewerRuntimeSelector` | same | unknown provider refuses naming the catalog |
| `ReviewerOutcome` (closed), `ReviewerExecutor`, `RateLimit` | `Reviewers/ReviewerExecutor.cs` | one launch + one repair; SIX named outcomes incl. NotStarted; `Ok` carries the run's `Usage`, both launches counted when repaired |
| `Usage`, `UsageParser` | `core/Findings/UsageParser.cs` | schema-less scan over any vendor envelope; MAX per key name then sum per category, so a streamed cumulative total is never summed with itself; money only when the vendor priced the run |
| `BoundedScheduler`, `ReviewerWork`, `ReviewerSummaryFactory` | `Reviewers/BoundedScheduler.cs` | global + per-provider semaphores; one rate-limit retry after backoff |

## The decisions a reader needs

- **One worktree per round, by SHA** — six read-only reviewers share one tree; six checkouts of a
  moving branch would be six different inputs to one comparison.
- **Provider cap beside the global cap** — a rate limit is per vendor; a global cap alone puts all
  its slots on one provider.
- **Codex reads from `-o`, Gemini from stdout, Claude from its JSON envelope's `result`** — each
  through its OWN adapter, because where an answer lands is vendor knowledge; exit-0-with-no-output
  is `Unparseable`, never `Ok`.
- **Adding a vendor is one class, not an edit to the executor** — `IReviewerRuntime` carries launch,
  answer and usage; the executor asks the adapter and knows no vendor's name.
- **Tokens come from the vendor, money only when the vendor prices it** — claude reports
  `total_cost_usd`, codex and gemini report tokens alone. A price table of our own would be wrong
  within a month, and a wrong number is worse than an absent one, so `costUsd` stays null and the
  UI says "no cost reported" rather than "$0.00".
- **Subset counts are never added** — codex's `cached_input_tokens` and `reasoning_output_tokens`
  are inside the totals beside them (measured: 14149 in / 9984 cached for one call), while claude's
  `cache_creation_input_tokens` and `cache_read_input_tokens` are additional and billed.
- **The fake CLI** (`src_mcp/tests_fakecli`) is the whole vendor surface in tests: emit / emit-to /
  stderr-exit / sleep / busy (start/end ticks for overlap measurement) / flip (first-launch
  failure), with launch counting. CI never touches a vendor.

## Two delivery rules the first real run wrote

Both cost a whole run each; see [RESULTS_first_real_run.md](RESULTS_first_real_run.md).

- **No argument may ever contain a newline.** On Windows the vendor CLIs are npm `.cmd` shims, so
  cmd.exe parses our argv and truncates an argument at its first newline — silently, so the model
  answers as if it had been handed nothing. The prompt therefore travels on **stdin**: `codex … -`
  (its documented "instructions from stdin"), and a one-line `-p` pointer for gemini, which appends
  `-p` to stdin. A test asserts the rule for all three runtimes.
- **A bare command name is not startable.** `Process.Start` does not read `PATHEXT`, so it finds
  npm's extensionless shell script and fails. `ExecutableResolver` tries the executable extensions
  first and the bare name last, and never rewrites an explicit path.

## External dependencies

`git` on PATH (worktrees, diffs); the vendor CLIs only at real runtime, never in tests.
