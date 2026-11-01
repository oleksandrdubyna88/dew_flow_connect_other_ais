using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.AssignmentViews;

namespace CoaiMcp.Tests;

/// <summary>
/// E4 decision 4: an agent that wrote into two repositories in one window is <c>ambiguous · two repositories</c> in every gate that
/// saw two repositories; its messages are owned ONCE — by the gate that completed first, into its ambiguous totals — and the other
/// gate labels them <c>claimed by peer</c>. Tokens never twice; the agent stays a contributor of each repository it wrote into
/// through its write evidence.
/// </summary>
public sealed class TwoRepositoriesOneAgentTests
{
    private static WindowAssignment A() => AttributionCases.TwoRepositoriesOneAgent().Assign("A", "CodeReview", 1);

    private static WindowAssignment B() => AttributionCases.TwoRepositoriesOneAgent().Assign("B", "CodeReview", 1);

    [Fact]
    public void ItsMessagesAreAmbiguousInTheFirstCompletedWindow()
    {
        A().Messages.Should().HaveCount(3).And.OnlyContain(p => p.Association == Association.Ambiguous && p.Rung == Rung.TwoRepositories && p.OwnerGate == "A");
        A().Launch("w2").Association.Should().Be(Association.Ambiguous);
    }

    [Fact]
    public void TheOtherGateSeesThemClaimedByPeer()
    {
        B().Messages.Should().HaveCount(3).And.OnlyContain(p => p.Association == Association.ClaimedByPeer && p.OwnerGate == "A");
        B().Launch("w2").Association.Should().Be(Association.ClaimedByPeer);
    }

    /// <summary>The agent COUNT is S3's (G10); here only the association each gate emits for the agent is held.</summary>
    [Fact]
    public void BothGatesEmitItsAgentAssociationAsAmbiguousTwoRepositories()
    {
        foreach (var assignment in new[] { A(), B() })
        {
            assignment.AssociationOf("w2", assignment.RoundWindow()).Should().Match<AgentAssociation>(a => a.Association == Association.Ambiguous && a.Rung == Rung.TwoRepositories);
        }

        A().AssociationOf("w2", A().RoundWindow()).ElsewhereRoots.Should().Equal(AttributionCases.Y);
        B().AssociationOf("w2", B().RoundWindow()).ElsewhereRoots.Should().Equal(AttributionCases.X);
    }

    [Fact]
    public void BothGatesKeepItsWriteCallsOnTheWritePlacements()
    {
        A().Writes.Select(w => (w.Where, w.Root)).Should().BeEquivalentTo([(WriteWhere.Here, AttributionCases.X), (WriteWhere.Elsewhere, AttributionCases.Y)]);
        B().Writes.Select(w => (w.Where, w.Root)).Should().BeEquivalentTo([(WriteWhere.Here, AttributionCases.Y), (WriteWhere.Elsewhere, AttributionCases.X)]);
    }

    [Fact]
    public void ItsTokensAreOwnedOnceOverBothGates()
    {
        var every = AttributionCases.TwoRepositoriesOneAgent().AssignEvery();

        every.SelectMany(one => one.Assignment.Messages).Where(p => Association.IsOwning(p.Association)).Sum(p => p.Tokens.Output).Should().Be(300);
    }
}
