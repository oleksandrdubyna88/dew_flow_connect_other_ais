using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.AssignmentViews;

namespace CoaiMcp.Tests;

/// <summary>
/// E4 decision 4 as S3 sees it (§S2.2b G10): an agent that wrote into two repositories has ONE <c>wrote here</c> row per gate carrying
/// its write calls into that gate's repository and nothing else — no turns, launches or tokens, no model claim — so it is a contributor
/// of each repository without a token added twice. Its messages are counted once, in the ambiguous totals of the gate that owns them or
/// in the claimed-by-peer totals of the other.
/// </summary>
public sealed class ATwoRepositoryAgentKeepsItsWritesAndAddsNoTokenTests
{
    private static RoundAuthors A() => AttributionCases.TwoRepositoriesOneAgent().AuthorsOf("A", "CodeReview", 1);

    private static RoundAuthors B() => AttributionCases.TwoRepositoriesOneAgent().AuthorsOf("B", "CodeReview", 1);

    [Fact]
    public void ItHasOneWroteHereRowWithItsWriteCallsAndNoTokens()
    {
        foreach (var authors in new[] { A(), B() })
        {
            authors.Rows.Should().ContainSingle().Which.Should().Match<AuthorRow>(row =>
                row.Agent.Id == "w2" && row.Association == Association.WroteHere && row.WriteCalls == 1 && row.Turns == 0 && row.Launches == 0
                && row.Model == "unknown" && row.Requested == "unknown" && row.Effort == "unknown");
            authors.Rows.Single().Tokens.Should().Be(new TokenTerms(0, 0, 0, 0));
        }
    }

    [Fact]
    public void ItsMessagesAreCountedOnce_InAmbiguousOrClaimedByPeer()
    {
        A().Ambiguous.Turns.Should().Be(3);
        A().Ambiguous.Tokens.Output.Should().Be(300);
        A().ClaimedByPeer.Turns.Should().Be(0);
        B().ClaimedByPeer.Turns.Should().Be(3);
        B().Ambiguous.Turns.Should().Be(0);
        B().Ambiguous.Tokens.Should().Be(new TokenTerms(0, 0, 0, 0));
    }

    /// <summary>Moved from S2 by G10: A and B each count 1 in <c>Ambiguous.Agents</c> — B from the agent's association, not from an item it owns.</summary>
    [Fact]
    public void BothGatesCountOneAmbiguousAgent()
    {
        A().Ambiguous.Agents.Should().Be(1);
        B().Ambiguous.Agents.Should().Be(1);
        B().ClaimedByPeer.Agents.Should().Be(1);
    }

    [Fact]
    public void ItsLaunchFollowsItsMessages()
    {
        A().Ambiguous.Launches.Should().Be(1);
        B().ClaimedByPeer.Launches.Should().Be(1);
        B().Ambiguous.Launches.Should().Be(0);
    }
}
