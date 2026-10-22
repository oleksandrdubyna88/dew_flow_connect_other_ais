using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using System.Collections.Immutable;
using CoaiMcp.Core.Gate;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// G4 of <c>todo/PLAN_a_cancelled_review_stops_its_round.md</c>, as revised on 2026-10-22: a withdrawal still stops every reviewer and still
/// never computes a verdict from a partial panel — but it never DISCARDS a finding that landed. The gated agent must not be able to cancel past
/// an adverse reviewer and retry. Every landed finding is kept on the cancelled record (session file) and in <c>coai.db</c>; a landed finding that
/// gates holds the gate for a person, durably, before the session claim is released; with none that gates the withdrawal stays free.
/// </summary>
/// <remarks>Driven through the real <see cref="PanelService"/> like <see cref="AWithdrawnRoundLeavesItsRecordTests"/>: codex answers with a finding, gemini waits for the cancel.</remarks>
[Collection("fakecli-env")]
public sealed class AWithdrawnRoundKeepsWhatLandedTests : IAsyncLifetime
{
    private const string Blocking = """
        {"findings": [{"severity": "blocking", "category": "completeness", "file": "", "line": 0,
          "title": "The rollback is missing", "why": "nothing undoes the migration if step two fails", "fix": "add the down step"}]}
        """;

    private const string Minor = """
        {"findings": [{"severity": "minor", "category": "clarity", "file": "", "line": 0,
          "title": "The heading is vague", "why": "a reader cannot tell what the section decides", "fix": "name the decision"}]}
        """;

    private static readonly string[] Both = ["codex", "gemini"];

    private WithdrawalRig _rig = null!;

    /// <summary>What gemini does: it waits for the cancel, until a test lets the next round through.</summary>
    private Play _gemini = Play.BlockUntilCancelled;

    public async ValueTask InitializeAsync() => _rig = await WithdrawalRig.CreateAsync();

    public async ValueTask DisposeAsync() => await _rig.DisposeAsync();

    /// <summary>
    /// A plan round whose codex reviewer answers <paramref name="answer"/> and whose gemini reviewer waits for the cancel; the call is cancelled once
    /// codex's answer is on disk, and its end awaited. A stopping host (<paramref name="hostStops"/>) cancels the request it was serving as well.
    /// </summary>
    private async Task<(PanelService Service, WithdrawalRig.Reviewers Reviewers, Exception? Thrown)> WithdrawnAfterAsync(string answer, bool hostStops = false)
    {
        WithdrawalRig.Script(answer);
        using var host = new CancellationTokenSource();
        var reviewers = _rig.Playing(p => p == "codex" ? Play.Answer : _gemini);
        var service = _rig.Service(reviewers, Both, hostStopping: host.Token);
        await service.OpenAsync(_rig.Repo, "feature");
        using var call = new CancellationTokenSource();
        var round = service.ReviewPlanAsync(_rig.Repo, "feature", "a plan", call.Token);

        await reviewers.Of("codex").Landed.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        await reviewers.Of("gemini").Arrived.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        (await _rig.SessionAsync(
            service,
            s => s.Rounds.LastOrDefault()?.ReviewerStates.Any(r => r.Provider == "codex" && r.Status == ReviewerState.Done) == true,
            TimeSpan.FromSeconds(60))).Should().BeTrue("codex's answer is reported before the call is taken back");
        if (hostStops)
        {
            await host.CancelAsync();
        }

        await call.CancelAsync();

        return (service, reviewers, await Record.ExceptionAsync(() => round));
    }

    private static string ErrorOf(string reply) => WithdrawalRig.Parse(reply).GetProperty("error").GetString() ?? string.Empty;

    /// <summary>The person's "keep going" on the hold's own notice, answered after the hold was raised.</summary>
    private async Task APersonAnswersAsync(PanelService service)
    {
        var id = _rig.Session(service).State.HoldQuestions.Single();
        await File.WriteAllTextAsync(
            new Escalations(_rig.Data).AnswerPath(id),
            $$"""{"id":"{{id}}","answer":"go","decision":"continue","answeredUtc":"{{DateTime.UtcNow.AddMinutes(1):O}}"}""",
            TestContext.Current.CancellationToken);
    }

    // ---------- a landed finding that gates holds the gate ----------

    [Fact]
    public async Task AWithdrawalAfterAGatingFinding_HoldsTheGateForAPerson()
    {
        var (service, reviewers, thrown) = await WithdrawnAfterAsync(Blocking);

        thrown.Should().BeAssignableTo<OperationCanceledException>("the call was taken back: the reply is still a cancellation");
        var session = _rig.Session(service);
        var round = session.Rounds[^1];
        round.Status.Should().Be(RoundRecord.Cancelled, "a partial panel is never judged: no verdict");
        round.Verdict.Should().Be(EndCauses.Withdrawn);
        round.Findings.Should().ContainSingle().Which.Title.Should().Be("The rollback is missing", "what codex found is kept on the record, not discarded");
        round.GatingCount.Should().Be(1);
        round.Note.Should().Be("withdrawn with a gating finding from codex — held for a person");
        session.State.HumanGate.Should().BeTrue("a gating finding landed: the gate is held, durably, before the claim was released");
        session.State.HoldQuestions.Should().ContainSingle("the hold is bound to the notice that releases it");
        session.State.RoundsRunThisStage.Should().Be(0, "the withdrawn round spent no budget round; the hold, not the count, stops the retry");
        session.Pending.Should().ContainSingle().Which.Title.Should().Be("The rollback is missing", "the held findings are what the person's card and resolve point at");
        File.Exists(new Escalations(_rig.Data).QuestionPath(session.State.HoldQuestions.Single())).Should().BeTrue("the person is asked, as for any hold");

        // The retry the withdrawal was meant to be able to make: clean answers from everybody.
        WithdrawalRig.Script(WithdrawalRig.Clean);
        _gemini = Play.Answer;
        var launchesBefore = reviewers.Of("codex").Launches;
        var retry = await service.ReviewPlanAsync(_rig.Repo, "feature", "a plan");
        retry.Should().NotContain("\"verdict\":\"proceed\"").And.NotContain("\"verdict\": \"proceed\"", "a cancel does not buy a clean second round");
        ErrorOf(retry).Should().Be(RoundMachine.Unresolved, "the held findings must be decided first");
        reviewers.Of("codex").Launches.Should().Be(launchesBefore, "the refused retry launched nobody");

        (await service.ResolveAsync(_rig.Repo, "feature", """[{"finding": 0, "action": "accept"}]""")).Should().NotContain("error");
        var stillHeld = await service.ReviewPlanAsync(_rig.Repo, "feature", "a plan");
        ErrorOf(stillHeld).Should().Be(RoundMachine.GateHeld, "deciding the findings does not release the hold: only a person's answer does");
        _rig.Session(service).Rounds.Should().ContainSingle("no round ran while the gate was held");
        reviewers.Of("codex").Launches.Should().Be(launchesBefore);

        await APersonAnswersAsync(service);
        var afterThePerson = WithdrawalRig.Parse(await service.ReviewPlanAsync(_rig.Repo, "feature", "a plan"));
        afterThePerson.GetProperty("verdict").GetString().Should().Be("proceed", $"once a person answered, the round runs and the clean answers count: {afterThePerson}");
    }

    [Fact]
    public async Task AHostStopAfterAGatingFinding_HoldsTheGateToo()
    {
        var (service, _, thrown) = await WithdrawnAfterAsync(Blocking, hostStops: true);

        thrown.Should().BeAssignableTo<OperationCanceledException>();
        var session = _rig.Session(service);
        session.Rounds[^1].Verdict.Should().Be(EndCauses.HostStopped);
        session.Rounds[^1].Note.Should().Be("the host stopped with a gating finding from codex — held for a person");
        session.State.HumanGate.Should().BeTrue("a stopping host is no more entitled to discard an adverse finding than a caller is");
    }

    // ---------- landed findings that do not gate leave the withdrawal free ----------

    [Fact]
    public async Task AWithdrawalWithOnlyNonGatingFindings_StaysFree_AndKeepsThem()
    {
        var (service, _, thrown) = await WithdrawnAfterAsync(Minor);

        thrown.Should().BeAssignableTo<OperationCanceledException>();
        var session = _rig.Session(service);
        var round = session.Rounds[^1];
        round.Status.Should().Be(RoundRecord.Cancelled);
        round.Findings.Should().ContainSingle().Which.Title.Should().Be("The heading is vague", "kept, though it gates nothing");
        round.GatingCount.Should().Be(0);
        round.Note.Should().Be("the caller withdrew the call while the round ran", "no hold, so the free withdrawal's own sentence");
        session.State.HumanGate.Should().BeFalse();
        session.State.AwaitingResolve.Should().BeFalse();
        session.State.HoldQuestions.Should().BeEmpty();
        session.Pending.Should().BeEmpty("nothing awaits a decision");
        _rig.HoldNotices().Should().BeEmpty("no hold, no notice");

        WithdrawalRig.Script(WithdrawalRig.Clean);
        _gemini = Play.Answer;
        var retry = WithdrawalRig.Parse(await service.ReviewPlanAsync(_rig.Repo, "feature", "a plan"));
        retry.GetProperty("verdict").GetString().Should().Be("proceed", $"a withdrawal with nothing gating stays free: {retry}");
    }

    // ---------- the history later rounds see ----------

    [Fact]
    public async Task AWithdrawnRoundsFindings_AreInTheDatabase()
    {
        await WithdrawnAfterAsync(Blocking);

        _rig.Db("SELECT r.status, r.verdict, r.gating, f.title, f.severity, f.is_gating FROM rounds r JOIN findings f ON f.round_id = r.id WHERE r.stage = 'PlanReview' AND r.number = 1")
            .Should().ContainSingle().Which.Should().Equal("cancelled", "withdrawn", "1", "The rollback is missing", "Blocking", "1");
    }

    // ---------- the rule, pure ----------

    [Fact]
    public void AStandingRejection_IsNotAGatingFinding_ForTheHold()
    {
        var finding = new Finding(Severity.Blocking, Category.Completeness, string.Empty, 0, "The rollback is missing", "nothing undoes the migration if step two fails", "add the down step", []) { Role = "Architecture" };
        var review = new NormalisedReview([finding], []);
        var results = new (ReviewerInvocation, ReviewerOutcome)[]
        {
            (new ReviewerInvocation("codex", "Architecture", new CoaiMcp.Runners.Processes.ProcessRequest("cli", [], ".")), new ReviewerOutcome.Ok(review, false)),
        };
        var work = results.Select(r => new ReviewerWork(r.Item1)).ToArray();

        var counted = KeptFindings.From(results, work, []);
        var rejected = KeptFindings.From(results, work, [new PriorRejection(finding, "already decided")]);

        counted.Gating.Should().Be(1);
        counted.GatingProviders.Should().Equal("codex");
        rejected.Findings.Should().ContainSingle("kept even when it no longer counts");
        rejected.Gating.Should().Be(0, "the completed path discounts a standing rejection; so does the hold");
    }

    // ---------- a conversation cut in a follow-up turn (security judge, 2026-10-22, §13.2) ----------

    private static (ReviewerInvocation, ReviewerOutcome)[] ConversationEndedBy(ReviewerOutcome ending, out ReviewerWork[] work)
    {
        var finding = new Finding(Severity.Blocking, Category.Completeness, string.Empty, 0, "The rollback is missing", "nothing undoes the migration if step two fails", "add the down step", []);
        var answered = new ReviewerOutcome.Ok(new NormalisedReview([finding], []), false);
        var invocation = new ReviewerInvocation("codex", "Architecture", new CoaiMcp.Runners.Processes.ProcessRequest("cli", [], "."));
        work = [new ReviewerWork(invocation)];

        return [(invocation, ending with { LastAnswered = answered })];
    }

    [Fact]
    public void ATurnTheRoundCut_KeepsTheLastAnsweredTurnsFindings()
    {
        var results = ConversationEndedBy(new ReviewerOutcome.NotStarted("the round ended") { EndedByRoundToken = true }, out var work);

        var kept = KeptFindings.From(results, work, []);

        kept.Findings.Should().ContainSingle().Which.Title.Should().Be("The rollback is missing");
        kept.Gating.Should().Be(1);
        kept.GatingProviders.Should().Equal("codex");
    }

    [Fact]
    public void ALaterTurnThatFailedOnItsOwnTerms_StillDiscardsTheEarlierTurns_AsTheCompletedPathDoes()
    {
        var results = ConversationEndedBy(new ReviewerOutcome.TimedOut(), out var work);

        var kept = KeptFindings.From(results, work, []);

        kept.Findings.Should().BeEmpty("a failed later turn is a failed reviewer: the round was not cut, so nothing of it is kept");
        kept.Gating.Should().Be(0);
    }
}
