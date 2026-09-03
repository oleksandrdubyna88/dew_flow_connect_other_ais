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

    /// <summary>
    /// Each reviewer's own duration, because the round's total cannot answer "which of the nine".
    /// </summary>
    /// <remarks>
    /// Measured 2026-09-03: a code round took 11m 2s across nine reviewers, and the two that spent
    /// 590 s each were indistinguishable in that number from the seven that took under a minute.
    /// The scheduler times every reviewer already — this is the number arriving instead of being
    /// dropped at the session boundary.
    /// </remarks>
    [Fact]
    public void AFinishedReviewer_KeepsItsOwnDuration()
    {
        var store = new SessionStore(_dir);
        var session = Session();
        store.Save(session);
        var live = new LiveRound(store, session, [Work("codex", ReviewRole.Architecture)]);

        live.Report(new ReviewerProgress("codex", ReviewRole.Architecture, "running"));
        StateOf(store, "codex").Seconds.Should().Be(0, "a running reviewer has no duration yet");

        live.Report(new ReviewerProgress(
            "codex",
            ReviewRole.Architecture,
            "done",
            new ReviewerOutcome.Ok(Review(3), Repaired: false, new Usage(1000, 100, 0.02)),
            TimeSpan.FromSeconds(38.7)));

        StateOf(store, "codex").Seconds.Should().Be(38.7);
    }

    [Fact]
    public void ALaterReportWithoutADuration_DoesNotEraseTheOneRecorded()
    {
        // A "running" report carries no elapsed time, and taking it would zero the number of a
        // reviewer that had already finished — a retry, a repaint, or any later progress line.
        var store = new SessionStore(_dir);
        var session = Session();
        store.Save(session);
        var live = new LiveRound(store, session, [Work("gemini", ReviewRole.Architecture)]);

        live.Report(new ReviewerProgress("gemini", ReviewRole.Architecture, "done", null, TimeSpan.FromSeconds(12.5)));
        live.Report(new ReviewerProgress("gemini", ReviewRole.Architecture, "running"));

        StateOf(store, "gemini").Seconds.Should().Be(12.5);
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
