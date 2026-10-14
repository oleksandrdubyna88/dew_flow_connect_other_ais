# PLAN — every round is counted, and what it cost the subscription is visible

> Status: **in progress, 2026-10-11 — E1 landed 2026-10-10 in part (story 1.2 whole; story 1.1 without three
> measurements it names — agy in WSL, the Team server slot, the read on an expired token); E2 landed 2026-10-10 whole
> (the local counter: `coai-mcp --round-counts` and schema step 20, the extension's cached read, the Spending tab's
> Rounds block, the model card line, tokens and money — stories 2.1–2.5); E3 is implemented whole
> (stories 3.1–3.5, PR #761); story 5.3a (the server reads a stated duration) landed 2026-10-11 ahead of E4; E4 is
> implemented whole 2026-10-11 on `feat/round-counter-e4-round-on-the-wire` — 4.1 (the client mints the round's
> identity and sends it, the attempt and the seconds left), 4.2 (the server records the round) and 4.3 (no start
> without budget: the server ends a job whose caller can no longer wait, by name, and moves on); E5 in progress
> 2026-10-11 on `feat/round-counter-e5-server-counts` — 5.1 (`/api/usage` counts rounds over several windows from one
> scan), 5.2 (the Team server tab shows them; an older server reads *count unavailable*), the plan round's seam leg,
> 5.3b (the stated reset read locally by the one moved parser, shown, and said by the catalog and a queued job's
> status) and 5.3c (a stated reset is never waited out locally) are implemented and through epic 5's code round; 5.4
> (release and the live count) waits for the deploy the owner approved for about 2026-10-14 08:00 UTC; E6 in progress
> 2026-10-12 on `feat/round-counter-e6-vendor-gauge` — 6.1a (the Team server captures the codex gauge inside the
> slot's lease, with the containment the epic-6 risk consultation fixed) is implemented, 6.1b deferred, 6.2 (the gauge
> shown on the Team server tab and the model card, rounds per 1 % derived once in the shared runners library and
> served by `/api/usage` and `--round-counts`, the plan share) implemented 2026-10-12, 6.3 not built (the owner wants
> it: read the vendor's gauge and when the quota comes back).
> Deviations so far are recorded below.** Scope: the Team server's job ledger, `/api/usage` and
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
7. A stated reset (`Resets in 110h25m`) parks the slot until then on the SERVER; locally it is never waited out, and
   shown (narrowed by E5's plan round, 2026-10-11 — see *What E5's plan round decided*).

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
  *Done in part 2026-10-10 — three measurements of this scope were NOT made and stay open: agy was not launched in
  WSL; the Team server slot was not read (a slot read needs the slot's lease on the live host — it moves to 6.1); and
  the rate-limit read with an EXPIRED access token was deliberately not tried (the refresh risk 6.1 must settle).
  What was measured — [RESULTS_vendor_quota_gauges.md](../research/RESULTS_vendor_quota_gauges.md): codex HAS a source
  (`codex app-server` · `account/rateLimits/read`, one weekly window, no 5-hour one on this plan; `exec --json` carries
  none), the read did not touch `auth.json` while the access token was valid, Gemini has only its refusal, and a fourth
  consumer — codex used directly outside coai — exists; E6 keeps 6.1 and 6.2 with the constraints its §6 lists.*
- **1.2 Server ledger bodies as fixtures** (Fable) — one `ok`, one withdrawal-written-as-`TimedOut`, one
  `RateLimited` line from the live server, redacted (no email, no prompt), under `src_server/tests/`, with a check
  that no fixture line contains `@`. *Done 2026-10-10 — `src_server/tests/fixtures/usage/`, `UsageLedgerFixtureTests`.*

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
- **What the risk consultation for 4 changed (2026-10-11, verified in code before E4 is built)** — binding on E4's plan
  round:
  - **The 60-second floor would refuse healthy short reviews.** A 60 s reviewer's shim advertises at most 50 s
    (`LocalAsk.ShimDeadlineSeconds`, reviewer timeout − 10, `src_mcp/runners/Reviewers/LocalAsk.cs:42`) before any
    server margin, and the server accepts budgets below 60 s. Bounding the floor by the job's budget does NOT fix it
    (the consultation's second turn: a fresh 60 s reviewer still arrives with 50 s minus the margin). The rule is
    therefore **"the caller can no longer wait"**, not "less than our preferred useful run": a job is ended only when
    the caller's remaining time, after the margin, cannot hold even a launch's start-up (a small fixed number, e.g.
    10 s, configurable). Tested through the REAL path — `RemoteRuntime` → captured POST → immediate claim — for fresh
    30 s and 60 s reviewers and a repair with 40 s left, each stated as MUST LAUNCH; a hand-built server request would
    miss the shim's own 10 s. An exhausted effective budget prevents the SUBMIT on the client (serialising 0 and having
    it dropped would quietly switch enforcement off).
  - **A repair or follow-up advertises a stale budget.** The shim's `--timeout-seconds` is fixed when the invocation
    is built (`RemoteRuntime.cs:98`); `RetryLadder.Lesser` (`RetryLadder.cs:180`) later shortens only
    `Request.Timeout`. 4.1 computes `remainingSeconds` from the EFFECTIVE timeout of the launch being submitted (the
    argv rebuilt from `Request.Timeout`, or the value passed separately), with a test that shortens a 600 s invocation
    to 40 s and reads the captured POST — and shows the shim's own WAITING deadline (`--timeout-seconds`) shortened
    with it, not only the advertised number. A stale large value over-admits; it never refuses.
  - **"Malformed → dropped" needs tolerant binding.** A plain nullable `int` in `ReviewRequestDto` rejects `"oops"`,
    `{}`, `1.5` or an overflow during JSON binding — a 400 before any `Dropped` note runs. The three fields bind
    tolerantly (a `JsonElement`, read by hand), and an endpoint test sends omitted, `null`, `0`, negative, `"oops"`,
    `{}`, fractional and oversized values: each accepted with a `Dropped` note, never refused.
  - **New client → old server enforces nothing.** Contract 2 cannot tell the client the server reads the fields; the
    full poll/cancel path is tested against the old server and nothing claims budget enforcement there.
  - **POST then DELETE is not zero-spend.** The runner can claim and launch between the two. The live measurement of
    the new body must prevent dispatch (a probe the catalog cannot run, or a vendor whose only slot is parked), with
    a counting fake launcher proving the interleaving in the suite; which probe is decided at E4's plan round.
- **What E4's plan round decided (2026-10-11, codex, `proceed`)** — binding on the build:
  - **The zero-spend live probe** (amendment 5): the contract suite POSTs the NEW body — `round`, `attempt`,
    `remainingSeconds` and one deliberately unknown member — with `"kind":"probe-no-such-kind"`. The server binds the
    JSON before it checks the kind, so a 400 naming *"'probe-no-such-kind' is not a kind of job"* proves the new
    members were skipped by binding, and no job is ever created. The control in the same run: the same body with a
    malformed KNOWN member (`timeoutSeconds: "x"`) answers the binding error instead. It never posts a valid job.
  - **Transit time is the margin's to absorb, and that is a stated limit.** The server anchors `remainingSeconds` at
    RECEIPT and cannot see how long the request travelled (a client timestamp would bring clock skew back).
    `Coai:DeadlineSafetyMarginSeconds` is therefore the transit allowance as well as the safety margin, documented as
    such: a request delayed in transit by MORE than the margin can still be claimed after its caller gave up — and
    is then withdrawn by the caller's own DELETE, the existing withdrawal path. Test: a request whose remaining time,
    less the margin, cannot hold the start-up is refused at claim; one just above that edge launches.
  - **The refusal is counted, asserted.** `AJobWithoutBudget_IsEndedWithoutStartingTheVendor` also asserts exactly
    ONE ledger line for the job: outcome `NoBudget`, zero tokens, no usage note, kind and round kept.

### E5 — the server counts rounds, and the fleet gets it · `feat/round-counter-e5-server-counts`

- **5.1 `/api/usage` counts** (Opus) — `UsageWindow.Names` (`src_server/src/Usage/UsageTotals.cs:28`) gains `5h` and
  `24h` (trailing); `windows=5h,24h,week` answers several windows from ONE scan; `VendorTotal` — so `vendors[]` and
  `people[].vendors[]` alike — gains `rounds` (distinct non-empty round, review kinds only, canary out),
  `runsWithoutRound`, `retries`, `repairs`, `followUps`, `cancelled`, `noBudget`, and launches per kind. Additive only;
  `http/usage/usage.http` asserts them.
- **5.2 The Team-server block** (Opus) — `VendorUsage` (`src_vs_code/src/teamServerApi.ts:134`) gains the optional
  fields; `teamUsageBlock` shows server rounds per vendor (5 h / 24 h / 7 d), `runsWithoutRound` apart, retries,
  repairs, cancelled, budget refusals. **A server too old for the windows is judged by the SHAPE of its answer, not its
  status** — today's `/api/usage` binds only `window` and answers `?windows=5h,24h,week` with **200 and today's range**
  (`UsageEndpoints.cs:23`, `UsageWindow.Range(null)` → `today`; the E4–E6 cadence consultation, verified 2026-10-11):
  an answer without the requested windows and count fields is *count unavailable* in those columns, never 0, and the
  test runs against that 200-without-them, not against a 400. The bundled-page harness asserts the line.
- **5.3a The server reads a stated duration** (Opus) — **extracted and shipped AHEAD of E4** (the cadence consultation,
  2026-10-11): it needs neither round ids nor usage aggregation, and the server re-asks an exhausted Gemini slot on its
  doubling 30-minute guess for the whole ~110 h of a stated reset today. `CooldownParser` (where it is,
  `src_server/src/Slots/CooldownParser.cs`) gains the duration form (`Resets in 88h49m9s`, `Resets in 33m52s` — 1.1's
  verbatim agy line and the fixture `src_mcp/tests/fixtures/antigravity/consult-quota.stderr.txt`); `SlotRegistry.MarkCoolingDown`
  already persists what it parses. Behavioural test through the real `JobRunner`: the verbatim refusal parks the slot
  until the stated instant (state reloaded from disk), a second queued job launches NOTHING before it, and — the
  positive control — DOES launch once the stated instant has passed. Ships as a server patch with the deploy already
  waiting for the Gemini reset.
- **5.3b A stated reset is read locally** (Opus) — `CooldownParser` moves to the shared runners library (the dependency
  runs server → mcp, so `src_mcp` cannot reach it where it is); locally: `gemini: quota out — resets in 110 h (Tue 07:20)`
  from the last refusal; the server catalog adds the earliest reset per vendor (no account names). **Display only:**
  the local scheduler still retries from phrase classification (`BoundedScheduler.cs:585`), so "parks the slot on both
  halves" (what-must-be-true 7) needs local PERSISTENCE and ADMISSION of the reset — its own story (**5.3c**), or item
  7 narrows to "shown locally, enforced on the server". Decided with 5.3b's plan round. **Also owed here** (5.3a's code
  round, 2026-10-11): a job queued behind a parked slot hears nothing about when it can run — `SlotSelector.Explain`
  (`src_server/src/Slots/AccountSlot.cs:69`) says *comes back at …* and no status path calls it; wire it into the
  queued job's status beside the catalog's per-vendor reset.
- **5.4 Release and the live count** (Opus) — the client halves release first; then ONE manual `deploy-server.yml`
  dispatch carrying E3–E5's server, with the owner's approval, after 4.1's body was measured against the old server.
  Live: one round through the Team server → that vendor's `rounds` +1 exactly, its launches as made.
  `module_team_server.md`, `module_server.md`, `module_extension.md`, `module_runners.md`.
- **What E5's plan round decided (2026-10-11, codex, `proceed`)** — binding on the build:
  - **5.3c is built, narrowed: a stated reset is never waited out locally.** The ladder
    (`BoundedScheduler.LadderAsync`) asks the moved `CooldownParser` for a STATED reset in the refusal; one that lies
    beyond what is left of the reviewer's own budget makes the refusal hopeless (no retry), as a spent daily allowance
    is today. Measured basis: agy's verbatim *"Individual quota reached … Resets in 88h49m9s"* matches none of
    `RateLimit.Spent`'s words, so today a local agy row climbs the whole ladder into a reset days away. A short stated
    reset inside the budget is still waited out. **Not built: a local park persisted across rounds** — a local CLI's
    account can change under it (a re-login), a parked row would then refuse a working account, and what a park saves
    is one refusal launch per round (seconds, no tokens). What-must-be-true item 7 therefore reads: *parked on the
    SERVER; locally never waited out, and shown.*
  - **A pre-release live contract check of the new `/api/usage` fields** (the round's one finding): a new leg of
    `npm run test:seam` starts the REAL server built from the tree over a seeded ledger, asks
    `/api/usage?windows=5h,24h,week` (and `scope=company`), and reads the answer through the extension's OWN parser
    (`teamServerApi.ts`), asserting every new field by its value — so a field serialised under a name the extension
    does not read fails before release, not after the deploy. The post-deploy round count stays as the release smoke.

### E6 — the vendor's own gauge (scope = 1.1's finding) · `feat/round-counter-e6-vendor-gauge`

- **6.1 Capture** (Fable) — per launch (if `--json` carries it) or per slot after each job INSIDE the lease (if only
  the app-server read does): `quota5hPct`, `quotaWeekPct`, `quotaResetsUtc` on the ledger line (trailing, defaulted)
  and the slot's `state.json` (one overwritten snapshot, the existing atomic write); locally the same into the local
  ledger. Test: a fixture gauge event → captured → stored → rendered. **Split by the cadence consultation
  (2026-10-11):** 1.1 found the source (`codex app-server` · `account/rateLimits/read`, RESULTS §6), so E6 is not
  reduced — but **6.1a server capture** (inside the slot's lease, after the expired-token measurement 1.1 still owes)
  ships first, and **6.1b local capture** waits until a read is shown to be excluded from a concurrent launch of the
  SAME store: `BoundedScheduler`'s semaphores belong to one process, and two coai processes (or direct codex use)
  sharing one `CODEX_HOME` are not covered by an "idle" check.
- **6.2 Show and derive** (Opus) — `codex: 5 h 37 % · week 81 % · resets Thu 07:00` beside the counter, and rounds per
  1 % of the weekly window, trailing over the last N gauge readings, computed ONCE in the shared runners library and
  answered by `--round-counts` and `/api/usage`. **Scope rule:** the derived number sums every consumer 1.1 recorded
  for that account (both local stores, plus the server rounds of that account's slot via `slot`); when a known
  consumer cannot be counted, the number is hidden and the tab says which consumer is missing. Tests: a reset boundary,
  a missing reading, an unchanged reading, a missing consumer. **For codex today the number IS hidden:** direct use
  outside coai is a recorded consumer that cannot be counted (RESULTS §2.4, §6.3) — so is the price per round derived
  from it; showing it as an upper bound instead is the owner's call, not this plan's.
  **A subscription's share** (the operator's "price" for a flat plan): an optional monthly plan price the person types
  ONCE per vendor account — one subscription serves every model on it, so it lives with the account (the vendor's
  card on the Settings page), not on each model card — never looked up; a finite number ≥ 0, anything else refused at the card with the reason and
  never saved (0 means "no price", not free) — turns the gauge into money: *≈ $0.42 of your plan per round* = monthly price ×
  weekly-% per round ÷ 100 ÷ (weeks in the month). Shown only beside a gauge reading and labelled as a share, never
  summed with a list-price estimate; without a gauge it is not shown at all.
- **6.3 The record** (Opus) — the status line says what was not possible; README rows; the plan promoted with its
  deviations and its open tail; a second server deploy only if 6.1 exists.
- **What the owner and the risk consultation for 6.1 decided (2026-10-11)** — binding on E6's plan round:
  - **The owner wants E6:** read each vendor's gauge and know when the quota comes back. The consultation
    (`71ab5408`, gpt-6-astra, two turns, verified in code) decides HOW.
  - **6.1a server capture — under the lease, never alone.** The read runs INSIDE `JobRunner`'s slot lease (taken before
    the claim, held across `RunAsync` — verified), after the job, and its process is killed before the lease is
    released (a test proves no reader outlives the lease, a parent crash included). Before spawning: skip unless the
    access token's JWT `exp` minus now exceeds the reader's enforced lifetime plus a margin; an unknown expiry skips.
    That guard NARROWS the risk and is not the safety boundary: an unexpired token can still meet a 401, or codex can
    refresh proactively. **The safety is exclusive ownership of the slot's token set** — a refresh, if one happens,
    happens while nothing else uses that set, and a test proves the NEXT launch on the slot uses the resulting
    credentials. First step of 6.1a: inspect the deployed codex version's managed-auth path for
    `account/rateLimits/read` (start-up, proactive refresh, 401 recovery) and record what it does.
  - **On any auth error or refresh request:** stop the capture, never log in or retry, keep the previous reading marked
    stale, and persist **"gauge capture suspended"** on the slot — its OWN state, never `NeedsSignIn` (which would make
    the recovery launch unschedulable) — until a later launch on that slot succeeds. A capture failure never changes the
    finished job's result. The reader logs allow-listed numbers and fixed codes, never a raw message or stderr.
  - **The expired-token measurement by a COPY is dropped as unsafe:** copying a store duplicates its refresh token at
    the vendor, so refreshing the copy can leave the original holding dead credentials. With no disposable account,
    the expired-token behaviour stays recorded as **unmeasured** — the design does not claim it is unreachable.
  - **6.1b local capture is deferred:** a lock excludes only cooperating coai processes, and "no other codex process
    visible" races with direct use. The local view shows the SERVER's observation only for an explicitly matched
    account, with its source and time; otherwise *unavailable*.
  - **6.2 reset first:** a reset per reported window, identified by **limit identity plus duration** (not primary /
    secondary); the observation time and its staleness; after a passed reset with no newer reading, *"reset time passed;
    availability unverified"* — recovery is never inferred from the clock or the percentage; `ordinaryUsageAllowed`
    kept; the observed gauge and reset shown even when rounds-per-1 % is hidden by the missing-consumer rule (kept).
    Daily token buckets never stand for quota recovery. agy has no gauge: its reset stays the stated one from E5.
- **What E6's plan round and its risk consultation for epic 6 decided (2026-10-11)** — binding on the build:
  - **Containment against a crashed parent (consultation `166a40f3`, all four code claims verified):** a startup sweep
    alone is NOT enough — `SlotRegistry.AcquireAsync` hands back a lease the moment the lock opens, `VendorLogin` uses the
    same path, `ProcessLauncher` starts a child before `ProcessTracking.Record`, and `ProcessTracking.Sweep` kills without
    awaiting exit and forgets the record either way. So the reader's safety lives IN THE SLOT: (1) take the lock;
    (2) reconcile the previous holder's reader marker — kill identified survivors (the whole tree, pid AND start time
    matched) and CONFIRM exit before the lease is usable; (3) persist a fail-closed "a reader may exist" marker BEFORE
    the reader is spawned (no marker written, no spawn); (4) record the child's identity with its start time after
    spawn; (5) on completion confirm every descendant exited, clear the marker, then release the lock. Missing or
    partial identity after a crash leaves the slot UNAVAILABLE until recovery proves it safe (availability is the
    price of an ambiguous crash). Login and every other acquisition obey the same barrier; the startup sweep runs
    through the same slot-locked path. systemd's control-group kill is extra protection, checked on the host at deploy
    (`systemctl show coai-server -p KillMode …`), never asserted from repo text. **Shipping proof:** an external crash
    harness with a fake reader that survives stdin EOF and disposable fake credentials — an already-running contender
    and a newly started server launch nothing until the reader tree is gone; login obeys the barrier; a crash between
    spawn and registration, a partial record and a failed termination keep the slot unavailable across another
    restart; a surviving grandchild is covered; a mismatched pid/start time is untouched. Recorded limit: this proves
    process exclusion, not that a vendor-side rotation survives an interruption before local persistence.
  - **The rounds-per-1 % formula (plan round):** per window (limit identity + duration), between two consecutive
    readings of the SAME window instance (same `resetsAt`): `rounds counted between them ÷ (usedPercent₂ −
    usedPercent₁)`. An interval is unusable — the ratio *unavailable*, never 0 or ∞ — when the percentage did not rise,
    when the window reset between the readings (`resetsAt` changed), or when either reading is stale or missing. The
    figure is the rounds-weighted mean over the last N usable intervals; with none, it is *unavailable* with the reason.
  - **An older client against the new server (plan round):** a seam/contract check runs the previously RELEASED
    extension's parser (extension-v0.69.0) against the new server's answers carrying the gauge fields, and records that
    it still loads usage and the catalog unchanged.
  - **A real-product scenario (plan round):** a scenario through the REAL `JobRunner` and slot with a fake
    `codex app-server` (the FakeCli pattern) — capture inside the lease, stored, served by the endpoint and rendered —
    catalogued in `research/module_tests.md`'s scenario-harness table with its run command and its limits.

## Build order

E1 → E2 → E3 → E4 → E5 → E6, each from the previous epic's commit.

- E2 needs nothing from E1 (the rounds database already holds the history) — it can start the same day.
- E3 needs 1.2's fixtures. E4 writes its fields beside 3.3's and 3.4's on E3's reshaped ledger call.
- E5 needs E4's fields on the wire and in the ledger and E3's outcomes and canary kind.
- E6 needs E1's scope and E5's counter to sit beside.
- **5.3a ships ahead of E4** as a server patch (it depends on nothing in E3–E5) and rides the deploy already waiting for
  the Gemini reset of 2026-10-14.
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
| `CooldownParser_ReadsResetsIn88h49m9s`, `…ReadsResetsIn33m52s` (verbatim), `AStatedReset_ParksTheSlot_UntilThen_AndNotAfter` (the real `JobRunner`, state reloaded, nothing launched before the instant, a launch after it) | 5.3a |
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

## Deviations so far (E2, E3, 5.3a, E4, E5, 6.1a, 6.2)

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
| 3.3 (the extension's probe) | — (the row above left it open) | **(b), on the consultation of 2026-10-11** (`866dbd5f`): `probeUsageFile.ts` — `recordingRun` wraps the probe's `run` at the LAUNCH boundary, so every request is a line before the probe's own merge logic sees it, failures included; `probe-usage.jsonl`, `UsageEntry`-shaped, kind `probe`, provider `claude`, the candidate as the model, Claude's own usage (through `claudeAdapter.classify`, cache reads and writes in the input as for a chat turn) and its billed `total_cost_usd`; a run that did not finish (`capture`'s -1) is *usage not captured*, never 0/0. The Spending tab (`PanelProvider.readUsage`) and the Rounds block (`roundsLoader`'s new `besidePaths`) read it beside `usage.jsonl`; `DATA_TO_MOVE` and `shared/data-inventory.json` carry it. `USAGE_NOT_CAPTURED` is exported from `roundsLog.ts` rather than spelled twice. | An older build never reads the new file, so it UNDER-counts probes (as today) and never misreads one as a chat — the consultation's one caveat, accepted: isolation prevents misclassification, not the undercount of a build that predates it. |
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
| 3.4 (after the consultation) | `effortFrom: ignored` with an empty effort | **Two facts, two fields** (consultation `bcd45d07`, 2026-10-11): `effort`/`effortFrom` say what RAN — sent (`row`/`server`), else the CLI configuration's (`cli-config`, an inference from a root key), else empty — and `effortNotSent` / `tierNotSent` say `ignored` / `dropped` when the asked-for value did not reach the CLI. `TheSameConfig_ReadsAsTheSameEffort_WhetherOrNotTheRowAskedForOne` (mcp) is the consultant's paired fixture; `ACodexRowsEffort_IsRecordedAsIgnored`, the local round test, `ADroppedRemoteEffort_IsRecordedAsDropped` and the extension's parser test were changed on purpose and seen red first. `ignored`/`dropped` are no longer SOURCE words. | One field mixed provenance with disposition: an ignored codex effort hid the inherited `high` that ran — the likely cost driver of the operator's question — and the same config read differently with an inert row setting. |
| 3.5 | POST_DEPLOY item 12 the same | **Not yet.** The by-hand per-vendor review (now item 11) runs against the DEPLOYED server, which refuses the kind until a server carrying 3.5 is deployed; its command gains `kind:'canary'` with that deploy (after E5). | A checklist command that 400s on today's server would be a check that fails for its own sake. |
| 3.2 | `ReviewerExecutor` checks `result.Cancelled` before `result.TimedOut` and returns a NEW `ReviewerOutcome.Cancelled` with `Usage.Unknown`, keeping `AbandonAsync` | The same, and the launch's own `ReviewerLaunch.Usage` is `Unknown` too (the Team server's attempt reads the LAUNCH's usage, and `TimedOut`'s launch usage is `None` — so the deployed line's free 0/0 came from there as much as from the kind). Every switch over the endings got its word: `Describe` → `cancelled`; `ReviewerNotices.ByType` → a new `reviewer-cancelled` code (`ServerNoticeCodes`), because the local round-clock cancellation used to raise `reviewer-timed-out` and losing that notice would have been a regression; `ConsultFailures.Ended` → `killedAs`, as `TimedOut`; the question row's `Unanswered` was NOT widened — a row killed by the caller's cancellation now reads `failed` / `cancelled` instead of `timed_out` "ran past its budget", which was never true of it, and a row past its own budget still reads `timed_out` because its launch's timeout fires before the row's token (`ConsultationDeadline.For` is the budget plus a grace). | A notice row rather than StoodDown's exemption keeps the record a person had; the row's word changes toward the truth and is listed as not covered rather than pinned by a harness the fan-out cannot drive. |
| 3.2 | a structured end cause (`withdrawn` / `abandoned` / `backstop` / `hostStopped`) set AT the transition and carried on the record; the ledger reads it from the STORE's terminal record | **Five words, not four**: `queueExpired` added (`EndCauses`, in the runners library beside `UsageKinds` and `LaunchAttempts`, so the ledger and the extension spell it once) — a queued job that waited its whole window never launched, and filing it under one of the four would be a lie on the record; it writes no ledger line. `JobRecord.EndCause`; `JobTransitions.Cancel` (the one transition that writes `FailureKind.Cancelled`) and `JobTransitions.Expiry`, which pairs the cause with the sentence `ExpiryReason` always gave. `JobStore.Finish` now RETURNS the record the store keeps — the earlier cancel/expiry record when there is one — carrying the late attempt's `Usage` (a billed first launch under a cancelled follow-up; an answer that landed a second before the DELETE), and `JobRunner.Ledger` writes from that, `endCause` on the line that says `Cancelled`. The ledger column is a new trailing `UsageEntry.EndCause` (mirrored in `UsageEntryDto`, `UsageLine`, the extension's parser); the `outcome` stays `Cancelled`. **Locally the column is empty**: the round's clock and the MCP client's own cancellation reach the executor as one linked token (`RoundEngine`), and nothing above it records which fired. | The late usage is folded into the store's record as well as the line because `/api/reviews/{id}` reports `tokensIn` from that record, and a withdrawal that was billed answering 0 would move the free-withdrawal lie from the ledger to the wire. A local cause would need the round engine to tell its clock from the caller — a story of its own, if anyone asks. |
| 3.2 | the wire: a new failure name, or an optional field? | **A new optional field `endCause` on `ReviewStatusDto`**; `failure` stays `cancelled`. Measured before choosing: `coai-mcp --ask-remote` (`AskRemote.FinishAsync`) and the extension (`remoteAsk.ts`) both branch on `status` and only render `failure` inside a sentence; `RemoteStatus` sets no `UnmappedMemberHandling`, so an older client ignores the field. `http/reviews/happy.http` asserts the field on the queued shape (empty) and the cancelled one (`withdrawn`). | A new failure NAME would have been rendered verbatim by every shipped client — harmless, but a change to what a person reads on an old build for no gain; a field nobody old reads cannot be misread. |
| 3.2 | both cancellation shapes produce exactly one ledger line with any earlier usage kept | The same, through ONE road: `JobRunner.RunAsync`'s catch records `new ReviewAttempt.Failed(new ReviewerOutcome.Cancelled())` through `Record` (it used to finish the job and write nothing), and `ReviewLauncher.FollowedUpAsync` turns a cancellation thrown out of the FOLLOW-UP into a `Cancelled` launch so the denied, billed first launch keeps its line (a first launch that throws has nothing earlier to keep, and the runner's catch records it). The runner's own cause is `hostStopped`, and it stands only when no store transition ended the job. | — |
| 3.2 (tests) | the five named tests, and the 1.2 fixture test changed on purpose | `HowAJobEndedIsSaidTests` (7, mcp — the local names differ: `AWithdrawnLaunch_IsItsOwnEnding_NotTheVendorsTimeout`, `ARealDeadline_IsStillATimeout`, `AWithdrawnRemoteReviewer_StillSendsTheDelete`, `ACancelledEnding_HasItsOwnSentence_AndItsOwnNotice`, `AConsultationLaunchKilledByItsCaller_IsStillWhatTheCallerDecided`, `ACancelledTurn_IsOneLedgerLine_…`, `ACancelledRepair_KeepsTheFirstLaunchsLine`) and `HowAJobEndedIsSaidTests` (8, server — `AWithdrawnLaunch_IsRecordedAsCancelled_WithUsageNotCaptured` through the real `ReviewLauncher`, `ARealDeadline_IsStillATimeout`, `AWithdrawalAbandonmentBackstopAndHostStop_EachCarryTheirCause`, `AJobThatExpiredInTheQueue_…`, `AThrownCancellation_WritesExactlyOneLine` and `…OfAFollowUp_KeepsTheFirstLaunchsUsage` — the planned `AThrownCancellation_WritesExactlyOneLine_KeepingEarlierUsage` split in two, because the first-launch shape has no earlier usage to keep, `ACancelThatLandsWhileFinishRuns_…`, `AnOldLedgerLine_StillReads_AndANewOneKeepsItsEndCause`); `UsageLedgerFixtureTests.TheDeployedWithdrawal_WasWrittenTimedOut_AndThisServerWritesItWithdrawn` (renamed from `TheWithdrawal_IsWrittenTimedOutWithNothingCounted_WhichIsTheDefectE3Names`: the deployed bytes stay asserted as history, the same event through this server beside them, via the shared `WithdrawnJob`); `ReviewEndpointTests` (+1 assertion), `usagePricing.test.ts` (+1), `TheReviewerFailuresAreWrittenDownTests.Built` (+1 builder). `ScriptedResults` extracted from `EveryPaidLaunchIsWrittenTests` rather than a fourth private copy (three exist: `ConsultantTurnTests`, `TheVaultKeyTravelsOnStdinTests`, and the extracted one — the other two are left as found). Listed in [module_tests.md](../research/module_tests.md). | — |
| E3 (code round) | — | The gate's code round for epic 3/6 (codex, four reviewers, `proceed`, 4 gating): **accepted** — (1) a cancellation while the rate-limit ladder WAITS no longer throws: the turn ends as the refusal it last saw, its paid launches kept (`BoundedScheduler.WaitedOut`); (2) `UsageLedger.RecordJob`'s `LaunchOptions? ranWith = null` became two overloads, the Team server's with a non-null `LaunchOptions`; (3) the deploy canary's old-server fallback is RUN by the suite (`TheCanaryFallbackRunsTests`: `deploy/systemd-release.sh` stops after its functions when sourced, and the real `canary` meets a stub curl). **Rejected** — a consultation's / question row's continued turn as one line (census rows 2/4, already this table's open tail; the line carries the turn's correct sum), and caching the codex config read per round (minor; one small local file per row). | — |
| 3.2 | help text in five languages if a person sees the new words | **No help change**: nothing renders `endCause` yet (as with 3.4's fields), and the one word a person can meet — `cancelled` as a reviewer's note where `timeout` stood — is English in every language, as every outcome word there is. | — |
| 5.3a | part of 5.3, shipped with E5's deploy | **Extracted and shipped ahead of E4**, server only; the move to the shared library, the local display and local enforcement stay in 5.3b / 5.3c. A stated duration is believed up to `MaxStatedWait` = **14 days**, read whole or not at all (one space allowed inside, `2h 30m`); a stated wait it cannot read — past the ceiling, too large to be an instant, glued text, a spaced trailing number, a wait in words — errs long to `MaxGuess` (5 h), never the 30-minute retry and never a throw; a line that also says "weekly" takes the weekly day, the longer guess. | The E4–E6 cadence consultation (verified in code): it needs neither round ids nor aggregation, and the server re-asked an exhausted Gemini slot every 30 min → 5 h for ~110 h. The bound is the plan round's finding (a malformed refusal must not fail the job); "whole or not at all" and "err long, to MaxGuess not MaxStatedWait" are CodeRabbit's on #754, the second narrowed: a fortnight's park over one malformed sentence was the other wrong direction. |
| 5.3a (tests) | a scenario-harness case through the product | The scenario as deep as the Team server's harness reaches without a product seam: the real `JobRunner`, `ReviewLauncher`, antigravity adapter and `SlotRegistry` on disk, a fake `IProcessLauncher` for the paid vendor — `AStatedResetParksTheSlotTests`. The HTTP host is not used: the cooldown is set after the claim, and `Program.cs` builds the launcher inline, so a vendor double there would need a product seam this story does not add. A real refusal on the deployed box is checked live at the deploy. | The plan round's second finding, accepted at the depth the harness has. |
| 5.3b | `CooldownParser` moves to the shared runners library; move its tests too, or keep them in the server suite | **Moved to `src_mcp/runners/Reviewers/CooldownParser.cs`, its 16 tests with it** (`src_mcp/tests/CooldownParserTests.cs`; `SlotTests.cs` keeps the slot environment and selector tests, `AStatedResetParksTheSlotTests` stays server-side). A second entry point, **`StatedReset`** — only what the vendor NAMED, null otherwise — beside `Until`, which keeps the server's guesses. #754's comma/semicolon fix (`3d18713f`) carried into the moved copy. | Tests live with the type and its other readers (the ladder, the ledger); the local half must never write or show a GUESS as if the vendor had said it. |
| 5.3b | the last stated reset shown locally: record it at the writer (a ledger field) or parse the outcome text in the extension | **At the writer:** a trailing, defaulted `UsageEntry.ResetsUtc` (`O`, UTC) on the LAST line of a turn that ended `RateLimited` (`UsageLedger.Record`), parsed by `usage.ts` and worded by `statedReset.ts` — *quota out — resets in 89 h (Wed 04:49)* on the Rounds block's row and the Models card, only while the row's newest line is that refusal and the instant is ahead, hours rounded UP. **Not mirrored in the server's `UsageEntryDto`/`UsageLine`:** the server writes no such field (its reset lives on the slot, said by the catalog), and `src_server/src/Usage/` belonged to 5.1's parallel worktree. ~~Not written for a consultation or a question row~~ — **written there too since epic 5's code round** (the row below). | One parser of vendor sentences, in C#; the extension reads an instant, never a phrase. |
| 5.3b | the catalog adds the earliest reset per vendor | `SlotSummaryDto.resetsUtc` (`""` when nothing is parked — a string, as `endCause` is), the minimum `CooldownUntilUtc` of the slots cooling down; the body names no slot and carries no email (asserted). **Also shown:** `slotSentence` ends *all N rate-limited … — the first in 89 h (…)*, through the same `untilWords` (a leaf, `resetWords.ts` — in `statedReset.ts` it closed three import cycles, caught by `importCycles.test.mjs`). `http/catalog` asserts the empty string. | The field is only useful where a person reads it; one wording for both halves. |
| 5.3b | a queued job hears `SlotSelector.Explain` — an optional additive field or the existing note field | **A new optional field `waiting` on `ReviewStatusDto`**, computed at the poll by `QueueWaiting` (`src_server/src/Slots/QueueWaiting.cs`): `Explain`'s sentence only for a QUEUED job that `SlotSelector.Pick` finds no account for (rate-limited → *the first comes back at …*; signed out → the `login` cure), empty otherwise. `reason` was not reused. The shim reads it (`RemotePoll.Waiting`) and appends it to its queued progress line, whose stem `IsProgress` still knows. `http/reviews/happy.http` asserts it. | `reason` is the FAILURE's own words and the shipped shim renders it inside a failure sentence — a queued job has failed at nothing; an additive field an older client never reads cannot be misread (the `endCause` precedent). |
| 5.3c | the ladder gives up on a stated reset beyond the reviewer's budget | As decided: `RetryLadder.ResetOutlasts(reason, left, now)` — `StatedReset − now > Remaining(elapsed, budget)` — beside `RateLimit.Hopeless` in `BoundedScheduler.Hopeless`; a short stated reset is still waited out; nothing persisted. `BoundedScheduler` gains an optional `TimeProvider`. **Found on the way:** agy's second stderr line (`AGY_ERROR … RESOURCE_EXHAUSTED …`) already matches *exhausted*, so with that line present the refusal was hopeless before this story — the ladder climbed on the plain verbatim line; the consult fixture's 34-minute reset is therefore hopeless by WORDS whatever the budget. Not changed here. | E5's plan round; the second point is recorded rather than fixed because loosening `Spent` is the other plan's (PLAN_a_stated_wait_is_read_rather_than_matched). |
| 5.3b / 5.3c (tests) | RED first through the scripted launcher | `CooldownParserTests` (+2, +3 assertions), `AStatedResetIsNeverWaitedOutTests` (9), `AStatedResetIsWrittenLocallyTests` (5), `AQueuedReviewHearsWhenItCanRunTests` (3), `AParkedSlotIsHeardTests` (5, server), `statedReset.test.ts` (9), `teamServerView.test.ts` (+1), the CSS pin re-taken, two `http/` assertions — each RED message and break-it in [module_tests.md](../research/module_tests.md). | — |
| E5 (code round) | — (three findings of epic 5's code round, accepted) | **(1, gating) every LOCAL refusal writes its stated reset:** `RecordJob`'s local overload takes `resetsUtc`, filled by the one helper `UsageLedger.StatedResetOf` (public now) from a consultation's, a consultant check's and a question row's final launch; the Team-server overload has no such parameter (both build through one `JobEntry`). RED through the real consultation path and the real question-row path with agy's verbatim `88h49m9s` line. **(2)** `TheWindowAnswerKeepsItsBytesTests` filters a COPY instead of mutating the parsed tree — test-only, no RED. **(3)** `UsageRounds.RequestsByWord` counts retries, repairs and follow-ups in one pass — no behaviour change, guarded by the existing round-count tests and the seam leg. | The code round's findings; the narrower behaviour 5.3b recorded ("not written for a consultation or a question row") was the gating one. |
| E5 (code round, rejected) | — | One gating finding **rejected**: "help in five languages conflicts with the English-only rule". The owner decided on 2026-10-09 that the extension's help stays in five languages (en, ru, uk, de, es), and `helpCoverage.test.ts` requires every article in all five; the English-only line governs code, logs, API messages and commits. | An owner decision, not this epic's to change. |
| 4.1 | the engine mints the id, persists it on the round (`round_key`, the session JSON), and every launch carries it | As planned: `RoundKeys.Mint()` (`RandomNumberGenerator.GetHexString(32, lowercase)`), `RoundRecord.Key`, schema step 21 `Schema.TheRoundsKey` (no index — nothing looks a round up by key locally). The key travels as **`ReviewerInvocation.Round`**, stamped by the ENGINE on the work the stage built (`RoundWork.InRound`: each reviewer's launch and repair, the security lane's too) rather than through the roster's `ReviewerSettings`: a retry re-launches the invocation, a follow-up is derived from it with `with`, and `TurnLoop` hands a conversation's later turn its first turn's key. The roster, the stages and `StageRun` did not change. A skipped round is minted none. | One stamp at the one place every launch of a round passes, instead of a new `MakeWork`/`BuildWork` parameter through four stages and some twenty harnesses. |
| 4.1 (risk amendment 2) | `remainingSeconds` from the EFFECTIVE timeout — "the argv rebuilt from `Request.Timeout`, or the value passed separately" | A new adapter seam, **`IReviewerRuntime.ForLaunch`** (identity default; only `RemoteRuntime` overrides it), called by `ReviewerExecutor.LaunchAsync` once per launch, immediately before the process starts: `--timeout-seconds` and `--vendor-timeout-seconds` re-derived from `Request.Timeout`, `--attempt` and `--round` from the invocation. `RetryLadder` is unchanged. **`ReviewerInvocation.Attempt`** carries the word — `retry` stamped by the ladder, `repair` / `followUp` by the executor; never `rotated`, which is the server's own. The shim computes `remainingSeconds` at submit from its own clock, and the submit's HTTP budget is that remainder. | Rebuilding at launch — the last moment — covers every shortening with one road, the turn loop's `Bounded` included; rebuilding inside `Lesser` / `WithinRemaining` would have been two sites and would have missed it. |
| 4.1 (risk amendment 1, client half) | an exhausted effective budget prevents the submit | `AskRemote.SubmitAsync` refuses `RemainingSeconds == 0` before the wire: exit 78 (`TooSlow`) with `RemoteAsk.BudgetSpentMessage`; `RequestBody` leaves a non-positive value out. Reachable today only by hand (`--timeout-seconds 0`): `LocalAsk.ShimDeadlineSeconds` floors a derived deadline at five seconds, so a shortened launch always advertises at least five. | The floor is shared with `--ask-local` and predates the story; whether five seconds should become a refusal is 4.3's server rule to decide, by name. |
| 4.1 (the probe) | `TheNewBody_IsAcceptedByTheLiveServer_ThenDeleted` — POST, 202, DELETE | **`TheNewBody_IsAcceptedByTheLiveServer`** — the zero-spend probe E4's plan round decided (`src_vs_code/src/test/reviewBody.contract.ts`): the new body plus an unknown member with `"kind":"probe-no-such-kind"` → 400 by kind; the control (`timeoutSeconds: "x"`) → the binding error. Run through `npm run test:contract` against the server built from THIS tree (HEAD without 4.2 — the same binding, `ServerJsonContext.cs:252`, the deployed server has); not yet against the DEPLOYED server (`COAI_CONTRACT_URL`, its key, `COAI_CONTRACT_ALLOW_REMOTE=1`). | The plan round's own decision: POST-then-DELETE is not zero-spend. |
| 4.1 (not in the plan) | — | The LOCAL ledger line does not carry the round. `UsageEntry` was left untouched so story 4.2's trailing `Round` / `ClientAttempt` columns (built in another worktree at the same time) merge without a conflict; the key sits on `ReviewerInvocation.Round` for the local writer to take once the column exists. | Two stories touching one shared record from two worktrees. |
| 4.1 (tests) | `ARetryARepairAndAFollowUp_ShareTheRoundsId`, `Again_GetsANewId`, the probe, the MUST LAUNCH set, the stale-budget test | `ARoundHasAnIdOnTheWireTests` (9), `ARoundIsMintedAKeyTests` (3 — the engine-level stamp through a spy adapter, a second turn included), `TheRoundKeyIsStepTwentyOneTests` (8), `RemoteShimScenarioTests` (+1, the real binary), `reviewBody.contract.ts` (2); `TheReviewersModelIsStepTwentyTests` changed on purpose (it pinned the step count), and `ThePairsThemselvesTests`' step-12 rewind drops `round_key` too (the full suite found it: a column the rewind keeps makes the reopen's migration throw and the test dereference a null store). Each RED for its real symptom first, then four plant rounds — listed in [module_tests.md](../research/module_tests.md). | — |
| 4.2 | `ReviewRequestDto`, `JobRecord` and the ledger line carry `round` and `attempt` | **Three request fields bound as `JsonElement?`** (`round`, `attempt`, `remainingSeconds`) and read by `RoundFields.Take` (`src_server/src/Jobs/RoundFields.cs`); `JobRecord.Round` / `ClientAttempt` / `RemainingSeconds`; the ledger gains **two** trailing `UsageEntry` fields after `EndCause` — `round` and **`clientAttempt`** — mirrored in `UsageEntryDto`, `UsageLine` and the extension's parser (`roundFields`). The line's `attempt` stays the SERVER's (`first` / `rotated` / `followUp`, story 3.3); the client's word goes beside it, never over it. | A client's `repair` or `retry` is a whole new JOB on the server, whose first launch is still the server's `first`; and a client's `retry` whose job then rotated is both `retry` and `rotated`. "The client word wins for `first`" would lose one of the two in exactly those cases, so neither meaning overwrites the other. A local line leaves `clientAttempt` empty — its own `attempt` already is the client's word. |
| 4.2 | a malformed value is dropped with a `Dropped` note; absent = an old client, stored empty; amendment 3's matrix — omitted, `null`, `0`, negative, `"oops"`, `{}`, fractional, oversized — "each accepted with a `Dropped` note" | Every value is accepted (202) — never refused. A value that was SENT and is malformed is dropped with one note naming its field, in the same `dropped` list `ClientOptions` fills; **omitted and JSON `null` get NO note** — both read as an old client, stored empty, never defaulted. A note quotes a short printable value and describes anything longer than 40 characters by its length, never echoing a 4 KiB value. Nothing is minted in place of a dropped round. | `effort: null` already reads as absent; and a note for a field nobody sent would be shown by every older client on every review. `null` was measured: a `JsonElement?` receives JSON `null` as a C# `null`. |
| 4.2 | (4.3's first bullet) `remainingSeconds` validated at submit — a positive integer, else dropped with a note | **Validated here, in 4.2**, as the brief asked: a JSON number that is a whole positive `int` (`"60"`, `60.0`, `1e400`, `2147483648` all dropped), kept as `JobRecord.RemainingSeconds` (`0` = none). **Not capped, not on the ledger line, not acted on** — the cap, the deadline and the budget rule stay 4.3's. | One reading of the three fields in one place; 4.3 adds the rule, not a second parser. |
| 4.2 | `round` | A JSON string of exactly 32 hex characters, **either case, kept in lower case**; anything else dropped. | The client may write the 128 bits with `ToHexString` (upper case) or `ToHexStringLower`; one round must never be two in story 5.1's distinct count. |
| 4.2 | (not in the plan) | **Not in the idempotency fingerprint**, and **not echoed by `GET /api/reviews/{id}`**. | A retry of a lost submit carries the same round and less time left, and is the same request (`ARepeatedSubmit_WithLessTimeLeft_IsTheSameJob`, seen red when the seconds were planted into the fingerprint: a 409). The client minted the round and holds it; the status describes the job, and a field no client reads is wire surface without a reader. |
| 4.2 (tests) | `AMalformedRound_IsDroppedWithANote_AndTheReviewRuns`; amendment 3's matrix through the real endpoint | `TheServerRecordsTheRoundTests` (52, server): the plan's test by name, the matrix as 32 malformed rows + 6 absent/null + 9 well-formed through the REAL `POST /api/reviews` with raw JSON, the repeat, a valid round on BOTH lines of a rotated job beside the server's `rotated`, an old client's line, the reader (`AnOldLedgerLine_StillReads_AndANewOneKeepsItsRoundAndClientAttempt`); `http/reviews/happy.http` +4 requests (well-formed: nothing dropped; malformed: three notes — through the source-generated binding); `usagePricing.test.ts` +1. First RED against a NAIVE typed binding: 18 matrix rows answered **500** `{"error":"internal error"}` in the in-process host. Listed in [module_tests.md](../research/module_tests.md). | The 500 (not a 400) is what the in-process host answers for a binding failure; either way the review was refused, which is the symptom amendment 3 names. |
| 4.3 | `Coai:MinUsefulRunSeconds` (default 60): a job with less than that left is ended | **`Coai:MinStartSeconds` (default 10)** beside **`Coai:DeadlineSafetyMarginSeconds` (default 5)** — `ClientBudgetPolicy`, refused at boot for a negative margin or a non-positive allowance: a job is ended at claim only when `ClientDeadlineUtc − now < MinStart`, the rule being "the caller can no longer wait for a launch's start-up" (risk amendment 1). The two numbers in `deploy/docker-compose.yml` as `DEADLINE_SAFETY_MARGIN_SECONDS` / `MIN_START_SECONDS`. | The amendment's own finding, now measured: under a 60 s floor planted into the default, the MUST LAUNCH set went red with *a fresh 30 s reviewer MUST LAUNCH; it advertised 19 s and was refused*. |
| 4.3 | `ClientDeadlineUtc = receiptUtc + min(remainingSeconds, cap) − margin`, computed at submit by the endpoint | The same arithmetic (`ClientBudget.DeadlineOf`), stamped by **`JobStore.Submit`** from the record's `SubmittedUtc` — the endpoint's receipt stamp, this process's clock — onto `JobRecord.ClientDeadlineUtc`; absent `remainingSeconds` → no deadline. The cap is `timeoutSeconds + (QueueDeadlineUtc − SubmittedUtc)`, the job's own lifetime. | One class owns the whole rule: the store stamps at submit and judges at claim, so neither half can be wired without the other, and the receipt and the submit stamp are one instant. |
| 4.3 | at claim: ended, the poller woken, nothing launched, the claim moves on; `TryClaim` picks one candidate today | **`TryClaim` answers a `ClaimOutcome(Job?, Token, Refused)`** — the job it started, if any, and every job it ended for want of budget; the candidates are materialised and walked oldest first under the claim's lock, each refused one ended with `JobTransitions.WithoutBudget` (`FailureKind.NoBudget`, the reason interpolating `MinStart`) and woken, the first that can still wait claimed. The run budget is capped in `JobTransitions.Start` (`ClientBudget.RunBudgetOf`) and the record KEEPS the capped number, so the timeout's and the backstop's sentences name what ran. Ten test call sites rewritten from the nullable tuple. | A refusal that the runner could not see would write no line; the shape carries both answers from one locked pass. |
| 4.3 | a ledger line with outcome `NoBudget` and an honest zero | `JobRunner.RefusedForBudget`: one line per refused job from the record the store ended — `NoBudget`, `Usage.None` (0/0, **no** usage note: nothing launched, so nothing is uncaptured), 0 s, **`slot` empty** (no account carried it), `attempt` `first` or `rotated` by the job's `RefusedBy` as every line decides it, `LaunchOptions.None`, the job's kind/round/clientAttempt/turn/job as 4.2 writes them, `endCause` empty. Nothing is logged beyond the line. | — |
| 4.3 | mapped to a distinct client outcome, read by name | **The shim's own exit, `RemoteAsk.NoBudget` = 79**, with `RemoteAsk.NoBudgetMessage`, when `failure` equals `RemoteAsk.NoBudgetFailure` (`no_budget`) by ordinal comparison (`AskRemote.Ended`); every other name is `VendorFailed` (70) with `FailedMessage`, which is also what a shim OLDER than the name does with it — the name inside its sentence, nothing thrown. The executor sees a `NonZeroExit(79, …)`; **no new `ReviewerOutcome` case** was added. The extension's `remoteAsk.ts` (remote chat) renders any `failure` string inside a sentence — read, untouched. | A new outcome type would widen every switch over outcomes (`Describe`, the notices, the ledger, the summary) for one server ending; the shim's exit code is the distinct outcome the round reads, and the phrase guard (`TheNoBudgetReason_MatchesNoRateLimitPhrase`, both shims' sentences) is what keeps the ladder from retrying it. If a round should one day treat 79 as its own ending, that is a story of its own. |
| 4.3 | transit (E4's plan round) | As decided: the margin is the transit allowance, documented as a stated limit in `module_team_server.md` (*Transit time is the margin's to absorb*); a request delayed by more than it can still be claimed and is then withdrawn by the caller's DELETE. The edge test holds 14 s refused / 15 s launched on a fixed clock. | — |
| 4.3 | — (not in the plan) | **The sweep does not read `ClientDeadlineUtc`.** A queued job past it waits for the next claim pass on its vendor, where it is ended and counted, or its queue window ends it as before (`queueExpired`, no line). | A sweep that ended it would lose the count the story exists to make; and nothing is spent while it waits. |
| 4.3 (tests) | the seven named tests; the MUST LAUNCH set through the real path; the margin edge; amendment 5's interleaving; the old client on the new name | `NoStartWithoutBudgetTests` (10, server — the seven, `AStatedBudgetLongerThanTheJobsOwn_LeavesTheRunBudgetAlone`, the edge, amendment 5's `ADeleteBeforeTheClaim_LaunchesNothing` through a counting launcher, the boot guard); `TheCallerCanStillWaitTests` (3, server — the clock test sends FOUR ten-minutes-off instants in the real body; the MUST LAUNCH set runs the REAL `--ask-remote` code in-process against `ShimCapture`, a loopback listener in the server tests, and replays each captured body to the real `POST /api/reviews` and claims it at once; the configured allowance read through the real host); `TheServerRefusesForBudgetByNameTests` (4, mcp). **`CoaiServer.Tests` now references `CoaiMcp.csproj`** (and `CoaiMcp.csproj` opens its internals to it) for `AskRemote.RunAsync` / `Program.Flags`. `http/reviews/happy.http` carries an `@uncovered` for `no_budget` (a claim needs a signed-in account; the suite's stack has none by design). Listed in [module_tests.md](../research/module_tests.md). | Capture-and-replay is the plan round's own wording ("`RemoteRuntime` → captured POST → immediate claim"); the in-process host has no socket for the shim's `HttpClient`, and a Kestrel-backed factory would have made the claim a race against the shim's own polling. |
| E4 (code round) | — | The gate's code round for epic 4/6 (codex, four reviewers, `proceed`, 1 gating), **accepted**: `JobRecord.RemainingSeconds` (an `int`, `0` for both an absent and a dropped value) became `JobRecord.StatedBudget` — `Absent`, `Stated(seconds)` or `Dropped(reason)` (`StatedBudget.cs`; `TakenRound.Budget`), the reason being the request's own note. Only a stated budget makes `ClientDeadlineUtc`, so no behaviour moved; the matrix test asserts the state and the reason, RED first on the inert shape. | The family rule that "not captured" and "empty" are different facts and never the same value. |
| 5.1 | `windows=5h,24h,week` answers several windows from ONE scan; decide the shape | **The response shape:** the answer's top is still the `window` asked (default `today`) — today's shape — and a trailing `windows[]` is ADDED only when asked: per name `{window, fromUtc, toUtc, vendors, people}` (people empty on a personal answer), from one `UsageReader.Read` over the union of every range the answer needs (the top's, the company chart's, each window's). `windows` is omitted (not `null`) when not asked; an unknown name in it is the same 400 as an unknown `window`; `?windows=` is no windows; a repeat is answered once. | Additive in the strictest form available: an old client asking `?window=` reads every key it read before, in the same order with the same values (`TheWindowAnswerKeepsItsBytesTests`, golden text captured before the change); the only difference is the count keys added to each vendor row — the plan's own "`VendorTotal` gains". |
| 5.1 | `retries`, `repairs`, `followUps` from the line's `clientAttempt`; how a server `followUp`/`rotated` counts | Counted per **REQUEST** — distinct job (else turn, else the line) — so a client retry whose job met a refusing account first (two lines) is one retry. **The server's own `followUp` and `rotated` launches (its `attempt` word) are counted in none of the three**: they are launches inside one request, visible in `runs` and `launchesByKind`. `rounds` counts `review` lines only (chat and canary out) and keeps a `NoBudget` line in its round; `launchesByKind` (a JSON object, kinds in ordinal order) leaves `NoBudget` lines out — nothing launched — while `runs` stays every line. | One client request is one job, and a job can write several lines (3.3's one line per launch); counting lines would make a rotation look like a retry. |
| 5.1 | (not in the plan) | A personal answer now applies its own window to the scan (`Mine` used the whole scan, which was its window until the scan could reach further back for the windows) — `APersonalAnswer_ReadsAsFarBackAsItsWidestWindow`. | — |
| 5.2 | render into the Team server tab (the boundary table); the brief named `teamUsageBlock` | **The Team server TAB** (`teamServerTab.ts`'s Vendors part, via a new pure `teamRounds.ts`), not the old sidebar `teamUsageBlock` — new UI only in the new design. Per vendor of the week window: `5 h N · 24 h N · 7 d N`, and over 7 d the runs without a round, retries, repairs, follow-ups, cancelled and budget refusals. The company request (`fetchUsage`) adds `&windows=5h,24h,week`, so the rounds ride the same cached answer as the chosen window's figures; a personal request asks none. | The plan's boundary row says the tab; the old block is on the page epic 5 of PLAN_one_model_catalog retires. |
| 5.2 | an answer without the requested windows and count fields is *count unavailable* in those columns | `serverRounds` believes the counts only when EVERY round window came back by name and EVERY vendor row in it (people's too) carries every count as a whole number — anything less makes the WHOLE block *count unavailable* with the reason (`ROUNDS_UNAVAILABLE`), never per-column zeroes. The reason names no server version: the release that carries E5's server is not cut yet. | A half-believed answer would mix a measurement with a gap in one row. |
| seam leg | a new leg of `npm run test:seam` starts the real server over a seeded ledger and reads `/api/usage?windows=…` (and `scope=company`) through the extension's own parser | As decided: `scripts/seam-usage-counts.mjs`, wired into `run-seam.mjs` after the catalog leg. The company answer is asked exactly as the tab asks (`fetchUsage`); a PERSONAL answer's windows go through the same client's `ask` with the route spelled out, because no extension surface asks them. The server is started by `scripts/team-server-child.mjs`, **extracted from `run-contract.mjs`** (which now uses it) rather than copied. | Reuse-first: one way to start, wait for and kill the real server. |
| 6.1a (first step) | inspect the deployed codex version's managed-auth path for `account/rateLimits/read` — start-up, proactive refresh, 401 recovery — and record it | **Inspected in source at `rust-v0.160.0`** (the measured version; the host's installed version is not pinned anywhere in this repository): start-up loads `auth.json` and refreshes nothing; `auth()` refreshes proactively when the access token's JWT `exp ≤ now + 5 min` (`CHATGPT_ACCESS_TOKEN_REFRESH_WINDOW_MINUTES`), and only with an unreadable `exp` when `last_refresh` is older than 8 days; the read's 401 becomes a JSON-RPC error with no refresh-and-retry in that path (`BackendClient` has no refresh; `unauthorized_recovery` is the model-request path's); `chatgptAuthTokens/refresh` is sent only to a client that supplied external tokens. Recorded with file:line references in [module_team_server.md](../research/module_team_server.md). | The deployed host was never touched; what its version does if it differs from 0.160.0 is not established — the guard's margin (10 min) is the five-minute window doubled, and the lease is the boundary either way. |
| 6.1a | `quota5hPct`, `quotaWeekPct`, `quotaResetsUtc` on the ledger line | **Per reported window, flat, nine trailing `UsageEntry` fields** (`gaugeObservedUtc`, `gaugePlan`, `gaugeLimit`, `gaugePrimaryMins` / `UsedPercent` / `ResetsUtc`, `gaugeSecondaryMins` / `UsedPercent` / `ResetsUtc` — `GaugeOnLine`), mirrored in `UsageEntryDto`, `UsageLine.Gauge` and `usage.ts` (`gaugeFields`); the numbers are nullable and left off the line when null, never 0; the slot's `state.json` keeps every window of every limit (`GaugeReadingDto.Windows`, identified by limit id + duration) plus `planType`, `rateLimitReachedType`, `ordinaryUsageAllowed`, `resetCreditsAvailable`, the observation time and `stale`. | The 6.1 consultation: a reset per window identified by limit identity + duration, never primary/secondary by name; the measured plan reports no five-hour window, so a named `quota5hPct` would be a 0 or a lie. |
| 6.1a | the read runs inside the lease "after the job" | After the job is made TERMINAL in the store and before its ledger lines are written (`JobRunner.RecordAsync`: `Finish` → capture → `Ledger`), with the HOST's token, never the job's; a requeued (rate-limited) attempt reads nothing; a capture failure never changes the finished job's result because the job was final before the reader ran. The lease is lengthened by the reader's run (≤ 20 s); a caller polling the job is answered before it. | The plan's own order, with "after the job" made exact. |
| 6.1a | the containment: marker → identity → confirm, in the slot | As the epic-6 consultation fixed it, and `AcquireAsync` now answers a closed `Acquisition` (`Taken` / `Busy` / **`Blocked(code)`**) rather than a nullable lease — the three production callers and `SlotRegistryTests` changed on purpose. `login` refuses a blocked slot with **exit 76**. A blocked slot is re-reconciled on EVERY later acquisition, so a block over a member that later dies lifts by itself; `reader-identity-missing` needs a human (stop the server, verify no `codex app-server` runs on that `CODEX_HOME`, remove `reader` from `state.json`). The startup sweep is `GaugeReaderSweep`, a hosted service that acquires each slot for an instant. | "Busy" and "unavailable until recovery proves it safe" have different cures, and a nullable lease could not say which. |
| 6.1a | kill identified survivors, the whole tree, and confirm exit | The launcher gained the tree discipline for a request that ends its child itself (`ProcessRequest.EndsWhen`, `ProcessResult.Ended` / `Survivors`): descendants listed BEFORE the kill and handed to the tracker, the tree killed, each member confirmed gone within 2 s (`ProcessTree`, Linux via `/proc`, Windows via Toolhelp32 — **macOS unsupported, and the capture refuses to spawn there**). A child whose record fails is ended before the launcher throws. Reviewer launches are unchanged. | Widen the one launcher rather than a second launch path (reuse-first); fail closed on the unprobed platform (platform-limits). |
| 6.1a | a fail-closed marker before the spawn | `SlotStateDto.Reader` (`spawning` → `running` with `tree[]` of pid + start time), beside `Blocked`, `Gauge`, `GaugeSuspended`, `GaugeSuspendedUtc` — five trailing, null-on-older-files fields on the existing atomic write. A capture whose launcher THREW clears a marker still in `spawning` (nothing was recorded, and the launcher ends what it cannot record) and keeps one that names the child. | Without that clearing, a box with no `codex` on its PATH would block every slot after one job. |
| 6.1a | the expiry guard: "the reader's enforced lifetime plus a margin" | `ReaderLifetime` 20 s + `ExpiryMargin` 10 min, against the JWT `exp` of `tokens.access_token` in `<slot>/.codex/auth.json` (`CodexAuthExpiry`: the one claim decoded, the token never kept or logged); an unknown expiry — no file, not `chatgpt` mode, no `last_refresh` (codex's own precondition for a token-backed request), no token, not a JWT, no `exp` — skips with its code. | codex's own window is five minutes; ten covers a clock a few minutes off. |
| 6.1a | the catalog serves the latest reading per vendor | `SlotSummaryDto.Gauge` — nullable AND omitted when null (the `UsageDto.Daily` precedent), the newest reading by `observedUtc` among the vendor's accounts, no account name, no email; `http/catalog/catalog.http` asserts the key is absent when nothing was read; `teamServerApi.ts` gained the optional type only (6.2 renders). | An older server and a server that never read are one absence. |
| 6.1a | the older-client check (`extension-v0.69.0`'s parser against the new answers) | `scripts/live-gauge-compat.mjs` (`npm run test:gauge-compat`): the released extension's own `createSession`, `fetchCatalog` and `parseUsage` against this tree's server over a planted reading and a ledger line carrying the gauge fields. By hand, not in CI (the network, a released tag). | The paths-per-side compat scripts' shape. |
| 6.1a (tests) | the named tests and the external crash harness | `AReaderIsEndedWithItsTreeTests` (5, mcp — the fifth: a server-shaped request keeps stdin open, so a child that exits on EOF cannot outrun its own answer and skip the tree discipline), `GaugeCaptureTests` (13), `ReaderBarrierTests` (8), **`GaugeReaderCrashHarnessTests`** (5 — the real binary as a child, a fake `codex` on its PATH, killed alone mid-read; a contender and a fresh server; `spawning` and partial markers across a restart; a reused number untouched), `SlotSelectorTests` (+1), `usagePricing.test.ts` (+1), `catalog.http` (+1). A kill that never takes is staged through the registry's reconcile seam — no test machine can make such a process safely. Every RED message and break-it in [module_tests.md](../research/module_tests.md). | The implementation landed before the tests; every test was then reddened by planting the behaviour line away and restored from a byte copy. |
| 4.1 (the old-server measurement) | the contract suite POSTs the new body to the LIVE old server before the client ships | **Measured on the deployed VERSION, locally** (2026-10-11): `coai.remsoft.dev/api/health` answered `0.9.0`; the tag `server-v0.9.0` was checked out in a scratch worktree, built, and `reviewBody.contract.ts` run against it by that tree's own `npm run test:contract` — `TheNewBody_IsAcceptedByTheLiveServer` (the kind refusal reached, so the three new members and an unknown one were bound past, no job made) and the control (a malformed known member is the binding error) both passed. Cited: 0.9.0's `JsonSourceGenerationOptions` sets no `UnmappedMemberHandling`. The probe against the production URL itself waits for the owner (it needs a session token for the company server) and runs with E5's deploy. | The same code that serves production, with no token and no request to the company server; the live run adds only the network path, which the deploy's canary exercises anyway. |
| 6.2 | rounds per 1 %, "trailing over the last N gauge readings", computed ONCE in the shared runners library | **`RoundsPerPercent.Derive`** (`src_mcp/runners/Reviewers/RoundsPerPercent.cs`), the plan round's formula exactly: per vendor × window (limit id + duration) × account, consecutive readings of one instance (same `resetsAt`); unusable — `null` with a fixed code, never 0 or ∞ — on a missing reading (a launch between with none of its own, or a reading without the window), a stale one (older than its line by > 5 min), a reset, no rise, or a review without a round; the rounds-weighted mean of the last **10** usable intervals; with none, the latest interval's reason. A reading belongs to the launch whose line carries it; a round over several launches counts in each interval by its SHARE of them. | The plan named the rule, not N or how a reading meets its rounds; one job's lines carry the reading taken after it (6.1a), and a code round's four launches would otherwise count as four rounds. |
| 6.2 | the formula as fixed | **Recorded limit, open for the owner:** with whole-number percentages, consecutive one-job readings rise by 0 (unusable) or 1, so the usable intervals over-represent the jobs that tipped the percentage and the figure leans high. Pairing a reading with the last one whose percentage DIFFERED removes the bias; it was not built, because it changes the formula the plan round fixed. For codex the figure is hidden anyway (below). | Never shown today; decided before it is. |
| 6.2 | the missing-consumer rule: the number hidden, the tab says which consumer is missing | `GaugeConsumers.UncountedFor(limitId)` — `codex` → `direct-codex-use`; the figure `null`, `unavailable: consumer-not-counted`, `missingConsumer` named; keyed by the LIMIT the reading names, not by the vendor row's id, so a server vendor named otherwise is still covered. The gauge and its reset are shown regardless (the catalog's). | The owner's rule; showing an upper bound instead stays the owner's call. |
| 6.2 | answered by `/api/usage` | `UsageDto.gauges` — nullable AND omitted when null, present exactly when `windows` is (the Team server tab's request); over EVERY caller's lines (a gauge is the account's), the last **8 days**, a budget refusal left out; the one scan widened to the lookback when windows are asked. `[]` over a ledger with no readings. | The `daily`/`windows` precedent: an older client's request is today's shape. |
| 6.2 | answered by `--round-counts` | A trailing `gauge` = `RoundsPerPercent.NotReadHere` (`{ratios: [], unavailable: local-capture-deferred}`), always — the mode still never opens `usage.jsonl` (2.1's decision), and no local line carries a reading while 6.1b is deferred. | The derivation is still the one function; its local answer is "not read here". |
| 6.2 | `codex (weekly): 37 % used · resets Thu 07:00 · seen 12 min ago` on the Team server tab and the vendor's model card | As planned (`gaugeWords.ts`): one line per REPORTED window, shortest first; *reset time passed; availability unverified · 37 % used when last read* after a passed reset; *stale — a later read failed*; *reset time not stated*; the vendor's `ordinaryUsageAllowed: false` as its own line. The Team server tab draws a **Vendor gauges** block under Rounds (`teamGauge.ts`) with each window's figure or its reason; **the model card shows the gauge only for a row that RUNS on a Team server** (that server's reading, *· read on <server>*), a local codex row says *codex gauge: unavailable — this machine does not read the gauge; a Team server does* (from `--round-counts`' reason), and **no card shows the figure** — a person's card has no company usage answer to take it from. | The 6.1 consultation's "the SERVER's observation only for an explicitly matched account": running on that server's vendor IS the explicit match; a local account is never guessed to be a server's. |
| 6.2 | a plan price typed once per vendor account "on the Settings page (the vendor's card)" | **Setup › Team servers → *Subscription prices*** — a box and a **Save price** button per vendor of each SIGNED-IN server (`planPricesView.ts`), stored in a new application-scoped `coai.planPrices` (`{"<server>/<vendor>": usd}`); the host decides (`savePlanPrice`) and refuses AT THE CARD (`role="alert"`, the typed text kept) — nothing stored; 0 removes. No local account has a box: no local gauge exists to price. | The only accounts with a gauge are a server's vendors; the new Settings design has no per-account card for them, and Setup › Team servers is where those accounts live. |
| 6.2 | the share "shown only beside a gauge reading" | Beside a SHOWN **weekly** figure only — so never for codex today; never on the model card (no figure there). | "Hidden whenever the ratio is hidden"; a 5-hour window is not the weekly share. |
| 6.2 | the older-client check | `scripts/live-gauge-compat.mjs` also runs the RELEASED client's `fetchUsage(company)` + `serverRounds`: `extension-v0.69.0` (asks no windows → no `gauges`) and `extension-v0.70.0` (asks them → `gauges`, counts still read) both exit 0, 2026-10-12; the control asserts the key rides exactly with the windows asked. | Both released parsers in the field. |
| 6.2 | the real-product scenario through the REAL `JobRunner` with a fake `codex app-server`, stored, served, rendered | **Split:** capture → stored → served is 6.1a's `GaugeCaptureTests` / `GaugeReaderCrashHarnessTests` (the real runner and slot, a fake reader); stored → served → **rendered** is the new seam leg `scripts/seam-gauge.mjs` (the REAL server binary over a planted slot reading and ledger, read by the extension's own client and parser, DRAWN by the tab's own renderer). Catalogued in `module_tests.md` with its run command and limits (`module_tests.md` holds no separate scenario-harness table). | No harness drives the real `JobRunner` AND renders a page in one process; the two legs share the stored reading as their seam. |
| 6.2 (tests) | a reset boundary, a missing reading, an unchanged reading, a missing consumer, a passed reset, the price refusal and 0, the share hidden with the ratio, the page harness, the older-client check | `RoundsPerPercentTests` (18, mcp), `RoundCountsTests` (+1), `RoundsPerPercentServedTests` (4) + `RoundsPerPercentEndpointTests` (1, server), `gaugeWords.test.ts` (9), `planPrice.test.ts` (7), `teamGauge.test.ts` (6), `planPricesView.test.ts` (5), `cardGauge.test.ts` (7), `teamServerTabPage.test.ts` (+2), `http/usage/usage.http` (+3 assertions), the seam's gauge leg, the older-client check; `helpCoverage` and `thePanelReadsThisSide` changed on purpose. Each RED message and break-it in [module_tests.md](../research/module_tests.md). | — |
| E6 (code round, 1) | — | `GaugeAnswer.Ratios` defaults to `[]` and is normalised; an answer built or read without it is no ratios, never null. | The repository's rule for a public list. |
| E6 (code round, 2) | 6.1a's ledger shape: `quota…` on the line, shipped as nine flat fields of ONE limit | **The line carries EVERY window of EVERY limit**: `gaugeObservedUtc`, `gaugePlan`, `gaugeWindows[]` of `{limitId, kind, mins, usedPercent, resetsUtc}` (`GaugeOnLine` / `GaugeWindowOnLine`, `GaugeReadingDto.OnLine`, `UsageEntryDto.GaugeWindows`, `usage.ts` `gaugeWindowsOf`); `RoundsPerPercent` consumes the list. The 6.1a row above describes the first shape and is kept as history. | A codex answer with several limits in `rateLimitsByLimitId` lost all but one on the line, so the derivation could never see them; nothing had shipped, so the shape changed in place rather than gaining a second. |
| E6 (code round, 3) | 6.1a: the launcher records the descendants, then ends the tree | **A descendant that will not record no longer stops the end**: the failure is held, stdin closed, the tree killed and confirmed, and then `ProcessTreeNotRecordedException` is raised; the capture makes the marker **`partial`** (a new `ReaderPhases` value — unidentified) and blocks the slot `reader-identity-missing` (`SlotRegistry.MarkReaderPartial`) — a human recovery, as for any nameless reader. | The finding asked for the slot to stay fail-closed; a marker naming only the root would have reconciled to Cleared once the root was gone, vouching for a descendant nobody recorded. |
| E6 (code round, 4) | — | `RoundsPerPercent` reads each account once (`AccountOf`, a forward-only pointer over the reading-less lines) and shares it across windows: 4 000 lines on one account 11.4 s → 0.26 s, answers byte-identical. No size test (not deterministic); the existing tests and the seam leg are the guard. | O(n²) over the eight-day lookback. |
| E6 (code round, rejected) | — | One gating finding **rejected**: "vendor, account and round cross the runners library as untyped strings". `RoundsPerPercent` is a pure computation over ledger LINES, and each of those identifiers is a field of `UsageEntry`, which the writer, both readers and the extension parser carry as strings — the serialised shape of the line. Typing them only inside this module would split the ledger model and force a conversion at every caller; typed identifiers for `UsageEntry` and its readers are a plan of their own. | Not epic 6's to change. |
| E6 (Linux CI) | 6.1a: reconcile kills the marker's members and confirms THEM gone | Linux CI found two holes the Windows runs could not. **A killed but unreaped process (a zombie) read as alive**: it keeps its `/proc` entry and start time until its parent reaps it — under a parent that never reaps, for ever, so the slot would have stayed unavailable over a process that had exited. `ProcessTree.IsAlive` now reads the state field of `/proc/<pid>/stat` and treats `Z`/`X` as gone. **The confirmation covered only the recorded members**: a member's children started after its record were killed with the tree but never waited for. The reconcile now lists the live descendants of every member that is still the recorded process (`ProcessTree.DescendantsOf`) BEFORE the kill, and confirms the union. Our own review of that fix added two guards: a zombie leader counts as exited only when no other thread of its group runs (`/proc/<pid>/task`), and a descendant must have started no earlier than its parent — a table records a parent NUMBER, and without it a reused number's orphans would be killed as the reader's. Accepted limit: a child spawned between the listing and the kill is ended with the tree but not confirmed. | The production host is Linux; both would have surfaced there, the first as a slot blocked for ever. |

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
