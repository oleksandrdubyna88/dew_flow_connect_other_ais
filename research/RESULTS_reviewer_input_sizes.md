# RESULTS — what each reviewer is actually handed, measured from the ledger

> **Subject:** the reviewer rounds this repository ran between 2026-09-06 and 2026-09-08, against
> `coai-mcp` 0.18.6–0.18.10 as installed at the time. Pinned: `reviewerTimeoutMinutes` 10,
> `maxConcurrency` 3, `maxPerProvider` 2, Team server `https://coai.remsoft.dev`. Repository at
> `cb71762` when this was written.
>
> **Harness:** none, deliberately. The numbers are read straight out of the append-only spending
> ledger — one line per reviewer launch, written by `src_mcp/runners/Reviewers/UsageLedger.cs` into
> `usage.jsonl` under the data directory — filtered by its `utc` field and grouped by `provider`. No
> experiment was run and no arm was configured: these are real rounds, which is both the strength of
> the document and its limit.
>
> **Machine:** the operator's Windows workstation for the local rows, the Team server's box for the
> `remsoftdev-*` rows. That distinction matters for the seconds and not for the tokens.
>
> Related: [module_runners.md](module_runners.md), [module_team_server.md](module_team_server.md),
> [PLAN_team_server_reviewer_never_called.md](PLAN_team_server_reviewer_never_called.md).

## Why this was measured

The operator's report was about SPEED: *look into haiku, it takes unreasonably long.* (Translated
from their Russian; this repository's documentation is English.) Speed is the symptom people notice;
the ledger records what was actually consumed, so it can say whether a slow reviewer is a slow MODEL
or a reviewer being handed more to read.

## One round, three vendors, the same diff and the same prompts

Code round of 2026-09-07, 21:26–21:47. Every cell is one reviewer launch.

| vendor | role | seconds | tokens in | tokens out |
|---|---|---:|---:|---:|
| remsoftdev-codex | Architecture | 39.3 | 42,090 | 1,938 |
| remsoftdev-codex | SecurityReliability | 42.0 | 42,119 | 1,957 |
| remsoftdev-codex | UxDxPerformance | 59.6 | 42,096 | 3,103 |
| remsoftdev-antigravity | Architecture | 57.8 | 63,927 | 20,476 |
| remsoftdev-antigravity | SecurityReliability | 13.0 | 42,545 | 772 |
| remsoftdev-antigravity | UxDxPerformance | 9.2 | 42,546 | 894 |
| **remsoftdev-claude** | Architecture | **108.8** | **190,139** | 9,137 |
| **remsoftdev-claude** | SecurityReliability | **352.3** | **107,991** | 23,652 |
| **remsoftdev-claude** | UxDxPerformance | **668.8** | **487,037** | 29,754 |

**What these numbers license, and no more.** In this round the rows labelled `remsoftdev-claude`
received between 2.5× and 11× the input tokens of the rows labelled `remsoftdev-codex` for the same
three roles, and their durations rose with that input. Codex is flat at ~42,100 tokens across all
three roles; antigravity is flat at ~42,500 with one outlier; claude ranges from 108k to **487k on
the same round**.

The flat-versus-varying shape is the suggestive part: a number that does not move across three
different questions plausibly comes from the round, and one that moves elevenfold inside it
plausibly comes from the reviewer's own behaviour — an agentic CLI reading files.

**"Plausibly" is as far as this goes.** One round is one sample, nothing was controlled, and the
data cannot separate the model from the CLI, from the workspace it was given, or from what the Team
server does before launching it. What it does establish is where to look — at the input rather than
at the model's speed — which is a different and much cheaper question to answer next.

## The same shape across three days

Code-stage launches since 2026-09-06 that returned an answer:

| vendor | n | median tokens in | max |
|---|---:|---:|---:|
| remsoftdev-claude | 11 | 169,823 | 487,037 |
| gemini (local CLI) | 209 | 64,547 | 428,707 |
| codex (local CLI) | 226 | 54,454 | 441,987 |
| remsoftdev-antigravity | 21 | 56,356 | 70,887 |
| remsoftdev-codex | 24 | 42,096 | 79,415 |
| local (Ollama) | 226 | 35,300 | 77,012 |

The median claude reviewer reads **four times** what the median codex reviewer reads and **five
times** the local one.

## Durations, and the one that was over its deadline

| vendor | n | p50 s | p90 s | max s | over 600 s |
|---|---:|---:|---:|---:|---:|
| gemini | 226 | 24.7 | 115.1 | 351.6 | 0 |
| local | 226 | 47.0 | 96.6 | 285.2 | 0 |
| codex | 226 | 47.4 | 75.6 | 160.6 | 0 |
| remsoftdev-claude | 75 | 0.4 | 290.1 | 668.8 | 1 |
| remsoftdev-codex | 30 | 46.5 | 99.9 | 224.1 | 0 |
| remsoftdev-antigravity | 30 | 12.9 | 57.8 | 92.9 | 0 |

Two things fall out of this table, and both were reported by the operator before they were measured.

1. **One run exceeded its ten-minute deadline** — the 668.8 s claude reviewer, which then reported
   `ok`. That was a real defect and is fixed: `reviewerTimeoutMinutes` bounded each LAUNCH, and a
   reviewer makes up to two (the review, and a repair when the first answer will not parse), so a
   reviewer needing a repair could take twice its deadline. See the commit
   *"the deadline bounds the reviewer, not each launch it happens to make"*.
2. **`remsoftdev-claude`'s p50 is 0.4 s while its p90 is 290 s.** The fast ones are refusals; the
   290.1 s entries are `exit 78` — *queued on the Team server, N ahead of it*. That constant is a
   waiting deadline, not work, and it is why an average over this vendor means nothing.

## What this does NOT say

- **Which model.** The ledger records the model the panel was CONFIGURED with, not the one a Team
  server actually ran — see [todo/PLAN_the_log_names_the_model.md](../todo/PLAN_the_log_names_the_model.md).
  So "haiku" is the operator's word for the row, not something this data confirms.
- **Why claude reads more.** The measurement locates the cost in the input, not in its cause. The
  candidates are the workspace the server gives it (`codeWorkspace` is a client-side setting; a
  Team-server run is launched by the server) and the CLI's own appetite for exploring a checkout.
  Both are answerable, and neither is answered here.

## What to do with it

The next step is not a model change. It is to find out what the Team server hands a claude reviewer
and whether it can be given the same thing codex is given — 42k of prompt and diff, no checkout.
Costed at the observed numbers, that is roughly a **4× reduction in input tokens** on the vendor
that is both the slowest and the most expensive per round.
