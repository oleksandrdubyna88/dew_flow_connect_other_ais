# S2 SPEC — the record a withdrawal leaves

> Status: **plan only, 2026-10-21 — the S2 cornerstone spec, by Fable.** The cornerstone of
> [PLAN_a_cancelled_review_stops_its_round.md](PLAN_a_cancelled_review_stops_its_round.md) (its D2 rule AND its mark, D3, D5, D9,
> D11's decide-once), written so a coder implements it test-first with no judgement call left. Scope: `src_mcp/runners/Reviewers/BoundedScheduler.cs`,
> `src_mcp/runners/Reviewers/ReviewerExecutor.cs` (one property on `ReviewerOutcome`), `src_mcp/src/Server/Rounds/RoundEngine.cs`,
> `src_mcp/src/Server/LiveRound.cs`, `src_mcp/src/Server/SessionStore.cs` (`RoundRecord`), the readers of §4, `src_vs_code/src/roundsLog.ts`,
> their tests.
>
> Related docs: [module_server.md](../research/module_server.md), [module_runners.md](../research/module_runners.md),
> [module_extension.md](../research/module_extension.md), [PLAN_the_round_counter_tail_E3.md](PLAN_the_round_counter_tail_E3.md) (the
> `RoundCancellation` this builds on).

References are to the branch `feat/cancel-stops-the-round`, which already holds #904's E3 (`RoundCancellation`, `EndCauses`, the local
`endCause`, `InputEndSignallingTransport`). Every `file:line` below was opened on that branch on 2026-10-21; where the parent plan's `main`
line differs, this document's number is the one to use. S1 (the four review lambdas take the SDK's token, `Tools.cs:83-87`, `:125-127`,
`:178-182`, `:238-245`, as `ask_human` does at `:387-391`) is assumed landed in the same pull request; nothing here works without it, and
nothing here re-specifies it.

## 1. What S2 decides, in one paragraph

Once the scheduler has returned (`RoundEngine.cs:416`), the engine decides ONCE whether the round was withdrawn or completed, from two
inputs only: the FIRST cause `RoundCancellation` recorded, read at the moment `RunAllAsync` returned; and whether any turn carries the mark
the scheduler set when the round's own token cut it. A withdrawn round writes ONE terminal record — `status: cancelled`, `verdict:
withdrawn | hostStopped` — onto the session as it was READ (never as the begin moved it), projects it, and leaves by throwing
`OperationCanceledException` with the caller's token. It never reaches `RoundMachine.CompleteRound`, never mints a hold notice, never
spends a budget round, never writes `AwaitingResolve`, never moves a feature base or ground. Every reader of a round's status or verdict
either skips the new status or is shown, by test, to be right without a change.

## 2. The decision rule

### 2.1 The mark — `ReviewerOutcome.EndedByRoundToken`

One `bool` on the BASE outcome, beside `Warning` (`ReviewerExecutor.cs:75`):

```csharp
/// <summary>
/// True when the round's own token ended this turn (the scheduler's mark, D2 of
/// PLAN_a_cancelled_review_stops_its_round.md): killed while running, cut while queued, or cut while waiting
/// on the rate-limit ladder. False for every turn that ended on its own terms — a <see cref="Cancelled"/>
/// whose token nobody fired included.
/// </summary>
public bool EndedByRoundToken { get; init; }
```

A property, not a subtype: `ReviewerNotices.ByType` (`ReviewerNotices.cs:47-58`) is keyed by TYPE and
`TheReviewerFailuresAreWrittenDownTests` compares its keys against the sealed subtypes — a seventh subtype would be red there, a property
is not. The runners assembly learns no ledger word by it (the boundary E3 kept): it says "the token I was handed fired", nothing more.

**Where it is set — the four cut shapes of the parent's §2, each at its site in `BoundedScheduler.cs`.** The criterion at every site is
the same: `ct.IsCancellationRequested` at the moment the outcome is OBSERVED, where `ct` is the round token the scheduler was handed
(`RunAllAsync(…, CancellationToken ct, …)`, `:224-230`). The timing of the report is never read.

| # | Shape (parent §2) | Site today | What changes |
|---|---|---|---|
| 1 | killed while running, the launcher reports cancelled → `Cancelled` | `LaunchAsync`, `:475-477`: `var outcome = settle(await loop.RunAsync(w, ct));` then `Report(…)` and `return` | after `settle`: `if (outcome is ReviewerOutcome.Cancelled && ct.IsCancellationRequested) { outcome = outcome with { EndedByRoundToken = true }; }` — BEFORE the `Report`, so the ledger, the live view and the stand-down see the marked outcome (the D8 rule the `settle` comment states, `:473-474`) |
| 2 | killed while running, the launch throws → `NotStarted` ("was cancelled while it was running") | `LaunchAsync`, `:479-482`: `catch (OperationCanceledException) { return Abandoned(…, "was cancelled while it was running", loop.Earlier); }` | `Abandoned` gains `bool endedByRoundToken`; this site passes `ct.IsCancellationRequested`. An `OperationCanceledException` whose token is NOT the round's (an adapter's own) stays unmarked — marking it would file a withdrawal nobody made |
| 3 | still queued for a slot → `NotStarted` ("was still queued") | `Took` false (`:438-449`) at `MachineLaneAsync :299-308` and `EngineLaneAsync :379-382` → `Abandoned(onProgress, w.Invocation, queued.Elapsed)` | these three calls pass `endedByRoundToken: true` — `SemaphoreSlim.WaitAsync(ct)` throws `OperationCanceledException` for `ct` and nothing else |
| 4 | waiting on the ladder → the refusal it last saw, `RateLimited` | `LadderAsync :643-648`: `if (!await WaitedOut(wait.Value, ct)) { return limited with { Attempts = attempts }; }` | `return limited with { Attempts = attempts, EndedByRoundToken = true };` — `Task.Delay(wait, ct)` throws for `ct` alone. The two other ladder exits (`wait is null`, `:634-637`; `Hopeless`, `:666-668`) are the ladder's OWN endings and stay unmarked |

`Abandoned` (`:495-510`) builds the outcome `with { EarlierTurns = …, EndedByRoundToken = endedByRoundToken }`. Nothing else in the
scheduler changes. `StandDown.Record` (`StandDown.cs:30-44`) and `ReviewerSummaryFactory` ignore the property, so a marked turn is still a
failure to the stand-down and to the summary — the parent's §5: a cut cloud reviewer is never "quiet".

**Invariant the coder may rely on:** a marked turn's `CauseAt(LandedAt)` is never `unknown`. `RoundCancellation.Fired` (`RoundCancellation.cs:86-90`)
records the cause BEFORE it cancels the round token, the mark requires that token to have fired, and `LandedAt` (`BoundedScheduler.cs:79`)
is stamped when the progress record is built — after the mark. So the round token fires ⇒ a cause is recorded ⇒ it is at or before any
landed moment that observed the firing.

### 2.2 The cause on the ledger line (G2)

`RoundEngine.EndCauseOf` (`RoundEngine.cs:853-854`) widens from `Cancelled`-only to the mark:

```csharp
private static string EndCauseOf(ReviewerOutcome finished, RoundCancellation cancellation, long landedAt) =>
    finished.EndedByRoundToken || finished is ReviewerOutcome.Cancelled ? cancellation.CauseAt(landedAt) : string.Empty;
```

Consequences, all intended: a ladder-cut `RateLimited` line keeps `outcome: rate limited (after N attempts)` and gains `endCause: withdrawn`
(one behaviour, no carve-out — parent §5); a queued-out `NotStarted` line gains the cause; an unmarked `Cancelled` (a remote row's server
ended it, nothing fired) stays `unknown` exactly as E3's `ACancellationNobodyCanName_IsUnknown_NeverEmpty` pins; and a turn the ROUND
CLOCK cut is marked too (the clock cancels the same token, `RoundCancellation.cs:56`), so a clock-cut `NotStarted` now says `roundClock`
where it said nothing — honest, and it decides nothing (§2.3). `UsageLedger.Record`/`AppendTurn` (`UsageLedger.cs:374-427`) are untouched:
the cause lands on the turn's LAST line only, as E3 built it.

### 2.3 The decision — one pure function, read once

A new `internal static` on a new file `src_mcp/src/Server/Rounds/RoundEnding.cs`:

```csharp
/// <summary>How a round that ran its scheduler ends — decided ONCE, from the recorded first cause and the scheduler's marks (D2, D11).</summary>
internal enum RoundEnding
{
    /// <summary>No reviewer was launched yet when the caller's token fired: nothing is recorded (D2, "a cancel during setup").</summary>
    Setup,
    /// <summary>The ordinary ending: the gate judges the results, whatever they are.</summary>
    Completed,
    /// <summary>The caller withdrew the call (or the host stopped) and it ended at least one turn — or the round had no reviewer at all.</summary>
    Withdrawn,
}

internal static class RoundEndings
{
    /// <param name="cause">RoundCancellation.CauseAt(the moment RunAllAsync returned).</param>
    /// <param name="anyTurnMarked">results.Any(r => r.Outcome.EndedByRoundToken).</param>
    /// <param name="noReviewer">work.Count == 0 — a round only the security lane's budget admitted (RoundMachine.AdmittedOnlyForTheLane).</param>
    public static RoundEnding Decide(string cause, bool anyTurnMarked, bool noReviewer) =>
        cause is EndCauses.Withdrawn or EndCauses.HostStopped && (anyTurnMarked || noReviewer)
            ? RoundEnding.Withdrawn
            : RoundEnding.Completed;
}
```

In `RunStageAsync`, immediately after the await at `RoundEngine.cs:416-436` returns:

```csharp
var results = await _scheduler.RunAllAsync(…);                 // :416, unchanged
var returnedAt = Stopwatch.GetTimestamp();                     // the ONE moment the cause is compared against
ending = RoundEndings.Decide(
    cancellation.CauseAt(returnedAt),
    results.Any(r => r.Outcome.EndedByRoundToken),
    work.Count == 0);
if (ending is RoundEnding.Withdrawn)
{
    return await WithdrawAsync(…);                             // §5 — never returns: throws OperationCanceledException(ct)
}
ending = RoundEnding.Completed;                                // already is; the local is what the catch arms read (§5)
// … :437 onward, byte for byte as today
```

`ending` is a `var ending = RoundEnding.Setup;` declared BEFORE the `try` at `:305` — the catch arms at `:662-685` read it (§5). The
`WhatEndedIt` sentence (`:448`, `:845-846`) and `summary` are built only on the completed path, as today.

**Why `CauseAt(returnedAt)` and not `CauseNow()`:** D2 says "fired before `RunAllAsync` returned". `CauseNow()` would be read a few
instructions later; for a lane-only round (`work.Count == 0`, `RunAllAsync` returns at once) a cancel landing in that gap would withdraw a
round that completed with nobody. The timestamp pins the comparison to the return.

### 2.4 Every interleaving, one terminal branch

Two reviewers A and B unless said otherwise; "cancel" is the caller's token (`ct`), `withdrawn` stands for `withdrawn` or `hostStopped`
throughout (the host token already cancelled when the caller's fires files `hostStopped`, `RoundCancellation.cs:83-84` — the same branch).
The theory `ExactlyOneTerminalBranch_PerInterleaving` (§6, T10) is rows 1–9 over the pure function plus rows 1–5 and 7 through the engine.

| # | A | B | Cancel fires… | First cause | Marks | `Decide` | Branch |
|---|---|---|---|---|---|---|---|
| 1 | answered | answered | after both landed | `withdrawn` | none | Completed | complete as today — paid answers, a verdict; `status` re-orients the caller whose reply the SDK dropped (G6) |
| 2 | answered | running | while B runs | `withdrawn` | B (shape 1 or 2) | Withdrawn | withdraw |
| 3 | answered | on the ladder | during B's wait | `withdrawn` | B (shape 4, `RateLimited`) | Withdrawn | withdraw |
| 4 | running | queued (`GlobalConcurrency = 1`) | while A runs | `withdrawn` | A (1/2), B (3) | Withdrawn | withdraw |
| 5 | ended `Cancelled` on its own (remote server) | answered | never | `unknown` | none | Completed | complete as today (`call_human` if nobody answered, else a verdict) |
| 6 | running | running | the CLOCK fires first, the cancel after | `roundClock` | A, B (the clock cut them) | Completed | complete as today: the deadline round, `EndedByDeadline` set (`:448`) |
| 7 | — | — | before `LiveRound` exists (`:400`): during `WarmRemoteRolesAsync :303`, `MakeWork :360` | `withdrawn` | — | (not consulted) | **Setup**: nothing recorded, one log line, the claim/worktree/scratch go with their `using`s (§5) |
| 8 | no reviewer at all (`work.Count == 0 && AdmittedOnlyForTheLane`, `:381`) | | before `RunAllAsync` returned | `withdrawn` | none | Withdrawn | withdraw — a lane-only round with nobody launched |
| 9 | no reviewer at all | | after `RunAllAsync` returned | `unknown` at `returnedAt` | none | Completed | complete as today (a round nobody answered) |
| 10 | answered | answered | between `RunAllAsync` returning and `CompleteRound` | `withdrawn`, but AFTER `returnedAt` | none | Completed | complete; if a later step throws, the arms see `Completed` and never withdraw (§5) |
| 11 | `NoBudget` landed (parent §5) | running | after A landed | `withdrawn` | B only | Withdrawn | withdraw; A's line keeps its word and no cause |
| 12 | `StoodDown` (parent §5) | running | after A stood down | `withdrawn` | B only | Withdrawn | withdraw; A stays `stood down`, unmarked |

Rows 6 and 10 are the ones a coder gets wrong by reading timing or flags: marks alone never withdraw (row 6), and a cause alone never
withdraws (rows 1, 10). The parent's D6 provisional flag does not exist in S2 and is never an input to `Decide` (S3 adds it, and D2 forbids
reading it).

## 3. The record a withdrawal writes

### 3.1 The constants (`SessionStore.cs:98-100`)

```csharp
public const string Running = "running";
public const string Done = "done";
public const string Interrupted = "interrupted";
/// <summary>The caller withdrew the call, or the host stopped, while the round ran: nothing was judged. Verdict = the cause word.</summary>
public const string Cancelled = "cancelled";
```

The verdict is the ledger's word, exactly: `EndCauses.Withdrawn` (`"withdrawn"`) or `EndCauses.HostStopped` (`"hostStopped"`),
`EndCauses.cs:25`, `:46`. Never `"cancelled"` in the verdict column and never `"withdrawn"` in the status column.

### 3.2 `LiveRound.Withdraw` (new, beside `Finish`, `LiveRound.cs:214-246`)

```csharp
/// <summary>
/// The record of a round the caller withdrew (or the host stopped): no verdict, no findings, the usage folded
/// exactly as <see cref="Finish"/> folds it — a reviewer the cancel killed still spent what its vendor reported.
/// </summary>
/// <param name="cause">EndCauses.Withdrawn or EndCauses.HostStopped — the round's recorded first cause.</param>
/// <param name="cut">How many turns carry the scheduler's mark.</param>
public RoundRecord Withdraw(string cause, int cut, IReadOnlyList<(ReviewerInvocation Invocation, ReviewerOutcome Outcome)> results)
{
    var usage = results.Select(r => r.Outcome.TotalUsage).Aggregate(Core.Findings.Usage.None, (total, one) => total.Add(one));
    lock (_gate)
    {
        var answered = _states.Values.Count(s => s.Status is ReviewerState.Done);
        return Record(cause, 0, $"{answered} of {_states.Count} answered, {cut} cut", RoundRecord.Cancelled) with
        {
            CompletedUtc = DateTime.UtcNow,
            TokensIn = usage.TokensIn,
            TokensOut = usage.TokensOut,
            CostUsd = usage.CostUsd,
            CostNote = usage.NoPriceSet ? Core.Findings.CostText.NoPriceSet : string.Empty,
            UsageNote = usage.NotCaptured ? Core.Findings.CostText.UsageNotCaptured : string.Empty,
            Note = cause == EndCauses.HostStopped
                ? "the host stopped while the round ran"
                : "the caller withdrew the call while the round ran",
        };
    }
}
```

Extract the usage fold into `private static Usage UsageOf(results)` and call it from both `Finish` and `Withdraw` (reuse-first; the
`Finish` remarks at `:220-228` move onto the helper). `Record` (`:269-291`) is unchanged, so the cancelled record carries `StartedUtc`,
`RunnerPid`, `Subject`, `Key`, `ReviewerStates` (the reviewer rows as they stood: the answered one `done`, the cut ones `failed` with
"cancelled" / "not started: the round ended while this reviewer …" / "rate limited (after N attempts)…" — exactly what `Noted`
`:141-149` wrote), `Caller`, `PlanKey`, `EpicNumber`. `_states.Count` is the number of reviewers the round was built with (`:86`).

### 3.3 The engine's withdraw path (`RoundEngine.cs`, replaces nothing — new private method)

```csharp
private async Task<string> WithdrawAsync(
    PersistedSession loaded, LiveRound live, RoundAudit audit, StageRun stage, RoundWork roundWork,
    IReadOnlyList<(ReviewerInvocation, ReviewerOutcome)> results, string cause, string sha, string planText, CancellationToken ct)
{
    var record = live.Withdraw(cause, results.Count(r => r.Outcome.EndedByRoundToken), results) with
    {
        Sha = Stages.Of(stage.Stage).RecordsSha ? sha : string.Empty,      // as :493
        Size = roundWork.Size,                                              // as :494
        Gitlinks = roundWork.Gitlinks,                                      // as :495
        CadenceNote = stage.Cadence.Call.Note,                              // as :497
        AdmittedBy = FeatureSecondRound.AdmittedBy(loaded.State),           // the ground it was ADMITTED on, read from the state it began from
        PlanSource = PlanSources.Stamp(stage, planText, sha),               // as :501
        // Authors stays null: a cancelled round is not settled (Windows.IsSettled), and RoundAuthorsStep.For is never asked.
    };
    var saved = loaded with { Rounds = [.. loaded.Rounds, record] };
    SaveWithdrawn(saved);                                                   // §5: never cancelled, SessionStoreException logged (S3 retries)
    var caller = RoundCommands.CallerFor(loaded);
    _projection.Write(db => db.RecordRound(
        loaded.State, record, [],
        new Store.RoundContext(planText, sha, caller, [], WhatTheCallerWasDoing(loaded, record)) { BaseRef = roundWork.BaseRef }));
    audit.Withdrawn(cause, record);                                         // one log line; RoundAudit gains the method (the Closing shape, :616)
    _log.Information("the {Stage} round {Number} was {Cause}: {Reviewers}", stage.Stage, record.Number, cause, record.Reviewers);
    throw new OperationCanceledException(ct);                               // D5
}
```

`AdmittedBy` reads `loaded.State` rather than `session.State` (`:500` reads `session.State`) — the two agree on `SecondRound` (the begin
moves `Stage`/`AdvanceOnResolve` only, `RoundMachine.cs:300-305`), and `loaded` is what is saved. `WhatTheCallerWasDoing` (`:916-925`) is
included because the NEXT round's stretch starts at `Rounds[^1].CompletedUtc` (`:918`) — without it the caller's work before the
withdrawal would be attributed to no round. `RecordConsultMissed` (`:646-652`) is NOT called: there are no findings to survive anything.

### 3.4 What the session file holds afterwards

| Field | Value | Why |
|---|---|---|
| `State` | `loaded.State` — the session as read under the claim (`:251`), with the person's answer already applied (`:241`, saved at `:112`) | D3: the state machine never sees the round. `RoundSkips.Record` (`RoundSkips.cs:76`) is the precedent — a round that did not judge saves `loaded` |
| `State.Stage`, `RoundsRunThisStage`, `AwaitingResolve`, `HumanGate`, `HoldQuestions`, `AdvanceOnResolve`, `EscalationsUsed`, `SecondRound`, `Rejections` | exactly as read | an `again` that was withdrawn leaves the stage `Done` (the reopen at `:268-270` was in memory only); a `HumanGate` the person released at `:241` stays released |
| `FeatureBase` | as read — NOT the base `HeldToBase` moved to (`:258`, `:712-716`) | `StageRun.FeatureBase` (`StageRun.cs:124-130`): "saved with the round only (never by a skip, a refusal or a build that failed)" — a withdrawn round judged nothing |
| `Plan`, `Epic`, `CadenceRepoId` | as read (the `:342-350` assignment was on `session`, not `loaded`) | the RECORD carries `PlanKey`/`EpicNumber` (`LiveRound.cs:289-290`, from `_session`, which is the begun session) — the cadence's "first code round of this epic" (`CadenceDesk.cs:423-428`) reads the records, so nothing is lost |
| `Rounds` | `[.. loaded.Rounds, record]` | the `running` row `LiveRound.Persist` kept appending (`:252-257`) is replaced by the terminal record — "never appended twice" (`:249-251`) |
| `Pending`, `UsedPrompts`, `PlanText`, `Carried` | as read | no findings were merged, no lens was spent (a cut reviewer's prompt is unspent — `SpentPrompts` is never called), the plan text and the carried decisions are the previous round's |

### 3.5 What is NOT touched — and the line that proves each

| Thing | Proof it is unmoved |
|---|---|
| the gate stage and `RoundsRunThisStage` | `RoundMachine.CompleteRound` (`RoundMachine.cs:387-437`) and `CompleteFeatureRound` (`:457-479`) are never called: `WithdrawAsync` returns by throwing before `:461` |
| the budget round | same — `roundsRun = s.RoundsRunThisStage + 1` at `:397`/`:459` is the only increment |
| `HumanGate` / a hold | `NoticeIfAPersonMustDecide` (`:599`, `:801-804`) is never reached; `HoldQuestions` is whatever `loaded` held |
| the phone notice | `_escalations.Notify` (`:654-658`) is never reached — zero files appear in `Escalations.Directory` (`Escalations.cs:127`) |
| the feature ground (`SecondRound`) and the lane budget | `CompleteFeatureRound :462-478` never runs; `AdmittedOnlyForTheLane` reads a count that did not move |
| `EscalationsUsed` | `Exhausted` (`:596-621`) never runs |
| the cadence record | `CadenceDesk.Close` is called only from `resolve` (`PanelService.cs:1775-1778`); `PrepareAsync`/`BeforeTheCode` ran BEFORE the round inside `RefuseBeforeBuilding` (`PanelService.cs:934-939`) and are not undone — D4 |
| the server notices per cut reviewer | **ARE written**, deliberately: `LiveRound.Noticed` (`LiveRound.cs:164-177`) offers one `ReviewerNotices.Of` per failed reviewer as it reports, and `Cancelled`, `NotStarted` and `RateLimited` are all in `ByType` (`ReviewerNotices.cs:50-56`). These are the notices page's rows, not the person's phone; G4's "no notice" is the hold notice above. Stated so nobody "fixes" it |
| `UsedPrompts` | `SpentPrompts` (`:67-72`, `:606`) is never called; a lens the cancel cut is asked again next round |

### 3.6 The round number spent, and `n+1`

`RoundNumber.Next` (`RoundNumber.cs:27-40`) is one past the highest number in the journal for the stage. The cancelled record carries `n`
(`LiveRound._number`, allocated at `RoundEngine.cs:323` under the claim), so the next round is `n+1` and `coai.db`'s
`(session_id, stage, number)` upsert (`RoundsDb.cs:865`) never overwrites it — exactly as an `interrupted` round keeps its number
(`RoundNumber.cs:20-21`).

### 3.7 The `coai.db` row

`RoundsDb.RecordRoundRow` (`RoundsDb.cs:849-887`) writes `status = "cancelled"`, `verdict = "withdrawn" | "hostStopped"`, `gating = 0`,
`completed_utc` set, `tokens_in`/`tokens_out`/`cost_usd` from the fold, `round_key` the round's key, `head_sha` from the context,
`base_ref` from the work, `agent_log` the caller's stretch, `note` the sentence of §3.2, `plan_key`/`epic_number` from the record. No
`findings` rows, no `consult_missed` row. **Verified deviation from the parent's D9:** `RoundsQuery.Read` (`RoundsQuery.cs:432-458`) and
its `LoggedRound` (`:27-124`) carry NO `status` or `verdict` column — only `LoggedConsultation` (`:136-170`) does — so `--log` renders
nothing to test; the row's two words are read by `RoundAuthorsQuery.cs:194` → `RoundAuthorsPopulation.cs:45`, `:97-98` (through
`Windows`, §4) and by `AttributionMapping.cs:100-103`. The "render test" of D9 becomes T5's two `SELECT`s.

### 3.8 `again: true` after a withdrawn round

`PanelService.SinceWhenFinished` (`PanelService.cs:963-964`) consults `LastCodeRound` (`:966-969`) only when the state is `Done` — which
it is after a withdrawn `again`, since `loaded` was saved. `LastCodeRound` becomes
`session.Rounds.LastOrDefault(r => r.Stage == nameof(Stage.CodeReview) && r.Status != RoundRecord.Cancelled)`: the comparison commit is
the last code round that JUDGED, so a commit the withdrawn round never read is "new" again. A cancelled code round still records its `Sha`
(§3.3, `RecordsSha` is true for the code stage, `Stages.cs:82`) — it is a fact about what was mounted, and `DeltaPredecessor`
(`DeltaPredecessor.cs:45-46`) already requires `Status == Done`, so it never chooses one.

### 3.9 `resolve` after a withdrawn round

`RoundMachine.Resolve` (`RoundMachine.cs:629-642`) refuses when `!AwaitingResolve`, and a withdrawn round never set it. So `resolve` right
after one answers "there is no completed round awaiting decisions — run a review first…" (`:638-641`), and `PanelService.Finish`'s
`session.Rounds[^1].Verdict` (`PanelService.cs:1777`) is never read with a cancelled record last: a begin is refused while
`AwaitingResolve` (`:88`, `:193`, `:251`, `:276`), so the only way a cancelled record is last is when the previous round was already
resolved. No change; T13 pins it.

### 3.10 `status` and the extension's answer

`PanelService.StatusAsync` (`PanelService.cs:559-578`) answers `SessionAnswerFor` (`:1928-1940`), which passes `session.Rounds` through
(`Authors` stripped). A client reads `rounds[^1].status == "cancelled"`, `verdict == "withdrawn"`, `completedUtc` set, the reviewer rows,
the usage. The rounds-log page builds its row from the SESSION FILE (`roundsLog.ts:507-570`, `round.status`/`round.verdict` at `:543-546`)
and asks the database only for `accepted`/`rejected`/`foundCount` (`:515-524`); `statusOf` (`:613-631`) therefore sees the file's
`cancelled` and today falls through to `done` (or `awaiting` when the database row is known with `accepted = -1` — it will be, and
`foundCount` is 0, so `done`). The change is §4's last four rows.

## 4. D9 — every reader of `RoundRecord.Status` / `Verdict`, verified on the branch

Found by searching `src_mcp` (non-test) for `.Status`/`.Verdict`/`RoundRecord.*`/`Rounds[^1]`/`LastOrDefault(r =>` and `src_vs_code/src`
(non-test) for `.status`/`.verdict`/`isRunning`. 24 readers; 8 change.

| # | Reader | What a `cancelled` / `withdrawn` round does there | Change |
|---|---|---|---|
| 1 | `PanelService.cs:966-969` `LastCodeRound` (via `:963-964`, used by `again` at `:935`) | its sha makes `again` "nothing since" | **skip `Cancelled`** (§3.8) |
| 2 | `Stages/FeatureSecondRound.cs:73-74` `FailedInRoundOne` | round 2's roster is built from the cut reviewers' `failed` states | **skip `Cancelled`**: `&& r.Status != RoundRecord.Cancelled` |
| 3 | `Stages/FeatureStage.cs:187-188` `LastReviewed` | `Unmoved` compares the head with a round that read nothing | **skip `Cancelled`**, same clause |
| 4 | `Cadence/CadenceDesk.cs:385-400` `Reconciled` | `WithClosed(last.EpicNumber, "withdrawn")` — an epic closed by a round that judged nothing | **skip `Cancelled`** in the `LastOrDefault` at `:385` |
| 5 | `Cadence/CadenceDesk.cs:423-428` `FirstCodeRound` | the epic's first code round is the withdrawn one; the next is not "first" | none — as `interrupted` already counts, deliberately (`RoundRecord.PlanKey` remarks, `SessionStore.cs:122-123`); T15 pins it |
| 6 | `core/Authors/Windows.cs:21-22` `IsRecorded` | a window boundary at a round that ran nothing | **exclude**: `Windows` gains `public const string Cancelled = "cancelled";` and the clause reads `round.Status is not (Interrupted or Cancelled)` (core cannot name `RoundRecord`) |
| 7 | `core/Authors/Windows.cs:28` `IsSettled`, `:164` | requires `Done` | none — follows #6 |
| 8 | `Store/RoundAuthorsPopulation.cs:45`, `:97-99` | `NotDone` exclusion, `IsSettled` false | none — follows #6/#7; the backfill never asks a cancelled round for authors |
| 9 | `Store/AttributionMapping.cs:100-103` | `CompletedUtc` mapped only for `Done` → null | none |
| 10 | `Server/Backfill/StoredHistory.cs:142-144` | passes `Status` into a `GateRound` → #6 | none |
| 11 | `Server/Backfill/BackfillDecision.cs:62` | reads `Done` | none |
| 12 | `Server/Rounds/DeltaPredecessor.cs:45-46` | reads `Done` | none |
| 13 | `Server/Rounds/CurrentAnswer.cs:98-103` `IsNewerThanTheLastRound` | an answer the cancelled round applied is older than its `CompletedUtc` → not re-applied | none — skipping it would make the answer "newer than the last round" again; T14 pins the file after the withdrawal |
| 14 | `Server/Rounds/RoundEngine.cs:916-925` `WhatTheCallerWasDoing` (`Rounds[^1].CompletedUtc`) — **not in the parent's D9** | the next round's stretch starts at the withdrawal | none in the reader; the cancelled row carries its own stretch (§3.3) |
| 15 | `Server/Rounds/RoundSkips.cs:95` (coalesce on `Verdict: Skipped` of the LAST row) | a cancelled last row is not a skip → a following skip appends a new row | none |
| 16 | `Server/PanelService.cs:1775-1778` `_cadence.Close(session, Rounds[^1].Verdict)` and `:1793-1794` `RecordDecisions` | unreachable with a cancelled record last (§3.9) | none; T13 |
| 17 | `Server/SessionStore.cs:609-621`, `:709-710` the start sweep | reads `Running` only; a cancelled record is terminal and never swept | none |
| 18 | `Server/LiveRound.cs:256-257` `Persist` | writes `Running` until the terminal record replaces it | none in S2 (S3's D6 guard) |
| 19 | `Store/RoundsDb.cs:880-881` | the two words are written | none (T5 reads them back) |
| 20 | `Store/RoundsQuery.cs:432-458` `Read`, `FindingsOf`/`FindingsOfMany` (`Program.cs:772`, `:1635`, `:1661`) | no status/verdict column on `LoggedRound` (§3.7) | none — the parent's "render test" does not apply |
| 21 | `src_vs_code/src/roundsLog.ts:86` `LogRow.status` union | `'cancelled'` is not a member | **add `'cancelled'`** |
| 22 | `src_vs_code/src/roundsLog.ts:613-631` `statusOf` | falls through to `done` | **`if (round.status === 'cancelled') { return 'cancelled'; }`** after the `interrupted` branch (`:621-623`); `secondsOf` (`:758-780`) is NOT given a null branch — a cancelled round has a real completion time; `rowFrom :528` becomes `const noFindings = status === 'skipped' \|\| status === 'cancelled';` and feeds `foundState` (`:566`) so opening the row asks the database for nothing |
| 23 | `src_vs_code/src/roundsLog.ts:1501-1505` (badge CSS) and `:1767-1772` (`badge()`) | no `.badge.cancelled` rule → unstyled text | **add** `.badge.cancelled { background: transparent; color: var(--vscode-charts-orange); border: 1px solid var(--vscode-charts-orange); }` beside `:1505`; `badge()`: `status === 'cancelled' ? 'cancelled — not judged' : …` in the `said` chain |
| 24 | `src_vs_code/src/rounds.ts:309-311` `isRunning`, `:336-359` `elapsed`; `panelView.ts:2859` `.filter(isRunning)`; `roundCountsCache.ts:7` | not running; its real duration (it has `completedUtc`); leaves the sidebar's running list; counts as a round run, as `interrupted` does | none |

The Team server reads no `RoundRecord` (`src_server/src` has no reader — checked by the parent and not re-checked here: S2 touches nothing
under `src_server`).

## 5. The order of operations, and what each test observes

```
RunStageAsync
  :226  claim taken                                   ── using: released however the method leaves
  :235-258 session read → loaded (:251) → HeldToBase
  :260-271 stage.Begin → session (begun state, IN MEMORY ONLY)
  :289-292 budget, RoundCancellation(host, ct, budget)
  var ending = RoundEnding.Setup;                     ── NEW, before the try
  try (:305 — WarmRemoteRolesAsync :303 MOVES INSIDE, first statement)
      :309-393 sha, RefuseBeforeBuilding, number, key, skips, lease, scratch, MakeWork, empty-roster refusal
      :400  LiveRound → first `running` save
      :416  results = await RunAllAsync(…)            ── progress: live.Report / audit.Moved / ledger.Record(EndCauseOf with the mark)
      returnedAt; ending = Decide(…)                  ── §2.3, read ONCE
      Withdrawn → WithdrawAsync:                      ── §3.3
            live.Withdraw (no IO) → SaveWithdrawn (not cancellable; SessionStoreException → _log.Error, continue)
            → _projection.Write → audit → throw OperationCanceledException(ct)
      Completed → :437-660 exactly as today
  catch (OperationCanceledException) when (ending is not RoundEnding.Completed && ct.IsCancellationRequested)   ── NEW, FIRST arm
      Withdrawn → nothing more to do (the log line was written in WithdrawAsync); throw;
      Setup     → _log.Information("the {Stage} round was withdrawn during setup: nothing was launched"); throw;
  catch (SessionStoreException)            :662  unchanged (the completed branch's failed save; S3's D10 rewrites it)
  catch (WorktreeException | ContextException) :672  unchanged
  catch (Exception)                         :676  unchanged
```

What the arms must NOT do, spelled out (D11): an `OperationCanceledException` with `ending == Completed` falls through to the catch-all
exactly as today ("the round failed: …") — a completion is never re-filed as a withdrawal because something later threw; S3's D10 replaces
that sentence, S2 does not. An `OperationCanceledException` whose `ct` has NOT fired (the clock, during `MakeWork`) is not caught by the
new arm and reaches the catch-all as today: the clock is out of S2's scope. `WarmRemoteRolesAsync` moves inside the `try` because a cancel
during it (`:303`) currently escapes `RunStageAsync` unlogged; the move is the only way the setup arm can see it. `SaveWithdrawn` is a
private wrapper: `try { _store.Save(saved); } catch (SessionStoreException e) { _log.Error(e, "the withdrawal of round {Number} could not be
recorded", record.Number); }` — S3 adds the retry and the in-process map (D10); S2 only guarantees the reply is a cancellation whatever the
disk did.

What `PanelService.ReviewPlanAsync` etc. do with the thrown `OperationCanceledException`: nothing — they `await` the engine (`:643-650`,
`:762-770`) and the exception reaches the tool lambda and the SDK. What SDK 2.2.0 then sends is S1's characterisation; S2 asserts only
that no `ReviewAnswer` and no `Error(...)` sentence is produced for a withdrawn round (T1's `reply` assertion is on the exception type).

**Seams, by test** (no new seam is introduced; every observation is of a persisted or public fact):

| Fact | Seam |
|---|---|
| the session file's last round, its status/verdict/usage/reviewer rows, the state's counters and flags | `SessionStore.Load` on the test's `_data` (as `ARoundIsMintedAKeyTests.KeysInTheSessionFile` reads through `StatusAsync`), or `File.ReadAllText(store.FileFor(…))` for a read without the service |
| the `coai.db` row | `SELECT status, verdict, gating, note FROM rounds WHERE stage = $stage AND number = $n` on `Path.Combine(_data, Store.RoundsDb.FileName)` — `ARoundIsMintedAKeyTests.KeysInTheDatabase` (`:115-130`) is the shape |
| "no hold notice" | `Directory.Exists(Path.Combine(_data, "escalations")) == false \|\| Directory.GetFiles(…).Length == 0` (`Escalations.cs:127`, `:145-162`) |
| "no budget round, no hold" | `State.RoundsRunThisStage`, `State.HumanGate`, `State.AwaitingResolve` from the file |
| the ledger lines and their `endCause` | `usage.jsonl` lines, `ALocalCancellationCarriesItsSourceTests.Lines()`/`CausesOfCancelledLines()` (`:197-201`) |
| the moment a reviewer is cut, held, released | the `Reviewers` fake's `Arrived`/`Killed`/`Release` (`ALocalCancellationCarriesItsSourceTests.cs:105-166`) |
| how many saves, and what each wrote | `new SessionStore(dir) { FileWriter = … }` (`ASessionSaveMeetsABusyFileTests.cs:33`) — only for T10's engine rows, built over `RoundEngine` directly; the service-driven tests read the file |
| the reply | the awaited task throws `OperationCanceledException` (`await act.Should().ThrowAsync<OperationCanceledException>()`) |

## 6. The tests — RED with S1 alone, then green; and the plants that keep them honest

All C# tests live in one new file `src_mcp/tests/AWithdrawnRoundLeavesItsRecordTests.cs`, `[Collection("fakecli-env")]`, copying
`ALocalCancellationCarriesItsSourceTests.cs` whole: its `InitializeAsync`/`DisposeAsync` (`:37-67`), `Git` (`:69-74`), the `Reviewers`
launcher with its `Play` enum and the three `TaskCompletionSource`s (`:76-166`), `Service` (`:168-178`), `Script`, `RoundAsync`, `Lines`.
Two additions to the fake: a second provider — `Providers = [new("codex") { ExecutablePath = FakeCliExe }, new("gemini") { ExecutablePath = FakeCliExe }]`
with the play chosen per provider by a `Func<string, Play>` keyed on the provider name the adapter put in the invocation (read it from
`request.TrackAs` — `ReviewerExecutor.cs:950-953` labels every reviewer launch `provider/role` before it reaches the launcher); and a
`Play.Throttled` that answers a limit the ladder RETRIES — not E3's `"You've hit your usage limit."` (`:136`), which `RateLimit.Hopeless`
classes as spent and never retries. Take the throttle sentence from `BoundedSchedulerTests`' ladder case (`grep -n "attempt 1" src_mcp/tests/BoundedSchedulerTests.cs`);
if none fits, `"429 Too Many Requests — try again shortly"`. With `RetryLadder = [TimeSpan.FromSeconds(30)]` in the settings the
reviewer is parked on the ladder's first wait when the cancel fires. The plan stage runs one role per provider, so two providers are two reviewers; for
"queued" set `GlobalConcurrency = 1` on `PanelSettings` (`PanelService.cs:81-86`).

RED reasons are stated as the assertion message the coder will see against S1 alone (`EndedByRoundToken`, `RoundRecord.Cancelled`,
`LiveRound.Withdraw` and `RoundEndings` do not exist yet, so the file does not COMPILE until the types are stubbed — stub the four as
no-ops first, then watch each test fail for the reason named, then implement).

| # | Test | Arrange | Assert | RED with S1 alone |
|---|---|---|---|---|
| T1 | `AWithdrawnRound_IsRecordedCancelled_AndMovesNoGate` | codex `BlockUntilCancelled`, gemini `BlockUntilCancelled`; await `Arrived`; `call.CancelAsync()`; `await round` | throws `OperationCanceledException`; file: `Rounds[^1].Status == "cancelled"`, `.Verdict == "withdrawn"`, `.GatingCount == 0`, `.CompletedUtc > StartedUtc`, `.Reviewers == "0 of 2 answered, 2 cut"`; `State.RoundsRunThisStage == 0`, `HumanGate == false`, `AwaitingResolve == false`, `HoldQuestions` empty, `Stage == PlanReview` | the task completes with a `ReviewAnswer` whose `verdict` is `call_human`; the file's last round is `done`/`call_human`, `HumanGate` true, `RoundsRunThisStage` 1 |
| T2 | `AWithdrawnRound_NotifiesNobody` | as T1 | `escalations/` absent or empty | one `*.json` in `escalations/` (the hold notice `:654-656`) |
| T3 | `AWithdrawnRound_SpendsItsNumber` | T1, then `Script(Clean)` + a second `ReviewPlanAsync` with `Play.Answer` for both | the second round's record has `Number == 2`, `Status == done`; the db has two rows for `PlanReview`, numbers 1 and 2, `status` `cancelled` then `done` | round 1's db row says `done`, `verdict` `call_human`; the second call is refused `GateHeld` (the hold stands), so no row 2 |
| T4 | `AWithdrawnRound_KeepsWhatItSpent` | codex `Answer` (Clean), gemini `BlockThenHoldTheAnswer`; await codex landed (a `Landed` TCS the fake completes after returning an `Answer`); cancel; `Release`; await | `Rounds[^1].TokensIn` equals the sum of the two `usage.jsonl` lines' `tokensIn`; `UsageNote == "usage not captured"` (the killed one is `Usage.Unknown`, `ReviewerExecutor.cs:990`); `Reviewers == "1 of 2 answered, 1 cut"`; `ReviewerStates` has codex `done` and gemini `failed` with note `cancelled` | `Status` is `done` with a verdict over one answer |
| T5 | `AWithdrawnRound_IsProjected_WithNoFindings` | T1 | `SELECT status, verdict, gating, note FROM rounds WHERE stage='PlanReview' AND number=1` → `cancelled`, `withdrawn`, `0`, the §3.2 sentence; `SELECT COUNT(*) FROM findings WHERE …` → 0 | `done`, `call_human` |
| T6 | `SomeLanded_SomeCutByCancel_IsWithdrawn` `[Theory]`: `Killed`, `Ladder` | `Killed`: codex `Answer`, gemini `BlockUntilCancelled`; await codex landed, cancel, await. `Ladder`: codex `Answer`, gemini `RateLimitedThenBlock` on a 30 s ladder step; await gemini's second `Arrived` is NOT what parks it — await the progress note "rate limited on attempt 1" (`BoundedScheduler.cs:641-642`) by polling `StatusAsync` for the gemini row's `note`; cancel; await | both: throws; `Status == cancelled`; `Reviewers == "1 of 2 answered, 1 cut"`. `Ladder`: gemini's `ReviewerStates` note starts `rate limited (after 1 attempt)` | `Killed`: a `ReviewAnswer` verdict over one answer, `done`. `Ladder`: the same — and it STAYS red after the engine change until the mark exists at `BoundedScheduler.cs:647`, because an unmarked `RateLimited` is an ordinary refusal and `Decide` says `Completed` |
| T7 | `ALadderCutTurn_SaysWithdrawn` | T6 `Ladder` | gemini's last `usage.jsonl` line: `outcome` starts `rate limited`, `endCause == "withdrawn"`; codex's line: `endCause == ""` | gemini's `endCause == ""` (`EndCauseOf` is `Cancelled`-only) |
| T8 | `AQueuedTurnCutByTheCancel_SaysWithdrawn` | `GlobalConcurrency = 1`; codex `BlockUntilCancelled`, gemini `BlockUntilCancelled`; await codex `Arrived`; cancel | gemini's line: `outcome` starts `not started: the round ended while this reviewer was still queued`, `endCause == "withdrawn"`; codex's: `cancelled`, `withdrawn`; `Reviewers == "0 of 2 answered, 2 cut"` | gemini's `endCause == ""` |
| T9 | `AgainAfterAWithdrawnAgain_IsNotRefusedAsNothingSince` | copy `ARoundIsMintedAKeyTests.Again_GetsANewId` (`:183-206`) up to the new commit; then `ReviewCodeAsync(…, again: true, ct)` with both reviewers `BlockUntilCancelled`, cancel after `Arrived` → withdrawn (code round 2, `cancelled`, `Sha` = the new commit); then `Script(Clean)` and `ReviewCodeAsync(…, again: true)` with `Answer` | the third call's reply has `verdict == proceed` and the db has code rounds 1, 2 (`cancelled`), 3 (`done`) | the third call is refused with the "nothing since" sentence (`NothingSince`, `PanelService.cs:977-985`) — `LastCodeRound` picked the cancelled round's sha |
| T10 | `ExactlyOneTerminalBranch_PerInterleaving` `[Theory]` over `RoundEndings.Decide` — rows 1–9 of §2.4 as `[InlineData(cause, anyMarked, noReviewer, expected)]`: (`withdrawn`,F,F,Completed), (`withdrawn`,T,F,Withdrawn), (`hostStopped`,T,F,Withdrawn), (`roundClock`,T,F,Completed), (`unknown`,F,F,Completed), (`withdrawn`,F,T,Withdrawn), (`unknown`,F,T,Completed), (`roundClock`,F,T,Completed), (`hostStopped`,F,F,Completed) | pure | `expected` | `RoundEndings` does not exist; with a plant that returns `Withdrawn` on cause alone, row 1 is red; on marks alone, row 4 |
| T10b | `ExactlyOneTerminalBranch_PerInterleaving_ThroughTheEngine` `[Theory]`: `BothAnswered`, `OneCut`, `BothQueuedOrRunning`, `ClockFirst` | the service-driven shapes of rows 1, 2, 4, 6 (row 6: `roundTimeout: 8 s`, `BlockThenHoldTheAnswer`, cancel after `Killed`, as `TheClockFiringFirst_IsTheRoundClock…` `:363-375`) | exactly one of: (`Rounds[^1].Status == cancelled` ∧ `RoundsRunThisStage == 0` ∧ no escalation file ∧ the task threw) or (`Status == done` ∧ `RoundsRunThisStage == 1` ∧ the task returned JSON) — asserted as `(a ^ b).Should().BeTrue()` with both sides fully evaluated; plus for `BothAnswered`: `verdict == proceed` and both lines `endCause == ""`; for `ClockFirst`: `done`, `call_human`, lines say `roundClock`, the reply contains `"minute limit"` | `OneCut` and `BothQueuedOrRunning` take branch (b); every other row is a guard that is green before and after |
| T11 | `EveryReviewerLandedBeforeTheCancel_TheRoundCompletes` | codex `Answer`, gemini `Answer`, both gated: the fake completes `Landed` after both answers returned, the test cancels, then the gate the fake holds `RunAllAsync`'s return behind is released — implement as `Play.AnswerThenHold`: return the clean answer only after `Release` is set, where the test sets `Release` AFTER cancelling; the outcomes are then built after the cancel with `ct` fired but no site marks them (shape 1 needs `Cancelled`, which an `Answer` is not) | `Status == done`, `verdict == proceed`, `RoundsRunThisStage == 1`; no `endCause` on either line | guard: green before and after; red on a plant that marks `Ok` outcomes when `ct.IsCancellationRequested` |
| T12 | `ACancelDuringSetup_RecordsNothing` | the `Reviewers` fake never reaches `Arrived`; instead cancel while `MakeWork` runs: pass a `PlanInput` with a `committedPlan` and a launcher whose `git` for `cat-file`/`ls-tree` blocks until `ct` fires — simpler: `WarmRemoteRolesAsync` with a remote provider whose `BaseUrl` is a listener the test opens and never answers (`HttpListener` on a free port) — simplest and chosen: a custom `StageRun` through `service.Engine.RunStageAsync` (`ARoundIsMintedAKeyTests.cs:160-171` shape) whose `MakeWork` is `async (_, _, _, token) => { await Task.Delay(Timeout.Infinite, token); … }`; cancel after a 100 ms `Task.Delay` | throws `OperationCanceledException`; the session file's `Rounds` is EMPTY; no `coai.db` file or no `rounds` row; the log (a `Serilog` test sink, or `Logger.None` and no assertion on the line) | the exception escapes as today's catch-all sentence "the round failed: The operation was canceled" (`:684`) — the task RETURNS a string instead of throwing |
| T13 | `ResolveReadsTheCompletedRound_NotACancelledOne` | plan round `Answer` → `proceed`; `ResolveAsync(…, "[]")`; code round with both `BlockUntilCancelled` → withdrawn; then `ResolveAsync(…, "[]")` | the second resolve answers the "no completed round awaiting decisions" refusal (`RoundMachine.cs:638-641`); the file's `State.Stage` is still `CodeReview`, `PlanProceeded` true | with S1 alone the withdrawn code round completes as `call_human` with `AwaitingResolve` TRUE, so the second resolve SUCCEEDS (`humanSaysProceed` false keeps the hold, but decisions are recorded): the "refused" assertion fails — the round was judged. Listed by the parent as a guard; it is RED here for the same reason T1 is |
| T14 | `AWithdrawnRound_KeepsThePersonsAnswerApplied` | drive the gate to `call_human` (`PanelConfig.Uniform(1, 2, StagePolicy.Human)` and a reviewer that answers one `Blocking` finding, then resolve nothing — the `StagePolicy.Human` exhausted path; copy the hold set-up from the test that drives `ApplyAnyHumanDecision`, `grep -l "HumanDecision.Continue" src_mcp/tests`); write the answer file for the hold's notice id with `Continue` (`Escalations.AnswerPath`, the shape `EscalationsTests` writes); next `ReviewPlanAsync` with `BlockUntilCancelled` + cancel | the file after the withdrawal: `HumanGate == false`, `RoundsRunThisStage == 0`, `HoldQuestions` empty (the answer applied at `:241` and saved at `:112` survives); the next round begins (not `GateHeld`) | the withdrawn round completes as `call_human` and re-holds the gate: `HumanGate` true |
| T15 | `AWithdrawnFirstCodeRound_StillCountsAsTheEpicsFirst` | cadence on (`CadenceMode.Require` — copy the set-up of the `FirstCodeRound` tests in `CadenceDeskTests`), a consultation closed for the epic; code round with `plan`/`epic` declared, withdrawn; a second code round with the same declaration | the second is NOT refused for the cadence (the first code round of the epic is on record) | guard, green before and after; a plant that skips `Cancelled` at `CadenceDesk.cs:427` turns it red |
| T16 | `TheLedgerWordsAndTheRecordWords_AreTheSame` | pure | `RoundRecord.Cancelled == "cancelled"`; a withdrawn record's `Verdict` is `EndCauses.Withdrawn`; a host-stopped one's is `EndCauses.HostStopped` (drive `LiveRound.Withdraw` directly, the `LiveRoundTests` shape, `LiveRoundTests.cs:26-30`, `:84-90`) | the constants do not exist |
| T17 | `AHostStopWithTheCall_IsRecordedHostStopped` | `ALocalCancellation_CarriesItsSource_TheHostsStop_EvenWhenTheCallIsCancelledWithIt` (`:329-342`) arrange: `Service(reviewers, hostStopping: host.Token)`, `host.CancelAsync()` then `call.CancelAsync()` | `Status == cancelled`, `Verdict == "hostStopped"`, `Note == "the host stopped while the round ran"`; the lines say `hostStopped` | `done`/`call_human` |

Extension tests:

| # | Test | Where | Assert | RED |
|---|---|---|---|---|
| E1 | `a cancelled round is its own status, never done or awaiting` | `src_vs_code/src/test/roundsLog.test.ts`, the `round()`/`session()` helpers (`:32-60`) | `rowsFrom([session([round({ status: 'cancelled', verdict: 'withdrawn' })])], NOW)[0].status === 'cancelled'`; and with a `DbLog` whose matching row has `accepted: -1, foundCount: 0` the status is still `cancelled` (not `awaiting`); `seconds` is the real 130 s, not null | `'done'` |
| E2 | `a cancelled round is drawn in a state of its own — "cancelled — not judged" — never done, and opening it asks for no findings` | `src_vs_code/src/test/bundledPage.test.ts`, copy `SKIPPED_SESSION`/`SKIPPED_LOG` and the two skipped tests (`:141-186`) as `CANCELLED_SESSION` (`status: 'cancelled', verdict: 'withdrawn', reviewers: '1 of 2 answered, 1 cut', reviewerStates: [codex done 0 findings 46 s, gemini failed note 'cancelled' 12 s]`) | `/class="badge cancelled"/`, `/cancelled — not judged/`, no `/class="badge done"/`; opening: `/class="detail"/`, the two reviewer rows, no `findings` command sent | the badge is `done`; opening sends a `findings` command |

`parseUsageLine` needs no test: E3's `ALocalCauseWord_IsReadLikeAnyOther` already proves `endCauseOf` takes any word.

### Break-its (plants) — each must turn the named test red, then be reverted

| Plant | Red |
|---|---|
| B1 `Decide` returns `Withdrawn` when the cause is `withdrawn` regardless of marks | T10 row 1, T10b `BothAnswered`, T11 |
| B2 `Decide` ignores the cause (marks alone) | T10 row 4, T10b `ClockFirst` |
| B3 the mark at `BoundedScheduler.cs:647` removed | T6 `Ladder`, T7 |
| B4 the mark at `Abandoned` for the queued path removed | T8 |
| B5 `EndCauseOf` back to `Cancelled`-only | T7, T8 |
| B6 `WithdrawAsync` saves `session` (the begun state) instead of `loaded` | T9 (the stage is left `CodeReview`, so the next `again` is not `again` at all and `LastCodeRound` is not consulted — assert additionally in T9 that the file's `Stage` is `Done` after the withdrawal), T14 |
| B7 `WithdrawAsync` returns the JSON of a `ReviewAnswer` instead of throwing | T1 (throws), T10b |
| B8 the new catch arm placed AFTER the catch-all | T1: the reply is the "the round failed" sentence |
| B9 `CompleteRound` still called before the decision | T1 (`RoundsRunThisStage` 1), T2 |
| B10 `LastCodeRound` not filtered | T9 |
| B11 `Windows.IsRecorded` not excluding `cancelled` | the existing `AWindowRunsFromThePreviousRecordedRoundsStartTests` gains one case: a `cancelled` round between two `done` ones opens no window (copy its `interrupted` case) |
| B12 `statusOf` without the branch | E1, E2 |
| B13 `noFindings` not covering `cancelled` | E2's "no findings command" |
| B14 `Withdraw` sums `Ok` usage only | T4 |
| B15 the `Reviewers` sentence counts marks as "answered" | T4, T6 |

## 7. What S2 must NOT do — the PR-A boundary

- **No provisional record, no `LiveRound` cancelled flag, no observer task** (D6, S3). In S2 the file reads `running` from the last
  `Persist` until `Withdraw`'s save lands — milliseconds after `RunAllAsync` returns. S3's `ALateProgressReport_DoesNotRewriteRunning`
  is RED on this branch and must stay so until S3.
- **No retry of a failed terminal save and no in-process map of unsaved endings** (D10, S3). `SaveWithdrawn` logs and goes on; the
  completed branch's `SessionStoreException` arm (`:662-671`) keeps its "run it again" sentence.
- **No `CancellationToken.None` audit of the teardown** (D4, S3): `EndItAsync`, `AbandonAsync` (`ReviewerExecutor.cs:817-837`),
  `UsageLedger.Record`, the projection and the lease's disposal already take no caller token today; S3 pins that by test, S2 does not
  touch them.
- **No host-stop bound at the serve loop's end** (`Program.cs:2216-2228`, `StartingHost.EndBudget`), no remote `DELETE` test, no seam
  leg (S4). `InputEndSignallingTransport` is not touched; D5's transport fallback belongs to S1 if its characterisation calls for it.
- **No change to `consult`/`ask_consultants`** (`Tools.cs:492`, `:546` on `main`) — parent §1.
- **No `RoundRecord.Cancelled` handling in the start sweep** (`SessionStore.cs:599-625`): a cancelled record is terminal, the sweep reads
  `Running` only.
- **No new status word anywhere but `RoundRecord`, `Windows` and the page's union.** The `ReviewerState` rows keep `done`/`failed`; the
  ledger keeps `EndCauses`.
- **No documentation beyond the three module files named by the parent's §9** — and in `module_server.md:2346` the sentence from
  "**Measured limit, reported rather than fixed:**" to the paragraph's end is REPLACED by two sentences: the four review tools take the
  request's token (S1), and a round the caller's token cut is recorded `cancelled`/`withdrawn` (this spec, D2–D3), with `hostStopped`
  when the host was stopping. `module_runners.md` gains the mark (§2.1) in its `EndCauses` paragraph; `module_extension.md` gains the
  `cancelled` row where the rounds-log statuses are listed (`grep -n "awaiting decisions" research/module_extension.md`).

## 8. Open questions — with the recommended answer

1. **Should a cancelled code round record its `Sha`?** Not derivable from the plan beyond "as today" (D3). Recommended: **yes** (§3.3,
   §3.8), because the row says what was mounted and every reader that chooses a comparison commit (`LastCodeRound`, `DeltaPredecessor`,
   `FeatureStage.LastReviewed`) now filters by status; an empty `Sha` would hide the fact and would make the `again` refusal depend on an
   absence rather than a rule.
2. **Should the withdraw row compute `agent_log` (`WhatTheCallerWasDoing`)?** D3 says only "projected, no findings". Recommended: **yes**
   (§3.3), because `RoundEngine.cs:918` makes the NEXT round's stretch start at the cancelled row's `CompletedUtc`; without the row's own
   stretch the caller's work before the withdrawal is attributed to nobody. It is a best-effort transcript read, as every row's is.
3. **Should a clock-cut queued/ladder turn now say `roundClock` on its line?** A consequence of writing the cause on the mark (§2.2),
   not a decision the plan took in words. Recommended: **yes, and say so in `module_runners.md`**: the line was blank for a turn the
   deadline ended, which is the under-report E3 set out to end; E3's `ATurnThatWasNotCancelled_CarriesNoCause` stays green because its
   `RateLimited` play is a hopeless limit, never a cut.
4. **The `Reviewers` sentence and the `Note` wording** (§3.2) are chosen here, not by the plan. They are the only two strings a person
   reads for a withdrawn round, and the page shows them in the row and the detail. Change them if a reviewer has better words; keep the
   counts (`answered`, `cut`) because T4/T6 assert on them.

## 9. Definition of Done (S2)

- [ ] `EndedByRoundToken` set at the four sites of §2.1 and nowhere else; `EndCauseOf` reads the mark; E3's whole
      `ALocalCancellationCarriesItsSourceTests` still green.
- [ ] `RoundEndings.Decide` is the only place the withdraw-or-complete question is asked; `ending` is read by the first catch arm; the
      withdraw path never reaches `CompleteRound`, `NoticeIfAPersonMustDecide`, `_escalations.Notify`, `SpentPrompts`.
- [ ] `RoundRecord.Cancelled`, `LiveRound.Withdraw`, the saved session is `loaded` plus the record; the db row carries the two words.
- [ ] The eight readers of §4 changed; the sixteen others listed with the reason they need none; T13/T14/T15 pin three of them.
- [ ] T1–T17 and E1–E2 watched RED for the stated reason, then green; B1–B15 each turned its test red and were reverted; the whole
      `src_mcp` suite (the test executable, never `dotnet test`) and `npm test` green.
- [ ] §7 untouched: nothing of S3/S4 landed in PR A.
- [ ] The three module docs updated as §7's last bullet says; the parent plan's `Epics` section marks S2's cornerstone as specified
      (this file), not built, until PR A merges.

## 10. What the plan round changed (2026-10-21, session 1831c0a3)

These override the sections above where they differ.

1. **The setup and withdraw catch arms test the ROUND token, not the caller's.** The filter is `ending is not Completed && cancellation.Token.IsCancellationRequested`. `RoundCancellation` records the cause and only then cancels that token, so a cancelled round is never seen with its token clear. An `OperationCanceledException` a setup step raises for its OWN reason (its timeout, its own token) while the round token is clear falls to today's failure arm. That is correct: no person withdrew it. Test: `ASetupStepsOwnTimeout_IsAFailure_NotAWithdrawal`.
2. **A turn is marked cut only when the ROUND token ended it** (finding 3). In the OCE catch and at the other cut sites, the mark is `e.CancellationToken == roundToken || (roundToken.IsCancellationRequested && cancellation.FiredAt <= observedAt)`, where `observedAt` is the `Stopwatch` timestamp taken as the catch is entered and `FiredAt` is the first source's recorded moment (`RoundCancellation` gains a read of it). An adapter that threw for its own reason before the caller cancelled is NOT marked. Test: `AnAdaptersOwnCancellation_BeforeTheCallerCancels_IsNotACut`: the adapter throws, a gate holds the catch, the caller cancels, the gate is released → no mark, and the round completes as today.
3. **`returnedAt` is taken INSIDE the scheduler** (finding 4). `RunAllAsync` stamps `Stopwatch.GetTimestamp()` as its last act and returns it with the results. The engine never reads the clock after the await. Test: `ACancelAfterTheSchedulerReturned_CompletesTheRound`: a lane-only round, a cancel fired between the scheduler's return and the engine's continuation (a seam) → `Completed`.
4. **A cancelled row opens cleanly in the rounds log** (finding 2). Expanding a `cancelled` row renders its reviewer states and the withdrawal note and asks the database for nothing. Test: `ACancelledRow_ExpandsToItsReviewersAndNote` in `bundledPage.test.ts` (no thrown error, no findings request, both texts present).
5. **Rejected: `SaveWithdrawn` swallowing a failed save** (finding 1). It is decided: D10 is story S3 (PR B), which makes a failed withdraw save never read `running` (provisional write, in-process map, startup sweep). The gap is closed by RELEASE ORDER: **PR A and PR B ship in the same coai-mcp release, and no release is cut between their merges.** PR A's description says so, and so does the release checklist.
