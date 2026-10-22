# PLAN — the round counter's open tail is closed: the accounting, a second gauge, both stores, the live count

> Status: **in progress (2026-10-22): E3 built and gated (PR #904); E1 and E4 measured and E6 built on their own branches; E2
> deferred by the owner; E5 cut provisionally; E7 not started (plan of 2026-10-20, revised after the coai plan gate, an Opus review and
> the epics 1–3 cadence consultation).** Scope: `src_mcp/runners/Reviewers` (the follow-up parts and the local ledger line),
> `src_mcp/core/Consultation` (the one subtraction, reused), `src_server/src/Jobs/ReviewLauncher.cs` (the server's own follow-up
> path, the same rule), `src_mcp/runners/Kimi` + `src_mcp/runners/Acp` (a Kimi gauge read under the sign-in's lock),
> `src_mcp/src/Store/RoundCounts.cs` + `src_mcp/src/Server/RoundCountsMode.cs` (a snapshot per store, read by the other side
> through a NEW one-shot mode), `src_vs_code/src/roundsBlock.ts` + `roundCountsRead.ts` + `roundMoney.ts` (both stores, launches
> per round, the follow-up line's discriminator), two live checks against the company Team server. The owner said "do all" of
> the Open tail on 2026-10-20.
>
> Related docs: [PLAN_every_round_is_counted.md](../research/PLAN_every_round_is_counted.md) (the finished plan whose *Open tail*
> this is), [PLAN_the_reviewer_follow_up_is_billed_once.md](PLAN_the_reviewer_follow_up_is_billed_once.md) (owns item 2's
> measurement — reused, not competed with), [PLAN_a_credential_that_is_not_a_person.md](PLAN_a_credential_that_is_not_a_person.md)
> (why the deploy canary cannot stand in for item 3), [PLAN_vibe_and_kimi_runtimes_E4.md](PLAN_vibe_and_kimi_runtimes_E4.md)
> (the Kimi shim item 1 reads through), [PLAN_a_stated_wait_is_read_rather_than_matched.md](PLAN_a_stated_wait_is_read_rather_than_matched.md)
> (owns agy's "exhausted" line), [RESULTS_vendor_quota_gauges.md](../research/RESULTS_vendor_quota_gauges.md) (§4 who shares each
> account, §5.1 the paid-path census, §7.1 the first live slot read), [RESULTS_kimi_confinement.md](../research/RESULTS_kimi_confinement.md)
> (§5 what `/usage` answered on 2026-10-12; §22 M-K7b; §23 M-K5b), [RESULTS_agy_consult_follow_up.md](../research/RESULTS_agy_consult_follow_up.md)
> (§3: a consultation follow-up's usage grew — *"not settled"* in its own words; it proves nothing about cumulative reporting and is
> cited here only for how a denial was elicited), [RESULTS_agy_write_block.md](../research/RESULTS_agy_write_block.md) (the
> read-only agent and hook every agy launch runs under), [module_runners.md](../research/module_runners.md),
> [module_server.md](../research/module_server.md), [module_extension.md](../research/module_extension.md),
> [module_team_server.md](../research/module_team_server.md).

## The goal

The round counter shipped and was promoted on 2026-10-19. Its *Open tail* names what it did not close. The owner asked for all of
it. This plan is seven epics and the recorded non-items; the table *Every open-tail bullet* below maps every bullet of that tail.

| # | The tail item (the promoted plan's words) | Here |
|---|---|---|
| 1 | *"A second gauge exists, unread"* — Kimi Code's `/usage` shows a 5-hour window and a monthly limit with time to reset; the server reads only codex | E4 measures where the numbers can be read; E5 reads them |
| 2 | *"A follow-up's usage may be cumulative"* — a reviewer asked again in the same agy conversation may report a running total, counting the first launch twice | E1 measures (the other plan's build order step 1); E2 fixes the parts WITH the total, locally AND on the Team server |
| 3 | *"Live checks never recorded"* — story 5.4's "+1 exactly" through the Team server, and 4.1's probe against the production URL | E7, with the owner's session token, after E2's server deploy |
| 4 | *"launches per round"* of story 2.1 was never built; local lines carry their round only since coai-mcp 0.60.2 (#877) | E6 (merged with item 5: one covered population) |
| 5 | *"The counter reads one store"* — the other OS's rounds are named and not included | E6 |
| 6 | *"Local ledger gaps"* — `endCause` empty locally; effort/tier/`cliVersion` empty on consult, question, probe and chat lines; per-consultation money is per TURN | E3 |
| 7 | *"The codex rounds-per-1 % figure is hidden, by decision"* | **not built** — a recorded non-item, below |

## What the code says today (verified 2026-10-20 on origin/main `20c35f026`; re-verified after the reviews)

**The follow-up and the add-up guard (items 2, 6).** `ReviewerExecutor.SecondLaunch`
(`src_mcp/runners/Reviewers/ReviewerExecutor.cs:576-577`) asks the adapter for a follow-up and falls back to a fresh REPAIR —
repairs are built for every seat (`RosterBuilder.cs:472-479`), so agy runs both kinds of second launch. **A second launch is asked
for only when the first answer did not parse** (`ConcludedAsync`, `:615-628`: an EMPTY answer takes that path, `:1069`); then
`AntigravityRuntime.FollowUp` (`AntigravityRuntime.cs:99-101`) continues the conversation only when `AntigravityStream.WasDenied`
(`:271-273`, same file) matches a literal anywhere in the ONE combined transcript it is handed (`Transcript`, `:726-727`:
`--- stdout ---` / `--- stderr ---`) AND `ConversationId` (`:395`) finds an id; the consultant path reads the facts parsed
(`DeniedActions(stdout, stderr)`, `:365-366`). Every agy launch runs as the agent `coai-reader`, whose only tool is `view_file`,
under a `PreToolUse` hook that allows nothing else (`AntigravityReadOnly.cs:29-32`, both measured in RESULTS_agy_write_block) — so
a prompt that needs `run_command` may be answered with an explanation rather than a denied call, and a denied call with a non-empty
answer is returned as it is (RESULTS_agy_consult_follow_up §3 saw both). `AfterTheRepair` (`:681-709`) has three branches and bills
both launches in each: a failed second launch keeps the first through `EarlierLaunches` (`:686`) and carries its own usage besides;
an answer and a second unparseable answer both `first.Usage.Add(second.Usage)` (`:692`, `:706`). The outcome then carries two parts
(`:644-651`): the first launch's usage and `SecondPart` (`:658-659`), what the SECOND launch reported. `LaunchLines.Of`
(`LaunchParts.cs:87-91`) splits a turn into lines only when the parts add up (`AddsUpTo`, `:116-121`). So if agy's follow-up report
is cumulative, every branch is too high by the first launch, the failure branch double-bills through `EarlierLaunches`, and the two
lines still add up to the wrong total — invisible to the guard. The rule exists for consultations: `ConsultationUsage.OfTwoLaunches`
(`src_mcp/core/Consultation/ConsultationUsage.cs:62-63`, the field-wise maximum at `:65-72`) behind
`IConsultantRuntime.UsageIsCumulative` (`IConsultantRuntime.cs:195`, default `false`; `AntigravityConsultant.cs:135` `true` — set
from a 2026-09-12 observation that §3 of the follow-up record says is *not settled*). `IReviewerRuntime` (`ReviewerRuntime.cs:324`)
has no such member. **The Team server has its own copy of the path:** `ReviewLauncher.FollowUpOf`
(`src_server/src/Jobs/ReviewLauncher.cs:261-264`) asks for the follow-up only when `Read(launch)` is `Failed { Unparseable }` and hands
`launch.Evidence` (the same combined transcript) to the same `FollowUp`; `Continued` (`:280-282`) sums `first.Usage.Add(second.Usage)`;
`Launches` (`:295-299`) writes the raw second report as the follow-up part.

**The local ledger line (item 6).** `UsageEntry` carries `Cli`, `CliVersion`, `Effort`, `EffortFrom`, `Tier`, `TierFrom`
(`UsageLedger.cs:175-196`), `EndCause` (`:227`), `Round` (`:241`) and `Turn` (`:157`, the Team server's). A review line is built by
`Entry` (`:423-445`) from the invocation's `RanWith`, labelled by `RowLaunchOptions.Labelled` (`RowLaunchOptions.cs:23-25`, called at
`RosterBuilder.cs:474`). A consultation (`ConsultantTurnBooks.cs:50-52`), a question row (`QuestionRowLaunch.cs:455-458`) and the
api probe (`ProbeApiMode.cs:286`) go through the local `RecordJob` overload (`UsageLedger.cs:510-513`), which hands
`JobPlace(LaunchOptions.None)` — effort, tier, CLI, turn and job are written EMPTY, so a local consultation line carries no
consultation id (the id exists: `consultationId`, `ConsultationService.cs:118`). The invocations are built at
`ConsultationService.cs:681` and `ConsultantCheck.cs:249`. A chat turn is the extension's own record (`chatUsage.ts:38-52`,
`ChatTurnRecord`: no effort, tier or CLI field). `EndCause` is empty locally because the round's clock and the caller's token reach
the executor as one linked token (`RoundEngine.cs:288-289`), the launcher records only `Cancelled: timedOut && callerCancelled`
(`ProcessLauncher.cs:570`), and the executor reads it at `ReviewerExecutor.cs:984-988`. The vocabulary is `EndCauses`
(`EndCauses.cs:20-44`: `withdrawn`, `abandoned`, `backstop`, `queueExpired`, `hostStopped` — server words). Its readers: the
extension's `usage.ts:67-72` (the field's comment lists the words) and `endCauseOf` (`:289-290`, any text); the server's
`UsageReader.cs:58-61` and `ServerJsonContext.cs:235` read the column verbatim. **The extension's line parser builds known fields
only** (`parseUsageLine`, `usage.ts:143-171`): an unknown field is ignored, the tokens and `costUsd` are read whatever else the line
carries. **`uncaptured()` (`usage.ts:506-508`) is true for ANY present `usageNote`** — such a line's money is `UNKNOWN` (`:498-500`)
and it is left out of totals and medians; `usageNote` means *usage not captured* and nothing else. The per-consultation money
divides by a count the caller supplies or by lines (`roundMoney.ts:184` `consult: 'consultation'`, `unitDivisor` `:297-299`); the
fallback label is *recorded turn* (`PER_RECORDED_TURN`, `:138`). `isLaunchLine` (`roundMoney.ts:69-71`) is an allow-list,
`ONE_LAUNCH_ATTEMPTS = {first, retry, repair}` (`:55`); `followUp` is in `NOT_ONE_LAUNCH_ATTEMPTS` (`:58-60`) BECAUSE its usage may be
cumulative — the thing E2.S1 changes.

**Kimi's gauge (item 1).** The shim sends `/usage` as a second prompt on the same session, after an ANSWERED turn only
(`AcpClient.cs:180`, `:189-203`), bounded by `usageBound` (`DefaultUsageBound` 15 s, `:43`; a timeout leaves the tokens unknown and
the turn intact, `:199-203`), and reads one line, `Session total: N input, M output` (`AcpUsageLine.cs:12-13`). The whole turn runs
inside `using (held.Hold)` (`AskAcpMode.cs:189-192`), and `AskAsync`'s one `finally` ends the agent and its tree whatever happened
(`AcpClient.cs:26-27`, `:127-129`) — so the hold is given back when the ask returns, never later. **Known before E4 (fact C,
2026-10-20, Kimi 2.2.0 on Windows, shim coai-mcp 0.62.0):** M-K5b re-run 3 of 3 — `/usage` after `session/load` on the same cwd
prints `Context: 20175 / 262144 tokens (8%) Session total: 40207 input, 84 output` — session totals only, **no plan/quota line**,
the same shape as 2.1.1 (RESULTS_kimi_confinement §23 and §5); M-K7b — `kimi-code/kimi-for-coding-highspeed` answers 403
"Your current subs…", a SUBSCRIPTION refusal that must not be read as a spent 5-hour window. The owner saw *Plan usage* with both
windows on 2026-10-19 in Kimi's own TUI `/usage`. Where Kimi states plan usage headlessly is therefore still open — E4's question.
Kimi launches one at a time (`KimiRuntime.cs:107-108`); the sign-in has two locks (`KimiCredentialsLock.cs:43-52`), a foreign-`kimi`
watch that looks ONCE, at the start (`ForeignKimi.cs:21-45`, its stated limit) and the shim's hold (`KimiShimHold.cs:21-28`). The
server has no Kimi runtime, so a Kimi reader is LOCAL — where story 6.1b deferred the codex reader because "a lock excludes only
cooperating coai processes". Kimi differs in one measured way: its launches ALREADY run under a cross-process lock and a
foreign-process check, because a race costs the sign-in. A rate limit is classified from `balance_spent` (`KimiRuntime.cs:269-280`).

**The codex reader to reuse (item 1).** `GaugeCapture` (`src_server/src/Slots/GaugeCapture.cs:30-38` the order, `:147-159` the
guards — a non-codex runtime is `NoGaugeForRuntime` at `:149-152`, so NO agy gauge is readable anywhere) and its codes
(`GaugeReading.cs:11-48`, `GaugeCodes`, referenced by nothing in `src_mcp`); the reading shape `GaugeWindowDto` (`:56`) and the
line shape `GaugeOnLine` (`GaugeOnLine.cs:29`, columns by `UsageEntry.WithGauge`, `UsageLedger.cs:303`). `CoaiServer.csproj:34`
references `CoaiMcp.Runners`, so a constant moved there stays visible to the server. `CooldownParser.StatedReset` reads only
`h`/`m`/`s` (`CooldownParser.cs:223`) and drops anything past `MaxStatedWait` = 14 d (`:52`) — a *resets in 24 d* is null to it.
`GaugeConsumers.UncountedFor` names only `direct-codex-use`, only for codex (`RoundsPerPercent.cs:73-89`). The extension words a
43 200-minute window as *monthly* (`gaugeWords.ts:36`).

**One store (items 4, 5).** `--round-counts` (`RoundCountsMode.cs:39-52`) answers `RoundCountsAnswer` (`RoundCounts.cs:24-31`)
from `coai.db` read-only (`Read`, `:113-125`) and never opens `usage.jsonl`. It counts distinct rounds by the integer `r.id`
(`SqlLaunches`, `:153-157`; `Distinct`, `:198`) — round 42 exists in BOTH stores, so a merge keyed on `id` would collapse two rounds
into one. `rounds.round_key` (`Schema.cs:615`, 32 hex, empty for rounds written before the column) is the cross-store key. The
extension spawns the mode on its own side (`roundCountsRead.ts:28-30`), parses it in `roundCounts.ts` (`parseRoundCounts`, `:92`),
reads its own ledger tail beside it (`roundsLoader`, `:39-54`), and names the other installations' stores by file existence only
(`otherStores`, `:60-62`; the sentence `roundsBlock.ts:110-115`). One-shot modes are listed in `.agents/PROJECT.md:51-69` and a new
one is a MODE, not a flag on an old one, so an old binary answers 64 (the `--findings-many` precedent, `:58-62`). The directories
come from `coai.alsoWatchDataDirectories` (`configTransfer.ts:34`) — a WSL store is spelled `\\wsl.localhost\…` OR `\\wsl$\…` from
Windows (two spellings, one store) and a Windows one `/mnt/c/…` from WSL. A round is written by `RoundsDb.RecordRound`
(`RoundsDb.cs:190`, called from the projection write at `RoundEngine.cs:620`). Volume (the promoted plan's *Growth*): ~1 500
rounds/month, ~8 000 reviewer rows — ~350 rounds in 7 d. `AtomicFile` (`AtomicFile.cs:25`) is the atomic write to reuse.

**The server count (item 3).** `UsageRounds.Of` (`src_server/src/Usage/UsageRounds.cs:39-53`) counts distinct non-empty round
ids over `review` lines only; `/api/usage` (`UsageEndpoints.cs:39`, scope at `:196-201`, `me` the default) answers per-vendor
TOTALS per window (`UsageTotals.cs:143` `Rounds`) — **no lines and no ids**; `GET /api/reviews/{id}` (`ReviewStatusDto`,
`ServerJsonContext.cs:185-196`) carries no round either. The round id is on the server's `usage.jsonl` lines (`:237`) and on this
side's `cli: remote` lines. A `canary` line is in neither `rounds` nor `runsWithoutRound`, which is why the deploy canary
(`deploy/systemd-release.sh:170`) cannot stand in for story 5.4. The zero-spend probe is `reviewBody.contract.ts`
(`COAI_CONTRACT_URL`, `COAI_CONTRACT_KEY`, `COAI_CONTRACT_DOMAIN`, `:37-39`), run through `scripts/run-contract.mjs`, which refuses
a non-local URL without `COAI_CONTRACT_ALLOW_REMOTE=1` (`:25`). The C#→TS seam script is `src_vs_code/scripts/seam-usage-counts.mjs`.

**Where the code contradicts the consultant's summary** (so nobody builds on the summary): (a) `LaunchLines.Of` is at
`LaunchParts.cs:87-91`, not "~57-91"; (b) the codex reader's lease, marker and containment live in `src_server`, which has no Kimi
runtime, so the Kimi reader cannot CALL `GaugeCapture` — it reuses its order, its codes and `GaugeOnLine`; (c) the ACP `/usage`
reply carries no plan-usage line on 2.1.1 or 2.2.0, so "read Kimi's gauge like the codex one" is a question before it is a story.

## Every open-tail bullet → where it goes

The promoted plan's *Open tail* (`research/PLAN_every_round_is_counted.md:729-807`) has more bullets than the seven items above.
Each one, and its home:

| Open-tail bullet | Where it goes |
|---|---|
| Census rows 2 and 4 — a consultation's / question row's continued turn is ONE line per turn | **non-item**: epic 3's code round rejected splitting; the line keeps the turn's correct sum. E3.S4 fixes the DIVISION, not the line |
| Row 5 — `--ask-api` run by hand writes nothing | **non-item**: not a launch coai made; the launcher that normally runs it writes the line |
| Row 9 — a chat turn whose vendor row was deleted mid-turn writes nothing | **non-item** here: an extension chat edge with no row to bill; its own small plan if the owner wants it |
| Row 10 — the broad `catch` in `JobRunner.RunAsync` ends the job `NotStarted` with no line | **non-item** here: server robustness, nothing was sent to a vendor; recorded, owned by no plan |
| Story 1.1 — agy in WSL not launched; the read with an EXPIRED codex token not tried | **non-item**: agy has no gauge to read (`GaugeCapture.cs:149-152`); copying a codex store duplicates its refresh token at the vendor (the tail's own reason) |
| Story 6.1b — local codex gauge capture deferred | **non-item** for codex (the lock excludes only cooperating processes). Kimi is the exception E5 builds, for the reason in *What the code says* |
| The codex rounds-per-1 % figure hidden by decision | item 7 — **non-item**, confirmed with the owner before E5 |
| The reader's containment, residual limits (unseen stdio-less descendant; a child between listing and kill; macOS) | **non-item**: accepted limits of the server reader, unchanged |
| Process-group ownership (Job Object / process group) | **non-item**: named in `module_runners.md` as the transport's follow-up; not this plan's |
| A per-window schema step | **non-item**: "not now" stands; E5.S3 keeps the Kimi reading in a JSON file, no table |
| `GaugeCapture` reason code — CLOSED by #875 | closed |
| The local line carried no round — CLOSED by #877; its rest, *launches per round* | **E6.S3** (and the C#→TS contract leg, E6.S1) |
| 4.1's probe against the production URL | **E7.S2** |
| A follow-up's usage may be cumulative | **E1** measures, **E2.S1/S3** fix (with [the follow-up plan](PLAN_the_reviewer_follow_up_is_billed_once.md)) |
| The counter reads one store | **E6** |
| Agy's second stderr line matches "exhausted" | owned by [PLAN_a_stated_wait_is_read_rather_than_matched.md](PLAN_a_stated_wait_is_read_rather_than_matched.md) |
| The consumer scope is narrower than the rule (local stores never summed; only `direct-codex-use` named) | **E5.S3** names `direct-kimi-use`; the local-store sum stays **open** and recorded — the ratio is hidden for both vendors |
| Local ledger gaps (`endCause`, effort/tier/`cliVersion`, per-consultation money) | **E3** |
| Live checks never recorded | **E7** |
| Labels that name the wrong thing — CLOSED by #891 | closed |
| `scope=me` gauges are company-wide (a design point) | **non-item**: the intended reading, recorded |
| Recorded limits kept (exit 79, the five-second floor, the transit margin, whole percentages) | **non-item**: each has its own ruling in the promoted plan |
| A second gauge exists, unread (Kimi) | **E4** measures, **E5** reads |

## Boundaries with the open plans (both sides)

| Item | Which plan builds it | The other one's part |
|---|---|---|
| The MEASUREMENT of a reviewer follow-up's usage | [PLAN_the_reviewer_follow_up_is_billed_once.md](PLAN_the_reviewer_follow_up_is_billed_once.md), its build order step 1 — this plan's E1 RUNS it (M1, redesigned below) and records the result in that plan | this plan adds nothing to the measurement's question |
| The reviewer's turn total under a cumulative follow-up (`OfTwoLaunches` behind a reviewer-side `UsageIsCumulative`) | that plan's step 2 — **built as E2.S1 (local) and E2.S3 (server)** here, because the total and its launch parts must change in one commit | that plan's boundary table already says the parts move with the sum; its DoD box is ticked by E2's commits |
| The second part's usage = the difference (cumulative) — so `LaunchLines.Of` still splits | **this plan** (E2.S1, E2.S3) | — |
| The reviewer's denial read parsed (`DeniedActions`) instead of matched (`WasDenied`) | that plan's *second defect* — **built as E2.S2** here, on the same path, both callers | — |
| Kimi's spent 5-hour window told from a lost sign-in (exit 77 after a run of launches; M-K14) | `todo/PLAN_a_spent_kimi_quota_is_not_a_lost_sign_in.md` (another session; not on main, not in this worktree) — classification of a REPORT | E4/E5 READ a gauge, never classify a failure. **Trigger:** if that plan has not landed by E2's release, E4's first step is to ask the owner to land or decline it; E4 does not start before the answer |
| What the deploy canary authenticates with | [PLAN_a_credential_that_is_not_a_person.md](PLAN_a_credential_that_is_not_a_person.md) | E7 uses the OWNER's session token for one real round; a canary line never counts as a round |
| The Kimi shim's `/usage` follow-up and its line parser | [PLAN_vibe_and_kimi_runtimes_E4.md](PLAN_vibe_and_kimi_runtimes_E4.md) E4.3 (shipped) | E5 here widens the parser to a second block and never changes the first line's reading |

Order: E1 first; E2 after E1's result (conclusive OR inconclusive — see E1); E3 any time after E1 starts (it does not wait for
M1); E4 after the Kimi trigger above; E5 after E4; E6 any time after E3 (it reads `endCause`/`cliVersion` only as columns); E7 last,
after E2's server deploy. **Owed to the other side:** the row that says E2 builds step 2 of the follow-up plan is written INTO that
plan's boundary table by E1's first commit.

## Item 7 — the codex rounds-per-1 % figure: a decided non-item

The owner chose *keep hidden* (story 6.2b): with direct codex use uncounted the measured ratio is a LOWER bound, so "≤ N" would be
wrong and "≥ N" is not what a person asks. Nothing here builds it. It opens when direct use can be counted or the owner chooses a
bound. **To confirm with the owner before E5 starts** (the Kimi ratio obeys the same rule).

## What is measured first, and what it may spend

Every measurement runs in a window with no other process of that vendor (Kimi's refresh token rotates — a second `kimi` signs the
owner out; agy shares the Google account with the Team server's slot). Each is recorded as a `research/RESULTS_*.md` row with the
raw blocks redacted (no email, no token).

| Measurement | Epic | Prediction written first | Budget (the owner's subscription) and the stop rules |
|---|---|---|---|
| **M1 — step 1, ONE capture under the CURRENT build configuration** (`Runtime.Build`, the `coai-reader` agent, the `view_file`-only hook): a reviewer prompt that needs a shell command (the consultation probe's elicitation, RESULTS_agy_consult_follow_up §3: a question unanswerable without a command, cited for the METHOD only) — kept as the fixture only if it shows all three continuation conditions at once: a denial (`denied_actions`), a conversation id, and an EMPTY answer (what `ConcludedAsync` and the server's `FollowUpOf` require before any follow-up). **Step 2a (elicited):** three launches in ONE conversation, 3 runs, by script on the real CLI — turn 1 the denied launch, turn 2 the follow-up as `FollowUp` sends it, turn 3 the words *reply OK*. **Step 2b (not elicited within the cap):** cumulative reporting measured SEPARATELY from denial recovery — turn 1 any short answered prompt, turn 2 an explicit same-conversation continuation (the one continuation `FollowUp` uses, `AntigravityRuntime.cs:103`), turn 3 *reply OK*; denial recovery stays unmeasured and is said so. All `usage` blocks kept. **Compared: OUTPUT and reasoning tokens**, never input | E1 | per-launch: turn 3's output is a few tokens, below turn 2's; cumulative: turn 3's output ≥ turn 2's ≥ turn 1's. Written down: *cumulative* | counted per ATTEMPT: ≤ 12 launches in all; step 1 ≤ 4 first launches (an answered, undenied, or denied-but-answered launch is recorded and the attempt discarded) — then step 2b; stop when 3 runs are complete; ≤ 400 k Google tokens by the reports' own input sums, ~60 k input per launch; no agy gauge is readable (`GaugeCapture.cs:149-152`), so no "window above 80 %" rule. **INCONCLUSIVE is an allowed outcome** (3 runs disagree, or the window ran out): recorded as such, never rounded to a verdict |
| **M2 — where Kimi's plan usage can be read:** (b) `/usage` as the ONLY prompt on a fresh session (no model call, 3 runs); (c) Kimi's session log for a quota field; (d) the TUI's `/usage` wording, for the parser. Cell (a) — the shim's `/usage` after an answered turn — is ANSWERED by fact C (session totals only on 2.2.0) and not re-run | E4 | after fact C: (b) prints the same session line and no plan line; (c) carries no quota; (d) the TUI alone has it — then E5 is cut | (b), (c), (d) are free; the whole of M2 inside ONE 5-hour window, no other `kimi` running; **stop at the first exit 77 / 401** (a lost sign-in is M-K14's, not this plan's — no re-sign-in inside the window); a 403 is read by its wording: the subscription-model 403 (M-K7b) is not the spent-window one |
| **M3 — one plan round through the company Team server, `/api/usage?windows=5h,24h,week&scope=me` before and after, and the server's ledger lines for the round key; the zero-spend probe against the production URL** | E7 | each vendor's `rounds` +1, `runsWithoutRound` unchanged, the round key on every vendor's lines; the probe answers the kind refusal | one plan round on a ~2 KB plan: ≤ 3 launches, one per vendor, ~60 k input each (the reviewer's prompt dominates, not the plan — agy's measured size); the probe spends nothing |

## Build order (and what each epic ships)

| # | Epic | Ships |
|---|---|---|
| 1 | **E1** — M1, recorded in the follow-up plan; the boundary row written into it | nothing (a `research/RESULTS_*` record and two plan edits) |
| 2 | **E2** — the accounting: S1 (cornerstone) the cumulative follow-up billed once in all three branches, parts still adding up, `usageBasis` on the line; S2 the parsed denial, streams apart; S3 the Team server's follow-up path under the same rule; S4 the extension's discriminator (`isLaunchLine`) | coai-mcp · Team server (+ deploy) · **extension** |
| 3 | **E3** — the ledger's metadata and its presentation: S1 labels on consult/question/probe lines; S2 a local `endCause` from the cancellation's source; S3 the chat line's `cli`/`cliVersion`; S4 per-consultation money per CONSULTATION | coai-mcp (S1, S2, S4's field) · **extension** (S2's help, S3, S4's division and label) |
| 4 | **E4** — M2, in a Kimi window of its own, after the spent-quota trigger is answered | nothing (a record and a fixture) |
| 5 | **E5** — the Kimi gauge: S1 the parser (days included); S2 (cornerstone) the read inside the turn's hold; S3 stored, answered by `--round-counts`, shown, the ratio hidden with the consumer named | coai-mcp (S1, S2, S3's store and answer) · **extension** (S3's card and block) |
| 6 | **E6** — both stores and launches per round: S1 (cornerstone) the snapshot after `RecordRound` and a NEW one-shot merge mode keyed by `round_key`; S2 the other side's ledger tail; S3 launches per round; S4 the block and the card; S5 the live contract leg | coai-mcp (S1) + one line in `.agents/PROJECT.md` · **extension** (S2–S5) |
| 7 | **E7** — M3 after E2's server deploy (and after any later server change this plan causes) | nothing (a record and a `POST_DEPLOY.md` stamp) |

A story that changes what the extension shows or reads ships an extension release; the mcp/server releases alone never close it.

## Risk

- **A wrong accounting change moves every spending figure** (E2.S1, E2.S3). Held by: the add-up guard stays; the cumulative
  difference is floored at zero through `ConsultationUsage.Less`, never a new subtraction; cumulative applies ONLY to a
  `followUp` attempt on a runtime that says so — a repair stays additive; a non-cumulative runtime is byte-identical (a golden
  ledger over the fixtures of `EveryPaidLaunchIsWrittenTests`); the server's and the local rule are one function; **the follow-up
  line never carries `usageNote`** — any present `usageNote` makes the extension drop the line's money (`uncaptured()`), so the
  measured difference would vanish from totals and medians; `usageBasis` alone discriminates.
- **A second Kimi process signs the owner out** (E4, E5). Held by: the read runs INSIDE the turn's hold, after the foreign watch
  (which looks once, at the start — the read adds no second look and claims none), never from the extension, never on a timer,
  never as a launch of its own; M2 stops at the first auth error; on any auth error the read records `auth-refused`.
- **The other side's snapshot is stale, half-written or doubled** (E6). Held by: `AtomicFile`; the snapshot is written at the
  mutation point (after `RecordRound`), never by a read; it carries `writtenUtc`, `schema`, the owning side's own spelling of the
  directory (the store id — `\\wsl$` and `\\wsl.localhost` resolve to ONE file with ONE id, so a store named twice is counted once)
  and the RAW rounds of the last 7 d, cut with the reader's clock; a window that begins after `writtenUtc` is shown *as of <when>*
  (complete unless a write failed, which is logged); a snapshot older than 7 d is a QUIET store — its windows are 0 *as of <when>*,
  never *not included*; missing, unreadable or a newer `schema` is *not included: <reason>* — never 0 and never silently merged.
  WSL's clock may drift after a sleep: `writtenUtc` in the future is shown as such, not clamped.
- **A number from two stores that are one account read as two accounts** (E6). Held by: rows are merged by vendor row NAME with
  each store's part shown, and the sentence says the stores are counted together — nothing claims one account or two.
- **The live round spends the company's accounts** (E7). Held by: M3's budget; the probe first; the owner present.

## Test plan (RED first, the symptom named)

| Test (new unless said) | RED symptom before the fix |
|---|---|
| `ACumulativeFollowUp_IsBilledOnce_AndItsPartsStillAddUp` (mcp) — two cumulative reports 100 → 140, attempt `followUp`: the turn is 140, the lines 100 + 40, the second line `usageBasis: difference` and NO `usageNote` | the turn is 240 and the lines 100 + 140 |
| `ACumulativeRuntimesRepair_StaysAdditive` — agy-shaped runtime, attempt `repair`, 100 + 40 | — (green before and after; a plant that applies the rule to repairs goes red) |
| `ANonCumulativeFollowUp_IsUnchanged` — codex-shaped runtime, same fixture | — (green before and after; a plant that flips the flag goes red) |
| `ASecondLaunchKilledOnItsDeadline_UnderCumulativeReporting_IsNotBilledTwice` — the failure branch of `AfterTheRepair`, second launch reports a cumulative 140 then is killed | the turn is 240: `EarlierLaunches` 100 + the failed launch's own 140 |
| `AReviewerThatGrepsForAutoDenied_IsNotAskedAgain` — the word on stdout, no denial on stderr, streams passed apart | a follow-up is launched |
| `TheServersContinuation_UnderCumulativeReporting_BillsOnce` (server) — `ReviewLauncher.Continued` and `Launches`, same fixture | the job's usage is 240, the follow-up part 140 |
| `roundMoney.test.ts` (contract): a `followUp` line with `usageBasis: difference` is one launch; an old follow-up line is not; `ONE_LAUNCH_ATTEMPTS` ∪ `NOT_ONE_LAUNCH_ATTEMPTS` still names every `LaunchAttempts` word | the new line is *per recorded turn* |
| `usage.test.ts` (compatibility): the parser of TODAY's extension (`usageBasis` an unknown field, `ONE_LAUNCH_ATTEMPTS` of today) reads the new follow-up line's tokens and `costUsd` whole, its money is known, the group's figure says *per recorded turn* | — (must be green against the committed parser; a plant that writes `usageNote` on the line goes red: money `UNKNOWN`) |
| `AConsultationLine_SaysWhatItRanWith`, `AQuestionRowLine_…`, `AProbeLine_…` — `cli`, `effort`/`effortFrom`, `tier`/`tierFrom` as sent; a consult line carries `consultation` | every field empty |
| `ALocalCancellation_CarriesItsSource` — the MCP client's cancel → `withdrawn`; the round clock → `roundClock`; the server's stop → `hostStopped`; a token nobody can name → `unknown` | `endCause` empty on all |
| `chatUsage.test.ts`: a chat line carries `cli` and `cliVersion`; an old line reads both empty | the record has no such field |
| `usagePricing.test.ts`: per-consultation money divides by distinct `consultation` ids; an old line (no id) is its own unit and the label says so | divides by lines |
| `KimiPlanUsageTests` — the parser over M2's verbatim text: two windows (`5h`, 43 200 min), used %, reset instant from `d`/`h`/`m` (*resets in 24 d* reads); the subscription 403 and a missing block → no reading, never 0 | — (new parser) |
| `AKimiGaugeIsReadInsideTheTurnsHold` — the read runs after the answer, before the hold's `using` ends; a `/usage` past `usageBound` → `read-timed-out`, the turn's own result unchanged, the hold released by the same `finally`; an auth error → `auth-refused`; a turn the watch refused at the start has no read | a read outside the hold |
| `RoundCountsTests` (+): `--round-counts` writes NOTHING; `RecordRound` writes `round-counts-snapshot.json` (atomic) with `round_key`s; a failed snapshot write is logged and the round still completes | the snapshot is written by the read / the write's failure fails the round |
| `--round-counts-merged` (mcp): two stores holding a round with the same integer id → two rounds; the same key twice → one; a quiet snapshot (> 7 d) → 0 *as of*; missing / unparseable / newer schema → `storesNotIncluded` with the reason; an old binary → 64 | `Distinct` on `id` collapses them |
| `TwoStoresAreCountedTogether` (ts): the merged answer → one row per vendor with both parts; 64 → the single-store sentence of today | the other store is named and not counted |
| `LaunchesPerRound_CountsOnlyRoundsWithIds` (ts): launch lines (`isLaunchLine`, no `NoBudget`, no `cli: remote`) carrying a `round` → launches ÷ distinct keys; rounds with no such line reported apart as *N rounds carry no launch count*, with *written before 0.60.2* only where the round's key is empty | — (new figure) |
| `the seam leg` (`scripts/seam-usage-counts.mjs` and a sibling `seam-round-counts-merged.mjs`): the built binary's `--round-counts` and `--round-counts-merged` fed to `parseRoundCounts` | — (E6.S5; run again before E7) |
| `RESULTS_the_round_counter_tail.md`: M1 (the step-1 fixture or why there is none; 2a or 2b; the verdict or *inconclusive*), M2, M3 rows with before/after, versions, round keys | — |

Each RED message and its plant go into `research/module_tests.md` with the story that lands it.

## Definition of Done

- [ ] M1 recorded in `PLAN_the_reviewer_follow_up_is_billed_once.md`: the step-1 capture (or why none could be elicited), every
      `usage` block per run, the verdict — *cumulative*, *per launch* or *inconclusive*; its boundary table names E2.
- [ ] A cumulative reviewer follow-up is billed once — locally in every branch and on the Team server — its two lines add up to
      the turn, the follow-up line carries `usageBasis` and no `usageNote`, repairs and non-cumulative runtimes unchanged; the
      reviewer's denial is read parsed from the two streams; today's extension reads the new line's money whole.
- [ ] Consultation, question-row, probe and chat lines say what they ran with; a consult line carries its consultation; a local
      cancellation carries its source; the Rounds block's per-consultation money is per consultation and says so.
- [ ] M2 recorded with fact C beside it; if a headless source exists: Kimi's 5-hour and monthly windows are read inside the
      turn's hold only, written on the line, answered by `--round-counts`, shown with their reset, the ratio hidden with
      `direct-kimi-use` named. If none exists: E5 cut and the owner told.
- [ ] Both stores are counted through the new one-shot mode (listed in `.agents/PROJECT.md`), each named with its part and its
      freshness; an old binary falls back to today's sentence; a missing or unreadable store is *not included* with the reason,
      never 0; a quiet store is 0 as of its write; launches per round is shown over the rounds that carry keyed lines.
- [ ] M3 recorded: one round → `rounds` +1 per vendor under `scope=me`, the round key on the server's lines for each vendor, and
      the probe against the production URL; `POST_DEPLOY.md` stamped.
- [ ] Item 7 confirmed with the owner as not built; every bullet of `research/PLAN_every_round_is_counted.md`'s *Open tail* has a
      row in *Every open-tail bullet* above, and the tail says so.
- [ ] Every story shipped in the binary the build-order table names — an extension release wherever the extension's behaviour
      changed; `module_runners.md`, `module_server.md`, `module_extension.md`, `module_team_server.md` updated per epic; help text
      in five languages where a person reads a new word; `node .agents/conventions/tools/plan-lifecycle.mjs` clean.

## Epics

Seven epics, each on its own branch from main, one pull request each, the coai gate once per epic (plan round with the epic
declared, code round over the whole diff). Stories documents (`PLAN_the_round_counter_tail_E<n>.md`) are written when each epic
starts. **Cornerstones: 0 of 3 — E2.S1, E5.S2, E6.S1** (the quota ⌈2·7/5⌉ = 3; specs by Fable, the rest Opus; measurements Fable).

### E1 — measure the agy reviewer follow-up (no product code)

- **Goal:** settle item 2's question with numbers before anything is built — by output tokens, which input growth cannot settle.
- **Stories:** S1 M1 step 1 — ONE capture under the current build configuration (agent, hook, `Runtime.Build`) that shows a
  denial, a conversation id and an empty answer together, kept as the fixture `src_mcp/tests/fixtures/antigravity/review-denied.*`;
  S2 step 2a or 2b as the table says, 3 runs, every `usage` block redacted into `RESULTS_the_round_counter_tail.md`; S3 the verdict
  written into the follow-up plan (its step 1 ticked; step 2 or 3 chosen, or *inconclusive* with the budget spent) and the boundary
  row owed to that plan written in. The consultant's 2026-09-12 observation is NOT cited as a verdict (§3: *not settled*).
- **Depends on:** a Google window with the Team server's agy slot idle (ask the owner for the hour). **Budget:** M1's.
- **Done when:** the follow-up plan says *measured: cumulative*, *measured: per launch* (E2.S1's flag stays `false` for agy; the
  mechanism still ships), or *inconclusive* — then E2.S1 ships the whole mechanism with `AntigravityRuntime.UsageIsCumulative =
  false` (byte-identical figures, no risk taken on a guess), the flag flips in a one-line follow-up commit when a later measurement
  settles it, and the follow-up plan stays open on that one line. E2.S2–S4 and E3 proceed whatever M1 says.

### E2 — the accounting is right on both follow-up paths

- **Goal:** the ledger's lines and totals say what was spent when agy is asked again in one conversation, locally and on the Team
  server, and the extension reads the new line as what it is.
- **S1 (CORNERSTONE) — a cumulative follow-up billed once, in all three branches, parts included.** `IReviewerRuntime` gains
  `UsageIsCumulative` (default `false`, `AntigravityRuntime` per E1's verdict, the consultant's word reused); the rule is ONE
  predicate, `cumulative = runtime.UsageIsCumulative && attempt == LaunchAttempts.FollowUp` — a REPAIR is a fresh conversation and
  stays additive. `AfterTheRepair` applies it in each branch: the answer and the unparseable branches use
  `ConsultationUsage.OfTwoLaunches(cumulative, first, second)`; the failure branch keeps `EarlierLaunches = first` and sets the
  failed launch's OWN usage to `Less(second, first)` under cumulative, so `LastTurnUsage` is the maximum, not the double. The second
  PART is `Less(second, first)` under cumulative, so `LaunchLines.AddsUpTo` holds by construction; a second launch that reports
  nothing keeps the first's. The follow-up line carries ONE new field, `usageBasis: difference` (`UsageEntry.UsageBasis`, absent on
  every older line and on every other line) — **never `usageNote`**, which the extension reads as *usage not captured* and
  prices as unknown (`usage.ts:506-508`); the explanation a person reads is derived from `usageBasis` by the extension (S4).
  Spec by Fable: the edge table (second < first, second `Unknown`, money on one side only, the security answer limit after a
  follow-up, each branch). Ships in coai-mcp.
- **S2 — the denial read parsed, streams apart.** `FollowUp(first, stdout, stderr)` decides on `DeniedActions(stdout, stderr)`;
  both callers pass the `ProcessResult`'s two streams (the executor holds it on `ReviewerLaunch`, `:988`; the server's
  `FollowUpOf` reads `launch`'s result the same way) — never a split of the combined transcript on `--- stderr ---`, which would be
  the matched read again. `WasDenied` stays for nothing but its tests, then goes. Ships in coai-mcp and the Team server.
- **S3 — the Team server's follow-up path, the same rule.** `ReviewLauncher.Continued` (`:280-282`) and `Launches` (`:295-299`)
  call the ONE function S1 writes (in `CoaiMcp.Runners`, which the server references): the job's usage is `OfTwoLaunches(cumulative,
  …)`, the follow-up part `Less(second, first)` with `usageBasis`; `FollowUpOf` passes the streams (S2). One Team server release,
  deployed — E7 runs after it. `module_team_server.md` updated.
- **S4 — the extension's discriminator.** `isLaunchLine` counts a `followUp` line as one launch exactly when `usageBasis` is
  `difference`; `NOT_ONE_LAUNCH_ATTEMPTS.followUp`'s reason is rewritten (*an older follow-up line may include the first launch*);
  the contract test still names every `LaunchAttempts` word; the line's tooltip says *difference of a cumulative report* from
  `usageBasis`. **Compatibility, both ways:** today's extension reading a new line ignores the unknown field (`parseUsageLine` builds
  known fields only), counts its tokens and money whole, and labels the group *per recorded turn* — right, if conservative; the new
  extension reading an old follow-up line (no `usageBasis`) keeps today's reading. Ships in the extension; test rows 7–8.
- **Depends on:** E1's result (any of the three). **Done when:** test rows 1–8 are green from red; one coai-mcp release, one Team
  server release deployed, one extension release.

### E3 — the ledger's metadata and its presentation

- **Goal:** the lines story 3.4 left unlabelled say what they ran with, a local cancellation says why, and the block's money
  per consultation is per consultation. Nothing here waits for M1.
- **S1 — consult, question and probe lines say what they ran with.** `RowLaunchOptions.Labelled` (or its `Of`) applied where the
  consultation (`ConsultationService.cs:681`), the check (`ConsultantCheck.cs:249`), the question row and the api probe build their
  invocations; the local `RecordJob` overload gains a `LaunchOptions` (the server overload's shape, reuse-first). `cliVersion` only
  where a version was already asked (no spawn per launch). Ships in coai-mcp.
- **S2 — a local end cause, from the cancellation's SOURCE.** The engine tells its own clock (`clock.Token`, `RoundEngine.cs:288`)
  from the caller's token (`ct`, the MCP client's cancel) and from the server's own stopping token where `ServeAsync` hands one:
  `roundClock` (a new local word in `EndCauses`), `withdrawn`, `hostStopped`; a cancellation whose source cannot be told is
  `unknown` — never inferred from a sentence, never empty when cancelled. Readers that learn the words: `EndCauses.Known` (`:47`,
  every cause this build names), the extension's `usage.ts:67-72` comment and the help text; the server's `UsageReader.cs:58-61` and
  `ServerJsonContext.cs:235` read any text and need no change — `roundClock` is a local word the server never writes. Ships in
  coai-mcp (the cause) and the extension (the help).
- **S3 — the chat line's `cli` and `cliVersion`** on `ChatTurnRecord` (the launching side knows the CLI; the version from the
  catalog probe's last answer, never a spawn); effort/tier stay empty on chat lines and the Rounds block says *not recorded*. Ships
  in the extension.
- **S4 — per-consultation money per consultation.** The local consult line gains `consultation` (the `consultationId` of
  `ConsultationService.cs:118`; coai-mcp); the block groups consult lines by it before dividing; an older line without one is its own
  unit; the label changes from *per recorded turn* to *per consultation (older lines per turn)*, help in five languages (extension).
- **Built 2026-10-21 on `feat/tail-e3-ledger-metadata`** (see [the E3 stories](PLAN_the_round_counter_tail_E3.md) for what shipped differently).
  Found there and NOT built: the `review_*` tools take no MCP request cancellation token, so `withdrawn`/`hostStopped` cannot be written by a
  live server until they do — wiring it makes a client's cancel stop the round, a behaviour change for its own decision.
- **Depends on:** nothing but E1 having started (its fixture names the fields). **Done when:** test rows 9–12 are green from red;
  one coai-mcp release, one extension release.

### E4 — measure where Kimi states its plan usage (no product code)

- **Goal:** item 1's source, measured, before a reader is written — the ACP reply carries none on 2.1.1 or 2.2.0 (fact C).
- **Stories:** S1 fact C written into `RESULTS_the_round_counter_tail.md` §M2 as the known starting point (M-K5b re-run, M-K7b);
  S2 M2 cells (b)–(d) with predictions first, in ONE 5-hour window with no other `kimi`, stopping at the first auth error; S3 the
  verbatim `/usage` text (redacted) as a fixture under `src_mcp/tests/fixtures/kimi/` (a new folder beside `antigravity/`); S4 the
  decision for E5 — a headless source or none — and the window semantics recorded (a monthly window has its own reset; the spent
  5-hour 403's wording against the subscription 403's).
- **Depends on:** the Kimi trigger in the boundary table (the spent-quota plan landed or declined). **Budget:** M2's.
- **Done when:** §M2 says which cell carries both windows, or that none does — then E5 is cut and item 1 recorded as *checked: the
  shim's reply, a usage-only prompt, the session log; the TUI only* with the owner told.

### E5 — the Kimi gauge is read, inside the turn's hold

- **Goal:** Kimi's 5-hour and monthly windows, with their resets, beside the Kimi row — the codex reader's rules, locally.
- **S1 — the parser.** `KimiPlanUsage.Parse(text)` → `GaugeOnLine` windows (`limitId: kimi`, `mins: 300` and `43200`, used %, reset
  instant from *resets in …* read by its OWN duration reader that knows `d`, `h`, `m` — `CooldownParser.StatedReset` stays a refusal
  parser capped at 14 d, untouched); `AcpUsageLine` untouched. Ships in coai-mcp.
- **S2 (CORNERSTONE) — the read, contained.** The `/usage` prompt (source per E4) runs INSIDE the turn's hold on the same session,
  after the answer — and, decided here, after a spent-window 403 as well (the agent answers `/usage` itself, no model call, M-K2), so
  a spent window is not a stale value; never after an auth error. Bounded by `usageBound` (15 s today): a read past it is
  `read-timed-out` and the pending call is ended by the ask's own `finally` with the tree; the hold is the `using` the turn already
  runs in (`AskAcpMode.cs:189-192`) and is released when the ask returns, never held past it. Never from the extension, never on a
  timer, **never a launch of its own**. The foreign watch looks once, at the start (its stated limit): a turn it refuses has no
  read, and the read adds no second look. An auth error or a refresh request stops it with `auth-refused`/`refresh-requested` —
  `GaugeCodes` MOVES to `src_mcp/runners/Reviewers/GaugeCodes.cs` (beside `GaugeOnLine`; `CoaiServer.csproj:34` already references
  the library, `GaugeReading.cs` keeps the DTOs and gains a `using`, the server compiles unchanged); the previous reading is marked
  stale, the launch's own result unchanged; one line in the log, numbers and codes only. Spec by Fable: the order, the no-read
  cases, the break-its (a plant that reads outside the hold, a plant that spawns a read of its own). Ships in coai-mcp.
- **S3 — stored, answered, shown.** The reading on the Kimi line (`WithGauge`, the local `Entry` gains the parameter) and in one
  overwritten `kimi-gauge.json` in the data directory (`AtomicFile`); `--round-counts` answers `gaugeReadings[]` (vendor, windows,
  observed, stale) beside the `gauge` figure — the figure stays hidden: `GaugeConsumers.UncountedFor` gains `direct-kimi-use` (the
  owner runs Kimi's TUI; RESULTS §4's rule), so `consumer-not-counted` names it (coai-mcp); the card and the Rounds block show
  *kimi: 5 h 100 % · resets in 1 h · monthly 39 % · resets in 24 d · read <when>* through `gaugeWords.ts`, help in five languages
  (extension). The derivation over local lines is NOT built (the mode never opens the ledger; the ratio is hidden).
- **Depends on:** E4; item 7's confirmation. **Done when:** a Kimi round on this machine shows both windows with their resets, a
  refused turn leaves the gauge unread with the reason, and `KimiPlanUsageTests` holds the fixture; one coai-mcp + one extension
  release.

### E6 — both stores are counted, and launches per round

- **Goal:** the Rounds block counts the Windows and the WSL store together, each named, and says launches per round.
- **S1 (CORNERSTONE) — the snapshot at the mutation point, and a NEW one-shot merge mode.** The owning OS's coai-mcp writes
  `round-counts-snapshot.json` beside its `coai.db` **after `RecordRound`** (`RoundEngine.cs:620`, the projection write) — best
  effort: a failed write is logged and the round completes; staleness shows through `writtenUtc`. `--round-counts` stays READ-ONLY
  and writes nothing. The file (`AtomicFile`) holds `schema: 1`, `writtenUtc`, the store id (the owning side and its own spelling of
  the directory, so two UNC spellings of one store merge to one), and per round of the last 7 d: `round_key` (empty for rounds
  older than the column), the integer `id`, `started_utc`, `stage`, the reviewer rows (`provider`, `model`). Bound, from the
  *Growth* numbers: ~350 rounds × ~300 bytes ≈ 100 KB, rewritten ~50 times a day (≈ 5 MB/day of writes); a hard cap of 5 000 rounds,
  oldest dropped and `truncated: true` said. Per-round records, not aggregates, because S3 needs the keys. **The merge is a NEW
  one-shot mode, `--round-counts-merged <snapshot-path>…`**, registered in `.agents/PROJECT.md`'s list (the `--findings-many`
  precedent: an old binary exits 64 and the extension falls back to `--round-counts` and today's single-store sentence; an answer
  without `stores[]` keeps that wording). It reads this side's `coai.db` and each snapshot (plain JSON, never the other side's
  WAL database), keys distinct rounds by `round_key` and — where it is empty — by (store id, `id`), cuts the windows with the one
  `RoundCounts.Cut` at the same `now`, and answers rows per vendor WITH per-store parts (`stores[]`: id, side, directory, `asOf`,
  rounds) and `storesNotIncluded[]` with a reason (missing, unreadable, newer schema). A quiet snapshot (older than 7 d) is a
  part with 0 *as of <when>*; a window that begins after `asOf` is shown *as of*. Both answers also carry the distinct `roundKeys`
  per window and vendor for S3. Spec by Fable: the file's growth and cap, its interruption (atomic), the reasons, the schema
  handshake, the store id. Ships in coai-mcp (+ `PROJECT.md`).
- **S2 — the other side's ledger tail beside this side's.** `roundsLoader` reads `<other store>/usage.jsonl` from its end as it
  reads its own (plain append-only JSONL, not WAL), each tail keeping its store id, so tokens and money carry provenance too; a tail
  that will not read is *not included* like the snapshot. Extension.
- **S3 — launches per round.** Over the covered population, both stores: launch lines (`isLaunchLine` — so no `NoBudget`, no
  `cli: remote`, no old follow-up) carrying a `round` → launches ÷ distinct keys, per vendor row and window; the rounds a window
  counted whose key is on NO such line (the `roundKeys` of S1 minus the lines' keys) are reported apart as the fact *N rounds carry
  no launch count*, with *written before 0.60.2* attached only to a round whose `round_key` is empty (the one checkable sign); the
  C#→TS contract leg (`roundCounts.ts` parser) the #877 record said belongs to this first consumer. Extension.
- **S4 — the block and the card.** The store sentence becomes *Counted from two stores — Windows (this machine) and WSL (<distro>,
  as of 3 min ago)*; `storesNotIncluded` keeps today's sentence with the reason; `cardRoundsLine` sums both; help in five
  languages. The bundled-page test runs the block with two stores, with one quiet and with one missing. Extension.
- **S5 — the live contract leg.** A sibling of `scripts/seam-usage-counts.mjs` runs the built binary's `--round-counts` and
  `--round-counts-merged` over a fixture store and snapshot and feeds the stdout to the extension's real `parseRoundCounts`; it
  runs in the extension's check and again before E7. Extension scripts.
- **Depends on:** E3 (columns only). **Done when:** a Windows window shows the WSL store's rounds with their age, and the reverse
  from a WSL remote window; an old binary shows today's sentence; one coai-mcp + one extension release.

### E7 — the live checks, recorded (no product code)

- **Goal:** story 5.4's "+1 exactly" and 4.1's production probe observed and written down.
- **Stories:** S1 the owner's session token in `COAI_SESSION_TOKEN` for one hour (the canary's token is a person's and expires; it
  is NOT used); S2 the zero-spend probe against `https://coai.remsoft.dev` (`COAI_CONTRACT_URL`, `COAI_CONTRACT_KEY`,
  `COAI_CONTRACT_DOMAIN`, `COAI_CONTRACT_ALLOW_REMOTE=1`); S3 M3 — the round key read from this side's `rounds.round_key` after the
  round; `/api/usage?windows=5h,24h,week&scope=me` before and after, the `week` window the one the delta is read from (a round can
  age out of `5h` between the reads), `scope=me` so other people's rounds in the window never move the number; **the primary
  assertion is per round key**: it appears on the server's `usage.jsonl` lines for each vendor after and on none before (read on
  the box by the owner — `/api/usage` carries no lines and `/api/reviews/{id}` no round); the count delta (+1 per vendor,
  `runsWithoutRound` unchanged) is the secondary check; S4 the record — deployed server version from `/api/health`, extension and
  coai-mcp versions, vendor, round key, before/after — in `RESULTS_the_round_counter_tail.md` and a `POST_DEPLOY.md` stamp; the
  promoted plan's *Open tail* updated.
- **Depends on:** the owner's token and presence; E2's server deploy (and any later one this plan causes). **Budget:** M3's.
- **Done when:** both observations are recorded with their numbers, or a mismatch is recorded as a defect with its own plan.

## What the reviews changed (2026-10-20)

The coai plan gate (session e69bd104, findings G), an Opus review (findings O) and the epics 1–3 cadence consultation (findings C),
each verified in the code before it was folded in. Epic numbers below are the CURRENT ones (C3 renumbered them).

- G1 — the merge is a NEW one-shot mode `--round-counts-merged`, listed in `.agents/PROJECT.md`; 64 → the old wording. → E6.S1, build order 6.
- G2/G6 — `--round-counts` stays read-only; the snapshot is written after `RecordRound`, best effort, logged; test row 15. → E6.S1, Risk.
- G3 — the gauge read is bounded by `usageBound`, times out to `read-timed-out`, the hold is the turn's `using`. → E5.S2, test row 14.
- G4 — `roundClock`'s readers named: `EndCauses.Known`, `usage.ts:67-72`, help; the server reads any text. → E3.S2.
- G5/O10 — M3 asserts per round key on the server's ledger lines, `scope=me`, the `week` window; the delta is secondary. → E7.S3, M3.
- G7 — the Kimi trigger: not landed by E2's release → ask the owner to land or decline. → Boundaries, E4.
- G8 — *N rounds carry no launch count* is the fact; *before 0.60.2* only where `round_key` is empty. → E6.S3, test row 18.
- G9 — M1's elicitation named, budget per attempt, a cap on undenied first launches, a stop condition. → M1, E1 (reshaped again by C1).
- G10 — a live contract leg over both modes and the real parser. → E6.S5, test row 19.
- G11 — volume, size and rewrite rate computed, a 5 000-round cap; per-round records kept for S3's keys. → E6.S1.
- G12 — every cancellation source mapped (`roundClock`, `withdrawn`, `hostStopped`, `unknown`). → E3.S2, test row 10.
- O1 — cumulative only for `followUp`; a repair stays additive; a test for it. → E2.S1, test row 2, Risk.
- O2 — the server's own follow-up path gets the same rule (E2.S3), E7 runs after its deploy; `GaugeCodes` moves to `CoaiMcp.Runners`. → E2.S3, E5.S2, build order.
- O3 — M1 compares output/reasoning tokens, adds the *reply OK* turn, replaces the unreadable 80 % rule with a token/attempt cap. → M1.
- O4 — distinct rounds keyed by `round_key`, (store id, `id`) as the fallback, carried in the snapshot. → E6.S1, test row 16.
- O5 — the table *Every open-tail bullet → where it goes*; the DoD line now says every bullet. → new section, DoD.
- O6 — the gauge's reset reads days with its own reader; `StatedReset` untouched. → E5.S1, test row 13.
- O7 — `/usage` also after a spent-window 403; never a standalone launch; the watch looks once and the test says so. → E5.S2.
- O8 — windows *as of*, a quiet store is 0, one store id for two spellings, WSL clock drift shown. → Risk, E6.S1.
- O9 — `roundKeys` in the answer, `isLaunchLine` reused, `NoBudget` and `cli: remote` excluded. → E6.S1, E6.S3.
- O11 — a `consultation` field on the local consult line; older lines are their own unit; the label corrected. → E3.S4.
- O12 — `FollowUp` takes stdout and stderr apart, both callers. → E2.S2.
- O13 — all three branches of `AfterTheRepair` designed; test row 4's RED corrected (double-billing, not a lost first launch). → E2.S1.
- LOW — the seam script path and the probe's three variables named; M3's input figure made consistent (~60 k); `usageBasis` tells a new follow-up line from an old one; M2 stops at exit 77/401. → What the code says, M2, M3, E2.S1/S4.
- Fact C — Kimi 2.2.0's `/usage` carries session totals only (M-K5b re-run) and the highspeed 403 is a subscription refusal (M-K7b); M2's cell (a) answered, its prediction for (b) flipped. → What the code says, M2, E4.
- C1 — M1's step 1 is ONE capture under the real `coai-reader` agent + `view_file` hook, kept as the fixture only with a denial, a conversation id AND an empty answer (the follow-up's own preconditions, local and server); not elicited → cumulative reporting measured through an explicit continuation, apart from denial recovery; *inconclusive* allowed, and E2.S1 then ships the mechanism with the agy flag `false`; the consultation record is cited for method only, never as proof. → M1, E1, DoD, What the code says.
- C2 — the follow-up line carries `usageBasis: difference` ONLY; `usageNote` is reserved for uncaptured usage (`uncaptured()` would price the line unknown and drop it from totals); the explanation is derived from `usageBasis`; a compatibility case for today's extension (unknown field ignored, money whole, *per recorded turn*) with its test. → E2.S1, E2.S4, Risk, test rows 1 and 8.
- C3 — E2 split by dependency into E2 (the accounting: S1, S2, S3 server, S4 the extension's discriminator) and E3 (metadata and presentation: labels, `endCause`, chat `cli`, per-consultation money), so an inconclusive M1 blocks nothing but E2.S1's flag; seven epics, cornerstones ⌈2·7/5⌉ = 3 (E2.S1, E5.S2, E6.S1); every story traced to its shipping binary, with an extension release wherever the extension changes. → build order table, Epics, DoD.
- Rejected by the gate, kept as is: English-only help (five-language help is established).

## Review notes — where a finding's wording and the code differ

- **G5/O10** said the round id appears "in the after-read's lines". `/api/usage` answers per-vendor totals with no lines
  (`UsageEndpoints.cs:118-131`, `UsageTotals.cs:143`), and `GET /api/reviews/{id}` carries no round (`ServerJsonContext.cs:185-196`).
  The per-key assertion is made on the server's `usage.jsonl` (`:237`), read on the box — folded in with that correction.
- **G3** asked for a deadline "that ends the query and releases the hold". The `/usage` call already has one (`usageBound`, 15 s,
  `AcpClient.cs:43`, `:195`), the pending call is ended by the ask's `finally` (`:127-129`), and the hold is a `using` around the
  whole ask (`AskAcpMode.cs:189-192`). E5.S2 names and reuses them and adds the code — nothing new to build for the deadline itself.
- **O7**'s "the foreign-kimi → no read test is trivial" is right as the plan was worded; the test is now about the turn the watch
  refuses (no read happens because no turn happens) and about the read never being a launch of its own.
- **O2**'s "say where `GaugeCodes` goes": `src_mcp` references it nowhere today, and `CoaiServer.csproj:34` already references
  `CoaiMcp.Runners` — so the move is one file and one `using`, not a shared-core change.
- **O5** counted "~15 bullets": the tail has 16 bullets of its own plus 7 from the Opus whole-feature review — all 23 are in the table.
- **C1**'s "denial alone does not trigger the server continuation" is right, and the same holds locally: the executor asks for a
  second launch only when the first answer did not parse (`ConcludedAsync`, `ReviewerExecutor.cs:615-628`), and only then does
  `FollowUp` look for the denial and the conversation id. The fixture needs all three; the plan says so.
- **C2** is confirmed verbatim: `uncaptured()` (`usage.ts:506-508`) tests `usageNote !== undefined`, and `lineCost` returns `UNKNOWN`
  on it (`:498-500`). The earlier revision's `usageNote` on the follow-up line would have hidden the measured difference.
