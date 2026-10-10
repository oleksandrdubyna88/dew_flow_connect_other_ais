# RESULTS — where each vendor states its quota gauge, who shares each account, and which paid paths write a ledger line

> Status: **record, 2026-10-10.** Story 1.1 of [PLAN_every_round_is_counted.md](../todo/PLAN_every_round_is_counted.md)
> (epic E1), with the three fixtures of story 1.2 beside it under `src_server/tests/fixtures/usage/`. Read-only on the
> Team server; on this machine one trivial launch per vendor per side was the accepted cost, and a 2 GB copy of the
> codex profile was made, read and deleted. Previous record: [RESULTS_what_spends_the_subscription_2026-10-09.md](RESULTS_what_spends_the_subscription_2026-10-09.md).
>
> Subject: this repository at `3bc85e04`; codex-cli **0.160.0** on Windows (the npm `@openai/codex` package, native
> `codex.exe`) and **0.154.0** in WSL; agy **1.3.3** on Windows and **1.3.2** in WSL (the plan said 1.3.2 — Windows
> had updated); Windows 11 10.0.26300; node 24.18.0 drove the stdio protocol. Everything below is UTC.

## The questions, and what was expected before the runs

The plan's 1.1 asks five things. The expectation held before each run is written first so the observation can be
read against it:

| Question | Expected before the run | Observed |
|---|---|---|
| Does `codex exec --json` carry a rate-limit percentage, window or reset? | No — [RESULTS_fast_mode_measured_2026-10-07.md](RESULTS_fast_mode_measured_2026-10-07.md) found no tier echo in the same stream | **No.** Four event types, usage only (§2.1) |
| Does `codex app-server` have a rate-limit read, and does it answer without a turn? | Yes — the protocol schema the binary generates names `account/rateLimits/read` | **Yes**, in 0.70 s from process start, no thread, no turn (§2.2) |
| Can that read rotate the OAuth refresh token? | Unknown; the load campaign saw two processes rotating one token | **Not while the access token is valid** — `auth.json` byte-identical after three reads. The expired-token case was deliberately not run (§2.3) |
| Does codex echo the effective effort or tier anywhere? | No | **No**, on either transport (§2.5) |
| Does agy state a gauge besides its refusal? | No | **No** — but the refusal is structured JSON on stderr, not only a sentence (§3) |
| Who shares each account? | The operator's three: Windows store, WSL store, the server slot | Those three, verified — **and a fourth the ledgers cannot see**: the operator's own codex use outside coai (§2.4, §4) |

## 1. Method

| # | What ran | Where | Exact invocation |
|---|---|---|---|
| B-win | one real reviewer-shaped launch | Windows, an empty scratch directory | `codex.exe exec --json --skip-git-repo-check -s read-only "Reply with the single word pong."` |
| B-wsl | the same | WSL (its own sign-in: a separate `~/.codex/auth.json`, different hash and mtime from the Windows one) | `codex exec --json --skip-git-repo-check -s read-only "…"` — nothing was deleted in WSL; the launch left an empty `/tmp/coai-gauge-*` directory and two output files that landed on the Windows drive and were removed from there |
| A | the app-server gauge read, isolated from the live profile | Windows | steps 1–4 below |
| A' | a second read, the token-usage one | Windows, the same copy | the same driver, method `account/usage/read` |
| C | one real agy launch, stderr unclipped | Windows, an empty `--add-dir` | `agy --print "Reply with the single word pong." --mode plan --output-format stream-json --add-dir <empty>` |
| D | the paid-path census | the checkout | every `IProcessLauncher` / `HttpClient` site in `src_mcp` and `src_server`, every `spawn` / `capture(` / `fetch(` in `src_vs_code`, each opened and traced to its ledger write (§5) |
| E | the fixtures | the live Team server, read-only, through the operator's credential vault | `grep` the last `ok`, the last `TimedOut` with `seconds` < 10, the last `RateLimited` line of `usage.jsonl`; the email replaced server-side by `sed` before the bytes left the host |

**Procedure A**, as the plan round's finding bound it (the read must never race the live OAuth refresh):

1. The live `%USERPROFILE%\.codex\auth.json`: mtime `2026-10-01T07:44:29Z`, 4 016 bytes, SHA-256 `FE36ACD0…43D1D5`
   (the first eight and the last six hex digits; the full digest is in the session, not here). Top-level keys `auth_mode`,
   `last_refresh`, `OPENAI_API_KEY`, `tokens` — names only, no value was printed.
2. The access token's `exp` claim, decoded from the JWT payload and nothing else: `2026-10-11T07:44:28Z`, 1 037 minutes
   ahead at `14:26:50Z`. More than 60 minutes, so the read was allowed to run. (`last_refresh` was `2026-10-01T07:44:29Z`:
   the access token on this plan lives ten days, and a refresh is a rare event.)
3. The WHOLE `CODEX_HOME` copied to a scratch folder — **33 629 files, 2.07 GB, 642 s**, because `.tmp` (32 330 files),
   `packages` (450 MB) and `sessions` (345 MB) live in it. `codex app-server` was then started with `CODEX_HOME` pointing
   at the copy and driven over stdio with three JSON-RPC lines: `{"id":1,"method":"initialize","params":{"clientInfo":{"name":"coai-gauge-measurement","version":"0.0.0"}}}`,
   the `initialized` notification, then `{"id":2,"method":"account/rateLimits/read","params":{}}`. The method name came
   from `codex app-server generate-json-schema --out <dir>` (the binary's own protocol bundle, 0.160.0), not from memory;
   the server's refusal of a wrong name lists every method it knows, which is how `account/usage/read` was found.
4. The copy's `auth.json` hashed again after each read, and compared with the live one. Unchanged → *no refresh on read*,
   nothing to copy back (§2.3). The copy was deleted at the end, because it holds credentials.

Pinned: the prompt (one sentence), the sandbox (`read-only`), the working directory (empty), `~/.codex/config.toml` left
as it is (`model = "gpt-6.1-sol"`, `model_reasoning_effort = "high"`, `service_tier = "default"` on Windows), no `-m`, no
`-c`. Variable: the side (Windows / WSL) for B; the method for A / A'.

## 2. Codex

### 2.1 `codex exec --json` — the whole stdout of one launch, both sides

Windows (0.160.0, exit 0, 8.6 s wall):

```jsonl
{"type":"thread.started","thread_id":"<uuid>"}
{"type":"turn.started"}
{"type":"item.completed","item":{"id":"item_0","type":"agent_message","text":"pong"}}
{"type":"turn.completed","usage":{"input_tokens":14508,"cached_input_tokens":12544,"cache_write_input_tokens":0,"output_tokens":5,"reasoning_output_tokens":0}}
```

WSL (0.154.0, exit 0): the same four events, usage `input_tokens 15307, cached_input_tokens 12032,
cache_write_input_tokens 0, output_tokens 5, reasoning_output_tokens 0`. stderr on both sides is one line, `Reading
additional input from stdin...` (stdin was not a terminal).

- **No rate-limit percentage, window or reset. No model, effort or tier.** The usage object is the only number a
  launch reports, and `cache_write_input_tokens` is a field coai's `UsageParser` does not read today.
- A one-word prompt costs ~14.5–15.3 k input tokens of which 83–86 % are cached — the system prompt and tool
  declarations; the per-launch floor every reviewer pays before the plan text. Reasoning was 0 on a trivial prompt even
  with the inherited `high`.
- `auth.json` was byte-identical before and after on both sides (Windows `FE36ACD0…`, WSL `a6e6f9c7…`).

### 2.2 `codex app-server` — the gauge, read without spending a turn

The answer to `account/rateLimits/read`, 0.70 s after the process started, with the account id redacted:

```json
{"ordinaryUsageAllowed":true,
 "rateLimits":{"limitId":"codex","limitName":null,"normalModelSlug":null,
   "primary":{"usedPercent":40,"windowDurationMins":10080,"resetsAt":1791964091},
   "secondary":null,
   "credits":{"hasCredits":false,"unlimited":false,"balance":"0"},
   "individualLimit":null,"spendControlReached":false,"planType":"prolite","rateLimitReachedType":null},
 "rateLimitsByLimitId":{"codex":{ …the same object… }},
 "rateLimitResetCredits":{"availableCount":1,"credits":[{"id":"<opaque>","resetType":"codexRateLimits","status":"available",
   "grantedAt":1791417020,"expiresAt":1794009020,"title":"Full reset","description":"Thanks for using Codex! You've been granted one free rate limit reset."}]},
 "accountId":"<redacted>","rateLimitUpsell":null}
```

Read as facts about this account on 2026-10-10 `14:43:42Z`:

| Field | Value | Meaning |
|---|---|---|
| `primary.usedPercent` | **40** | of the one metered window |
| `primary.windowDurationMins` | **10 080** | seven days — the weekly window |
| `primary.resetsAt` | 1791964091 = **2026-10-14T07:48:11Z** | 89.1 h ahead |
| `secondary` | **null** | **no 5-hour window is reported on this plan.** The plan's `quota5hPct` has nothing to read here; E6 renders its absence as absent, never 0 |
| `planType` | `prolite` | also sent as an `account/updated` notification (`authMode: chatgpt`) |
| `credits` | `hasCredits false, unlimited false, balance "0"` | no pay-as-you-go credits |
| `rateLimitReachedType` | null | the enum is `rate_limit_reached` / `workspace_*_credits_depleted` / `workspace_*_usage_limit_reached` when hit |
| `rateLimitResetCredits` | 1 available, `Full reset`, granted 2026-10-07T23:50:20Z, expires 2026-11-06T23:50:20Z | a one-time reset the vendor granted; `account/rateLimitResetCredit/consume` would spend it — **not called** |
| `ordinaryUsageAllowed` | true | the schema says clients must not infer recovery from percentages |

The protocol also carries `account/rateLimits/updated`, a *"sparse rolling rate-limit update"* notification sent to a
connected client during its own turns — so a client that ran its reviewers THROUGH app-server would receive the gauge
per turn without asking. coai runs `codex exec`, which emits none of it (§2.1).

### 2.3 Does the read refresh or rotate the OAuth tokens?

Three reads were made against the copy (`account/rateLimits/read`, a refused `account/tokenUsage/read` — the method
is `account/usage/read` — and `account/usage/read`). After each, the copy's `auth.json` hashed `FE36ACD0…43D1D5` with
mtime `2026-10-01T07:44:29Z`, and the live file the same. **No refresh on read, while the access token is valid.**

What the read DID write into the copied `CODEX_HOME` (files newer than the copy): `logs_2.sqlite` with its `-wal` and
`-shm` (the app-server's own log database), `models_cache.json` (refreshed), and the `-shm` / `-wal` files of
`state_5`, `queue_1`, `memories_1`, `goals_1` (opened, nothing of size written). Never `auth.json`. Also on stderr:
*"WARNING: proceeding, even though we could not create PATH aliases: Refusing to create helper binaries under temporary
dir"* — app-server tries to write helper binaries under `CODEX_HOME` on start and refuses when that is a temp folder.
A reader run inside a Team-server slot's home will therefore write a log database and a models cache there, and may
write helper binaries; a slot's directory is not left untouched by a read.

**Not measured, on purpose:** the read with an EXPIRED access token. The protocol has the machinery for a refresh on
this transport — a server→client request `chatgptAuthTokens/refresh` with reason `unauthorized`, and `account/login/*`
— so whether a read rotates the refresh token when the access token has lapsed is still unknown, and that is the case
the load campaign's sign-out was. Hence the constraint E6 inherits: **the read runs only inside the slot's lease, and
locally only while no `codex` launch of the same store is in flight** (each store has its own sign-in — the Windows,
WSL and server `auth.json` files are three different token sets — so a read in one store cannot rotate another's).

### 2.4 `account/usage/read` — the vendor's own per-day token total for the whole account

```json
{"summary":{"lifetimeTokens":989320742,"peakDailyTokens":143536811,"longestRunningTurnSec":12768,"currentStreakDays":40,"longestStreakDays":40},
 "dailyUsageBuckets":[{"startDate":"2026-08-18","tokens":3415951}, … 41 buckets … ,{"startDate":"2026-10-09","tokens":48452524}],
 "threadUsage":null}
```

The last twelve buckets, in millions of tokens as the vendor counts them (every consumer of the account, every tool):

| 09-28 | 09-29 | 09-30 | **10-01** | **10-02** | 10-03 | 10-04 | 10-05 | 10-06 | 10-07 | 10-08 | 10-09 |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 14.0 | 53.6 | 21.1 | **143.5** | **139.1** | 0.75 | 0.71 | 3.4 | 55.8 | 24.3 | 23.4 | 48.5 |

- This is **account-wide**: it already sums this machine's two stores, the Team-server slot, and anything else signed
  in as this account. It is per calendar day (the vendor's day boundary is not stated), not per quota window.
- Against the previous record: the week of 2026-10-05's local codex tokens (reviews + consultations, Windows + WSL)
  were ≈ 128 M; the vendor's 10-05 → 10-09 buckets sum to 155 M — the same order of magnitude, with the server slot's
  share and the vendor's own weighting of cached input unknown.
- **2026-10-01 and 10-02 carried 283 M tokens between them** — ten times the local ledgers' codex tokens for that week
  (≈ 47 M, previous record §3) — and the window ran out on 10-03 (0.75 M). The ledgers coai writes do not account for
  it. What might (not settled here, each a hypothesis): the Team server's slot (it runs the cheaper models and was
  flat per day in the previous record, so unlikely to be ten times the local figure), the load campaign of early
  October, and **the operator's direct use of codex outside coai** — the local `sessions` folder holds hundreds of
  session files, none of which any coai ledger sees. That fourth consumer is the one 6.2's scope rule must name.
- `peakDailyTokens` = the 10-01 bucket; `currentStreakDays 40` — the vendor counts days of use.

### 2.5 Effort and tier: echoed nowhere

Neither transport reports the effort or the service tier a launch ran with: not in the `exec --json` events (§2.1), not
in the rate-limit or usage reads (§2.2, §2.4 — `normalModelSlug` was null; the usage schema has `reasoningEffort` and
`speed` fields in a per-thread breakdown, `threadUsage`, which was null because no thread was named). The local codex
reviewer rows therefore still run on whatever `~/.codex/config.toml` says — `high` on Windows today — and only story 3.4
(record what was SENT, with its source) can make that visible.

### 2.6 What a gauge reader must not log

The raw stream carries identity: the response's `accountId`, an `account/updated` notification, and a
`remoteControl/status/changed` notification that names **the machine and an installation id**. The reader keeps the
numbers (§2.2's table) and drops the envelope; it never writes a raw message to a log.

## 3. agy (antigravity)

### 3.1 The refusal, whole — the quota was exhausted, so the refusal was the sample

Launch at `2026-10-10T14:30:07Z`, exit 3, 4.6 s wall, usage all zero. stdout (stream-json) was an `init` event (the
tool list, `permission_mode: request-review`), two `step_update` events (`user_input` DONE, `error_message` DONE) and:

```json
{"event":"result","result":{"conversation_id":"<uuid>","status":"ERROR","response":"",
 "error":"Individual quota reached. Please upgrade your subscription to increase your limits. Resets in 88h49m9s.",
 "duration_seconds":0,"num_turns":1,
 "usage":{"input_tokens":0,"output_tokens":0,"thinking_tokens":0,"cache_read_tokens":0,"total_tokens":0}}}
```

stderr, both lines, verbatim but for the id:

```
error: Individual quota reached. Please upgrade your subscription to increase your limits. Resets in 88h49m9s.
AGY_ERROR: {"short_error":"RESOURCE_EXHAUSTED (code 429): Individual quota reached. Please upgrade your subscription to increase your limits. Resets in 88h49m9s.","status":"RESOURCE_EXHAUSTED","error_code":429,"code_kind":"http","retryable":true,"error_id":"<redacted>"}
```

- **The refusal is structured.** `AGY_ERROR:` prefixes one JSON object with `status`, `error_code`, `code_kind` and
  `retryable` — fields a reader takes by NAME. Only the duration (`Resets in 88h49m9s`) is inside the sentence, and it
  is the one thing the plan's 5.3 has to parse.
- `Resets in 88h49m9s` from `14:30:07Z` is **2026-10-14T07:19:16Z** — 29 minutes before codex's weekly reset
  (`07:48:11Z`, §2.2). Both vendors' windows reset within half an hour of each other on the same morning; a
  coincidence of this week, recorded because rounds-per-1 % across a reset will meet both at once.
- The sentence is 107 characters. coai's logs clip a vendor reason at about 100, which is exactly how the previous
  record lost `Resets in 110h12m4…` — story 3.3 keeps reasons whole.
- The refused launch reported zero usage. Whether it counted against anything is not knowable from the client.

### 3.2 Any other gauge?

None found. `agy --help` (1.3.3) lists no status, usage or quota command; `agy models` fetches the model list (18 rows:
Gemini 3.8 / 3.7 / 3.6 Flash at high / medium / low, Gemini 3.1 Pro at high / low, Claude Opus 5.5 and Sonnet 5.5 at
three efforts, GPT-OSS 120B medium) with no quota beside them; agy's state and settings files carry no quota keys
(`terminalSetupPromptShown`, `tipsShown`; `ide`, `security`). So for Gemini the only gauge is the refusal's stated reset,
which is what story 5.3 reads.

### 3.3 Offered to PLAN_a_stated_wait_is_read_rather_than_matched

That plan's gate table wants a verbatim stated-wait sample per vendor. This document OFFERS, without editing that plan:
`agy 1.3.3 · 2026-10-10T14:30:07Z · "Individual quota reached. Please upgrade your subscription to increase your limits.
Resets in 88h49m9s." · structured beside it: status RESOURCE_EXHAUSTED, error_code 429, retryable true · the wait =
88 h 49 m 9 s → 2026-10-14T07:19:16Z`. A second vendor's sample is still owed there; codex states no wait in a sentence
— it states an instant (`resetsAt`) in a field, which is better and different.

## 4. Who shares each account

Stated by the operator on 2026-10-10 (one ChatGPT account and one Google account everywhere; Windows and WSL each
signed in in their own CLIs; the Team server's slots on the same accounts) and verified where a read could:

| Account | Consumer | Verified how | Counted by coai today |
|---|---|---|---|
| ChatGPT | this machine's **Windows** store — `%USERPROFILE%\.codex`, codex-cli 0.160.0 | `auth.json` present, its own token set | reviews, consultations, question rows in the Windows `usage.jsonl`; chat in `chat-usage.jsonl` |
| ChatGPT | this machine's **WSL** store — `~/.codex`, codex-cli 0.154.0 | its own `auth.json` (different hash and mtime from the Windows one) | the WSL store's ledgers |
| ChatGPT | the **Team server's codex slot** — exactly one slot, read-only listing of `accounts/codex/` | one directory, named with a single letter, not an email | the server `usage.jsonl` (one line per terminal job) |
| ChatGPT | **codex used directly** by the operator — the TUI, the app, an IDE, any other tool signed in as this account | the local `sessions` folder (hundreds of files) and the two 140 M-token days of §2.4 that no ledger here holds | **nothing** — and nothing in coai can |
| Google | Windows agy 1.3.3 (`~/.gemini`), WSL agy 1.3.2 | both signed in (the Windows one answered the refusal; WSL's binary and version found) | the local ledgers, as the `gemini` / `antigravity` rows |
| Google | the **Team server's antigravity slot** — one slot | the same listing | the server ledger |
| Google | agy used directly | not verified; the same shape as codex's | nothing |

(The server also has one `claude` slot; Claude is outside this plan.) So the scope rule of 6.2 — *the derived number sums
every consumer recorded here* — has, for codex, three consumers coai can count and **one it cannot**. Either the
rounds-per-1 % figure is hidden with *"codex is also used outside coai on this account"* as the reason, or the plan
decides to show it as an upper bound labelled so. That is the plan owner's call; the measurement only says the fourth
consumer exists and is large.

## 5. Paid-path census — every launch of a vendor process or vendor HTTP request, and whether it writes a ledger line

Method: every `IProcessLauncher` and `HttpClient` use in `src_mcp` (outside `tests/`) and `src_server/src`, every
`spawn(` / `execFile(` / `capture(` / `fetch(` in `src_vs_code/src` (outside `test/`), each opened and traced to its
ledger write. "Billed" means a request the vendor meters against the subscription or the key.

### 5.1 Paths that spend, and what they write

| # | Path | Billed launch | Ledger line | Folds / drops |
|---|---|---|---|---|
| 1 | Review rounds — plan, code, document, feature; the security lane inside the same round. `RoundEngine.cs:401` runs the `work`; `:408-416` records each finished reviewer; the feature stage takes the engine (`FeatureStage.cs:58`); the lane's reviewers are in the same `work` (`SecurityRound.cs:10`, merged at `RoundEngine.cs:437`) | yes, one or more per reviewer | `UsageLedger.Record` — one line per TURN (`UsageLedger.cs:170-191`) | **a repair folds two launches into one line** (`ReviewerExecutor.cs:552-580`, `first.Usage.Add(second.Usage)`); an agy follow-up folds the same way; a retry is a NEW line only because the scheduler re-runs the invocation |
| 2 | Consultations — `ConsultationService.cs:791` (answered), `ConsultationFailedTurns.cs:133,164` (failed) | yes | `ConsultantTurnBooks.Billed` (`:47-48`) → `RecordJob(…, Usage, "consult", "Consultation")`, one line per turn | a continued turn (lookup, follow-up) is one line — agy reports cumulatively, so the line is the turn's total |
| 3 | Consultant / model check — `--check-consultant`, `--check-model` (`consultantCheckRun.ts:60` → `ConsultantCheck.cs:266,281,305`) | yes, one paid turn | `Billed` as role `check`, kind `consult` | a refused preparation writes a line with `Usage.None` — a line for a launch that never ran |
| 4 | Question consultant rows — `QuestionRowLaunch.cs:292-295` | yes | `RecordJob(…, Usage, "question", "Question")`, one line per turn | a continued row folds into the turn's line |
| 5 | `coai-mcp --ask-api` — the api shim, `AskApiMode.cs:155-175` | yes (metered key) | **none of its own**: it prints usage on stdout and whoever LAUNCHED it writes the line — the engine (1), a consultation (2), a question row (4), or the chat (9) | run by hand from a terminal → **nothing is written** |
| 6 | `coai-mcp --ask-remote` — the Team-server shim, `AskRemote.cs:65,159` | yes, on the server's account | **two lines for one launch**: the client's engine writes the remote row's line (provider `<server>-<vendor>`), the server writes its own (10) | the previous record already keeps server rows out of local totals; a counter must too |
| 7 | `coai-mcp --probe-api` with `--model` — `ProbeApiMode.cs:148-172`: `GET /models` then **ten chat completions** (`Variants` `:181-198`, `CompletionAsync` `:228-242`, a review-sized prompt, 2 048 max tokens) | yes, ten per probe | **nothing** — the mode prints a report and has no ledger | the extension's ≡ passes no `--model` (`apiModelsProbe.ts:29`), so from the panel only `GET /models` runs; from a terminal the matrix is paid and invisible |
| 8 | The extension's **Claude model probe** — `claudeProbeCache.ts:181-192` → `claudeProbe.ts:71`: `claude --model <candidate> -p hi --output-format json`, up to four candidates, weekly or on a CLI version change | yes, up to four | **nothing** | the comment at `:66-68` calls it "up to a hundred seconds of BILLED requests" |
| 9 | Chat turns — `chatProcess.ts:49` launches the vendor CLI (claude / codex / agy) or the `--ask-api` / `--ask-local` / `--ask-remote` shim through `processLauncher.ts:149` | yes | `chatTurn.ts:594` → `recordChatTurn` (`chatUsageFile.ts:83`) into `chat-usage.jsonl`, one line per finished turn, failures included | **a turn whose vendor row was deleted mid-turn writes nothing** (`chatTurn.ts:580-582`) |
| 10 | Team server review and chat jobs — `ReviewLauncher.cs:106` (first launch), `:119` (follow-up) → `JobRunner.Record` (`JobRunner.cs:132-178`) → `Ledger` (`:228-256`) | yes | the bare `RecordJob` — tokens only, `costUsd: null`, **no cached / reasoning / usage note** (the live lines of §8 show it) | a follow-up folds into the first launch's line (`ReviewLauncher.cs:162-164`); a `NonZeroExit` is written with its usage **dropped** (`JobRunner.cs:145-146`); a requeued attempt after one slot's `RateLimited` writes **nothing** (`:169-174`) — moot while every vendor has one slot, live the moment a second is added; a job ended by the host stopping (`:113-120`) or by an infrastructure throw (`:121-130`) is finished without a `Ledger` call → **nothing**; a job the client withdrew or the store expired is written `TimedOut` with 0 tokens (§8) |
| 11 | The deploy canary — `deploy/systemd-release.sh:159-171`, one `PlanCritique` review per vendor per release | yes | the server's line, kind `review` | indistinguishable from a person's review except by its token's email |

### 5.2 Paths that launch a vendor process or call a vendor host but spend no turn

Listed so the census is complete and the next reader need not re-derive it: `VendorProbe.cs:144` (`--version`),
`CodexTierSupport.cs:72-76` (`codex --version`), `ClaudeCapability.cs:143-144` (`claude --help`, also from
`ConsultantsReadMode.cs:210,252`), `VendorHealthCache` (`CatalogEndpoints.cs:132` → `--version`), `VendorLogin.cs:193-195`
(`codex login --device-auth`, `<cli> login` — a sign-in, not a turn), `agy models` from `panelProvider.ts:4430` (a model
listing), `--probe-api` without `--model` (`GET /models`), `HealthProbe` (`Startup.cs:139-155`, the server's own
`/api/health`), `RemoteProbe` and `RemoteRuntime.cs:188,266` (the Team server's health and the courtesy `DELETE` on
abandon), `AskRemote`'s polling. Not vendors at all: `--ask-local` (`Program.cs:1775-1777`, a LOCAL engine), `KeyVault`
(the credential CLI), `UploadRun.cs:190` (the bugs server), git everywhere (`ContextAssembler`, `WorktreeManager`,
`GitHistory`, `WorkRangeReader`, `FeatureRefs`, `FilesystemInvariant`, `ConsultCheckScratch`), and in the extension
`versionProbe.ts:62`, `providersProbe.ts:46`, `roundsDbRead.ts:330-373` (coai-mcp read modes), `cliVersions.ts:142` (the
npm registry), `installer.ts:277-303` (GitHub releases), `localEngines.ts:342`, `modelPrices.ts:264` (price tables),
`teamUsageCache.ts:239` (`/api/usage`), `bugsAdminApi.ts:181`, `chatOrphans.ts:289` (a `powershell` kill),
`wslNetwork.ts:344`.

### 5.3 What story 3.3 has to close, from this table

Unwritten paid launches: **7** (`--probe-api` matrix), **8** (the Claude model probe), **5** by hand, **10**'s requeued
attempt, host stop and infrastructure throw, **9**'s deleted-row turn. Written with loss: **10**'s `NonZeroExit`
(usage dropped) and the withdrawal written `TimedOut` 0/0. Folded: **1**'s repair and follow-up, **2** and **4**'s
continued turns, **10**'s follow-up. Doubled: **6**.

## 6. The decision for E6

**Codex has a source, so E6 is not reduced to its record story.** The source is `codex app-server` ·
`account/rateLimits/read` (and `account/usage/read` beside it), never `codex exec --json`. What follows for the plan's
6.1 and 6.2, each a consequence of a measurement above:

1. **Per slot / per store, not per launch.** A read is its own short-lived `codex app-server` process, ~0.7–0.9 s to
   the answer plus start-up, run **inside the slot's lease after a job** on the server and, locally, **while no codex
   launch of the same store runs** (§2.3). It never copies `CODEX_HOME` (the copy here was 642 s and 2 GB — a
   measurement isolation, not a production shape) and it accepts that the read writes a log database and a models cache
   into the home (§2.3).
2. **What to capture:** `primary.usedPercent`, `primary.windowDurationMins`, `primary.resetsAt`, `planType`,
   `rateLimitReachedType`, `rateLimitResetCredits.availableCount` — and `secondary` only when present. On this plan
   there is **no 5-hour window**: `quota5hPct` stays absent and is rendered as absent (coding-style: *absent is not
   zero*). `account/usage/read`'s daily buckets are the account-wide token total the gauge is compared with.
3. **Scope of rounds-per-1 %:** for codex the consumers are the Windows store, the WSL store, the server slot — and
   direct use outside coai, which exists and is large (§2.4) and cannot be counted. Under the plan's own rule the
   figure is hidden with that reason, unless the plan is amended to show it as an upper bound; decided by the plan's
   owner, not here.
4. **Gemini has no gauge.** E6 for Gemini is the reset time read from the last refusal — story 5.3's duration form,
   which §3.1 gives its first verbatim sample — and `AGY_ERROR`'s `retryable` / `error_code` read by name.
5. **The reader logs numbers, never messages** (§2.6).
6. **Still owed before 6.1 ships:** the expired-token behaviour of the read (§2.3) — measured, with a token allowed to
   lapse on a copy, inside the lease discipline — or 6.1 treats a read that triggers a refresh as a reason to stop
   reading until the lease owner's own launch has refreshed.

## 7. What this does not settle, and what was not run

- **Whether the read rotates the refresh token when the access token has expired.** Not run, by the plan round's
  finding; the only safe way to run it is inside a lease with no other process on that token set.
- **Whether `exec --json` ever carries a rate-limit event.** Two launches on two versions carried none; the schema's
  `rate_limit_reached` is a value of the app-server's own `RateLimitReachedType`, not an exec event type. A launch
  that actually hits the limit was not provoked.
- **The 5-hour window** — not reported for this plan type; whether other plans report a `secondary` window is unknown.
- **The vendor's day boundary** for `dailyUsageBuckets`, and how it weighs cached input.
- **What spent 283 M tokens on 2026-10-01 / 10-02** — three hypotheses in §2.4, none tested.
- **agy on a successful turn** — the quota was out, so no gauge could have been observed on success; the refusal was
  the sample. **agy in WSL** was not launched (the Windows refusal is the same account's).
- **`codex doctor`** was not run: it "diagnoses auth" and might touch the token set; the plan asked for the
  app-server read, which was enough.
- The Team server's slot was never touched; its directory names were listed read-only, its ledger read read-only.

## 8. Fixtures — story 1.2

Three live lines, copied read-only from the Team server's `usage.jsonl` (4 094 lines on 2026-10-10; outcomes: 3 976
`ok`, 50 `UnparseableByVendor`, 24 `TimedOut`, 23 `RateLimited`, 19 `NonZeroExit`, 2 `NotStarted`), the email replaced by
`person@example.invalid` before the bytes left the host, under `src_server/tests/fixtures/usage/`:

| File | Line | What it is |
|---|---|---|
| `ok.jsonl` | codex `gpt-5.6-luna`, `PlanCritique`, 31.3 s, 25 170 in / 1 539 out | a success |
| `timed-out-withdrawal.jsonl` | codex `gpt-5.6-luna`, `Architecture`, **8.4 s**, 0 / 0, `TimedOut` | the client withdrew the job; the server wrote the vendor's timeout and a free 0/0 — the defect stories 3.1 / 3.2 fix |
| `rate-limited.jsonl` | antigravity `gemini-3.8-flash-low`, `Architecture`, 3.2 s, 0 / 0, `RateLimited` | the refusal of 2026-10-09 |

Every line carries exactly the keys the deployed server writes — `utc, provider, model, role, stage, seconds, tokensIn,
tokensOut, costUsd, outcome, email, kind` — and none of `tokensCached`, `tokensReasoning`, `usageNote`, `costNote`: the
deployed binary predates them, which is the second half of the previous record's "cached and reasoning are never
written". `UsageLedgerFixtureTests` holds that each line has only the ledger's keys, carries no `@` but the
placeholder's, names no host or address, and reads through the server's own `UsageReader` as one line with zero
unreadable; a planted un-redacted line in the output folder turned four of them red with the real symptoms before it
was removed.
