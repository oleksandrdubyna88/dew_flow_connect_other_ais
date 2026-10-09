# RESULTS — what spends the subscription: rounds, consultations and tokens, 2026-09-07 → 2026-10-09

> Status: **record, 2026-10-09.** The measurement behind [PLAN_every_round_is_counted.md](../todo/PLAN_every_round_is_counted.md).
> Read-only: nothing was changed on the Team server or on this machine while it was taken.

## The question

The operator: *"my plan used to allow about 250 rounds comfortably, now it hits its limit at about 150. The same
account is used on the Team server, but I do not see a big load there. Either the server carries far more than it
looks, or something happened to the plan."*

Nothing in coai can answer that today: no surface counts **rounds** or **consultations**, the server does not know what
a round is, and no vendor's own quota gauge is read anywhere. So the answer below is put together by hand from three
ledgers.

## Sources and method

| Source | What one line / row is | Rounds counted as |
|---|---|---|
| Team server `usage.jsonl` (4 091 lines, 2026-09-07 → 2026-10-09) | one vendor launch (one role, one vendor) | `PlanCritique` lines (one per plan round per vendor) + `Architecture` lines (one per code round per vendor). Cross-checked: in the last 24 h `Architecture`, `SecurityReliability` and `UxDxPerformance` each appeared exactly 8 times per vendor, and the extra `Conventions` lines were exactly the failed launches. An UPPER bound: `PlanCritique` lines also include client retries and the deploy canary, and every account slot is counted while only one spends this plan |
| Local `coai.db`, Windows store | `rounds` row + its `reviewers` rows | distinct `rounds.id` with at least one reviewer whose provider is exactly that row's id (`codex`; `gemini` or `antigravity`). Team-server rows (`<server>-<vendor>`) are separate ids and are **not** added here — those rounds are in the server column |
| Local `coai.db`, WSL store (copied, then read) | same | same |
| Local `usage.jsonl`, both stores | one reviewer's, consultation's or question's final outcome, with its model and kind | — (tokens per launch, and everything that is not a round) |

The two local stores are one person's two sides of the same machine: `coai-mcp` resolves its data directory per
side (`%LOCALAPPDATA%\coai-mcp` on Windows, `~/.local/share/coai-mcp` in WSL), so a session started in WSL writes the
WSL store. They are added, not deduplicated — a round lives in exactly one of them. Weeks start on Monday, UTC. The
week of 2026-10-05 is Monday to Friday 17:00 UTC, about 4.7 days.

## 1. Rounds per week — the server is the smaller share, and it is not growing

**Codex** (rounds that included a codex reviewer):

| Week | Windows | WSL | Team server (upper bound) | Total |
|---|---:|---:|---:|---:|
| 2026-09-14 | 280 | 122 | 247 | **649** |
| 2026-09-21 | 194 | 182 | 269 | **645** |
| 2026-09-28 | 90 | 189 | 161 | **440** — both vendors' limits hit on 2026-10-03 |
| 2026-10-05 (4.7 days) | 193 | 221 | 137 | **551** |

**Gemini** (rows `gemini` and `antigravity`, both the antigravity runtime):

| Week | Windows | WSL | Team server (upper bound) | Total |
|---|---:|---:|---:|---:|
| 2026-09-14 | 280 | 122 | 248 | 650 |
| 2026-09-21 | 194 | 182 | 269 | 645 |
| 2026-09-28 | 149 | 211 | 207 | 567 |
| 2026-10-05 (4.7 days) | 117 | 184 | 42 | 343 — the weekly quota ran out again on 2026-10-09 |

The local codex and Gemini counts are identical in the first two weeks because every local round then ran both
vendors; they part from 2026-09-28, when one vendor was out of quota for days at a time.

The server is at most 25–42 % of codex rounds. Per day its codex launches were 88 (week of 09-21), 72 (09-28) and
83 (this week) — flat, not rising. Two teammates make almost all of its lines since 2026-09-14; the operator's own
account is about 2 % of the whole ledger, nearly all in the first week. The server is not the hidden consumer.

## 2. What changed per reviewer — the models

Local `usage.jsonl` (Windows), code-round reviewers that answered, median per reviewer launch. Each row ran on its
vendor's own CLI as coai launches it: the codex rows on `codex exec` (codex-cli; 0.160.0 on 2026-10-09), the gemini
rows on the antigravity CLI `agy` (1.3.2 on 2026-10-09). The ledger records no CLI version per line, so the version
at each date in the table is not known — story 3.4 of the plan adds it:

| Row | CLI | Model | Days | Output tokens (median) |
|---|---|---|---|---:|
| codex | `codex exec` | `gpt-5.6-luna` | 2026-09-21 → 22 | 7 017 – 9 048 |
| codex | `codex exec` | `gpt-6-luna` | 2026-09-23 → 10-02 | 162 – 937 |
| codex | `codex exec` | `gpt-6-luna` | 2026-10-06 → 10-09 | 1 629 – 2 031 |
| gemini | `agy` | `gemini-3.8-flash-low` | 2026-09-21 → 10-02 | 506 – 1 352 |
| gemini | `agy` | `gemini-3.8-flash-medium` | **2026-10-02** → 10-09 | **32 635 – 40 717** |

- **Gemini's output per reviewer went up roughly 25–45× the day the row moved to `flash-medium`** (Windows medians
  above; WSL 1 297 → 35 551, 27×). The quota ran out the next day (2026-10-03, a weekly quota, ~87 h), and again on
  2026-10-09: an antigravity consultant asked at ~17:00 UTC answered `RESOURCE_EXHAUSTED (code 429): Individual quota
  reached … Resets in 110h25m`. Google does not publish how that quota is computed, so this is a correlation with a
  timing, not a proof — but it is the largest single change in the record.
- The Team server never switched: it still runs `gemini-3.8-flash-low` and `gpt-5.6-luna`, and its antigravity output
  stayed at 2–5 k per launch.
- **Codex: tokens do not show the reviewer getting more expensive** — `gpt-6-luna` writes FEWER output tokens per
  reviewer than `gpt-5.6-luna` did. Three settings changed on this machine in the window, none visible in any coai
  surface, and whether each costs the plan more per token is the vendor's weighting, which coai does not read:
  1. the row's model, `gpt-5.6-luna` → `gpt-6-luna`, on 2026-09-23;
  2. the service tier: [RESULTS_fast_mode_vendors.md](RESULTS_fast_mode_vendors.md) found `service_tier = "priority"`
     in `~/.codex/config.toml` on 2026-10-06 (about 2–2.5× usage, per the vendor documentation quoted there); it was
     `default` by 2026-10-07 ([RESULTS_fast_mode_measured_2026-10-07.md](RESULTS_fast_mode_measured_2026-10-07.md));
  3. the reasoning effort: the same file says `model_reasoning_effort = "high"` today. A local codex reviewer row
     sends no effort, so every local codex reviewer inherits it. When that line was written is not recorded.
- Input per reviewer moved less: local plan-round reviewers rose from ~15 k (early September) to ~33–35 k while the
  median plan text FELL (5.8 k → 4.9 k characters) — the growth is in what coai wraps around the plan. Server code
  roles rose from ~40 k to ~60 k median in the last week (`Conventions` 69 k).

## 3. What is not a round — consultations and questions, on codex's heaviest model

Local `usage.jsonl`, codex only (all on `codex exec`; consultations and question rows on `gpt-6-astra`), launches and tokens (input + output, cached input included) per week:

| Week | Store | Review launches | Review tokens | Consult + question launches | Consult + question tokens |
|---|---|---:|---:|---:|---:|
| 2026-09-14 | Windows | 650 | 32.6 M | 24 | 9.0 M |
| 2026-09-14 | WSL | 272 | 13.6 M | 5 | 0.2 M |
| 2026-09-21 | Windows | 449 | 21.0 M | 25 | 14.2 M |
| 2026-09-21 | WSL | 428 | 21.5 M | 14 | 2.8 M |
| 2026-09-28 | Windows | 228 | 13.6 M | 15 | 5.6 M |
| 2026-09-28 | WSL | 471 | 21.9 M | 37 | 6.4 M |
| 2026-10-05 | Windows | 540 | 30.5 M | 45 | **43.2 M** |
| 2026-10-05 | WSL | 580 | 30.3 M | 78 | **23.6 M** |

- **This week the codex consultations and question rows carried about as many tokens as every codex review together
  (66.8 M against 60.8 M)**, from about one launch in ten. They run on `gpt-6-astra`, read the checkout, and take a
  median 250 k–650 k input tokens per launch (up to 2.45 M); on 2026-10-06 thirty consultations on Windows carried
  31 M tokens in one day. From 2026-10-07 the ledger records 85–93 % of that input as cached, which codex weighs
  lower — how much lower is, again, the vendor's.
- None of this shows as a round, so a count of rounds before the limit falls whenever consultations rise — without the
  vendor changing anything. The cadence consultations and the question consultant both arrived in this window.
- Gemini consultations and questions are small (0.4–0.7 M a week); their cost is in the reviewers (§2).

## 4. What the server ledger cannot say

| Gap | Effect on the question | Where |
|---|---|---|
| No round id, job id, account slot or attempt on a line | rounds can only be estimated, as above; retries and repair launches look like extra work | `src_server/src/Jobs/JobRunner.cs:228-256` |
| Cached and reasoning tokens are never written (0 on every one of 4 091 lines) | codex weighs cached input lower; a line cannot say how much of its input was cache | `ReviewAttempt.Answered` carries two longs (`src_server/src/Jobs/ReviewLauncher.cs:36`), `JobRecord` holds only `TokensIn`/`TokensOut` (`src_server/src/Jobs/JobRecord.cs:86-87`), and the bare-count `UsageLedger.RecordJob` (`src_mcp/runners/Reviewers/UsageLedger.cs:220`) is used instead of the `Usage` one (`:258`) |
| A cancelled launch is written `TimedOut`, 0 tokens | the 1.6 s and 7.9 s "timeouts" of 2026-10-09 16:23 and 16:32 were the client withdrawing jobs, not slow vendors; their usage is unknown, not zero | `ProcessLauncher.cs:342` sets `Cancelled`, `ReviewerExecutor.cs:791-797` reads only `TimedOut` |
| An agy refusal's `Resets in 110h25m` is not read | the slot is parked on the 30-minute doubling guess (capped at 5 h) instead of the 110 h the vendor stated, and retried into the refusal | `src_server/src/Slots/CooldownParser.cs` has no duration pattern (`:136`, `:141`, `:146` read a clock time, a date, "weekly") |
| The deploy canary spends one real review per vendor per release, filed as `review` | indistinguishable from a person's review except by the canary token's email | `deploy/systemd-release.sh:159-218`, `:391-412` |

## What this settles and what it does not

- **Settled:** the Team server is not the cause. It is at most a quarter to two fifths of codex rounds, flat per day,
  and it runs the cheaper models.
- **Settled for Gemini, as a correlation:** the row moved to `flash-medium` on 2026-10-02; output per reviewer went up
  25–45×; the weekly quota ran out the next day and again a week later.
- **Consistent with, not proven, for codex:** half of this week's codex tokens went to consultations and questions on
  `gpt-6-astra`, which no round count sees; the reviewers moved to `gpt-6-luna` with an inherited `high` effort and,
  until about 2026-10-06, the `priority` tier. Which of these the vendor weighs most cannot be read from tokens.
- **Not settled:** whether either vendor also changed the plan itself. Answering that needs the vendor's own gauge
  (percent of window used) next to a count of rounds AND consultations, over a week with the settings held still.
- **Not settled:** the operator's "250" and "150". The totals above are larger than either, so those numbers count
  something narrower (one store, one window, or one vendor) — asked, not guessed.
