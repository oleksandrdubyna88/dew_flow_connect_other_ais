# PLAN — a cancelled review stops its round

> Status: **plan only, nothing implemented yet (2026-10-21).** Decided by the owner on 2026-10-21; depends on #904.
> Plan gate session 0c31bf4f, round 1: 12 findings accepted; the ten relayed for this revision are folded the same day (§12).
> Scope: `src_mcp/src/Tools.cs`, `src_mcp/src/Server/Rounds/RoundEngine.cs`, `src_mcp/src/Server/LiveRound.cs`,
> `src_mcp/src/Server/SessionStore.cs` (`RoundRecord`), `src_mcp/runners/Reviewers/BoundedScheduler.cs`, the journal readers of §4 D9,
> `src_mcp/src/Program.cs` (the serve loop's end), `src_vs_code/src/roundsLog.ts`, their tests, the live seam leg, `research/module_*.md`.

References are to `main` at `42eb3a95a` unless marked **#904** — the E3 branch `feat/tail-e3-ledger-metadata` at `7359ae933`, read with
`git show`; that pull request is open, so its lines may move before it merges. Its plan is `todo/PLAN_the_round_counter_tail_E3.md` on that
branch (a path, not a link: it is not on `main` yet).

## 1. The symptom, and what this plan does NOT cover

When an MCP client cancels a `review_*` call with `notifications/cancelled`, the round does not notice. Measured on #904's live leg: a
running `review_plan` answered nothing for 15 s after the cancel and wrote no ledger line; its reviewers ran on to the round's clock, on the
person's subscriptions, for an answer nobody reads. The ledger's local end causes `withdrawn` and `hostStopped` (#904
`runners/Reviewers/EndCauses.cs`) are unreachable on a live server (#904 `research/module_server.md:2344`).

**The owner's decision (2026-10-21): a cancelled call stops its round**, recorded `withdrawn` — or `hostStopped` when the host is stopping.

**Covered:** a cancel delivered as `notifications/cancelled`, and the host stopping (a client closing stdin; #904's
`InputEndSignallingTransport` turns that into the host token firing before the SDK cancels in-flight requests, #904 `Program.cs:2176-2181`).
**Not covered, unchanged:** a client whose own tool timeout fires WITHOUT sending the notification — the server is never told, so the round
runs to its clock exactly as today. **`consult` and `ask_consultants`** (`Tools.cs:492`, `:546`) take no token either and stay out: whether a
cancelled consultation stops is a separate decision (its records, budgets and resumable states are a different machine). A person who cancels
one today sees it run on to its own deadline, its record written as usual, and the cancelled call answered with nothing.

## 2. What is true today (each line opened, not guessed)

**The break is one layer: the tool lambdas.** Everything below them already carries a token.

| Step | Where | Token today |
|---|---|---|
| `review_plan` / `review_code` / `review_document` / `review_feature` lambdas | `Tools.cs:83-86`, `:125-127`, `:178-182`, `:238-246` | none |
| `ask_human` lambda — the precedent | `Tools.cs:387-391` (comment `:384-386`) | `CancellationToken cancellationToken = default`, bound by the SDK per request, absent from the schema |
| `PanelService.Review{Plan,Code,Document,Feature}Async` | `PanelService.cs:647`→`:669`; `:759`→`:790`; `:1018`→`:1199`→`:1216`; `:1581`→`Stages/FeatureStage.cs:64`→`:109` | pass `ct` to `RunStageAsync` |
| `RoundEngine.RunStageAsync` | `RoundEngine.cs:207`; main links `ct` with the clock (`:287-289`); **#904**: `new RoundCancellation(_hostStopping, ct, budget)` (#904 `RoundEngine.cs:292`) | reaches `WarmRemoteRolesAsync` (`:300`), `MakeWork` (`:357`), `_scheduler.RunAllAsync` (`:413`) |

**#904's `RoundCancellation`** (`Server/Rounds/RoundCancellation.cs`) registers the caller's token (`:55`) and records the first cause once,
with its moment: `withdrawn`, or `hostStopped` when the host's token is already cancelled (`CallerCause`, `:83-84`); the clock records
`roundClock`; the host token alone records nothing. `CauseAt(landedAt)` (`:72`) names a cause only for an outcome that landed after it fired.

**How a CUT turn ends** — four shapes, three outcome types, none timestamped at the cut:
- killed while running, launcher reports cancelled → `ReviewerExecutor.KilledAsync` (`ReviewerExecutor.cs:979`) → `ReviewerOutcome.Cancelled`;
- killed while running, the launch throws → `ReviewerExecutor.cs:957-966` (abandon, rethrow) → `BoundedScheduler.cs:472-475` → `NotStarted`
  ("was cancelled while it was running", `Abandoned`, `:488-503`);
- still queued for a slot → `Took` false (`:430-441`) → `NotStarted` ("was still queued");
- waiting on the rate-limit ladder → `WaitedOut` false (`:636-641`) → the refusal it last saw (`RateLimited`).
`ReviewerProgress.LandedAt` (#904 `BoundedScheduler.cs:79`) is a default initializer — the moment the terminal REPORT is built, i.e. after the
kill and any 10 s remote abandon, never the cut itself; and `RunAllAsync`'s results carry no `LandedAt` at all, only the progress callback does.
#904 writes `endCause` only for `Cancelled` (#904 `RoundEngine.cs:853-854`).

**The trap if only the lambdas are wired.** The scheduler reports cut reviewers instead of throwing, so the round reaches
`RoundMachine.CompleteRound` (`RoundEngine.cs:458`): with nobody answered that is `call_human` with `HumanGate = true`
(`core/Rounds/RoundMachine.cs:403-415`), a budget round spent (`:397`), a phone notice (`RoundEngine.cs:596`); with some answered, a verdict
over a partial panel.

**Durable status.** The round is written `running` before the first CLI starts (`LiveRound.cs:256-257`), closed by `Finish` (`:214`), and
otherwise swept to `interrupted` only for a DEAD runner pid at start (`SessionStore.cs:659`, from `PanelService.cs:143-149`). An exception
after `LiveRound` exists falls to `catch (Exception e)` (`RoundEngine.cs:673`) and leaves `running` while this server lives
(`.agents/conventions/common/durable-status.md` rule 4 forbids it); a `WorktreeException`/`ContextException` (`:669`) returns a sentence.

**Remote rows.** A token-killed shim → `KilledAsync` → `AbandonAsync` (`ReviewerExecutor.cs:817`; own `AbandonBudget` of 10 s, `:837`) →
`RemoteRuntime.AbandonAsync` (`RemoteRuntime.cs:252`) → `CancelAbandonedAsync` (`:277`) → `DELETE /api/reviews/{id}` (`:309-322`) →
`ReviewEndpoints.cs:147` → `JobStore.Cancel` files `withdrawn` (`src_server/src/Jobs/JobStore.cs:389`). An undelivered DELETE keeps the job
file (`RemoteRuntime.cs:290-297`), and nothing retries it later (`AbandonAsync` is `CancelAbandonedAsync`'s only caller).

**The session claim** (`SessionClaim.cs:44`, taken at `RoundEngine.cs:224`) is an OS lock held for the round; a second call is refused with
`SessionClaim.Busy` (`SessionClaim.cs:62`).

## 3. The goal — what must be true

- **G1** A cancelled `review_*` stops its round: every reviewer tree killed (`ProcessLauncher.EndItAsync`, `ProcessLauncher.cs:704-711`),
  every accepted remote job sent its `DELETE` (best effort — D7).
- **G2** Every turn the cancel ENDED carries `endCause: withdrawn` (or `hostStopped`) on its last ledger line, whatever its outcome type —
  `Cancelled`, `NotStarted`, or a ladder `RateLimited` whose refusal stays the line's outcome. A turn that ended for its own reason keeps no cause.
- **G3** The round is recorded `cancelled` in the session file and `coai.db`, and never reads `running` once the cause fired — including when
  the write fails (D10) or the host is killed (D6).
- **G4** The gate is unmoved and NEVER judges a partial panel: no verdict, no budget round, no `call_human`, no notice, no feature ground.
- **G5** The reply follows the protocol: no response for the cancelled request — accepted only on the seam leg's observation (D5).
- **G6** A round that already finished is untouched; a cancel racing the round's end has one deterministic answer (D2).
- **G7** A second caller of the same session meets `Busy` until the withdrawal is written, then starts the next round normally.

## 4. Decisions

- **D1 — the four review lambdas take the SDK's token**, exactly as `ask_human` does: a trailing `CancellationToken cancellationToken = default`
  passed to `PanelService`. No schema change. `consult`/`ask_consultants`: §1.
- **D2 — which round is withdrawn.** Not by `CauseAt(lastLanded)` alone (§2: `LandedAt` is a report time and results carry none). The
  scheduler MARKS every turn it ended because the round token fired — at the four sites of §2 — with an `EndedByRoundToken` flag on the outcome.
  The round is withdrawn iff the recorded first cause is `withdrawn`/`hostStopped` and fired before `RunAllAsync` returned, and either at
  least one turn carries the mark, or the round had no reviewer at all (a lane-only round, `RoundMachine.AdmittedOnlyForTheLane`). Every
  reviewer landing before the fire → the round completes as today (real, paid answers; `status` re-orients a caller whose reply was
  dropped). `roundClock`/`unknown` complete as today. The mark is also what G2's cause is written on (widening #904's `Cancelled`-only rule).
  **A cancel during setup** (before `LiveRound`, `RoundEngine.cs:397`): nothing is recorded — no record, no number, no ledger line (nothing
  launched) — one log line `withdrawn during setup`; claim, worktree and scratch go with their `using`s. Between `LiveRound` and
  `RunAllAsync` there is no await, so a cancel there reaches `RunAllAsync` already fired: every reviewer is cut, marked, and withdrawn.
- **D3 — the record.** New `RoundRecord.Cancelled = "cancelled"` (beside `SessionStore.cs:98-100`); `Verdict` = the cause word;
  `CompletedUtc`; usage summed as `Finish` sums it (`LiveRound.cs:229-231`); a `Note`. Written by a new `LiveRound.Withdraw`. The session
  saved is `loaded` (`RoundEngine.cs:249`, the session as read under the claim, with any person's answer already applied at `:239`) plus this
  record: the state machine never sees the round; the journal keeps its number (`RoundNumber.Next`, `RoundNumber.cs:26`), so the next round is
  `n+1` and the `(session, stage, number)` upsert never overwrites it. Projected through `_projection.Write` (`RoundEngine.cs:618`), no findings.
- **D4 — what is never cancelled with the call:** the tree-kill wait (`EndItAsync`, `CancellationToken.None`), `AbandonAsync` (own budget),
  `UsageLedger.Record` (`UsageLedger.cs:356`), `LiveRound.Withdraw`, `SessionStore.Save` (`SessionStore.cs:551`), the projection, the worktree
  lease's disposal, and the cadence record (`CadenceStore.Update`, `Cadence/CadenceStore.cs:62`, reached from `CadenceDesk.cs:126`). Pinned by test.
- **D5 — the reply.** After the record is written, `RunStageAsync` throws `OperationCanceledException` with the caller's token; a
  `catch (OperationCanceledException)` plus a cause check run BEFORE the `WorktreeException`/`ContextException` and catch-all arms
  (`RoundEngine.cs:669`, `:673`), so whatever a cancelled round surfaces is a cancellation, never "the round failed". What SDK 2.2.0
  (`Directory.Packages.props:14`) then sends is **unmeasured**. **Acceptance:** the seam leg sees no JSON-RPC message with the cancelled
  id within 5 s of the record being written. **Named fallback** if S1 shows the SDK responds: #904's `InputEndSignallingTransport` — its pump
  sees every inbound message (#904 `:83`) and `SendMessageAsync` is a pass-through (#904 `:51-52`) — remembers the ids of inbound
  `notifications/cancelled` (bounded, retired on use) and drops an outbound response or error for them, logged. Unit-tested and seam-tested.
- **D6 — the host stopping, and the order of teardown.** When the caller's cause fires, the engine (awaiting `RunAllAsync` together with a
  `Fired` task the cancellation completes — never file IO inside a token callback) writes a PROVISIONAL `cancelled` record at once, with the
  usage so far, BEFORE any reviewer's teardown or remote `AbandonAsync` is awaited; the final `Withdraw` rewrites it with the full usage (or
  `Finish` rewrites it, if D2 finds every reviewer landed first). The serve loop's end, which today waits `StartingHost.EndBudget` = **1 s**
  (`StartingHost.cs:41`; #904 `Program.cs:2214`), also awaits registered in-flight rounds under a bound DERIVED from
  `ReviewerExecutor.AbandonBudget` (10 s) + the tree-kill time S4 measures + one save — not a separate literal. **Exceeded anyway** (or the
  client kills the process sooner, which no bound prevents): the record already reads `cancelled`/`hostStopped`, so no sweep is needed;
  turns that had not landed lose their ledger lines (stated); remote jobs whose DELETE never went out end by D7's server expiry.
- **D7 — the Team server.** Nothing new server-side. The DELETE is best effort, and the guarantee when it fails (server unreachable, signed
  out, 5xx) is the server's own: a job nobody polls ends after `QueuedAbandonedAfter` = 3 min queued or `RunningAbandonedAfter` = 10 min
  running (`src_server/src/Jobs/JobRecord.cs:223`, `:236`), and a running job at its run deadline (its budget + `RunSlack` 30 s, `:200`) —
  filed `abandoned`/`backstop`, not `withdrawn`. The server files `withdrawn` even for a local `hostStopped` (it cannot tell). A cancel
  before the server accepted the job leaves no job file: the same expiry ends it.
- **D8 — a second caller.** `Busy` while the withdrawal tears down (bounded by D6); `status` reads `cancelled` from the provisional write on;
  the next `review_*` begins from the unmoved state.
- **D9 — every reader of `RoundRecord.Status`/`Verdict`, checked against `cancelled`/`withdrawn`/`hostStopped`:**

| Reader | What a cancelled round would do | Change |
|---|---|---|
| `PanelService.cs:957-960` `LastCodeRound`, used by `again` (`:926`) | a withdrawn `again` round's sha makes the retry "nothing since" — refused | skip `Cancelled` |
| `Stages/FeatureSecondRound.cs:73`, `Stages/FeatureStage.cs:188` (last feature round) | the second-round ground read off `withdrawn` | skip `Cancelled` |
| `Cadence/CadenceDesk.cs:385` (`Reconciled`, last code round) | reconciles against a round that judged nothing | skip `Cancelled` |
| `Cadence/CadenceDesk.cs:427` (first code round of an epic) | counts it | none — as `interrupted` already counts, deliberately (`RoundRecord.PlanKey` remarks) |
| `core/Authors/Windows.cs:22` `IsRecorded` (excludes `interrupted`) | a window boundary at a round that ran nothing | exclude `Cancelled` |
| `Rounds/CurrentAnswer.cs:100` (answer newer than the last round) | an answer the cancelled round applied reads stale | none — `loaded` saved it applied; skipping would apply it twice; pinned |
| `PanelService.cs:1768` `Rounds[^1].Verdict` on resolve | — | none: a begin is refused while `AwaitingResolve` (`RoundMachine.cs:88`), so a cancelled record is never last at a resolve; pinned |
| `DeltaPredecessor.cs:46`, `Backfill/BackfillDecision.cs:62`, `AttributionMapping.cs:103`, `Windows.cs:28`, `SessionStore.cs:770` | read `Done`/`Running` only | none |
| `RoundsDb.cs:855-881`; `RoundsQuery.Read` (`--log`, `Program.cs:772`), `FindingsOf`/`FindingsOfMany` (`--findings`, `--findings-many`, `:1661`, `:1635`) | the words pass through as text | a render test |
| `src_vs_code/src/roundsLog.ts:612-630` `statusOf` | falls through to **`done`** (or `awaiting`) — wrong | a `cancelled` status; exercised through the `bundledPage.test.ts` harness on a cancelled-round fixture |
| `rounds.ts:309-311` `isRunning`, `:342` `elapsed` | ended; its real duration shown | none |
| Team server | reads no `RoundRecord` (checked: no reader under `src_server/src`) | none |

- **D10 — a `Withdraw` save that fails** (`SessionStore.Save` throws `SessionStoreException` on IO, `SessionStore.cs:551`): retry once; if that
  fails, log the IO error and keep the ending in an in-process map of unsaved endings that `status` and every later claim-holder of that
  session apply before reading, retried by each of them and at the serve loop's end. After a restart the pid is dead, so the sweep files it
  `interrupted` — never `running` either way.

## 5. Interactions

- **Round budget and deadline.** `RoundDeadline.For` (`RoundEngine.cs:287`) unchanged; a clock that fired first is a deadline round as today
  (`WhatEndedIt`, #904 `RoundEngine.cs:845-846`).
- **`NoBudget`** (`RemoteAsk.NoBudget = 79`, `runners/Reviewers/RemoteAsk.cs:86`; story 4.3 of
  [PLAN_every_round_is_counted.md](../research/PLAN_every_round_is_counted.md)): an outcome that landed as `NoBudget` before the cancel
  carries no mark and keeps its word.
- **The stand-down (#485)** (`StandDown.cs:16`, `RoundEngine.cs:431`): a cut cloud reviewer is a failure, never "quiet"; a local row already
  `StoodDown` keeps that status, unmarked, inside a withdrawn round.
- **Retries.** A ladder-cut turn launches no retry after the cancel and is marked (G2: its line says `withdrawn` with `RateLimited` as the
  outcome — one behaviour, no carve-out). The round-level retry of
  [PLAN_a_failed_round_can_be_retried.md](../research/PLAN_a_failed_round_can_be_retried.md) is untouched: D3's state is `loaded`.
- **Feature review (D23)** and **the security lane**: no ground and no lane budget moves (`CompleteRound`/`CompleteFeatureRound`,
  `RoundMachine.cs:387`, `:457`, are never reached).

## 6. Risks — the two most expensive

1. **Half-wired: the tokens reach the reviewers, the round completes as a failure** (§2, *the trap*): every withdrawal becomes `call_human`
   — an enforced hold that blocks `review_*` until a person answers — a phone notice for the person's own click, a spent budget round, or a
   verdict over a partial panel. **Guard:** S1 does not merge without S2 (one pull request); S2's RED asserts no hold, no notice, no budget.
2. **A cancel racing the round's own end.** It fires while the record is finalised and an exception leaves `running` for the server's life
   (no sweep reaches a live pid); or every reviewer answered and the cancel lands a moment later, throwing away a paid review; or a cut turn
   is misclassified because its report time is not its cut time. **Guard:** D2's mark (not timestamps), D6's provisional write, D4's
   `CancellationToken.None`, D10's fallback, and S3's interleavings with a gated fake reviewer.

Lesser: the SDK may answer a cancelled request (D5's fallback); a host killed before the provisional write leaves `interrupted` after the
next start (D6); a failed DELETE costs up to the server's expiry (D7).

## 7. Build order

1. **#904 merges.** Nothing here starts before it; every reference above is re-read on the merged `main`.
2. S1 + S2 in one pull request, RED first (risk 1). 3. S3. 4. S4. 5. Docs, then the releases (§10).

## 8. Test plan

- **C# (`src_mcp/tests`)**, through `PanelService` with a fake reviewer that blocks until its token fires (the `ARoundIsMintedAKeyTests.cs`
  shape #904's `ALocalCancellationCarriesItsSourceTests.cs` uses); each story's scenarios watched RED for the stated reason. The test
  executable, never `dotnet test`.
- **Extension:** `roundsLog.ts` `statusOf` unit test, and the rounds-log page through `bundledPage.test.ts` on a cancelled-round fixture.
- **Live**, extending #904's seam leg (`src_vs_code/scripts/seam-ledger-fields.mjs`, `npm run test:seam`): the REAL `coai-mcp` over stdio
  against `FakeCli`. (a) `review_plan`, then `notifications/cancelled` for its id: no message with that id within 5 s (D5), the `FakeCli`
  tree gone, the cut turn's last `usage.jsonl` line `endCause: withdrawn` read through the extension's `parseUsageLine`, the session file's
  round `cancelled`/`withdrawn`, the `coai.db` row the same; then a second `review_plan` starts round `n+1`. (b) the same, ending stdin:
  `hostStopped` already in the session file when the process exits. No vendor is called.

## 9. Docs to update

`research/module_server.md` (D2–D6, D10; replaces #904's "measured limit" paragraph), `research/module_runners.md` (`EndCauses` written live;
the mark; D7), `research/module_extension.md` (the `cancelled` row), `research/module_tests.md`, `research/architecture.md` if the serve
loop's end changes shape (D6), and one sentence in each `review_*` tool description in `Tools.cs`.

## 10. Release

A `feat(mcp)` change in **coai-mcp** (the minor after the release carrying #904), and an **extension** release for `roundsLog.ts` (D9) — a
change to what the extension shows ships one. No Team server release (D7). `POST_DEPLOY.md`: nothing new.

## 11. Definition of Done

- [ ] #904 merged first; every reference re-read against the merged `main` and corrected where it moved.
- [ ] The four review lambdas take the SDK token; `ask_human` unchanged; `consult`/`ask_consultants` untouched and said so.
- [ ] A withdrawn round: trees killed, accepted remote jobs `DELETE`d, every cut turn's last line `withdrawn`/`hostStopped`.
- [ ] Recorded `cancelled` in the session file and `coai.db` from the moment the cause fires; never `running` after it, even when the save fails.
- [ ] Gate state, budget, holds and notices unmoved; never a verdict over a partial panel; every D9 reader handled.
- [ ] D5's acceptance observed on the seam leg (or its fallback shipped and observed); the SDK's behaviour recorded in `module_server.md`.
- [ ] Each story's tests watched RED for the stated reason, then green; the whole suite and the seam leg green.
- [ ] Docs (§9) updated; this plan promoted with `/promote-plan` when both releases ship.

## 12. What the reviews changed (2026-10-21)

Plan gate session 0c31bf4f, round 1 — every finding verified against the code before it was folded:
1. **D2 no longer classifies by `CauseAt(lastLanded)`.** Verified: `LandedAt` is the report's construction time (#904 `BoundedScheduler.cs:79`),
   after the kill and abandon, and `RunAllAsync`'s results carry none. Also found: a cut turn is not always `Cancelled` — the throw path and the
   queue end `NotStarted`, the ladder `RateLimited` (§2). Hence the scheduler's mark; S3 RED `SomeLanded_SomeCutByCancel_IsWithdrawn`.
2. **No reviewer landed / setup**: D2 now states both — the lane-only round and the cut-before-launch round are withdrawn; a setup cancel records nothing.
3. **G2 vs §5**: one behaviour — the ladder-cut turn's line says `withdrawn`, its refusal kept as the outcome; the carve-out is gone.
4. **G5**: an acceptance criterion on the seam leg and a named fallback in `InputEndSignallingTransport` (D5).
5. **Remote DELETE**: D7 names the server's expiry with file:line; S4 tests a failed DELETE.
6. **Host-stop bound**: derived from the 10 s `AbandonBudget` (verified, `ReviewerExecutor.cs:837`) + kill time; the provisional record is
   written before any abandon is awaited; the exceeded case stated (D6).
7. **`consult`/`ask_consultants`** out of scope with the reason and what the person sees (§1).
8. **Every reader enumerated (D9).** Contradicted the first draft twice: the rounds-log page's `statusOf` would show a cancelled round as
   `done`, so an extension release IS needed (the first draft said none); and `again` would refuse to re-run a commit after a withdrawn
   `again` round (`PanelService.cs:926`, `:957-960`).
9. **A failing `Withdraw` save**: D10.
10. **Scope**: a client timeout without the notification is unchanged and out of scope (§1).

## Epics

One epic. Cornerstones: 1 of 1 — S2 (the record a withdrawal leaves; it decides what the gate, the database and every reader see).
The stories live here rather than in a `_E1` document: the epic is small enough that one document is the honest size.

### E1 — a cancelled review stops its round (depends on #904)

Done when §11 holds. Stories, test-first:

**S1 — the token reaches the round.** D1, and a characterization of SDK 2.2.0: what it sends for a cancelled request whose handler returns
normally, and whose handler throws `OperationCanceledException`. RED: `TheReviewTools_TakeTheRequestsToken` (reflective, over `Tools.All`) —
no review lambda declares one; `ACancelledReviewPlan_CancelsItsRound` (in-memory transport) — the fake reviewer is never cancelled, the 5 s wait elapses.

**S2 — CORNERSTONE: the record a withdrawal leaves** (D2's rule, D3, D5, D9's C# and extension readers). RED with S1 alone:
`AWithdrawnRound_IsRecordedCancelled_AndMovesNoGate` — the verdict is `call_human`, `HumanGate` true, `RoundsRunThisStage` 1;
`AWithdrawnRound_NotifiesNobody` — one notice; `AWithdrawnRound_SpendsItsNumber` — round `n`'s `coai.db` row says `done`;
`AgainAfterAWithdrawnAgain_IsNotRefusedAsNothingSince` — refused today by the cancelled round's sha; `statusOf` on a cancelled fixture —
`done`. Guards (green before and after): `ACancelDuringSetup_RecordsNothing`, `ResolveReadsTheCompletedRound_NotACancelledOne`.

**S3 — the races, the mark, and what is never cancelled** (D2, D4, D10). RED: `SomeLanded_SomeCutByCancel_IsWithdrawn`, in two shapes —
one reviewer answers, the other is killed; one answers, the other is cut on the rate-limit ladder — fails with a verdict over one answer;
`EveryReviewerLandedBeforeTheCancel_TheRoundCompletes` — a gated fake, both answer, then the fire; `ALadderCutTurn_SaysWithdrawn` — its line
has no `endCause` today; `ACancelDuringFinalising_NeverLeavesRunning` and `AFailedWithdrawSave_IsNeverReadRunning` (a store seam that throws) —
`running`; `ACancelledContextRead_IsACancellation_NotAFailure` — "the round failed …". Guard: `TheCadenceRecord_IsWrittenWhole_WhenCancelled`.

**S4 — the host's stop, the remote row, the reply, the live leg** (D5's fallback if needed, D6, D7). RED:
`AStoppingHost_WritesHostStopped_BeforeTheProcessEnds` — `running`, because the loop's end waits 1 s for its own tasks only;
`AWithdrawnRemoteRow_SendsItsDelete` (a fake Team server) — zero DELETEs; `AFailedDelete_KeepsTheJobFile_AndTheRoundIsStillWithdrawn` (the
fake answers 503) — no DELETE is attempted today; the seam leg (§8) — no `withdrawn` line and no cancelled record.
