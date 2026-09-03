using System.Collections.Immutable;
using Xunit;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// The round's live status: on disk before the first CLI starts, advancing while it runs, swept
/// when the process that owned it is gone. The defect these hold shut is the one the operator saw
/// — a ten-minute round that showed nothing at all until it ended.
/// </summary>
public sealed class LiveRoundTests
{
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-live-").FullName;

    private static PersistedSession Session() =>
        new(new SessionState("s-live", "D:/repo", "feature/x", new PanelConfig()), []);

    private static ReviewerWork Work(string provider, ReviewRole role) =>
        new(new ReviewerInvocation(provider, role, new ProcessRequest("cli", [], ".")));

    private static NormalisedReview Review(int findings) =>
        new(
            [.. Enumerable.Range(0, findings).Select(i =>
                new Finding(Severity.Major, Category.Security, "a.cs", i + 1, $"f{i}", "why", "fix", ["codex"]))],
            []);

    [Fact]
    public void TheRoundIsOnDisk_BeforeAnyReviewerHasAnswered()
    {
        var store = new SessionStore(_dir);
        var session = Session();
        store.Save(session);

        _ = new LiveRound(store, session, [Work("codex", ReviewRole.Architecture), Work("claude", ReviewRole.Architecture)]);

        var round = store.Load("D:/repo", "feature/x")!.Rounds.Should().ContainSingle().Subject;
        round.Status.Should().Be(RoundRecord.Running);
        round.StartedUtc.Should().NotBe(default);
        round.RunnerPid.Should().Be(Environment.ProcessId);
        round.ReviewerStates.Should().HaveCount(2).And.OnlyContain(s => s.Status == ReviewerState.Queued);
    }

    [Fact]
    public void EachReviewerMoving_IsVisibleImmediately_QueuedThenRunningThenDone()
    {
        var store = new SessionStore(_dir);
        var session = Session();
        store.Save(session);
        var live = new LiveRound(store, session, [Work("codex", ReviewRole.Architecture), Work("gemini", ReviewRole.Architecture)]);

        live.Report(new ReviewerProgress("codex", ReviewRole.Architecture, "running"));

        StateOf(store, "codex").Status.Should().Be(ReviewerState.Running);
        StateOf(store, "gemini").Status.Should().Be(ReviewerState.Queued, "one reviewer's progress is not another's");

        live.Report(new ReviewerProgress("codex", ReviewRole.Architecture, "done",
            new ReviewerOutcome.Ok(Review(3), Repaired: false, new Usage(1000, 100, 0.02))));

        var done = StateOf(store, "codex");
        done.Status.Should().Be(ReviewerState.Done);
        done.Findings.Should().Be(3, "the count is what makes a finished reviewer worth reading");
    }

    [Fact]
    public void AFailedReviewer_SaysWhy_WhileTheRoundIsStillOpen()
    {
        var store = new SessionStore(_dir);
        var session = Session();
        store.Save(session);
        var live = new LiveRound(store, session, [Work("gemini", ReviewRole.PlanCritique)]);

        live.Report(new ReviewerProgress("gemini", ReviewRole.PlanCritique, "failed", new ReviewerOutcome.TimedOut()));

        var state = StateOf(store, "gemini");
        state.Status.Should().Be(ReviewerState.Failed);
        state.Note.Should().Be("timeout");
    }

    [Fact]
    public void TheFinishedRecord_CarriesTheRoundsTokensAndMoney()
    {
        var store = new SessionStore(_dir);
        var session = Session();
        store.Save(session);
        var work = new[] { Work("codex", ReviewRole.Architecture), Work("claude", ReviewRole.Architecture) };
        var live = new LiveRound(store, session, work);

        var record = live.Finish("revise", 4, "all 2 reviewers answered",
        [
            (work[0].Invocation, new ReviewerOutcome.Ok(Review(2), false, new Usage(5300, 260, null))),
            (work[1].Invocation, new ReviewerOutcome.Ok(Review(2), false, new Usage(24064, 44, 0.0489))),
        ]);

        record.Status.Should().Be(RoundRecord.Done);
        record.TokensIn.Should().Be(29364);
        record.TokensOut.Should().Be(304);
        record.CostUsd.Should().BeApproximately(0.0489, 0.000001, "the priced vendor's spend is real money spent");
    }

    [Fact]
    public void TheFinishedRecord_SplitsTheSpendPerReviewer_SoAMixedRoundCanBePriced()
    {
        // The panel read "no cost reported" beside 220k tokens for every round of vendors that do
        // not price their own runs — codex and gemini both report tokens and no money. The rates
        // were in the panel all along; what was missing was WHOSE tokens they were. A round's
        // summed tokens cannot be priced by any one vendor's rate, so the split is the fix.
        var store = new SessionStore(_dir);
        var session = Session();
        store.Save(session);
        var work = new[] { Work("codex", ReviewRole.Architecture), Work("gemini", ReviewRole.Architecture) };
        var live = new LiveRound(store, session, work);

        var record = live.Finish("revise", 4, "all 2 reviewers answered",
        [
            (work[0].Invocation, new ReviewerOutcome.Ok(Review(2), false, new Usage(5300, 260, null))),
            (work[1].Invocation, new ReviewerOutcome.Ok(Review(2), false, new Usage(24064, 44, null))),
        ]);

        var codex = record.ReviewerStates.Single(s => s.Provider == "codex");
        var gemini = record.ReviewerStates.Single(s => s.Provider == "gemini");
        codex.TokensIn.Should().Be(5300);
        codex.TokensOut.Should().Be(260);
        gemini.TokensIn.Should().Be(24064);
        gemini.TokensOut.Should().Be(44);
        codex.CostUsd.Should().BeNull("codex prices nothing, and unknown must not become zero");
        record.TokensIn.Should().Be(29364, "the round's total is still the fold of the parts");
    }

    [Fact]
    public void ARepairedReviewersTwoLaunches_AreAddedTogether_NotOverwritten()
    {
        // A reviewer that is relaunched appears TWICE in the results for the same provider and
        // role. Taking the last entry would report half of what that reviewer actually consumed —
        // and half of what it is about to be priced at.
        var store = new SessionStore(_dir);
        var session = Session();
        store.Save(session);
        var work = new[] { Work("codex", ReviewRole.Architecture) };
        var live = new LiveRound(store, session, work);

        var record = live.Finish("revise", 1, "1 of 1 reviewers answered",
        [
            (work[0].Invocation, new ReviewerOutcome.Unparseable("torn", new Usage(1000, 10, null))),
            (work[0].Invocation, new ReviewerOutcome.Ok(Review(1), false, new Usage(2000, 20, null))),
        ]);

        var codex = record.ReviewerStates.Single();
        codex.TokensIn.Should().Be(3000, "both launches burned tokens");
        codex.TokensOut.Should().Be(30);
    }

    [Fact]
    public void ARoundAbandonedByADeadProcess_IsSwept_NeverLeftRunningForever()
    {
        var store = new SessionStore(_dir);
        var session = Session();
        store.Save(session);
        _ = new LiveRound(store, session, [Work("codex", ReviewRole.Architecture)]);

        var swept = store.SweepOrphanedRounds(_ => false);

        swept.Should().Be(1);
        var round = store.Load("D:/repo", "feature/x")!.Rounds.Single();
        round.Status.Should().Be(RoundRecord.Interrupted);
        round.Verdict.Should().Be("interrupted");
    }

    [Fact]
    public void ALiveRoundOfAnotherRunningServer_IsLeftAlone()
    {
        // Two MCP clients can share this data directory; declaring the other one's round dead
        // would be worse than showing a stale one.
        var store = new SessionStore(_dir);
        var session = Session();
        store.Save(session);
        _ = new LiveRound(store, session, [Work("codex", ReviewRole.Architecture)]);

        store.SweepOrphanedRounds(_ => true).Should().Be(0);
        store.Load("D:/repo", "feature/x")!.Rounds.Single().Status.Should().Be(RoundRecord.Running);
    }

    private static ReviewerState StateOf(SessionStore store, string provider) =>
        store.Load("D:/repo", "feature/x")!.Rounds.Last().ReviewerStates.Single(s => s.Provider == provider);
}
