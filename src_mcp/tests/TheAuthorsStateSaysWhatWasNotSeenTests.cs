using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.AssignmentViews;

namespace CoaiMcp.Tests;

/// <summary>
/// The state table of E4.S3: a round's state says what was NOT seen, and an unobserved round is never a zero — its totals are
/// <see cref="ActivityTotals.Unknown"/> and its unobserved-launch count is −1 (M1).
/// </summary>
public sealed class TheAuthorsStateSaysWhatWasNotSeenTests
{
    private const string X = AttributionCases.X;

    [Fact]
    public void AnObservedSessionWithNoSubagents_IsObservedZero()
    {
        var authors = AttributionScenario.For().Gate("A", X).Opened(0).Code(1, 60, 70).Main(10).Write("main", 10, X + "/a.cs").AuthorsOf("A", "CodeReview", 1);

        authors.State.Should().Be(ObservationKinds.Observed);
        authors.Reasons.Should().BeEmpty();
        authors.Ambiguous.Should().Be(ActivityTotals.Zero);
        authors.UnobservedLaunches.Should().Be(0);
        authors.Rows.Should().ContainSingle();
    }

    [Fact]
    public void AnUnobservedRound_NeverReadsAsZero()
    {
        var authors = AttributionCases.Case5().CallerUnobserved("transcript not found").AuthorsOf("A", "CodeReview", 1);

        authors.State.Should().Be(ObservationKinds.Unobserved);
        authors.Reasons.Should().Equal("transcript not found");
        authors.Rows.Should().BeEmpty();
        authors.Ambiguous.Should().Be(ActivityTotals.Unknown);
        authors.Elsewhere.Should().Be(ActivityTotals.Unknown);
        authors.ClaimedByPeer.Should().Be(ActivityTotals.Unknown);
        authors.UnobservedLaunches.Should().Be(-1);
        authors.WritesOutsideTheGateCheckout.Should().BeEmpty();
        authors.Windows.Should().OnlyContain(window => window.State == ObservationKinds.Unobserved && window.UnobservedLaunches == -1 && window.Ambiguous.IsUnknown);
    }

    [Fact]
    public void AMissingChild_IsPartialWithEachReasonAndAFloor()
    {
        var authors = AttributionCases.Case5().AuthorsOf("A", "CodeReview", 1);

        authors.State.Should().Be(ObservationKinds.Partial);
        authors.Reasons.Should().Equal("agent call without transcript", "meta without transcript");
        authors.UnobservedLaunches.Should().Be(2);
        authors.Rows.Single().Should().Match<AuthorRow>(row => row.Agent.IsMain && row.Turns == 1, "what was seen is kept, as a floor");
    }

    [Fact]
    public void AReadBudgetMarksEveryWindow()
    {
        var authors = AttributionCases.Case5().Reason("read budget").AuthorsOf("A", "CodeReview", 2);

        authors.State.Should().Be(ObservationKinds.Partial);
        authors.Reasons.Should().Equal("read budget");
        authors.Windows.Should().OnlyContain(window => window.State == ObservationKinds.Partial);
    }

    [Fact]
    public void APeerRunningWindow_IsPartialPeerRunning()
    {
        var authors = AttributionCases.Case1(peerStatus: "running", peerStamp: 105).AuthorsOf("A", "CodeReview", 2);

        authors.State.Should().Be(ObservationKinds.Partial);
        authors.Reasons.Should().Equal(AttributionReasons.PeerRunning);
        authors.PeerGates.Should().Equal("B");
    }

    [Fact]
    public void ABlindRound_IsUnobservedNoRoundStart()
    {
        var authors = AttributionCases.BlindRounds().AuthorsOf("A", "CodeReview", 1);

        authors.State.Should().Be(ObservationKinds.Unobserved);
        authors.Reasons.Should().Equal(AttributionReasons.NoRoundStart);
        authors.Ambiguous.Should().Be(ActivityTotals.Unknown);
        authors.UnobservedLaunches.Should().Be(-1);
    }

    [Fact]
    public void AnUnobservedLaunchWithNoInstant_IsCountedInEveryWindow()
    {
        var scenario = AttributionScenario.For().Gate("A", X).Opened(0).Code(1, 60, 70).MissingChild("main", null, "agent call without transcript");

        scenario.AuthorsOf("A", "CodeReview", 1).Windows.Should().OnlyContain(window => window.UnobservedLaunches == 1);
    }

    [Fact]
    public void ThePeersTheRoundSawArePassedThrough() =>
        AttributionCases.Case1().AuthorsOf("A", "CodeReview", 2).PeerGates.Should().Equal("B");

    [Fact]
    public void NoReasonContainsTheJoiner()
    {
        var reasons = typeof(AttributionReasons).GetFields().Where(field => field.IsLiteral).Select(field => (string)field.GetRawConstantValue()!)
            .Concat(typeof(CoaiMcp.Store.ObservationReasons).GetFields().Where(field => field.IsLiteral).Select(field => (string)field.GetRawConstantValue()!))
            .Concat(new[] { "claude", "codex", "gemini", "kimi", "vibe", "agy" }.Select(CoaiMcp.Store.ObservationReasons.NoReaderFor))
            .ToList();

        reasons.Should().NotBeEmpty().And.OnlyContain(reason => !reason.Contains(", ", StringComparison.Ordinal), "E5 joins reasons with exactly these two characters");
        reasons.Should().Contain(AttributionReasons.PeerRunning).And.Contain(CoaiMcp.Store.ObservationReasons.ReadBudget);
    }
}
