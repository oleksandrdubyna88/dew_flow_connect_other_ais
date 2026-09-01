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
  V-->>E: -o file (codex) / stdout envelope (gemini) / NDJSON result (antigravity)
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
| `IReviewerRuntime`: `CodexRuntime`, `DeepseekRuntime`, `GeminiRuntime`, `ClaudeRuntime`, `AntigravityRuntime`, `CustomCodexRuntime` | `Reviewers/ReviewerRuntime.cs`, `ClaudeRuntime.cs`, `CustomRuntime.cs` | THE vendor adapter: `Build` (argv, pure) + `ReadAnswer` + `ReadUsage`, the last two with working defaults. Flags verified against codex 0.147.0 / gemini 0.55.1 / claude 2.1.197 / agy 1.1.22; keys ride env, never argv; DeepSeek = Codex config-shifted |
| `ReviewerRuntimeSelector` | same | unknown provider refuses naming the catalog |
| `ReviewerOutcome` (closed), `ReviewerExecutor`, `RateLimit` | `Reviewers/ReviewerExecutor.cs` | one launch + one repair; SIX named outcomes incl. NotStarted; `Ok` carries the run's `Usage`, both launches counted when repaired |
| `Usage`, `UsageParser` | `core/Findings/UsageParser.cs` | schema-less scan over any vendor envelope; MAX per key name then sum per category, so a streamed cumulative total is never summed with itself; money only when the vendor priced the run |
| `BoundedScheduler`, `ReviewerWork`, `ReviewerSummaryFactory` | `Reviewers/BoundedScheduler.cs` | global + per-provider semaphores; one rate-limit retry after backoff |

## The decisions a reader needs

- **One worktree per round, by SHA** — six read-only reviewers share one tree; six checkouts of a
  moving branch would be six different inputs to one comparison.
- **Provider cap beside the global cap** — a rate limit is per vendor; a global cap alone puts all
  its slots on one provider.
- **Antigravity (`agy`) is the closest fit to this product's contract**: `--json-schema` puts the
  finding schema straight into `result.response`, the same envelope carries `usage`, and the model
  ids carry their own reasoning effort. Its prompt rides `--input-format stream-json` on stdin as
  one NDJSON line (`{"event":"user","message":{"role":"user","content":"..."}}`) because `--print`
  takes its prompt as a flag VALUE and a review prompt is ~33 KB — past the Windows command line.
  It exists because Google retired Gemini Code Assist for individuals mid-2026-08-31: the Gemini
  CLI now fails in `_doSetupUser` with "migrate to the Antigravity suite", before any model is
  reached, which was mistaken in turn for a quota, a timeout and an untrusted folder.

- **Codex reads from `-o`, Gemini from stdout, Claude from its JSON envelope's `result`** — each
  through its OWN adapter, because where an answer lands is vendor knowledge; exit-0-with-no-output
  is `Unparseable`, never `Ok`.
- **Adding a vendor is one class, not an edit to the executor** — `IReviewerRuntime` carries launch,
  answer and usage; the executor asks the adapter and knows no vendor's name.
- **Tokens come from the vendor, money only when the vendor prices it** — claude reports
  `total_cost_usd`, codex, gemini and antigravity report tokens alone. A price table of our own would be wrong
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

## Token accounting is per vendor, because a shared rule is wrong for one of them

| Vendor | Cache tokens | Reads |
|---|---|---|
| codex | `cached_input_tokens` is a SUBSET of `input_tokens` — adding it double-bills | the generic scan over `--json` events |
| claude | `cache_creation_input_tokens` / `cache_read_input_tokens` sit BESIDE the input count and are both billed | `modelUsage`, not `usage` — see below |
| antigravity | `thinking_tokens` inside `output_tokens`, `cache_read_tokens` inside `input_tokens`; its own `total_tokens` = in + out proves it | `result.usage` on the stream's result event |

Claude reports the SAME run twice and the two disagree: measured on a real call, `usage` said 10
input / 44 output while `modelUsage` said 532 / 57. `usage` is the last message's usage;
`modelUsage` is the aggregate across every turn, which is what a multi-turn review actually
consumed. Five tests pin all of this to envelopes captured from real calls.

## Evidence is kept, never summarised away

Three failures cost hours because the reason was discarded at the last step:

- a non-zero exit reported as `exit 1` while the executor held the stderr → the reason travels with
  the code now, chosen by CONTENT (the first line that announces an error), because "the last line"
  picks node's version banner on exactly the vendor CLIs this product drives;
- a rate limit reported without saying WHICH limit → a daily quota and a per-minute throttle read
  identically and only one is worth retrying, so the vendor's words travel and a hopeless limit
  skips its retry;
- an unparseable answer whose text was thrown away → kept under `<dataDir>/unparseable/` now, with
  the outcome naming the file. The one replayed by hand afterwards succeeded, which is precisely
  the case where the raw text is the whole story.

### The Gemini retirement (2026-09-01)

Google closed Gemini Code Assist for individual accounts. The CLI now fails inside `_doSetupUser`,
BEFORE it reaches a model, so every symptom it produces belongs to something else — three
observers read the same failure as a daily quota, a timeout and an untrusted directory.

`AntigravityRuntime` shipped on 2026-08-31 and **nothing used it for a day**: no preset offered it,
every default still named `gemini`, and a reviewer list saved before the retirement went on naming
the closed door. Supporting a vendor and DEFAULTING to it are different changes, and only the first
had been made. What changed on 2026-09-01:

| Where | Was | Is |
|---|---|---|
| `PanelSettings.Providers` | codex, gemini, deepseek(off) | codex, **antigravity**, deepseek(off) |
| `COAI_PROVIDERS` fallback | `codex, gemini` | `codex, antigravity` |
| `PanelSettings.Translator` | `gemini` / `gemini-flash-latest` | `antigravity`, model unset |
| extension `DEFAULT_VENDORS` | codex, gemini | codex, **antigravity** (`gemini-3.7-flash-high`) |
| extension presets | no Antigravity entry at all | Antigravity first-class; Gemini kept, marked retired |
| a SAVED `runtime: "gemini"` | run as-is | migrated to `antigravity` (id kept — it names the row, the usage history and the vault key) |
| `providers` health | `--version` exits 0 ⇒ "own auth" | `VendorDiagnosis.ForRuntime` answers **before** the probe |

That last row is the one worth remembering: `gemini --version` exits 0 because it prints a version
without ever reaching Google, so a probe built on `--version` is *structurally* incapable of seeing
the retirement. Green health on a dead vendor is worse than no health at all — it is why a round
was still being spent on it a day later.

A vendor with its own `baseUrl` is never migrated: that is not Google's CLI at all.
