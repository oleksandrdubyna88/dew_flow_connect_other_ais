# PLAN — a cancelled review stops its round

> Status: **plan only, nothing implemented yet (2026-10-21).** Decided by the owner on 2026-10-21; depends on #904.
> Plan gate session 0c31bf4f, round 1: 12 findings accepted; the ten relayed for this revision are folded the same day (§12), and
> the epic's cadence consultation (three findings, §12 11–13) and risk consultation (four, §12 14–17) after them.
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
- **G8** The caller-versus-host attribution does not depend on callback timing (carried from #904's second code round, finding 3, which
  was unreachable there because no tool passed the caller's token). Today `CallerCause(host)` reads the host token when the caller's callback
  RUNS. If the caller fires first but its callback is delayed behind another registration, and the host fires in between, a person's
  withdrawal is filed `hostStopped`. The host token's firing moment is recorded on its own registration, without cancelling the round, and
  the caller's cause compares the two moments. RED first: a test that fires the caller, holds its callback, fires the host, then releases.

## 4. Decisions

- **D1 — the four review lambdas take the SDK's token**, exactly as `ask_human` does: a trailing `CancellationToken cancellationToken = default`
  passed to `PanelService`. No schema change. `consult`/`ask_consultants`: §1.
- **D2 — which round is withdrawn.** Not by `CauseAt(lastLanded)` alone (§2: `LandedAt` is a report time and results carry none). The
  scheduler MARKS every turn it ended because the round token fired — at the four sites of §2 — with an `EndedByRoundToken` flag on the outcome.
  The round is withdrawn iff the recorded first cause is `withdrawn`/`hostStopped` and fired before `RunAllAsync` returned, and either at
  least one turn carries the mark, or the round had no reviewer at all (a lane-only round, `RoundMachine.AdmittedOnlyForTheLane`). No turn
  marked → the round completes as today, however late its reports land after the fire (real, paid answers; `status` re-orients a caller
  whose reply was dropped). The marks decide — never report timing, never D6's provisional flag. `roundClock`/`unknown` complete as today. The mark is also what G2's cause is written on (widening #904's `Cancelled`-only rule).
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
- **D6 — the host stopping, and the provisional record.** When the caller's cause fires, an observer task the engine starts (on a `Fired`
  task the cancellation completes — never file IO inside a token callback) writes a PROVISIONAL `cancelled` record with the usage so far.
  **The ordering promised is "persisted before the engine awaits teardown COMPLETION"** — not "before teardown starts": #904 cancels the round
  token inside the callback itself, so the kill begins on the canceller's thread before any task can write. Holding the kill back until the
  write lands was rejected: the kill is what stops the spending, and it would put a save (with `SessionTurn`'s lock retries) in front of every
  kill to buy a record the start sweep already recovers. **The crash window, acknowledged:** a host killed between the cause firing and the
  provisional save (milliseconds) leaves `running` until the next start's sweep files it `interrupted`. **It guards the PERSISTED STATUS
  only:** today every progress event goes `LiveRound.Report` (`LiveRound.cs:196-207`) → `Persist()` (`:252-266`), which writes
  `Record("running", …)` unconditionally (`:256-257`), so a terminal report landing after the provisional write would put `running` back.
  `LiveRound` gains a cancelled flag, set by the provisional write under the same `_gate` lock `Report` takes; once set, `Persist` writes the
  cancelled record instead of `running`. Progress and usage keep being ACCEPTED after it, and the flag decides nothing (D2, D11): the terminal
  owner's `Withdraw` or `Finish` writes the final record. The serve loop's end, which today waits `StartingHost.EndBudget` = **1 s**
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

- **D10 — a terminal save that fails, on EITHER branch** (`SessionStore.Save` throws `SessionStoreException` on IO, `SessionStore.cs:551`).
  The withdraw branch's save, and the success branch's (`RoundEngine.cs:597`, outside `LiveRound`'s lock — `Finish`, `LiveRound.cs:214`,
  only builds the record) — which a cancel firing just before it does not turn into a withdrawal (D11). Today that branch answers "could not
  be recorded … run it again" (`RoundEngine.cs:659-667`), the findings live only in the log, and the file keeps the last `running` repaint.
  Now: retry once; if that fails, log the IO error and keep the WHOLE terminal session — for a completed round its state, `Pending`
  findings, `UsedPrompts` and record, not the record alone — in an in-process map of unsaved endings that `status`, `resolve` and every later
  claim-holder of that session apply before reading, retried by each of them and at the serve loop's end; the `coai.db` projection follows
  the save that succeeds. The completed round's reply says its findings are held and resolvable in this server, not "run it again". **The guarantee is NARROWED, deliberately:** in-process
  callers see the ending; ANOTHER `coai-mcp` process reads the session from disk (`status`, `PanelService.cs:558`) and sees `running` until
  the owner's next successful save, or until the owner has exited AND a server starts — only a start sweeps (`PanelService.cs:143-149`), and
  only a dead owner (`IsOrphaned`, `SessionStore.cs:769-770`) — which then files it `interrupted`. A cross-process side file was rejected:
  the failures that defeat this save (a full disk, a denied or locked data directory) defeat a second write into the same directory too, so
  it would add a reader to every `Load` for a guarantee it cannot keep in the one case it exists for.
- **D11 — exactly one terminal owner.** The engine continuation that awaits `RunAllAsync` decides withdraw-or-complete ONCE (D2), before
  `CompleteRound`, any notice, hold or budget movement, and alone owns the terminal persistence. Before it persists, the D6 observer is
  finished or disabled (awaited, then the `LiveRound` flag can no longer be set). The decision is held in a local the exception arms read: a
  withdraw stays a withdraw whatever is thrown after it; a completion whose save fails goes to D10, never to a withdrawal or "the round failed".

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
   is misclassified because its report time is not its cut time; or a late progress report rewrites the provisional `cancelled` as
   `running`; or two branches both finalise. **Guard:** D2's mark (not timestamps, not the provisional flag) shipped with the record,
   D11's single terminal owner, D6's `Persist` guard, D4's `CancellationToken.None`, D10 on both branches, and gated-fake interleavings.

Lesser: the SDK may answer a cancelled request (D5's fallback); a host killed before the provisional write leaves `interrupted` after the
next start (D6); a failed DELETE costs up to the server's expiry (D7).

## 7. Build order

1. **#904 merges.** Nothing here starts before it; every reference above is re-read on the merged `main`.
2. **PR A — atomic: S1 + S2**, RED first (risk 1). It carries everything D2 needs to decide a round — the token, the scheduler's
   `EndedByRoundToken` mark at all four cut sites (today a ladder cancellation is an ordinary `RateLimited`, `BoundedScheduler.cs:636-641`),
   the record, the readers, D11's decide-once — and the race tests that pin them, "one answered, one cut on the ladder" included. **If S1's
   characterization shows SDK 2.2.0 answers a cancelled request, D5's transport fallback ships in PR A too**: without it the change would
   reply to requests the protocol says not to.
3. **PR B — S3**: D6's provisional write with its `Persist` guard and D11's observer hand-off (never one without the others), the
   finalising races, D10 on both branches.
4. **PR C — S4**: the host-stop bound, the remote DELETE tests, the live seam leg. It needs PR B (the host stop relies on the provisional write).
5. Docs with each PR; the releases (§10) after PR C.

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
- [ ] Recorded `cancelled` in the session file and `coai.db` from the provisional write on (D6's crash window aside); never `running` to
      this server after it, even when a terminal save fails on either branch (D10); exactly one terminal branch per round (D11).
- [ ] Gate state, budget, holds and notices unmoved; never a verdict over a partial panel; every D9 reader handled.
- [ ] D5's acceptance observed on the seam leg (or its fallback shipped and observed); the SDK's behaviour recorded in `module_server.md`.
- [ ] Each story's tests watched RED for the stated reason, then green; the whole suite and the seam leg green.
- [ ] Docs (§9) updated; this plan promoted with `/promote-plan` when both releases ship.

## 12. What the reviews changed (2026-10-21)

Every finding verified against the code before it was folded. **Plan gate session 0c31bf4f, round 1:**
1. D2 classifies by the scheduler's mark, not `CauseAt(lastLanded)`: `LandedAt` is the report's construction time (#904
   `BoundedScheduler.cs:79`) and results carry none; a cut turn may end `Cancelled`, `NotStarted` or `RateLimited` (§2).
2. No reviewer landed / setup: both stated in D2. 3. G2 vs §5: one behaviour, the carve-out gone.
4. G5: an acceptance on the seam leg and a named fallback (D5). 5. Remote DELETE: the server's expiry with file:line (D7), a failed-DELETE test.
6. Host-stop bound derived from the 10 s `AbandonBudget` (`ReviewerExecutor.cs:837`) + kill time (D6). 7. `consult`/`ask_consultants` out of scope (§1).
8. Every reader enumerated (D9) — contradicted the first draft twice: the rounds-log page's `statusOf` would show `done` (so an extension
   release IS needed), and `again` would refuse a commit after a withdrawn `again` round (`PanelService.cs:926`, `:957-960`).
9. A failing save: D10. 10. A client timeout without the notification: out of scope (§1).

**The epic's cadence consultation** (findings 11–12 verified by the lead and again here):
11. Progress overwrote the provisional cancel (`LiveRound.cs:196-207` → `:252-266`): D6's `Persist` guard under the progress lock.
12. D10's in-memory fallback is invisible to another process (`PanelService.cs:558`, `SessionStore.cs:769-770`): the guarantee narrowed, a
    side file rejected with the reason, the second-process case tested.
13. Merge boundary: the mark moved into S2, D5's fallback into S1 when the SDK replies; §7 names three pull requests.

**The epic's risk consultation** (the code facts verified by the lead and again here: `Finish`, `LiveRound.cs:214`, only builds the record;
the success save is `RoundEngine.cs:597`, outside `_gate`; the scheduler reports progress synchronously, then returns the outcome):
14. The provisional flag guards the persisted status only; progress and usage are still accepted and D2 never reads the flag (D2, D6) —
    S3 RED `APaidAnswerReportedAfterTheCancel_IsKept`.
15. The success branch's final save can fail too: D10 now covers both branches and keeps the WHOLE completed session (state, pending
    findings, record), never a withdrawal — S3 RED `ACompletedRoundsFailedSave_KeepsItsFindings_AndIsNeverWithdrawn`.
16. Exactly one terminal owner (D11): the decision is taken once, the observer is finished before terminal persistence, the exception arms
    keep the decision — S2 RED `ExactlyOneTerminalBranch_PerInterleaving`, S3 RED `TheObserverIsFinished_BeforeTerminalPersistence`.
17. D6 overstated its ordering: the promise is now "persisted before the engine awaits teardown completion", the kill is not held back for
    the write (the reason is in D6), and the millisecond crash window is acknowledged.

## Epics

One epic. Cornerstones: 1 of 1 — S2 (the record a withdrawal leaves; it decides what the gate, the database and every reader see).
The stories live here rather than in a `_E1` document: the epic is small enough that one document is the honest size.

### E1 — a cancelled review stops its round (depends on #904)

Done when §11 holds. Stories, test-first:

**S1 — the token reaches the round** (PR A). D1, and a characterization of SDK 2.2.0: what it sends for a cancelled request whose handler
returns normally, and whose handler throws `OperationCanceledException`; if it answers, D5's transport fallback ships here (unit test: an
outbound response for a cancelled id is dropped). RED: `TheReviewTools_TakeTheRequestsToken` (reflective, over `Tools.All`) — no review
lambda declares one; `ACancelledReviewPlan_CancelsItsRound` (in-memory transport) — the fake reviewer is never cancelled, the 5 s wait elapses.

**S2 — CORNERSTONE: the record a withdrawal leaves** (PR A; D2's rule AND its mark, D3, D5, D9, D11's decide-once). RED with S1
alone: `AWithdrawnRound_IsRecordedCancelled_AndMovesNoGate` — the verdict is `call_human`, `HumanGate` true, `RoundsRunThisStage` 1;
`AWithdrawnRound_NotifiesNobody` — one notice; `AWithdrawnRound_SpendsItsNumber` — round `n`'s `coai.db` row says `done`;
`SomeLanded_SomeCutByCancel_IsWithdrawn`, in two shapes — one reviewer answers, the other is killed; one answers, the other is cut on the
rate-limit ladder — fails with a verdict over one answer (the ladder shape stays red until the mark exists: an ordinary `RateLimited`);
`ALadderCutTurn_SaysWithdrawn` — its line has no `endCause`; `AgainAfterAWithdrawnAgain_IsNotRefusedAsNothingSince` — refused by the
cancelled round's sha; `statusOf` on a cancelled fixture — `done`; `ExactlyOneTerminalBranch_PerInterleaving` (theory: every answer
before the cancel / one turn marked cut; seams count `CompleteRound`, `Withdraw`, notices and saves) — the marked case runs `CompleteRound`,
moves the budget and notifies. Guards (green before and after): `EveryReviewerLandedBeforeTheCancel_TheRoundCompletes`
(a gated fake: both answer, then the fire), `ACancelDuringSetup_RecordsNothing`, `ResolveReadsTheCompletedRound_NotACancelledOne`.

**S3 — the provisional record, the finalising races, what is never cancelled** (PR B; D4, D6, D10, D11's observer).
RED: `ALateProgressReport_DoesNotRewriteRunning` — deterministic: provisional cancellation, then one terminal progress report, then the
session read FROM DISK before the final `Withdraw` — `running` today (`LiveRound.cs:256-257`); `ACancelDuringFinalising_NeverLeavesRunning`
— `running`; `AFailedWithdrawSave_IsNeverReadRunning` (a store seam that throws), two cases — in-process `status` sees `cancelled`
(`running` today); a SECOND `PanelService` over the same data directory sees `running` while the owner lives (D10's narrowed guarantee,
pinned) and `interrupted` once the owner's pid is dead and a server starts; `ACancelledContextRead_IsACancellation_NotAFailure` — "the
round failed …"; `APaidAnswerReportedAfterTheCancel_IsKept` — a reviewer paused after it obtained `Ok`, the cancel fired, the
provisional `cancelled` observed on disk, the report released: no turn marked, so the final record is `done` with its findings and exact
usage — red today at the provisional step (no such write), and red on a plant where D2 reads the flag (`cancelled`);
`ACompletedRoundsFailedSave_KeepsItsFindings_AndIsNeverWithdrawn` — every reviewer done, the cancel fired just before `RoundEngine.cs:597`,
that save throws: today "run it again", the file `running`, the findings gone; `TheObserverIsFinished_BeforeTerminalPersistence` — an
observer gated to write after a completion's save: red with it unawaited (the file ends `cancelled` over `done`).
Guard: `TheCadenceRecord_IsWrittenWhole_WhenCancelled`.

**S4 — the host's stop, the remote row, the live leg** (PR C; D6's bound, D7). RED:
`AStoppingHost_WritesHostStopped_BeforeTheProcessEnds` — `running`, because the loop's end waits 1 s for its own tasks only;
`AWithdrawnRemoteRow_SendsItsDelete` (a fake Team server) — zero DELETEs; `AFailedDelete_KeepsTheJobFile_AndTheRoundIsStillWithdrawn` (the
fake answers 503) — no DELETE is attempted today; the seam leg (§8) — no `withdrawn` line and no cancelled record.
