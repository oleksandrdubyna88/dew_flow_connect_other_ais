# PLAN — a cancelled review stops its round

> Status: **plan only, nothing implemented yet (2026-10-21).** Decided by the owner on 2026-10-21; depends on #904.
> Scope: `src_mcp/src/Tools.cs`, `src_mcp/src/Server/Rounds/RoundEngine.cs`, `src_mcp/src/Server/LiveRound.cs`,
> `src_mcp/src/Server/SessionStore.cs` (`RoundRecord`), `src_mcp/src/Store/RoundsDb.cs`, `src_mcp/src/Program.cs` (the serve loop's end),
> their tests, the live seam leg, `research/module_server.md`, `research/module_runners.md`, `research/module_tests.md`.

References are to `main` at `42eb3a95a` unless marked **#904** — the E3 branch `feat/tail-e3-ledger-metadata` at `7359ae933`, read with
`git show`; that pull request is open, so its lines may move before it merges. The E3 plan it carries is
`todo/PLAN_the_round_counter_tail_E3.md` on that branch (a path, not a link: it is not on `main` yet).

## 1. The symptom

When an MCP client cancels a `review_*` call — `notifications/cancelled` for its request id, or its own tool timeout — the round
does not notice. Measured on #904's live leg: a running `review_plan` answered nothing for 15 s after the cancel and wrote no ledger
line; its reviewers ran on to the round's own clock, on the person's subscriptions, for an answer nobody will read. The ledger's
local end causes `withdrawn` and `hostStopped` (#904, `src_mcp/runners/Reviewers/EndCauses.cs`) are therefore unreachable on a live
server: only `roundClock` and `unknown` can be written (#904 `research/module_server.md:2344`).

**The owner's decision (2026-10-21): a cancelled call stops its round**, and the turn's last ledger line says `withdrawn` — or
`hostStopped` when the host is the one stopping.

## 2. What is true today (each line opened, not guessed)

**The break is one layer: the tool lambdas.** Everything below them already carries a token.

| Step | Where | Token today |
|---|---|---|
| `review_plan` lambda | `Tools.cs:83-86` | none — calls `ReviewPlanAsync(…, Cadence(…))`, `ct` defaults |
| `review_code` lambda | `Tools.cs:125-127` | none |
| `review_document` lambda | `Tools.cs:178-182` | none |
| `review_feature` lambda | `Tools.cs:238-246` | none |
| `ask_human` lambda — the precedent | `Tools.cs:387-391` (comment `:384-386`) | `CancellationToken cancellationToken = default`, bound by the SDK per request, absent from the schema |
| `PanelService.ReviewPlanAsync` | `PanelService.cs:647` → `_engine.RunStageAsync(…, ct)` `:669` | passes `ct` |
| `PanelService.ReviewCodeAsync` | `PanelService.cs:759` → `:790` | passes `ct` |
| `PanelService.ReviewDocumentAsync` | `PanelService.cs:1018` (`ct` `:1026`) → `RunDocumentStageAsync` `:1199` → `:1216` | passes `ct` |
| `PanelService.ReviewFeatureAsync` | `PanelService.cs:1581` → `FeatureStage.ReviewAsync` (`Stages/FeatureStage.cs:64`) → `:109` | passes `ct` |
| `RoundEngine.RunStageAsync` | `RoundEngine.cs:207`; main links `ct` with the clock (`:287-289`); **#904** replaces that with `new RoundCancellation(_hostStopping, ct, budget)` (#904 `RoundEngine.cs:292`) | the round's token reaches `WarmRemoteRolesAsync` (`:300`), `MakeWork` (`:357`), `_scheduler.RunAllAsync` (`:413`) |

**#904's `RoundCancellation`** (`src_mcp/src/Server/Rounds/RoundCancellation.cs`) registers the caller's token (`:55`) and records the
first cause once, with its moment: `withdrawn`, or `hostStopped` when the host's token is already cancelled (`CallerCause`, `:83-84`);
the clock records `roundClock`; the host's token alone records nothing. `CauseAt(landedAt)` (`:72`) answers a cause only for an outcome
that landed after it fired. #904's `InputEndSignallingTransport` cancels the host token before the SDK cancels in-flight request tokens
(#904 `Program.cs:2176-2181`), so `hostStopped` is classifiable once a request token is wired.

**What a cancelled round would do if only the lambdas were wired — the trap.** The scheduler reports a cancelled reviewer instead of
throwing (`BoundedScheduler.cs:472-475`), so `RunAllAsync` returns and the round goes on to `RoundMachine.CompleteRound`
(`RoundEngine.cs:458`). With nobody answered that is `call_human` with `HumanGate = true` (`core/Rounds/RoundMachine.cs:403-415`), a
budget round spent (`RoundsRunThisStage + 1`, `:397`), and a notice to the person's phone (`RoundEngine.cs:596`) — a person's own
withdrawal turned into an enforced hold that `review_*` then refuses on. With some answered it is a verdict over a partial panel.

**Durable status.** The round is written `running` before the first CLI starts (`LiveRound.cs:256-257`) and closed by `Finish`
(`:214`). The only other exit from `running` is the startup sweep for a DEAD runner pid (`SessionStore.cs:659`, called from
`PanelService.cs:143-149`) → `interrupted` (`RoundRecord.Interrupted`, `SessionStore.cs:100`). An exception after `LiveRound` exists
falls to `catch (Exception e)` (`RoundEngine.cs:673`) and returns a sentence, leaving the record `running` while this server lives —
which `.agents/conventions/common/durable-status.md` rule 4 forbids. A `WorktreeException`/`ContextException` (`:669`) — what a git
child killed by the token can surface as — returns a sentence, not a cancellation.

**What already stops a remote job.** A token-killed shim tree ends `result.Cancelled` → `ReviewerExecutor.KilledAsync`
(`ReviewerExecutor.cs:979`) → `AbandonAsync` (`:817`, its own 10 s budget `:837`, never the round's token) → `RemoteRuntime.AbandonAsync`
(`RemoteRuntime.cs:252`) → `CancelAbandonedAsync` (`:277`) reads the shim's job file and sends `DELETE /api/reviews/{id}` (`:309-322`)
→ `ReviewEndpoints.cs:147` → `JobStore.Cancel` files `EndCauses.Withdrawn` (`src_server/src/Jobs/JobStore.cs:389`).

**The session claim** (`SessionClaim.cs:44`, taken at `RoundEngine.cs:224`) is an OS file lock held for the whole round; a second
mutating call is refused with `SessionClaim.Busy` (`SessionClaim.cs:62`) — "try once, then refuse", no waiting.

## 3. The goal — what must be true

- **G1** A cancelled `review_plan`/`review_code`/`review_document`/`review_feature` stops its round: every reviewer's process tree is
  killed (`ProcessLauncher.EndItAsync`, `ProcessLauncher.cs:704-711`) and every remote job gets its `DELETE`.
- **G2** Each cut turn's LAST ledger line carries `endCause: withdrawn` (or `hostStopped`); a turn that landed before the cancel keeps
  its own outcome and no cause (#904's `CauseAt`).
- **G3** The round is recorded as cancelled — in the session file and in `coai.db`'s `rounds` row — and never left `running`.
- **G4** The gate is unmoved: no verdict, no budget round spent, no `call_human`, no phone notice, no feature second-round ground.
- **G5** The reply follows the protocol for a cancelled request: none is sent (MCP: the receiver SHOULD NOT respond).
- **G6** A round that already finished is untouched; a cancel that races the round's end has one deterministic answer (D2).
- **G7** A second caller of the same session meets `Busy` until the withdrawal is written, then starts the next round normally.

## 4. Decisions

- **D1 — the four lambdas take the SDK's token**, exactly as `ask_human` does (`Tools.cs:387-391`): a trailing
  `CancellationToken cancellationToken = default`, passed to the `PanelService` call. No schema change. `consult` and
  `ask_consultants` (`Tools.cs:492`, `:546`) are NOT in scope — a separate decision.
- **D2 — which round is withdrawn.** The round is withdrawn when `cancellation.CauseAt(lastLanded)` is `withdrawn` or `hostStopped`,
  where `lastLanded` is the latest `progress.LandedAt` of the round's outcomes: some reviewer was CUT by the caller. If every outcome
  landed before the cause fired, the round completes as today — its answers are real and paid for, and `status` re-orients a caller
  whose reply was dropped. `roundClock` and `unknown` complete as today (the deadline path is unchanged). A cancel during setup
  (before `LiveRound`, `RoundEngine.cs:397`) records nothing: no number, no line, no row.
- **D3 — the record.** New `RoundRecord.Cancelled = "cancelled"` status (beside `Running`/`Done`/`Interrupted`, `SessionStore.cs:98-100`);
  `Verdict` = the cause word; `CompletedUtc`; usage summed exactly as `Finish` does (`LiveRound.cs:229-231`) — a withdrawn round still
  cost money; a `Note` sentence. A new `LiveRound.Withdraw(cause, results)` writes it. The session saved is `loaded`
  (`RoundEngine.cs:249` — the session as read under the claim, the one a refusal leaves) plus this record: the state machine never
  sees the round, the journal keeps its number (`RoundNumber.Next` counts it, `RoundNumber.cs:26`), and the next round is `n+1`, so
  the `(session, stage, number)` upsert never overwrites it. Projected through `_projection.Write` (`RoundEngine.cs:618`) with no
  findings. The extension needs no change: anything but `running` is ended (`src_vs_code/src/rounds.ts:309-311`).
- **D4 — what is never cancelled with the call.** The tree-kill wait (`EndItAsync` waits with `CancellationToken.None`), the remote
  `AbandonAsync` (own budget), `UsageLedger.Record` (`UsageLedger.cs:356`, no token), `LiveRound.Withdraw`, `SessionStore.Save`
  (`SessionStore.cs:551`, no token), the `coai.db` projection, the worktree lease's disposal, and the cadence record's write
  (`CadenceStore.Update`, `Cadence/CadenceStore.cs:62`, no token; reached from `CadenceDesk.PrepareAsync`, `CadenceDesk.cs:126`). The
  consultation files are not touched by a round at all. Pinned by test, not just stated.
- **D5 — the reply.** After the record is written, `RunStageAsync` throws `OperationCanceledException` carrying the caller's token, so
  the SDK sees the request cancelled. What SDK 2.2.0 (`Directory.Packages.props:14`) then sends is **unmeasured** — S1 pins it. A
  `catch (OperationCanceledException)` and a cause check before the `WorktreeException`/`ContextException` and catch-all arms
  (`RoundEngine.cs:669`, `:673`): whatever exception a cancelled round surfaces, a recorded `withdrawn`/`hostStopped` makes it a
  cancellation, never "the round failed".
- **D6 — the host stopping.** `hostStopped` follows the same road. The serve loop's end waits on its tasks for
  `StartingHost.EndBudget` — **1 s** (`StartingHost.cs:41`; #904 `Program.cs:2214`) — which the tree kill plus a remote `DELETE` can
  exceed. In-flight rounds are registered and awaited in that `finally` under their own bound (proposed 5 s, set from S4's
  measurement); past it the startup sweep's `interrupted` is the honest record, and the ledger keeps whatever lines landed.
- **D7 — the Team server.** Nothing new on the server: the token kills the shim tree and the existing `DELETE` withdraws the job. The
  server files `withdrawn` even for a local `hostStopped` (it cannot tell) — stated in `module_runners.md`, not "fixed". A cancel
  before the server accepted the job leaves no job file, so the job ends by the server's own queue/abandon expiry — stated.
- **D8 — a second caller.** Unchanged mechanism: `Busy` while the withdrawal tears down (bounded by the tree kill and the 10 s
  `AbandonAsync`); `status` reads `running`, then `cancelled`; the next `review_*` begins from the unmoved state.

## 5. Interactions

- **Round budget and deadline.** `RoundDeadline.For` (`RoundEngine.cs:287`) is unchanged; a cancel and the clock are ordered by #904's
  first-fire rule, so a clock that fired first is a deadline round as today (`WhatEndedIt`, #904 `RoundEngine.cs:845-846`).
- **`NoBudget`** (`RemoteAsk.NoBudget = 79`, `runners/Reviewers/RemoteAsk.cs:86`; story 4.3 of
  [PLAN_every_round_is_counted.md](../research/PLAN_every_round_is_counted.md)) — a server that ended a job because the reviewer
  could no longer wait. An outcome that landed as `NoBudget` before the cancel keeps it; it is never re-filed as `withdrawn`.
- **The stand-down (#485)** (`StandDown.cs:16`, wired at `RoundEngine.cs:431`): a cut cloud reviewer counts as failed, so it never
  makes the round "quiet"; a local row already `StoodDown` keeps that status inside a withdrawn round.
- **Retries.** The rate-limit ladder ends a turn cancelled while WAITING as the refusal it last saw (`BoundedScheduler.cs:636-641`) —
  so that line carries no `withdrawn`, and no retry launches after the cancel; pinned as a stated limit. The round-level retry of
  [PLAN_a_failed_round_can_be_retried.md](../research/PLAN_a_failed_round_can_be_retried.md) is not consumed: D3's state is `loaded`.
- **Feature review (D23)** and **the security lane**: neither ground nor lane budget moves — the round never reaches
  `CompleteRound`/`CompleteFeatureRound` (`RoundMachine.cs:387`, `:457`).

## 6. Risks — the two most expensive

1. **Half-wired: the tokens reach the reviewers, the round completes as a failure.** The lambdas alone produce exactly that (§2,
   *the trap*): every withdrawal becomes `call_human`, a hold `review_*` then refuses on until a person answers, a phone notice, and a
   spent budget round. It looks like a vendor outage, the person is paged for their own click, and the gate is blocked. **Guard:**
   S1's lambdas do not merge without S2 — one pull request; S2's RED test asserts no `HumanGate`, no notice, `RoundsRunThisStage`
   unchanged.
2. **A cancel racing the round's own end leaves it `running`, or files a finished round as withdrawn.** Two orderings: the cause
   fires while the record is being finalised (an `OperationCanceledException` from a token-taking step after `LiveRound` exists falls
   to the catch-all and leaves `running` for the server's life — no sweep reaches a live pid); or every reviewer answered and the
   cancel lands a moment later, throwing away a paid, complete review. **Guard:** D2's single decision point (`CauseAt(lastLanded)`),
   all finalisation on `CancellationToken.None` (D4), and S3's interleaving tests with a gated fake reviewer.

Lesser: SDK 2.2.0 may still send a response after an `OperationCanceledException` (harmless to a client that ignores it; S1 records
which); a host stop past the bound leaves `interrupted` rather than `hostStopped` (D6, stated).

## 7. Build order

1. **#904 merges** (`RoundCancellation`, the host token, `EndCauseOf`, `InputEndSignallingTransport`). Nothing here starts before it.
2. S1 + S2 together, RED first (one pull request — risk 1).
3. S3 (the races and what is never cancelled).
4. S4 (the host's stop, the remote row, the live seam leg).
5. Docs, then the coai-mcp release (§10).

## 8. Test plan

- **Unit/integration (C#, `src_mcp/tests`)**, through `PanelService` with a fake reviewer that blocks until its token fires (the
  `ARoundIsMintedAKeyTests.cs` shape #904's `ALocalCancellationCarriesItsSourceTests.cs` uses) — scenarios per story below, each
  watched RED for the reason given. Run as the test executable, never `dotnet test`.
- **Live**, extending #904's seam leg (`src_vs_code/scripts/seam-ledger-fields.mjs` via `npm run test:seam`): the REAL `coai-mcp`
  over stdio against `FakeCli`; (a) `review_plan` started, then `notifications/cancelled` for its request id: no response within 5 s
  (or the one S1 recorded), the `FakeCli` tree gone, the turn's last `usage.jsonl` line `endCause: withdrawn` read through the
  extension's own `parseUsageLine`, the session file's round `cancelled`/`withdrawn`, the `coai.db` row the same; then a second
  `review_plan` on the session starts round `n+1`. (b) the same, ending stdin instead: `hostStopped`, or `interrupted` after the next
  start when past D6's bound. No vendor is called.

## 9. Docs to update

`research/module_server.md` (the cancellation road, D2–D6; replace #904's "measured limit" paragraph), `research/module_runners.md`
(`EndCauses`: `withdrawn`/`hostStopped` now written by a live server; D7's server-side `withdrawn`), `research/module_tests.md`
(the new tests and the seam leg), `research/architecture.md` if the serve loop's end changes shape (D6), the `review_*` tool
descriptions in `Tools.cs` (one sentence: a cancelled call stops its round and is recorded `cancelled`).

## 10. Release

A `feat(mcp)` change — coai-mcp, the minor release after the one carrying #904 (release-please names the number). No Team server
release (D7) and no extension release (D3). `POST_DEPLOY.md`: nothing new to check.

## 11. Definition of Done

- [ ] #904 merged first; every reference above re-read against the merged `main` and corrected where it moved.
- [ ] The four review lambdas take the SDK token (S1); `ask_human` unchanged.
- [ ] A withdrawn round: reviewers' trees killed, remote jobs `DELETE`d, last ledger line per cut turn `withdrawn`/`hostStopped`.
- [ ] Recorded `cancelled` in the session file and `coai.db`; never `running` after the call returns; gate state, budget, holds and
      notices unmoved.
- [ ] The reply behaviour measured (S1) and recorded in `module_server.md`.
- [ ] Each story's tests watched RED for the stated reason, then green; the whole suite green; the live seam leg green.
- [ ] Docs (§9) updated; this plan promoted with `/promote-plan` when the release ships.

## Epics

One epic. Cornerstones: 1 of 1 — S2 (the record a withdrawal leaves; it decides what the gate, the database and every reader see).
The stories live here rather than in a `_E1` document: the epic is small enough that one document is the honest size.

### E1 — a cancelled review stops its round (depends on #904)

Done when §11 holds. Stories, test-first:

**S1 — the token reaches the round.** The four lambdas take `CancellationToken cancellationToken = default` (D1); a characterization
test of SDK 2.2.0: a tool cancelled by `notifications/cancelled` — what it sends after the handler returns normally, and after it
throws `OperationCanceledException`.
RED: `TheReviewTools_TakeTheRequestsToken` (each tool's delegate signature has a `CancellationToken` parameter, read reflectively
from `Tools.All`) — fails today: no review lambda declares one. `ACancelledReviewPlan_CancelsItsRound` (in-memory transport, a fake
reviewer blocking on its token) — fails today: the reviewer is never cancelled, the test's 5 s wait elapses.

**S2 — CORNERSTONE: the record a withdrawal leaves** (D2, D3, D5). `LiveRound.Withdraw`, `RoundRecord.Cancelled`, the `loaded`
save, the projection row, the `OperationCanceledException` out, the catch order.
RED: `AWithdrawnRound_IsRecordedCancelled_AndMovesNoGate` — after S1 alone it fails with the real symptom: the record's verdict is
`call_human`, `HumanGate` is true and `RoundsRunThisStage` is 1. `AWithdrawnRound_NotifiesNobody` — fails: one escalation notice.
`AWithdrawnRound_SpendsItsNumber` — the next round is `n+1` and the `coai.db` row of `n` is `cancelled`; fails: `n` is `done`.
`ACancelDuringSetup_RecordsNothing` — green before and after (a guard: no number, no line, no row).

**S3 — the races, and what is never cancelled** (D2, D4).
RED: `EveryReviewerLandedBeforeTheCancel_TheRoundCompletes` (a gated fake: both answer, then the token fires before `RunAllAsync`
returns) — fails if S2 classified by "the cause fired at all": the round is `cancelled`. `ACancelDuringFinalising_NeverLeavesRunning`
(a fake `SessionStore`/projection seam that cancels the caller mid-write) — fails with `Status == "running"` when a finalising step
takes the token. `ACancelledContextRead_IsACancellation_NotAFailure` (the git read's token fires inside `CollectAsync`) — fails today:
the reply is "the round failed …". `TheCadenceRecord_IsWrittenWhole_WhenTheCallIsCancelled` — green guard.

**S4 — the host's stop, the remote row, the live leg** (D6, D7). The in-flight round registry awaited in the serve loop's `finally`
under its own bound; the seam leg (§8).
RED: `AStoppingHost_WritesHostStopped_BeforeTheProcessEnds` — fails with `interrupted` (or still `running`) because the loop's end
waits only 1 s for its own tasks. `AWithdrawnRemoteRow_SendsItsDelete` (a fake Team server counting `DELETE /api/reviews/{id}`) —
fails today: zero `DELETE`s, because no token ever kills the shim. The live leg — fails today: no response and no `withdrawn` line.
