using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.AssignmentViews;

namespace CoaiMcp.Tests;

/// <summary>
/// §S2.2b G9: the round keeps the state AND the totals of each of its windows — empty ones included — beside the roll-up, so a report
/// that leaves <c>before-open</c> out can still say what the windows it keeps were.
/// </summary>
public sealed class EveryWindowKeepsItsOwnStateAndTotalsTests
{
    private const string X = AttributionCases.X;

    private static AttributionScenario Round() => AttributionScenario.For().Gate("A", X).Opened(0).Code(1, 60, 70).Main(10).Main(40).Write("main", 10, X + "/a.cs");

    [Fact]
    public void ThereIsOneEntryPerWindow_TheEmptyBeforeOpenOfAFirstRoundIncluded()
    {
        var authors = Round().AuthorsOf("A", "CodeReview", 1);

        authors.Windows.Select(window => window.Window).Should().Equal(WindowKind.BeforeOpen, WindowKind.Round);
        authors.Windows[0].Ambiguous.Should().Be(ActivityTotals.Zero, "empty, and observed: a zero that was seen");
        authors.Windows[0].State.Should().Be(ObservationKinds.Observed);
    }

    [Fact]
    public void ALaterRoundHasOnlyItsRoundWindow() =>
        AttributionCases.Case1().AuthorsOf("A", "CodeReview", 2).Windows.Select(window => window.Window).Should().Equal(WindowKind.Round);

    /// <summary>The roll-up is `partial` in both, and the per-window states say where.</summary>
    [Fact]
    public void BeforeOpenPartialRoundObserved_AndTheReverse_AreDifferent()
    {
        var early = Round().MissingChild("main", -5, "agent call without transcript").AuthorsOf("A", "CodeReview", 1);
        var late = Round().MissingChild("main", 30, "agent call without transcript").AuthorsOf("A", "CodeReview", 1);

        early.State.Should().Be(ObservationKinds.Partial);
        late.State.Should().Be(ObservationKinds.Partial);
        early.Windows.Select(window => window.State).Should().Equal(ObservationKinds.Partial, ObservationKinds.Observed);
        late.Windows.Select(window => window.State).Should().Equal(ObservationKinds.Observed, ObservationKinds.Partial);
        early.Windows.Select(window => window.UnobservedLaunches).Should().Equal(1, 0);
        late.Windows.Select(window => window.UnobservedLaunches).Should().Equal(0, 1);
    }

    /// <summary>
    /// A blind first round has no edge for ANY of its windows (no open time takes both: they meet at the open), so its roll-up is unknown
    /// and each window says so — never a zero. (Recorded as a deviation: the plan's "blind before-open, counted round window" cannot
    /// arise, because both windows of the first round depend on the same two instants.)
    /// </summary>
    [Fact]
    public void ABlindFirstRound_IsUnknownInEveryWindowAndInTheRollUp()
    {
        var authors = AttributionScenario.For().Gate("A", X).NeverOpened().Code(1, 60, 70).Main(10).AuthorsOf("A", "CodeReview", 1);

        authors.Windows.Should().OnlyContain(window => window.State == ObservationKinds.Unobserved && window.Reasons.SequenceEqual(new[] { AttributionReasons.NoOpenTime }) && window.Ambiguous.IsUnknown);
        authors.Ambiguous.Should().Be(ActivityTotals.Unknown);
        authors.UnobservedLaunches.Should().Be(-1);
    }

    [Fact]
    public void WhenNoWindowIsUnobserved_TheWindowTotalsSumToTheRoundTotals()
    {
        var authors = AttributionCases.Case1().AuthorsOf("A", "CodeReview", 1);

        authors.Ambiguous.Turns.Should().Be(authors.Windows.Sum(window => window.Ambiguous.Turns));
        authors.Ambiguous.Launches.Should().Be(authors.Windows.Sum(window => window.Ambiguous.Launches));
        authors.Ambiguous.Agents.Should().Be(authors.Windows.Sum(window => window.Ambiguous.Agents));
        authors.Ambiguous.Tokens.Output.Should().Be(authors.Windows.Sum(window => window.Ambiguous.Tokens.Output));
        authors.Elsewhere.Turns.Should().Be(authors.Windows.Sum(window => window.Elsewhere.Turns));
        authors.UnobservedLaunches.Should().Be(authors.Windows.Sum(window => window.UnobservedLaunches));
        authors.Windows.Sum(window => window.Ambiguous.Turns).Should().BeGreaterThan(0, "the sum is not over nothing");
    }

    [Fact]
    public void TheRollUpReasonsAreDistinctInWindowOrder()
    {
        var authors = Round().MissingChild("main", -5, "agent call without transcript").MissingChild("main", 30, "agent call without transcript").Reason("read budget").AuthorsOf("A", "CodeReview", 1);

        authors.Reasons.Should().Equal("agent call without transcript", "read budget");
    }
}
