using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// S2 of <c>todo/PLAN_a_cancelled_review_stops_its_round.md</c> (its spec: <c>..._S2_spec.md</c>): a round the caller withdrew — or the host
/// stopped — leaves ONE terminal record, <c>cancelled</c> with the cause as its verdict, and judges nothing: no verdict, no budget round, no hold,
/// no phone notice, no findings, no spent lens. The gate is exactly where the round found it.
/// </summary>
/// <remarks>
/// Driven end to end through the real <see cref="PanelService"/> (<see cref="WithdrawalRig"/>): the reviewer's process is played, the cancel is the
/// request token an MCP client's <c>notifications/cancelled</c> fires, and every fact is read from the session file, the database or the ledger —
/// never from a seam the product added for the test.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AWithdrawnRoundLeavesItsRecordTests : IAsyncLifetime
{
    private static readonly string[] Both = ["codex", "gemini"];

    private WithdrawalRig _rig = null!;

    public async ValueTask InitializeAsync() => _rig = await WithdrawalRig.CreateAsync();

    public async ValueTask DisposeAsync() => await _rig.DisposeAsync();

    /// <summary>A plan round in flight: its service, its played reviewers, the call's own token and the task of the call.</summary>
    private sealed record Run(PanelService Service, WithdrawalRig.Reviewers Reviewers, CancellationTokenSource Call, Task<string> Round)
    {
        public Task Arrived(params string[] providers) =>
            Task.WhenAll(providers.Select(p => Reviewers.Of(p).Arrived.Task)).WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

        /// <summary>The first of these providers to be launched — under a global cap of one, which of two goes first is not the test's to say.</summary>
        public async Task ArrivedFirst(params string[] providers) =>
            await (await Task.WhenAny(providers.Select(p => Reviewers.Of(p).Arrived.Task)).WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken));

        public Task Landed(string provider) =>
            Reviewers.Of(provider).Landed.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

        public Task Killed(string provider) =>
            Reviewers.Of(provider).Killed.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

        public async Task<Exception?> EndedAsync() => await Record.ExceptionAsync(() => Round);
    }

    private async Task<Run> StartPlanAsync(
        Func<string, Play> play,
        string[]? providers = null,
        TimeSpan roundTimeout = default,
        int globalConcurrency = 3,
        TimeSpan[]? ladder = null,
        CancellationToken hostStopping = default)
    {
        var reviewers = _rig.Playing(play);
        var service = _rig.Service(reviewers, providers ?? Both, roundTimeout, hostStopping, globalConcurrency, ladder);
        await service.OpenAsync(_rig.Repo, "feature");
        var call = new CancellationTokenSource();

        return new Run(service, reviewers, call, service.ReviewPlanAsync(_rig.Repo, "feature", "a plan", call.Token));
    }

    /// <summary>Both reviewers blocked, the call cancelled once both are under way, the call's end awaited.</summary>
    private async Task<(Run Run, Exception? Thrown)> WithdrawnPlanAsync(Func<string, Play>? play = null)
    {
        var run = await StartPlanAsync(play ?? (_ => Play.BlockUntilCancelled));
        await run.Arrived(Both);
        await run.Call.CancelAsync();

        return (run, await run.EndedAsync());
    }

    // ---------- the record ----------

    [Fact]
    public async Task AWithdrawnRound_IsRecordedCancelled_AndMovesNoGate()
    {
        var (run, thrown) = await WithdrawnPlanAsync();

        var session = _rig.Session(run.Service);
        var round = session.Rounds[^1];
        round.Status.Should().Be(RoundRecord.Cancelled, "the call was withdrawn: the round is cancelled, not judged");
        round.Verdict.Should().Be(EndCauses.Withdrawn);
        round.GatingCount.Should().Be(0);
        round.CompletedUtc.Should().BeAfter(round.StartedUtc);
        round.Reviewers.Should().Be("0 of 2 answered, 2 cut");
        session.State.RoundsRunThisStage.Should().Be(0, "a withdrawn round spends no budget round");
        session.State.HumanGate.Should().BeFalse("a withdrawn round holds nobody");
        session.State.AwaitingResolve.Should().BeFalse();
        session.State.HoldQuestions.Should().BeEmpty();
        session.State.Stage.Should().Be(Stage.PlanReview);
        thrown.Should().BeAssignableTo<OperationCanceledException>("the reply is a cancellation, never a verdict or a sentence");
    }

    [Fact]
    public async Task AWithdrawnRound_NotifiesNobody()
    {
        await WithdrawnPlanAsync();

        _rig.HoldNotices().Should().BeEmpty("the person pressed the button: there is no decision to put to them");
    }

    [Fact]
    public async Task AWithdrawnRound_SpendsItsNumber()
    {
        var play = Play.BlockUntilCancelled;
        var run = await StartPlanAsync(_ => play);
        await run.Arrived(Both);
        await run.Call.CancelAsync();
        await run.EndedAsync();

        play = Play.Answer;
        var second = WithdrawalRig.Parse(await run.Service.ReviewPlanAsync(_rig.Repo, "feature", "a plan"));

        second.GetProperty("verdict").GetString().Should().Be("proceed", $"the next call begins from the state the withdrawn one began from: {second}");
        _rig.Session(run.Service).Rounds.Select(r => (r.Number, r.Status)).Should().Equal([(1, "cancelled"), (2, "done")]);
        _rig.Db("SELECT number, status FROM rounds WHERE stage = 'PlanReview' ORDER BY number")
            .Select(r => $"{r[0]}:{r[1]}").Should().Equal("1:cancelled", "2:done");
    }

    [Fact]
    public async Task AWithdrawnRound_KeepsWhatItSpent()
    {
        var run = await StartPlanAsync(p => p == "codex" ? Play.Answer : Play.BlockThenHoldTheAnswer);
        try
        {
            await run.Landed("codex");
            await run.Arrived("gemini");
            await run.Call.CancelAsync();
            await run.Killed("gemini");
        }
        finally
        {
            run.Reviewers.Of("gemini").Release.TrySetResult();
        }

        await run.EndedAsync();

        var round = _rig.LastRound(run.Service);
        round.Status.Should().Be(RoundRecord.Cancelled);
        round.Reviewers.Should().Be("1 of 2 answered, 1 cut");
        round.UsageNote.Should().Be("usage not captured", "the killed reviewer's vendor never reported: the total is a floor, and it says so");
        round.ReviewerStates.Single(s => s.Provider == "codex").Status.Should().Be(ReviewerState.Done);
        round.ReviewerStates.Single(s => s.Provider == "gemini").Status.Should().Be(ReviewerState.Failed);
        round.TokensIn.Should().Be(_rig.Lines().Sum(l => l.TryGetProperty("tokensIn", out var t) ? t.GetInt64() : 0),
            "the round's total is every ledger line's, the killed reviewer's included");
    }

    [Fact]
    public async Task AWithdrawnRound_IsProjected_WithNoFindings()
    {
        var (run, _) = await WithdrawnPlanAsync();

        var row = _rig.Db("SELECT status, verdict, gating, note FROM rounds WHERE stage = 'PlanReview' AND number = 1").Should().ContainSingle().Which;
        row.Should().Equal("cancelled", "withdrawn", "0", "the caller withdrew the call while the round ran");
        _rig.Db("SELECT COUNT(*) FROM findings").Single()[0].Should().Be("0");
        _rig.LastRound(run.Service).Verdict.Should().Be("withdrawn");
    }

    [Theory]
    [InlineData("Killed")]
    [InlineData("Ladder")]
    public async Task SomeLanded_SomeCutByCancel_IsWithdrawn(string shape)
    {
        var ladder = shape == "Ladder";
        var run = await StartPlanAsync(
            p => p == "codex" ? Play.Answer : ladder ? Play.Throttled : Play.BlockUntilCancelled,
            ladder: ladder ? [TimeSpan.FromSeconds(30)] : null);
        await run.Landed("codex");
        await run.Arrived("gemini");
        if (ladder)
        {
            (await _rig.SessionAsync(
                run.Service,
                s => s.Rounds.LastOrDefault()?.ReviewerStates.Any(r => r.Provider == "gemini" && r.Note.StartsWith("rate limited on attempt 1", StringComparison.Ordinal)) == true,
                TimeSpan.FromSeconds(60))).Should().BeTrue("the second reviewer is parked on the ladder's first wait");
        }

        await run.Call.CancelAsync();
        var thrown = await run.EndedAsync();

        var round = _rig.LastRound(run.Service);
        round.Status.Should().Be(RoundRecord.Cancelled, "one answer is not a panel: a partial round is never judged");
        round.Reviewers.Should().Be("1 of 2 answered, 1 cut");
        if (ladder)
        {
            round.ReviewerStates.Single(s => s.Provider == "gemini").Note.Should().Contain("rate limited");
        }

        thrown.Should().BeAssignableTo<OperationCanceledException>();
    }

    [Fact]
    public async Task ALadderCutTurn_SaysWithdrawn()
    {
        var run = await StartPlanAsync(p => p == "codex" ? Play.Answer : Play.Throttled, ladder: [TimeSpan.FromSeconds(30)]);
        await run.Landed("codex");
        (await _rig.SessionAsync(
            run.Service,
            s => s.Rounds.LastOrDefault()?.ReviewerStates.Any(r => r.Provider == "gemini" && r.Note.StartsWith("rate limited on attempt 1", StringComparison.Ordinal)) == true,
            TimeSpan.FromSeconds(60))).Should().BeTrue();
        await run.Call.CancelAsync();
        await run.EndedAsync();

        var gemini = _rig.LastLineOf("gemini");
        WithdrawalRig.Text(gemini, "outcome").Should().StartWith("rate limited", "the refusal it last saw stays the line's outcome");
        WithdrawalRig.Text(gemini, "endCause").Should().Be(EndCauses.Withdrawn, "the cancel ended this turn, whatever its outcome type");
        WithdrawalRig.Text(_rig.LastLineOf("codex"), "endCause").Should().BeEmpty("it answered on its own terms");
    }

    [Fact]
    public async Task AQueuedTurnCutByTheCancel_SaysWithdrawn()
    {
        var run = await StartPlanAsync(_ => Play.BlockUntilCancelled, globalConcurrency: 1);
        await run.ArrivedFirst(Both);
        await run.Call.CancelAsync();
        await run.EndedAsync();

        var lines = Both.Select(_rig.LastLineOf).ToList();
        lines.Select(l => WithdrawalRig.Text(l, "endCause")).Should().OnlyContain(c => c == EndCauses.Withdrawn, "the one that ran was killed, the one that waited never got its slot");
        lines.Select(l => WithdrawalRig.Text(l, "outcome")).Should().ContainSingle(o => o.StartsWith("not started: the round ended while this reviewer was still queued", StringComparison.Ordinal));
        _rig.LastRound(run.Service).Reviewers.Should().Be("0 of 2 answered, 2 cut");
    }

    [Fact]
    public async Task AHostStopWithTheCall_IsRecordedHostStopped()
    {
        using var host = new CancellationTokenSource();
        var run = await StartPlanAsync(_ => Play.BlockUntilCancelled, hostStopping: host.Token);
        await run.Arrived(Both);

        // A stopping host cancels the request it was serving as well: that is the host's doing, not a withdrawal.
        await host.CancelAsync();
        await run.Call.CancelAsync();
        await run.EndedAsync();

        var round = _rig.LastRound(run.Service);
        round.Status.Should().Be(RoundRecord.Cancelled);
        round.Verdict.Should().Be(EndCauses.HostStopped);
        round.Note.Should().Be("the host stopped while the round ran");
        _rig.Lines().Select(l => WithdrawalRig.Text(l, "endCause")).Should().OnlyContain(c => c == EndCauses.HostStopped);
    }

    // ---------- again, resolve, the person's answer ----------

    private async Task<(PanelService Service, Func<Play> Current, Action<Play> Set)> ThroughTheCodeStageAsync()
    {
        var play = Play.Answer;
        var reviewers = _rig.Playing(_ => play);
        var service = _rig.Service(reviewers, ["codex"]);
        await service.OpenAsync(_rig.Repo, "feature");
        await service.ReviewPlanAsync(_rig.Repo, "feature", "a plan nobody objects to");
        await service.ResolveAsync(_rig.Repo, "feature", "[]");

        return (service, () => play, p => play = p);
    }

    [Fact]
    public async Task AgainAfterAWithdrawnAgain_IsNotRefusedAsNothingSince()
    {
        var (service, _, set) = await ThroughTheCodeStageAsync();
        WithdrawalRig.Parse(await service.ReviewCodeAsync(_rig.Repo, "feature", "main", WithdrawalRig.Scope)).GetProperty("verdict").GetString().Should().Be("proceed");
        await service.ResolveAsync(_rig.Repo, "feature", "[]");
        await _rig.CommitAgainAsync("the checkpoint's follow-up\n");

        set(Play.BlockUntilCancelled);
        using var call = new CancellationTokenSource();
        var again = service.ReviewCodeAsync(_rig.Repo, "feature", "main", WithdrawalRig.Scope, again: true, call.Token);
        // The code stage runs several roles: wait until the round is on disk with a reviewer under way.
        (await _rig.SessionAsync(service, s => s.Rounds.LastOrDefault() is { Status: "running" }, TimeSpan.FromSeconds(60))).Should().BeTrue();
        await call.CancelAsync();
        await Record.ExceptionAsync(() => again);

        var afterWithdrawal = _rig.Session(service);
        afterWithdrawal.Rounds[^1].Status.Should().Be(RoundRecord.Cancelled);
        afterWithdrawal.Rounds[^1].Sha.Should().NotBeEmpty("the row says what was mounted");
        afterWithdrawal.State.Stage.Should().Be(Stage.Done, "the reopen was in memory only: the saved state is the one the withdrawn round began from");

        set(Play.Answer);
        var third = WithdrawalRig.Parse(await service.ReviewCodeAsync(_rig.Repo, "feature", "main", WithdrawalRig.Scope, again: true));
        third.GetProperty("verdict").GetString().Should().Be("proceed", $"the commit the withdrawn round never read is new again: {third}");
        _rig.Db("SELECT number, status FROM rounds WHERE stage = 'CodeReview' ORDER BY number")
            .Select(r => r[1]).Should().Equal("done", "cancelled", "done");
    }

    [Fact]
    public async Task ResolveReadsTheCompletedRound_NotACancelledOne()
    {
        var (service, _, set) = await ThroughTheCodeStageAsync();
        set(Play.BlockUntilCancelled);
        using var call = new CancellationTokenSource();
        var code = service.ReviewCodeAsync(_rig.Repo, "feature", "main", WithdrawalRig.Scope, again: false, call.Token);
        (await _rig.SessionAsync(service, s => s.Rounds.LastOrDefault() is { Status: "running", Stage: "CodeReview" }, TimeSpan.FromSeconds(60))).Should().BeTrue();
        await call.CancelAsync();
        await Record.ExceptionAsync(() => code);

        var resolved = await service.ResolveAsync(_rig.Repo, "feature", "[]");

        resolved.Should().Contain("no completed round awaiting decisions", "a withdrawn round judged nothing: there is nothing to resolve");
        var session = _rig.Session(service);
        session.State.Stage.Should().Be(Stage.CodeReview);
        session.State.PlanProceeded.Should().BeTrue();
        session.Rounds[^1].Status.Should().Be(RoundRecord.Cancelled);
    }

    [Fact]
    public async Task AWithdrawnRound_KeepsThePersonsAnswerApplied()
    {
        var reviewers = _rig.PlayAll(Play.BlockUntilCancelled);
        var service = _rig.Service(reviewers, ["codex"]);
        await service.OpenAsync(_rig.Repo, "feature");
        // A gate held by a round that ended call_human, and the person's "keep going" on the notice that was asked for that hold.
        var opened = _rig.Session(service);
        var held = new RoundRecord(nameof(Stage.PlanReview), 1, "call_human", 2, "all 1 reviewers answered", DateTime.UtcNow.AddMinutes(-10))
        {
            StartedUtc = DateTime.UtcNow.AddMinutes(-11),
        };
        service.Store.Save(opened with
        {
            State = opened.State with { Stage = Stage.PlanReview, RoundsRunThisStage = 3, HumanGate = true, HoldQuestions = ["q1"] },
            Rounds = [held],
        });
        var escalations = new Escalations(_rig.Data);
        escalations.Notify(new EscalationQuestion(
            "q1", opened.State.SessionId, _rig.Repo, "feature", "The plan review gate needs your decision", "", "en", "", [],
            DateTime.UtcNow.AddMinutes(-9).ToString("O")));
        await File.WriteAllTextAsync(
            escalations.AnswerPath("q1"),
            $$"""{"id":"q1","answer":"go","decision":"continue","answeredUtc":"{{DateTime.UtcNow.AddMinutes(-5):O}}"}""",
            TestContext.Current.CancellationToken);

        using var call = new CancellationTokenSource();
        var round = service.ReviewPlanAsync(_rig.Repo, "feature", "a plan", call.Token);
        await reviewers.Of("codex").Arrived.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        await call.CancelAsync();
        await Record.ExceptionAsync(() => round);

        var session = _rig.Session(service);
        session.Rounds[^1].Status.Should().Be(RoundRecord.Cancelled);
        session.Rounds[^1].Number.Should().Be(2, "the held round keeps its number");
        session.State.HumanGate.Should().BeFalse("the person's answer was applied before the round and saved with the claim: a withdrawal does not re-hold the gate");
        session.State.RoundsRunThisStage.Should().Be(0, "'keep going' gave the stage a fresh set of rounds, and the withdrawn one spent none");
        session.State.HoldQuestions.Should().BeEmpty();
    }

    [Theory]
    [InlineData(EndCauses.Withdrawn, "the caller withdrew the call while the round ran")]
    [InlineData(EndCauses.HostStopped, "the host stopped while the round ran")]
    public void TheLedgerWordsAndTheRecordWords_AreTheSame(string cause, string note)
    {
        RoundRecord.Cancelled.Should().Be("cancelled");
        RoundRecord.Cancelled.Should().Be(RoundStatus.Cancelled, "one spelling, in core, for the server's record and the authors windows alike");
        var directory = Directory.CreateTempSubdirectory("coai-withdraw-live-");
        try
        {
            var session = new PersistedSession(new SessionState("s-live", "/repo", "feature/x", new PanelConfig()), []);
            var work = new ReviewerWork(new ReviewerInvocation("codex", "Architecture", new CoaiMcp.Runners.Processes.ProcessRequest("cli", [], ".")));
            var live = new LiveRound(new SessionStore(directory.FullName), session, 1, [work], "", Noticing.None);

            var record = live.Withdraw(cause, 1, [], KeptFindings.None);

            record.Status.Should().Be(RoundRecord.Cancelled);
            record.Verdict.Should().Be(cause, "the verdict column is the ledger's word, never 'cancelled'");
            record.Note.Should().Be(note);
            record.Reviewers.Should().Be("0 of 1 answered, 1 cut");
            record.CompletedUtc.Should().BeOnOrAfter(record.StartedUtc);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    // ---------- the decision, once ----------

    [Theory]
    [InlineData(EndCauses.Withdrawn, false, false, RoundEnding.Completed)]
    [InlineData(EndCauses.Withdrawn, true, false, RoundEnding.Withdrawn)]
    [InlineData(EndCauses.HostStopped, true, false, RoundEnding.Withdrawn)]
    [InlineData(EndCauses.RoundClock, true, false, RoundEnding.Completed)]
    [InlineData(EndCauses.Unknown, false, false, RoundEnding.Completed)]
    [InlineData(EndCauses.Withdrawn, false, true, RoundEnding.Withdrawn)]
    [InlineData(EndCauses.Unknown, false, true, RoundEnding.Completed)]
    [InlineData(EndCauses.RoundClock, false, true, RoundEnding.Completed)]
    [InlineData(EndCauses.HostStopped, false, false, RoundEnding.Completed)]
    public void ExactlyOneTerminalBranch_PerInterleaving(string cause, bool anyTurnMarked, bool noReviewer, RoundEnding expected)
    {
        RoundEndings.Decide(cause, anyTurnMarked, noReviewer).Should().Be(expected);
    }

    [Fact]
    public void ACancelAfterTheSchedulerReturned_CompletesTheRound()
    {
        // A lane-only round: nobody launched, the scheduler returns at once, and the caller cancels between its return and the engine's continuation.
        using var caller = new CancellationTokenSource();
        using var cancellation = new RoundCancellation(CancellationToken.None, caller.Token, TimeSpan.FromMinutes(5));
        var returned = new SchedulerRun([], WithdrawalRig.Now());

        caller.Cancel();

        RoundEndings.Decide(cancellation, returned, noReviewer: true).Should().Be(RoundEnding.Completed,
            "the cancel fired after the scheduler returned: the round is read at the moment the scheduler returned, never later");
    }

    [Fact]
    public void ACancelBeforeTheSchedulerReturned_WithdrawsALaneOnlyRound()
    {
        using var caller = new CancellationTokenSource();
        using var cancellation = new RoundCancellation(CancellationToken.None, caller.Token, TimeSpan.FromMinutes(5));

        caller.Cancel();
        var returned = new SchedulerRun([], WithdrawalRig.Now());

        RoundEndings.Decide(cancellation, returned, noReviewer: true).Should().Be(RoundEnding.Withdrawn);
    }

    private async Task<string> BranchAsync(Run run, Exception? thrown)
    {
        var session = _rig.Session(run.Service);
        var last = session.Rounds[^1];
        var withdrawn = thrown is OperationCanceledException && last.Status == RoundRecord.Cancelled
            && session.State.RoundsRunThisStage == 0 && _rig.HoldNotices().Length == 0;
        var completed = thrown is null && last.Status == RoundRecord.Done && session.State.RoundsRunThisStage == 1;
        await Task.CompletedTask;

        return (withdrawn, completed) switch
        {
            (true, false) => "withdrawn",
            (false, true) => "completed",
            _ => $"inconsistent: thrown={thrown?.GetType().Name ?? "none"}, status={last.Status}, rounds={session.State.RoundsRunThisStage}, notices={_rig.HoldNotices().Length}",
        };
    }

    [Theory]
    [InlineData("BothAnswered", "completed")]
    [InlineData("OneCut", "withdrawn")]
    [InlineData("BothQueuedOrRunning", "withdrawn")]
    [InlineData("ClockFirst", "completed")]
    public async Task ExactlyOneTerminalBranch_PerInterleaving_ThroughTheEngine(string interleaving, string expected)
    {
        Run run;
        switch (interleaving)
        {
            case "BothAnswered":
                run = await StartPlanAsync(_ => Play.Answer);
                var reply = await run.Round;
                await run.Call.CancelAsync();
                WithdrawalRig.Parse(reply).GetProperty("verdict").GetString().Should().Be("proceed");
                _rig.Lines().Select(l => WithdrawalRig.Text(l, "endCause")).Should().OnlyContain(c => c.Length == 0);
                (await BranchAsync(run, null)).Should().Be(expected);
                return;
            case "OneCut":
                run = await StartPlanAsync(p => p == "codex" ? Play.Answer : Play.BlockUntilCancelled);
                await run.Landed("codex");
                await run.Arrived("gemini");
                await run.Call.CancelAsync();
                break;
            case "BothQueuedOrRunning":
                run = await StartPlanAsync(_ => Play.BlockUntilCancelled, globalConcurrency: 1);
                await run.ArrivedFirst(Both);
                await run.Call.CancelAsync();
                break;
            default:
                run = await StartPlanAsync(_ => Play.BlockThenHoldTheAnswer, providers: ["codex"], roundTimeout: TimeSpan.FromSeconds(8));
                await run.Killed("codex");
                await run.Call.CancelAsync();
                run.Reviewers.Of("codex").Release.TrySetResult();
                break;
        }

        var thrown = await run.EndedAsync();

        (await BranchAsync(run, thrown)).Should().Be(expected);
        if (interleaving == "ClockFirst")
        {
            _rig.LastRound(run.Service).Verdict.Should().Be("call_human", "the clock cut every reviewer first: the deadline round, as today");
            _rig.Lines().Select(l => WithdrawalRig.Text(l, "endCause")).Should().OnlyContain(c => c == EndCauses.RoundClock);
        }
    }

    [Fact]
    public async Task EveryReviewerLandedBeforeTheCancel_TheRoundCompletes()
    {
        var run = await StartPlanAsync(_ => Play.AnswerThenHold);
        await run.Landed("codex");
        await run.Landed("gemini");

        // Both answers are paid for; the cancel fires; THEN the reviewers return — the outcomes are built after the fire, and nothing marks them.
        await run.Call.CancelAsync();
        run.Reviewers.Of("codex").Release.TrySetResult();
        run.Reviewers.Of("gemini").Release.TrySetResult();
        var thrown = await run.EndedAsync();

        thrown.Should().BeNull("real, paid answers: the round completes");
        var session = _rig.Session(run.Service);
        session.Rounds[^1].Status.Should().Be(RoundRecord.Done);
        session.Rounds[^1].Verdict.Should().Be("proceed");
        session.State.RoundsRunThisStage.Should().Be(1);
        _rig.Lines().Select(l => WithdrawalRig.Text(l, "endCause")).Should().OnlyContain(c => c.Length == 0);
    }

    // ---------- setup ----------

    private StageRun SetupStage(PanelService service, Func<CancellationToken, Task> makeWork) =>
        new(RoundMachine.BeginPlanRound, false, Stage.PlanReview, false,
            async (_, _, _, token) =>
            {
                await makeWork(token);

                return new RoundWork([], []);
            })
        {
            Session = new SessionRule.CreateIfAbsent(() => new PersistedSession(
                new SessionState("setup", _rig.Repo, "feature", service.Settings.Rounds) { Stage = Stage.PlanReview }, [])),
        };

    [Theory]
    [InlineData("caller", RoundEnding.Setup, true)]
    [InlineData("host", RoundEnding.Setup, true)]
    [InlineData("clock", RoundEnding.Setup, false)]
    [InlineData("none", RoundEnding.Setup, false)]
    [InlineData("caller", RoundEnding.Completed, false)]
    [InlineData("none", RoundEnding.Withdrawn, true)]
    public void OnlyAPersonsOrTheHostsCancel_IsAWithdrawal(string source, RoundEnding ending, bool expected)
    {
        // The round token is cancelled by the caller AND by the round's clock; a setup step that ran out of the DEADLINE is a failure, not a withdrawal.
        using var host = new CancellationTokenSource();
        using var caller = new CancellationTokenSource();
        using var cancellation = new RoundCancellation(host.Token, caller.Token, source == "clock" ? TimeSpan.FromMilliseconds(30) : TimeSpan.FromMinutes(5));
        switch (source)
        {
            case "caller":
                caller.Cancel();
                break;
            case "host":
                host.Cancel();
                caller.Cancel();
                break;
            case "clock":
                cancellation.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(30)).Should().BeTrue("the 30 ms clock fires");
                break;
        }

        RoundEndings.IsAWithdrawal(ending, cancellation).Should().Be(expected);
    }

    [Fact]
    public async Task ACancelDuringSetup_RecordsNothing()
    {
        var log = new WithdrawalRig.LogSink();
        var service = _rig.Service(_rig.PlayAll(Play.Answer), ["codex"], log: new Serilog.LoggerConfiguration().WriteTo.Sink(log).CreateLogger());
        using var call = new CancellationTokenSource();
        var round = service.Engine.RunStageAsync(
            _rig.Repo, "feature", "a plan", SetupStage(service, token => Task.Delay(Timeout.Infinite, token)), call.Token);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        await call.CancelAsync();

        var thrown = await Record.ExceptionAsync(() => round);

        _rig.Session(service).Rounds.Should().BeEmpty("nothing was launched: no record, no number, no ledger line");
        log.Lines.Should().Contain(l => l.Contains("withdrawn during setup"), "the cancel is said once, though nothing is recorded");
        File.Exists(Path.Combine(_rig.Data, "usage.jsonl")).Should().BeFalse();
        thrown.Should().BeAssignableTo<OperationCanceledException>("a cancel during setup is a cancellation, not 'the round failed'");
    }

    [Fact]
    public async Task ASetupStepsOwnTimeout_IsAFailure_NotAWithdrawal()
    {
        var service = _rig.Service(_rig.PlayAll(Play.Answer), ["codex"]);
        using var call = new CancellationTokenSource();

        var reply = await service.Engine.RunStageAsync(
            _rig.Repo, "feature", "a plan",
            SetupStage(service, async _ =>
            {
                using var own = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
                await Task.Delay(Timeout.Infinite, own.Token);
            }),
            call.Token);

        reply.Should().Contain("the round failed", "nobody withdrew it: a step that timed out on its own token is today's failure sentence");
    }
}
