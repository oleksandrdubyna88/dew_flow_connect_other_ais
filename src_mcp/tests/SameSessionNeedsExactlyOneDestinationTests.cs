using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.AssignmentViews;

namespace CoaiMcp.Tests;

/// <summary>
/// The last rung (E4 §S2.4): an agent with no write of its own is credited to this repository only when every write by ANY agent in
/// the window went to exactly this one destination. Anything else — none, another, several — is <c>ambiguous</c>, never <c>wrote
/// elsewhere</c>: the agent wrote nothing.
/// </summary>
public sealed class SameSessionNeedsExactlyOneDestinationTests
{
    private const string X = AttributionCases.X;
    private const string Y = AttributionCases.Y;

    private static AttributionScenario Window() => AttributionScenario.For().Gate("A", X).Opened(0).Code(1, 60, 70).Agent("scout", launchedAt: 3).Agent("w", launchedAt: 4);

    [Fact]
    public void OneDestinationAndItIsThisOne_IsSameSession() =>
        AttributionCases.Case1().Assign("A", "CodeReview", 3).Msg("s3", 140).Should().Match<Placement>(p => p.Association == Association.SameSession && p.Rung == Rung.Session);

    [Fact]
    public void NoWriteAtAll_IsAmbiguous() =>
        Window().Message("scout", 10).Assign("A", "CodeReview", 1).Msg("scout", 10).Should().Match<Placement>(p => p.Association == Association.Ambiguous && p.Rung == Rung.NoEvidence);

    [Fact]
    public void TwoDestinations_IsAmbiguous() =>
        AttributionCases.Case1().Assign("A", "CodeReview", 1).Msg("s1", 45).Association.Should().Be(Association.Ambiguous);

    [Fact]
    public void ASubmoduleAndItsSuperproject_AreOneDestination()
    {
        var scenario = Window().Repositories(X + "/sub").Message("scout", 20).Message("w", 10).Message("w", 11)
            .Write("w", 10, X + "/sub/a.cs").Write("w", 11, X + "/b.cs");

        scenario.Assign("A", "CodeReview", 1).Msg("scout", 20).Association.Should().Be(Association.SameSession, "the submodule's destination is the gate's repo_path");
    }

    [Fact]
    public void AnotherCheckoutOnly_IsAmbiguousNotElsewhere()
    {
        var scenario = Window().Repositories(Y).Message("scout", 20).Message("w", 10).Write("w", 10, Y + "/b.cs");

        var assignment = scenario.Assign("A", "CodeReview", 1);

        assignment.Msg("scout", 20).Association.Should().Be(Association.Ambiguous, "the scout wrote nothing; the window's only destination is not this one");
        assignment.Msg("w", 10).Association.Should().Be(Association.WroteElsewhere);
    }

    /// <summary>
    /// G12c: the destination of a root is the longest <c>repo_path</c> of EVERY gate in the input that contains it — so a nested
    /// gate's repository is its own destination, and the superproject's gate does not read the nested one's writes as its own.
    /// </summary>
    [Fact]
    public void ADestinationIsTheLongestGateRepositoryOverEveryGateInTheInput()
    {
        var scenario = AttributionScenario.For().Gate("A", X).Opened(0).Code(1, 60, 70).Gate("N", X + "/n").Opened(0).Code(1, 60, 90)
            .Agent("w", launchedAt: 4).Agent("scout", launchedAt: 5)
            .Message("w", 10).Write("w", 10, X + "/n/a.cs").Message("scout", 20);

        var assignment = scenario.Assign("A", "CodeReview", 1);

        assignment.Msg("w", 10).Association.Should().Be(Association.WroteHere, "the write is inside A's repo_path");
        assignment.Msg("scout", 20).Association.Should().Be(Association.Ambiguous, "the only destination is N's, not A's");
    }
}
