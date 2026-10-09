# PLAN — every round is counted, and what it cost the subscription is visible

> Status: **plan only, nothing implemented yet, 2026-10-09.** Scope: the Team server's job ledger, `/api/usage` and
> job claim (`src_server`), the reviewer launch path both halves share (`src_mcp/runners/Reviewers`), `coai-mcp`'s
> round store (`--log` family), the extension's Spending tab, Team-server block and model cards (`src_vs_code`), and
> the deploy canary (`deploy/`).
>
> Measurement this plan is built on: [RESULTS_what_spends_the_subscription_2026-10-09.md](../research/RESULTS_what_spends_the_subscription_2026-10-09.md).
> Related docs: [module_team_server.md](../research/module_team_server.md),
> [module_server.md](../research/module_server.md), [module_extension.md](../research/module_extension.md),
> [module_runners.md](../research/module_runners.md).

## The symptom

The operator, 2026-10-09: *"add a counter. My plan used to allow ~250 rounds comfortably; now it hits the limit at
~150. This account is used on the Team server, but I do not see a big load there. Either the server carries far more,
or something happened to the plan."*

No surface in coai can answer that:

1. **Nothing counts rounds.** The Spending tab counts reviewer *launches* ("N runs") per row
   (`src_vs_code/src/usage.ts:157`, rendered by `src_vs_code/src/roundsLog.ts:927`); the Team-server block counts
   ledger lines (`src_vs_code/src/teamServerView.ts:79`). A round is one launch per vendor (plan) or up to four (code),
   plus retries, repairs and follow-ups — "runs" moves with settings, not with work.
2. **Nothing counts what is not a round.** Consultations and question rows spend the same subscriptions and appear
   in no count. This week they carried as many codex tokens as every codex review together (66.8 M against 60.8 M,
   RESULTS §3) — a falling "rounds before the limit" is exactly what they produce, with no vendor change at all.
3. **The server does not know what a round is.** One `POST /api/reviews` is one role for one vendor; the body
   (`src_mcp/runners/Reviewers/RemoteAsk.cs:96-101`, record at `:320-327`) carries nothing that ties launches
   together, and the ledger line has no round, job, slot or attempt (`src_server/src/Jobs/JobRunner.cs:228-256`).
4. **The server ledger under-reports and mislabels.** Cached and reasoning tokens are 0 on all 4 091 lines:
   `ReviewAttempt.Answered` carries two longs (`src_server/src/Jobs/ReviewLauncher.cs:36`, filled at `:196`),
   `JobRecord` holds `TokensIn`/`TokensOut` only (`src_server/src/Jobs/JobRecord.cs:86-87`), `JobRunner` drops a
   `NonZeroExit`'s usage (`JobRunner.cs:145-146`) and calls the bare-count `UsageLedger.RecordJob`
   (`src_mcp/runners/Reviewers/UsageLedger.cs:220`) instead of the `Usage` overload beside it (`:258`). A launch the
   client withdraws is written `TimedOut` with 0 tokens: `ProcessLauncher` marks it `Cancelled`
   (`src_mcp/runners/Processes/ProcessLauncher.cs:342`) and `ReviewerExecutor` reads only `TimedOut`
   (`src_mcp/runners/Reviewers/ReviewerExecutor.cs:791-797`) — the 1.6 s and 7.9 s "timeouts" of 2026-10-09 were
   withdrawals.
5. **A stated reset is ignored.** agy says `Resets in 110h25m`; `CooldownParser` reads a clock time, a date or
   "weekly" (`src_server/src/Slots/CooldownParser.cs:136`, `:141`, `:146`) and no duration, so the Team server parks
   an exhausted Gemini slot on its 30-minute doubling guess and asks again into the refusal.
6. **No vendor gauge is read, and what a launch ran with is invisible.** Nothing parses a quota percentage. A codex
   row's effort is never sent (`src_mcp/src/Server/Rounds/RosterBuilder.cs:680`), so a local launch inherits
   `~/.codex/config.toml` (`high` on this machine today), and the tier came from the same file until PLAN_fast_mode.

What the hand-built answer found: the server is at most 25–42 % of codex rounds and flat per day; the Gemini row moved
to `flash-medium` on 2026-10-02 (output per reviewer up 25–45×, quota out the next day and again on 2026-10-09);
half of this week's codex tokens went to consultations on `gpt-6-astra`; the codex reviewers moved to `gpt-6-luna`
with an inherited `high` effort and, until ~2026-10-06, the `priority` tier. Whether a vendor ALSO changed the plan
is the one question the data cannot settle — the counter has to make it settle-able next time, not just count.

## What must be true when this is done

1. A person sees, per vendor row, how many **rounds** ran in the last 5 hours, 24 hours and 7 days — plan, code,
   document and feature separately — beside the **consultations, question rows and chat turns** that spent the same
   vendor, and how many recorded turns and launches each took. Locally from day one, history included. Windows are trailing everywhere,
   so the two halves never disagree about where "today" starts.
2. On the Team server, an admin sees rounds **per vendor**, counted by a round identity the client sends; lines from
   clients that send none are their own number, never folded into a guess. `people[].vendors[]` carries the same
   fields; rendering the per-PERSON list is [PLAN_team_usage_by_person.md](PLAN_team_usage_by_person.md)'s.
3. Where a vendor exposes it, the **vendor's own gauge** is shown — percent of the 5-hour and weekly window used and
   when it resets — and **rounds per 1 % of the weekly window**, but only over every known consumer of that account;
   when a consumer is known and not counted, the number is not shown and the reason is.
4. Every ledger line says what the launch **ran with** — model, effort and tier as actually sent, each with its source
   — and what it **was**: first launch, retry, repair or follow-up; review, consult, question, chat or canary.
5. A withdrawn launch is recorded as withdrawn (and an abandoned or backstopped one as such), with its usage *not
   captured* rather than 0; cached and reasoning tokens reach the server ledger.
6. The server does not start a launch its caller can no longer wait for, and says so in a form the client reads
   without matching a sentence.
7. A stated reset (`Resets in 110h25m`) parks the slot until then, on both halves.

## Out of scope — said so it is not read as forgotten

| Item | Why not here |
|---|---|
| Changing anyone's models, effort, tier or consultation cadence | The operator's settings. The counter makes their cost visible; choosing is theirs. |
| Merging the Windows and WSL stores | A separate defect (one `coai-mcp` data directory per side). The counter **names the store it reads**, and says when the other side's store exists and is not included (story 2.2 — reading the other side's live WAL database across the OS boundary is not established safe). Counting it from inside the owning OS, or merging the stores, wants its own plan. |
| Making token columns comparable across vendors | [PLAN_usage_that_compares.md](PLAN_usage_that_compares.md) — boundary below. |
| Rendering the per-person list | [PLAN_team_usage_by_person.md](PLAN_team_usage_by_person.md) — boundary below. |
| A second account running concurrently | [PLAN_the_second_account_actually_runs.md](PLAN_the_second_account_actually_runs.md) (deferred). Story 4.3 removes the waste the serial queue causes; it adds no parallelism. |
| Comparing a stated wait with a reviewer's remaining deadline | [PLAN_a_stated_wait_is_read_rather_than_matched.md](PLAN_a_stated_wait_is_read_rather_than_matched.md) (blocked on samples) — boundary below. |

## Boundaries with the open plans

| Item | Built by | The other plan's part |
|---|---|---|
| Round fields in `/api/usage`'s `vendors[]` and `people[].vendors[]` | **this plan** (5.1) | [PLAN_team_usage_by_person.md](PLAN_team_usage_by_person.md) builds the admin-only **Team server** tab on the Review rounds page; this plan's 5.2 renders its server fields INTO that tab (not the old Team-server block), and the tab counts launches until they arrive |
| Cached + reasoning tokens and `usage not captured` on server lines | **this plan** (3.1) | PLAN_usage_that_compares decides what a token COLUMN means per vendor; this plan stops the server dropping fields |
| A `5h` / `24h` window and the multi-window query on `/api/usage` | **this plan** (5.1) | [PLAN_the_usage_page_reads_the_window_not_the_history.md](PLAN_the_usage_page_reads_the_window_not_the_history.md) (deferred) — reopen condition unchanged; one scan answers every window, see *Growth* |
| Effort and tier with their source on every ledger line | **this plan** (3.4) | [PLAN_the_log_names_the_model.md](PLAN_the_log_names_the_model.md) step 3 owns the ledger's MODEL field; the effort already on `ReviewerState` came from [the shipped repair plan](../research/PLAN_the_log_names_every_model_and_its_effort.md) |
| The canary's `kind` | **this plan** (3.5) | [PLAN_a_credential_that_is_not_a_person.md](PLAN_a_credential_that_is_not_a_person.md) replaces the canary's credential; disjoint |
| A DURATION form (`Resets in 110h25m`) read into a slot's park time; the verbatim agy line captured | **this plan** (1.1, 5.3) | PLAN_a_stated_wait_is_read_rather_than_matched compares a stated wait with the reviewer's deadline in `Hopeless`; 1.1's agy sample becomes the first row of its gate table — it still needs a second vendor's |
| The follow-up launch's usage on one ledger line | [PLAN_the_reviewer_follow_up_is_billed_once.md](PLAN_the_reviewer_follow_up_is_billed_once.md) | this plan's 3.1 carries whatever usage that plan's sum produces and labels the line `followUp`; it does not change the sum |
| The vendor card's new line | **this plan** (2.4) | [PLAN_the_sidebar_pays_only_for_what_it_shows.md](PLAN_the_sidebar_pays_only_for_what_it_shows.md) measures a render's cost; this line reads a CACHED count (no spawn per render) so it adds nothing to measure |
| `JobRunner`, `JobStore` and `ProcessLauncher` edits | **this plan** (3.1, 3.2, 3.3, 4.3) | [PLAN_the_audit_of_2026_09_09_is_built.md](PLAN_the_audit_of_2026_09_09_is_built.md) and [PLAN_a_prompt_has_a_ceiling_and_a_finished_job_forgets_it.md](PLAN_a_prompt_has_a_ceiling_and_a_finished_job_forgets_it.md) touch the same files; their open stories are cut or declined, so nothing collides today — whichever reopens second rebases on the other |

Order: this plan first — it lands the fields; the others read them or are disjoint.

## Epics and stories

Six epics, each on its own branch from the previous epic's commit and ONE commit; the coai gate runs once per epic
(plan round on this document with the epic declared, code round over the epic's whole diff). Every surface story
carries its own docs: the `module_*.md` paragraph, and help text in all five languages where a person sees it.
**Model per story** is named; Fable where being wrong is company-wide or silent, Opus for a decided shape with a
shipped twin.

### E1 — measure before building (no product code) · `feat/round-counter-e1-measure`

- **1.1 Where each vendor states its gauge, and who shares an account** (Fable) — `research/RESULTS_vendor_quota_gauges.md`,
  on codex-cli 0.160.0 / agy 1.3.2, both sides and the server host:
  - codex `exec --json` events from one real reviewer launch's stdout (not documentation): any rate-limit
    percentage, window or reset;
  - whether `codex app-server`'s rate-limit read answers a ChatGPT sign-in **without spending a turn**, and whether
    it can rotate the OAuth refresh token (if it can, it may only ever run inside the slot's lease — two processes
    rotating one refresh token signed the account out during the load campaign);
  - whether codex echoes the effective effort and tier anywhere;
  - agy's full refusal text, captured from stderr unclipped (the logs clip it at `Resets in 110h12m4…`), and any
    gauge besides it;
  - **which consumers share each account**: this machine's two stores, the server slots, anything else the
    operator names — the input 6.2's scope rule needs;
  - **a paid-path census**: every code path that sends a request a vendor bills, and whether it writes a ledger line.
    Known to write nothing today: the extension's claude probe (`src_vs_code/src/claudeProbe.ts:71`), `ProbeApiMode`
    (`src_mcp/src/Api/ProbeApiMode.cs:37`), a server attempt requeued onto another account (`JobRunner.cs:169-173`) and
    a job ended by the host stopping (`JobRunner.cs:113-120`). Known to fold two launches into one line: a repair
    (`ReviewerExecutor.cs:565`) and a follow-up. Story 3.3 closes what this census lists.
  **Decides E6's scope.** No codex source → E6 is its record story alone, folded into E5.
- **1.2 Server ledger bodies as fixtures** (Fable) — one `ok`, one withdrawal-written-as-`TimedOut`, one
  `RateLimited` line from the live server, redacted (no email, no prompt), under `src_server/tests/`, with a check
  that no fixture line contains `@`.

### E2 — the local counter · `feat/round-counter-e2-local-counter`

Answers the operator's question for this machine's work from the whole history, before any server change.

- **2.1 `coai-mcp --round-counts`** (Opus) — a mode beside `--log` (`src_mcp/src/Program.cs:298`), answering ALL
  windows (5 h / 24 h / 7 d) in one call, `--now <utc>` for tests:
  - from `coai.db`: distinct rounds per vendor row × stage (plan, code, document, feature) — `rounds` ⨝ `reviewers`,
    `rounds_by_time` (`src_mcp/src/Store/Schema.cs:117`) plus an index on `reviewers(round_id)` (`Schema.cs:79-87` has
    none);
  - from `usage.jsonl` — read from the END and stopped at the oldest window's start (7 days), never the whole history:
    the rounds themselves come from the indexed `coai.db` query, and the ledger is only needed for the windows (a
    re-scan of a year of history per refresh is what the code round refused) — per vendor row × model × kind (review,
    consult, question; chat from `chat-usage.jsonl`): **recorded turns** (a ledger line is one terminal turn — a repair or a follow-up is two launches on one line, so a
    line is never called a launch), median input / output, outcomes. History before story 4.1 has no round id on
    ledger lines, so its per-round figure is labelled **reviewers per round** (from `coai.db`); **launches per round**
    appears only for lines written after 3.3 (one line per launch) that carry 4.1's round.
  - Team-server rows (`<server>-<vendor>`) are their own lines, labelled *ran on <server>*, never added to the
    server's own totals.
- **2.2 The extension reads it** (Opus) — `readRoundCounts` in `roundsDbRead.ts` through its existing spawn helper;
  exit 64 / timeout / unparseable → *count unavailable*, never 0. **Bounded freshness**, not "until a round ends": the
  answer is reused for at most 60 s and refreshed early when a round, consultation or question ends — a
  consultation-only session must still move the numbers, and a trailing window must fall while nothing runs (fake-clock
  tests for both). So the card line in 2.4 is never a spawn per render. Names the store (`this store: Windows` / WSL)
  and, when the other side's store exists, says in one sentence that its rounds are NOT included. It does not open the
  other side's `coai.db`: the store is in WAL mode (`src_mcp/storage/SqliteMigrator.cs:68`), and WAL needs shared
  memory and locks that a read across the `\\wsl.localhost` / drvfs boundary does not coordinate — a copied file read
  fine for the RESULTS, a live one is not established safe. Counting the other side by running `--round-counts`
  inside the owning OS is the open tail, not this story.
- **2.3 Spending tab, first block: Rounds** (Opus) — a pure `roundsBlock` renderer (vendor row · model lines · 5 h ·
  24 h · 7 d by stage · consultations / questions / chat · per-round figure · median output per launch · the store
  sentence). A model change inside a window is two lines for that row, so the step of 2026-10-02 is visible the day it
  happens. **A bundled-page test** (`src_vs_code/src/test/bundledPage.test.ts` on `roundsLogPageHarness.ts`) RUNS the
  Review rounds page and asserts the block appears with counts, and with an unavailable read shows *count unavailable*
  and no zero.
- **2.4 Model card line** (Opus) — `7 days: 214 rounds · 31 consults · 5 h: 12` on the card (`src_vs_code/src/modelCard.ts`),
  from the cached read.

### E3 — the ledger tells the truth · `feat/round-counter-e3-ledger-truth`

- **3.1 The whole usage travels** (Opus) — field preservation only:
  - `ReviewAttempt.Answered` (`ReviewLauncher.cs:36`) carries a `Usage`; so do `NonZeroExit` (dropped today at
    `JobRunner.cs:145-146`) and a continuation that ends badly (`ReviewLauncher.cs:155-164`); `JobRecord` carries a
    `Usage` instead of two longs; `JobRunner.Ledger` uses the `Usage` overload (`UsageLedger.cs:258`).
  - `UsageEntryDto` (`src_server/src/ServerJsonContext.cs:134`) and `UsageReader`'s `UsageLine`
    (`src_server/src/Usage/UsageReader.cs:121-158`) read cached, reasoning and `usageNote`.
- **3.2 How a job ended is said, not guessed** (Fable) — lifecycle and races, after 3.1:
  - `ReviewerExecutor` checks `result.Cancelled` before `result.TimedOut` (`ReviewerExecutor.cs:791`) and returns a
    NEW `ReviewerOutcome.Cancelled` with `Usage.Unknown` — and **keeps `AbandonAsync` in that branch**: it is what
    DELETEs a withdrawn remote job today, and an early return without it leaves the job running on the company's
    subscription.
  - `JobStore.Cancel` (`:303`) and `JobStore.Expire` (`:469`) both store `FailureKind.Cancelled` today, told apart only
    by a sentence — so a structured **end cause** (`withdrawn` / `abandoned` / `backstop` / `hostStopped`) is set AT
    the transition and carried on the record; the ledger reads it from the STORE's terminal record, not from
    `JobRunner`'s own recomputed `finished` (`JobRunner.cs:176-177`). A genuine deadline stays `TimedOut`.
  - Both cancellation shapes produce **exactly one** ledger line with any earlier usage kept: a cancelled
    `ProcessResult` and a thrown `OperationCanceledException` (`JobRunner.cs:113-120` writes none today).
- **3.3 Every paid launch is written, and written whole** (Opus) — closes 1.1's census: the probes write a line of
  kind `probe`; a requeued server attempt writes its own line (`attempt: rotated`) instead of nothing; **one ledger line per
  paid launch** — a repair or a follow-up writes its own line with its own usage and outcome, linked to the first by a
  shared `turn` id, so retries, repairs and follow-ups are counted from lines rather than inferred (where a vendor
  reports usage only cumulatively across the two launches, as agy does, the second line carries the difference and
  says so); vendor reasons are kept whole in the ledger and the log
  (clipped at ~100 characters today, which is how this plan lost the verbatim weekly line).
- **3.4 Each line says what it ran with** (Opus) — trailing, defaulted `UsageEntry` fields (`UsageLedger.cs:61`):
  `cli` and `cliVersion` (which CLI ran the launch and its version — a cross-vendor measurement must say it, and today's
  ledger cannot), `effort`/`effortFrom` and `tier`/`tierFrom` as actually SENT, after the server's `Dropped` (a codex row's own effort
  setting shows as *ignored*; `src_server/src/Jobs/ClientOptions.cs` drops a remote codex effort), with `cli-config`
  read only to LABEL an empty value, never to change it; server lines add `job` and `slot` (the slot name, never the
  account's email) and label the tier from the slot's own config — the request carries none.
- **3.5 The canary is a canary** (Opus) — `deploy/systemd-release.sh:171` sends `"kind":"canary"` when the target's
  `/api/health` version knows it (a `--canary-only` against an older binary would otherwise be a 400); `JobKind.Canary`
  with its row in `JobKinds.Refusal` (`src_server/src/Jobs/JobKind.cs:109`) and `UsageKinds.Canary` (the parity test
  keeps the two vocabularies level); `/api/usage` reports it by kind and excludes it from rounds AND from
  `runsWithoutRound`; POST_DEPLOY item 12 the same.

### E4 — a round has an identity across the wire, and a budget · `feat/round-counter-e4-round-on-the-wire`

- **4.1 The client mints and sends it** (Fable):
  - at round start the engine (`src_mcp/src/Server/Rounds/RoundEngine.cs:318`, and the feature stage) mints a
    **random 128-bit id**, persists it on the round (a `round_key` column on `rounds`, and the session JSON) and every
    launch of that round carries it — retries, repairs and follow-ups included; `again`, Continue/Fix and escalation
    start a new round and a new id. Random, not a hash: `sessionId` is 8 hex characters and the round number resets
    with the budget counter (`src_mcp/src/Server/RoundNumber.cs:10`), so a hash would collide and add no privacy;
  - `attempt` is `first` / `retry` / `repair` / `followUp`;
  - `RemoteAsk.RequestBody` adds `round`, `attempt` and **`remainingSeconds`** — the seconds left on the shim's own
    deadline at submit, RELATIVE, never an instant — each left out when empty, exactly as contract 2 added `effort`.
    **`ContractVersion` stays 2**: the fields are additive and optional, and a version the old server does not serve
    would refuse every review;
  - the client ships first, and its safety is measured AND cited: `ServerJsonContext` sets no
    `UnmappedMemberHandling` (`ServerJsonContext.cs:199`), so the deployed server skips unknown members; the contract
    suite POSTs the new body to the live old server, asserts 202, and DELETEs the job at once (no review spent).
- **4.2 The server records it** (Opus) — `ReviewRequestDto` (`ServerJsonContext.cs:91`), `JobRecord` and the ledger
  line carry `round` and `attempt`. A malformed value is **dropped with a `Dropped` note** (`ServerJsonContext.cs:104`)
  — a counting field never fails a review; absent = an old client, stored empty, never defaulted.
- **4.3 No start without budget** (Fable):
  - at submit the server validates `remainingSeconds` (a positive integer, else dropped with a `Dropped` note) and
    CAPS it at the job's own `timeoutSeconds` plus the queue's own give-up time (`DefaultQueueWait`, 10 min, `src_server/src/Jobs/JobRecord.cs:143`) — a
    client cannot buy a longer-lived job than one without the field; then `ClientDeadlineUtc = receiptUtc +
    min(remainingSeconds, cap) − Coai:DeadlineSafetyMarginSeconds`, from **its own clock only** — client clock skew
    cannot enter;
  - at claim (`JobStore.TryClaim`, `src_server/src/Jobs/JobStore.cs:234`) a job with less than
    `Coai:MinUsefulRunSeconds` (default 60) left is ended — a terminal failure with its OWN name
    (`FailureKind.NoBudget`, mapped to a distinct client outcome, read by name and never by phrase; a test asserts its
    sentence matches none of the rate-limit phrases at `ReviewerExecutor.cs:216`), its reason interpolating the
    configured number, the poller woken, nothing launched — and the claim **moves on to the next queued job** in the
    same pass (`:247-260` picks one candidate today), so a free slot is not left idle for a tick;
  - the run budget is capped at what is left; a request without `remainingSeconds` (an old client) behaves exactly
    as today;
  - refusals are counted: a ledger line with outcome `NoBudget` and an honest zero, so the effect of this story can
    be seen.

### E5 — the server counts rounds, and the fleet gets it · `feat/round-counter-e5-server-counts`

- **5.1 `/api/usage` counts** (Opus) — `UsageWindow.Names` (`src_server/src/Usage/UsageTotals.cs:28`) gains `5h` and
  `24h` (trailing); `windows=5h,24h,week` answers several windows from ONE scan; `VendorTotal` — so `vendors[]` and
  `people[].vendors[]` alike — gains `rounds` (distinct non-empty round, review kinds only, canary out),
  `runsWithoutRound`, `retries`, `repairs`, `followUps`, `cancelled`, `noBudget`, and launches per kind. Additive only;
  `http/usage/usage.http` asserts them.
- **5.2 The Team-server block** (Opus) — `VendorUsage` (`src_vs_code/src/teamServerApi.ts:134`) gains the optional
  fields; `teamUsageBlock` shows server rounds per vendor (5 h / 24 h / 7 d), `runsWithoutRound` apart, retries,
  repairs, cancelled, budget refusals. A server too old for the windows answers 400 → *count unavailable* in that
  column, never 0. The bundled-page harness asserts the line.
- **5.3 A stated reset is read** (Opus) — `CooldownParser` moves to the shared runners library (the dependency runs
  server → mcp, so `src_mcp` cannot reach it where it is) and gains the duration form; the RED test uses 1.1's
  verbatim agy line and today's fixture `src_mcp/tests/fixtures/antigravity/consult-quota.stderr.txt`
  (`Resets in 33m52s`). Locally: `gemini: quota out — resets in 110 h (Tue 07:20)` from the last refusal; the server
  catalog adds the earliest reset per vendor (no account names). Ships with E5's deploy because the server is
  re-asking an exhausted account every 30 min → 5 h today.
- **5.4 Release and the live count** (Opus) — the client halves release first; then ONE manual `deploy-server.yml`
  dispatch carrying E3–E5's server, with the owner's approval, after 4.1's body was measured against the old server.
  Live: one round through the Team server → that vendor's `rounds` +1 exactly, its launches as made.
  `module_team_server.md`, `module_server.md`, `module_extension.md`, `module_runners.md`.

### E6 — the vendor's own gauge (scope = 1.1's finding) · `feat/round-counter-e6-vendor-gauge`

- **6.1 Capture** (Fable) — per launch (if `--json` carries it) or per slot after each job INSIDE the lease (if only
  the app-server read does): `quota5hPct`, `quotaWeekPct`, `quotaResetsUtc` on the ledger line (trailing, defaulted)
  and the slot's `state.json` (one overwritten snapshot, the existing atomic write); locally the same into the local
  ledger. Test: a fixture gauge event → captured → stored → rendered.
- **6.2 Show and derive** (Opus) — `codex: 5 h 37 % · week 81 % · resets Thu 07:00` beside the counter, and rounds per
  1 % of the weekly window, trailing over the last N gauge readings, computed ONCE in the shared runners library and
  answered by `--round-counts` and `/api/usage`. **Scope rule:** the derived number sums every consumer 1.1 recorded
  for that account (both local stores, plus the server rounds of that account's slot via `slot`); when a known
  consumer cannot be counted, the number is hidden and the tab says which consumer is missing. Tests: a reset boundary,
  a missing reading, an unchanged reading, a missing consumer.
- **6.3 The record** (Opus) — the status line says what was not possible; README rows; the plan promoted with its
  deviations and its open tail; a second server deploy only if 6.1 exists.

## Build order

E1 → E2 → E3 → E4 → E5 → E6, each from the previous epic's commit.

- E2 needs nothing from E1 (the rounds database already holds the history) — it can start the same day.
- E3 needs 1.2's fixtures. E4 writes its fields beside 3.3's and 3.4's on E3's reshaped ledger call.
- E5 needs E4's fields on the wire and in the ledger and E3's outcomes and canary kind.
- E6 needs E1's scope and E5's counter to sit beside.
- **Server deploys:** once after E5 (after E4 instead if E5 slips — every undeployed day is a day of
  `runsWithoutRound`), and once more after E6 only if 6.1 exists. Client halves always release first.

## Risk (named for the gate's `riskItems`)

1. **E4** — the public wire contract plus a new company-wide refusal path: three new fields on `POST /api/reviews`,
   four version mixes (old/new client × old/new server), and a budget rule that with a wrong margin refuses every
   teammate's review. Guards: the body measured against the live old server before the server ships; no
   `remainingSeconds` → no rule; the server-clock-only deadline with a ten-minute client-offset test; the fake
   launcher asserting zero launches; the refusal read by name.
2. **E6 · 6.1** — a gauge read that can rotate the OAuth refresh token of the Team server's shared accounts; outside
   the slot's lease it signs the company out. Guards: 1.1 decides the source before any code; the read runs only inside
   the lease; scope shrinks to nothing rather than guessing.

## Growth

| Surface | Projected size | Who retires it | Interrupted |
|---|---|---|---|
| Server `usage.jsonl`, wider lines | ~4 100 lines/month today; ~+200 bytes per line → ≈ +0.8 MB/month on ≈ 1.1 MB/month | kept forever, as decided in story 2.4 of PLAN_team_server; the deferred reader plan reopens at ≈ 50 000 lines (≈ 12 months at today's rate) | append-only, one line per terminal job (budget refusals included); nothing in flight |
| Local `usage.jsonl`, wider lines and one line per launch | ~9 000 lines/month on Windows, + a few % for repair / follow-up lines; ≈ +2 MB/month; the counter reads only the last 7 days of it | the existing per-provider "forget" mark; otherwise kept | same |
| `rounds.round_key` column, `reviewers(round_id)` index | 16 bytes per round (~1 500 rounds/month) and an index the size of `reviewers` (~8 000 rows today) | the rounds they belong to | written in the round's own insert |
| Slot `state.json` gauge snapshot | one object per slot, overwritten | bounded by slot count | last write wins; the existing atomic write |
| 6.1's app-server read, if 1.1 picks it | one short process per job per slot, inside the lease — lengthens the lease by its run time (measured in 1.1) | ends with the job | killed with the lease; a reading that did not arrive is *no reading*, never 0 % |
| `--round-counts` | no storage — a query, cached in the extension until a round ends | — | a killed spawn returns nothing; the tab says *count unavailable*, never 0 |

## Test plan

Every bug fix RED first: it fails on today's code with the real symptom, passes after, and is re-reddened by
reverting the fix once. The seam rule: a test of a defect below `IReviewLauncher` uses the REAL `ReviewLauncher` with a
fake `IProcessLauncher` — a fake launcher above the defect cannot fail for the right reason (`ReviewLauncher.cs:19-23`
records that failure).

| Test | Guards |
|---|---|
| `RoundCounts_FromAFixtureDb` — every stage, two models in one window → two lines, a remote row apart | 2.1 |
| `TheLedgerRead_StopsAtTheOldestWindow` (a fixture ledger with a year of lines before the window: lines read ≤ the window's + one chunk) | 2.1 |
| `ConsultationsAndQuestions_AreCountedBesideRounds` | 2.1 |
| `roundsBlock` renderer cases, and the bundled-page run of the Review rounds page (counts shown; unavailable ≠ 0; the store sentence) | 2.3 |
| `TheCardLine_DoesNotSpawnPerRender`, `AConsultationOnlySession_RefreshesTheCount`, `AnIdleWindow_FallsOnAFakeClock`, `TheOtherSidesStore_IsNamedNotOpened` | 2.2 / 2.4 |
| `TheServerLedger_KeepsCachedAndReasoningTokens` — real `ReviewLauncher`, fake `IProcessLauncher` with a codex `turn.completed` carrying `cached_input_tokens` | 3.1 |
| `ANonZeroExit_KeepsItsUsage`, `AContinuationThatEndsBadly_KeepsTheFirstLaunchsUsage` | 3.1 |
| `AWithdrawnLaunch_IsRecordedAsCancelled_WithUsageNotCaptured` — real `ReviewLauncher`, `ProcessResult(TimedOut: true, Cancelled: true)` | 3.2 |
| `AWithdrawnRemoteReviewer_StillSendsTheDelete` | 3.2 — `AbandonAsync` kept |
| `AWithdrawalAbandonmentBackstopAndHostStop_EachCarryTheirCause`, `ARealDeadline_IsStillATimeout`, `AThrownCancellation_WritesExactlyOneLine_KeepingEarlierUsage` | 3.2 |
| `AnOldLedgerLine_StillReads` (1.2's fixtures) | 3.1 / 3.4 |
| `AVendorReason_IsNotClipped`, `AProbe_WritesALine`, `ARequeuedAttempt_WritesALine`, `ARepair_WritesTwoLinesSharingATurn`, `AnAgyFollowUp_SecondLineCarriesTheDifference` | 3.3 |
| `ACodexRowsEffort_IsRecordedAsIgnored` | 3.4 |
| `TheCanary_IsNotARound_AndNotARunWithoutARound` | 3.5 |
| `ARetryARepairAndAFollowUp_ShareTheRoundsId`, `Again_GetsANewId` | 4.1 |
| `TheNewBody_IsAcceptedByTheLiveServer_ThenDeleted` (contract suite, `COAI_CONTRACT_URL`) | 4.1 |
| `AMalformedRound_IsDroppedWithANote_AndTheReviewRuns` | 4.2 |
| `AJobWithoutBudget_IsEndedWithoutStartingTheVendor` (zero launches), `TheClaimMovesOnToTheNextJob`, `TheRunBudget_IsCappedAtWhatIsLeft`, `AClientClockTenMinutesOff_GetsTheSameDeadline`, `AHugeRemainingSeconds_IsCappedAtTheJobsOwnLifetime`, `TheNoBudgetReason_MatchesNoRateLimitPhrase`, `AnOldClient_IsNeverRefusedForBudget` | 4.3 |
| `FourRolesOfOneRound_CountAsOneRound`, `ARetryAndARepair_AddNoRound`, `LinesWithoutARound_AreCountedApart`, `TheFiveHourWindow_IsTrailing`, `SeveralWindows_OneScan` | 5.1 |
| `AnOlderServer_ShowsUnavailable_NotZero` | 5.2 |
| `CooldownParser_ReadsResetsIn110h25m`, `…ReadsResetsIn33m52s` (verbatim) | 5.3 |
| a gauge fixture captured → stored → rendered; rounds per 1 % across a reset, with a missing reading, an unchanged reading, a missing consumer | 6.1 / 6.2 |

Then every suite, not only the ones near the change: the `src_mcp` and `src_server` test executables, `npm test`,
`npm run test:contract` against a built server, the `http/` contract suite.

Live check after each epic, the prediction recorded before observing: one plan round and one code round through the
product path on Windows — the counter shows +1 / +1 and the launches the round really made; after the server deploy,
one round through the Team server — that vendor's `rounds` goes up by exactly one.

## What the reviews changed (2026-10-09)

The plan round (codex; gemini out of quota) — four findings, all accepted: an absolute client deadline is exposed to
clock skew (now `remainingSeconds`, anchored on the server's clock); per-person counts were promised and delegated
(now: fields here, rendering there, said in must-have 2); the gauge epic had no value tests; the Rounds block had only
pure-renderer tests (now a bundled-page run). An own review (Opus) found, among 24: the usage fix did not reach the
success path (`Answered` drops it); the withdrawal fix would have lost `AbandonAsync`; a hashed round id collides
(now random, persisted); malformed counting fields must not fail a review; bumping the contract version would refuse
every review on the old server; the budget refusal must be read by name and move the claim on; `Resets in 110h25m` is
not read today; feature rounds and consultations were not counted; rounds per 1 % compared scopes. The epic split was
drafted on Fable and reconciled with those findings. The cadence consultation for epics 1–3 (codex) found, each
checked in code: a ledger line is a terminal TURN, not a launch (a repair folds two; a requeued server attempt and a
host stop write none; two probes spend and write nothing) — hence 1.1's paid-path census and 3.3; a live WAL
database is not safely read across the Windows/WSL boundary — 2.2 names the other store instead of opening it; and
`Cancel` and `Expire` store the same `FailureKind`, so the end cause is a structured field set at the transition —
3.1 was split into usage (3.1) and lifecycle (3.2). The code round on these documents (codex) — four findings, all
accepted: the RESULTS table now says which CLI ran each model (and 3.4 records `cli`/`cliVersion`); one ledger line
per paid launch linked by a `turn` id instead of a folded count; `remainingSeconds` capped at the job's own lifetime;
the local counter reads the ledger only back to its oldest window.

## Definition of Done

- [ ] `RESULTS_vendor_quota_gauges.md` records what each vendor exposes and who shares each account, measured; E6's
      scope follows it and the status line says what was not possible.
- [ ] The Spending tab shows rounds per vendor row and stage for 5 h / 24 h / 7 d, consultations / questions / chat
      beside them, and the per-round figure honestly labelled — from the whole history, naming the store(s) it read.
- [ ] The Team-server block shows server rounds per vendor, `runsWithoutRound` apart; `people[].vendors[]` carries the
      same fields.
- [ ] Server lines carry cached and reasoning tokens; a withdrawn, abandoned or backstopped launch is named as such
      with usage not captured and its end cause, on both halves; every paid path in 1.1's census writes a line; vendor reasons are not clipped.
- [ ] Every new ledger line carries round, attempt, effort/tier as sent with their source; server lines job and slot;
      the canary is `canary` and in no round count.
- [ ] The server ends a job with too little budget left without starting the vendor, by name, and moves on.
- [ ] A stated `Resets in …` parks the slot until then on both halves.
- [ ] Where a gauge exists, percent used, reset time and rounds per 1 % (all consumers, or hidden with the reason);
      where none does, the reset time from the last refusal.
- [ ] Every RED test went red with the real symptom first; all suites green.
- [ ] `module_team_server.md`, `module_server.md`, `module_extension.md`, `module_runners.md` and the help pages (five
      languages) describe the counter, the fields, the refusal and the reset; README rows match; the neighbouring
      plans' boundary tables match this one.
- [ ] Released in the order above; the server deploy verified live (one round through it, counted once).
- [ ] This plan promoted with its deviations and its open tail.
