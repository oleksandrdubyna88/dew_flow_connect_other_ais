# PLAN — the round counter's open tail is closed: the accounting, a second gauge, both stores, the live count

> Status: **plan only, nothing implemented yet (2026-10-20).** Scope: `src_mcp/runners/Reviewers` (the follow-up parts and the
> local ledger line), `src_mcp/core/Consultation` (the one subtraction, reused), `src_mcp/runners/Kimi` + `src_mcp/runners/Acp`
> (a Kimi gauge read under the sign-in's lock), `src_mcp/src/Store/RoundCounts.cs` + `src_mcp/src/Server/RoundCountsMode.cs`
> (a snapshot per store, read by the other side), `src_vs_code/src/roundsBlock.ts` + `roundCountsRead.ts` (both stores, launches
> per round), two live checks against the company Team server. The owner said "do all" of the Open tail on 2026-10-20.
>
> Related docs: [PLAN_every_round_is_counted.md](../research/PLAN_every_round_is_counted.md) (the finished plan whose *Open tail*
> this is), [PLAN_the_reviewer_follow_up_is_billed_once.md](PLAN_the_reviewer_follow_up_is_billed_once.md) (owns item 2's
> measurement — reused, not competed with), [PLAN_a_credential_that_is_not_a_person.md](PLAN_a_credential_that_is_not_a_person.md)
> (why the deploy canary cannot stand in for item 3), [PLAN_vibe_and_kimi_runtimes_E4.md](PLAN_vibe_and_kimi_runtimes_E4.md)
> (the Kimi shim item 1 reads through), [RESULTS_vendor_quota_gauges.md](../research/RESULTS_vendor_quota_gauges.md) (§4 who
> shares each account, §5.1 the paid-path census, §7.1 the first live slot read),
> [RESULTS_kimi_confinement.md](../research/RESULTS_kimi_confinement.md) (§5: what `/usage` answered on 2026-10-12),
> [RESULTS_agy_consult_follow_up.md](../research/RESULTS_agy_consult_follow_up.md) (§3: a consultation follow-up's usage grew,
> which is consistent with a running total and does not prove one), [module_runners.md](../research/module_runners.md),
> [module_server.md](../research/module_server.md), [module_extension.md](../research/module_extension.md),
> [module_team_server.md](../research/module_team_server.md).

## The goal

The round counter shipped and was promoted on 2026-10-19. Its *Open tail* names seven things it did not close. The owner asked
for the seven to be done. This plan is the seven, as six epics and one recorded non-item:

| # | The tail item (the promoted plan's words) | Here |
|---|---|---|
| 1 | *"A second gauge exists, unread"* — Kimi Code's `/usage` shows a 5-hour window and a monthly limit with time to reset; the server reads only codex | E3 measures where the numbers can be read; E4 reads them |
| 2 | *"A follow-up's usage may be cumulative"* — a reviewer asked again in the same agy conversation may report a running total, counting the first launch twice | E1 measures (the other plan's build order step 1); E2 fixes the parts WITH the total |
| 3 | *"Live checks never recorded"* — story 5.4's "+1 exactly" through the Team server, and 4.1's probe against the production URL | E6, with the owner's session token, after the last server change of this plan is deployed |
| 4 | *"launches per round"* of story 2.1 was never built; local lines carry their round only since coai-mcp 0.60.2 (#877) | E5 (merged with item 5: one covered population) |
| 5 | *"The counter reads one store"* — the other OS's rounds are named and not included | E5 |
| 6 | *"Local ledger gaps"* — `endCause` empty locally; effort/tier/`cliVersion` empty on consult, question, probe and chat lines; per-consultation money is per TURN | E2 |
| 7 | *"The codex rounds-per-1 % figure is hidden, by decision"* | **not built** — a recorded non-item, below |

## What the code says today (verified 2026-10-20 on main `20c35f026`)

**The follow-up and the add-up guard (items 2, 6).** `ReviewerExecutor.SecondLaunch`
(`src_mcp/runners/Reviewers/ReviewerExecutor.cs:576-577`) asks the adapter for a follow-up; `AntigravityRuntime.FollowUp`
(`src_mcp/runners/Reviewers/AntigravityRuntime.cs:99-101`) continues the conversation when `AntigravityStream.WasDenied`
(`:271`) matches a literal anywhere in the transcript, while the consultant path reads the facts parsed
(`AntigravityStream.DeniedActions(stdout, stderr)`, `:365-366`). The executor then sums the two launches —
`first.Usage.Add(second.Usage)` at `ReviewerExecutor.cs:692` and `:706` — and writes two parts (`:644-651`): the first launch's
usage and `SecondPart` (`:658-659`), what the SECOND launch reported. `LaunchLines.Of` (`src_mcp/runners/Reviewers/LaunchParts.cs:87-91`)
splits a turn into lines only when the parts add up to the turn's usage (`AddsUpTo`, `:116-121`); otherwise the turn is one line.
So if agy's follow-up report is cumulative, the turn's total is too high by the first launch, and the two lines still add up to that
wrong total — the defect is invisible to the guard. The rule that already handles this exists for consultations:
`ConsultationUsage.OfTwoLaunches` (`src_mcp/core/Consultation/ConsultationUsage.cs:62-63`, the field-wise maximum at `:65-72`)
behind `IConsultantRuntime.UsageIsCumulative` (`src_mcp/runners/Consultation/IConsultantRuntime.cs:195`, default `false`;
`AntigravityConsultant.cs:135` says `true`), called at `ConsultantTurn.cs:143`. `IReviewerRuntime`
(`src_mcp/runners/Reviewers/ReviewerRuntime.cs:324`) has no such member.

**The local ledger line (item 6).** `UsageEntry` carries `Cli`, `CliVersion`, `Effort`, `EffortFrom`, `Tier`, `TierFrom`
(`src_mcp/runners/Reviewers/UsageLedger.cs:175-196`), `EndCause` (`:227`) and `Round` (`:241`). A review line is built by
`Entry` (`:423-445`) from the invocation's `RanWith`, labelled by `RowLaunchOptions.Labelled`
(`src_mcp/src/Server/Rounds/RowLaunchOptions.cs:23-25`, called at `RosterBuilder.cs:474`). A consultation
(`src_mcp/src/Server/Consultation/ConsultantTurnBooks.cs:50-52`), a question row
(`src_mcp/src/Server/QuestionConsult/QuestionRowLaunch.cs:455-458`) and the api probe (`src_mcp/src/Api/ProbeApiMode.cs:286`)
go through the local `RecordJob` overload (`UsageLedger.cs:510-513`), which hands `LaunchOptions.None` — the fields are written
empty. Their invocations are built at `ConsultationService.cs:681` and `ConsultantCheck.cs:249` (`Runtime.Build(…)`), where a
label could be applied. A chat turn is the extension's own record (`src_vs_code/src/chatUsage.ts:38-52`, `ChatTurnRecord`: no
effort, tier or CLI field at all). `EndCause` is empty locally because the round's clock and the caller's token reach the
executor as one linked token (`src_mcp/src/Server/Rounds/RoundEngine.cs:288-289`), and the launcher records only
`Cancelled: timedOut && callerCancelled` (`src_mcp/runners/Processes/ProcessLauncher.cs:570`); the executor reads it at
`ReviewerExecutor.cs:984-988`. The vocabulary is `EndCauses` (`src_mcp/runners/Reviewers/EndCauses.cs:20-44`): `withdrawn`,
`abandoned`, `backstop`, `queueExpired`, `hostStopped` — every one a server word.

**Kimi's gauge (item 1).** The shim sends `/usage` as a second prompt on the same session after an answered turn
(`src_mcp/runners/Acp/AcpClient.cs:34`, `:189-203`) and reads one line, `Session total: N input, M output`
(`src_mcp/runners/Acp/AcpUsageLine.cs:12-13`). The 2026-10-12 sample (RESULTS_kimi_confinement §5) shows `Context: …` and
`Session total: …` and **no plan-usage line**; the owner saw *Plan usage* with the 5-hour and monthly windows on 2026-10-19 in
Kimi's own `/usage`. Whether the ACP reply carries it (a newer Kimi, or only the TUI) is unmeasured — E3's first question.
Kimi launches one at a time (`KimiRuntime.cs:107-108`); the refresh token rotates, so the sign-in has two locks
(`src_mcp/runners/Kimi/KimiCredentialsLock.cs:43-52`), a foreign-`kimi` watch (`ForeignKimi.cs:21-45`) and the shim's hold
(`KimiShimHold.cs:21-28`). The server has no Kimi slot (`src_server/src` contains no Kimi runtime), so a Kimi reader is LOCAL —
exactly where story 6.1b deferred the codex reader because "a lock excludes only cooperating coai processes". Kimi is different
in one measured way: its launches ALREADY run under a cross-process lock and a foreign-process check, because a race costs the
sign-in. A rate limit is classified from the report's `balance_spent` field (`KimiRuntime.cs:269-280`, `KimiAcpProfile.cs:50`).

**The codex reader to reuse (item 1).** `GaugeCapture` (`src_server/src/Slots/GaugeCapture.cs:30-38` the order, `:147-159` the
guards, `:177-200` marker → tracker → survivors → clear) and its codes (`GaugeReading.cs:11-48`); the reading shape
`GaugeWindowDto` (`:56`: limit id, kind, used %, duration, reset) and the line shape `GaugeOnLine`
(`src_mcp/runners/Reviewers/GaugeOnLine.cs:29`, columns by `UsageEntry.WithGauge`, `UsageLedger.cs:303`). The derivation hides
a figure for a known uncounted consumer — `GaugeConsumers.UncountedFor` names only `direct-codex-use`, only for codex
(`src_mcp/runners/Reviewers/RoundsPerPercent.cs:73-89`, `Hidden` at `:417-421`). The extension already words a 43 200-minute window
as *monthly* (`src_vs_code/src/gaugeWords.ts:36`) and a local row's missing gauge from `gaugeUnavailable` (`cardGauge.ts:50`).

**One store (items 4, 5).** `--round-counts` (`src_mcp/src/Server/RoundCountsMode.cs:39-52`) answers `RoundCountsAnswer`
(`src_mcp/src/Store/RoundCounts.cs:24-31`: windows → rows → `provider`, rounds by stage, `reviewers`, `models`) from `coai.db`
(`Read`, `:113-125`) and never opens `usage.jsonl`. The extension spawns it on its own side (`src_vs_code/src/roundCountsRead.ts:28-30`),
reads its own ledger tail beside it (`roundsLoader`, `:39-54`, `besidePaths` for ledgers of the same shape), and names the other
installations' stores by file existence only (`otherStores`, `:60-62`; `panelProvider.ts:701`; the sentence `roundsBlock.ts:110-115`).
The directories come from `coai.alsoWatchDataDirectories`, which holds *paths on this machine* (`configTransfer.ts:34`) — a WSL
store is spelled `\\wsl.localhost\…` from Windows and a Windows one `/mnt/c/…` from WSL. A round is written by
`RoundsDb.RecordRound` (`src_mcp/src/Store/RoundsDb.cs:190`, called at `RoundEngine.cs:620`). The block's footnote
(`roundsBlock.ts:121-126`) already says a review line is one launch since coai-mcp 0.48.0; nothing counts launches per round.
No Windows→WSL launch exists (it is the deferred slice of
[PLAN_a_wsl_consultant_is_checked_from_windows.md](PLAN_a_wsl_consultant_is_checked_from_windows.md)), and WAL across the OS
boundary is what story 2.2 refused. `AtomicFile` (`src_mcp/runners/Files/AtomicFile.cs:25`) is the atomic write to reuse.

**The server count (item 3).** `UsageRounds.Of` (`src_server/src/Usage/UsageRounds.cs:39-53`) counts distinct non-empty round
ids over `review` lines only — a `canary` line is in neither `rounds` nor `runsWithoutRound`, which is why the deploy canary
(`deploy/systemd-release.sh:170`, `"kind":"canary"`) and POST_DEPLOY item 11's by-hand review cannot stand in for story 5.4. The
Team server tab renders `rounds` per vendor and window (`src_vs_code/src/teamRounds.ts:54`). The zero-spend probe is
`src_vs_code/src/test/reviewBody.contract.ts` (`COAI_CONTRACT_URL`, `COAI_CONTRACT_KEY`, `:37-38`), run through
`scripts/run-contract.mjs`, which refuses a non-local URL without `COAI_CONTRACT_ALLOW_REMOTE=1` (`:25`).

**Where the code contradicts the consultant's summary** (recorded so nobody builds on the summary): (a) `LaunchLines.Of` is at
`LaunchParts.cs:87-91`, not "~57-91" — `:57` is the class; (b) the codex reader's lease, marker and containment live in
`src_server`, which has no Kimi runtime, so the Kimi reader cannot CALL `GaugeCapture` — it reuses its order, its codes and
`GaugeOnLine`, under the Kimi sign-in's own locks instead of a slot lease; (c) the ACP `/usage` reply measured on 2026-10-12
carries no plan-usage line, so "read Kimi's gauge like the codex one" is a question before it is a story.

## Boundaries with the open plans (both sides)

| Item | Which plan builds it | The other one's part |
|---|---|---|
| The MEASUREMENT of a reviewer follow-up's usage (one denied launch and its follow-up, both `usage` blocks) | [PLAN_the_reviewer_follow_up_is_billed_once.md](PLAN_the_reviewer_follow_up_is_billed_once.md), its build order step 1 — this plan's E1 RUNS it and records the result in that plan | this plan adds nothing to the measurement |
| The reviewer's turn total under a cumulative follow-up (`OfTwoLaunches` behind a reviewer-side `UsageIsCumulative`) | that plan's step 2 — **built as this plan's E2.S1**, because the total and its launch parts must change in one commit | that plan's boundary table already says the parts move with the sum; its DoD box "fixed with a test that was observed red" is ticked by E2.S1's commit |
| The second part's usage = the difference (cumulative) — so `LaunchLines.Of` still splits | **this plan** (E2.S1) | — |
| The reviewer's denial read parsed (`DeniedActions`) instead of matched (`WasDenied`) | that plan's *second defect* — **built as E2.S2** here, on the same path and in the same commit series | — |
| Kimi's spent 5-hour window told from a lost sign-in (exit 77 after a run of launches; M-K14) | `todo/PLAN_a_spent_kimi_quota_is_not_a_lost_sign_in.md` (another session, branch not yet on main) — classification of a REPORT | this plan's E3/E4 READ a gauge; they never classify a failure. E4 waits for that plan to land and then names the boundary in its stories document; a spent window that plan recognises is shown by E4 as *100 %, resets in …* |
| What the deploy canary authenticates with | [PLAN_a_credential_that_is_not_a_person.md](PLAN_a_credential_that_is_not_a_person.md) | E6 uses the OWNER's session token for one real round; a canary line never counts as a round, so that plan is not waited for |
| The Kimi shim's `/usage` follow-up and its line parser | [PLAN_vibe_and_kimi_runtimes_E4.md](PLAN_vibe_and_kimi_runtimes_E4.md) E4.3 (shipped) | E4 here widens the parser to a second block and never changes the first line's reading |

Order: E1 first (free to start, nothing waits); E2 after E1's number; E3 after `PLAN_a_spent_kimi_quota…` lands or is
declined (its window measurement must not share a Kimi window with this one); E4 after E3; E5 any time after E2 (it reads
`endCause`/`cliVersion` only as columns); E6 last, after the last server deploy. **Owed to the other side:** the row above
that says E2.S1 builds step 2 of the follow-up plan is written INTO that plan's boundary table by E1's first commit (this plan was
written without editing it).

## Item 7 — the codex rounds-per-1 % figure: a decided non-item

The owner chose *keep hidden* (story 6.2b): with direct codex use uncounted the measured ratio is a LOWER bound on rounds per
percent, so "≤ N" would be wrong and "≥ N" is not what a person asks. Nothing here builds it. It opens when direct use can be
counted or the owner chooses a bound. **To confirm with the owner before E4 starts** (the Kimi ratio obeys the same rule).

## What is measured first, and what it may spend

Every measurement runs in a window with no other process of that vendor (Kimi's refresh token rotates — a second `kimi`
signs the owner out; agy shares the Google account with the Team server's slot). Each is recorded as a `research/RESULTS_*.md`
row with the raw blocks redacted (no email, no token).

| Measurement | Epic | Prediction written first | Budget (the owner's subscription) |
|---|---|---|---|
| M1 — a denied agy reviewer launch and its follow-up, both `usage` blocks, 3 runs | E1 | the follow-up's input ≥ the first's and grows: cumulative | ≤ 6 agy launches (3 denied + 3 follow-ups), ~60 k input each → ≤ 400 k Google tokens; stop after the first run if the 5-hour window is above 80 % |
| M2 — where Kimi's plan usage can be read: (a) the shim's `/usage` after an answered turn on Kimi 2.1.x; (b) `/usage` as the ONLY prompt (no model call, 3 runs); (c) Kimi's session log; (d) the TUI's wording, for the parser | E3 | (b) carries *Plan usage* with both windows and no model call — the free source | (b) is free; (a) ≤ 3 one-word turns, ~20 k input each (the 8 571-character system prompt) → ≤ 70 k Kimi tokens; the whole of M2 inside ONE 5-hour window, no other `kimi` running |
| M3 — one plan round through the company Team server, `/api/usage?windows=5h,24h,week&scope=company` read before and after; the zero-spend probe against the production URL | E6 | each vendor's `rounds` +1 exactly, `runsWithoutRound` unchanged; the probe answers the kind refusal | one plan round on a ~2 KB plan: ≤ 3 launches, one per vendor, ≤ 10 k input each; the probe spends nothing |

## Build order

1. **E1** — M1, recorded in the follow-up plan; the boundary row written into it. No product code.
2. **E2** — the accounting: E2.S1 (cornerstone) the cumulative follow-up billed once with the parts still adding up; E2.S2 the
   parsed denial; E2.S3 labels on consult/question/probe lines; E2.S4 a local `endCause` captured where the cancellation
   originates; E2.S5 the chat line's `cli`/`cliVersion`; E2.S6 per-consultation money per CONSULTATION. One coai-mcp release.
3. **E3** — M2, in a Kimi window of its own, after the spent-quota plan lands or is declined.
4. **E4** — the Kimi gauge: E4.S1 the parser; E4.S2 (cornerstone) the read under the sign-in's locks, never outside them;
   E4.S3 stored and answered by `--round-counts`, shown on the card and the Rounds block, the ratio hidden with the consumer
   named. One coai-mcp + one extension release.
5. **E5** — both stores and launches per round: E5.S1 (cornerstone) the per-store snapshot and its merge with provenance; E5.S2
   the other side's ledger tail beside this side's; E5.S3 launches per round over the covered population; E5.S4 the block and the
   card. One coai-mcp + one extension release.
6. **E6** — M3 after the last server deploy this plan causes (there is none planned: E2–E5 touch `src_mcp` and `src_vs_code`; if
   a server change appears, E6 runs after ITS deploy).

## Risk

- **A wrong accounting change moves every spending figure** (E2.S1). Held by: the add-up guard stays; the cumulative difference
  is floored at zero through `ConsultationUsage.Less`, never a new subtraction; a non-cumulative runtime is byte-identical
  (a golden ledger over the fixtures of `EveryPaidLaunchIsWrittenTests`).
- **A second Kimi process signs the owner out** (E3, E4). Held by: the read runs INSIDE `KimiShimHold` (after the foreign watch),
  never from the extension, never on a timer; M2's cells never overlap another Kimi plan's window; on any auth error the read
  stops and records `auth-refused`, as the codex reader does.
- **The other side's snapshot is stale or half-written** (E5). Held by: `AtomicFile`; the snapshot carries `writtenUtc` and the
  raw rounds of the last 7 d (not pre-cut windows), so the reader cuts the windows with its own clock; a snapshot older than 7 d,
  missing or unreadable makes that store *not included: <reason>* — never 0 and never silently merged.
- **A number from two stores that are one account read as two accounts** (E5). Held by: rows are merged by vendor row NAME with
  each store's part shown, and the sentence says the stores are counted together — nothing claims they are one account or two.
- **The live round spends the company's accounts** (E6). Held by: M3's budget; the probe first; the owner present.

## Test plan (RED first, the symptom named)

| Test (new unless said) | RED symptom before the fix |
|---|---|
| `ACumulativeFollowUp_IsBilledOnce_AndItsPartsStillAddUp` (mcp) — two cumulative reports 100 → 140: the turn is 140, the lines are 100 + 40 | the turn is 240 and the lines 100 + 140 |
| `ANonCumulativeFollowUp_IsUnchanged` — codex-shaped runtime, same fixture | — (must be green before and after; a plant that flips the flag goes red) |
| `ASecondLaunchKilledOnItsDeadline_KeepsTheFirstLaunchsReport` — second launch `Usage.Unknown`, cumulative | the turn is `Unknown`/0, the first launch lost |
| `AReviewerThatGrepsForAutoDenied_IsNotAskedAgain` — the word on stdout, no denial on stderr | a follow-up is launched |
| `AConsultationLine_SaysWhatItRanWith`, `AQuestionRowLine_…`, `AProbeLine_…` — `cli`, `effort`/`effortFrom`, `tier`/`tierFrom` as sent | every field empty |
| `ALocalWithdrawal_CarriesItsCause` — the MCP client's cancel → `endCause: withdrawn`; the round clock → `roundClock` (a new local word in `EndCauses`) | `endCause` empty on both |
| `chatUsage.test.ts`: a chat line carries `cli` and `cliVersion`; an old line reads both empty | the record has no such field |
| `usagePricing.test.ts`: per-consultation money divides by distinct consultations (lines grouped by their `conversation`/turn key), not by lines | divides by lines |
| `KimiPlanUsageTests` — the parser over M2's verbatim text: two windows (`5h`, 43 200 min), used %, reset instant; `no LLM calls yet` and a missing block → no reading, never 0 | — (new parser) |
| `AKimiGaugeIsReadUnderTheHold` — the read happens after the hold is taken and before it is given back; a foreign `kimi` → no read, reason recorded; an auth error → suspended code, previous reading stale | a read outside the hold |
| `RoundCountsTests` (+): the mode writes `round-counts-snapshot.json` after answering; the snapshot's rounds equal the answer's over 7 d | no file |
| `TwoStoresAreCountedTogether` (ts): a Windows answer + a WSL snapshot → one row per vendor with both parts; a snapshot older than 7 d / missing / unparseable → that store *not included* with its reason, the other store's count unchanged | the other store is named and not counted |
| `LaunchesPerRound_CountsOnlyRoundsWithIds` (ts): lines with `round` → launches ÷ distinct rounds; rounds without ids reported apart as *N rounds before 0.60.2 carry no launch count* | — (new figure) |
| `the seam leg` (existing `scripts/seam-usage-counts.mjs`): unchanged, run before E6 | — |
| `RESULTS_the_round_counter_tail.md`: M1, M2, M3 rows with before/after, versions, round ids | — |

Each RED message and its plant go into `research/module_tests.md` with the story that lands it.

## Definition of Done

- [ ] M1 recorded in `PLAN_the_reviewer_follow_up_is_billed_once.md` with both `usage` blocks; its boundary table names E2.S1.
- [ ] A cumulative reviewer follow-up is billed once, its two lines add up to the turn, non-cumulative runtimes unchanged; the
      reviewer's denial is read parsed.
- [ ] Consultation, question-row, probe and chat lines say what they ran with; a local cancellation carries its cause; the
      Rounds block's per-consultation money is per consultation and says so.
- [ ] M2 recorded; Kimi's 5-hour and monthly windows are read under the sign-in's locks only, written on the line, answered by
      `--round-counts`, shown with their reset, the ratio hidden with `direct-kimi-use` named.
- [ ] Both stores are counted, each named with its part and its freshness; a missing or stale store is *not included* with
      the reason, never 0; launches per round is shown over the rounds that carry ids, the rest reported apart.
- [ ] M3 recorded: one round → `rounds` +1 per vendor exactly, and the probe against the production URL; `POST_DEPLOY.md` stamped.
- [ ] Item 7 confirmed with the owner as not built; `research/PLAN_every_round_is_counted.md`'s *Open tail* points here.
- [ ] `module_runners.md`, `module_server.md`, `module_extension.md` updated per epic; help text in five languages where a
      person reads a new word; `node .agents/conventions/tools/plan-lifecycle.mjs` clean.

## Epics

Six epics, each on its own branch from main, one pull request each, the coai gate once per epic (plan round with the epic
declared, code round over the whole diff). Stories documents (`PLAN_the_round_counter_tail_E<n>.md`) are written when each epic
starts. **Cornerstones: 0 of 3 — E2.S1, E4.S2, E5.S1** (six epics → three; specs by Fable, the rest Opus; measurements Fable).

### E1 — measure the agy reviewer follow-up (no product code)

- **Goal:** settle item 2's question with numbers before anything is built.
- **Stories:** S1 M1 — one denied agy reviewer launch and its follow-up on the real CLI, 3 runs, both `usage` blocks redacted
  into `RESULTS_the_round_counter_tail.md`; S2 the result written into the follow-up plan (its step 1 ticked, step 2 or 3 chosen)
  and the boundary row owed to that plan written in.
- **Depends on:** a Google window with the Team server's agy slot idle (ask the owner for the hour). **Budget:** M1's.
- **Done when:** the follow-up plan says *measured: cumulative* or *refuted and closed*. Refuted → E2.S1 is cut and E2 keeps
  S2–S6.

### E2 — the accounting is right on every local line

- **Goal:** the ledger's lines and totals say what was spent, on the follow-up path and on the lines story 3.4 left unlabelled.
- **S1 (CORNERSTONE) — a cumulative follow-up billed once, parts included.** `IReviewerRuntime` gains `UsageIsCumulative`
  (default `false`, `AntigravityRuntime` `true`, the consultant's word reused); `ConcludedAsync` uses
  `ConsultationUsage.OfTwoLaunches(cumulative, first, second)` for the turn and `ConsultationUsage.Less(second, Of(first))` for
  the second PART, in one expression, so `LaunchLines.AddsUpTo` holds by construction; a second launch that reports nothing
  keeps the first's (the maximum). The ledger's `attempt: followUp` line carries the difference and `usageNote` says
  *difference of a cumulative report*. Spec by Fable: the edge table (second < first, second `Unknown`, money on one side only,
  the security answer limit after a follow-up).
- **S2 — the denial read parsed.** `AntigravityRuntime.FollowUp` decides on `DeniedActions(stdout, stderr)`; `WasDenied` stays
  for nothing but its tests, then goes.
- **S3 — consult, question and probe lines say what they ran with.** `RowLaunchOptions.Labelled` (or its `Of`) applied where
  the consultation (`ConsultationService.cs:681`), the check (`ConsultantCheck.cs:249`), the question row and the api probe build
  their invocations; the local `RecordJob` overload gains a `LaunchOptions` (the server overload's shape, reuse-first).
  `cliVersion` only where a version was already asked (the brief's rule: no spawn per launch).
- **S4 — a local end cause, captured where it originates.** The round engine tells its own clock from the caller's token
  (`RoundEngine.cs:288-289`): a new local word `roundClock` in `EndCauses` beside `withdrawn` for the MCP client's cancel; the
  executor's `Cancelled` outcome carries the cause; `UsageLedger.Entry` writes it. Never inferred from a sentence.
- **S5 — the chat line's `cli` and `cliVersion`** on `ChatTurnRecord` (the launching side knows the CLI; the version from the
  catalog probe's last answer, never a spawn); effort/tier stay empty on chat lines and the Rounds block says *not recorded*.
- **S6 — per-consultation money per consultation.** The block groups consult lines by consultation (the turn key / the
  conversation id the line carries) before dividing; the label changes from *per consultation turn* to *per consultation*, help
  in five languages.
- **Depends on:** E1. **Done when:** the test table's first eight rows are green from red; one coai-mcp release.

### E3 — measure where Kimi states its plan usage (no product code)

- **Goal:** item 1's source, measured, before a reader is written — the ACP reply of 2026-10-12 did not carry it.
- **Stories:** S1 M2 cells (a)–(d) with predictions first, in ONE 5-hour window with no other `kimi`; S2 the verbatim `/usage`
  text (redacted) as a fixture under `src_mcp/tests/fixtures/kimi/`; S3 the decision for E4 — free source (b) or a paid turn —
  and the window semantics recorded (a monthly window has its own reset; a 5-hour window at 100 % and its 403 wording).
- **Depends on:** `todo/PLAN_a_spent_kimi_quota_is_not_a_lost_sign_in.md` landed or declined (same vendor, same window). **Budget:** M2's.
- **Done when:** `RESULTS_the_round_counter_tail.md` §M2 says which cell carries both windows, or that none does — then E4 is cut
  and item 1 recorded as *checked: the shim's reply, a usage-only prompt, the session log; the TUI only* with the owner told.

### E4 — the Kimi gauge is read, under the sign-in's locks

- **Goal:** Kimi's 5-hour and monthly windows, with their resets, beside the Kimi row — the codex reader's rules, locally.
- **S1 — the parser.** `KimiPlanUsage.Parse(text)` → `GaugeOnLine` windows (`limitId: kimi`, `mins: 300` and `43200`, used %,
  reset instant from "resets in …" through `CooldownParser.StatedReset`, never a guess); `AcpUsageLine` untouched.
- **S2 (CORNERSTONE) — the read, contained.** The `/usage` prompt (source per E3) runs INSIDE `KimiShimHold` after the turn, on
  the same session, after `ForeignKimiWatch` — never from the extension, never on a timer, never while a foreign `kimi` runs;
  an auth error or a refresh request stops it with `auth-refused`/`refresh-requested` (the `GaugeCodes` words reused as
  constants moved to the runners library), the previous reading marked stale, the launch's own result unchanged; one line in
  the log, numbers and codes only. Spec by Fable: the order, the no-read cases, the break-its (a plant that reads outside the
  hold, a plant that reads on a foreign `kimi`).
- **S3 — stored, answered, shown.** The reading on the Kimi line (`WithGauge`, the local `Entry` gains the parameter) and in
  one overwritten `kimi-gauge.json` in the data directory (`AtomicFile`); `--round-counts` answers `gaugeReadings[]` (vendor,
  windows, observed, stale) beside the `gauge` figure — the figure stays hidden: `GaugeConsumers.UncountedFor` gains
  `direct-kimi-use` (the owner runs Kimi's TUI; RESULTS §4's rule), so `consumer-not-counted` names it; the card and the Rounds
  block show *kimi: 5 h 100 % · resets in 1 h · monthly 39 % · resets in 24 d · read <when>* through `gaugeWords.ts`, help in five
  languages. The derivation over local lines is NOT built (the mode never opens the ledger, 2.1's decision; the ratio is hidden).
- **Depends on:** E3; item 7's confirmation. **Done when:** a Kimi round on this machine shows both windows with their resets, a
  foreign `kimi` leaves the gauge unread with the reason, and `KimiPlanUsageTests` holds the fixture.

### E5 — both stores are counted, and launches per round

- **Goal:** the Rounds block counts the Windows and the WSL store together, each named, and says launches per round.
- **S1 (CORNERSTONE) — the snapshot and the merge.** The owning OS's coai-mcp writes `round-counts-snapshot.json` beside its
  `coai.db` — after `RecordRound` and after every `--round-counts` answer — holding `writtenUtc`, the store's side and directory,
  and the RAW rounds of the last 7 d (start, provider, stage, model, `reviewers`), through `AtomicFile`. `--round-counts` gains
  `--also <snapshot-path>` (repeatable): it reads the other store's plain JSON file (never its WAL database), cuts the windows
  with the one `RoundCounts.Cut` at the same `now`, and answers rows per vendor WITH per-store parts (`stores[]`: side,
  directory, `asOf`, rounds) and `storesNotIncluded[]` with a reason (missing, unreadable, older than 7 d, from a newer
  schema). Partial is never zero; nothing infers an account from a matching vendor name. Spec by Fable: the file's growth
  (bounded: 7 d of rounds, overwritten), its interruption (atomic), the reasons, the version handshake.
- **S2 — the other side's ledger tail beside this side's.** `roundsLoader` reads `<other store>/usage.jsonl` from its end as it
  reads its own (plain append-only JSONL, not WAL), each tail keeping its store, so tokens and money carry provenance too; a
  tail that will not read is *not included* like the snapshot.
- **S3 — launches per round.** Over the covered population: review lines carrying a `round` (coai-mcp ≥ 0.60.2, both stores)
  → launches ÷ distinct round ids, per vendor row and window; rounds with no line carrying an id are reported apart
  (*N rounds carry no launch count — written before 0.60.2*); the C#→TS contract leg (`roundCounts.ts` parser) the #877
  record said belongs to this first consumer.
- **S4 — the block and the card.** The store sentence becomes *Counted from two stores — Windows (this machine) and WSL
  (<distro>, as of 3 min ago)*; `storesNotIncluded` keeps today's sentence with the reason; `cardRoundsLine` sums both;
  help in five languages. The bundled-page test runs the block with two stores and with one missing.
- **Depends on:** E2 (columns only). **Done when:** a Windows window shows the WSL store's rounds with their age, and the
  reverse from a WSL remote window; one coai-mcp + one extension release.

### E6 — the live checks, recorded (no product code)

- **Goal:** story 5.4's "+1 exactly" and 4.1's production probe observed and written down.
- **Stories:** S1 the owner's session token in `COAI_SESSION_TOKEN` for one hour (the canary's token is a person's and expires;
  it is NOT used); S2 the zero-spend probe against `https://coai.remsoft.dev` (`COAI_CONTRACT_URL`, `COAI_CONTRACT_ALLOW_REMOTE=1`);
  S3 M3 — `/api/usage?windows=5h,24h,week&scope=company` before, one plan round from this machine's extension on a ~2 KB plan,
  the same read after: per vendor `rounds` +1, `runsWithoutRound` unchanged, the round id on every line; S4 the record —
  deployed server version from `/api/health`, extension and coai-mcp versions, vendor, round id, before/after — in
  `RESULTS_the_round_counter_tail.md` and a `POST_DEPLOY.md` stamp; the promoted plan's *Open tail* updated.
- **Depends on:** the owner's token and presence; the last server deploy this plan causes (none planned). **Budget:** M3's.
- **Done when:** both observations are recorded with their numbers, or a mismatch is recorded as a defect with its own plan.
