# PLAN — every round is counted, and what it cost the subscription is visible

> Status: **in progress, 2026-10-11 — E2 (the local counter: `coai-mcp --round-counts` and schema step 20, the
> extension's cached read, the Spending tab's Rounds block, the model card line, tokens and money — stories 2.1–2.5) is
> implemented on `feat/round-counter-e2-local-counter`; of E3, stories 3.1, 3.3, 3.4 and 3.5 are implemented on
> `feat/round-counter-e3-ledger-truth` (3.2 is not yet); E1 and E4–E6 are still plans. Deviations so far are
> recorded below.** Scope: the Team server's job ledger, `/api/usage` and
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
| Pricing a line: its own model, cached input at the cached rate, a reported cost never re-estimated | **this plan** (2.5) | [PLAN_usage_that_compares.md](PLAN_usage_that_compares.md) defines what each vendor's token columns MEAN; 2.5 prices only where cached is measured to be a subset of input (codex, api rows) and uses claude's reported cost instead of an estimate |
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
  - The mode is listed with the other one-shot modes in `AGENTS.md`; bad arguments to it (an unparseable `--now`, an
    unknown flag) exit with a usage error, never 64 — 64 means "this build has no such mode" to the extension.
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
  sentence) — the output figure is the median **per recorded turn**, labelled so: until story 3.3 writes one line per
  launch, a repair or a follow-up is two launches summed on one line, so a per-launch median cannot be computed from the
  ledger and is not claimed (a fixture with a repaired turn pins the label). A model change inside a window is two lines for that row, so the step of 2026-10-02 is visible the day it
  happens. **A bundled-page test** (`src_vs_code/src/test/bundledPage.test.ts` on `roundsLogPageHarness.ts`) RUNS the
  Review rounds page and asserts the block appears with counts, and with an unavailable read shows *count unavailable*
  and no zero.
- **2.4 Model card line** (Opus) — `7 days: 214 rounds · 31 consults · 5 h: 12` on the card (`src_vs_code/src/modelCard.ts`),
  from the cached read.
- **2.5 Tokens and money beside every count** (Opus) — asked by the operator on 2026-10-09 (*"token counting, prices and
  so on are needed too"*). Per vendor row × model × kind × window, beside the rounds: tokens **in / cached / out /
  reasoning** as totals and per round / per consultation, and the money. A line whose usage was not captured adds
  NOTHING to a token total and marks it a floor (`≥ 1.2 M in`, with how many lines are unknown); a total made only of
  such lines is *unknown*, never 0 — the same rule as the money:
  - a cost the vendor REPORTED (claude, priced `api` rows) is shown as money and never re-estimated;
  - otherwise a `~` list-price estimate priced **per ledger line by that line's own model** — never the row's current
    model — with the line's cached input at the model's cached rate (codex reports `cached_input_tokens` as a subset of
    input, `UsageAccountingTests`), a long-context tier applied per LINE (it is a per-request threshold), and a line
    with no price or with usage not captured making its total a floor (`≥ ~$…`);
  - **~$ per round and per consultation**, so 66.8 M tokens of `gpt-6-astra` consultations in a week read as money.
    On a MODEL's line the denominator is the rounds that used that model; on the vendor row and the total it is every
    round in the window — each labelled for what it is. The denominator is never only the priced rounds; when any line inside
    them is unpriced or not captured, the per-round figure carries the same floor mark as the total (`≥ ~$0.31 per
    round`) and the tooltip says how many of the N rounds were fully priced; when none was, it is a dash, never an
    average of the priced few. A window with priced usage but ZERO rounds (consultations only) shows a dash for the
    per-round figure, labelled *no rounds in this window* — never a division by zero.
  Two defects in today's spending tab are fixed on the way, each RED first: `totalsByVendor` prices a row's whole
  history at the row's CURRENT model (`priceOf(provider, …)`, `src_vs_code/src/usage.ts:174`) — after the Gemini row
  moved to `flash-medium`, every earlier `flash-low` line is priced as `flash-medium` (`APastLine_IsPricedAtItsOwnModel`);
  and it prices all input at the full rate, because the extension's `UsageEntry` (`usage.ts:9`, parsed at `:66`) does
  not even read `tokensCached`, `tokensReasoning` or `kind` from the ledger (`CachedInput_IsPricedAtTheCachedRate`,
  `TheParser_KeepsCachedReasoningAndKind`). The typed row rate still wins over a list price for the row's OWN lines,
  as `priceOfLine` decides (`usage.ts:282-297`).

### E3 — the ledger tells the truth · `feat/round-counter-e3-ledger-truth`

- **3.1 The whole usage travels** (Opus) — field preservation only:
  - `ReviewAttempt.Answered` (`ReviewLauncher.cs:36`) carries a `Usage`; so do `NonZeroExit` (dropped today at
    `JobRunner.cs:145-146`) and a continuation that ends badly (`ReviewLauncher.cs:155-164`); `JobRecord` carries a
    `Usage` instead of two longs; `JobRunner.Ledger` uses the `Usage` overload (`UsageLedger.cs:258`).
  - `UsageEntryDto` (`src_server/src/ServerJsonContext.cs:152`) and `UsageReader`'s `UsageLine`
    (`src_server/src/Usage/UsageReader.cs:16`) read cached, reasoning and `usageNote`.
- **3.2 How a job ended is said, not guessed** (Fable) — lifecycle and races, after 3.1:
  - `ReviewerExecutor` checks `result.Cancelled` before `result.TimedOut` (`ReviewerExecutor.cs:791`) and returns a
    NEW `ReviewerOutcome.Cancelled` with `Usage.Unknown` — and **keeps `AbandonAsync` in that branch**: it is what
    DELETEs a withdrawn remote job today, and an early return without it leaves the job running on the company's
    subscription.
  - `JobStore.Cancel` (`:303`) and `JobStore.Expire` (`:469`) both store `FailureKind.Cancelled` today, told apart only
    by a sentence — so a structured **end cause** (`withdrawn` / `abandoned` / `backstop` / `hostStopped`) is set AT
    the transition and carried on the record; the ledger reads it from the STORE's terminal record, not from
    `JobRunner`'s own recomputed `finished` (`JobRunner.cs:175-176`). A genuine deadline stays `TimedOut`.
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
- **3.5 The canary is a canary** (Opus) — `deploy/systemd-release.sh:168` sends `"kind":"canary"` when the target's
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
  **A subscription's share** (the operator's "price" for a flat plan): an optional monthly plan price the person types
  ONCE per vendor account — one subscription serves every model on it, so it lives with the account (the vendor's
  card on the Settings page), not on each model card — never looked up; a finite number ≥ 0, anything else refused at the card with the reason and
  never saved (0 means "no price", not free) — turns the gauge into money: *≈ $0.42 of your plan per round* = monthly price ×
  weekly-% per round ÷ 100 ÷ (weeks in the month). Shown only beside a gauge reading and labelled as a share, never
  summed with a list-price estimate; without a gauge it is not shown at all.
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
| `APastLine_IsPricedAtItsOwnModel`, `CachedInput_IsPricedAtTheCachedRate`, `TheParser_KeepsCachedReasoningAndKind`, `AReportedCost_IsNeverReEstimated`, `AnUnpricedLine_MakesTheTotalAFloor`, `ALongContextTier_AppliesPerLine` | 2.5 |
| `AConsultationOnlyWindow_ShowsNoPerRoundFigure`, `ARepairedTurn_IsOneRecordedTurn_NotOneLaunch`, `ABadArgument_IsAUsageErrorNot64` | 2.1 / 2.3 / 2.5 |
| `APerRoundFigure_CountsEveryRound_AndCarriesTheFloor`, `AModelLine_DividesByTheRoundsThatUsedIt`, `AnUncapturedLine_MakesATokenTotalAFloor_NeverZero` | 2.5 |
| `ASubscriptionShare_NeedsAGaugeReading`, `ASubscriptionShare_IsNeverSummedWithAnEstimate`, `ANegativeOrNonFinitePlanPrice_IsRefusedAtTheCard`, `OnePlanPrice_ServesEveryModelOfTheAccount` | 6.2 |
| `TheCardLine_DoesNotSpawnPerRender`, `AConsultationOnlySession_RefreshesTheCount`, `AnIdleWindow_FallsOnAFakeClock`, `TheOtherSidesStore_IsNamedNotOpened` | 2.2 / 2.4 |
| `TheServerLedger_KeepsCachedAndReasoningTokens` — real `ReviewLauncher`, fake `IProcessLauncher` with a codex `turn.completed` carrying `cached_input_tokens` | 3.1 |
| `ANonZeroExit_KeepsItsUsage`, `AContinuationThatEndsBadly_KeepsTheFirstLaunchsUsage` | 3.1 |
| `AWithdrawnLaunch_IsRecordedAsCancelled_WithUsageNotCaptured` — real `ReviewLauncher`, `ProcessResult(TimedOut: true, Cancelled: true)` | 3.2 |
| `AWithdrawnRemoteReviewer_StillSendsTheDelete` | 3.2 — `AbandonAsync` kept |
| `AWithdrawalAbandonmentBackstopAndHostStop_EachCarryTheirCause`, `ARealDeadline_IsStillATimeout`, `AThrownCancellation_WritesExactlyOneLine_KeepingEarlierUsage` | 3.2 |
| `AnOldLedgerLine_StillReads` (1.2's fixtures) | 3.1 / 3.4 |
| `AVendorReason_IsNotClipped`, `AProbe_WritesALine`, `ARequeuedAttempt_WritesALine`, `ARepair_WritesTwoLinesSharingATurn`, `AFollowUp_WritesTwoLinesSharingATurn` (replaced `AnAgyFollowUp_SecondLineCarriesTheDifference` — see the deviations) | 3.3 |
| `ACodexRowsEffort_IsRecordedAsIgnored` | 3.4 |
| `TheRecordedLaunch_IsWhatWasSent` — for a local launch and a server launch, the line's `cli`/`cliVersion`, `effort`/`effortFrom` and `tier`/`tierFrom` equal the effective launch options the fake `IProcessLauncher` received (argv and environment), not the row's configured ones; `ADroppedRemoteEffort_IsRecordedAsDropped` (the server's `ClientOptions` drop → `effortFrom: dropped`, effort empty); `AnEmptyValue_IsLabelledFromCliConfig_NeverChanged` (`cli-config` fills only the LABEL, and the argv carries no flag it did not carry before) | 3.4 — the plan round's finding (2026-10-10) |
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

## Deviations so far (E2, E3)

Recorded as each story lands, so the promotion has them ready; what shipped differently is the part a later reader
needs. Story 2.1 shipped 2026-10-10 on `feat/round-counter-e2-local-counter` — `src_mcp/src/Store/RoundCounts.cs`,
`src_mcp/src/Server/RoundCountsMode.cs`, `Schema.TheReviewersModel`, documented in
[module_server.md](../research/module_server.md) (*`--round-counts` — rounds per vendor row*) and
[module_tests.md](../research/module_tests.md).

| Story | The plan said | What shipped | Why |
|---|---|---|---|
| 2.1 | `--round-counts` answers rounds from `coai.db` AND, from `usage.jsonl` read from the end back to the oldest window, recorded turns, median input/output and outcomes per row × model × kind | **Rounds only, from `coai.db`.** Per window (5 h / 24 h / 7 d) and vendor row: DISTINCT rounds by stage (plan / code / document / feature), the reviewer rows those rounds launched (the "reviewers per round" numerator), and the rounds per reviewer MODEL; `modelsKnownSince` says from when the per-model lines exist. The mode never opens `usage.jsonl`. | Design decision taken before the story (2026-10-10): tokens and money (2.5) are computed in the EXTENSION from the ledger it already parses (`src_vs_code/src/usage.ts`) — one reader of the ledger, not two that drift — and the mode answers the one thing only the database can, which is what a round IS. The code round's rule that the ledger is read only back to the oldest window therefore binds the extension's reader (2.2 / 2.5), not this mode. |
| 2.1 | `rounds ⨝ reviewers` plus an index on `reviewers(round_id)` | **Schema step 20** `TheReviewersModel`: `reviewers.model TEXT NOT NULL DEFAULT ''` **and** `reviewers_by_round`, in one step; `RoundsDb.RecordRound` fills the model from `ReviewerState.Model`. Rows written before the step are empty and sit on no model line. A read-only reader asks the schema for the column and counts a step-19 file's rounds without models. | The per-model lines ("a model change inside a window is two lines for that row", 2.3) need a model per REVIEWER row, and the table had none; one step because a database at nineteen must gain both or neither. |
| 2.1 | windows 5 h / 24 h / 7 d, trailing; `--now <utc>` for tests; bad arguments exit with a usage error, never 64 | The same, with the edges fixed as **half-open `(from, to]`**: a round on a window's starting edge is the next window's, one exactly at `now` is in every window, one in the future in none. `--now` takes an ISO-8601 UTC instant through the parse `--since` uses (`Program.TryParseInstant`, widened rather than copied). Exit codes: **0** with the answer — and an EMPTY answer, exit 0, file not created, when `coai.db` does not exist; **65** for an unparseable or repeated `--now`, a dangling `--now`, an unknown flag; **74** for a database that will not read; **never 64**. | The edges had to be decided for the boundary tests; the missing-database answer is the brief's — a machine that has never run a round has nought rounds, and the extension tells a count from "unavailable" by the exit code and the parse. |
| 2.1 | listed with the other one-shot modes in `AGENTS.md` | Listed in `.agents/PROJECT.md`'s one-shot list — `AGENTS.md` is the pointer to that file, and `ARequestFaultIsNotAnOldBinaryTests` / `TheBinarySaysWhatItAcceptsTests` read the list from there. | Where the list is. |
| 2.1 (tests) | `RoundCounts_FromAFixtureDb`, `TheLedgerRead_StopsAtTheOldestWindow`, `ConsultationsAndQuestions_AreCountedBesideRounds`, `ABadArgument_IsAUsageErrorNot64` | `RoundCountsTests` (nine, over one fixture of twelve rounds placed on and around every window edge), `RoundCountsCliScenarioTests` (six, the real process — the contract, the real clock, the bad arguments with `--round-countz` → 64 as the control, the missing database, the read-only open on a step-19 file, the help and the list), `TheReviewersModelIsStepTwentyTests` (six: step 20 by position, a step-19 file, the writer, both preview forks, a second open). `TheLedgerRead_StopsAtTheOldestWindow` and `ConsultationsAndQuestions_AreCountedBesideRounds` move with the ledger to the extension's reader (2.2 / 2.5). | The ledger left the mode. |
| 2.2 | `readRoundCounts` in `roundsDbRead.ts`, through its existing spawn helper | `readRoundCounts` in **`roundCountsRead.ts`**, through `serverRun` from `roundsDbRead.ts` — the one door that hands the child this window's data directory (issue #115). Beside it: the contract's parser and types in `roundCounts.ts` (page-importable: no `node:child_process`), the cache in `roundCountsCache.ts`, the tail read in `ledgerTail.ts`. Exit 64 → *older than --round-counts — update it in Setup › MCP server*; 65, 74, -1 (timeout or a spawn that failed), an exit-0 answer that does not parse, a thrown load → `unavailable` with its reason; rows `[]` on exit 0 is a count of nought. | `roundsDbRead.ts` stands at 796 of its 800 lines; and the page module imports the types, which a spawn in the same file would drag into the webview bundle (the `require is not defined` the bundled-page test caught once already). |
| 2.2 | the answer reused ≤ 60 s, refreshed early when a round, consultation or question ends; never a spawn per render | The same, as `RoundCountsCache`: `current()` never reads; `refresh()` reads when the snapshot is older than 60 s, after `forget()` (a consultation or a question ended — `consultations.onChanged` / `questionConsults.onChanged` in `extension.ts`) or after the session files hold one more finished round (`noteFinishedRounds`, from the panel's own render), one read at a time; a load that throws is a stamped `unavailable` snapshot, so a failing read is not retried per render. The ledger (`readTailSince`) is read from its END in 64 KiB chunks and stops at the first chunk whose oldest line is older than the 7 d window; the counts and the tail load in parallel. | As planned; the "read from the end" rule moved here with the ledger (see 2.1). |
| 2.2 | names the store (`this store: Windows` / WSL) and says the other side's rounds are not included | *Counted from this side's store only — `<side>`, `<directory>`.*, with `WSL: <distro>` from the window's remote name and *this machine* otherwise; every directory of `coai.alsoWatchDataDirectories` whose `coai.db` EXISTS is named as another installation's store whose rounds are not included — asked by `existsSync` on the file (`otherStores`), never opened. | The storage article's sentence shape; the existence check is the whole of what crosses the boundary. |
| 2.3 | median output **per launch**; the bundled run in `src_vs_code/src/test/bundledPage.test.ts` | The column is **per recorded turn** (`MEDIAN_OUTPUT_LABEL`), as 2.5 decided — one line holds a repair or a follow-up until 3.3. The bundled run lives in **`roundsBlock.test.ts`** beside the renderer cases, through `bundledLogModule` widened to take more `export … from` lines for the same bundle; `bundledPage.test.ts` is untouched. The block is rendered by the HOST (`PanelProvider.roundsBlockHtml`) and handed to `usageTabHtml` as `extras.rounds`, drawn FIRST, above the window buttons — the block's windows are its own. | The label is the plan's own 2.3 text, recorded so the column is not read as a slip; one test file per surface, and the harness change is the reusable part. |
| 2.4 | `7 days: 214 rounds · 31 consults · 5 h: 12` | `7 days: 214 rounds · 31 consult turns · 5 h: 12` (`cardRoundsLine`, from the cached snapshot through `PanelState.roundsSnapshot`); `Rounds: count unavailable` when the read failed; nothing at all before the first read. | The ledger counts recorded TURNS of kind `consult`, and one consultation is several turns (2.3's own rule) — "31 consults" would overcount. |
| 2.2 (code round) | a missing ledger is no lines | A missing `usage.jsonl` is still no lines; any OTHER failure (a permission error, a directory in the file's place — which on Windows opens with size 0 and never fails a read, so `isFile()` decides) is no lines AND `failure`, carried to the snapshot as `ledgerFailure`; the block says *Tokens and money: unavailable — the usage ledger could not be read: <why>* once and *unavailable* in every cell drawn from the ledger, the card *consult turns unavailable*. A thrown load sets it too. | The epic-2 code round (codex, two reviewers): an unreadable ledger read as an empty one draws nothing spent — the same lie *count unavailable* exists to prevent. `missingRatherThanBroken` (`notificationsFile.ts`) is reused for the missing-or-broken decision. |
| 2.1 (code round) | `modelsKnownSince` over the whole table | The same answer, by a walk: rounds oldest first through `rounds_by_time`, each one's reviewers through `reviewers_by_round`, `LIMIT 1` — the cost is the rounds written before step 20, fixed once it ran, not the history. Measured on this machine before the change: the `MIN` over the join took 4.2 ms over 1 463 rounds and 8 513 reviewer rows. | The epic-2 code round: a once-a-minute read whose cost grows with the lifetime history. |
| 3.1 | `NonZeroExit` and a continuation that ends badly carry a `Usage` | **`ReviewAttempt.Failed` carries the launch's `Usage`** (beside `Answered`'s), for EVERY verdict — the server reads the attempt's usage, not each outcome's. `NonZeroExit` already carried its own (`ReviewerExecutor`, since 2026-09-26); what dropped it was `JobRunner`, and a continuation's terminal arm read the outcome alone. A `Failed` built from an outcome alone carries `ReviewerOutcome.TotalUsage`. `JobRecord.TokensIn`/`TokensOut` stay as read-only views of `Usage` for the status DTO. | One field on the attempt answers every failure the same way, and the shared outcome type in `src_mcp` needed no change. |
| 3.1 (tests) | `TheServerLedger_KeepsCachedAndReasoningTokens` through the real launcher | The real launcher with a codex stream proves CACHED; `AReasoningCount_ReachesTheServerLedger` proves reasoning above the launcher. Plus `TheServersReader_ReadsCachedReasoningAndTheUsageNote` for the reader half. | codex-cli's `reasoning_output_tokens` is not read by `UsageParser` (what a column means per vendor is PLAN_usage_that_compares'), so no real codex stream can carry a reasoning count to the ledger today. |
| 3.5 | the script sends `"kind":"canary"` when the target's `/api/health` version knows it | It sends the kind ALWAYS, and asks once more without it when the server answers 400 *"'canary' is not a kind of job"* — before any vendor starts. | No version number is known for the release that ships the kind until it is cut, and a version table in a shell script is one more thing to keep level; the refusal names itself and costs nothing. |
| 3.3 | `turn` and `attempt` on every line | Two trailing, defaulted `UsageEntry` fields, mirrored in `UsageEntryDto`, `UsageLine` and the extension's parser (`launchFields`); an old line reads both empty / absent (`AnOldLedgerLine_StillReads_AndANewOneKeepsItsTurnAndAttempt`, `TheParser_KeepsTheTurnAndTheAttempt`). A LOCAL turn id is twelve hex characters of a GUID minted per turn when the reviewer is recorded — nothing carries it through the outcome; the SERVER's is the job id (a job is one turn). `attempt` is `first` / `repair` / `followUp` / `retry` / `rotated` (`LaunchAttempts`), empty on kinds with no attempts (consult, question, probe). | A turn lives inside one `Record` call, so minting it there needs no new field on the outcome; the job id is already unique and already in the server's log. |
| 3.3 | a repair or a follow-up writes its own line | The same, and wider: `ReviewerOutcome.Launches` (the parts of the LAST turn) is set by the executor's second launch (`repair` or `followUp`, `SecondLaunch` says which), by the rate-limit ladder (every step, `first` then `retry` — the ladder folded steps too, which census row 1 had not counted), kept by the turn loop on each earlier turn (`TurnUsage.Launches`) and by the security answer limit. Round totals unchanged: `LastTurnUsage` / `TotalUsage` keep their sums; `LaunchLines.Of` writes the parts only when their usage adds up to the turn's EXACTLY (every count, both markers, the money to 1e-8), else the turn as one line. The last line takes the turn's own outcome and the rest of its seconds, so seconds add up too. The "no-repair `Unparseable`" of the brief is one launch and gets no parts. | The add-up guard is what makes "no total moves" checkable on every path, including ones a later change forgets to update. |
| 3.3 | where agy reports cumulatively, the second line carries the difference and says so | **Not done.** Each line carries what ITS launch reported; `AnAgyFollowUp_SecondLineCarriesTheDifference` is replaced by `AFollowUp_WritesTwoLinesSharingATurn` (local and server). No `UsageIsCumulative` seam was added. | Whether a REVIEWER follow-up's usage is cumulative is unmeasured; it is [PLAN_the_reviewer_follow_up_is_billed_once.md](PLAN_the_reviewer_follow_up_is_billed_once.md)'s to measure and, if confirmed, to change the follow-up part together with the sum — written into both plans' boundary tables. |
| 3.3 | a requeued server attempt writes its own line (`attempt: rotated`) | The same: `RateLimited`, `rotated`, the refused launch's usage (0/0 here), its seconds from the claim, turned by the job id, written before the job returns to the queue (`JobRunner.Refused`, a record built for the line and never stored). The final attempt is `first` when the job met one account, else `rotated`; a follow-up's two launches are `first`/`rotated` + `followUp`. | As the brief decided. |
| 3.3 | the probes write a line of kind `probe` | **`--probe-api --model` does** (`UsageKinds.Probe`, local-only; the server parity test still green): one line per completion sent with the real key — the wrong-key request bills nobody and writes nothing — stage `Probe`, the variant as role, `ok` / `HTTP 400 (refused <field>)` / `no answer` (never the vendor's text). **The extension's Claude model probe (`claudeProbe.ts`) still writes nothing.** The Rounds block shows `probe` as its own kind beside the rounds (`BESIDE`), never on a model line; help in five languages says so. | No sanctioned writer fits the extension probe: `chat-usage.jsonl` is read as chat turns by every existing reader and by any older build (the door ledger was refused into it for exactly that, `chatDoors.ts`), and `usage.jsonl` belongs to `coai-mcp` (`chatUsage.ts`). Options left to the owner: (a) a `kind` field on chat lines with every reader filtering it (an older build still shows the probe as a chat conversation with real money); (b) a new extension ledger `probe-usage.jsonl` through the shared `jsonlLedger.appendLine` (a fourth file the Rounds block would read); (c) run the claude probe through a `coai-mcp` one-shot mode so the C# ledger writes it (a new mode in `.agents/PROJECT.md`). |
| 3.3 | vendor reasons kept whole in the ledger and the log | `ReviewerSummaryFactory.DescribeWhole` — the vendor's line uncut to `VendorReason.SafetyCap` (4 000) — for the review ledger line, `RoundAudit`'s FAILED warning, and also the question row's and the consultation's ledger outcome (the same `Describe` road); `ReviewerExecutor.TailOf` keeps one stderr line longer than its 400 budget whole to the cap. **And redacted** (`Redaction.SafeText` at the cap): the question fan-out's own fixture showed a CLI's `Authorization: Bearer …` line reaching `usage.jsonl` unredacted through the old 160-character cut, though the record had lost it — found by this story's RED tests, closed with them. Display (`Describe`, notices, cells) unchanged. Not changed: `ReviewerExecutor.Complaint`'s 240-character cap inside an empty answer's `Unparseable.Reason` (one string shared by the summary and the ledger). | Keeping a vendor's stderr whole must not widen what a secret on it can reach; the redactor is the one every record already passes. |
| 3.3 | (not in the plan) | **A repair that exits non-zero after billing is counted once.** `AfterTheRepair` added the repair's launch usage to `EarlierLaunches` while the `NonZeroExit` carried the same usage as its own — counted twice in the turn, the line and the round total (`ARepairThatExitsNonZero_IsBilledOnce`, RED at 1400 for 1200). | Found because the split lines could not add up to the double-counted sum; it changes a round total only in that edge, downward to the truth. |
| 3.3 | closes 1.1's census | Closed: rows 1 (repair, follow-up, ladder), 7, 10 (follow-up, requeue). **Still open:** row 8 (above), rows 2/4 (a consultation's / question row's continued turn is still one line per turn), row 5 by hand (`--ask-api` from a terminal writes nothing), row 9 (a chat turn whose row was deleted mid-turn), row 10's host stop and infrastructure throw (story 3.2's). | Outside the brief's rows. |
| 3.3 (tests) | five named tests | `EveryPaidLaunchIsWrittenTests` (12), `EveryServerLaunchIsWrittenTests` (4), `ProbeApiModeTests` (+2), `SecurityAnswerLimitCharacterizationTests` (+1), `QuestionFanOutTests` (+1), `ConsultScenarioTests` (+1), `usagePricing.test.ts` / `roundsBlock.test.ts` / `helpCoverage.test.ts` (+1 each); five existing tests that pinned the folded line were updated to one line per launch with the same sums — listed in [module_tests.md](../research/module_tests.md). | — |
| 3.4 | trailing, defaulted `UsageEntry` fields `cli`, `cliVersion`, `effort`/`effortFrom`, `tier`/`tierFrom`; server lines `job` and `slot` | The ten words as **eight** fields (`cli`, `cliVersion`, `effort`, `effortFrom`, `tier`, `tierFrom`, `job`, `slot`) at the END of `UsageEntry`, after 3.3's `turn`/`attempt` (the record grew since `:61`), mirrored in `UsageEntryDto`, `UsageLine` (`RanWith`, `Job`, `Slot`) and the extension's parser (`ranWithFields`); an old line reads `LaunchOptions.None` / absent (`AnOldLedgerLine_StillReads_WhatItRanWith`, `TheParser_KeepsWhatTheLaunchRanWith`). One record for the six launch fields both halves write — `LaunchOptions` in `src_mcp/runners/Reviewers/LaunchOptions.cs` — and one place it becomes columns (`UsageEntry.RanWith`). `cli` is the RUNTIME in the catalog's words (`codex`, `claude`, `antigravity`, `local`, `api`, `remote`), not the executable's name. | The runtime name is what every other surface calls the CLI; an executable name (`agy`, an npm shim path) differs per machine. |
| 3.4 | the source vocabulary: the brief's `row`, `ignored`, `dropped`, `cli-config`, `` | **Six answers**: those five and **`server`** — a value that was sent and that the Team server chose: an effort lowered to `Coai:MaxEffort`, and the tier every server launch sets. `effort`/`tier` hold what the launch RAN WITH: the sent value (`row`/`server`); EMPTY for `ignored` and `dropped` (what the row asked for did not run, and nothing is claimed about what did — an ignored codex effort is not relabelled as the configuration's); the configuration's value for `cli-config` (nothing asked, nothing sent). `OptionChoice.Labelled` decides it in that order. | A clamped effort labelled `row` would say the person's level ran; `server` says whose choice it was. Empty on `ignored`/`dropped` keeps the line's question — did the setting run — answered by one field. |
| 3.4 | the server labels the tier from the slot's own config — the request carries none | **The tier is what the argv carried, labelled `server`**: a server launch is built with fast mode Off (`ReviewerSettings.Fast`'s default), so a codex job is TOLD `service_tier=default` — the slot's `service_tier` is overridden and does not label it. The slot's configuration labels what nothing sent: the effort of a request that asked none (`cli-config`, read through the launch's own `CODEX_HOME`/`HOME`). A claude job on the server is handed no fast-mode file (no data folder), and claude's own settings are not read, so its tier is empty. | `TheRecordedLaunch_IsWhatWasSent` binds the line to the argv; a slot-config label for a tier the argv overrides would be the very mislabel the story exists to end. Whether the server SHOULD send `default` is not this story's question. |
| 3.4 | `cli-config` read to label an empty value | Only **codex's** `config.toml` (`model_reasoning_effort`; `service_tier` only on codex's own service), through the new `CodexConfigFile` — extracted from `NoMcpServers`, which reads the file through it now — root keys before the first table header, unsure means empty. Claude's (`settings.json`), agy's and gemini's are not read. The argv is built before the label and never touched (`AnEmptyValue_IsLabelledFromCliConfig_NeverChanged` compares the argv with and without a config). | codex is the one CLI whose effort and tier a launch leaves to its configuration (symptom 6). Reading claude's settings would be a second, unmeasured parser for no row that needs it today. |
| 3.4 | `cliVersion` — reuse a cached version or leave it empty with a reason | Locally: the codex tier probe's answer for the round (`CodexTierSupport.Printed`, `codex-cli X.Y.Z`) — asked only for an Off codex row on codex's own service; **every other local line's `cliVersion` is empty** (no `--version` is asked on those paths, and none is added). On the server: `VendorHealthCache.LastSeenVersion` — the catalog probe's last answer, however old, never a probe; empty on a server no panel has asked since it started. | The brief's rule: no spawn per launch. |
| 3.4 | effort as SENT, after the server's `Dropped` | On the server line, yes (`ClientOptions.Take` → `TakenOptions.EffortFrom` → `JobRecord.EffortFrom` → `dropped`/`server`/`row`). **The client's own line for a Team-server row records the effort it SENT** (`row`): the client learns of a drop only as a sentence (`NotApplied`), and matching that sentence would be the guess this plan refuses elsewhere. The server's line is the authority on what was applied. | — |
| 3.4 | each line | Labelled: every REVIEW launch — the round's reviewers and the security lane (`RowLaunchOptions.Labelled` in `RosterBuilder` and `SecurityRoster`), and every Team-server job line. **Not labelled**: consultation, question-row, probe and chat lines — their fields are written empty ("not recorded"). | The plan's tests name review launches; the consultant and question launch paths build their invocations elsewhere and are left for a story that measures them. |
| 3.4 (tests) | `ACodexRowsEffort_IsRecordedAsIgnored`, `TheRecordedLaunch_IsWhatWasSent` (local and server), `ADroppedRemoteEffort_IsRecordedAsDropped`, `AnEmptyValue_IsLabelledFromCliConfig_NeverChanged`, `AnOldLedgerLine_StillReads` | `EachLineSaysWhatItRanWithTests` (3, mcp): the first, the local `TheRecordedLaunch_IsWhatWasSent` — a real plan round through `PanelService` with the fake CLI behind a RECORDING `IProcessLauncher` (the real launcher, every request kept, so the codex tier probe runs and names the version), and `AnEmptyValue…`. `EachServerLineSaysWhatItRanWithTests` (5, server): `TheRecordedLaunch_IsWhatWasSent`, `ADroppedRemoteEffort_IsRecordedAsDropped` (through the real `POST /api/reviews`), plus `AClampedEffort_IsRecordedAsTheServers_AndATakenOneAsTheRows`, `TheSlot_IsItsName_AndNoFieldButTheCallersCarriesAnEmail`, `AnOldLedgerLine_StillReads_WhatItRanWith`; `usagePricing.test.ts`'s `TheParser_KeepsWhatTheLaunchRanWith` — listed in [module_tests.md](../research/module_tests.md). | A fake launcher that answered `--version` itself could not prove the version came from a probe that ran anyway. |
| 3.5 | POST_DEPLOY item 12 the same | **Not yet.** The by-hand per-vendor review (now item 11) runs against the DEPLOYED server, which refuses the kind until a server carrying 3.5 is deployed; its command gains `kind:'canary'` with that deploy (after E5). | A checklist command that 400s on today's server would be a check that fails for its own sake. |

## Definition of Done

- [ ] `RESULTS_vendor_quota_gauges.md` records what each vendor exposes and who shares each account, measured; E6's
      scope follows it and the status line says what was not possible.
- [ ] The Spending tab shows rounds per vendor row and stage for 5 h / 24 h / 7 d, consultations / questions / chat
      beside them, and the per-round figure honestly labelled — from the whole history, naming the store(s) it read.
- [ ] Tokens (in / cached / out / reasoning) and money sit beside every count: reported cost as money, otherwise a `~`
      estimate priced per line by its own model with cached input at the cached rate, per round and per consultation;
      the two pricing defects of today's spending tab are fixed RED first; a typed plan price shows a subscription share
      only beside a gauge reading.
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
