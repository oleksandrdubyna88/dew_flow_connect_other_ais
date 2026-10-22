using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// G4 of <c>todo/PLAN_a_cancelled_review_stops_its_round.md</c>, security judge 2026-10-22 (§13.1 of its S2 spec): a free withdrawal must not be a
/// free RESAMPLE of the panel. The agent a gate is meant to stop could otherwise cancel any round whose partial answers it dislikes and ask again,
/// as often as it likes, until a draw is clean. So withdrawals in a row — rounds of one stage that were cancelled with no judged round between them —
/// are counted: the person is told at two, and the cap (<c>COAI_WITHDRAWALS_BEFORE_HOLD</c>, three) holds the gate exactly as a landed gating finding does.
/// </summary>
/// <remarks>Driven through the real <see cref="PanelService"/> like <see cref="AWithdrawnRoundKeepsWhatLandedTests"/>: one codex reviewer that waits for the cancel.</remarks>
[Collection("fakecli-env")]
public sealed class ConsecutiveWithdrawalsAreCappedTests : IAsyncLifetime
{
    private static readonly string[] OneVendor = ["codex"];

    private WithdrawalRig _rig = null!;

    public async ValueTask InitializeAsync() => _rig = await WithdrawalRig.CreateAsync();

    public async ValueTask DisposeAsync() => await _rig.DisposeAsync();

    /// <summary>A service whose only reviewer waits for the cancel, and the session opened.</summary>
    private async Task<(PanelService Service, WithdrawalRig.Reviewers Reviewers)> ServiceAsync(int cap = PanelSettings.DefaultWithdrawalsBeforeHold)
    {
        var reviewers = _rig.PlayAll(Play.BlockUntilCancelled);
        var service = _rig.Service(reviewers, OneVendor, withdrawalsBeforeHold: cap);
        await service.OpenAsync(_rig.Repo, "feature");

        return (service, reviewers);
    }

    /// <summary>One more plan round, taken back once its reviewer is under way; the call's end awaited.</summary>
    private async Task<Exception?> WithdrawAsync(PanelService service, WithdrawalRig.Reviewers reviewers)
    {
        var before = reviewers.Of("codex").Launches;
        using var call = new CancellationTokenSource();
        var round = service.ReviewPlanAsync(_rig.Repo, "feature", "a plan", call.Token);
        var until = DateTime.UtcNow.AddSeconds(60);
        while (reviewers.Of("codex").Launches <= before && DateTime.UtcNow < until)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        reviewers.Of("codex").Launches.Should().BeGreaterThan(before, "the round under way is what the cancel takes back");
        await call.CancelAsync();

        return await Record.ExceptionAsync(() => round);
    }

    /// <summary>What every notice the person was sent says, oldest first.</summary>
    private List<string> Notices() =>
        [.. _rig.HoldNotices()
            .Where(f => f.EndsWith(".json", StringComparison.Ordinal) && !f.EndsWith(".answer.json", StringComparison.Ordinal))
            .Select(f => JsonDocument.Parse(File.ReadAllText(f)).RootElement)
            .OrderBy(q => q.GetProperty("askedUtc").GetString(), StringComparer.Ordinal)
            .Select(q => q.GetProperty("question").GetString() ?? string.Empty)];

    private static RoundRecord Seeded(int number, string status, string verdict) =>
        new(nameof(Stage.PlanReview), number, verdict, 0, "seeded", DateTime.UtcNow) { Status = status };

    /// <summary>Rounds put in the journal as earlier calls would have left them, so a count can start from any history.</summary>
    private void Seed(PanelService service, params RoundRecord[] rounds)
    {
        var session = _rig.Session(service);
        service.Store.Save(session with { Rounds = [.. session.Rounds, .. rounds] });
    }

    [Fact]
    public async Task TwoWithdrawalsInARow_TellThePerson()
    {
        var (service, reviewers) = await ServiceAsync();

        await WithdrawAsync(service, reviewers);
        Notices().Should().BeEmpty("one withdrawal is nothing to report");

        var thrown = await WithdrawAsync(service, reviewers);

        thrown.Should().BeAssignableTo<OperationCanceledException>("the second call is still answered as a cancellation");
        Notices().Should().ContainSingle().Which.Should().Contain("plan review round of feature was withdrawn 2 times in a row without a verdict");
        var session = _rig.Session(service);
        session.State.HumanGate.Should().BeFalse("the notice informs; it is not a hold");
        session.State.HoldQuestions.Should().BeEmpty();
        session.Rounds.Should().HaveCount(2).And.OnlyContain(r => r.Status == RoundRecord.Cancelled);

        var third = await WithdrawAsync(service, reviewers);
        third.Should().BeAssignableTo<OperationCanceledException>("two in a row ask the person, they do not yet stop the third");
    }

    [Fact]
    public async Task TheThirdWithdrawalInARow_HoldsTheGate()
    {
        var (service, reviewers) = await ServiceAsync();
        await WithdrawAsync(service, reviewers);
        await WithdrawAsync(service, reviewers);

        var thrown = await WithdrawAsync(service, reviewers);

        thrown.Should().BeAssignableTo<OperationCanceledException>("the reply is still a cancellation");
        var session = _rig.Session(service);
        var round = session.Rounds[^1];
        round.Status.Should().Be(RoundRecord.Cancelled);
        round.Note.Should().Be("withdrawn 3 times in a row — held for a person");
        round.GatingCount.Should().Be(0, "nothing landed: the cap holds, not a finding");
        session.State.HumanGate.Should().BeTrue("a third free resample is refused: a person decides");
        session.State.AwaitingResolve.Should().BeTrue();
        session.State.HoldQuestions.Should().ContainSingle("the hold is bound to the notice that releases it");
        session.State.RoundsRunThisStage.Should().Be(0, "no budget round was spent");
        File.Exists(new Escalations(_rig.Data).QuestionPath(session.State.HoldQuestions.Single())).Should().BeTrue("the person is asked, as for any hold");

        var launches = reviewers.Of("codex").Launches;
        var retry = await service.ReviewPlanAsync(_rig.Repo, "feature", "a plan");
        WithdrawalRig.Parse(retry).GetProperty("error").GetString().Should().Be(RoundMachine.Unresolved, "the fourth call meets the hold");
        reviewers.Of("codex").Launches.Should().Be(launches, "the refused call launched nobody");
    }

    [Fact]
    public async Task AJudgedRoundBetween_ResetsTheCount()
    {
        var (service, reviewers) = await ServiceAsync();
        Seed(service, Seeded(1, RoundRecord.Cancelled, EndCauses.Withdrawn), Seeded(2, RoundRecord.Done, "revise"));

        await WithdrawAsync(service, reviewers);
        Notices().Should().BeEmpty("the judged round reset the count: this is the first withdrawal");
        await WithdrawAsync(service, reviewers);

        Notices().Should().ContainSingle().Which.Should().Contain("withdrawn 2 times in a row");
        var session = _rig.Session(service);
        session.State.HumanGate.Should().BeFalse("two since the judged round, not three: without the reset the cancelled round before it would have held the gate");
    }

    [Fact]
    public async Task TheCapComesFromTheSetting()
    {
        var (service, reviewers) = await ServiceAsync(cap: 2);

        await WithdrawAsync(service, reviewers);
        _rig.Session(service).State.HumanGate.Should().BeFalse();
        await WithdrawAsync(service, reviewers);

        var session = _rig.Session(service);
        session.State.HumanGate.Should().BeTrue("the second withdrawal in a row reaches a cap of two");
        session.Rounds[^1].Note.Should().Be("withdrawn 2 times in a row — held for a person");
        Notices().Should().ContainSingle("the hold's own notice is the one the person gets; the informational one would be a second card for the same fact");
    }

    [Theory]
    [InlineData(null, 3)]
    [InlineData("5", 5)]
    [InlineData("1", 1)]
    [InlineData("0", 3)]
    [InlineData("-2", 3)]
    [InlineData("many", 3)]
    public void TheSettingIsReadFromTheEnvironment_AtLeastOne(string? value, int expected) =>
        PanelSettings.FromEnvironment(name => name == "COAI_WITHDRAWALS_BEFORE_HOLD" ? value : null)
            .WithdrawalsBeforeHold.Should().Be(expected);

    // ---------- the count, pure ----------

    [Fact]
    public void TheCount_IsTheRunOfCancelledRoundsOfTheStage_SinceTheLastJudgedOne_AndTheOneBeingRecorded()
    {
        IReadOnlyList<RoundRecord> journal =
        [
            Seeded(1, RoundRecord.Cancelled, EndCauses.Withdrawn),
            Seeded(2, RoundRecord.Done, "proceed"),
            Seeded(3, RoundRecord.Cancelled, EndCauses.HostStopped),
            Seeded(4, RoundRecord.Done, RoundRecord.Skipped),
            Seeded(5, RoundRecord.Interrupted, "interrupted"),
            Seeded(6, RoundRecord.Cancelled, EndCauses.Withdrawn),
            Seeded(7, RoundRecord.Done, "proceed") with { Stage = nameof(Stage.CodeReview) },
        ];

        WithdrawalStreak.Of(journal, nameof(Stage.PlanReview), 3).Count
            .Should().Be(3, "rounds 3 and 6 (a skipped and an interrupted round judge nothing, another stage's round is none of this one's) and the one being recorded");
        WithdrawalStreak.Of([.. journal.Take(2)], nameof(Stage.PlanReview), 3).Count.Should().Be(1, "a judged round last: only the one being recorded");
        WithdrawalStreak.Of([], nameof(Stage.PlanReview), 3).Count.Should().Be(1);
    }
}
